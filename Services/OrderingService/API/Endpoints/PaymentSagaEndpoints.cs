using MediatR;
using OrderingService.API.Contracts;
using OrderingService.Application.OrderPaymentSagas.ApplyPaymentEvent;
using MicroShop.ServiceDefaults.Diagnostics;

namespace OrderingService.API.Endpoints;

public static class PaymentSagaEndpoints
{
    public static IEndpointRouteBuilder MapPaymentSagaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/_internal/orders")
            .WithTags("Internal Payment Saga")
            .ExcludeFromDescription()
            .RequireInternalApiKey(app.ServiceProvider.GetRequiredService<IConfiguration>());

        group.MapPost("/{orderId:guid}/payment-events", async (
            Guid orderId,
            ApplyPaymentSagaEventRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseEventType(request.EventType, out var eventType))
            {
                return ApiProblemResults.BadRequest("EventType must be 'PaymentCollectionPending', 'PaymentAuthorized', 'PaymentCaptured', 'PaymentVoided', 'PaymentRefunded', 'PaymentSucceeded', 'PaymentFailed', or 'PaymentTimedOut'.", "PAYMENT_EVENT_TYPE_INVALID");
            }

            var result = await sender.Send(new ApplyPaymentSagaEventCommand(
                request.EventId,
                eventType,
                orderId,
                request.PaymentId,
                request.FailureReason), cancellationToken);

            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        return app;
    }

    private static bool TryParseEventType(string eventType, out OrderPaymentSagaEventType result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(eventType))
        {
            return false;
        }

        var normalized = eventType.Trim();
        normalized = normalized.Equals("CollectionPending", StringComparison.OrdinalIgnoreCase)
            ? nameof(OrderPaymentSagaEventType.PaymentCollectionPending)
            : normalized;
        normalized = normalized.Equals("Succeeded", StringComparison.OrdinalIgnoreCase)
            ? nameof(OrderPaymentSagaEventType.PaymentSucceeded)
            : normalized;
        normalized = normalized.Equals("Authorized", StringComparison.OrdinalIgnoreCase)
            ? nameof(OrderPaymentSagaEventType.PaymentAuthorized)
            : normalized;
        normalized = normalized.Equals("Captured", StringComparison.OrdinalIgnoreCase)
            ? nameof(OrderPaymentSagaEventType.PaymentCaptured)
            : normalized;
        normalized = normalized.Equals("Voided", StringComparison.OrdinalIgnoreCase)
            ? nameof(OrderPaymentSagaEventType.PaymentVoided)
            : normalized;
        normalized = normalized.Equals("Refunded", StringComparison.OrdinalIgnoreCase)
            ? nameof(OrderPaymentSagaEventType.PaymentRefunded)
            : normalized;
        normalized = normalized.Equals("Failed", StringComparison.OrdinalIgnoreCase)
            ? nameof(OrderPaymentSagaEventType.PaymentFailed)
            : normalized;
        normalized = normalized.Equals("TimedOut", StringComparison.OrdinalIgnoreCase) ||
                     normalized.Equals("Timeout", StringComparison.OrdinalIgnoreCase)
            ? nameof(OrderPaymentSagaEventType.PaymentTimedOut)
            : normalized;

        return Enum.TryParse(normalized, ignoreCase: true, out result) &&
               Enum.IsDefined(result);
    }
}
