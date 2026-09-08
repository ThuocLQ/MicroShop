using Microsoft.Extensions.Options;
using PaymentService.Application.Payments.Providers;
using DomainPayment = PaymentService.Domain.Payments.Payment;
using PaymentService.Infrastructure.Providers;

namespace MicroShop.IntegrationTests.Payment;

public sealed class PaymentProviderResolverTests
{
    [Fact]
    public void DisabledProvider_IsNeitherListedNorResolvable()
    {
        var resolver = CreateResolver("Sandbox", ["Sandbox"]);

        var available = resolver.GetAvailableProviders();

        var provider = Assert.Single(available);
        Assert.Equal("Sandbox", provider.Name);
        Assert.Throws<ArgumentException>(() => resolver.Resolve("PayPal"));
    }

    [Fact]
    public void EnabledProvider_IsListedWithItsCapabilityMetadata()
    {
        var resolver = CreateResolver("Sandbox", ["Sandbox", "PayPal"]);

        var available = resolver.GetAvailableProviders();

        Assert.Equal(["Sandbox", "PayPal"], available.Select(provider => provider.Name));
        Assert.Contains(available, provider => provider.Name == "PayPal" && provider.RequiresRedirect && provider.SupportedCurrencies.SequenceEqual(["USD"]));
        Assert.Contains(available, provider => provider.Name == "Sandbox" && provider.SupportedCurrencies.SequenceEqual([PaymentProviderPolicy.AnyCurrency]));
    }

    [Fact]
    public void DefaultProvider_IsListedFirst_AndMoMoRemainsSecondary()
    {
        var resolver = CreateResolver("PayPal", ["Sandbox", "PayPal", "MoMo"]);

        var available = resolver.GetAvailableProviders();

        Assert.Equal(["PayPal", "MoMo", "Sandbox"], available.Select(provider => provider.Name));
    }

    private static PaymentProviderResolver CreateResolver(string defaultProvider, string[] enabledProviders) =>
        new(
            [new StubProvider("Sandbox"), new StubProvider("PayPal"), new StubProvider("MoMo")],
            Options.Create(new PaymentProviderOptions
            {
                Provider = defaultProvider,
                EnabledProviders = enabledProviders
            }));

    private sealed class StubProvider(string name) : IPaymentProvider
    {
        public string Name => name;
        public IReadOnlyList<string> SupportedCurrencies => PaymentProviderPolicy.GetSupportedCurrencies(Name);

        public Task<PaymentProviderAction> CreateActionAsync(PaymentProviderActionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PaymentProviderWebhook?> RequestCaptureAsync(DomainPayment payment, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PaymentProviderWebhook?> RequestVoidAsync(DomainPayment payment, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PaymentProviderWebhook?> RequestRefundAsync(DomainPayment payment, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
