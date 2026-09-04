namespace PaymentService.Application.Payments.Providers;

public static class PaymentProviderPolicy
{
    private static readonly HashSet<string> VndOnlyProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "MoMo",
        "SePay"
    };

    public static void EnsureActionIsSupported(string providerName, decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new ArgumentException("Payment provider is required.", nameof(providerName));
        }

        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Payment amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Payment currency is required.", nameof(currency));
        }

        if (!VndOnlyProviders.Contains(providerName))
        {
            return;
        }

        if (!string.Equals(currency.Trim(), "VND", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{providerName} is available only for VND payments.");
        }

        if (decimal.Truncate(amount) != amount)
        {
            throw new InvalidOperationException($"{providerName} payment amounts must be whole VND values.");
        }
    }

    public static IReadOnlyList<string> GetSupportedCurrencies(string providerName) =>
        VndOnlyProviders.Contains(providerName) ? ["VND"] : [];
}