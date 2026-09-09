using BuildingBlocks.Contracts.Events.Discounts;
using BuildingBlocks.Contracts.Events.Payments;
using MediatR;
using OrderingService.Application.Abstractions;
using OrderingService.Application.IntegrationEvents;
using OrderingService.Application.Outbox;
using OrderingService.Domain.OrderPaymentSagas;
using OrderingService.Domain.Orders;

namespace OrderingService.Application.OrderPaymentSagas.ApplyInventorySettlement;

public sealed class ApplyInventorySettlementEventHandler
    : IRequestHandler<ApplyInventorySettlementEventCommand, InventorySettlementApplyResult>
{
    internal const string ConsumerName = "OrderingService.InventorySettlement";

    private readonly IOrderingUnitOfWork _unitOfWork;
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderPaymentSagaRepository _sagaRepository;
    private readonly IInboxRepository _inboxRepository;
    private readonly IOutboxRepository _outboxRepository;

    public ApplyInventorySettlementEventHandler(
        IOrderingUnitOfWork unitOfWork,
        IOrderRepository orderRepository,
        IOrderPaymentSagaRepository sagaRepository,
        IInboxRepository inboxRepository,
        IOutboxRepository outboxRepository)
    {
        _unitOfWork = unitOfWork;
        _orderRepository = orderRepository;
        _sagaRepository = sagaRepository;
        _inboxRepository = inboxRepository;
        _outboxRepository = outboxRepository;
    }

    public async Task<InventorySettlementApplyResult> Handle(
        ApplyInventorySettlementEventCommand request,
        CancellationToken cancellationToken)
    {
        if (request.EventId == Guid.Empty)
        {
            throw new ArgumentException("Event id cannot be empty.", nameof(request.EventId));
        }

        if (request.OrderId == Guid.Empty)
        {
            throw new ArgumentException("Order id cannot be empty.", nameof(request.OrderId));
        }

        var result = await _unitOfWork.ExecuteAsync(async transaction =>
        {
            var order = await _orderRepository.GetByIdAsync(request.OrderId, transaction, cancellationToken);
            if (order is null)
            {
                return new InventorySettlementApplyResult(false, null);
            }

            if (!await _inboxRepository.TryRecordAsync(request.EventId, ConsumerName, transaction, cancellationToken))
            {
                var replaySaga = await _sagaRepository.GetByOrderIdAsync(order.Id, transaction, cancellationToken);
                return new InventorySettlementApplyResult(true, replaySaga is null ? null : OrderPaymentSagaMapper.ToDto(replaySaga));
            }

            var currentSaga = await _sagaRepository.GetByOrderIdAsync(order.Id, transaction, cancellationToken);
            if (currentSaga is null)
            {
                if (request.EventType != OrderInventorySettlementEventType.InventoryReleased)
                {
                    throw new InvalidOperationException($"Payment saga for order '{order.Id}' was not found.");
                }

                await CancelOrderForExpiredReservationAsync(order, request.EventId, transaction, cancellationToken);
                return new InventorySettlementApplyResult(true, null);
            }

            var previousState = currentSaga.State;
            var updatedAtUtc = DateTime.UtcNow;

            if (request.EventType == OrderInventorySettlementEventType.InventoryReleased &&
                currentSaga.State is OrderPaymentSagaState.PaymentRequested or OrderPaymentSagaState.CashOnDeliveryRequested)
            {
                await CancelOrderForExpiredReservationAsync(order, request.EventId, transaction, cancellationToken);
            }

            if (request.EventType == OrderInventorySettlementEventType.InventoryCommitted &&
                (currentSaga.ExpectedInventoryCommandEventId is null ||
                 request.CausationEventId != currentSaga.ExpectedInventoryCommandEventId))
            {
                currentSaga.RecordIgnoredEvent(
                    request.EventId,
                    updatedAtUtc,
                    "InventoryCommitted causation does not match the expected inventory command.");
            }
            else if (request.EventType == OrderInventorySettlementEventType.InventoryCommitted &&
                     currentSaga.State == OrderPaymentSagaState.CashOnDeliveryRequested)
            {
                await ConfirmCashOnDeliveryOrderAsync(
                    order,
                    currentSaga,
                    request.EventId,
                    updatedAtUtc,
                    transaction,
                    cancellationToken);
            }
            else
            {
                ApplySettlement(request, currentSaga, updatedAtUtc);
            }

            if (currentSaga.State != previousState)
            {
                var stateChangedEvent = OrderIntegrationEventFactory.CreatePaymentSagaStateChanged(
                    currentSaga,
                    previousState,
                    request.EventId.ToString("D"));
                await _outboxRepository.AddAsync(OutboxMessageFactory.Create(stateChangedEvent), transaction, cancellationToken);
                await AddPaymentCommandAsync(order, currentSaga, request.EventId, transaction, cancellationToken);
            }

            await _sagaRepository.UpsertAsync(currentSaga, transaction, cancellationToken);
            return new InventorySettlementApplyResult(true, OrderPaymentSagaMapper.ToDto(currentSaga));
        }, cancellationToken);

        return result;
    }

    private async Task CancelOrderForExpiredReservationAsync(
        Order order,
        Guid causationEventId,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (order.Status is not (OrderStatus.Pending or OrderStatus.PendingPayment))
        {
            return;
        }

        var previousStatus = order.Status;
        if (!order.Cancel())
        {
            return;
        }

        var updated = await _orderRepository.TryUpdateStatusAsync(
            order.Id,
            order.Status,
            [previousStatus],
            transaction,
            cancellationToken);
        if (!updated)
        {
            throw new InvalidOperationException("Order status changed before inventory expiration was applied.");
        }

        var statusChanged = OrderIntegrationEventFactory.CreateOrderStatusChanged(order, previousStatus) with
        {
            CausationId = causationEventId.ToString("D")
        };
        var projection = OrderIntegrationEventFactory.CreateOrderProjectionStatusChanged(order, previousStatus) with
        {
            CausationId = causationEventId.ToString("D")
        };
        await _outboxRepository.AddAsync(OutboxMessageFactory.Create(statusChanged), transaction, cancellationToken);
        await _outboxRepository.AddAsync(OutboxMessageFactory.CreateKafka(projection), transaction, cancellationToken);
    }

    private async Task ConfirmCashOnDeliveryOrderAsync(
        Order order,
        OrderPaymentSaga saga,
        Guid eventId,
        DateTime updatedAtUtc,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (order.Status != OrderStatus.PendingPayment)
        {
            saga.RecordIgnoredEvent(
                eventId,
                updatedAtUtc,
                $"Inventory committed for cash-on-delivery while order status was {order.Status}.");
            return;
        }

        var previousStatus = order.Status;
        if (!order.MoveToFulfillmentStatus(OrderStatus.Confirmed) ||
            !await _orderRepository.TryUpdateStatusAsync(
                order.Id,
                order.Status,
                [previousStatus],
                transaction,
                cancellationToken))
        {
            throw new InvalidOperationException("Order status changed before cash-on-delivery fulfillment was confirmed.");
        }

        await _outboxRepository.AddAsync(
            OutboxMessageFactory.Create(OrderIntegrationEventFactory.CreateOrderStatusChanged(order, previousStatus) with
            {
                CausationId = eventId.ToString("D")
            }),
            transaction,
            cancellationToken);
        await _outboxRepository.AddAsync(
            OutboxMessageFactory.CreateKafka(OrderIntegrationEventFactory.CreateOrderProjectionStatusChanged(order, previousStatus) with
            {
                CausationId = eventId.ToString("D")
            }),
            transaction,
            cancellationToken);

        if (order.DiscountReservationId is { } reservationId)
        {
            await _outboxRepository.AddAsync(
                OutboxMessageFactory.Create(new PromotionRedeemRequestedIntegrationEvent
                {
                    ReservationId = reservationId,
                    OrderId = order.Id,
                    CorrelationId = order.Id.ToString("N"),
                    CausationId = eventId.ToString("D")
                }),
                transaction,
                cancellationToken);
        }

        saga.MarkCashOnDeliveryReadyForFulfillment(eventId, updatedAtUtc);
    }

    private Task AddPaymentCommandAsync(
        OrderingService.Domain.Orders.Order order,
        OrderPaymentSaga saga,
        Guid causationEventId,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (saga.State == OrderPaymentSagaState.CaptureRequested)
        {
            return _outboxRepository.AddAsync(
                OutboxMessageFactory.Create(new PaymentCaptureRequestedIntegrationEvent
                {
                    PaymentId = saga.PaymentId,
                    OrderId = order.Id,
                    CustomerId = order.CustomerId,
                    Amount = order.TotalAmount,
                    Currency = order.Currency,
                    CorrelationId = order.Id.ToString("N"),
                    CausationId = causationEventId.ToString("D")
                }),
                transaction,
                cancellationToken);
        }

        if (saga.State == OrderPaymentSagaState.VoidRequested)
        {
            return _outboxRepository.AddAsync(
                OutboxMessageFactory.Create(new PaymentVoidRequestedIntegrationEvent
                {
                    PaymentId = saga.PaymentId,
                    OrderId = order.Id,
                    CustomerId = order.CustomerId,
                    Amount = order.TotalAmount,
                    Currency = order.Currency,
                    Reason = saga.LastError ?? "Inventory reservation was released.",
                    CorrelationId = order.Id.ToString("N"),
                    CausationId = causationEventId.ToString("D")
                }),
                transaction,
                cancellationToken);
        }

        if (saga.State == OrderPaymentSagaState.RefundRequested)
        {
            return _outboxRepository.AddAsync(
                OutboxMessageFactory.Create(new PaymentRefundRequestedIntegrationEvent
                {
                    PaymentId = saga.PaymentId,
                    OrderId = order.Id,
                    CustomerId = order.CustomerId,
                    Amount = order.TotalAmount,
                    Currency = order.Currency,
                    Reason = saga.LastError ?? "Inventory settlement requires payment refund.",
                    CorrelationId = order.Id.ToString("N"),
                    CausationId = causationEventId.ToString("D")
                }),
                transaction,
                cancellationToken);
        }

        return Task.CompletedTask;
    }

    private static void ApplySettlement(
        ApplyInventorySettlementEventCommand request,
        OrderPaymentSaga saga,
        DateTime updatedAtUtc)
    {
        switch (request.EventType)
        {
            case OrderInventorySettlementEventType.InventoryCommitted:
                if (saga.State == OrderPaymentSagaState.PaymentAuthorized)
                {
                    saga.MarkCaptureRequested(request.EventId, updatedAtUtc);
                    return;
                }

                if (saga.State == OrderPaymentSagaState.OrderPaid)
                {
                    saga.MarkInventoryCommitted(request.EventId, updatedAtUtc);
                    return;
                }

                saga.RecordIgnoredEvent(
                    request.EventId,
                    updatedAtUtc,
                    "InventoryCommitted was received after the payment saga had already reached a terminal state.");
                return;

            case OrderInventorySettlementEventType.InventoryReleased:
                if (saga.State is OrderPaymentSagaState.PaymentRequested or OrderPaymentSagaState.CashOnDeliveryRequested)
                {
                    saga.MarkTimedOut(
                        request.EventId,
                        updatedAtUtc,
                        string.IsNullOrWhiteSpace(request.Reason)
                            ? "Inventory reservation expired before payment completed."
                            : request.Reason);
                    return;
                }

                if (saga.State is OrderPaymentSagaState.PaymentAuthorized or OrderPaymentSagaState.CaptureRequested)
                {
                    saga.MarkVoidRequested(
                        request.EventId,
                        updatedAtUtc,
                        string.IsNullOrWhiteSpace(request.Reason)
                            ? "Inventory reservation was released before capture completed."
                            : request.Reason);
                    return;
                }

                if (saga.State is OrderPaymentSagaState.OrderPaid or OrderPaymentSagaState.InventoryCommitted)
                {
                    saga.MarkRefundRequested(
                        request.EventId,
                        updatedAtUtc,
                        string.IsNullOrWhiteSpace(request.Reason)
                            ? "Inventory reservation was released after payment succeeded; payment must be refunded."
                            : request.Reason);
                    return;
                }

                saga.RecordIgnoredEvent(request.EventId, updatedAtUtc, request.Reason);
                return;

            default:
                throw new InvalidOperationException($"Unsupported inventory settlement event type '{request.EventType}'.");
        }
    }
}
