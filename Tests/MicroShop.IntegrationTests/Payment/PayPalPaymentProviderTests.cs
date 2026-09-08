using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using PaymentService.Application.Payments.Providers;
using PaymentService.Infrastructure.Providers;

namespace MicroShop.IntegrationTests.Payment;

public sealed class PayPalPaymentProviderTests
{
    [Fact]
    public async Task CreateActionAsync_SendsServerDerivedCheckoutOrderAndReturnsApproveUrl()
    {
        var paymentId = Guid.Parse("a79f5981-6adc-48dc-bad6-9d76f0c5bb77");
        var orderId = Guid.Parse("4daaee03-f19f-4b9d-9d6d-96e085513e14");
        var handler = new PayPalRecordingHandler();
        var options = CreateOptions();
        var provider = new PayPalPaymentProvider(
            new PayPalApiClient(new StaticHttpClientFactory(handler), Options.Create(options)),
            Options.Create(options));

        var action = await provider.CreateActionAsync(
            new PaymentProviderActionRequest(paymentId, orderId, 42.50m, "USD"),
            TestContext.Current.CancellationToken);

        Assert.Equal("PayPal", action.Provider);
        Assert.Equal("paypal-order-001", action.SessionId);
        Assert.Equal("https://www.sandbox.paypal.com/checkoutnow?token=paypal-order-001", action.CheckoutUrl);
        Assert.Equal(["/v1/oauth2/token", "/v2/checkout/orders"], handler.RequestPaths);
        Assert.Equal($"ms-{paymentId:N}"[..25], handler.OrderRequestId);

        using var request = JsonDocument.Parse(handler.OrderRequestBody!);
        var unit = request.RootElement.GetProperty("purchase_units")[0];
        Assert.Equal(paymentId.ToString("D"), unit.GetProperty("custom_id").GetString());
        Assert.Equal($"microshop-{orderId:N}", unit.GetProperty("invoice_id").GetString());
        Assert.Equal("USD", unit.GetProperty("amount").GetProperty("currency_code").GetString());
        Assert.Equal("42.50", unit.GetProperty("amount").GetProperty("value").GetString());
        Assert.Contains($"paymentId={paymentId:D}", request.RootElement.GetProperty("application_context").GetProperty("return_url").GetString());
    }

    [Fact]
    public async Task CreateActionAsync_RejectsVndBeforeAnyProviderCall()
    {
        var handler = new PayPalRecordingHandler();
        var options = CreateOptions();
        var provider = new PayPalPaymentProvider(
            new PayPalApiClient(new StaticHttpClientFactory(handler), Options.Create(options)),
            Options.Create(options));

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CreateActionAsync(
            new PaymentProviderActionRequest(Guid.NewGuid(), Guid.NewGuid(), 125_000m, "VND"),
            TestContext.Current.CancellationToken));

        Assert.Empty(handler.RequestPaths);
    }

    [Fact]
    public async Task VerifyWebhookSignatureAsync_ForwardsRequiredHeadersAndWebhookId()
    {
        var handler = new PayPalRecordingHandler();
        var options = CreateOptions();
        var client = new PayPalApiClient(new StaticHttpClientFactory(handler), Options.Create(options));
        var headers = new HeaderDictionary
        {
            ["PAYPAL-TRANSMISSION-ID"] = "transmission-001",
            ["PAYPAL-TRANSMISSION-TIME"] = "2026-09-06T00:00:00Z",
            ["PAYPAL-CERT-URL"] = "https://api-m.paypal.com/certs/cert.pem",
            ["PAYPAL-AUTH-ALGO"] = "SHA256withRSA",
            ["PAYPAL-TRANSMISSION-SIG"] = "signature"
        };

        var verified = await client.VerifyWebhookSignatureAsync(headers, "{\"id\":\"event-001\"}", TestContext.Current.CancellationToken);

        Assert.True(verified);
        Assert.Equal(["/v1/oauth2/token", "/v1/notifications/verify-webhook-signature"], handler.RequestPaths);
        using var request = JsonDocument.Parse(handler.WebhookVerificationBody!);
        Assert.Equal(options.WebhookId, request.RootElement.GetProperty("webhook_id").GetString());
        Assert.Equal("event-001", request.RootElement.GetProperty("webhook_event").GetProperty("id").GetString());
    }

    private static PayPalOptions CreateOptions() => new()
    {
        ClientId = "client-id",
        ClientSecret = "client-secret",
        WebhookId = "webhook-id",
        ReturnUrl = "https://shop.example.com/payment/return",
        CancelUrl = "https://shop.example.com/payment/cancel",
        SupportedCurrencies = ["USD"]
    };

    private sealed class StaticHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://api-m.sandbox.paypal.com/")
        };
    }

    private sealed class PayPalRecordingHandler : HttpMessageHandler
    {
        public List<string> RequestPaths { get; } = [];
        public string? OrderRequestId { get; private set; }
        public string? OrderRequestBody { get; private set; }
        public string? WebhookVerificationBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestPaths.Add(request.RequestUri!.AbsolutePath);
            if (request.RequestUri.AbsolutePath == "/v1/oauth2/token")
            {
                Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
                return JsonResponse("{\"access_token\":\"access-token\",\"expires_in\":3600}");
            }

            if (request.RequestUri.AbsolutePath == "/v2/checkout/orders")
            {
                OrderRequestId = request.Headers.GetValues("PayPal-Request-Id").Single();
                OrderRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                return JsonResponse("{\"id\":\"paypal-order-001\",\"links\":[{\"rel\":\"approve\",\"href\":\"https://www.sandbox.paypal.com/checkoutnow?token=paypal-order-001\"}]}");
            }

            if (request.RequestUri.AbsolutePath == "/v1/notifications/verify-webhook-signature")
            {
                WebhookVerificationBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                return JsonResponse("{\"verification_status\":\"SUCCESS\"}");
            }

            throw new InvalidOperationException($"Unexpected PayPal path {request.RequestUri.AbsolutePath}.");
        }

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
