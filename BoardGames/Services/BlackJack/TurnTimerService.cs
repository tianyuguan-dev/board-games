using System.Collections.Concurrent;
using BoardGames.Data;
using BoardGames.Dtos.BlackJack;
using BoardGames.Hubs.BlackJack;
using BoardGames.Models;
using BoardGames.Models.BlackJack;
using Microsoft.AspNetCore.SignalR;

namespace BoardGames.Services.BlackJack;

public class TurnTimerService(
    IHubContext<BlackJackHub> hubContext,
    IBlackJackRoomManager roomManager,
    IServiceScopeFactory scopeFactory)
    : ITurnTimerService
{
    public const int TurnTimeSeconds = 10;
    public const int BettingTimeSeconds = 10;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _turnTimers = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _bettingTimers = new();

    public void StartTurnTimer(string roomId)
    {
        CancelTurnTimer(roomId);
        var cts = new CancellationTokenSource();
        _turnTimers[roomId] = cts;
        _ = RunTurnTimerAsync(roomId, cts.Token);
    }

    public void CancelTurnTimer(string roomId)
    {
        if (_turnTimers.TryRemove(roomId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    public void StartBettingTimer(string roomId)
    {
        CancelBettingTimer(roomId);
        var cts = new CancellationTokenSource();
        _bettingTimers[roomId] = cts;
        _ = RunBettingTimerAsync(roomId, cts.Token);
    }

    public void CancelBettingTimer(string roomId)
    {
        if (_bettingTimers.TryRemove(roomId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private async Task RunTurnTimerAsync(string roomId, CancellationToken ct)
    {
        try
        {
            await Task.Delay(TurnTimeSeconds * 1000, ct);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        _turnTimers.TryRemove(roomId, out _);

        var room = roomManager.GetRoom(roomId);
        if (room?.BlackJackGame == null || room.BlackJackGame.State != BlackJackGameState.PlayerTurn)
            return;

        room.BlackJackGame.Stand();

        var dto = CreateGameDto(room);
        await hubContext.Clients.Group(roomId).SendAsync("PlayerStand", dto);

        if (room.BlackJackGame.State == BlackJackGameState.PlayerTurn)
        {
            StartTurnTimer(roomId);
        }
        else
        {
            await SettleIfFinished(room);
            await BroadcastRoomPlayers(room);
        }
    }

    private async Task RunBettingTimerAsync(string roomId, CancellationToken ct)
    {
        try
        {
            await Task.Delay(BettingTimeSeconds * 1000, ct);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        _bettingTimers.TryRemove(roomId, out _);

        var room = roomManager.GetRoom(roomId);
        if (room?.BlackJackGame == null || room.BlackJackGame.State != BlackJackGameState.Betting)
            return;

        // Auto-bet for players who haven't bet; forfeit those with insufficient balance
        var game = room.BlackJackGame;
        for (int i = 0; i < game.Bets.Count; i++)
        {
            if (game.Bets[i] > 0) continue;
            var playerBalance = room.GamePlayerBalances.ElementAtOrDefault(i);
            if (playerBalance >= BlackJackGame.MinBet)
                game.PlaceBet(i, BlackJackGame.MinBet);
            else
                game.ForfeitPlayer(i);
        }
        game.Start();

        var dto = CreateGameDto(room);
        await hubContext.Clients.Group(roomId).SendAsync("GameDealt", dto);

        if (room.BlackJackGame.State == BlackJackGameState.Finished)
        {
            await SettleIfFinished(room);
            await BroadcastRoomPlayers(room);
        }
        else if (room.BlackJackGame.State == BlackJackGameState.PlayerTurn)
        {
            StartTurnTimer(roomId);
        }
    }

    private async Task SettleIfFinished(BlackJackRoom room)
    {
        if (room.BlackJackGame?.State != BlackJackGameState.Finished || room.IsSettled)
            return;

        room.IsSettled = true;
        var game = room.BlackJackGame;

        using var scope = scopeFactory.CreateScope();
        var balanceRepo = scope.ServiceProvider.GetRequiredService<IGameBalanceRepository>();

        for (int i = 0; i < game.Bets.Count; i++)
        {
            if (game.Bets[i] <= 0) continue;
            var userId = room.GamePlayerUserIds.ElementAtOrDefault(i);
            if (userId == 0) continue;

            var bet = game.Bets[i];
            int delta = game.Results[i] switch
            {
                BlackJackGameResult.PlayerWin => game.PlayerHands[i].IsBlackJack() ? bet * 3 / 2 : bet,
                BlackJackGameResult.DealerWin => -bet,
                _ => 0
            };

            if (delta != 0)
                await balanceRepo.UpdateBalance(userId, GameType.BlackJack, delta);
        }

        // Send updated balances and kick broke players
        var toKick = new List<string>();
        foreach (var (connectionId, _) in room.Players)
        {
            var uid = room.PlayerUserIds.GetValueOrDefault(connectionId);
            if (uid > 0)
            {
                var balance = await balanceRepo.GetOrCreate(uid, GameType.BlackJack);
                await hubContext.Clients.Client(connectionId).SendAsync("BalanceUpdate", balance.Balance);
                if (balance.Balance < BlackJackGame.MinBet)
                    toKick.Add(connectionId);
            }
        }
        foreach (var connId in toKick)
        {
            room.Players.Remove(connId);
            room.ReadyPlayers.Remove(connId);
            room.PlayerNicknames.Remove(connId);
            room.PlayerUserIds.Remove(connId);
            if (room.HostConnectionId == connId)
                room.HostConnectionId = room.Players.Keys.FirstOrDefault();
            await hubContext.Groups.RemoveFromGroupAsync(connId, room.RoomId);
            await hubContext.Clients.Client(connId).SendAsync("Kicked", "Insufficient balance");
        }
        if (toKick.Count > 0)
        {
            if (room.Players.Count == 0)
                roomManager.RemoveRoom(room.RoomId);
            else
            {
                await hubContext.Clients.Group(room.RoomId).SendAsync("PlayerLeft", room.Players.Count);
                await BroadcastRoomPlayers(room);
            }
        }
    }

    private static BlackJackGameStateDto CreateGameDto(BlackJackRoom room)
    {
        var dto = new BlackJackGameStateDto(room.BlackJackGame!);
        dto.PlayerNames = room.GamePlayerNames;
        dto.TotalCards = room.BlackJackTable.TotalCards;
        dto.CardsRemaining = room.BlackJackTable.CardsRemaining;
        dto.ReshuffleThreshold = room.BlackJackTable.ReshuffleThreshold;
        return dto;
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
