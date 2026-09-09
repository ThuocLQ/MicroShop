namespace PaymentService.Application.Payments.Providers;

public sealed class PaymentProviderOptions
{
    public const string SectionName = "PaymentProvider";

    // Sandbox is permitted only when configuration explicitly enables it.
    public string Provider { get; init; } = string.Empty;
    public string[] EnabledProviders { get; init; } = [];
    public bool AllowSandbox { get; init; }
    public int SandboxActionExpiryMinutes { get; init; } = 30;

    public IReadOnlySet<string> GetEnabledProviderNames()
    {
        var configured = EnabledProviders
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (configured.Count == 0 && !string.IsNullOrWhiteSpace(Provider))
        {
            configured.Add(Provider.Trim());
        }

        return configured;
    }
}
