namespace OrderingService.Domain.OrderPaymentSagas;

public enum OrderPaymentSagaState
{
    PaymentRequested = 1,
    PaymentAuthorized = 2,
    InventoryCommitted = 3,
    CaptureRequested = 4,
    OrderPaid = 5,
    OrderCancelled = 6,
    TimedOut = 7,
    CompensationRequired = 8,
    OrderRefunded = 9,
    VoidRequested = 10,
    RefundRequested = 11,
    CompensationCompleted = 12,
    CashOnDeliveryRequested = 13,
    CashOnDeliveryReadyForFulfillment = 14
}
