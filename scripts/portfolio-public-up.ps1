[CmdletBinding()]
param(
    [ValidateSet("Core", "Full")]
    [string]$Mode = "Core",
    [string]$EnvFile = ".env.local-prod",
    [string]$GatewayBaseUrl = "http://api.localhost:5027",
    [switch]$Build,
    [switch]$SkipPortfolioStart,
    [switch]$IncludeOperations,
    [int]$TunnelReadyTimeoutSeconds = 60
)

$ErrorActionPreference = "Stop"

$statePath = Join-Path $env:TEMP "microshop-portfolio-tunnels.json"
$logDirectory = Join-Path $env:TEMP "microshop-portfolio-tunnels"
$portfolioNetworkName = "microshop-local-prod_microshop-network"
$cloudflared = Get-Command cloudflared -ErrorAction SilentlyContinue
$docker = Get-Command docker -ErrorAction SilentlyContinue

if ($null -eq $cloudflared -and $null -eq $docker) {
    throw "Neither cloudflared nor Docker was found. Install Cloudflare.cloudflared or start Docker Desktop, then run this script again."
}

$runtime = if ($null -ne $cloudflared) { "Native" } else { "Docker" }

if (Test-Path -LiteralPath $statePath) {
    $existingState = Get-Content -Raw $statePath | ConvertFrom-Json
    $running = @($existingState.Tunnels | Where-Object {
        if ($_.Runtime -eq "Docker") {
            $containerName = $_.ContainerName
            $container = & docker ps --filter "name=^/$containerName$" --format "{{.Names}}" 2>$null
            return $container -eq $containerName
        }

        return Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue
    })

    if ($running.Count -gt 0) {
        $existingState.Tunnels | Format-Table Name, Url, Runtime, ProcessId, ContainerName -AutoSize
        Write-Host "Existing MicroShop portfolio tunnels are still running. Use scripts/portfolio-public-down.ps1 before creating new URLs."
        return
    }

    Remove-Item -LiteralPath $statePath -Force
}

if (-not $SkipPortfolioStart) {
    $startParameters = @{
        Mode = $Mode
        EnvFile = $EnvFile
        GatewayBaseUrl = $GatewayBaseUrl
    }

    if ($Build) {
        $startParameters.Build = $true
    }

    & (Join-Path $PSScriptRoot "portfolio-up.ps1") @startParameters
    if ($LASTEXITCODE -ne 0) {
        throw "Portfolio stack startup failed with exit code $LASTEXITCODE."
    }
}

New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null

function Assert-OriginReady {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url,

        [Parameter(Mandatory = $true)]
        [string]$HostHeader
    )

    $deadline = (Get-Date).AddSeconds($TunnelReadyTimeoutSeconds)
    $lastError = $null

    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Url -Headers @{ Host = $HostHeader } -UseBasicParsing -TimeoutSec 10
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 400) {
                return
            }

            $lastError = "HTTP $($response.StatusCode)"
        }
        catch {
            $lastError = $_.Exception.Message
        }

        Start-Sleep -Seconds 2
    }

    throw "Portfolio origin '$Url' did not become ready within $TunnelReadyTimeoutSeconds seconds. Last error: $lastError"
}

function Start-QuickTunnel {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$OriginHostHeader
    )

    $originUrl = if ($runtime -eq "Docker") { "http://reverse-proxy:8080" } else { "http://localhost:5027" }
    Assert-OriginReady -Url "http://localhost:5027" -HostHeader $OriginHostHeader

    $stdoutPath = Join-Path $logDirectory "$Name.stdout.log"
    $stderrPath = Join-Path $logDirectory "$Name.stderr.log"
    Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue

    $containerName = $null
    if ($runtime -eq "Docker") {
        $containerName = "microshop-portfolio-$Name-tunnel"
        $existingContainer = & docker ps -a --filter "name=^/$containerName$" --format "{{.Names}}" 2>$null
        if ($existingContainer -eq $containerName) {
            throw "Docker container '$containerName' already exists. Run scripts/portfolio-public-down.ps1 first."
        }

        $process = Start-Process -FilePath $docker.Source `
            -ArgumentList @("run", "--rm", "--name", $containerName, "--network", $portfolioNetworkName, "cloudflare/cloudflared:latest", "tunnel", "--no-autoupdate", "--url", $originUrl, "--http-host-header", $OriginHostHeader) `
            -RedirectStandardOutput $stdoutPath `
            -RedirectStandardError $stderrPath `
            -WindowStyle Hidden `
            -PassThru
    }
    else {
        $process = Start-Process -FilePath $cloudflared.Source `
            -ArgumentList @("tunnel", "--no-autoupdate", "--url", $originUrl, "--http-host-header", $OriginHostHeader) `
            -RedirectStandardOutput $stdoutPath `
            -RedirectStandardError $stderrPath `
            -WindowStyle Hidden `
            -PassThru
    }

    $deadline = (Get-Date).AddSeconds($TunnelReadyTimeoutSeconds)
    $url = $null

    while ((Get-Date) -lt $deadline) {
        if ($process.HasExited) {
            $logs = @($stdoutPath, $stderrPath) | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object { Get-Content -Raw $_ }
            throw "Cloudflare quick tunnel '$Name' exited before receiving a public URL. $($logs -join [Environment]::NewLine)"
        }

        $logs = @($stdoutPath, $stderrPath) | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object { Get-Content -Raw $_ }
        $match = [regex]::Match(($logs -join [Environment]::NewLine), 'https://[-a-z0-9]+\.trycloudflare\.com', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

        if ($match.Success) {
            $url = $match.Value
            break
        }

        Start-Sleep -Seconds 2
    }

    if ([string]::IsNullOrWhiteSpace($url)) {
        if ($runtime -eq "Docker") {
            & docker stop $containerName | Out-Null
        }
        else {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }

        throw "Timed out waiting for Cloudflare quick tunnel '$Name'. Review $stderrPath."
    }

    return [PSCustomObject]@{
        Name = $Name
        Url = $url
        Runtime = $runtime
        ProcessId = $process.Id
        ContainerName = $containerName
        OriginHostHeader = $OriginHostHeader
        StartedAtUtc = [DateTime]::UtcNow.ToString("O")
        StdOutPath = $stdoutPath
        StdErrPath = $stderrPath
    }
}

function Stop-QuickTunnel {
    param([Parameter(Mandatory = $true)]$Tunnel)

    if ($Tunnel.Runtime -eq "Docker") {
        & docker stop $Tunnel.ContainerName 2>$null | Out-Null
        return
    }

    Stop-Process -Id $Tunnel.ProcessId -Force -ErrorAction SilentlyContinue
}

function Get-HttpStatus {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url,

        [Parameter(Mandatory = $true)]
        [hashtable]$Headers
    )

    try {
        return (Invoke-WebRequest -Uri $Url -Method Post -Headers $Headers -ContentType "application/json" -Body '{"userName":"origin-check","password":"not-a-valid-password"}' -UseBasicParsing -TimeoutSec 15).StatusCode
    }
    catch {
        if ($null -ne $_.Exception.Response) {
            return [int]$_.Exception.Response.StatusCode
        }

        throw
    }
}

function Assert-BffOriginPolicy {
    param([Parameter(Mandatory = $true)]$Tunnel)

    Assert-OriginReady -Url "http://localhost:5027" -HostHeader $Tunnel.OriginHostHeader

    $requestUrl = "http://localhost:5027/api/session"
    $allowedStatus = Get-HttpStatus -Url $requestUrl -Headers @{ Host = $Tunnel.OriginHostHeader; Origin = $Tunnel.Url }
    if ($allowedStatus -ne 401) {
        throw "Expected the configured public origin for '$($Tunnel.Name)' to reach authentication (HTTP 401 for invalid credentials), but received HTTP $allowedStatus."
    }

    $blockedStatus = Get-HttpStatus -Url $requestUrl -Headers @{ Host = $Tunnel.OriginHostHeader; Origin = "https://untrusted.example" }
    if ($blockedStatus -ne 403) {
        throw "Expected an untrusted origin for '$($Tunnel.Name)' to be rejected with HTTP 403, but received HTTP $blockedStatus."
    }
}

$tunnels = @()
try {
    $storefrontTunnel = Start-QuickTunnel -Name "storefront" -OriginHostHeader "localhost"
    $tunnels += $storefrontTunnel

    $frontendParameters = @{
        Mode = $Mode
        EnvFile = $EnvFile
        GatewayBaseUrl = $GatewayBaseUrl
        StorefrontPublicOrigin = $storefrontTunnel.Url
        RecreateFrontends = $true
        RecreatePaymentService = $true
        SkipSmoke = $true
        SkipSeed = $true
    }

    if ($IncludeOperations) {
        $operationsTunnel = Start-QuickTunnel -Name "operations" -OriginHostHeader "operations.localhost"
        $tunnels += $operationsTunnel
        $frontendParameters.OperationsPublicOrigin = $operationsTunnel.Url
    }

    & (Join-Path $PSScriptRoot "portfolio-up.ps1") @frontendParameters
    if ($LASTEXITCODE -ne 0) {
        throw "Portfolio frontend origin configuration failed with exit code $LASTEXITCODE."
    }

    foreach ($tunnel in $tunnels) {
        Assert-BffOriginPolicy -Tunnel $tunnel
    }
}
catch {
    foreach ($tunnel in $tunnels) {
        Stop-QuickTunnel -Tunnel $tunnel
    }

    throw
}

@{
    Mode = $Mode
    Runtime = $runtime
    StartedAtUtc = [DateTime]::UtcNow.ToString("O")
    Tunnels = $tunnels
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $statePath -NoNewline

Write-Host "MicroShop portfolio is public through temporary Cloudflare Quick Tunnels:"
$tunnels | Format-Table Name, Url, Runtime, ProcessId -AutoSize
Write-Host "These URLs remain available only while this machine, Docker portfolio stack, and tunnel processes stay running."
