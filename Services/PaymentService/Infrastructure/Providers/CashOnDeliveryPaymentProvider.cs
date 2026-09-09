using PaymentService.Application.Payments.Providers;
using PaymentService.Domain.Payments;

namespace PaymentService.Infrastructure.Providers;

public sealed class CashOnDeliveryPaymentProvider : IPaymentProvider
{
    public const string ProviderName = "CashOnDelivery";

    public string Name => ProviderName;
    public IReadOnlyList<string> SupportedCurrencies => [PaymentProviderPolicy.AnyCurrency];

    public Task<PaymentProviderAction> CreateActionAsync(
        PaymentProviderActionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.PaymentId == Guid.Empty ||
            request.OrderId == Guid.Empty ||
            request.Amount <= 0 ||
            string.IsNullOrWhiteSpace(request.Currency))
        {
            throw new ArgumentException(
                "A valid cash-on-delivery action requires payment, order, amount, and currency.",
                nameof(request));
        }

        return Task.FromResult(new PaymentProviderAction(
            Name,
            $"cod-collection-{request.PaymentId:N}",
            null,
            DateTime.UtcNow.AddDays(30),
            PaymentStatus.AwaitingCollection));
    }

    public Task<PaymentProviderWebhook?> RequestCaptureAsync(
        Payment payment,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<PaymentProviderWebhook?>(null);

    public Task<PaymentProviderWebhook?> RequestVoidAsync(
        Payment payment,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<PaymentProviderWebhook?>(null);

    public Task<PaymentProviderWebhook?> RequestRefundAsync(
        Payment payment,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<PaymentProviderWebhook?>(null);
}