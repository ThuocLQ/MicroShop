using MediatR;
using PaymentService.Application.Abstractions;
using PaymentService.Domain.Payments;

namespace PaymentService.Application.Payments.Webhooks;

public sealed class PaymentWebhookHandler : IRequestHandler<PaymentWebhookCommand, PaymentWebhookApplyResult>
{
    private readonly IPaymentWebhookRepository _repository;
    private readonly IPaymentMetrics _metrics;
    private readonly IPaymentOperationalActionRepository? _operationalActions;

    public PaymentWebhookHandler(
        IPaymentWebhookRepository repository,
        IPaymentMetrics metrics,
        IPaymentOperationalActionRepository? operationalActions = null)
    {
        _repository = repository;
        _metrics = metrics;
        _operationalActions = operationalActions;
    }

    public async Task<PaymentWebhookApplyResult> Handle(PaymentWebhookCommand request, CancellationToken cancellationToken)
    {
        var normalizedStatus = request.Status.Trim().ToUpperInvariant();
        var status = normalizedStatus switch
        {
            "AUTHORIZED" => PaymentStatus.Authorized,
            "CAPTURED" or "SUCCEEDED" => PaymentStatus.Captured,
            "VOIDED" => PaymentStatus.Voided,
            "REFUNDED" => PaymentStatus.Refunded,
            "FAILED" => PaymentStatus.Failed,
            _ => throw new InvalidOperationException($"Unsupported payment webhook status '{request.Status}'.")
        };

        var providerEventId = string.IsNullOrWhiteSpace(request.ProviderEventId)
            ? $"{request.PaymentId:N}:{request.ProviderTransactionId.Trim()}:{normalizedStatus}"
            : request.ProviderEventId.Trim();

        if (!string.Equals(request.SignatureStatus, "Verified", StringComparison.OrdinalIgnoreCase))
        {
            await _repository.RecordRejectedAsync(
                providerEventId,
                request.PaymentId,
                request.ProviderTransactionId,
                normalizedStatus,
                request.PayloadHash,
                request.SignatureStatus,
                "Webhook signature verification failed.",
                DateTime.UtcNow,
                cancellationToken);

            _metrics.RecordWebhookRequest("rejected");
            throw new UnauthorizedAccessException("Webhook signature verification failed.");
        }

        var result = request.IsProviderAutoCapture && status == PaymentStatus.Captured
            ? await _repository.ApplyVerifiedAutoCaptureAsync(
                providerEventId,
                request.PaymentId,
                request.ProviderTransactionId,
                request.FailureReason,
                request.PayloadHash,
                request.SignatureStatus,
                DateTime.UtcNow,
                cancellationToken)
            : await _repository.ApplyAsync(
                providerEventId,
                request.PaymentId,
                request.ProviderTransactionId,
                status,
                request.FailureReason,
                request.PayloadHash,
                request.SignatureStatus,
                DateTime.UtcNow,
                cancellationToken);

        if (result.Payment is not null &&
            _operationalActions is not null &&
            TryGetCompletedActionType(status, out var actionType))
        {
            await _operationalActions.CompleteLatestPendingAsync(
                result.Payment.Id,
                actionType,
                DateTime.UtcNow,
                cancellationToken);
        }

        _metrics.RecordWebhookRequest(
            result.IsDuplicate
                ? "duplicate"
                : result.Payment is null
                    ? "not_found"
                    : "accepted");

        return result;
    }

    private static bool TryGetCompletedActionType(PaymentStatus status, out string actionType)
    {
        actionType = status switch
        {
            PaymentStatus.Captured => "Capture",
            PaymentStatus.Voided => "Void",
            PaymentStatus.Refunded => "Refund",
            _ => string.Empty
        };

        return actionType.Length > 0;
    }
}