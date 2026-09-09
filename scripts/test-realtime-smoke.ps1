[CmdletBinding()]
param(
    [string]$BaseUrl = "http://localhost:5027"
)

$ErrorActionPreference = "Stop"
$base = $BaseUrl.TrimEnd("/")
$originHeaders = @{ Origin = $base }

$anonymous = Invoke-WebRequest -Uri "$base/realtime/customer-events/negotiate?negotiateVersion=1" -Method Post -SkipHttpErrorCheck
if ($anonymous.StatusCode -ne 401) {
    throw "Anonymous realtime negotiation must return 401, received $($anonymous.StatusCode)."
}

$username = "realtime-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
$password = "RealtimePass!234"
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession

$registration = Invoke-WebRequest -Uri "$base/api/session" -Method Put -WebSession $session -Headers $originHeaders -ContentType "application/json" -Body (@{
    userName = $username
    email = "$username@example.test"
    password = $password
} | ConvertTo-Json -Compress)
if ($registration.StatusCode -ne 201) {
    throw "Test customer registration failed with $($registration.StatusCode)."
}

$login = Invoke-WebRequest -Uri "$base/api/session" -Method Post -WebSession $session -Headers $originHeaders -ContentType "application/json" -Body (@{
    userName = $username
    password = $password
} | ConvertTo-Json -Compress)
if ($login.StatusCode -ne 200) {
    throw "Test customer login failed with $($login.StatusCode)."
}

$negotiate = Invoke-WebRequest -Uri "$base/realtime/customer-events/negotiate?negotiateVersion=1" -Method Post -WebSession $session
$payload = $negotiate.Content | ConvertFrom-Json
if ($negotiate.StatusCode -ne 200 -or [string]::IsNullOrWhiteSpace($payload.connectionId)) {
    throw "Authenticated realtime negotiation did not return a valid connection id."
}

$cookie = $session.Cookies.GetCookies($base)["microshop_access_token"]
if ($null -eq $cookie) {
    throw "The BFF session cookie was not created."
}

$storefrontNodeModules = Join-Path $PSScriptRoot "..\Frontend\apps\storefront\node_modules\@microsoft\signalr"
if (-not (Test-Path $storefrontNodeModules)) {
    throw "SignalR client dependency is missing. Run pnpm install in Frontend/apps/storefront."
}

$scriptPath = Join-Path $env:TEMP "microshop-realtime-smoke-$([Guid]::NewGuid().ToString('N')).cjs"
try {
    [IO.File]::WriteAllText($scriptPath, @"
const signalR = require($(ConvertTo-Json $storefrontNodeModules));
const baseUrl = $(ConvertTo-Json $base);
const cookie = $(ConvertTo-Json ("$($cookie.Name)=$($cookie.Value)"));

(async () => {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(baseUrl + "/realtime/customer-events", { headers: { Cookie: cookie } })
    .configureLogging(signalR.LogLevel.Warning)
    .build();
  await connection.start();
  if (connection.state !== signalR.HubConnectionState.Connected) throw new Error("Unexpected state: " + connection.state);
  await connection.stop();
})().catch(error => { console.error(error); process.exitCode = 1; });
"@, [Text.UTF8Encoding]::new($false))

    & node $scriptPath
    if ($LASTEXITCODE -ne 0) {
        throw "SignalR JavaScript client smoke failed."
    }
}
finally {
    Remove-Item -LiteralPath $scriptPath -Force -ErrorAction SilentlyContinue
}

Write-Host "Realtime smoke passed: anonymous access was denied, and an authenticated BFF session opened a SignalR connection."
