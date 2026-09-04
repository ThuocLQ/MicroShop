namespace PaymentService.Application.Payments.Webhooks;

public sealed class PaymentWebhookOptions
{
    public const string SectionName = "PaymentWebhooks";

    public string SignatureHeaderName { get; init; } = "X-MicroShop-Signature";
    public string SharedSecret { get; init; } = string.Empty;
    public bool RequireSignature { get; init; } = true;
    public int MaxBodyBytes { get; init; } = 64 * 1024;
    public int PermitLimit { get; init; } = 60;
    public int WindowSeconds { get; init; } = 60;
}