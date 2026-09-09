using MediatR;
using BuildingBlocks.Contracts.Events.Inventory;
using BuildingBlocks.Contracts.Events.Payments;
using BuildingBlocks.Contracts.Events.Discounts;
using OrderingService.Application.Abstractions;
using OrderingService.Application.IntegrationEvents;
using OrderingService.Application.Outbox;
using OrderingService.Domain.OrderPaymentSagas;
using OrderingService.Domain.Orders;

namespace OrderingService.Application.OrderPaymentSagas.ApplyPaymentEvent;

public sealed class ApplyPaymentSagaEventHandler : IRequestHandler<ApplyPaymentSagaEventCommand, OrderPaymentSagaDto?>
{
    private static readonly TimeSpan DefaultPaymentTimeout = TimeSpan.FromMinutes(30);

    private readonly IOrderingUnitOfWork _unitOfWork;
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderPaymentSagaRepository _sagaRepository;
    private readonly IOutboxRepository _outboxRepository;

    public ApplyPaymentSagaEventHandler(
        IOrderingUnitOfWork unitOfWork,
        IOrderRepository orderRepository,
        IOrderPaymentSagaRepository sagaRepository,
        IOutboxRepository outboxRepository)
    {
        _unitOfWork = unitOfWork;
        _orderRepository = orderRepository;
        _sagaRepository = sagaRepository;
        _outboxRepository = outboxRepository;
    }

    public async Task<OrderPaymentSagaDto?> Handle(
        ApplyPaymentSagaEventCommand request,
        CancellationToken cancellationToken)
    {
        if (request.EventId == Guid.Empty)
        {
            throw new ArgumentException("Event id cannot be empty.", nameof(request.EventId));
        }

        if (request.PaymentId == Guid.Empty)
        {
            throw new ArgumentException("Payment id cannot be empty.", nameof(request.PaymentId));
        }

        var saga = await _unitOfWork.ExecuteAsync(async transaction =>
        {
            var order = await _orderRepository.GetByIdAsync(request.OrderId, transaction, cancellationToken);
            if (order is null)
            {
                return null;
            }

            var currentSaga = await _sagaRepository.GetByOrderIdAsync(order.Id, transaction, cancellationToken)
                ?? OrderPaymentSaga.Start(order.Id, request.PaymentId, DateTime.UtcNow, DefaultPaymentTimeout);

            if (currentSaga.HasProcessed(request.EventId))
            {
                return currentSaga;
            }

            var previousOrderStatus = order.Status;
            var previousSagaState = currentSaga.State;
            await ApplyEventAsync(request, order, currentSaga, transaction, cancellationToken);

            if (order.Status != previousOrderStatus)
            {
                var statusChangedEvent = OrderIntegrationEventFactory.CreateOrderStatusChanged(order, previousOrderStatus);
                var projectionEvent = OrderIntegrationEventFactory.CreateOrderProjectionStatusChanged(order, previousOrderStatus);
                await _outboxRepository.AddAsync(OutboxMessageFactory.Create(statusChangedEvent), transaction, cancellationToken);
                await _outboxRepository.AddAsync(OutboxMessageFactory.CreateKafka(projectionEvent), transaction, cancellationToken);
                await AddPromotionSettlementCommandAsync(order, request.EventId, transaction, cancellationToken);
            }

            if (currentSaga.State != previousSagaState)
            {
                var sagaStateChangedEvent = OrderIntegrationEventFactory.CreatePaymentSagaStateChanged(currentSaga, previousSagaState);
                await _outboxRepository.AddAsync(OutboxMessageFactory.Create(sagaStateChangedEvent), transaction, cancellationToken);
                await AddInventoryCommandAsync(currentSaga, request.EventType, previousOrderStatus, transaction, cancellationToken);
                await AddPaymentOperationCommandAsync(order, currentSaga, request.EventId, transaction, cancellationToken);
            }

            await _sagaRepository.UpsertAsync(currentSaga, transaction, cancellationToken);

            return currentSaga;
        }, cancellationToken);

        if (saga is null)
        {
            return null;
        }

        return OrderPaymentSagaMapper.ToDto(saga);
    }

    private Task AddInventoryCommandAsync(
        OrderPaymentSaga saga,
        OrderPaymentSagaEventType eventType,
        OrderStatus previousOrderStatus,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (saga.State == OrderPaymentSagaState.PaymentAuthorized ||
            saga.State == OrderPaymentSagaState.CashOnDeliveryRequested ||
            saga.State == OrderPaymentSagaState.OrderPaid && eventType == OrderPaymentSagaEventType.PaymentSucceeded)
        {
            var command = new InventoryCommitRequestedIntegrationEvent { OrderId = saga.OrderId };
            saga.ExpectInventorySettlement(command.EventId);
            return _outboxRepository.AddAsync(OutboxMessageFactory.Create(command), transaction, cancellationToken);
        }

        if ((saga.State is OrderPaymentSagaState.OrderCancelled or OrderPaymentSagaState.TimedOut) &&
            previousOrderStatus != OrderStatus.Cancelled)
        {
            var command = new InventoryReleaseRequestedIntegrationEvent
            {
                OrderId = saga.OrderId,
                Reason = saga.LastError ?? saga.State.ToString()
            };
            return _outboxRepository.AddAsync(OutboxMessageFactory.Create(command), transaction, cancellationToken);
        }

        return Task.CompletedTask;
    }

    private Task AddPaymentOperationCommandAsync(
        Order order,
        OrderPaymentSaga saga,
        Guid causationEventId,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (saga.State == OrderPaymentSagaState.VoidRequested)
        {
            return _outboxRepository.AddAsync(OutboxMessageFactory.Create(new PaymentVoidRequestedIntegrationEvent
            {
                PaymentId = saga.PaymentId,
                OrderId = order.Id,
                CustomerId = order.CustomerId,
                Amount = order.TotalAmount,
                Currency = order.Currency,
                Reason = saga.LastError ?? "Payment saga requested void.",
                CorrelationId = order.Id.ToString("N"),
                CausationId = causationEventId.ToString("D")
            }), transaction, cancellationToken);
        }

        if (saga.State == OrderPaymentSagaState.RefundRequested)
        {
            return _outboxRepository.AddAsync(OutboxMessageFactory.Create(new PaymentRefundRequestedIntegrationEvent
            {
                PaymentId = saga.PaymentId,
                OrderId = order.Id,
                CustomerId = order.CustomerId,
                Amount = order.TotalAmount,
                Currency = order.Currency,
                Reason = saga.LastError ?? "Payment saga requested refund.",
                CorrelationId = order.Id.ToString("N"),
                CausationId = causationEventId.ToString("D")
            }), transaction, cancellationToken);
        }

        return Task.CompletedTask;
    }

    private Task AddPromotionSettlementCommandAsync(
        Order order,
        Guid causationEventId,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (order.DiscountReservationId is not { } reservationId)
        {
            return Task.CompletedTask;
        }

        if (order.Status is OrderStatus.Paid or OrderStatus.Confirmed)
        {
            return _outboxRepository.AddAsync(OutboxMessageFactory.Create(new PromotionRedeemRequestedIntegrationEvent
            {
                ReservationId = reservationId,
                OrderId = order.Id,
                CorrelationId = order.Id.ToString("N"),
                CausationId = causationEventId.ToString("D")
            }), transaction, cancellationToken);
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return _outboxRepository.AddAsync(OutboxMessageFactory.Create(new PromotionReleaseRequestedIntegrationEvent
            {
                ReservationId = reservationId,
                OrderId = order.Id,
                Reason = "Order was cancelled before payment capture completed.",
                CorrelationId = order.Id.ToString("N"),
                CausationId = causationEventId.ToString("D")
            }), transaction, cancellationToken);
        }

        return Task.CompletedTask;
    }

    private async Task ApplyEventAsync(
        ApplyPaymentSagaEventCommand request,
        Order order,
        OrderPaymentSaga saga,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var updatedAtUtc = DateTime.UtcNow;

        switch (request.EventType)
        {
            case OrderPaymentSagaEventType.PaymentCollectionPending:
                ApplyPaymentCollectionPending(request, order, saga, updatedAtUtc);
                break;
            case OrderPaymentSagaEventType.PaymentAuthorized:
                ApplyPaymentAuthorized(request, order, saga, updatedAtUtc);
                break;
            case OrderPaymentSagaEventType.PaymentCaptured:
                await ApplyPaymentCapturedAsync(request, order, saga, updatedAtUtc, transaction, cancellationToken);
                break;
            case OrderPaymentSagaEventType.PaymentVoided:
                await ApplyPaymentVoidedAsync(request, order, saga, updatedAtUtc, transaction, cancellationToken);
                break;
            case OrderPaymentSagaEventType.PaymentRefunded:
                await ApplyPaymentRefundedAsync(request, order, saga, updatedAtUtc, transaction, cancellationToken);
                break;
            case OrderPaymentSagaEventType.PaymentSucceeded:
                await ApplyPaymentSucceededAsync(request, order, saga, updatedAtUtc, transaction, cancellationToken);
                break;
            case OrderPaymentSagaEventType.PaymentFailed:
                await ApplyPaymentFailedAsync(request, order, saga, updatedAtUtc, transaction, cancellationToken);
                break;
            case OrderPaymentSagaEventType.PaymentTimedOut:
                await ApplyPaymentTimedOutAsync(request, order, saga, updatedAtUtc, transaction, cancellationToken);
                break;
            default:
                throw new InvalidOperationException($"Unsupported payment saga event type '{request.EventType}'.");
        }
    }

    private static void ApplyPaymentCollectionPending(
        ApplyPaymentSagaEventCommand request,
        Order order,
        OrderPaymentSaga saga,
        DateTime updatedAtUtc)
    {
        if (saga.State is OrderPaymentSagaState.CashOnDeliveryRequested
            or OrderPaymentSagaState.CashOnDeliveryReadyForFulfillment)
        {
            saga.RecordIgnoredEvent(request.EventId, updatedAtUtc);
            return;
        }

        if (saga.State is OrderPaymentSagaState.OrderCancelled
            or OrderPaymentSagaState.TimedOut
            or OrderPaymentSagaState.CompensationRequired ||
            order.Status == OrderStatus.Cancelled)
        {
            saga.RecordIgnoredEvent(
                request.EventId,
                updatedAtUtc,
                "Cash-on-delivery selection was received after the order had already reached a terminal state.");
            return;
        }

        if (order.Status != OrderStatus.PendingPayment)
        {
            throw new InvalidOperationException(
                $"Cash-on-delivery can only be selected while an order is awaiting payment. Current status: {order.Status}.");
        }

        saga.MarkCashOnDeliveryRequested(request.EventId, updatedAtUtc);
    }

    private static void ApplyPaymentAuthorized(
        ApplyPaymentSagaEventCommand request,
        Order order,
        OrderPaymentSaga saga,
        DateTime updatedAtUtc)
    {
        if (saga.State is OrderPaymentSagaState.PaymentAuthorized
            or OrderPaymentSagaState.InventoryCommitted
            or OrderPaymentSagaState.CaptureRequested
            or OrderPaymentSagaState.OrderPaid
            or OrderPaymentSagaState.VoidRequested
            or OrderPaymentSagaState.RefundRequested
            or OrderPaymentSagaState.OrderRefunded
            or OrderPaymentSagaState.CompensationCompleted)
        {
            saga.RecordIgnoredEvent(request.EventId, updatedAtUtc);
            return;
        }

        if (saga.State is OrderPaymentSagaState.OrderCancelled or OrderPaymentSagaState.TimedOut ||
            order.Status == OrderStatus.Cancelled)
        {
            saga.MarkVoidRequested(
                request.EventId,
                updatedAtUtc,
                "Payment was authorized after the inventory reservation expired or the order was cancelled.");
            return;
        }

        if (saga.State == OrderPaymentSagaState.CompensationRequired)
        {
            saga.RecordIgnoredEvent(
                request.EventId,
                updatedAtUtc,
                "Payment authorization was received while manual reconciliation is required.");
            return;
        }

        saga.MarkPaymentAuthorized(request.EventId, updatedAtUtc);
    }

    private async Task ApplyPaymentCapturedAsync(
        ApplyPaymentSagaEventCommand request,
        Order order,
        OrderPaymentSaga saga,
        DateTime updatedAtUtc,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (saga.State is OrderPaymentSagaState.OrderPaid
            or OrderPaymentSagaState.RefundRequested
            or OrderPaymentSagaState.OrderRefunded
            or OrderPaymentSagaState.CompensationCompleted
            or OrderPaymentSagaState.CompensationRequired ||
            order.Status == OrderStatus.Paid)
        {
            saga.RecordIgnoredEvent(request.EventId, updatedAtUtc);
            return;
        }

        if (saga.State is OrderPaymentSagaState.OrderCancelled or OrderPaymentSagaState.TimedOut or OrderPaymentSagaState.VoidRequested ||
            order.Status == OrderStatus.Cancelled)
        {
            saga.MarkRefundRequested(
                request.EventId,
                updatedAtUtc,
                "Payment was captured after the order was cancelled or timed out.");
            return;
        }

        if (saga.State != OrderPaymentSagaState.CaptureRequested)
        {
            saga.MarkCompensationRequired(
                request.EventId,
                updatedAtUtc,
                "Payment was captured without a durable inventory-commit and capture-request transition.");
            return;
        }

        var previousStatus = order.Status;
        if (order.MarkPaid())
        {
            var updated = await _orderRepository.TryUpdateStatusAsync(
                order.Id,
                order.Status,
                [previousStatus],
                transaction,
                cancellationToken);

            if (!updated)
            {
                throw new InvalidOperationException("Order status changed before PaymentCaptured was applied.");
            }
        }

        saga.MarkOrderPaid(request.EventId, updatedAtUtc);
    }

    private async Task ApplyPaymentVoidedAsync(
        ApplyPaymentSagaEventCommand request,
        Order order,
        OrderPaymentSaga saga,
        DateTime updatedAtUtc,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (saga.State is OrderPaymentSagaState.OrderPaid
            or OrderPaymentSagaState.OrderRefunded
            or OrderPaymentSagaState.CompensationRequired
            || order.Status is OrderStatus.Paid or OrderStatus.Refunded)
        {
            saga.MarkCompensationRequired(
                request.EventId,
                updatedAtUtc,
                "Payment was voided after the order had already been paid.");
            return;
        }

        await ApplyPaymentFailedAsync(
            request with { EventType = OrderPaymentSagaEventType.PaymentFailed, FailureReason = request.FailureReason ?? "Payment authorization was voided." },
            order,
            saga,
            updatedAtUtc,
            transaction,
            cancellationToken);
    }

    private async Task ApplyPaymentRefundedAsync(
        ApplyPaymentSagaEventCommand request,
        Order order,
        OrderPaymentSaga saga,
        DateTime updatedAtUtc,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (saga.State is OrderPaymentSagaState.OrderRefunded or OrderPaymentSagaState.CompensationCompleted ||
            order.Status == OrderStatus.Refunded)
        {
            saga.RecordIgnoredEvent(request.EventId, updatedAtUtc);
            return;
        }

        if (saga.State == OrderPaymentSagaState.RefundRequested)
        {
            var compensationPreviousStatus = order.Status;
            if (order.Status is OrderStatus.Pending or OrderStatus.PendingPayment)
            {
                if (order.Cancel())
                {
                    var updated = await _orderRepository.TryUpdateStatusAsync(
                        order.Id,
                        order.Status,
                        [compensationPreviousStatus],
                        transaction,
                        cancellationToken);

                    if (!updated)
                    {
                        throw new InvalidOperationException("Order status changed before compensation refund was applied.");
                    }
                }
            }
            else if (order.Status == OrderStatus.Paid)
            {
                if (order.MarkRefunded())
                {
                    var updated = await _orderRepository.TryUpdateStatusAsync(
                        order.Id,
                        order.Status,
                        [compensationPreviousStatus],
                        transaction,
                        cancellationToken);

                    if (!updated)
                    {
                        throw new InvalidOperationException("Order status changed before compensation refund was applied.");
                    }
                }
            }

            saga.MarkCompensationCompleted(request.EventId, updatedAtUtc, request.FailureReason);
            return;
        }

        if (saga.State != OrderPaymentSagaState.OrderPaid || order.Status != OrderStatus.Paid)
        {
            saga.MarkCompensationRequired(
                request.EventId,
                updatedAtUtc,
                "Payment was refunded before the order was durably marked as paid.");
            return;
        }

        var previousStatus = order.Status;
        if (order.MarkRefunded())
        {
            var updated = await _orderRepository.TryUpdateStatusAsync(
                order.Id,
                order.Status,
                [previousStatus],
                transaction,
                cancellationToken);

            if (!updated)
            {
                throw new InvalidOperationException("Order status changed before PaymentRefunded was applied.");
            }
        }

        saga.MarkOrderRefunded(request.EventId, updatedAtUtc);
    }

    private async Task ApplyPaymentSucceededAsync(
        ApplyPaymentSagaEventCommand request,
        Order order,
        OrderPaymentSaga saga,
        DateTime updatedAtUtc,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (saga.State is OrderPaymentSagaState.OrderPaid or OrderPaymentSagaState.CompensationRequired ||
            order.Status == OrderStatus.Paid)
        {
            saga.RecordIgnoredEvent(request.EventId, updatedAtUtc);
            return;
        }

        if (saga.State is OrderPaymentSagaState.OrderCancelled or OrderPaymentSagaState.TimedOut ||
            order.Status == OrderStatus.Cancelled)
        {
            saga.MarkRefundRequested(
                request.EventId,
                updatedAtUtc,
                "Payment succeeded after the inventory reservation expired or the order was cancelled.");
            return;
        }

        var previousStatus = order.Status;
        if (order.MarkPaid())
        {
            var updated = await _orderRepository.TryUpdateStatusAsync(
                order.Id,
                order.Status,
                [previousStatus],
                transaction,
                cancellationToken);

            if (!updated)
            {
                throw new InvalidOperationException("Order status changed before PaymentSucceeded was applied.");
            }
        }

        saga.MarkOrderPaid(request.EventId, updatedAtUtc);
    }

    private async Task ApplyPaymentFailedAsync(
        ApplyPaymentSagaEventCommand request,
        Order order,
        OrderPaymentSaga saga,
        DateTime updatedAtUtc,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (saga.State is OrderPaymentSagaState.OrderPaid or OrderPaymentSagaState.CompensationRequired ||
            order.Status == OrderStatus.Paid)
        {
            saga.RecordIgnoredEvent(
                request.EventId,
                updatedAtUtc,
                "Late PaymentFailed ignored because the order is already paid.");
            return;
        }

        if (saga.State is OrderPaymentSagaState.OrderCancelled or OrderPaymentSagaState.TimedOut)
        {
            saga.RecordIgnoredEvent(request.EventId, updatedAtUtc, request.FailureReason);
            return;
        }

        var previousStatus = order.Status;
        if (order.Cancel())
        {
            var updated = await _orderRepository.TryUpdateStatusAsync(
                order.Id,
                order.Status,
                [previousStatus],
                transaction,
                cancellationToken);

            if (!updated)
            {
                throw new InvalidOperationException("Order status changed before PaymentFailed was applied.");
            }
        }

        saga.MarkOrderCancelled(
            request.EventId,
            updatedAtUtc,
            string.IsNullOrWhiteSpace(request.FailureReason) ? "Payment failed." : request.FailureReason);
    }

    private async Task ApplyPaymentTimedOutAsync(
        ApplyPaymentSagaEventCommand request,
        Order order,
        OrderPaymentSaga saga,
        DateTime updatedAtUtc,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (saga.State is OrderPaymentSagaState.OrderPaid or OrderPaymentSagaState.CompensationRequired ||
            order.Status == OrderStatus.Paid)
        {
            saga.RecordIgnoredEvent(
                request.EventId,
                updatedAtUtc,
                "Payment timeout ignored because the order is already paid.");
            return;
        }

        if (saga.State is OrderPaymentSagaState.OrderCancelled or OrderPaymentSagaState.TimedOut)
        {
            saga.RecordIgnoredEvent(request.EventId, updatedAtUtc);
            return;
        }

        if (saga.State is OrderPaymentSagaState.PaymentAuthorized or OrderPaymentSagaState.CaptureRequested)
        {
            saga.MarkVoidRequested(request.EventId, updatedAtUtc, "Payment timed out before capture completed.");
            return;
        }

        var previousStatus = order.Status;
        if (order.Cancel())
        {
            var updated = await _orderRepository.TryUpdateStatusAsync(
                order.Id,
                order.Status,
                [previousStatus],
                transaction,
                cancellationToken);

            if (!updated)
            {
                throw new InvalidOperationException("Order status changed before PaymentTimedOut was applied.");
            }
        }

        saga.MarkTimedOut(request.EventId, updatedAtUtc, "Payment timed out.");
    }
}
