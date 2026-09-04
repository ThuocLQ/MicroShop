using System.Buffers;
using System.Text;
using Microsoft.Extensions.Options;
using MicroShop.ServiceDefaults.Diagnostics;
using PaymentService.API;
using PaymentService.Application.Payments.Webhooks;

namespace PaymentService.API.Endpoints;

public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/webhooks")
            .WithTags("Webhooks");

        group.MapPost("/payment", HandlePaymentWebhookAsync)
            .RequireRateLimiting(PaymentWebhookRateLimiter.PolicyName);
        app.MapPost("/payments/webhooks/payment", HandlePaymentWebhookAsync)
            .WithTags("Webhooks")
            .RequireRateLimiting(PaymentWebhookRateLimiter.PolicyName);

        if (app.ServiceProvider.GetService<IPayPalWebhookProcessor>() is not null)
        {
            group.MapPost("/paypal", HandlePayPalWebhookAsync)
                .WithSummary("Receive verified PayPal payment lifecycle events")
                .RequireRateLimiting(PaymentWebhookRateLimiter.PolicyName);
        }
        if (app.ServiceProvider.GetService<IMoMoWebhookProcessor>() is not null)
        {
            group.MapPost("/momo", HandleMoMoWebhookAsync)
                .WithSummary("Receive verified MoMo IPN payment events")
                .RequireRateLimiting(PaymentWebhookRateLimiter.PolicyName);
        }

        return app;
    }

    private static async Task<IResult> HandlePaymentWebhookAsync(
        HttpRequest httpRequest,
        IOptions<PaymentWebhookOptions> options,
        IPaymentWebhookProcessor processor,
        CancellationToken cancellationToken)
    {
        var rawBody = await TryReadRawBodyAsync(httpRequest, options.Value.MaxBodyBytes, cancellationToken);
        if (rawBody.Error is not null)
        {
            return rawBody.Error;
        }

        httpRequest.Headers.TryGetValue(options.Value.SignatureHeaderName, out var signature);
        PaymentWebhookProcessingResult result;
        try
        {
            result = await processor.ProcessAsync(rawBody.Value!, signature.ToString(), cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return ApiProblemResults.BadRequest(exception.Message, "PAYMENT_WEBHOOK_INVALID");
        }

        return result.Payment is null
            ? ApiProblemResults.NotFound("Payment was not found.", "PAYMENT_NOT_FOUND")
            : Results.Ok(result.Payment);
    }

    private static async Task<IResult> HandlePayPalWebhookAsync(
        HttpRequest httpRequest,
        IOptions<PaymentWebhookOptions> options,
        IPayPalWebhookProcessor processor,
        CancellationToken cancellationToken)
    {
        var rawBody = await TryReadRawBodyAsync(httpRequest, options.Value.MaxBodyBytes, cancellationToken);
        if (rawBody.Error is not null)
        {
            return rawBody.Error;
        }

        var result = await processor.ProcessAsync(httpRequest.Headers, rawBody.Value!, cancellationToken);
        return result.Payment is null ? Results.NoContent() : Results.Ok(result.Payment);
    }

    private static async Task<IResult> HandleMoMoWebhookAsync(
        HttpRequest httpRequest,
        IOptions<PaymentWebhookOptions> options,
        IMoMoWebhookProcessor processor,
        CancellationToken cancellationToken)
    {
        var rawBody = await TryReadRawBodyAsync(httpRequest, options.Value.MaxBodyBytes, cancellationToken);
        if (rawBody.Error is not null)
        {
            return rawBody.Error;
        }

        await processor.ProcessAsync(httpRequest.Headers, rawBody.Value!, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<WebhookBodyReadResult> TryReadRawBodyAsync(
        HttpRequest request,
        int maxBodyBytes,
        CancellationToken cancellationToken)
    {
        if (!request.HasJsonContentType())
        {
            return new WebhookBodyReadResult(null, Results.Problem(
                statusCode: StatusCodes.Status415UnsupportedMediaType,
                title: "Unsupported webhook content type",
                type: "https://microshop.dev/problems/webhook-content-type"));
        }

        if (request.ContentLength is long declaredLength && declaredLength > maxBodyBytes)
        {
            return new WebhookBodyReadResult(null, Results.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Webhook payload is too large",
                type: "https://microshop.dev/problems/webhook-payload-too-large"));
        }

        await using var body = new MemoryStream(capacity: Math.Min(maxBodyBytes, 8 * 1024));
        var buffer = ArrayPool<byte>.Shared.Rent(8 * 1024);
        try
        {
            int read;
            while ((read = await request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                if (body.Length + read > maxBodyBytes)
                {
                    return new WebhookBodyReadResult(null, Results.Problem(
                        statusCode: StatusCodes.Status413PayloadTooLarge,
                        title: "Webhook payload is too large",
                        type: "https://microshop.dev/problems/webhook-payload-too-large"));
                }

                await body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            return new WebhookBodyReadResult(
                Encoding.UTF8.GetString(body.GetBuffer(), 0, checked((int)body.Length)),
                null);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private sealed record WebhookBodyReadResult(string? Value, IResult? Error);
}