using System.Security.Claims;
using IdentityService.Application.SavedItems;
using Microsoft.AspNetCore.Authorization;

namespace IdentityService.API.Endpoints;

public static class SavedItemEndpoints
{
    public static IEndpointRouteBuilder MapSavedItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/me/saved-items").WithTags("Saved items").RequireAuthorization();

        group.MapGet("", async (ClaimsPrincipal principal, SavedItemService service, CancellationToken cancellationToken) =>
        {
            return TryGetCustomerId(principal, out var customerId)
                ? Results.Ok((await service.GetAsync(customerId, cancellationToken)).Select(item => new { item.ProductId, item.CreatedAtUtc }))
                : Results.Unauthorized();
        });

        group.MapPost("", async (SaveItemRequest request, ClaimsPrincipal principal, SavedItemService service, CancellationToken cancellationToken) =>
        {
            if (!TryGetCustomerId(principal, out var customerId)) return Results.Unauthorized();
            if (request.ProductId == Guid.Empty) return Results.ValidationProblem(new Dictionary<string, string[]> { ["productId"] = ["Product id is required."] });
            var created = await service.SaveAsync(customerId, request.ProductId, cancellationToken);
            return created
                ? Results.Created($"/me/saved-items/{request.ProductId:D}", new { request.ProductId })
                : Results.NoContent();
        });

        group.MapDelete("/{productId:guid}", async (Guid productId, ClaimsPrincipal principal, SavedItemService service, CancellationToken cancellationToken) =>
        {
            return !TryGetCustomerId(principal, out var customerId)
                ? Results.Unauthorized()
                : await service.RemoveAsync(customerId, productId, cancellationToken) ? Results.NoContent() : Results.NotFound();
        });

        return app;
    }

    private static bool TryGetCustomerId(ClaimsPrincipal principal, out Guid customerId) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub"), out customerId);

    private sealed record SaveItemRequest(Guid ProductId);
}