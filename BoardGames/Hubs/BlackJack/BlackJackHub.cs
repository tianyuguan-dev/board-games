using BoardGames.Dtos.BlackJack;
using BoardGames.Models.BlackJack;
using BoardGames.Services.BlackJack;
using Microsoft.AspNetCore.SignalR;

namespace BoardGames.Hubs.BlackJack;

public class BlackJackHub(IBlackJackRoomManager roomManager):Hub
{
    
    public async Task<BlackJackRoom> CreateRoom(int maxPlayers)
    {
        if (maxPlayers <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPlayers));
        var contextConnectionId = Context.ConnectionId;
        var blackJackRoom = roomManager.CreateRoom(maxPlayers);
        await Groups.AddToGroupAsync(contextConnectionId, blackJackRoom.RoomId);
        roomManager.JoinRoom(blackJackRoom.RoomId, contextConnectionId);
        return blackJackRoom;
    }

    public async Task JoinRoom(string roomId)
    {
        var contextConnectionId = Context.ConnectionId;
        roomManager.JoinRoom(roomId, contextConnectionId);
        await Groups.AddToGroupAsync(contextConnectionId, roomId);
        await Clients.Group(roomId).SendAsync("JoinRoom", roomId);
    }
    
    public async Task StartGame(string roomId)
    {
        var blackJackRoom = roomManager.GetRoom(roomId);
        if (blackJackRoom == null)
            throw new InvalidOperationException($"Room with id {roomId} not found");
        blackJackRoom.ReassignSeats();
        BlackJackGame blackJackGame = blackJackRoom.BlackJackTable.NewRound(blackJackRoom.Players.Count);
        blackJackRoom.BlackJackGame = blackJackGame;
        await Clients.Group(roomId).SendAsync("StartGame", new BlackJackGameStateDto(blackJackGame));
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
        await Clients.Group(roomId).SendAsync("PlayerHit", new BlackJackGameStateDto(blackJackRoom.BlackJackGame));
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
        await Clients.Group(roomId).SendAsync("PlayerStand", new BlackJackGameStateDto(blackJackRoom.BlackJackGame));
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        (string? roomId,int seatIndex) = roomManager.FindAndRemoveByConnectionId(Context.ConnectionId);
        if (roomId != null)
        {
            BlackJackRoom? blackJackRoom = roomManager.GetRoom(roomId);
            blackJackRoom?.BlackJackGame?.ForfeitPlayer(seatIndex);
            await Clients.Group(roomId).SendAsync("PlayerDisconnected", roomId);
        }
        await base.OnDisconnectedAsync(exception);
    }
}