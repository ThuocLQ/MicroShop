using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NotificationWorker.Application.Realtime;

namespace NotificationWorker.Infrastructure.Realtime;

public interface ICustomerUpdatesClient
{
    Task ReceiveCustomerUpdate(CustomerRealtimeUpdate update);
}

[Authorize]
public sealed class CustomerUpdatesHub : Hub<ICustomerUpdatesClient>
{
    public override async Task OnConnectedAsync()
    {
        if (!TryGetCustomerId(Context.User, out var customerId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(customerId));
        await base.OnConnectedAsync();
    }

    public static string GroupName(Guid customerId) => $"customer:{customerId:D}";

    private static bool TryGetCustomerId(ClaimsPrincipal? principal, out Guid customerId) =>
        Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal?.FindFirstValue("sub"), out customerId);
}