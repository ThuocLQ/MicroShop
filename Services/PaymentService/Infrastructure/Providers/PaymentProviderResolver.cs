using Microsoft.Extensions.Options;
using PaymentService.Application.Payments.Providers;

namespace PaymentService.Infrastructure.Providers;

public sealed class PaymentProviderResolver : IPaymentProviderResolver
{
    private readonly IReadOnlyDictionary<string, IPaymentProvider> _providers;
    private readonly PaymentProviderOptions _options;

    public PaymentProviderResolver(
        IEnumerable<IPaymentProvider> providers,
        IOptions<PaymentProviderOptions> options)
    {
        _options = options.Value;
        var enabledProviderNames = _options.GetEnabledProviderNames();
        _providers = providers
            .Where(provider => enabledProviderNames.Contains(provider.Name))
            .ToDictionary(provider => provider.Name, StringComparer.OrdinalIgnoreCase);

        if (_providers.Count == 0)
        {
            throw new InvalidOperationException("At least one enabled payment provider must be registered.");
        }

        if (!_providers.ContainsKey(_options.Provider))
        {
            throw new InvalidOperationException("The configured default payment provider is not registered and enabled.");
        }
    }

    public IPaymentProvider Resolve(string? providerName)
    {
        var normalizedName = string.IsNullOrWhiteSpace(providerName)
            ? _options.Provider
            : providerName.Trim();

        if (!_providers.TryGetValue(normalizedName, out var provider))
        {
            throw new ArgumentException("The requested payment method is unavailable.", nameof(providerName));
        }

        return provider;
    }

    public IReadOnlyList<PaymentProviderDescriptor> GetAvailableProviders() =>
        _providers.Values
            .OrderBy(provider => provider.Name, StringComparer.OrdinalIgnoreCase)
            .Select(provider => new PaymentProviderDescriptor(
                provider.Name,
                string.Equals(provider.Name, "Sandbox", StringComparison.OrdinalIgnoreCase),
                !string.Equals(provider.Name, "Sandbox", StringComparison.OrdinalIgnoreCase),
                PaymentProviderPolicy.GetSupportedCurrencies(provider.Name)))
            .ToList();
}