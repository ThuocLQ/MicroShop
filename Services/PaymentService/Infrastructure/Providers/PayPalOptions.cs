namespace PaymentService.Infrastructure.Providers;

public sealed class PayPalOptions
{
    public const string SectionName = "PaymentProvider:PayPal";

    public bool Enabled { get; init; }
    public bool UseSandbox { get; init; } = true;
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string WebhookId { get; init; } = string.Empty;
    public string ReturnUrl { get; init; } = string.Empty;
    public string CancelUrl { get; init; } = string.Empty;
    public string[] SupportedCurrencies { get; init; } = ["USD"];
    public int ActionExpiryMinutes { get; init; } = 30;
}
