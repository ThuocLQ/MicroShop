namespace PaymentService.Domain.Payments;

public enum PaymentStatus
{
    PendingAuthorization = 1,
    Authorized = 2,
    CapturePending = 3,
    Captured = 4,
    VoidPending = 5,
    Voided = 6,
    RefundPending = 7,
    Refunded = 8,
    Failed = 9,
    ReconciliationRequired = 10,
    AwaitingCollection = 11,

    // Kept for persisted legacy values and callers during the lifecycle migration.
    Pending = PendingAuthorization,
    Succeeded = Captured
}
