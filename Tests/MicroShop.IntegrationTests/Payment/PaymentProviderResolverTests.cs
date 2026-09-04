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

        Assert.Equal(["PayPal", "Sandbox"], available.Select(provider => provider.Name));
        Assert.Contains(available, provider => provider.Name == "PayPal" && provider.RequiresRedirect);
    }

    private static PaymentProviderResolver CreateResolver(string defaultProvider, string[] enabledProviders) =>
        new(
            [new StubProvider("Sandbox"), new StubProvider("PayPal")],
            Options.Create(new PaymentProviderOptions
            {
                Provider = defaultProvider,
                EnabledProviders = enabledProviders
            }));

    private sealed class StubProvider(string name) : IPaymentProvider
    {
        public string Name => name;

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
