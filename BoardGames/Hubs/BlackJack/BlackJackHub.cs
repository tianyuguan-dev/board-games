using System.Security.Claims;
using BoardGames.Data;
using BoardGames.Dtos.BlackJack;
using BoardGames.Models;
using BoardGames.Models.BlackJack;
using BoardGames.Services.BlackJack;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BoardGames.Hubs.BlackJack;
[Authorize]
public class BlackJackHub(
    IBlackJackRoomManager roomManager,
    IUserRepository userRepository,
    ITurnTimerService turnTimer,
    IGameBalanceRepository balanceRepo) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = int.Parse(Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await userRepository.FindById(userId);
        if (user != null) { user.LastActiveAt = DateTime.UtcNow; await userRepository.Update(user); }
        await base.OnConnectedAsync();
    }

    private void ManageTurnTimer(string roomId, BlackJackGame game)
    {
        if (game.State == BlackJackGameState.PlayerTurn)
            turnTimer.StartTurnTimer(roomId);
        else
            turnTimer.CancelTurnTimer(roomId);
    }

    private static BlackJackGameStateDto CreateGameDto(BlackJackRoom room)
    {
        var dto = new BlackJackGameStateDto(room.BlackJackGame!);
        dto.PlayerNames = room.GamePlayerNames;
        dto.PlayerBalances = room.GamePlayerBalances;
        dto.TotalCards = room.BlackJackTable.TotalCards;
        dto.CardsRemaining = room.BlackJackTable.CardsRemaining;
        dto.ReshuffleThreshold = room.BlackJackTable.ReshuffleThreshold;
        return dto;
    }

    private async Task BroadcastRoomPlayers(string roomId)
    {
        var room = roomManager.GetRoom(roomId);
        if (room == null) return;
        var playerList = new List<object>();
        foreach (var p in room.Players.OrderBy(p => p.Value))
        {
            var uid = room.PlayerUserIds.GetValueOrDefault(p.Key);
            var bal = uid > 0 ? (await balanceRepo.GetOrCreate(uid, GameType.BlackJack)).Balance : 0;
            playerList.Add(new
            {
                Nickname = room.PlayerNicknames.GetValueOrDefault(p.Key, "Player " + p.Value),
                IsReady = room.ReadyPlayers.Contains(p.Key),
                IsHost = p.Key == room.HostConnectionId,
                SeatIndex = p.Value,
                Balance = bal
            });
        }
        foreach (var connId in room.Players.Keys)
        {
            await Clients.Client(connId).SendAsync("RoomUpdate", new { Players = playerList, IsHost = connId == room.HostConnectionId });
        }
    }

    private int GetUserId()
    {
        return int.Parse(Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    }

    private async Task<string> GetNickname()
    {
        var userId = GetUserId();
        var user = await userRepository.FindById(userId);
        if (user == null) return "Anonymous";
        return string.IsNullOrEmpty(user.Nickname) ? user.Username : user.Nickname;
    }

    public async Task<int> GetBalance()
    {
        var balance = await balanceRepo.GetOrCreate(GetUserId(), GameType.BlackJack);
        return balance.Balance;
    }

    public async Task<List<object>> GetLeaderboard()
    {
        var top = await balanceRepo.GetTopBalances(GameType.BlackJack, 5);
        return top.Select(x => (object)new { nickname = x.Nickname, balance = x.Balance }).ToList();
    }

    public async Task<int> ClaimBonus()
    {
        var userId = GetUserId();
        var balance = await balanceRepo.GetOrCreate(userId, GameType.BlackJack);
        if (balance.Balance >= 50)
            throw new InvalidOperationException("Balance too high to claim bonus");
        var bonus = Random.Shared.Next(10, 21);
        await balanceRepo.UpdateBalance(userId, GameType.BlackJack, bonus);
        return balance.Balance;
    }

    private async Task SettleIfFinished(string roomId, BlackJackRoom room)
    {
        if (room.BlackJackGame?.State != BlackJackGameState.Finished || room.IsSettled)
            return;

        room.IsSettled = true;
        var game = room.BlackJackGame;

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

        // Update GamePlayerBalances so DTO reflects settled balances
        for (int i = 0; i < room.GamePlayerUserIds.Count; i++)
        {
            var uid = room.GamePlayerUserIds[i];
            if (uid == 0) continue;
            var bal = await balanceRepo.GetOrCreate(uid, GameType.BlackJack);
            if (i < room.GamePlayerBalances.Count)
                room.GamePlayerBalances[i] = bal.Balance;
        }

        // Send updated balances and kick broke players
        var toKick = new List<string>();
        foreach (var (connectionId, _) in room.Players)
        {
            var uid = room.PlayerUserIds.GetValueOrDefault(connectionId);
            if (uid > 0)
            {
                var balance = await balanceRepo.GetOrCreate(uid, GameType.BlackJack);
                await Clients.Client(connectionId).SendAsync("BalanceUpdate", balance.Balance);
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
            TransferHostIfNeeded(room, connId);
            await Groups.RemoveFromGroupAsync(connId, roomId);
            await Clients.Client(connId).SendAsync("Kicked", "Insufficient balance");
        }
        if (toKick.Count > 0)
        {
            if (room.Players.Count == 0)
                roomManager.RemoveRoom(roomId);
            else
            {
                await Clients.Group(roomId).SendAsync("PlayerLeft", room.Players.Count);
                await BroadcastRoomPlayers(roomId);
            }
        }
    }

    private async Task EnsureSufficientBalance()
    {
        var userId = GetUserId();
        var balance = await balanceRepo.GetOrCreate(userId, GameType.BlackJack);
        if (balance.Balance < BlackJackGame.MinBet)
            throw new InvalidOperationException("Insufficient balance");
    }

    public async Task<BlackJackRoom> CreateRoom(int maxPlayers)
    {
        if (maxPlayers is <= 0 or > 7)
            throw new ArgumentOutOfRangeException(nameof(maxPlayers));
        await EnsureSufficientBalance();
        var contextConnectionId = Context.ConnectionId;
        var blackJackRoom = roomManager.CreateRoom(maxPlayers);
        await Groups.AddToGroupAsync(contextConnectionId, blackJackRoom.RoomId);
        roomManager.JoinRoom(blackJackRoom.RoomId, contextConnectionId);
        blackJackRoom.HostConnectionId = contextConnectionId;
        blackJackRoom.PlayerNicknames[contextConnectionId] = await GetNickname();
        blackJackRoom.PlayerUserIds[contextConnectionId] = GetUserId();
        await Clients.Group(blackJackRoom.RoomId).SendAsync("PlayerJoined", blackJackRoom.Players.Count);
        await BroadcastRoomPlayers(blackJackRoom.RoomId);
        return blackJackRoom;
    }

    public async Task<JoinRoomResult> JoinRoom(string roomId)
    {
        await EnsureSufficientBalance();
        var contextConnectionId = Context.ConnectionId;
        var userId = GetUserId();
        var existingRoom = roomManager.GetRoom(roomId)
            ?? throw new InvalidOperationException($"Room {roomId} not found");
        if (existingRoom.PlayerUserIds.ContainsValue(userId))
            throw new InvalidOperationException("You are already in this room");
        roomManager.JoinRoom(roomId, contextConnectionId);
        await Groups.AddToGroupAsync(contextConnectionId, roomId);
        var blackJackRoom = roomManager.GetRoom(roomId);
        blackJackRoom!.PlayerNicknames[contextConnectionId] = await GetNickname();
        blackJackRoom.PlayerUserIds[contextConnectionId] = userId;
        await Clients.Group(roomId).SendAsync("PlayerJoined", blackJackRoom!.Players.Count);
        await BroadcastRoomPlayers(roomId);
        return new JoinRoomResult(blackJackRoom.MaxPlayers, blackJackRoom.Players.Count);
    }

    public async Task StartGame(string roomId)
    {
        var blackJackRoom = roomManager.GetRoom(roomId);
        if (blackJackRoom == null)
            throw new InvalidOperationException($"Room with id {roomId} not found");
        if (blackJackRoom.HostConnectionId != Context.ConnectionId)
            throw new InvalidOperationException("Only the host can start the game");
        if (blackJackRoom.BlackJackGame != null && blackJackRoom.BlackJackGame.State != BlackJackGameState.Finished)
            throw new InvalidOperationException($"The game is in progress in room with id {roomId}");
        var nonHostPlayers = blackJackRoom.Players.Keys.Where(k => k != blackJackRoom.HostConnectionId).ToList();
        if (nonHostPlayers.Count > 0 && !nonHostPlayers.All(p => blackJackRoom.ReadyPlayers.Contains(p)))
            throw new InvalidOperationException("Not all players are ready");
        blackJackRoom.ReassignSeats();
        blackJackRoom.GamePlayerNames = blackJackRoom.Players
            .OrderBy(p => p.Value)
            .Select(p => blackJackRoom.PlayerNicknames.GetValueOrDefault(p.Key, "Player " + p.Value))
            .ToList();
        blackJackRoom.GamePlayerUserIds = blackJackRoom.Players
            .OrderBy(p => p.Value)
            .Select(p => blackJackRoom.PlayerUserIds.GetValueOrDefault(p.Key, 0))
            .ToList();
        blackJackRoom.IsSettled = false;
        blackJackRoom.GamePlayerBalances = new List<int>();
        BlackJackGame blackJackGame = blackJackRoom.BlackJackTable.NewRound(blackJackRoom.Players.Count);
        blackJackRoom.BlackJackGame = blackJackGame;
        blackJackRoom.ReadyPlayers.Clear();
        await BroadcastRoomPlayers(roomId);
        foreach (var (connectionId, playerIndex) in blackJackRoom.Players.OrderBy(p => p.Value))
        {
            var userId = blackJackRoom.PlayerUserIds.GetValueOrDefault(connectionId);
            var bal = userId > 0 ? await balanceRepo.GetOrCreate(userId, GameType.BlackJack) : null;
            blackJackRoom.GamePlayerBalances.Add(bal?.Balance ?? 0);
            if (bal != null)
                await Clients.Client(connectionId).SendAsync("BalanceUpdate", bal.Balance);
            await Clients.Client(connectionId).SendAsync("YourSeat", blackJackRoom.Players[connectionId]);
        }
        // Auto-forfeit players with insufficient balance
        int activePlayers = 0;
        for (int i = 0; i < blackJackRoom.GamePlayerBalances.Count; i++)
        {
            if (blackJackRoom.GamePlayerBalances[i] < BlackJackGame.MinBet)
                blackJackGame.ForfeitPlayer(i);
            else
                activePlayers++;
        }
        if (activePlayers == 0)
            throw new InvalidOperationException("No players have sufficient balance to play");
        await Clients.Group(roomId).SendAsync("StartGame", CreateGameDto(blackJackRoom));
        turnTimer.StartBettingTimer(roomId);
    }

    public async Task PlaceBet(string roomId, int amount)
    {
        var contextConnectionId = Context.ConnectionId;
        var blackJackRoom = roomManager.GetRoom(roomId);
        if (blackJackRoom == null)
            throw new InvalidOperationException($"Room with id {roomId} not found");
        if (blackJackRoom.BlackJackGame == null)
            throw new InvalidOperationException($"Room {roomId}, game not start yet");
        if (blackJackRoom.BlackJackGame.State != BlackJackGameState.Betting)
            throw new InvalidOperationException("Not in betting phase");
        if (!blackJackRoom.Players.TryGetValue(contextConnectionId, out var playerIndex))
            throw new InvalidOperationException("Player not in room");
        var playerBalance = blackJackRoom.GamePlayerBalances.ElementAtOrDefault(playerIndex);
        if (amount > playerBalance)
            throw new InvalidOperationException("Bet exceeds your balance");

        blackJackRoom.BlackJackGame.PlaceBet(playerIndex, amount);
        await Clients.Group(roomId).SendAsync("BetPlaced", CreateGameDto(blackJackRoom));

        if (blackJackRoom.BlackJackGame.AllBetsPlaced())
        {
            turnTimer.CancelBettingTimer(roomId);
            blackJackRoom.BlackJackGame.Start();
            await Clients.Group(roomId).SendAsync("GameDealt", CreateGameDto(blackJackRoom));

            if (blackJackRoom.BlackJackGame.State == BlackJackGameState.Finished)
            {
                await SettleIfFinished(roomId, blackJackRoom);
                await BroadcastRoomPlayers(roomId);
            }
            else
            {
                ManageTurnTimer(roomId, blackJackRoom.BlackJackGame);
            }
        }
    }

    public async Task BlackJackPlayerHit(string roomId)
    {
        var contextConnectionId = Context.ConnectionId;
        var blackJackRoom = roomManager.GetRoom(roomId);
        if (blackJackRoom == null)
            throw new InvalidOperationException($"Room with id {roomId} not found");
        if (blackJackRoom.BlackJackGame == null)
            throw new InvalidOperationException($"Room {roomId}, game not start yet");
        if (!blackJackRoom.Players.TryGetValue(contextConnectionId, out var playerIndex))
            throw new InvalidOperationException("Player not in room");
        if (playerIndex != blackJackRoom.BlackJackGame.CurrentPlayerIndex)
            throw new InvalidOperationException($"Not this player's turn");
        blackJackRoom.BlackJackGame.Hit();
        await Clients.Group(roomId).SendAsync("PlayerHit", CreateGameDto(blackJackRoom));
        if (blackJackRoom.BlackJackGame.State == BlackJackGameState.Finished)
            await SettleIfFinished(roomId, blackJackRoom);
        ManageTurnTimer(roomId, blackJackRoom.BlackJackGame);
    }

    public async Task BlackJackPlayerStand(string roomId)
    {
        var contextConnectionId = Context.ConnectionId;
        var blackJackRoom = roomManager.GetRoom(roomId);
        if (blackJackRoom == null)
            throw new InvalidOperationException($"Room with id {roomId} not found");
        if (blackJackRoom.BlackJackGame == null)
            throw new InvalidOperationException($"Room {roomId}, game not start yet");
        if (!blackJackRoom.Players.TryGetValue(contextConnectionId, out var playerIndex))
            throw new InvalidOperationException("Player not in room");
        if (playerIndex != blackJackRoom.BlackJackGame.CurrentPlayerIndex)
            throw new InvalidOperationException($"Not this player's turn");
        blackJackRoom.BlackJackGame.Stand();
        await Clients.Group(roomId).SendAsync("PlayerStand", CreateGameDto(blackJackRoom));
        if (blackJackRoom.BlackJackGame.State == BlackJackGameState.Finished)
            await SettleIfFinished(roomId, blackJackRoom);
        ManageTurnTimer(roomId, blackJackRoom.BlackJackGame);
    }

    public async Task BlackJackPlayerDoubleDown(string roomId)
    {
        var contextConnectionId = Context.ConnectionId;
        var blackJackRoom = roomManager.GetRoom(roomId);
        if (blackJackRoom == null)
            throw new InvalidOperationException($"Room with id {roomId} not found");
        if (blackJackRoom.BlackJackGame == null)
            throw new InvalidOperationException($"Room {roomId}, game not start yet");
        if (!blackJackRoom.Players.TryGetValue(contextConnectionId, out var playerIndex))
            throw new InvalidOperationException("Player not in room");
        if (playerIndex != blackJackRoom.BlackJackGame.CurrentPlayerIndex)
            throw new InvalidOperationException("Not this player's turn");
        if (!blackJackRoom.BlackJackGame.CanDoubleDown())
            throw new InvalidOperationException("Cannot double down after hitting");
        var currentBet = blackJackRoom.BlackJackGame.Bets[playerIndex];
        var playerBalance = blackJackRoom.GamePlayerBalances.ElementAtOrDefault(playerIndex);
        if (playerBalance < currentBet * 2)
            throw new InvalidOperationException("Insufficient balance to double down");
        blackJackRoom.BlackJackGame.DoubleDown();
        await Clients.Group(roomId).SendAsync("PlayerDoubleDown", CreateGameDto(blackJackRoom));
        if (blackJackRoom.BlackJackGame.State == BlackJackGameState.Finished)
            await SettleIfFinished(roomId, blackJackRoom);
        ManageTurnTimer(roomId, blackJackRoom.BlackJackGame);
    }

    private static void TransferHostIfNeeded(BlackJackRoom room, string leavingConnectionId)
    {
        if (room.HostConnectionId == leavingConnectionId)
        {
            room.HostConnectionId = room.Players.Keys.FirstOrDefault(k => k != leavingConnectionId);
        }
    }

    private async Task HandlePlayerLeave(string connectionId)
    {
        (string? roomId, int seatIndex) = roomManager.FindAndRemoveByConnectionId(connectionId);
        if (roomId == null) return;

        BlackJackRoom? blackJackRoom = roomManager.GetRoom(roomId);
        var prevIndex = blackJackRoom?.BlackJackGame?.CurrentPlayerIndex;
        var prevState = blackJackRoom?.BlackJackGame?.State;
        blackJackRoom?.BlackJackGame?.ForfeitPlayer(seatIndex);
        if (blackJackRoom != null) TransferHostIfNeeded(blackJackRoom, connectionId);
        blackJackRoom?.ReadyPlayers.Remove(connectionId);
        blackJackRoom?.PlayerNicknames.Remove(connectionId);
        blackJackRoom?.PlayerUserIds.Remove(connectionId);

        if (blackJackRoom?.Players.Count == 0)
        {
            turnTimer.CancelTurnTimer(roomId);
            turnTimer.CancelBettingTimer(roomId);
            roomManager.RemoveRoom(roomId);
        }

        await Groups.RemoveFromGroupAsync(connectionId, roomId);

        if (blackJackRoom?.BlackJackGame != null &&
            (blackJackRoom.BlackJackGame.CurrentPlayerIndex != prevIndex || blackJackRoom.BlackJackGame.State != prevState))
        {
            // Check if all bets placed after forfeit during betting
            if (blackJackRoom.BlackJackGame.State == BlackJackGameState.Betting &&
                blackJackRoom.BlackJackGame.AllBetsPlaced())
            {
                turnTimer.CancelBettingTimer(roomId);
                blackJackRoom.BlackJackGame.Start();
                await Clients.Group(roomId).SendAsync("GameDealt", CreateGameDto(blackJackRoom));
                if (blackJackRoom.BlackJackGame.State == BlackJackGameState.Finished)
                    await SettleIfFinished(roomId, blackJackRoom);
                ManageTurnTimer(roomId, blackJackRoom.BlackJackGame);
            }
            else
            {
                await Clients.Group(roomId).SendAsync("PlayerStand", CreateGameDto(blackJackRoom));
                if (blackJackRoom.BlackJackGame.State == BlackJackGameState.Finished)
                    await SettleIfFinished(roomId, blackJackRoom);
                ManageTurnTimer(roomId, blackJackRoom.BlackJackGame);
            }
        }

        await Clients.Group(roomId).SendAsync("PlayerLeft", blackJackRoom?.Players.Count ?? 0);
        await BroadcastRoomPlayers(roomId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await HandlePlayerLeave(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task Ready(string roomId)
    {
        var contextConnectionId = Context.ConnectionId;
        var blackJackRoom = roomManager.GetRoom(roomId);
        if (blackJackRoom == null)
            throw new InvalidOperationException($"Room with id {roomId} not found");
        if (!blackJackRoom.Players.Keys.Contains(contextConnectionId))
            throw new InvalidOperationException("Player not in room");
        if (contextConnectionId == blackJackRoom.HostConnectionId)
            throw new InvalidOperationException("Host should use StartGame instead");
        blackJackRoom.ReadyPlayers.Add(contextConnectionId);
        await BroadcastRoomPlayers(roomId);
    }

    public async Task Unready(string roomId)
    {
        var contextConnectionId = Context.ConnectionId;
        var blackJackRoom = roomManager.GetRoom(roomId);
        if (blackJackRoom == null)
            throw new InvalidOperationException($"Room with id {roomId} not found");
        if (!blackJackRoom.ReadyPlayers.Contains(contextConnectionId))
            throw new InvalidOperationException("Player not ready");
        blackJackRoom.ReadyPlayers.Remove(contextConnectionId);
        await BroadcastRoomPlayers(roomId);
    }

    public async Task LeaveRoom()
    {
        await HandlePlayerLeave(Context.ConnectionId);
    }

    public async Task KickPlayer(string roomId, int seatIndex)
    {
        var blackJackRoom = roomManager.GetRoom(roomId);
        if (blackJackRoom == null)
            throw new InvalidOperationException($"Room with id {roomId} not found");
        if (blackJackRoom.HostConnectionId != Context.ConnectionId)
            throw new InvalidOperationException("Only the host can kick players");
        if (blackJackRoom.BlackJackGame != null && blackJackRoom.BlackJackGame.State != BlackJackGameState.Finished)
            throw new InvalidOperationException("Cannot kick players during a game");

        var target = blackJackRoom.Players.FirstOrDefault(p => p.Value == seatIndex);
        if (target.Key == null)
            throw new InvalidOperationException("Player not found at that seat");
        if (target.Key == Context.ConnectionId)
            throw new InvalidOperationException("Cannot kick yourself");

        blackJackRoom.Players.Remove(target.Key);
        blackJackRoom.ReadyPlayers.Remove(target.Key);
        blackJackRoom.PlayerNicknames.Remove(target.Key);
        blackJackRoom.PlayerUserIds.Remove(target.Key);
        await Groups.RemoveFromGroupAsync(target.Key, roomId);
        await Clients.Client(target.Key).SendAsync("Kicked");
        await Clients.Group(roomId).SendAsync("PlayerLeft", blackJackRoom.Players.Count);
        await BroadcastRoomPlayers(roomId);
    }
}
