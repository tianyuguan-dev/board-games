using System.Collections.Concurrent;
using BoardGames.Models.BlackJack;
using BoardGames.Models.Poker;

namespace BoardGames.Services.BlackJack;

public class BlackJackRoomManager(IDeckFactory? deckFactory = null, Func<int>? nextRoomId = null) : IBlackJackRoomManager
{
    private readonly ConcurrentDictionary<string, BlackJackRoom> _rooms = new();
    // Stateless and shared by every room; readonly so no connection can swap it.
    private readonly IDeckFactory? _deckFactory = deckFactory;
    // Scripted ids for tests; null means Random.Shared (see RoomIds).
    private readonly Func<int>? _nextRoomId = nextRoomId;
    
    public BlackJackRoom CreateRoom(int maxPlayers)
    {
        return RoomIds.Register(_rooms, id => new BlackJackRoom(id, maxPlayers, _deckFactory), _nextRoomId);
    }

    public BlackJackRoom? GetRoom(string roomId)
    {
        _rooms.TryGetValue(roomId, out var room);
        return room;
    }

    public bool IsInAnyRoom(string connectionId)
    {
        return _rooms.Values.Any(r => r.Players.ContainsKey(connectionId));
    }

    public void JoinRoom(string roomId, string connectionId)
    {
        if (IsInAnyRoom(connectionId))
            throw new InvalidOperationException("Player is already in a room");
        var blackJackRoom = GetRoom(roomId);
        if (blackJackRoom == null)
            throw new InvalidOperationException($"Cannot join room {roomId} because it doesn't exist");
        if (blackJackRoom.BlackJackGame!=null&&blackJackRoom.BlackJackGame.State!=BlackJackGameState.Finished)
        {
            throw new InvalidOperationException(
                $"Cannot join room {roomId} because the game is in progress");
        }

        if (blackJackRoom.Players.ContainsKey(connectionId))
            throw new InvalidOperationException($"Same player cannot join room twice");
        var maxPlayers = blackJackRoom.MaxPlayers;
        var players = blackJackRoom.Players;
        if (players.Count>= maxPlayers)
        {
            throw new InvalidOperationException(
                $"Cannot join room {roomId} because the maximum number of players has been reached");
        }
        // Lowest free seat, not Players.Count: a leave or kick can leave a gap, and Count would collide with an occupied seat.
        var usedSeats = new HashSet<int>(players.Values);
        var seat = 0;
        while (usedSeats.Contains(seat)) seat++;
        players.Add(connectionId, seat);
    }

    public BlackJackRoom? FindRoomByConnectionId(string connectionId)
    {
        return _rooms.Values.FirstOrDefault(r => r.Players.ContainsKey(connectionId));
    }

    public void RemoveRoom(string roomId)
    {
        _rooms.TryRemove(roomId, out _);
    }
}