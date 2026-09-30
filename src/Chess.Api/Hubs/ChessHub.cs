using Microsoft.AspNetCore.SignalR;

namespace Chess.Api.Hubs;

public sealed class ChessHub : Hub
{
    public static string GroupName(Guid gameId) => $"game:{gameId:N}";

    public async Task JoinGame(string gameId)
    {
        if (!Guid.TryParse(gameId, out var id))
        {
            throw new HubException("Game id is invalid.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(id));
    }

    public async Task LeaveGame(string gameId)
    {
        if (Guid.TryParse(gameId, out var id))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(id));
        }
    }
}
