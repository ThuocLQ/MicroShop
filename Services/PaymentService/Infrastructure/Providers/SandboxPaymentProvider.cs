using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PaymentService.Application.Payments.Providers;
using PaymentService.Application.Payments.Webhooks;
using PaymentService.Domain.Payments;

namespace PaymentService.Infrastructure.Providers;

// Development/portfolio simulator only. It models a provider session and signed callbacks;
// it does not accept, collect, transmit, or store any payment instrument data.
public sealed class SandboxPaymentProvider : ISandboxPaymentProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PaymentProviderOptions _providerOptions;
    private readonly PaymentWebhookOptions _webhookOptions;

    public SandboxPaymentProvider(
        IOptions<PaymentProviderOptions> providerOptions,
        IOptions<PaymentWebhookOptions> webhookOptions)
    {
        _providerOptions = providerOptions.Value;
        _webhookOptions = webhookOptions.Value;
    }

    public string Name => "Sandbox";
    public IReadOnlyList<string> SupportedCurrencies => [PaymentProviderPolicy.AnyCurrency];

    public Task<PaymentProviderAction> CreateActionAsync(
        PaymentProviderActionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.PaymentId == Guid.Empty || request.OrderId == Guid.Empty || request.Amount <= 0 || string.IsNullOrWhiteSpace(request.Currency))
        {
            throw new ArgumentException("A valid payment action requires payment, order, amount, and currency.", nameof(request));
        }

        return Task.FromResult(new PaymentProviderAction(
            Name,
            $"sandbox-session-{request.PaymentId:N}",
            null,
            DateTime.UtcNow.AddMinutes(_providerOptions.SandboxActionExpiryMinutes)));
    }

    public Task<PaymentProviderWebhook> CompleteAsync(
        Payment payment,
        SandboxPaymentOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        EnsureSandboxAction(payment);
        return Task.FromResult(CreateWebhook(
            payment,
            outcome == SandboxPaymentOutcome.Approve ? "AUTHORIZED" : "FAILED",
            outcome == SandboxPaymentOutcome.Approve ? null : "Sandbox provider declined the payment action.",
            outcome == SandboxPaymentOutcome.Approve ? "authorize" : "decline"));
    }

    public Task<PaymentProviderWebhook?> RequestCaptureAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        EnsureSandboxAction(payment);
        return Task.FromResult<PaymentProviderWebhook?>(CreateWebhook(payment, "CAPTURED", null, "capture"));
    }

    public Task<PaymentProviderWebhook?> RequestVoidAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        EnsureSandboxAction(payment);
        return Task.FromResult<PaymentProviderWebhook?>(CreateWebhook(payment, "VOIDED", null, "void"));
    }

    public Task<PaymentProviderWebhook?> RequestRefundAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        EnsureSandboxAction(payment);
        return Task.FromResult<PaymentProviderWebhook?>(CreateWebhook(payment, "REFUNDED", null, "refund"));
    }

    private PaymentProviderWebhook CreateWebhook(Payment payment, string status, string? failureReason, string operation)
    {
        var payload = new PaymentWebhookPayload(
            payment.Id,
            $"sandbox:{payment.ProviderSessionId}:{operation}",
            $"sandbox-transaction-{payment.Id:N}",
            status,
            failureReason);
        var rawBody = JsonSerializer.Serialize(payload, JsonOptions);
        var signature = Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(_webhookOptions.SharedSecret),
            Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();

        return new PaymentProviderWebhook(rawBody, $"sha256={signature}");
    }

    private static void EnsureSandboxAction(Payment payment)
    {
        if (!string.Equals(payment.Provider, "Sandbox", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(payment.ProviderSessionId))
        {
            throw new InvalidOperationException("The payment does not have a sandbox provider action.");
        }
    }
}
