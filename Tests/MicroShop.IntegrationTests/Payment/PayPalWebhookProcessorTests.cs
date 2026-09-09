using System.Data;
using System.Net;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PaymentService.Application.Abstractions;
using PaymentService.Application.Payments.Webhooks;
using PaymentService.Domain.Payments;
using PaymentService.Infrastructure.Providers;
using DomainPayment = PaymentService.Domain.Payments.Payment;

namespace MicroShop.IntegrationTests.Payment;

public sealed class PayPalWebhookProcessorTests
{
    [Fact]
    public async Task VerifiedAuthorization_UpdatesPaymentAndDuplicateEventHasNoSecondSideEffect()
    {
        var payment = CreatePayment();
        var payments = new PaymentRepository(payment);
        var webhooks = new WebhookRepository(payment);
        using var services = CreateServices(webhooks);
        var processor = CreateProcessor(payments, services);
        var body = CreateWebhookBody("event-001", "PAYMENT.AUTHORIZATION.CREATED", payment.Id, "authorization-001");

        var first = await processor.ProcessAsync(CreateHeaders(), body, TestContext.Current.CancellationToken);
        var duplicate = await processor.ProcessAsync(CreateHeaders(), body, TestContext.Current.CancellationToken);

        Assert.NotNull(first.Payment);
        Assert.False(first.IsDuplicate);
        Assert.True(duplicate.IsDuplicate);
        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal([PaymentStatus.Authorized], webhooks.AppliedStatuses);
        Assert.Equal("authorization-001", payment.ProviderTransactionId);
    }

    [Fact]
    public async Task InvalidSignature_RejectsWebhookWithoutChangingPayment()
    {
        var payment = CreatePayment();
        var payments = new PaymentRepository(payment);
        var webhooks = new WebhookRepository(payment);
        using var services = CreateServices(webhooks);
        var processor = CreateProcessor(payments, services, verificationStatus: "FAILURE");
        var body = CreateWebhookBody("event-invalid", "PAYMENT.AUTHORIZATION.CREATED", payment.Id, "authorization-001");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => processor.ProcessAsync(
            CreateHeaders(), body, TestContext.Current.CancellationToken));

        Assert.Equal(PaymentStatus.PendingAuthorization, payment.Status);
        Assert.Empty(webhooks.AppliedStatuses);
    }

    private static PayPalWebhookProcessor CreateProcessor(
        PaymentRepository payments,
        ServiceProvider services,
        string verificationStatus = "SUCCESS")
    {
        var options = new PayPalOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            WebhookId = "webhook-id",
            ReturnUrl = "https://shop.example.com/payment/return",
            CancelUrl = "https://shop.example.com/payment/cancel"
        };

        return new PayPalWebhookProcessor(
            new PayPalApiClient(new StaticHttpClientFactory(new VerificationHandler(verificationStatus)), Options.Create(options)),
            payments,
            services.GetRequiredService<ISender>());
    }

    private static ServiceProvider CreateServices(WebhookRepository repository) => new ServiceCollection()
        .AddLogging()
        .AddSingleton<IPaymentWebhookRepository>(repository)
        .AddSingleton<IPaymentMetrics, NoopPaymentMetrics>()
        .AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(PaymentWebhookHandler).Assembly))
        .BuildServiceProvider();

    private static DomainPayment CreatePayment()
    {
        var id = Guid.NewGuid();
        return new DomainPayment(
            id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            42.50m,
            "USD",
            PaymentStatus.PendingAuthorization,
            DateTime.UtcNow,
            provider: "PayPal",
            providerSessionId: "paypal-order-001",
            paymentActionIdempotencyKey: "paypal-webhook-test",
            paymentActionRequestHash: new string('a', 64),
            paymentActionExpiresAtUtc: DateTime.UtcNow.AddMinutes(30),
            providerCheckoutUrl: "https://www.sandbox.paypal.com/checkoutnow?token=paypal-order-001");
    }

    private static HeaderDictionary CreateHeaders() => new()
    {
        ["PAYPAL-TRANSMISSION-ID"] = "transmission-001",
        ["PAYPAL-TRANSMISSION-TIME"] = "2026-09-06T00:00:00Z",
        ["PAYPAL-CERT-URL"] = "https://api-m.paypal.com/certs/cert.pem",
        ["PAYPAL-AUTH-ALGO"] = "SHA256withRSA",
        ["PAYPAL-TRANSMISSION-SIG"] = "signature"
    };

    private static string CreateWebhookBody(string eventId, string eventType, Guid paymentId, string transactionId) =>
        JsonSerializer.Serialize(new
        {
            id = eventId,
            event_type = eventType,
            resource = new { id = transactionId, custom_id = paymentId.ToString("D") }
        });

    private sealed class StaticHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://api-m.sandbox.paypal.com/")
        };
    }

    private sealed class VerificationHandler(string verificationStatus) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/v1/oauth2/token")
            {
                return JsonResponse("{\"access_token\":\"access-token\",\"expires_in\":3600}");
            }

            if (request.RequestUri.AbsolutePath == "/v1/notifications/verify-webhook-signature")
            {
                _ = await request.Content!.ReadAsStringAsync(cancellationToken);
                return JsonResponse($$"""{"verification_status":"{{verificationStatus}}"}""");
            }

            throw new InvalidOperationException($"Unexpected PayPal path {request.RequestUri.AbsolutePath}.");
        }

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class PaymentRepository(DomainPayment payment) : IPaymentRepository
    {
        public Task<DomainPayment> CreateAsync(DomainPayment value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DomainPayment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<DomainPayment?>(payment.Id == id ? payment : null);
        public Task<DomainPayment?> GetByIdAsync(Guid id, IDbTransaction transaction, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DomainPayment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DomainPayment?> GetByProviderSessionIdAsync(string provider, string providerSessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<DomainPayment?>(string.Equals(provider, "PayPal", StringComparison.OrdinalIgnoreCase) && payment.ProviderSessionId == providerSessionId ? payment : null);
        public Task<DomainPayment?> GetByCustomerAndActionIdempotencyKeyAsync(Guid customerId, string idempotencyKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DomainPayment>> GetRecentAsync(int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(DomainPayment value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(DomainPayment value, IDbTransaction transaction, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class WebhookRepository(DomainPayment payment) : IPaymentWebhookRepository
    {
        private readonly HashSet<string> _eventIds = [];
        public List<PaymentStatus> AppliedStatuses { get; } = [];

        public Task<PaymentWebhookApplyResult> ApplyAsync(string providerEventId, Guid paymentId, string providerTransactionId, PaymentStatus status, string? failureReason, string payloadHash, string signatureStatus, DateTime receivedAtUtc, CancellationToken cancellationToken = default)
        {
            var duplicate = !_eventIds.Add(providerEventId);
            if (!duplicate)
            {
                if (status == PaymentStatus.Authorized) payment.MarkAuthorized(providerTransactionId, receivedAtUtc);
                if (status == PaymentStatus.Captured) payment.MarkCaptured(providerTransactionId, receivedAtUtc);
                AppliedStatuses.Add(status);
            }

            return Task.FromResult(new PaymentWebhookApplyResult(payment, duplicate, providerEventId, status));
        }

        public Task RecordRejectedAsync(string providerEventId, Guid paymentId, string providerTransactionId, string eventType, string payloadHash, string signatureStatus, string error, DateTime receivedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoopPaymentMetrics : IPaymentMetrics
    {
        public void RecordWebhookRequest(string outcome) { }
    }
}
