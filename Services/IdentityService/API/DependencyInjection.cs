using IdentityService.API.Endpoints;
using Microsoft.AspNetCore.Diagnostics;

namespace IdentityService.API;

public static class DependencyInjection
{
    public static IServiceCollection AddApi(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddEndpointsApiExplorer();
        services.AddAuthorization();

        return services;
    }

    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapAuthEndpoints();
        app.MapEmailVerificationEndpoints();
        app.MapAddressEndpoints();
        app.MapNotificationPreferenceEndpoints();
        app.MapSavedItemEndpoints();
        app.MapInternalAddressEndpoints();
        app.MapInternalCustomerContactEndpoints();

        return app;
    }

    public static WebApplication UseApiExceptionHandling(this WebApplication app)
    {
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;

                if (exception is InvalidOperationException)
                {
                    await Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                        .ExecuteAsync(context);
                    return;
                }

                await Results.Problem().ExecuteAsync(context);
            });
        });

        return app;
    }
}
