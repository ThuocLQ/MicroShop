namespace PaymentService.Application.Payments.Providers;

public static class PaymentProviderPolicy
{
    public const string AnyCurrency = "*";

    private static readonly HashSet<string> PayPalCheckoutCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "AUD", "BRL", "CAD", "CNY", "CZK", "DKK", "EUR", "GBP", "HKD", "HUF", "ILS", "JPY",
        "MYR", "MXN", "NOK", "NZD", "PHP", "PLN", "SGD", "SEK", "CHF", "THB", "TWD", "USD"
    };

    private static readonly HashSet<string> VndOnlyProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "MoMo",
        "SePay"
    };

    public static void EnsureActionIsSupported(
        string providerName,
        decimal amount,
        string currency,
        IReadOnlyCollection<string>? supportedCurrencies = null)
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

        var normalizedCurrency = currency.Trim().ToUpperInvariant();
        var configuredCurrencies = supportedCurrencies ?? GetSupportedCurrencies(providerName);
        if (!SupportsCurrency(configuredCurrencies, normalizedCurrency))
        {
            throw new InvalidOperationException($"{providerName} is unavailable for {normalizedCurrency} payments.");
        }

        if (!VndOnlyProviders.Contains(providerName))
        {
            return;
        }

        if (!string.Equals(normalizedCurrency, "VND", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{providerName} is available only for VND payments.");
        }

        if (decimal.Truncate(amount) != amount)
        {
            throw new InvalidOperationException($"{providerName} payment amounts must be whole VND values.");
        }
    }

    public static IReadOnlyList<string> GetSupportedCurrencies(string providerName) =>
        VndOnlyProviders.Contains(providerName) ? ["VND"] :
        string.Equals(providerName, "PayPal", StringComparison.OrdinalIgnoreCase) ? ["USD"] :
        [AnyCurrency];

    public static IReadOnlyList<string> GetConfiguredPayPalCurrencies(IEnumerable<string>? configuredCurrencies)
    {
        var currencies = (configuredCurrencies ?? [])
            .Where(currency => !string.IsNullOrWhiteSpace(currency))
            .Select(currency => currency.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (currencies.Length == 0 || currencies.Any(currency => !PayPalCheckoutCurrencies.Contains(currency)))
        {
            throw new InvalidOperationException("PayPal supported currencies must be a non-empty subset of the supported PayPal Checkout currency codes.");
        }

        return currencies;
    }

    public static bool SupportsCurrency(IEnumerable<string> supportedCurrencies, string currency) =>
        supportedCurrencies.Any(value =>
            string.Equals(value, AnyCurrency, StringComparison.Ordinal) ||
            string.Equals(value, currency, StringComparison.OrdinalIgnoreCase));

    public static int GetDisplayOrder(string providerName) =>
        providerName.ToUpperInvariant() switch
        {
            "PAYPAL" => 10,
            "SEPAY" => 20,
            "MOMO" => 30,
            "SANDBOX" => 90,
            _ => 50
        };
}
