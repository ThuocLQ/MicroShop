using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PaymentService.Application.Payments.Providers;
using PaymentService.Infrastructure.Providers;

namespace MicroShop.IntegrationTests.Payment;

public sealed class MoMoPaymentProviderTests
{
    [Fact]
    public async Task CreateActionAsync_SendsSignedVndRequestAndReturnsHostedCheckout()
    {
        var paymentId = Guid.Parse("a79f5981-6adc-48dc-bad6-9d76f0c5bb77");
        var orderId = Guid.Parse("4daaee03-f19f-4b9d-9d6d-96e085513e14");
        var sessionId = $"ms-{paymentId:N}";
        var handler = new RecordingHandler($$"""
            {"resultCode":0,"orderId":"{{sessionId}}","requestId":"{{sessionId}}","payUrl":"https://test-payment.momo.vn/v2/gateway/pay/test"}
            """);
        var options = new MoMoOptions
        {
            PartnerCode = "MOMO",
            AccessKey = "access-key",
            SecretKey = "secret-key",
            RedirectUrl = "https://shop.example.com/payment/return",
            IpnUrl = "https://api.example.com/webhooks/momo",
            ActionExpiryMinutes = 30
        };
        var provider = new MoMoPaymentProvider(
            new MoMoApiClient(new StaticHttpClientFactory(handler)),
            Options.Create(options));

        var action = await provider.CreateActionAsync(
            new PaymentProviderActionRequest(paymentId, orderId, 125000m, "VND"),
            TestContext.Current.CancellationToken);

        Assert.Equal("MoMo", action.Provider);
        Assert.Equal(sessionId, action.SessionId);
        Assert.Equal("https://test-payment.momo.vn/v2/gateway/pay/test", action.CheckoutUrl);
        Assert.Equal("/v2/gateway/api/create", handler.RequestUri!.AbsolutePath);

        using var request = JsonDocument.Parse(handler.RequestBody!);
        var root = request.RootElement;
        Assert.Equal(125000, root.GetProperty("amount").GetInt64());
        Assert.Equal(sessionId, root.GetProperty("orderId").GetString());
        Assert.Equal($"MicroShop payment {orderId:N}", root.GetProperty("orderInfo").GetString());

        var redirectUrl = $"https://shop.example.com/payment/return?paymentId={paymentId:D}&provider=momo";
        var canonical = $"accessKey=access-key&amount=125000&extraData=&ipnUrl=https://api.example.com/webhooks/momo&orderId={sessionId}&orderInfo=MicroShop payment {orderId:N}&partnerCode=MOMO&redirectUrl={redirectUrl}&requestId={sessionId}&requestType=payWithMethod";
        var expectedSignature = Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("secret-key"),
            Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        Assert.Equal(expectedSignature, root.GetProperty("signature").GetString());
    }

    [Fact]
    public async Task CreateActionAsync_RejectsFractionalVndBeforeCallingProvider()
    {
        var handler = new RecordingHandler("{}");
        var provider = new MoMoPaymentProvider(
            new MoMoApiClient(new StaticHttpClientFactory(handler)),
            Options.Create(new MoMoOptions
            {
                PartnerCode = "MOMO",
                AccessKey = "access-key",
                SecretKey = "secret-key",
                RedirectUrl = "https://shop.example.com/payment/return",
                IpnUrl = "https://api.example.com/webhooks/momo"
            }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CreateActionAsync(
            new PaymentProviderActionRequest(Guid.NewGuid(), Guid.NewGuid(), 12.5m, "VND"),
            TestContext.Current.CancellationToken));

        Assert.Null(handler.RequestUri);
    }

    private sealed class StaticHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://test-payment.momo.vn/")
        };
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
