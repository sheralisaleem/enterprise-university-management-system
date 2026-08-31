using Microsoft.AspNetCore.SignalR;

namespace BackendApi.Hubs;

public class DashboardHub : Hub
{
    public async Task JoinRole(string role) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, $"role:{role}");

    public async Task JoinEvent(int eventId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, $"event:{eventId}");

    public static string RoleGroup(string role) => $"role:{role}";
    public static string EventGroup(int eventId) => $"event:{eventId}";
}
