[CmdletBinding()]
param(
    [string]$GatewayBaseUrl = "http://api.localhost:5027"
)

$ErrorActionPreference = "Stop"
$GatewayBaseUrl = $GatewayBaseUrl.TrimEnd("/")

function Invoke-GatewayRequest {
    param([Parameter(Mandatory = $true)][string]$Path)

    Invoke-WebRequest -Uri "$GatewayBaseUrl$Path" -UseBasicParsing -TimeoutSec 10 -SkipHttpErrorCheck
}

function Get-ResponseContent {
    param([Parameter(Mandatory = $true)]$Response)

    if ($Response.Content -is [byte[]]) {
        return [Text.Encoding]::UTF8.GetString($Response.Content)
    }

    return [string]$Response.Content
}
function Assert-ProblemResponse {
    param(
        [string]$Path,
        [int]$ExpectedStatus,
        [string]$ExpectedType
    )

    $response = Invoke-GatewayRequest -Path $Path
    if ([int]$response.StatusCode -ne $ExpectedStatus) {
        throw "$Path returned HTTP $($response.StatusCode), expected $ExpectedStatus."
    }

    $problem = (Get-ResponseContent -Response $response) | ConvertFrom-Json
    if ($problem.type -ne $ExpectedType) {
        throw "$Path returned unexpected problem type '$($problem.type)'."
    }

    $correlationHeaders = @($response.Headers.Keys | Where-Object { $_ -ieq "X-Correlation-ID" })
    if ($correlationHeaders.Count -eq 0) {
        throw "$Path did not return X-Correlation-ID."
    }

    Write-Host "[ok] $Path returns protected ProblemDetails"
}

$ordersResponse = Invoke-GatewayRequest -Path "/orders"
if ([int]$ordersResponse.StatusCode -ne 401) {
    throw "Anonymous caller accessed /orders with HTTP $($ordersResponse.StatusCode), expected HTTP 401."
}

$ordersProblem = (Get-ResponseContent -Response $ordersResponse) | ConvertFrom-Json
if ($ordersProblem.type -ne "https://microshop.dev/problems/unauthorized") {
    throw "/orders did not return the expected unauthorized ProblemDetails type."
}

Write-Host "[ok] /orders requires authentication and returns ProblemDetails"

Assert-ProblemResponse -Path "/debug/order-summaries" -ExpectedStatus 404 -ExpectedType "https://microshop.dev/problems/debug-route-not-available"
Assert-ProblemResponse -Path "/orders/00000000-0000-0000-0000-000000000000/payment-result" -ExpectedStatus 404 -ExpectedType "https://microshop.dev/problems/internal-route-not-available"
Assert-ProblemResponse -Path "/__p1-route-does-not-exist" -ExpectedStatus 404 -ExpectedType "https://microshop.dev/problems/http-404"

Write-Host "Gateway route security smoke passed."