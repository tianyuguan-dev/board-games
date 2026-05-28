using System.Collections.Concurrent;
using BoardGames.Models.Avalon;

namespace BoardGames.Services.Avalon;

public class AvalonRoomManager : IAvalonRoomManager
{
    private readonly ConcurrentDictionary<string, AvalonRoom> _rooms = new();
    private readonly Random _random = new();

    public AvalonRoom CreateRoom(int maxPlayers)
    {
        if (maxPlayers < 5) maxPlayers = 5;
        if (maxPlayers > 10) maxPlayers = 10;

        string roomId = _random.Next(10000, 100000).ToString();
        while (_rooms.ContainsKey(roomId))
            roomId = _random.Next(10000, 100000).ToString();

        var room = new AvalonRoom(roomId, maxPlayers);
        _rooms.TryAdd(roomId, room);
        return room;
    }

    public AvalonRoom? GetRoom(string roomId)
    {
        _rooms.TryGetValue(roomId, out var room);
        return room;
    }

    public void JoinRoom(string roomId, string connectionId)
    {
        if (_rooms.Values.Any(r => r.Players.ContainsKey(connectionId)))
            throw new InvalidOperationException("Player is already in a room");

        var room = GetRoom(roomId) ?? throw new InvalidOperationException("Room not found");

        if (room.Game != null && room.Game.Phase != AvalonPhase.GameOver)
            throw new InvalidOperationException("Game is in progress");

        if (room.Players.ContainsKey(connectionId))
            throw new InvalidOperationException("Already in this room");

        if (room.Players.Count >= room.MaxPlayers)
            throw new InvalidOperationException("Room is full");

        // Assign the lowest free seat, not Players.Count — a mid-game/lobby disconnect
        // can leave a gap, and Players.Count would collide with an occupied seat.
        var usedSeats = new HashSet<int>(room.Players.Values);
        int seat = 0;
        while (usedSeats.Contains(seat)) seat++;
        room.Players.Add(connectionId, seat);
    }

    public (string? roomId, int seatIndex) FindRoomByConnectionId(string connectionId)
    {
        foreach (var room in _rooms.Values)
        {
            if (room.Players.TryGetValue(connectionId, out int seatIndex))
                return (room.RoomId, seatIndex);
        }
        return (null, -1);
    }

    public (string? roomId, int seatIndex) FindAndRemoveByConnectionId(string connectionId)
    {
        foreach (var room in _rooms.Values)
        {
            if (room.Players.TryGetValue(connectionId, out int seatIndex))
            {
                room.Players.Remove(connectionId);
                return (room.RoomId, seatIndex);
            }
        }
        return (null, -1);
    }

    public string? FindRoomByUserId(int userId)
    {
        foreach (var room in _rooms.Values)
        {
            if (room.PlayerUserIds.ContainsValue(userId))
                return room.RoomId;
            if (room.DisconnectedPlayers.ContainsKey(userId))
                return room.RoomId;
        }
        return null;
    }

    public void RemoveRoom(string roomId)
    {
        _rooms.TryRemove(roomId, out _);
    }
}
