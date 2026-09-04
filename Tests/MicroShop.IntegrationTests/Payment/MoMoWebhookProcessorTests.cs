using System.Data;
using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PaymentService.Application.Abstractions;
using PaymentService.Application.Payments.Webhooks;
using PaymentService.Domain.Payments;
using DomainPayment = PaymentService.Domain.Payments.Payment;
using PaymentService.Infrastructure.Providers;

namespace MicroShop.IntegrationTests.Payment;

public sealed class MoMoWebhookProcessorTests
{
    [Fact]
    public async Task SuccessfulIpn_AuthorizesThenCapturesThePayment()
    {
        var payment = CreatePayment();
        var payments = new PaymentRepository(payment);
        var webhooks = new WebhookRepository(payment);
        using var services = CreateServices(webhooks);
        var processor = new MoMoWebhookProcessor(
            Microsoft.Extensions.Options.Options.Create(PaymentOptions),
            payments,
            webhooks,
            services.GetRequiredService<ISender>());
        var body = CreateIpn(payment.ProviderSessionId!, resultCode: 0, message: "Successful.");

        var result = await processor.ProcessAsync(
            new HeaderDictionary(),
            body,
            TestContext.Current.CancellationToken);

        Assert.NotNull(result.Payment);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal([PaymentStatus.Authorized, PaymentStatus.Captured], webhooks.AppliedStatuses);
        Assert.Equal("Verified", webhooks.LastSignatureStatus);
    }

    [Fact]
    public async Task InvalidSignature_IsRecordedWithoutApplyingPayment()
    {
        var payment = CreatePayment();
        var payments = new PaymentRepository(payment);
        var webhooks = new WebhookRepository(payment);
        using var services = CreateServices(webhooks);
        var processor = new MoMoWebhookProcessor(
            Microsoft.Extensions.Options.Options.Create(PaymentOptions),
            payments,
            webhooks,
            services.GetRequiredService<ISender>());
        var body = CreateIpn(payment.ProviderSessionId!, resultCode: 0, message: "Successful.", signature: "invalid");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => processor.ProcessAsync(
            new HeaderDictionary(),
            body,
            TestContext.Current.CancellationToken));

        Assert.Equal(PaymentStatus.PendingAuthorization, payment.Status);
        Assert.Equal(1, webhooks.RejectedCount);
        Assert.Empty(webhooks.AppliedStatuses);
    }

    private static readonly MoMoOptions PaymentOptions = new()
    {
        PartnerCode = "MOMO",
        AccessKey = "access-key",
        SecretKey = "secret-key"
    };

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
            125_000m,
            "VND",
            PaymentStatus.PendingAuthorization,
            DateTime.UtcNow,
            provider: "MoMo",
            providerSessionId: $"ms-{id:N}",
            paymentActionIdempotencyKey: "momo-ipn-test",
            paymentActionRequestHash: new string('a', 64),
            paymentActionExpiresAtUtc: DateTime.UtcNow.AddMinutes(30),
            providerCheckoutUrl: "https://test-payment.momo.vn/pay/test");
    }

    private static string CreateIpn(string orderId, int resultCode, string message, string? signature = null)
    {
        const long amount = 125000;
        const long transactionId = 1900012345;
        const long responseTime = 1720000000000;
        const string orderInfo = "MicroShop payment test";
        const string orderType = "momo_wallet";
        const string payType = "qr";
        const string requestId = "momo-request-001";
        const string extraData = "";
        var canonical = $"accessKey={PaymentOptions.AccessKey}&amount={amount}&extraData={extraData}&message={message}&orderId={orderId}&orderInfo={orderInfo}&orderType={orderType}&partnerCode={PaymentOptions.PartnerCode}&payType={payType}&requestId={requestId}&responseTime={responseTime}&resultCode={resultCode}&transId={transactionId}";
        signature ??= Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(PaymentOptions.SecretKey),
            Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();

        return $$"""{"partnerCode":"MOMO","orderId":"{{orderId}}","requestId":"{{requestId}}","amount":{{amount}},"orderInfo":"{{orderInfo}}","orderType":"{{orderType}}","transId":{{transactionId}},"resultCode":{{resultCode}},"message":"{{message}}","payType":"{{payType}}","responseTime":{{responseTime}},"extraData":"","signature":"{{signature}}"}""";
    }

    private sealed class PaymentRepository(DomainPayment payment) : IPaymentRepository
    {
        public Task<DomainPayment> CreateAsync(DomainPayment value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DomainPayment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<DomainPayment?>(payment.Id == id ? payment : null);
        public Task<DomainPayment?> GetByIdAsync(Guid id, IDbTransaction transaction, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DomainPayment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DomainPayment?> GetByProviderSessionIdAsync(string provider, string providerSessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<DomainPayment?>(string.Equals(provider, "MoMo", StringComparison.OrdinalIgnoreCase) && payment.ProviderSessionId == providerSessionId ? payment : null);
        public Task<DomainPayment?> GetByCustomerAndActionIdempotencyKeyAsync(Guid customerId, string idempotencyKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DomainPayment>> GetRecentAsync(int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(DomainPayment value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(DomainPayment value, IDbTransaction transaction, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class WebhookRepository(DomainPayment payment) : IPaymentWebhookRepository
    {
        private readonly HashSet<string> _eventIds = [];
        public List<PaymentStatus> AppliedStatuses { get; } = [];
        public int RejectedCount { get; private set; }
        public string? LastSignatureStatus { get; private set; }

        public Task<PaymentWebhookApplyResult> ApplyAsync(string providerEventId, Guid paymentId, string providerTransactionId, PaymentStatus status, string? failureReason, string payloadHash, string signatureStatus, DateTime receivedAtUtc, CancellationToken cancellationToken = default)
        {
            var duplicate = !_eventIds.Add(providerEventId);
            if (!duplicate)
            {
                if (status == PaymentStatus.Authorized) payment.MarkAuthorized(providerTransactionId, receivedAtUtc);
                if (status == PaymentStatus.Captured) payment.MarkCaptured(providerTransactionId, receivedAtUtc);
                AppliedStatuses.Add(status);
            }
            LastSignatureStatus = signatureStatus;
            return Task.FromResult(new PaymentWebhookApplyResult(payment, duplicate, providerEventId, status));
        }

        public Task RecordRejectedAsync(string providerEventId, Guid paymentId, string providerTransactionId, string eventType, string payloadHash, string signatureStatus, string error, DateTime receivedAtUtc, CancellationToken cancellationToken = default)
        {
            RejectedCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class NoopPaymentMetrics : IPaymentMetrics
    {
        public void RecordWebhookRequest(string outcome) { }
    }
}
