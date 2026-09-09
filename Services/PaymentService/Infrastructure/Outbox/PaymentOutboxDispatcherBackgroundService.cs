using System.Text.Json;
using BuildingBlocks.Contracts.Correlation;
using BuildingBlocks.Contracts.Events.Payments;
using Microsoft.Extensions.Options;
using MicroShop.ServiceDefaults.Diagnostics;
using PaymentService.Application.Abstractions;
using PaymentService.Domain.Outbox;

namespace PaymentService.Infrastructure.Outbox;

public sealed class PaymentOutboxDispatcherBackgroundService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaymentOutboxDispatcherBackgroundService> _logger;
    private readonly PaymentOutboxDispatcherOptions _options;

    public PaymentOutboxDispatcherBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<PaymentOutboxDispatcherBackgroundService> logger,
        IOptions<PaymentOutboxDispatcherOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Payment outbox dispatcher is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.IntervalSeconds));

        await DispatchPendingMessagesAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await DispatchPendingMessagesAsync(stoppingToken);
        }
    }

    private async Task DispatchPendingMessagesAsync(CancellationToken cancellationToken)
    {
        var lockId = Guid.NewGuid();

        using var scope = _scopeFactory.CreateScope();
        var outboxRepository = scope.ServiceProvider.GetRequiredService<IPaymentOutboxRepository>();
        var sagaClient = scope.ServiceProvider.GetRequiredService<OrderingPaymentSagaClient>();
        var reclaimedCount = await outboxRepository.ReclaimExpiredLocksAsync(cancellationToken);

        if (reclaimedCount > 0)
        {
            MicroShopMetrics.RecordOutboxMessage("PaymentService", "lease_reclaimed", reclaimedCount);
            _logger.LogWarning(
                "Reclaimed {ReclaimedCount} expired payment outbox dispatcher leases.",
                reclaimedCount);
        }

        var messages = await outboxRepository.ClaimPendingAsync(
            _options.BatchSize,
            _options.MaxRetryCount,
            lockId,
            TimeSpan.FromSeconds(_options.LockSeconds),
            cancellationToken);

        if (messages.Count > 0)
        {
            MicroShopMetrics.RecordOutboxMessage("PaymentService", "claimed", messages.Count);
        }

        foreach (var message in messages)
        {
            await DispatchMessageAsync(outboxRepository, sagaClient, message, lockId, cancellationToken);
        }
    }

    private async Task DispatchMessageAsync(
        IPaymentOutboxRepository outboxRepository,
        OrderingPaymentSagaClient sagaClient,
        PaymentOutboxMessage message,
        Guid lockId,
        CancellationToken cancellationToken)
    {
        try
        {
            using (CorrelationContext.BeginScope(message.CorrelationId))
            {
                await DispatchIntegrationEventAsync(sagaClient, message, cancellationToken);
            }

            var markedAsProcessed = await outboxRepository.MarkAsProcessedAsync(
                message.Id,
                lockId,
                cancellationToken);

            if (!markedAsProcessed)
            {
                _logger.LogWarning(
                    "Payment outbox lease was lost before message {OutboxMessageId} could be marked as processed.",
                    message.Id);
                return;
            }

            MicroShopMetrics.RecordOutboxMessage("PaymentService", "dispatched");

            _logger.LogInformation(
                "Dispatched payment outbox message {OutboxMessageId} of type {OutboxMessageType}.",
                message.Id,
                message.Type);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var nextAttemptAtUtc = CalculateNextAttemptAtUtc(message.RetryCount + 1);

            var markedAsFailed = await outboxRepository.MarkAsFailedAsync(
                message.Id,
                lockId,
                ex.Message,
                nextAttemptAtUtc,
                cancellationToken);

            if (!markedAsFailed)
            {
                _logger.LogWarning(
                    "Payment outbox lease was lost before failed message {OutboxMessageId} could be scheduled for retry.",
                    message.Id);
                return;
            }

            MicroShopMetrics.RecordOutboxMessage("PaymentService", "failed");

            _logger.LogWarning(
                ex,
                "Failed to dispatch payment outbox message {OutboxMessageId}. RetryCount={RetryCount}, NextAttemptAtUtc={NextAttemptAtUtc}.",
                message.Id,
                message.RetryCount + 1,
                nextAttemptAtUtc);
        }
    }

    private static async Task DispatchIntegrationEventAsync(
        OrderingPaymentSagaClient sagaClient,
        PaymentOutboxMessage message,
        CancellationToken cancellationToken)
    {
        if (message.Type is nameof(PaymentCollectionPendingIntegrationEvent) ||
            message.Type == typeof(PaymentCollectionPendingIntegrationEvent).FullName)
        {
            var integrationEvent = JsonSerializer.Deserialize<PaymentCollectionPendingIntegrationEvent>(message.Content, JsonOptions)
                ?? throw new InvalidOperationException($"Cannot deserialize outbox message {message.Id} to {nameof(PaymentCollectionPendingIntegrationEvent)}.");

            await sagaClient.ApplyPaymentCollectionPendingAsync(integrationEvent, cancellationToken);
            return;
        }

        if (message.Type is nameof(PaymentAuthorizedIntegrationEvent) ||
            message.Type == typeof(PaymentAuthorizedIntegrationEvent).FullName)
        {
            var integrationEvent = JsonSerializer.Deserialize<PaymentAuthorizedIntegrationEvent>(message.Content, JsonOptions)
                ?? throw new InvalidOperationException($"Cannot deserialize outbox message {message.Id} to {nameof(PaymentAuthorizedIntegrationEvent)}.");

            await sagaClient.ApplyPaymentAuthorizedAsync(integrationEvent, cancellationToken);
            return;
        }

        if (message.Type is nameof(PaymentCapturedIntegrationEvent) ||
            message.Type == typeof(PaymentCapturedIntegrationEvent).FullName)
        {
            var integrationEvent = JsonSerializer.Deserialize<PaymentCapturedIntegrationEvent>(message.Content, JsonOptions)
                ?? throw new InvalidOperationException($"Cannot deserialize outbox message {message.Id} to {nameof(PaymentCapturedIntegrationEvent)}.");

            await sagaClient.ApplyPaymentCapturedAsync(integrationEvent, cancellationToken);
            return;
        }

        if (message.Type is nameof(PaymentVoidedIntegrationEvent) ||
            message.Type == typeof(PaymentVoidedIntegrationEvent).FullName)
        {
            var integrationEvent = JsonSerializer.Deserialize<PaymentVoidedIntegrationEvent>(message.Content, JsonOptions)
                ?? throw new InvalidOperationException($"Cannot deserialize outbox message {message.Id} to {nameof(PaymentVoidedIntegrationEvent)}.");

            await sagaClient.ApplyPaymentVoidedAsync(integrationEvent, cancellationToken);
            return;
        }

        if (message.Type is nameof(PaymentRefundedIntegrationEvent) ||
            message.Type == typeof(PaymentRefundedIntegrationEvent).FullName)
        {
            var integrationEvent = JsonSerializer.Deserialize<PaymentRefundedIntegrationEvent>(message.Content, JsonOptions)
                ?? throw new InvalidOperationException($"Cannot deserialize outbox message {message.Id} to {nameof(PaymentRefundedIntegrationEvent)}.");

            await sagaClient.ApplyPaymentRefundedAsync(integrationEvent, cancellationToken);
            return;
        }

        if (message.Type is nameof(PaymentSucceededIntegrationEvent) ||
            message.Type == typeof(PaymentSucceededIntegrationEvent).FullName)
        {
            var integrationEvent = JsonSerializer.Deserialize<PaymentSucceededIntegrationEvent>(
                message.Content,
                JsonOptions);

            if (integrationEvent is null)
            {
                throw new InvalidOperationException(
                    $"Cannot deserialize outbox message {message.Id} to {nameof(PaymentSucceededIntegrationEvent)}.");
            }

            await sagaClient.ApplyPaymentSucceededAsync(integrationEvent, cancellationToken);
            return;
        }

        if (message.Type is nameof(PaymentFailedIntegrationEvent) ||
            message.Type == typeof(PaymentFailedIntegrationEvent).FullName)
        {
            var integrationEvent = JsonSerializer.Deserialize<PaymentFailedIntegrationEvent>(
                message.Content,
                JsonOptions);

            if (integrationEvent is null)
            {
                throw new InvalidOperationException(
                    $"Cannot deserialize outbox message {message.Id} to {nameof(PaymentFailedIntegrationEvent)}.");
            }

            await sagaClient.ApplyPaymentFailedAsync(integrationEvent, cancellationToken);
            return;
        }

        throw new NotSupportedException($"Unsupported payment outbox message type: {message.Type}");
    }

    private DateTime CalculateNextAttemptAtUtc(int retryCount)
    {
        var delaySeconds = Math.Min(
            _options.MaxRetryDelaySeconds,
            _options.RetryDelaySeconds * Math.Max(1, retryCount));

        return DateTime.UtcNow.AddSeconds(delaySeconds);
    }
}
