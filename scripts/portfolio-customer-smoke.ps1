[CmdletBinding()]
param(
    [string]$StorefrontBaseUrl = "http://localhost:5027",
    [string]$GatewayBaseUrl = "http://api.localhost:5027",
    [string]$EnvFile = ".env.local-prod",
    [ValidateSet("Cancellation", "Fulfillment")]
    [string]$Scenario = "Fulfillment",
    [ValidateSet("Sandbox", "CashOnDelivery")]
    [string]$PaymentProvider = "CashOnDelivery",
    [string]$UserName,
    [string]$Email,
    [string]$Password = "PortfolioSmoke!2026"
)

$ErrorActionPreference = "Stop"
if ($PaymentProvider -eq "CashOnDelivery" -and $Scenario -eq "Cancellation") {
    throw "The COD smoke validates the fulfillment path. Test cancellation before payment selection with a separate order."
}
$StorefrontBaseUrl = $StorefrontBaseUrl.TrimEnd("/")
$storefrontUri = [Uri]$StorefrontBaseUrl
if (-not $storefrontUri.IsAbsoluteUri -or $storefrontUri.GetLeftPart([UriPartial]::Authority) -ne $StorefrontBaseUrl) {
    throw "StorefrontBaseUrl must be an absolute origin without a path, query, or fragment."
}

if ([string]::IsNullOrWhiteSpace($UserName)) {
    $UserName = "portfolio-smoke-" + [Guid]::NewGuid().ToString("N").Substring(0, 10)
}
if ([string]::IsNullOrWhiteSpace($Email)) {
    $Email = "$UserName@example.test"
}

$headers = @{ Origin = $StorefrontBaseUrl; Accept = "application/json" }
$credentials = @{ userName = $UserName; email = $Email; password = $Password } | ConvertTo-Json

function Get-EnvFileValue {
    param([Parameter(Mandatory = $true)][string]$Key)

    if (-not (Test-Path -Path $EnvFile)) {
        throw "Missing $EnvFile."
    }

    foreach ($line in Get-Content -Path $EnvFile) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith("#")) {
            continue
        }

        $separatorIndex = $trimmed.IndexOf("=")
        if ($separatorIndex -gt 0 -and $trimmed.Substring(0, $separatorIndex).Trim() -eq $Key) {
            return $trimmed.Substring($separatorIndex + 1).Trim()
        }
    }

    return $null
}

function Assert-Status {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Actual,

        [Parameter(Mandatory = $true)]
        [int]$Expected,

        [Parameter(Mandatory = $true)]
        [string]$Operation
    )

    if ($Actual -ne $Expected) {
        throw "$Operation returned HTTP $Actual; expected HTTP $Expected."
    }
}
function Assert-RequestStatus {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Uri,

        [Parameter(Mandatory = $true)]
        [Microsoft.PowerShell.Commands.WebRequestSession]$WebSession,

        [Parameter(Mandatory = $true)]
        [int]$Expected,

        [Parameter(Mandatory = $true)]
        [string]$Operation
    )

    try {
        $response = Invoke-WebRequest -Uri $Uri -Method Get -WebSession $WebSession -UseBasicParsing
        Assert-Status -Actual $response.StatusCode -Expected $Expected -Operation $Operation
    }
    catch {
        $httpResponse = $_.Exception.Response
        if ($null -eq $httpResponse) { throw }
        Assert-Status -Actual ([int]$httpResponse.StatusCode) -Expected $Expected -Operation $Operation
    }
}

Write-Host "Running Storefront customer journey smoke against $StorefrontBaseUrl"

$registration = Invoke-WebRequest -Uri "$StorefrontBaseUrl/api/session" -Method Put -Headers $headers -ContentType "application/json" -Body $credentials -UseBasicParsing
Assert-Status -Actual $registration.StatusCode -Expected 201 -Operation "Customer registration"
Write-Host "[ok] customer registration"

$login = Invoke-WebRequest -Uri "$StorefrontBaseUrl/api/session" -Method Post -Headers $headers -ContentType "application/json" -Body $credentials -SessionVariable storefrontSession -UseBasicParsing
Assert-Status -Actual $login.StatusCode -Expected 200 -Operation "Customer sign-in"
$customer = $login.Content | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($customer.user.userId)) {
    throw "Customer sign-in did not return a user id."
}
Write-Host "[ok] customer sign-in"

$addressInput = @{ label = "Portfolio smoke"; recipientName = "Portfolio Smoke"; line1 = "100 Snapshot Lane"; line2 = $null; city = "Bangkok"; countryCode = "TH"; postalCode = "10110"; makeDefault = $false }
$addressHeaders = @{ Origin = $StorefrontBaseUrl; Accept = "application/json"; "Idempotency-Key" = [Guid]::NewGuid().ToString() }
$address = Invoke-RestMethod -Uri "$StorefrontBaseUrl/api/addresses" -Method Post -Headers $addressHeaders -ContentType "application/json" -Body ($addressInput | ConvertTo-Json) -WebSession $storefrontSession
if ([string]::IsNullOrWhiteSpace($address.id)) {
    throw "Customer address creation did not return an id."
}

$defaultAddress = Invoke-WebRequest -Uri "$StorefrontBaseUrl/api/addresses/$($address.id)/default" -Method Put -Headers $headers -WebSession $storefrontSession -UseBasicParsing
Assert-Status -Actual $defaultAddress.StatusCode -Expected 204 -Operation "Customer address defaulting"
$addresses = Invoke-RestMethod -Uri "$StorefrontBaseUrl/api/addresses" -Method Get -WebSession $storefrontSession
if ($null -eq @($addresses | Where-Object { $_.id -eq $address.id -and $_.isDefault -and -not $_.isArchived })[0]) {
    throw "Created address was not returned as the active default address."
}
Write-Host "[ok] customer address creation and defaulting"

$products = Invoke-RestMethod -Uri "$StorefrontBaseUrl/api/catalog/products" -Method Get
$product = @($products | Where-Object { $_.stockQuantity -gt 0 -and $_.price -gt 0 } | Select-Object -First 1)[0]
if ($null -eq $product) {
    throw "No positive-priced in-stock product was available for customer checkout."
}
Write-Host "[ok] catalog product $($product.name)"

$cart = Invoke-RestMethod -Uri "$StorefrontBaseUrl/api/cart/$($customer.user.userId)/items" -Method Post -Headers $headers -ContentType "application/json" -Body (@{ productId = $product.id; quantity = 1 } | ConvertTo-Json) -WebSession $storefrontSession
if ([string]::IsNullOrWhiteSpace($cart.basketId) -or @($cart.items).Count -eq 0) {
    throw "Add-to-cart did not return a populated basket."
}
Write-Host "[ok] add item to cart"

$quote = Invoke-RestMethod -Uri "$StorefrontBaseUrl/api/checkout/quote" -Method Post -Headers $headers -ContentType "application/json" -Body (@{ basketId = $cart.basketId; basketVersion = $cart.version; shippingAddressId = $address.id } | ConvertTo-Json) -WebSession $storefrontSession
if (-not $quote.canCheckout -or [string]::IsNullOrWhiteSpace($quote.quoteToken) -or $quote.totalAmount -le 0) {
    throw "Checkout quote was not eligible to create an order."
}
if ($quote.finalRevalidationRequired -ne $true) {
    throw "Checkout quote did not require final revalidation."
}
Write-Host "[ok] checkout quote with current pricing and availability"

$order = Invoke-RestMethod -Uri "$StorefrontBaseUrl/api/checkout" -Method Post -Headers $headers -ContentType "application/json" -Body (@{ basketId = $cart.basketId; basketVersion = $cart.version; shippingAddressId = $address.id; idempotencyKey = [Guid]::NewGuid().ToString(); quoteToken = $quote.quoteToken } | ConvertTo-Json) -WebSession $storefrontSession
if ([string]::IsNullOrWhiteSpace($order.id) -or $order.status -ne "PendingPayment") {
    throw "Checkout did not create a PendingPayment order."
}
Write-Host "[ok] checkout with shipping address"

$orders = Invoke-RestMethod -Uri "$StorefrontBaseUrl/api/orders" -Method Get -WebSession $storefrontSession
$persistedOrder = @($orders | Where-Object { $_.id -eq $order.id })[0]
if ($null -eq $persistedOrder) {
    throw "Created order was not visible in the customer order list."
}
if ($null -eq $persistedOrder.shippingAddress -or $persistedOrder.shippingAddress.addressId -ne $address.id -or $persistedOrder.shippingAddress.line1 -ne $addressInput.line1 -or $persistedOrder.shippingAddress.recipientName -ne $addressInput.recipientName) {
    throw "Created order did not contain the expected shipping address snapshot."
}

$changedAddressInput = @{ label = "Portfolio smoke changed"; recipientName = "Portfolio Smoke Changed"; line1 = "200 Changed Lane"; line2 = $null; city = "Bangkok"; countryCode = "TH"; postalCode = "10110"; makeDefault = $true }
$updatedAddress = Invoke-WebRequest -Uri "$StorefrontBaseUrl/api/addresses/$($address.id)" -Method Patch -Headers $headers -ContentType "application/json" -Body ($changedAddressInput | ConvertTo-Json) -WebSession $storefrontSession -UseBasicParsing
Assert-Status -Actual $updatedAddress.StatusCode -Expected 200 -Operation "Customer address update"
$ordersAfterAddressUpdate = Invoke-RestMethod -Uri "$StorefrontBaseUrl/api/orders" -Method Get -WebSession $storefrontSession
$snapshotOrder = @($ordersAfterAddressUpdate | Where-Object { $_.id -eq $order.id })[0]
if ($null -eq $snapshotOrder -or $null -eq $snapshotOrder.shippingAddress -or $snapshotOrder.shippingAddress.addressId -ne $address.id -or $snapshotOrder.shippingAddress.line1 -ne $addressInput.line1 -or $snapshotOrder.shippingAddress.recipientName -ne $addressInput.recipientName -or $snapshotOrder.shippingAddress.line1 -eq $changedAddressInput.line1) {
    throw "Order address snapshot changed after the customer address was updated."
}
Write-Host "[ok] customer order history and immutable address snapshot"

$otherUserName = "portfolio-isolation-" + [Guid]::NewGuid().ToString("N").Substring(0, 10)
$otherCredentials = @{ userName = $otherUserName; email = "$otherUserName@example.test"; password = $Password } | ConvertTo-Json
$otherRegistration = Invoke-WebRequest -Uri "$StorefrontBaseUrl/api/session" -Method Put -Headers $headers -ContentType "application/json" -Body $otherCredentials -UseBasicParsing
Assert-Status -Actual $otherRegistration.StatusCode -Expected 201 -Operation "Second customer registration"
$otherLogin = Invoke-WebRequest -Uri "$StorefrontBaseUrl/api/session" -Method Post -Headers $headers -ContentType "application/json" -Body $otherCredentials -SessionVariable otherStorefrontSession -UseBasicParsing
Assert-Status -Actual $otherLogin.StatusCode -Expected 200 -Operation "Second customer sign-in"
Assert-RequestStatus -Uri "$StorefrontBaseUrl/api/orders/$($order.id)" -WebSession $otherStorefrontSession -Expected 404 -Operation "Cross-account order lookup"
Assert-RequestStatus -Uri "$StorefrontBaseUrl/api/addresses/$($address.id)" -WebSession $otherStorefrontSession -Expected 404 -Operation "Cross-account address lookup"
Write-Host "[ok] customer data isolation through Storefront BFF"

# PowerShell keeps separate cookie jars while exercising the second account. Refresh
# the primary BFF session so subsequent customer assertions use its own cookie jar.
$customerRelogin = Invoke-WebRequest -Uri "$StorefrontBaseUrl/api/session" -Method Post -Headers $headers -ContentType "application/json" -Body $credentials -SessionVariable storefrontSession -UseBasicParsing
Assert-Status -Actual $customerRelogin.StatusCode -Expected 200 -Operation "Customer sign-in refresh"

$paymentHeaders = @{ Origin = $StorefrontBaseUrl; Accept = "application/json"; "Idempotency-Key" = [Guid]::NewGuid().ToString() }
$paymentAction = Invoke-RestMethod -Uri "$StorefrontBaseUrl/api/payments" -Method Post -Headers $paymentHeaders -ContentType "application/json" -Body (@{ orderId = $order.id; provider = $PaymentProvider } | ConvertTo-Json) -WebSession $storefrontSession
if ($null -eq $paymentAction.payment -or [string]::IsNullOrWhiteSpace($paymentAction.payment.id)) {
    throw "Payment initiation did not return a payment action."
}
if ($null -eq $paymentAction.action -or [string]::IsNullOrWhiteSpace($paymentAction.action.expiresAtUtc)) {
    throw "Portfolio payment initiation did not return a durable payment action."
}
if ($PaymentProvider -eq "CashOnDelivery") {
    if ($paymentAction.payment.status -ne "AwaitingCollection" -or -not $paymentAction.action.cashOnDelivery) {
        throw "COD initiation did not return an AwaitingCollection cash-on-delivery payment."
    }
    Write-Host "[ok] cash-on-delivery payment initiation"
}
else {
    if ($paymentAction.payment.status -ne "PendingAuthorization" -or -not $paymentAction.action.sandboxCompletionAvailable) {
        throw "Sandbox initiation did not return a PendingAuthorization sandbox action."
    }
    Write-Host "[ok] sandbox payment initiation"
}
$initialPaymentStatus = Invoke-RestMethod -Uri "$StorefrontBaseUrl/api/payments/orders/$($order.id)" -Method Get -WebSession $storefrontSession
if ($initialPaymentStatus.id -ne $paymentAction.payment.id -or $initialPaymentStatus.orderId -ne $order.id -or
    ($PaymentProvider -eq "CashOnDelivery" -and $initialPaymentStatus.status -ne "AwaitingCollection") -or
    ($PaymentProvider -eq "Sandbox" -and $initialPaymentStatus.status -ne "PendingAuthorization")) {
    throw "Persisted payment status was not available for the created order."
}
Write-Host "[ok] persisted payment status by order"
$finalOrder = $null
$projectionStatus = $null
$completedPaymentStatus = $null

if ($Scenario -eq "Cancellation") {
    $cancelledOrder = Invoke-RestMethod -Uri "$StorefrontBaseUrl/api/orders/$($order.id)/cancel" -Method Post -Headers $headers -ContentType "application/json" -Body (@{ reason = "Portfolio smoke cancellation before fulfillment." } | ConvertTo-Json) -WebSession $storefrontSession
    if ($cancelledOrder.id -ne $order.id -or $cancelledOrder.status -ne "Cancelled") {
        throw "Customer cancellation did not return the cancelled order."
    }
    $ordersAfterCancellation = Invoke-RestMethod -Uri "$StorefrontBaseUrl/api/orders" -Method Get -WebSession $storefrontSession
    $persistedCancelledOrder = @($ordersAfterCancellation | Where-Object { $_.id -eq $order.id })[0]
    if ($null -eq $persistedCancelledOrder -or $persistedCancelledOrder.status -ne "Cancelled") {
        throw "Customer cancellation was not persisted in the customer order list."
    }
    $finalOrder = $persistedCancelledOrder
    Write-Host "[ok] pre-fulfillment order cancellation"
}
else {
    if ($PaymentProvider -eq "Sandbox") {
        $completion = Invoke-WebRequest -Uri "$StorefrontBaseUrl/api/payments/$($paymentAction.payment.id)/sandbox-completion" -Method Post -Headers $headers -ContentType "application/json" -Body (@{ outcome = "Approve" } | ConvertTo-Json) -WebSession $storefrontSession -UseBasicParsing
        Assert-Status -Actual $completion.StatusCode -Expected 202 -Operation "Sandbox payment completion"

        for ($attempt = 1; $attempt -le 30; $attempt++) {
            Start-Sleep -Seconds 1
            $currentOrders = Invoke-RestMethod -Uri "$StorefrontBaseUrl/api/orders" -Method Get -WebSession $storefrontSession
            $candidate = @($currentOrders | Where-Object { $_.id -eq $order.id })[0]
            if ($null -ne $candidate -and $candidate.status -eq "Paid") {
                $finalOrder = $candidate
                break
            }
        }
        if ($null -eq $finalOrder) {
            throw "Order did not become Paid after sandbox payment completion."
        }

        for ($attempt = 1; $attempt -le 30; $attempt++) {
            $candidatePayment = Invoke-RestMethod -Uri "$StorefrontBaseUrl/api/payments/orders/$($order.id)" -Method Get -WebSession $storefrontSession
            if ($candidatePayment.status -eq "Captured") {
                $completedPaymentStatus = $candidatePayment
                break
            }
            Start-Sleep -Seconds 1
        }
        if ($null -eq $completedPaymentStatus) {
            throw "Payment did not become Captured after sandbox payment completion."
        }
        Write-Host "[ok] sandbox payment captured and order paid"
    }
    else {
        for ($attempt = 1; $attempt -le 30; $attempt++) {
            Start-Sleep -Seconds 1
            $currentOrders = Invoke-RestMethod -Uri "$StorefrontBaseUrl/api/orders" -Method Get -WebSession $storefrontSession
            $candidate = @($currentOrders | Where-Object { $_.id -eq $order.id })[0]
            if ($null -ne $candidate -and $candidate.status -eq "Confirmed") {
                $finalOrder = $candidate
                break
            }
        }
        if ($null -eq $finalOrder) {
            throw "COD order did not become Confirmed after inventory commit."
        }
        Write-Host "[ok] cash-on-delivery order confirmed after inventory commit"
    }
    $adminUserName = Get-EnvFileValue "MICROSHOP_BOOTSTRAP_ADMIN_USERNAME"
    $adminPassword = Get-EnvFileValue "MICROSHOP_BOOTSTRAP_ADMIN_PASSWORD"
    if ([string]::IsNullOrWhiteSpace($adminUserName) -or [string]::IsNullOrWhiteSpace($adminPassword)) {
        throw "Fulfillment smoke requires bootstrap admin credentials in $EnvFile."
    }
    $adminLogin = Invoke-RestMethod -Uri "$GatewayBaseUrl/auth/login" -Method Post -ContentType "application/json" -Body (@{ userName = $adminUserName; password = $adminPassword } | ConvertTo-Json) -TimeoutSec 15
    if ([string]::IsNullOrWhiteSpace($adminLogin.accessToken)) {
        throw "Administrator sign-in did not return an access token."
    }
    $adminHeaders = @{ Authorization = "Bearer $($adminLogin.accessToken)"; Accept = "application/json" }
    $adminPayments = Invoke-RestMethod -Uri "$GatewayBaseUrl/payments/admin?limit=20" -Method Get -Headers $adminHeaders -TimeoutSec 15
    $adminPayment = @($adminPayments | Where-Object { $_.id -eq $paymentAction.payment.id })[0]
    if ($null -eq $adminPayment -or $adminPayment.PSObject.Properties.Name -contains "providerCheckoutUrl") {
        throw "Payment operations list did not return the expected redacted payment contract."
    }
    $adminPaymentDetail = Invoke-RestMethod -Uri "$GatewayBaseUrl/payments/admin/$($paymentAction.payment.id)" -Method Get -Headers $adminHeaders -TimeoutSec 15
    if ($adminPaymentDetail.id -ne $paymentAction.payment.id -or $adminPaymentDetail.PSObject.Properties.Name -contains "providerCheckoutUrl") {
        throw "Payment operations detail did not return the expected redacted payment contract."
    }
    Write-Host "[ok] payment operations redacted read contract"
    $shipment = Invoke-RestMethod -Uri "$GatewayBaseUrl/orders/admin/$($order.id)/shipment" -Method Post -Headers $adminHeaders -TimeoutSec 15
    if ($shipment.status -ne "ReadyToShip") { throw "Shipment was not created in ReadyToShip." }
    $shipment = Invoke-RestMethod -Uri "$GatewayBaseUrl/orders/admin/$($order.id)/shipment/dispatch" -Method Post -Headers $adminHeaders -ContentType "application/json" -Body (@{ carrier = "DHL"; trackingNumber = "PORTFOLIO-$($order.id.ToString().Substring(0,8))" } | ConvertTo-Json) -TimeoutSec 15
    if ($shipment.status -ne "Shipped") { throw "Shipment was not dispatched." }
    $shipment = Invoke-RestMethod -Uri "$GatewayBaseUrl/orders/admin/$($order.id)/shipment/deliver" -Method Post -Headers $adminHeaders -TimeoutSec 15
    if ($shipment.status -ne "Delivered") { throw "Shipment was not delivered." }
    $shipmentDetail = Invoke-RestMethod -Uri "$GatewayBaseUrl/orders/admin/$($order.id)/shipment" -Method Get -Headers $adminHeaders -TimeoutSec 15
    if (@($shipmentDetail.history.currentStatus) -notcontains "ReadyToShip" -or @($shipmentDetail.history.currentStatus) -notcontains "Shipped" -or @($shipmentDetail.history.currentStatus) -notcontains "Delivered") { throw "Shipment audit history is incomplete." }
    $customerRelogin = Invoke-WebRequest -Uri "$StorefrontBaseUrl/api/session" -Method Post -Headers $headers -ContentType "application/json" -Body $credentials -SessionVariable storefrontSession -UseBasicParsing
    Assert-Status -Actual $customerRelogin.StatusCode -Expected 200 -Operation "Customer sign-in refresh before shipment tracking"
    $customerShipment = Invoke-RestMethod -Uri "$StorefrontBaseUrl/api/orders/$($order.id)/shipment" -Method Get -WebSession $storefrontSession
    if ($customerShipment.shipment.status -ne "Delivered" -or @($customerShipment.history.currentStatus) -notcontains "Delivered") {
        throw "Customer shipment tracking did not show delivered state."
    }
    Write-Host "[ok] customer shipment tracking"
    $customerOrdersAfterFulfillment = Invoke-RestMethod -Uri "$StorefrontBaseUrl/api/orders" -Method Get -WebSession $storefrontSession
    $finalOrder = @($customerOrdersAfterFulfillment | Where-Object { $_.id -eq $order.id })[0]
    if ($null -eq $finalOrder -or $finalOrder.status -ne "Delivered") {
        throw "Delivered order was not visible in the customer order detail."
    }
    Write-Host "[ok] administrator shipment lifecycle and audit history"

    for ($attempt = 1; $attempt -le 30; $attempt++) {
        Start-Sleep -Seconds 1
        try {
            $projection = Invoke-RestMethod -Uri "$GatewayBaseUrl/order-summaries/$($order.id)" -Method Get -Headers $adminHeaders -TimeoutSec 15
            if ($projection.status -eq "Delivered") {
                $projectionStatus = $projection.status
                break
            }
        }
        catch {
            # The projection is eventually consistent; retry until the timeout.
        }
    }
    if ($projectionStatus -ne "Delivered") {
        throw "Order projection did not reach Delivered after the fulfillment transitions."
    }
    Write-Host "[ok] Kafka to Mongo order projection"
}
[PSCustomObject]@{
    Customer = $customer.user.userName
    CustomerEmail = $Email
    Product = $product.name
    OrderId = $order.id
    OrderStatus = $finalOrder.status
    PaymentId = $paymentAction.payment.id
    InitialPaymentStatus = $initialPaymentStatus.status
    FinalPaymentStatus = if ($null -ne $completedPaymentStatus) { $completedPaymentStatus.status } else { $initialPaymentStatus.status }
    ShippingAddressId = $address.id
    ShippingAddressSnapshotImmutable = $true
    CustomerDataIsolationVerified = $true
    QuoteTotal = $quote.totalAmount
    QuoteFinalRevalidationRequired = $quote.finalRevalidationRequired
    Scenario = $Scenario
    ProjectionStatus = $projectionStatus
} | Format-List

Write-Host "Storefront customer journey smoke passed."