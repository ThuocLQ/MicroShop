namespace PaymentService.Infrastructure.Providers;

public sealed class MoMoOptions
{
    public const string SectionName = "PaymentProvider:MoMo";

    public bool Enabled { get; init; }
    public bool UseSandbox { get; init; } = true;
    public string PartnerCode { get; init; } = string.Empty;
    public string AccessKey { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;
    public string RedirectUrl { get; init; } = string.Empty;
    public string IpnUrl { get; init; } = string.Empty;
    public int ActionExpiryMinutes { get; init; } = 30;
}
