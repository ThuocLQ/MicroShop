using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using PaymentService.Application.Payments.Providers;
using PaymentService.Domain.Payments;

namespace PaymentService.Infrastructure.Providers;

public sealed class PayPalPaymentProvider : IPaymentProvider
{
    private readonly PayPalApiClient _apiClient;
    private readonly PayPalOptions _options;

    public PayPalPaymentProvider(PayPalApiClient apiClient, Microsoft.Extensions.Options.IOptions<PayPalOptions> options)
    {
        _apiClient = apiClient;
        _options = options.Value;
    }

    public string Name => "PayPal";
    public IReadOnlyList<string> SupportedCurrencies => PaymentProviderPolicy.GetConfiguredPayPalCurrencies(_options.SupportedCurrencies);

    public async Task<PaymentProviderAction> CreateActionAsync(
        PaymentProviderActionRequest request,
        CancellationToken cancellationToken = default)
    {
        PaymentProviderPolicy.EnsureActionIsSupported(Name, request.Amount, request.Currency, SupportedCurrencies);

        using var createOrder = new HttpRequestMessage(HttpMethod.Post, "v2/checkout/orders")
        {
            Content = JsonContent.Create(new
            {
                intent = "AUTHORIZE",
                purchase_units = new[]
                {
                    new
                    {
                        reference_id = request.PaymentId.ToString("D"),
                        custom_id = request.PaymentId.ToString("D"),
                        invoice_id = $"microshop-{request.OrderId:N}",
                        amount = new
                        {
                            currency_code = request.Currency.Trim().ToUpperInvariant(),
                            value = request.Amount.ToString("0.00", CultureInfo.InvariantCulture)
                        }
                    }
                },
                application_context = new
                {
                    return_url = AppendPaymentReference(_options.ReturnUrl, request.PaymentId),
                    cancel_url = AppendPaymentReference(_options.CancelUrl, request.PaymentId),
                    user_action = "PAY_NOW",
                    shipping_preference = "NO_SHIPPING"
                }
            })
        };
        createOrder.Headers.TryAddWithoutValidation("PayPal-Request-Id", $"ms-{request.PaymentId:N}"[..25]);

        using var response = await _apiClient.SendAuthorizedAsync(createOrder, cancellationToken);
        var sessionId = ReadRequiredString(response.RootElement, "id");
        var checkoutUrl = response.RootElement.TryGetProperty("links", out var links)
            ? links.EnumerateArray()
                .Where(link => link.TryGetProperty("rel", out var rel) &&
                               (string.Equals(rel.GetString(), "approve", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(rel.GetString(), "payer-action", StringComparison.OrdinalIgnoreCase)))
                .Select(link => link.TryGetProperty("href", out var href) ? href.GetString() : null)
                .FirstOrDefault(IsValidPayPalCheckoutUrl)
            : null;

        if (string.IsNullOrWhiteSpace(checkoutUrl))
        {
            throw new InvalidOperationException("The payment provider did not return an approval URL.");
        }

        return new PaymentProviderAction(
            Name,
            sessionId,
            checkoutUrl,
            DateTime.UtcNow.AddMinutes(_options.ActionExpiryMinutes));
    }

    public async Task<PaymentProviderWebhook?> RequestCaptureAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        var authorizationId = RequireProviderTransactionId(payment, "capture");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture")
        {
            Content = JsonContent.Create(new { })
        };
        using var _ = await _apiClient.SendAuthorizedAsync(request, cancellationToken);
        return null;
    }

    public async Task<PaymentProviderWebhook?> RequestVoidAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        var authorizationId = RequireProviderTransactionId(payment, "void");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void")
        {
            Content = JsonContent.Create(new { })
        };
        using var _ = await _apiClient.SendAuthorizedAsync(request, cancellationToken);
        return null;
    }

    public async Task<PaymentProviderWebhook?> RequestRefundAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        var captureId = RequireProviderTransactionId(payment, "refund");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund")
        {
            Content = JsonContent.Create(new { })
        };
        using var _ = await _apiClient.SendAuthorizedAsync(request, cancellationToken);
        return null;
    }

    private static string RequireProviderTransactionId(Payment payment, string action)
    {
        if (!string.Equals(payment.Provider, "PayPal", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(payment.ProviderTransactionId))
        {
            throw new InvalidOperationException($"PayPal {action} requires a provider transaction id.");
        }

        return payment.ProviderTransactionId;
    }

    private static string AppendPaymentReference(string baseUrl, Guid paymentId)
    {
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{baseUrl}{separator}paymentId={Uri.EscapeDataString(paymentId.ToString("D"))}&provider=paypal";
    }

    private static bool IsValidPayPalCheckoutUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               (uri.Host.Equals("paypal.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.EndsWith(".paypal.com", StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadRequiredString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidOperationException($"The payment provider did not return '{name}'.");
        }

        return value.GetString()!;
    }
}
