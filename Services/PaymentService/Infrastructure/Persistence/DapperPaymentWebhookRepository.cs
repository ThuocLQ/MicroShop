using BuildingBlocks.Contracts.Events.Payments;
using Dapper;
using PaymentService.Application.Abstractions;
using PaymentService.Application.Outbox;
using PaymentService.Application.Payments.Webhooks;
using PaymentService.Domain.Outbox;
using PaymentService.Domain.Payments;

namespace PaymentService.Infrastructure.Persistence;

public sealed class DapperPaymentWebhookRepository : IPaymentWebhookRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public DapperPaymentWebhookRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<PaymentWebhookApplyResult> ApplyAsync(
        string providerEventId,
        Guid paymentId,
        string providerTransactionId,
        PaymentStatus status,
        string? failureReason,
        string payloadHash,
        string signatureStatus,
        DateTime receivedAtUtc,
        CancellationToken cancellationToken = default) =>
        ApplyCoreAsync(
            providerEventId,
            paymentId,
            providerTransactionId,
            status,
            failureReason,
            payloadHash,
            signatureStatus,
            receivedAtUtc,
            allowProviderAutoCapture: false,
            cancellationToken);

    public Task<PaymentWebhookApplyResult> ApplyVerifiedAutoCaptureAsync(
        string providerEventId,
        Guid paymentId,
        string providerTransactionId,
        string? failureReason,
        string payloadHash,
        string signatureStatus,
        DateTime receivedAtUtc,
        CancellationToken cancellationToken = default) =>
        ApplyCoreAsync(
            providerEventId,
            paymentId,
            providerTransactionId,
            PaymentStatus.Captured,
            failureReason,
            payloadHash,
            signatureStatus,
            receivedAtUtc,
            allowProviderAutoCapture: true,
            cancellationToken);

    private async Task<PaymentWebhookApplyResult> ApplyCoreAsync(
        string providerEventId,
        Guid paymentId,
        string providerTransactionId,
        PaymentStatus status,
        string? failureReason,
        string payloadHash,
        string signatureStatus,
        DateTime receivedAtUtc,
        bool allowProviderAutoCapture,
        CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var normalizedProviderEventId = providerEventId.Trim();
        var normalizedProviderTransactionId = providerTransactionId.Trim();

        var inserted = await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO WebhookLogs (
                Id,
                ProviderEventId,
                PaymentId,
                ProviderTransactionId,
                EventType,
                Status,
                Error,
                PayloadHash,
                SignatureStatus,
                ReceivedAtUtc,
                ProcessedAtUtc)
            VALUES (
                @Id,
                @ProviderEventId,
                @PaymentId,
                @ProviderTransactionId,
                @EventType,
                'Processing',
                NULL,
                @PayloadHash,
                @SignatureStatus,
                @ReceivedAtUtc,
                NULL)
            ON CONFLICT (ProviderEventId) DO NOTHING;
            """, new
        {
            Id = Guid.NewGuid(),
            ProviderEventId = normalizedProviderEventId,
            PaymentId = paymentId,
            ProviderTransactionId = normalizedProviderTransactionId,
            EventType = status.ToString(),
            PayloadHash = payloadHash,
            SignatureStatus = signatureStatus,
            ReceivedAtUtc = receivedAtUtc
        }, transaction, cancellationToken: cancellationToken));

        if (inserted == 0)
        {
            var existingWebhook = await GetWebhookLogAsync(normalizedProviderEventId, transaction, cancellationToken)
                ?? throw new InvalidOperationException($"Webhook log '{normalizedProviderEventId}' disappeared after a duplicate conflict.");

            transaction.Commit();

            if (!Matches(existingWebhook, paymentId, normalizedProviderTransactionId, status, payloadHash, signatureStatus))
            {
                await RecordConflictAsync(existingWebhook, paymentId, normalizedProviderTransactionId, status, payloadHash, signatureStatus, cancellationToken);
                throw new PaymentWebhookIntegrityException(normalizedProviderEventId);
            }

            var existingPayment = await GetPaymentAsync(existingWebhook.PaymentId, cancellationToken);
            return new PaymentWebhookApplyResult(existingPayment, true, normalizedProviderEventId, status);
        }

        var payment = await GetPaymentForUpdateAsync(paymentId, transaction, cancellationToken);

        if (payment is null)
        {
            await MarkWebhookFailedAsync(
                normalizedProviderEventId,
                "Payment was not found.",
                DateTime.UtcNow,
                transaction,
                cancellationToken);

            transaction.Commit();
            return new PaymentWebhookApplyResult(null, false, normalizedProviderEventId, status);
        }

        try
        {
            var statusBeforeWebhook = payment.Status;
            ApplyWebhookStatus(
                payment,
                status,
                normalizedProviderTransactionId,
                failureReason,
                receivedAtUtc,
                allowProviderAutoCapture);

            if (payment.Status != statusBeforeWebhook)
            {
                await UpdatePaymentAsync(payment, transaction, cancellationToken);
                await AddOutboxMessageAsync(payment, status, normalizedProviderEventId, transaction, cancellationToken);
            }
            await MarkWebhookProcessedAsync(normalizedProviderEventId, DateTime.UtcNow, transaction, cancellationToken);

            transaction.Commit();
            return new PaymentWebhookApplyResult(payment, false, normalizedProviderEventId, status);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task RecordRejectedAsync(
        string providerEventId,
        Guid paymentId,
        string providerTransactionId,
        string eventType,
        string payloadHash,
        string signatureStatus,
        string error,
        DateTime receivedAtUtc,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO WebhookLogs (
                Id,
                ProviderEventId,
                PaymentId,
                ProviderTransactionId,
                EventType,
                Status,
                Error,
                PayloadHash,
                SignatureStatus,
                ReceivedAtUtc,
                ProcessedAtUtc)
            VALUES (
                @Id,
                @ProviderEventId,
                @PaymentId,
                @ProviderTransactionId,
                @EventType,
                'Rejected',
                @Error,
                @PayloadHash,
                @SignatureStatus,
                @ReceivedAtUtc,
                @ProcessedAtUtc)
            ON CONFLICT (ProviderEventId) DO NOTHING;
            """, new
        {
            Id = Guid.NewGuid(),
            ProviderEventId = providerEventId,
            PaymentId = paymentId,
            ProviderTransactionId = string.IsNullOrWhiteSpace(providerTransactionId) ? "unknown" : providerTransactionId.Trim(),
            EventType = string.IsNullOrWhiteSpace(eventType) ? "Unknown" : eventType.Trim(),
            Error = Truncate(error, 4000),
            PayloadHash = payloadHash,
            SignatureStatus = signatureStatus,
            ReceivedAtUtc = receivedAtUtc,
            ProcessedAtUtc = receivedAtUtc
        }, cancellationToken: cancellationToken));
    }

    private async Task<Payment?> GetPaymentAsync(
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();

        var row = await connection.QuerySingleOrDefaultAsync<PaymentRow>(new CommandDefinition("""
            SELECT Id, OrderId, CustomerId, Amount, Currency, Status, ProviderTransactionId, FailureReason, CreatedAtUtc, CompletedAtUtc,
                   AuthorizedAtUtc, CaptureRequestedAtUtc, CapturedAtUtc, VoidRequestedAtUtc, VoidedAtUtc,
                   RefundRequestedAtUtc, RefundedAtUtc, Provider, ProviderSessionId, PaymentActionIdempotencyKey,
                   PaymentActionRequestHash, PaymentActionExpiresAtUtc, ProviderCheckoutUrl
            FROM Payments
            WHERE Id = @PaymentId;
            """, new { PaymentId = paymentId }, cancellationToken: cancellationToken));

        return row is null ? null : MapPayment(row);
    }

    private static async Task<WebhookLogRow?> GetWebhookLogAsync(
        string providerEventId,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        return await transaction.Connection!.QuerySingleOrDefaultAsync<WebhookLogRow>(new CommandDefinition("""
            SELECT ProviderEventId, PaymentId, ProviderTransactionId, EventType, PayloadHash, SignatureStatus
            FROM WebhookLogs
            WHERE ProviderEventId = @ProviderEventId;
            """, new { ProviderEventId = providerEventId }, transaction, cancellationToken: cancellationToken));
    }

    private async Task RecordConflictAsync(
        WebhookLogRow existing,
        Guid paymentId,
        string providerTransactionId,
        PaymentStatus status,
        string payloadHash,
        string signatureStatus,
        CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO WebhookEventConflicts (
                Id, ProviderEventId, ExistingPaymentId, IncomingPaymentId,
                ExistingProviderTransactionId, IncomingProviderTransactionId,
                ExistingEventType, IncomingEventType,
                ExistingPayloadHash, IncomingPayloadHash,
                ExistingSignatureStatus, IncomingSignatureStatus, DetectedAtUtc)
            VALUES (
                @Id, @ProviderEventId, @ExistingPaymentId, @IncomingPaymentId,
                @ExistingProviderTransactionId, @IncomingProviderTransactionId,
                @ExistingEventType, @IncomingEventType,
                @ExistingPayloadHash, @IncomingPayloadHash,
                @ExistingSignatureStatus, @IncomingSignatureStatus, @DetectedAtUtc);
            """, new
        {
            Id = Guid.NewGuid(),
            existing.ProviderEventId,
            ExistingPaymentId = existing.PaymentId,
            IncomingPaymentId = paymentId,
            ExistingProviderTransactionId = existing.ProviderTransactionId,
            IncomingProviderTransactionId = providerTransactionId.Trim(),
            ExistingEventType = existing.EventType,
            IncomingEventType = status.ToString(),
            ExistingPayloadHash = existing.PayloadHash,
            IncomingPayloadHash = payloadHash,
            ExistingSignatureStatus = existing.SignatureStatus,
            IncomingSignatureStatus = signatureStatus,
            DetectedAtUtc = DateTime.UtcNow
        }, cancellationToken: cancellationToken));
    }

    private static bool Matches(
        WebhookLogRow existing,
        Guid paymentId,
        string providerTransactionId,
        PaymentStatus status,
        string payloadHash,
        string signatureStatus)
    {
        return existing.PaymentId == paymentId
            && string.Equals(existing.ProviderTransactionId, providerTransactionId.Trim(), StringComparison.Ordinal)
            && string.Equals(existing.EventType, status.ToString(), StringComparison.Ordinal)
            && string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal)
            && string.Equals(existing.SignatureStatus, signatureStatus, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<Payment?> GetPaymentForUpdateAsync(
        Guid paymentId,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var row = await transaction.Connection!.QuerySingleOrDefaultAsync<PaymentRow>(new CommandDefinition("""
            SELECT Id, OrderId, CustomerId, Amount, Currency, Status, ProviderTransactionId, FailureReason, CreatedAtUtc, CompletedAtUtc,
                   AuthorizedAtUtc, CaptureRequestedAtUtc, CapturedAtUtc, VoidRequestedAtUtc, VoidedAtUtc,
                   RefundRequestedAtUtc, RefundedAtUtc, Provider, ProviderSessionId, PaymentActionIdempotencyKey,
                   PaymentActionRequestHash, PaymentActionExpiresAtUtc, ProviderCheckoutUrl
            FROM Payments
            WHERE Id = @PaymentId
            FOR UPDATE;
            """, new { PaymentId = paymentId }, transaction, cancellationToken: cancellationToken));

        return row is null ? null : MapPayment(row);
    }

    private static async Task UpdatePaymentAsync(
        Payment payment,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await transaction.Connection!.ExecuteAsync(new CommandDefinition("""
            UPDATE Payments
            SET Status = @Status,
                ProviderTransactionId = @ProviderTransactionId,
                FailureReason = @FailureReason,
                CompletedAtUtc = @CompletedAtUtc,
                AuthorizedAtUtc = @AuthorizedAtUtc,
                CaptureRequestedAtUtc = @CaptureRequestedAtUtc,
                CapturedAtUtc = @CapturedAtUtc,
                VoidRequestedAtUtc = @VoidRequestedAtUtc,
                VoidedAtUtc = @VoidedAtUtc,
                RefundRequestedAtUtc = @RefundRequestedAtUtc,
                RefundedAtUtc = @RefundedAtUtc
            WHERE Id = @Id;
            """, ToParameters(payment), transaction, cancellationToken: cancellationToken));
    }

    private static async Task AddOutboxMessageAsync(
        Payment payment,
        PaymentStatus status,
        string providerEventId,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var message = status switch
        {
            PaymentStatus.Authorized => PaymentOutboxMessageFactory.Create(new PaymentAuthorizedIntegrationEvent
            {
                PaymentId = payment.Id,
                OrderId = payment.OrderId,
                CustomerId = payment.CustomerId,
                Amount = payment.Amount,
                Currency = payment.Currency,
                ProviderTransactionId = payment.ProviderTransactionId ?? string.Empty,
                CorrelationId = payment.OrderId.ToString("N"),
                CausationId = providerEventId
            }),
            PaymentStatus.Captured => PaymentOutboxMessageFactory.Create(new PaymentCapturedIntegrationEvent
            {
                PaymentId = payment.Id,
                OrderId = payment.OrderId,
                CustomerId = payment.CustomerId,
                Amount = payment.Amount,
                Currency = payment.Currency,
                ProviderTransactionId = payment.ProviderTransactionId ?? string.Empty,
                CorrelationId = payment.OrderId.ToString("N"),
                CausationId = providerEventId
            }),
            PaymentStatus.Voided => PaymentOutboxMessageFactory.Create(new PaymentVoidedIntegrationEvent
            {
                PaymentId = payment.Id,
                OrderId = payment.OrderId,
                CustomerId = payment.CustomerId,
                Amount = payment.Amount,
                Currency = payment.Currency,
                ProviderTransactionId = payment.ProviderTransactionId ?? string.Empty,
                Reason = payment.FailureReason ?? string.Empty,
                CorrelationId = payment.OrderId.ToString("N"),
                CausationId = providerEventId
            }),
            PaymentStatus.Refunded => PaymentOutboxMessageFactory.Create(new PaymentRefundedIntegrationEvent
            {
                PaymentId = payment.Id,
                OrderId = payment.OrderId,
                CustomerId = payment.CustomerId,
                Amount = payment.Amount,
                Currency = payment.Currency,
                ProviderTransactionId = payment.ProviderTransactionId ?? string.Empty,
                Reason = payment.FailureReason ?? string.Empty,
                CorrelationId = payment.OrderId.ToString("N"),
                CausationId = providerEventId
            }),
            PaymentStatus.Failed => PaymentOutboxMessageFactory.Create(new PaymentFailedIntegrationEvent
            {
                PaymentId = payment.Id,
                OrderId = payment.OrderId,
                CustomerId = payment.CustomerId,
                Amount = payment.Amount,
                Currency = payment.Currency,
                ProviderEventId = providerEventId,
                FailureReason = payment.FailureReason ?? "Payment failed.",
                CorrelationId = payment.OrderId.ToString("N"),
                CausationId = providerEventId
            }),
            _ => throw new InvalidOperationException($"Unsupported payment webhook status '{status}'.")
        };

        await InsertOutboxMessageAsync(message, transaction, cancellationToken);
    }

    private static async Task InsertOutboxMessageAsync(
        PaymentOutboxMessage message,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await transaction.Connection!.ExecuteAsync(new CommandDefinition("""
            INSERT INTO PaymentOutboxMessages (
                Id, OccurredAtUtc, Type, Content, CorrelationId, CausationId, Status, RetryCount, Error, NextAttemptAtUtc, ProcessedAtUtc)
            VALUES (
                @Id, @OccurredAtUtc, @Type, @Content, @CorrelationId, @CausationId, @Status, @RetryCount, @Error, @NextAttemptAtUtc, @ProcessedAtUtc)
            ON CONFLICT (Id) DO NOTHING;
            """, new
        {
            message.Id,
            message.OccurredAtUtc,
            message.Type,
            message.Content,
            message.CorrelationId,
            message.CausationId,
            message.Status,
            message.RetryCount,
            message.Error,
            message.NextAttemptAtUtc,
            message.ProcessedAtUtc
        }, transaction, cancellationToken: cancellationToken));
    }

    private static async Task MarkWebhookProcessedAsync(
        string providerEventId,
        DateTime processedAtUtc,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await transaction.Connection!.ExecuteAsync(new CommandDefinition("""
            UPDATE WebhookLogs
            SET Status = 'Processed',
                Error = NULL,
                ProcessedAtUtc = @ProcessedAtUtc
            WHERE ProviderEventId = @ProviderEventId;
            """, new
        {
            ProviderEventId = providerEventId,
            ProcessedAtUtc = processedAtUtc
        }, transaction, cancellationToken: cancellationToken));
    }

    private static async Task MarkWebhookFailedAsync(
        string providerEventId,
        string error,
        DateTime processedAtUtc,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await transaction.Connection!.ExecuteAsync(new CommandDefinition("""
            UPDATE WebhookLogs
            SET Status = 'Failed',
                Error = @Error,
                ProcessedAtUtc = @ProcessedAtUtc
            WHERE ProviderEventId = @ProviderEventId;
            """, new
        {
            ProviderEventId = providerEventId,
            Error = Truncate(error, 4000),
            ProcessedAtUtc = processedAtUtc
        }, transaction, cancellationToken: cancellationToken));
    }

    private static object ToParameters(Payment payment)
    {
        return new
        {
            payment.Id,
            Status = payment.Status.ToString(),
            payment.ProviderTransactionId,
            payment.FailureReason,
            payment.CompletedAtUtc,
            payment.AuthorizedAtUtc,
            payment.CaptureRequestedAtUtc,
            payment.CapturedAtUtc,
            payment.VoidRequestedAtUtc,
            payment.VoidedAtUtc,
            payment.RefundRequestedAtUtc,
            payment.RefundedAtUtc
        };
    }

    private static Payment MapPayment(PaymentRow row)
    {
        return new Payment(
            row.Id,
            row.OrderId,
            row.CustomerId,
            row.Amount,
            row.Currency,
            Enum.Parse<PaymentStatus>(row.Status),
            row.CreatedAtUtc,
            row.ProviderTransactionId,
            row.FailureReason,
            row.CompletedAtUtc,
            row.AuthorizedAtUtc,
            row.CaptureRequestedAtUtc,
            row.CapturedAtUtc,
            row.VoidRequestedAtUtc,
            row.VoidedAtUtc,
            row.RefundRequestedAtUtc,
            row.RefundedAtUtc,
            row.Provider,
            row.ProviderSessionId,
            row.PaymentActionIdempotencyKey,
            row.PaymentActionRequestHash,
            row.PaymentActionExpiresAtUtc,
            row.ProviderCheckoutUrl);
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private sealed record PaymentRow(
        Guid Id,
        Guid OrderId,
        Guid CustomerId,
        decimal Amount,
        string Currency,
        string Status,
        string? ProviderTransactionId,
        string? FailureReason,
        DateTime CreatedAtUtc,
        DateTime? CompletedAtUtc,
        DateTime? AuthorizedAtUtc,
        DateTime? CaptureRequestedAtUtc,
        DateTime? CapturedAtUtc,
        DateTime? VoidRequestedAtUtc,
        DateTime? VoidedAtUtc,
        DateTime? RefundRequestedAtUtc,
        DateTime? RefundedAtUtc,
        string? Provider,
        string? ProviderSessionId,
        string? PaymentActionIdempotencyKey,
        string? PaymentActionRequestHash,
        DateTime? PaymentActionExpiresAtUtc,
        string? ProviderCheckoutUrl);

    private static void ApplyWebhookStatus(
        Payment payment,
        PaymentStatus status,
        string providerTransactionId,
        string? failureReason,
        DateTime occurredAtUtc,
        bool allowProviderAutoCapture)
    {
        switch (status)
        {
            case PaymentStatus.Authorized:
                payment.MarkAuthorized(providerTransactionId, occurredAtUtc);
                break;
            case PaymentStatus.Captured:
                payment.MarkCaptured(providerTransactionId, occurredAtUtc, allowProviderAutoCapture);
                break;
            case PaymentStatus.Voided:
                payment.MarkVoided(occurredAtUtc);
                break;
            case PaymentStatus.Refunded:
                payment.MarkRefunded(occurredAtUtc);
                break;
            case PaymentStatus.Failed:
                payment.MarkFailed(failureReason ?? "Payment failed by provider.", occurredAtUtc);
                break;
            default:
                throw new InvalidOperationException($"Unsupported payment webhook status '{status}'.");
        }
    }

    private sealed record WebhookLogRow(
        string ProviderEventId,
        Guid PaymentId,
        string ProviderTransactionId,
        string EventType,
        string? PayloadHash,
        string? SignatureStatus);
}
