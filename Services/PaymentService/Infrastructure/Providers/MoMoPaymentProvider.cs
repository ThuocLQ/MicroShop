using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PaymentService.Application.Payments.Providers;
using PaymentService.Domain.Payments;

namespace PaymentService.Infrastructure.Providers;

public sealed class MoMoPaymentProvider : IPaymentProvider
{
    private readonly MoMoApiClient _apiClient;
    private readonly MoMoOptions _options;

    public MoMoPaymentProvider(MoMoApiClient apiClient, IOptions<MoMoOptions> options)
    {
        _apiClient = apiClient;
        _options = options.Value;
    }

    public string Name => "MoMo";

    public async Task<PaymentProviderAction> CreateActionAsync(
        PaymentProviderActionRequest request,
        CancellationToken cancellationToken = default)
    {
        PaymentProviderPolicy.EnsureActionIsSupported(Name, request.Amount, request.Currency);

        var amount = decimal.ToInt64(request.Amount);
        var merchantOrderId = $"ms-{request.PaymentId:N}";
        var requestId = merchantOrderId;
        var orderInfo = $"MicroShop payment {request.OrderId:N}";
        const string requestType = "payWithMethod";
        const string extraData = "";
        var redirectUrl = AppendPaymentReference(_options.RedirectUrl, request.PaymentId);
        var signature = Sign($"accessKey={_options.AccessKey}&amount={amount}&extraData={extraData}&ipnUrl={_options.IpnUrl}&orderId={merchantOrderId}&orderInfo={orderInfo}&partnerCode={_options.PartnerCode}&redirectUrl={redirectUrl}&requestId={requestId}&requestType={requestType}");

        using var document = await _apiClient.CreatePaymentAsync(new
        {
            partnerCode = _options.PartnerCode,
            partnerName = "MicroShop",
            storeId = "MicroShop",
            requestId,
            amount,
            orderId = merchantOrderId,
            orderInfo,
            redirectUrl,
            ipnUrl = _options.IpnUrl,
            requestType,
            extraData,
            lang = "vi",
            autoCapture = true,
            signature
        }, cancellationToken);

        var root = document.RootElement;
        var resultCode = ReadRequiredInt(root, "resultCode");
        if (resultCode != 0)
        {
            throw new InvalidOperationException("MoMo rejected the payment creation request.");
        }

        if (!string.Equals(ReadRequiredString(root, "orderId"), merchantOrderId, StringComparison.Ordinal) ||
            !string.Equals(ReadRequiredString(root, "requestId"), requestId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("MoMo returned an unexpected payment reference.");
        }

        var checkoutUrl = ReadRequiredString(root, "payUrl");
        if (!Uri.TryCreate(checkoutUrl, UriKind.Absolute, out var checkoutUri) ||
            !string.Equals(checkoutUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("MoMo returned an invalid checkout URL.");
        }

        return new PaymentProviderAction(
            Name,
            merchantOrderId,
            checkoutUri.ToString(),
            DateTime.UtcNow.AddMinutes(_options.ActionExpiryMinutes));
    }

    public Task<PaymentProviderWebhook?> RequestCaptureAsync(Payment payment, CancellationToken cancellationToken = default) =>
        UnsupportedLifecycleOperation("capture");

    public Task<PaymentProviderWebhook?> RequestVoidAsync(Payment payment, CancellationToken cancellationToken = default) =>
        UnsupportedLifecycleOperation("void");

    public Task<PaymentProviderWebhook?> RequestRefundAsync(Payment payment, CancellationToken cancellationToken = default) =>
        UnsupportedLifecycleOperation("refund");

    private string Sign(string value) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.SecretKey), Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static Task<PaymentProviderWebhook?> UnsupportedLifecycleOperation(string operation) =>
        Task.FromException<PaymentProviderWebhook?>(new NotSupportedException(
            $"MoMo {operation} is not enabled. Confirm the merchant product contract and implement its provider API before enabling this operation."));

    private static string AppendPaymentReference(string baseUrl, Guid paymentId)
    {
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{baseUrl}{separator}paymentId={Uri.EscapeDataString(paymentId.ToString("D"))}&provider=momo";
    }

    private static string ReadRequiredString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidOperationException($"MoMo did not return '{name}'.");
        }

        return value.GetString()!;
    }

    private static int ReadRequiredInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result))
        {
            throw new InvalidOperationException($"MoMo did not return a valid '{name}'.");
        }

        return result;
    }
}
