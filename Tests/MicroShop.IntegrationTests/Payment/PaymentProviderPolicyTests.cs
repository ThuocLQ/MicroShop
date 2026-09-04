using PaymentService.Application.Payments.Providers;

namespace MicroShop.IntegrationTests.Payment;

public sealed class PaymentProviderPolicyTests
{
    [Theory]
    [InlineData("MoMo", 125_000.50, "VND")]
    [InlineData("SePay", 125_000, "USD")]
    public void VietnameseProvider_RejectsUnsupportedVndIntent(string providerName, decimal amount, string currency)
    {
        Assert.Throws<InvalidOperationException>(() =>
            PaymentProviderPolicy.EnsureActionIsSupported(providerName, amount, currency));
    }

    [Theory]
    [InlineData("MoMo")]
    [InlineData("SePay")]
    public void VietnameseProvider_AdvertisesVndOnly(string providerName)
    {
        Assert.Equal(["VND"], PaymentProviderPolicy.GetSupportedCurrencies(providerName));
    }

    [Fact]
    public void Sandbox_DoesNotInheritVietnameseProviderRestrictions()
    {
        PaymentProviderPolicy.EnsureActionIsSupported("Sandbox", 12.34m, "USD");
    }
}