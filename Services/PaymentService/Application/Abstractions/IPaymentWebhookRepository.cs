using PaymentService.Domain.Payments;
using PaymentService.Application.Payments.Webhooks;

namespace PaymentService.Application.Abstractions;

public interface IPaymentWebhookRepository
{
    Task<PaymentWebhookApplyResult> ApplyAsync(
        string providerEventId,
        Guid paymentId,
        string providerTransactionId,
        PaymentStatus status,
        string? failureReason,
        string payloadHash,
        string signatureStatus,
        DateTime receivedAtUtc,
        CancellationToken cancellationToken = default);

    Task<PaymentWebhookApplyResult> ApplyVerifiedAutoCaptureAsync(
        string providerEventId,
        Guid paymentId,
        string providerTransactionId,
        string? failureReason,
        string payloadHash,
        string signatureStatus,
        DateTime receivedAtUtc,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(
            providerEventId,
            paymentId,
            providerTransactionId,
            PaymentStatus.Captured,
            failureReason,
            payloadHash,
            signatureStatus,
            receivedAtUtc,
            cancellationToken);
    Task RecordRejectedAsync(
        string providerEventId,
        Guid paymentId,
        string providerTransactionId,
        string eventType,
        string payloadHash,
        string signatureStatus,
        string error,
        DateTime receivedAtUtc,
        CancellationToken cancellationToken = default);
}
