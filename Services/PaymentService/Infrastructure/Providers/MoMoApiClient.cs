using System.Net.Http.Json;
using System.Text.Json;

namespace PaymentService.Infrastructure.Providers;

public sealed class MoMoApiClient
{
    public const string HttpClientName = "MoMo";

    private readonly IHttpClientFactory _httpClientFactory;

    public MoMoApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<JsonDocument> CreatePaymentAsync(object request, CancellationToken cancellationToken)
    {
        using var response = await _httpClientFactory.CreateClient(HttpClientName).PostAsJsonAsync(
            "v2/gateway/api/create",
            request,
            cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("MoMo could not process the payment creation request.");
        }

        return JsonDocument.Parse(content);
    }
}
