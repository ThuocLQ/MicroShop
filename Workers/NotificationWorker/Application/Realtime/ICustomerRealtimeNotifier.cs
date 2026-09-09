namespace NotificationWorker.Application.Realtime;

public sealed record CustomerRealtimeUpdate(
    Guid EventId,
    string EventType,
    Guid ResourceId,
    string Status,
    DateTime OccurredAtUtc,
    string? CorrelationId);

public interface ICustomerRealtimeNotifier
{
    Task PublishAsync(Guid customerId, CustomerRealtimeUpdate update, CancellationToken cancellationToken = default);
}