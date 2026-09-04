using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using PaymentService.Application.Abstractions;
using PaymentService.Application.Payments;
using PaymentService.Application.Payments.Webhooks;

namespace PaymentService.Infrastructure.Providers;

public sealed class MoMoWebhookProcessor : IMoMoWebhookProcessor
{
    private readonly MoMoOptions _options;
    private readonly IPaymentRepository _payments;
    private readonly IPaymentWebhookRepository _webhooks;
    private readonly ISender _sender;

    public MoMoWebhookProcessor(
        IOptions<MoMoOptions> options,
        IPaymentRepository payments,
        IPaymentWebhookRepository webhooks,
        ISender sender)
    {
        _options = options.Value;
        _payments = payments;
        _webhooks = webhooks;
        _sender = sender;
    }

    public async Task<PaymentWebhookProcessingResult> ProcessAsync(
        IHeaderDictionary headers,
        string rawBody,
        CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(rawBody);
        var payload = MoMoIpnPayload.Read(document.RootElement);
        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();
        var providerEventId = $"momo:{payload.TransId}:{payload.ResultCode}";
        var payment = await _payments.GetByProviderSessionIdAsync("MoMo", payload.OrderId, cancellationToken);

        if (payment is null)
        {
            return new PaymentWebhookProcessingResult(null, IsDuplicate: false);
        }

        if (!VerifySignature(payload))
        {
            await RecordRejectedAsync(payment.Id, providerEventId, payload, payloadHash, "Invalid MoMo IPN signature.", cancellationToken);
            throw new UnauthorizedAccessException("MoMo IPN signature verification failed.");
        }

        if (!string.Equals(payload.PartnerCode, _options.PartnerCode, StringComparison.Ordinal) ||
            !string.Equals(payment.Currency, "VND", StringComparison.OrdinalIgnoreCase) ||
            payment.Amount != payload.Amount)
        {
            await RecordRejectedAsync(payment.Id, providerEventId, payload, payloadHash, "MoMo IPN does not match the stored payment.", cancellationToken);
            throw new PaymentWebhookIntegrityException(providerEventId);
        }

        var transactionId = payload.TransId.ToString(CultureInfo.InvariantCulture);
        if (payload.ResultCode == 0)
        {
            await _sender.Send(new PaymentWebhookCommand(
                payment.Id,
                $"{providerEventId}:authorized",
                transactionId,
                "AUTHORIZED",
                null,
                payloadHash,
                "Verified"), cancellationToken);
        }

        var result = await _sender.Send(new PaymentWebhookCommand(
            payment.Id,
            payload.ResultCode == 0 ? $"{providerEventId}:captured" : providerEventId,
            transactionId,
            payload.ResultCode == 0 ? "CAPTURED" : "FAILED",
            payload.ResultCode == 0 ? null : payload.Message,
            payloadHash,
            "Verified"), cancellationToken);

        return new PaymentWebhookProcessingResult(
            result.Payment is null ? null : PaymentMapper.ToDto(result.Payment),
            result.IsDuplicate);
    }

    private bool VerifySignature(MoMoIpnPayload payload)
    {
        var canonical = $"accessKey={_options.AccessKey}&amount={payload.Amount}&extraData={payload.ExtraData}&message={payload.Message}&orderId={payload.OrderId}&orderInfo={payload.OrderInfo}&orderType={payload.OrderType}&partnerCode={payload.PartnerCode}&payType={payload.PayType}&requestId={payload.RequestId}&responseTime={payload.ResponseTime}&resultCode={payload.ResultCode}&transId={payload.TransId}";
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.SecretKey), Encoding.UTF8.GetBytes(canonical));
        return TryDecodeHex(payload.Signature, out var actual) && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private Task RecordRejectedAsync(Guid paymentId, string providerEventId, MoMoIpnPayload payload, string payloadHash, string error, CancellationToken cancellationToken) =>
        _webhooks.RecordRejectedAsync(
            providerEventId,
            paymentId,
            payload.TransId.ToString(CultureInfo.InvariantCulture),
            payload.ResultCode == 0 ? "CAPTURED" : "FAILED",
            payloadHash,
            "Rejected",
            error,
            DateTime.UtcNow,
            cancellationToken);

    private static bool TryDecodeHex(string value, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromHexString(value);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    private sealed record MoMoIpnPayload(
        string PartnerCode,
        string OrderId,
        string RequestId,
        long Amount,
        string OrderInfo,
        string OrderType,
        long TransId,
        int ResultCode,
        string Message,
        string PayType,
        long ResponseTime,
        string ExtraData,
        string Signature)
    {
        public static MoMoIpnPayload Read(JsonElement root) => new(
            ReadString(root, "partnerCode"), ReadString(root, "orderId"), ReadString(root, "requestId"),
            ReadLong(root, "amount"), ReadString(root, "orderInfo"), ReadString(root, "orderType"),
            ReadLong(root, "transId"), ReadInt(root, "resultCode"), ReadString(root, "message"),
            ReadString(root, "payType"), ReadLong(root, "responseTime"), ReadString(root, "extraData", allowEmpty: true),
            ReadString(root, "signature"));

        private static string ReadString(JsonElement root, string name, bool allowEmpty = false)
        {
            if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || (!allowEmpty && string.IsNullOrWhiteSpace(value.GetString())))
            {
                throw new ArgumentException($"MoMo IPN field '{name}' is required.");
            }

            return value.GetString() ?? string.Empty;
        }

        private static long ReadLong(JsonElement root, string name) =>
            root.TryGetProperty(name, out var value) && value.TryGetInt64(out var result)
                ? result : throw new ArgumentException($"MoMo IPN field '{name}' is required.");

        private static int ReadInt(JsonElement root, string name) =>
            root.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
                ? result : throw new ArgumentException($"MoMo IPN field '{name}' is required.");
    }
}