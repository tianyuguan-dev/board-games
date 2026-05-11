using System.Collections.Concurrent;
using BoardGames.Dtos.BlackJack;
using BoardGames.Hubs.BlackJack;
using BoardGames.Models.BlackJack;
using Microsoft.AspNetCore.SignalR;

namespace BoardGames.Services.BlackJack;

public class TurnTimerService(IHubContext<BlackJackHub> hubContext, IBlackJackRoomManager roomManager)
    : ITurnTimerService
{
    public const int TurnTimeSeconds = 10;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _timers = new();

    public void StartTurnTimer(string roomId)
    {
        CancelTurnTimer(roomId);
        var cts = new CancellationTokenSource();
        _timers[roomId] = cts;
        _ = RunTimerAsync(roomId, cts.Token);
    }

    public void CancelTurnTimer(string roomId)
    {
        if (_timers.TryRemove(roomId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private async Task RunTimerAsync(string roomId, CancellationToken ct)
    {
        try
        {
            await Task.Delay(TurnTimeSeconds * 1000, ct);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        _timers.TryRemove(roomId, out _);

        var room = roomManager.GetRoom(roomId);
        if (room?.BlackJackGame == null || room.BlackJackGame.State != BlackJackGameState.PlayerTurn)
            return;

        room.BlackJackGame.Stand();

        var dto = new BlackJackGameStateDto(room.BlackJackGame);
        dto.PlayerNames = room.GamePlayerNames;
        dto.TotalCards = room.BlackJackTable.TotalCards;
        dto.CardsRemaining = room.BlackJackTable.CardsRemaining;
        dto.ReshuffleThreshold = room.BlackJackTable.ReshuffleThreshold;
        await hubContext.Clients.Group(roomId).SendAsync("PlayerStand", dto);

        if (room.BlackJackGame.State == BlackJackGameState.PlayerTurn)
        {
            StartTurnTimer(roomId);
        }
        else
        {
            await BroadcastRoomPlayers(room);
        }
    }

    private async Task BroadcastRoomPlayers(BlackJackRoom room)
    {
        var players = room.Players
            .OrderBy(p => p.Value)
            .Select(p => new
            {
                Nickname = room.PlayerNicknames.GetValueOrDefault(p.Key, "Player " + p.Value),
                IsReady = room.ReadyPlayers.Contains(p.Key),
                IsHost = p.Key == room.HostConnectionId,
                SeatIndex = p.Value
            })
            .ToList();
        foreach (var connId in room.Players.Keys)
        {
            await hubContext.Clients.Client(connId).SendAsync("RoomUpdate",
                new { Players = players, IsHost = connId == room.HostConnectionId });
        }
    }
}
