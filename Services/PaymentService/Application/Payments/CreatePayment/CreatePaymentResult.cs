namespace PaymentService.Application.Payments.CreatePayment;

public sealed record CreatePaymentResult(
    PaymentDto Payment,
    PaymentActionDto Action,
    bool IsReplay);

public sealed record PaymentActionDto(
    string Provider,
    string SessionId,
    string? CheckoutUrl,
    string PaymentStatus,
    DateTime ExpiresAtUtc,
    bool SandboxCompletionAvailable,
    bool CashOnDelivery);