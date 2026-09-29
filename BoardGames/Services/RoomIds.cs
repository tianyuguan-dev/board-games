using System.Collections.Concurrent;

namespace BoardGames.Services;

// Picks a random 5-digit room id and registers the new room atomically. Room managers call this from many
// connections at once and outside any room lock: Random.Shared is thread safe and TryAdd is atomic, and this
// class keeps no state of its own.
public static class RoomIds
{
    public const int MaxAttempts = 100;

    public static T Register<T>(ConcurrentDictionary<string, T> rooms, Func<string, T> createRoom, Func<int>? nextId = null)
    {
        nextId ??= () => Random.Shared.Next(10000, 100000);
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var roomId = nextId().ToString();
            if (rooms.ContainsKey(roomId)) continue;
            // Another connection can still take this id between the check and TryAdd; then we draw again.
            var room = createRoom(roomId);
            if (rooms.TryAdd(roomId, room)) return room;
        }
        throw new InvalidOperationException("Could not allocate a room id");
    }
}
