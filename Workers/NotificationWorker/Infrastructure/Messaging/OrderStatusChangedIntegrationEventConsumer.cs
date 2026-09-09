using BuildingBlocks.Contracts.Correlation;
using BuildingBlocks.Contracts.Events.Orders;
using MassTransit;
using NotificationWorker.Application.Abstractions;
using NotificationWorker.Application.Notifications.HandleOrderStatusChanged;
using NotificationWorker.Application.Realtime;

namespace NotificationWorker.Infrastructure.Messaging;

public sealed class OrderStatusChangedIntegrationEventConsumer : IConsumer<OrderStatusChangedIntegrationEvent>
{
    private readonly OrderStatusChangedNotificationHandler _handler;
    private readonly ILogger<OrderStatusChangedIntegrationEventConsumer> _logger;
    private readonly ICustomerRealtimeNotifier _realtimeNotifier;

    public OrderStatusChangedIntegrationEventConsumer(
        OrderStatusChangedNotificationHandler handler,
        ICustomerRealtimeNotifier realtimeNotifier,
        ILogger<OrderStatusChangedIntegrationEventConsumer> logger)
    {
        _handler = handler;
        _realtimeNotifier = realtimeNotifier;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderStatusChangedIntegrationEvent> context)
    {
        var message = context.Message;
        var correlationId = message.CorrelationId ?? context.Headers.Get<string>("X-Correlation-ID");

        using (CorrelationContext.BeginScope(correlationId))
        using (_logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlationId }))
        {
            _logger.LogInformation(
                "OrderStatusChangedIntegrationEvent received. EventId={EventId}, OrderId={OrderId}, CustomerId={CustomerId}, PreviousStatus={PreviousStatus}, CurrentStatus={CurrentStatus}, CorrelationId={CorrelationId}",
                message.EventId,
                message.OrderId,
                message.CustomerId,
                message.PreviousStatus,
                message.CurrentStatus,
                correlationId);

            var notification = new OrderStatusChangedNotification(
                message.EventId,
                message.OrderId,
                message.CustomerId,
                message.PreviousStatus,
                message.CurrentStatus,
                message.TotalAmount,
                message.Currency,
                message.OccurredAtUtc,
                message.Version,
                correlationId);

            await _handler.HandleAsync(notification, context.CancellationToken);
            await _realtimeNotifier.PublishAsync(
                message.CustomerId,
                new CustomerRealtimeUpdate(
                    message.EventId,
                    "order.status-changed",
                    message.OrderId,
                    message.CurrentStatus,
                    message.OccurredAtUtc,
                    correlationId),
                context.CancellationToken);
        }
    }
}