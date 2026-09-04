using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using PaymentService.API.Endpoints;
using PaymentService.Application.Payments.CreatePayment;
using PaymentService.Application.Payments.Webhooks;
using PaymentService.Application.Payments.Providers;
using PaymentService.Infrastructure.Clients;
using MicroShop.ServiceDefaults.Diagnostics;

namespace PaymentService.API;

public static class DependencyInjection
{
    public static IServiceCollection AddApi(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddProblemDetails();
        services.AddEndpointsApiExplorer();
        services.AddMicroShopJwtAuthentication(configuration, environment);

        var webhookOptions = configuration.GetSection(PaymentWebhookOptions.SectionName).Get<PaymentWebhookOptions>()
            ?? new PaymentWebhookOptions();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter(PaymentWebhookRateLimiter.PolicyName, limiter =>
            {
                limiter.PermitLimit = webhookOptions.PermitLimit;
                limiter.Window = TimeSpan.FromSeconds(webhookOptions.WindowSeconds);
                limiter.QueueLimit = 0;
                limiter.AutoReplenishment = true;
            });
        });

        return services;
    }

    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPaymentEndpoints();
        app.MapWebhookEndpoints();

        return app;
    }

    public static WebApplication UseApiExceptionHandling(this WebApplication app)
    {
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;

                if (exception is PaymentWebhookIntegrityException)
                {
                    await ApiProblemResults.Conflict(exception.Message, "PAYMENT_WEBHOOK_INTEGRITY_CONFLICT").ExecuteAsync(context);
                    return;
                }

                if (exception is PaymentActionIdempotencyConflictException)
                {
                    await ApiProblemResults.Conflict(exception.Message, "PAYMENT_ACTION_IDEMPOTENCY_CONFLICT").ExecuteAsync(context);
                    return;
                }

                if (exception is PaymentOrderNotAccessibleException)
                {
                    await ApiProblemResults.NotFound("Order was not found.", "ORDER_NOT_FOUND").ExecuteAsync(context);
                    return;
                }

                if (exception is ArgumentException or InvalidOperationException)
                {
                    await ApiProblemResults.BadRequest(exception.Message).ExecuteAsync(context);
                    return;
                }

                if (exception is UnauthorizedAccessException)
                {
                    await ApiProblemResults.Unauthorized().ExecuteAsync(context);
                    return;
                }

                if (exception is KeyNotFoundException)
                {
                    await ApiProblemResults.NotFound(exception.Message).ExecuteAsync(context);
                    return;
                }

                if (exception is OrderServiceUnavailableException)
                {
                    await ApiProblemResults.ServiceUnavailable(exception.Message, "ORDERING_UNAVAILABLE").ExecuteAsync(context);
                    return;
                }

                await Results.Problem().ExecuteAsync(context);
            });
        });

        return app;
    }
}

internal static class PaymentWebhookRateLimiter
{
    public const string PolicyName = "payment-webhooks";
}