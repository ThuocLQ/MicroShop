using Microsoft.AspNetCore.SignalR;
using NotificationWorker.Application.Realtime;

namespace NotificationWorker.Infrastructure.Realtime;

public sealed class CustomerRealtimeNotifier : ICustomerRealtimeNotifier
{
    private readonly IHubContext<CustomerUpdatesHub, ICustomerUpdatesClient> _hubContext;
    private readonly ILogger<CustomerRealtimeNotifier> _logger;

    public CustomerRealtimeNotifier(
        IHubContext<CustomerUpdatesHub, ICustomerUpdatesClient> hubContext,
        ILogger<CustomerRealtimeNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task PublishAsync(Guid customerId, CustomerRealtimeUpdate update, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hubContext.Clients.Group(CustomerUpdatesHub.GroupName(customerId)).ReceiveCustomerUpdate(update);
        }
        catch (Exception exception)
        {
            // Realtime delivery is a best-effort signal; REST remains the canonical recovery path.
            _logger.LogWarning(
                exception,
                "Realtime customer update was not delivered. EventId={EventId}, CustomerId={CustomerId}, ResourceId={ResourceId}",
                update.EventId,
                customerId,
                update.ResourceId);
        }
    }
}