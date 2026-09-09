[CmdletBinding()]
param(
    [string]$EnvFile = ".env.public-portfolio",
    [switch]$Build
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath ".env.local-prod")) {
    throw "Missing .env.local-prod. Start from .env.example and configure local portfolio secrets first."
}

if (-not (Test-Path -LiteralPath $EnvFile)) {
    throw "Missing $EnvFile. Copy .env.public-portfolio.example and set the exact HTTPS hostname, pinned cloudflared image, and named-tunnel token."
}

$requiredKeys = @(
    "MICROSHOP_STOREFRONT_PUBLIC_ORIGIN",
    "MICROSHOP_PUBLIC_ORIGIN",
    "MICROSHOP_CLOUDFLARED_IMAGE",
    "MICROSHOP_CLOUDFLARE_TUNNEL_TOKEN"
)
$values = @{}
foreach ($line in Get-Content -LiteralPath $EnvFile) {
    if ($line -match '^\s*([^#=\s]+)\s*=\s*(.*)\s*$') {
        $values[$matches[1]] = $matches[2]
    }
}

foreach ($key in $requiredKeys) {
    $value = $values[$key]
    if ([string]::IsNullOrWhiteSpace($value) -or $value -match 'REPLACE|CHANGEME|example\.domain') {
        throw "$key must be set to a real deployment value in $EnvFile."
    }
}

foreach ($key in @("MICROSHOP_STOREFRONT_PUBLIC_ORIGIN", "MICROSHOP_PUBLIC_ORIGIN")) {
    $uri = [Uri]$values[$key]
    if (-not $uri.IsAbsoluteUri -or $uri.Scheme -ne "https" -or $uri.AbsolutePath -ne "/" -or $uri.Query -or $uri.Fragment) {
        throw "$key must be an exact HTTPS origin without a path, query, or fragment."
    }
}

if ($values["MICROSHOP_STOREFRONT_PUBLIC_ORIGIN"] -ne $values["MICROSHOP_PUBLIC_ORIGIN"]) {
    throw "MICROSHOP_STOREFRONT_PUBLIC_ORIGIN and MICROSHOP_PUBLIC_ORIGIN must match for the public Storefront deployment."
}

$composeArgs = @(
    "--env-file", ".env.local-prod",
    "--env-file", $EnvFile,
    "-f", "compose.local-prod.yml",
    "-f", "compose.portfolio.yml",
    "-f", "compose.public-portfolio.yml"
)

& docker compose @composeArgs config --quiet
if ($LASTEXITCODE -ne 0) {
    throw "Public portfolio Compose configuration is invalid."
}

$upArgs = @("up", "-d", "--no-deps", "--force-recreate", "reverse-proxy", "api-gateway", "storefront", "cloudflared")
if ($Build) {
    $upArgs = @("up", "-d", "--build", "--no-deps", "--force-recreate", "reverse-proxy", "api-gateway", "storefront", "cloudflared")
}

& docker compose @composeArgs @upArgs
if ($LASTEXITCODE -ne 0) {
    throw "Public portfolio deployment failed."
}

Write-Host "Named-tunnel portfolio deployment started. Verify the public hostname, login origin checks, and Cloudflare edge rules before sharing it."