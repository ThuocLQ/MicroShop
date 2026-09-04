[CmdletBinding()]
param(
    [ValidateSet("Core", "Full")]
    [string]$Mode = "Core",
    [string]$EnvFile = ".env.local-prod",
    [string]$GatewayBaseUrl = "http://api.localhost:5027",
    [switch]$Build,
    [switch]$SkipSmoke,
    [switch]$SkipSeed,
    [string]$StorefrontPublicOrigin,
    [string]$OperationsPublicOrigin,
    [switch]$RecreateFrontends,
    [switch]$RecreatePaymentService
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -Path $EnvFile)) {
    throw "Missing $EnvFile. Copy .env.example to $EnvFile and replace every CHANGEME value first."
}

function Get-ExactHttpsOrigin {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    try {
        $uri = [Uri]$Value
    }
    catch {
        throw "$Name must be an absolute HTTPS origin."
    }

    if (-not $uri.IsAbsoluteUri -or $uri.Scheme -ne "https" -or
        -not [string]::IsNullOrWhiteSpace($uri.Query) -or
        -not [string]::IsNullOrWhiteSpace($uri.Fragment) -or
        -not [string]::IsNullOrWhiteSpace($uri.UserInfo) -or
        ($uri.AbsolutePath -ne "/")) {
        throw "$Name must be an absolute HTTPS origin without a path, query, fragment, or user info."
    }

    return $uri.GetLeftPart([UriPartial]::Authority)
}

$hasStorefrontPublicOrigin = -not [string]::IsNullOrWhiteSpace($StorefrontPublicOrigin)
$hasOperationsPublicOrigin = -not [string]::IsNullOrWhiteSpace($OperationsPublicOrigin)
if ($hasOperationsPublicOrigin -and -not $hasStorefrontPublicOrigin) {
    throw "OperationsPublicOrigin requires StorefrontPublicOrigin."
}

if (($RecreateFrontends -or $RecreatePaymentService) -and -not $hasStorefrontPublicOrigin) {
    throw "RecreateFrontends and RecreatePaymentService require an exact HTTPS StorefrontPublicOrigin."
}

if ($hasStorefrontPublicOrigin) {
    $StorefrontPublicOrigin = Get-ExactHttpsOrigin -Value $StorefrontPublicOrigin -Name "StorefrontPublicOrigin"
}

if ($hasOperationsPublicOrigin) {
    $OperationsPublicOrigin = Get-ExactHttpsOrigin -Value $OperationsPublicOrigin -Name "OperationsPublicOrigin"
}

$composeArgs = @(
    "--env-file", $EnvFile,
    "-f", "compose.local-prod.yml",
    "-f", "compose.portfolio.yml"
)

if ($Mode -eq "Full") {
    $composeArgs += @("--profile", "read-model")
}

$scopedEnvironment = @{}
if ($hasStorefrontPublicOrigin) {
    $scopedEnvironment = @{
        MICROSHOP_STOREFRONT_PUBLIC_ORIGIN = $StorefrontPublicOrigin
        MICROSHOP_STOREFRONT_COOKIE_SECURE = "true"
    }

    if ($hasOperationsPublicOrigin) {
        $scopedEnvironment["MICROSHOP_OPERATIONS_PUBLIC_ORIGIN"] = $OperationsPublicOrigin
        $scopedEnvironment["MICROSHOP_OPERATIONS_COOKIE_SECURE"] = "true"
    }
}

$previousEnvironment = @{}
try {
    foreach ($entry in $scopedEnvironment.GetEnumerator()) {
        $previousEnvironment[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, "Process")
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, "Process")
    }

    if ($RecreateFrontends -or $RecreatePaymentService) {
        Write-Host "Applying exact public origin configuration to portfolio services..."
        $frontendServices = @("storefront")
        if ($hasOperationsPublicOrigin) {
            $frontendServices += "operations"
        }

        if ($RecreatePaymentService) {
            $frontendServices += "paymentservice"
        }

        & docker compose @composeArgs up -d --no-deps --force-recreate @frontendServices
        if ($LASTEXITCODE -ne 0) {
            throw "docker compose frontend recreation failed with exit code $LASTEXITCODE."
        }

        & docker compose @composeArgs ps @frontendServices
        if ($LASTEXITCODE -ne 0) {
            throw "docker compose ps failed with exit code $LASTEXITCODE."
        }

        return
    }

    $upArgs = @("up", "-d")
    if ($Build) {
        $upArgs += "--build"
    }

    Write-Host "Starting MicroShop portfolio $Mode profile..."
    & docker compose @composeArgs @upArgs
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose up failed with exit code $LASTEXITCODE."
    }

    & docker compose @composeArgs ps
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose ps failed with exit code $LASTEXITCODE."
    }

    if (-not $SkipSeed) {
        & (Join-Path $PSScriptRoot "portfolio-seed.ps1") -GatewayBaseUrl $GatewayBaseUrl -EnvFile $EnvFile
        if ($LASTEXITCODE -ne 0) {
            throw "portfolio seed failed with exit code $LASTEXITCODE."
        }
    }

    if (-not $SkipSmoke) {
        $smokeScript = Join-Path $PSScriptRoot "local-prod-smoke.ps1"

        if ($Mode -eq "Core") {
            & $smokeScript -GatewayBaseUrl $GatewayBaseUrl -EnvFile $EnvFile -VerifyPortfolioFrontends -SkipReadModel
        }
        else {
            & $smokeScript -GatewayBaseUrl $GatewayBaseUrl -EnvFile $EnvFile -VerifyPortfolioFrontends
        }

        if ($LASTEXITCODE -ne 0) {
            throw "portfolio smoke failed with exit code $LASTEXITCODE."
        }
    }
}
finally {
    foreach ($entry in $previousEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, "Process")
    }
}
