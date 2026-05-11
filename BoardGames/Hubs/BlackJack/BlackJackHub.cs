using System.Security.Claims;
using BoardGames.Data;
using BoardGames.Dtos.BlackJack;
using BoardGames.Models.BlackJack;
using BoardGames.Services.BlackJack;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BoardGames.Hubs.BlackJack;
[Authorize]
public class BlackJackHub(IBlackJackRoomManager roomManager, IUserRepository userRepository, ITurnTimerService turnTimer):Hub
{
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
        dto.TotalCards = room.BlackJackTable.TotalCards;
        dto.CardsRemaining = room.BlackJackTable.CardsRemaining;
        dto.ReshuffleThreshold = room.BlackJackTable.ReshuffleThreshold;
        return dto;
    }

    private async Task BroadcastRoomPlayers(string roomId)
    {
        var room = roomManager.GetRoom(roomId);
        if (room == null) return;
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
            await Clients.Client(connId).SendAsync("RoomUpdate", new { Players = players, IsHost = connId == room.HostConnectionId });
        }
    }

    private async Task<string> GetNickname()
    {
        var userId = int.Parse(Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await userRepository.FindById(userId);
        if (user == null) return "Anonymous";
        return string.IsNullOrEmpty(user.Nickname) ? user.Username : user.Nickname;
    }
    
    public async Task<BlackJackRoom> CreateRoom(int maxPlayers)
    {
        if (maxPlayers is <= 0 or > 7)
            throw new ArgumentOutOfRangeException(nameof(maxPlayers));
        var contextConnectionId = Context.ConnectionId;
        var blackJackRoom = roomManager.CreateRoom(maxPlayers);
        await Groups.AddToGroupAsync(contextConnectionId, blackJackRoom.RoomId);
        roomManager.JoinRoom(blackJackRoom.RoomId, contextConnectionId);
        blackJackRoom.HostConnectionId = contextConnectionId;
        blackJackRoom.PlayerNicknames[contextConnectionId] = await GetNickname();
        await Clients.Group(blackJackRoom.RoomId).SendAsync("PlayerJoined", blackJackRoom.Players.Count);
        await BroadcastRoomPlayers(blackJackRoom.RoomId);
        return blackJackRoom;
    }

    public async Task<JoinRoomResult> JoinRoom(string roomId)
    {
        var contextConnectionId = Context.ConnectionId;
        roomManager.JoinRoom(roomId, contextConnectionId);
        await Groups.AddToGroupAsync(contextConnectionId, roomId);
        var blackJackRoom = roomManager.GetRoom(roomId);
        blackJackRoom!.PlayerNicknames[contextConnectionId] = await GetNickname();
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
        if (blackJackRoom.BlackJackGame != null && blackJackRoom.BlackJackGame.State!=BlackJackGameState.Finished)
            throw new InvalidOperationException($"The game is in progress in room with id {roomId}");
        var nonHostPlayers = blackJackRoom.Players.Keys.Where(k => k != blackJackRoom.HostConnectionId).ToList();
        if (nonHostPlayers.Count > 0 && !nonHostPlayers.All(p => blackJackRoom.ReadyPlayers.Contains(p)))
            throw new InvalidOperationException("Not all players are ready");
        blackJackRoom.ReassignSeats();
        blackJackRoom.GamePlayerNames = blackJackRoom.Players
            .OrderBy(p => p.Value)
            .Select(p => blackJackRoom.PlayerNicknames.GetValueOrDefault(p.Key, "Player " + p.Value))
            .ToList();
        BlackJackGame blackJackGame = blackJackRoom.BlackJackTable.NewRound(blackJackRoom.Players.Count);
        blackJackRoom.BlackJackGame = blackJackGame;
        blackJackRoom.ReadyPlayers.Clear();
        await BroadcastRoomPlayers(roomId);
        foreach (var (connectionId, playerIndex) in blackJackRoom.Players)
        {
            await Clients.Client(connectionId).SendAsync("YourSeat", playerIndex);
        }
        await Clients.Group(roomId).SendAsync("StartGame", CreateGameDto(blackJackRoom));
        ManageTurnTimer(roomId, blackJackGame);
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
        if (playerIndex!=blackJackRoom.BlackJackGame.CurrentPlayerIndex)
            throw new InvalidOperationException($"Not this player's turn");
        blackJackRoom.BlackJackGame.Hit();
        await Clients.Group(roomId).SendAsync("PlayerHit", CreateGameDto(blackJackRoom));
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
        if (playerIndex!=blackJackRoom.BlackJackGame.CurrentPlayerIndex)
            throw new InvalidOperationException($"Not this player's turn");
        blackJackRoom.BlackJackGame.Stand();
        await Clients.Group(roomId).SendAsync("PlayerStand", CreateGameDto(blackJackRoom));
        ManageTurnTimer(roomId, blackJackRoom.BlackJackGame);
    }

    private static void TransferHostIfNeeded(BlackJackRoom room, string leavingConnectionId)
    {
        if (room.HostConnectionId == leavingConnectionId)
        {
            room.HostConnectionId = room.Players.Keys.FirstOrDefault(k => k != leavingConnectionId);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        (string? roomId,int seatIndex) = roomManager.FindAndRemoveByConnectionId(Context.ConnectionId);
        if (roomId != null)
        {
            BlackJackRoom? blackJackRoom = roomManager.GetRoom(roomId);
            var prevIndex = blackJackRoom?.BlackJackGame?.CurrentPlayerIndex;
            var prevState = blackJackRoom?.BlackJackGame?.State;
            blackJackRoom?.BlackJackGame?.ForfeitPlayer(seatIndex);
            if (blackJackRoom != null) TransferHostIfNeeded(blackJackRoom, Context.ConnectionId);
            if (blackJackRoom?.Players.Count == 0)
            {
                turnTimer.CancelTurnTimer(roomId);
                roomManager.RemoveRoom(roomId);
            }
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);
            blackJackRoom?.ReadyPlayers.Remove(Context.ConnectionId);
            blackJackRoom?.PlayerNicknames.Remove(Context.ConnectionId);
            if (blackJackRoom?.BlackJackGame != null &&
                (blackJackRoom.BlackJackGame.CurrentPlayerIndex != prevIndex || blackJackRoom.BlackJackGame.State != prevState))
            {
                await Clients.Group(roomId).SendAsync("PlayerStand", CreateGameDto(blackJackRoom));
                ManageTurnTimer(roomId, blackJackRoom.BlackJackGame);
            }
            await Clients.Group(roomId).SendAsync("PlayerLeft", blackJackRoom?.Players.Count ?? 0);
            await BroadcastRoomPlayers(roomId);
        }
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
        (string? roomId,int seatIndex) = roomManager.FindAndRemoveByConnectionId(Context.ConnectionId);
        if (roomId != null)
        {
            BlackJackRoom? blackJackRoom = roomManager.GetRoom(roomId);
            var prevIndex = blackJackRoom?.BlackJackGame?.CurrentPlayerIndex;
            var prevState = blackJackRoom?.BlackJackGame?.State;
            blackJackRoom?.BlackJackGame?.ForfeitPlayer(seatIndex);
            if (blackJackRoom != null) TransferHostIfNeeded(blackJackRoom, Context.ConnectionId);
            blackJackRoom?.ReadyPlayers.Remove(Context.ConnectionId);
            blackJackRoom?.PlayerNicknames.Remove(Context.ConnectionId);
            if (blackJackRoom?.Players.Count == 0)
            {
                turnTimer.CancelTurnTimer(roomId);
                roomManager.RemoveRoom(roomId);
            }

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);
            if (blackJackRoom?.BlackJackGame != null &&
                (blackJackRoom.BlackJackGame.CurrentPlayerIndex != prevIndex || blackJackRoom.BlackJackGame.State != prevState))
            {
                await Clients.Group(roomId).SendAsync("PlayerStand", CreateGameDto(blackJackRoom));
                ManageTurnTimer(roomId, blackJackRoom.BlackJackGame);
            }
            await Clients.Group(roomId).SendAsync("PlayerLeft", blackJackRoom?.Players.Count ?? 0);
            await BroadcastRoomPlayers(roomId);
        }
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
        await Groups.RemoveFromGroupAsync(target.Key, roomId);
        await Clients.Client(target.Key).SendAsync("Kicked");
        await Clients.Group(roomId).SendAsync("PlayerLeft", blackJackRoom.Players.Count);
        await BroadcastRoomPlayers(roomId);
    }
}