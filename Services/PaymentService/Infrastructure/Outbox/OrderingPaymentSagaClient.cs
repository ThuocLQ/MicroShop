using System.Net.Http.Json;
using BuildingBlocks.Contracts.Events.Payments;

namespace PaymentService.Infrastructure.Outbox;

public sealed class OrderingPaymentSagaClient
{
    private readonly HttpClient _httpClient;
    private readonly string _internalApiKey;

    public OrderingPaymentSagaClient(
        HttpClient httpClient,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _httpClient = httpClient;
        _internalApiKey = configuration["InternalApi:Key"]
            ?? throw new InvalidOperationException("InternalApi:Key is missing.");

        if (!environment.IsDevelopment() &&
            (_internalApiKey.Contains("SET_BY_ENVIRONMENT", StringComparison.OrdinalIgnoreCase) ||
             _internalApiKey.Contains("CHANGEME", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "InternalApi:Key must be supplied from a non-development secret source outside Development.");
        }
    }

    public Task ApplyPaymentCollectionPendingAsync(
        PaymentCollectionPendingIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        SendAsync(
            integrationEvent.OrderId,
            new ApplyPaymentSagaEventRequest(
                integrationEvent.EventId,
                integrationEvent.PaymentId,
                "PaymentCollectionPending",
                null),
            cancellationToken);

    public async Task ApplyPaymentSucceededAsync(
        PaymentSucceededIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await SendAsync(
            integrationEvent.OrderId,
            new ApplyPaymentSagaEventRequest(
                integrationEvent.EventId,
                integrationEvent.PaymentId,
                "PaymentSucceeded",
                null),
            cancellationToken);
    }

    public Task ApplyPaymentAuthorizedAsync(
        PaymentAuthorizedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        SendAsync(
            integrationEvent.OrderId,
            new ApplyPaymentSagaEventRequest(
                integrationEvent.EventId,
                integrationEvent.PaymentId,
                "PaymentAuthorized",
                null),
            cancellationToken);

    public Task ApplyPaymentCapturedAsync(
        PaymentCapturedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        SendAsync(
            integrationEvent.OrderId,
            new ApplyPaymentSagaEventRequest(
                integrationEvent.EventId,
                integrationEvent.PaymentId,
                "PaymentCaptured",
                null),
            cancellationToken);

    public Task ApplyPaymentVoidedAsync(
        PaymentVoidedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        SendAsync(
            integrationEvent.OrderId,
            new ApplyPaymentSagaEventRequest(
                integrationEvent.EventId,
                integrationEvent.PaymentId,
                "PaymentVoided",
                integrationEvent.Reason),
            cancellationToken);

    public Task ApplyPaymentRefundedAsync(
        PaymentRefundedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        SendAsync(
            integrationEvent.OrderId,
            new ApplyPaymentSagaEventRequest(
                integrationEvent.EventId,
                integrationEvent.PaymentId,
                "PaymentRefunded",
                integrationEvent.Reason),
            cancellationToken);

    public async Task ApplyPaymentFailedAsync(
        PaymentFailedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await SendAsync(
            integrationEvent.OrderId,
            new ApplyPaymentSagaEventRequest(
                integrationEvent.EventId,
                integrationEvent.PaymentId,
                "PaymentFailed",
                integrationEvent.FailureReason),
            cancellationToken);
    }

    private async Task SendAsync(
        Guid orderId,
        ApplyPaymentSagaEventRequest request,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"/_internal/orders/{orderId}/payment-events")
        {
            Content = JsonContent.Create(request)
        };

        httpRequest.Headers.TryAddWithoutValidation("X-MicroShop-Internal-Key", _internalApiKey);

        var correlationId = BuildingBlocks.Contracts.Correlation.CorrelationContext.CorrelationId;
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            httpRequest.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
        }

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Ordering saga endpoint returned {(int)response.StatusCode}. Body: {body}");
        }
    }

    private sealed record ApplyPaymentSagaEventRequest(
        Guid EventId,
        Guid PaymentId,
        string EventType,
        string? FailureReason);
}
