using System.Collections.Concurrent;
using BoardGames.Models.BlackJack;
using BoardGames.Services.BlackJack;

namespace BoardGames.Tests.Integration;

// Delegates to the real room manager and lets a test wait until someone (e.g. a timer) looks a room up.
// That makes "the timer has fetched the room and is now queued on its lock" observable without sleeping.
public class SpyBlackJackRoomManager(BlackJackRoomManager inner) : IBlackJackRoomManager
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _awaitedLookups = new();

    // Completes the next time GetRoom(roomId) returns. Arm it before starting whatever will do the lookup.
    public Task NextLookup(string roomId) =>
        _awaitedLookups.GetOrAdd(roomId, _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

    public BlackJackRoom? GetRoom(string roomId)
    {
        var room = inner.GetRoom(roomId);
        if (_awaitedLookups.TryRemove(roomId, out var lookup)) lookup.TrySetResult(true);
        return room;
    }

    public BlackJackRoom CreateRoom(int maxPlayers) => inner.CreateRoom(maxPlayers);
    public bool IsInAnyRoom(string connectionId) => inner.IsInAnyRoom(connectionId);
    public void JoinRoom(string roomId, string connectionId) => inner.JoinRoom(roomId, connectionId);
    public BlackJackRoom? FindRoomByConnectionId(string connectionId) => inner.FindRoomByConnectionId(connectionId);
    public void RemoveRoom(string roomId) => inner.RemoveRoom(roomId);
}
