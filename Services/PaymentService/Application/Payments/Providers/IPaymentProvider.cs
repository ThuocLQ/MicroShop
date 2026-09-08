using PaymentService.Domain.Payments;

namespace PaymentService.Application.Payments.Providers;

public interface IPaymentProvider
{
    string Name { get; }
    IReadOnlyList<string> SupportedCurrencies { get; }

    Task<PaymentProviderAction> CreateActionAsync(
        PaymentProviderActionRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentProviderWebhook?> RequestCaptureAsync(
        Payment payment,
        CancellationToken cancellationToken = default);

    Task<PaymentProviderWebhook?> RequestVoidAsync(
        Payment payment,
        CancellationToken cancellationToken = default);

    Task<PaymentProviderWebhook?> RequestRefundAsync(
        Payment payment,
        CancellationToken cancellationToken = default);
}

public interface IPaymentProviderResolver
{
    IPaymentProvider Resolve(string? providerName);
    IReadOnlyList<PaymentProviderDescriptor> GetAvailableProviders();
}

public interface ISandboxPaymentProvider : IPaymentProvider
{
    Task<PaymentProviderWebhook> CompleteAsync(
        Payment payment,
        SandboxPaymentOutcome outcome,
        CancellationToken cancellationToken = default);
}

public sealed record PaymentProviderAction(
    string Provider,
    string SessionId,
    string? CheckoutUrl,
    DateTime ExpiresAtUtc);

public sealed record PaymentProviderDescriptor(
    string Name,
    bool IsSandbox,
    bool RequiresRedirect,
    IReadOnlyList<string> SupportedCurrencies);

public sealed record PaymentProviderActionRequest(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string Currency);

public sealed record PaymentProviderWebhook(string RawBody, string Signature);

public enum SandboxPaymentOutcome
{
    Approve,
    Decline
}
