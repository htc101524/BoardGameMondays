using Microsoft.AspNetCore.SignalR;

namespace BoardGameMondays.Core;

/// <summary>
/// SignalR hub for broadcasting real-time game night updates.
/// </summary>
public sealed class GameNightHub : Hub
{
    /// <summary>
    /// Client method name for odds updates.
    /// Clients should implement: ReceiveOddsUpdate(Guid gameNightId, int gameNightGameId)
    /// </summary>
    public const string ReceiveOddsUpdate = "ReceiveOddsUpdate";

    /// <summary>
    /// Broadcasts an odds update to all clients viewing a specific game night.
    /// </summary>
    public static async Task BroadcastOddsUpdateAsync(
        IHubContext<GameNightHub> hubContext,
        Guid gameNightId,
        int gameNightGameId)
    {
        await hubContext.Clients.All.SendAsync(ReceiveOddsUpdate, gameNightId, gameNightGameId);
    }
}
