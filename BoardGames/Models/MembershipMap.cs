using System.Collections.Concurrent;

namespace BoardGames.Models;

// Room membership collections are written under the room's lock but read by the room managers from other threads
// (cross-room lookups), so they must be thread safe. This keeps the Dictionary methods the code already uses,
// with the same meaning, on top of a ConcurrentDictionary.
public class MembershipMap<TKey, TValue> : ConcurrentDictionary<TKey, TValue> where TKey : notnull
{
    public MembershipMap() { }

    public MembershipMap(IEnumerable<KeyValuePair<TKey, TValue>> items) : base(items) { }

    // Like Dictionary.Add: throws if the key is already present.
    public void Add(TKey key, TValue value)
    {
        if (!TryAdd(key, value))
            throw new ArgumentException($"An item with the same key has already been added. Key: {key}", nameof(key));
    }

    public bool Remove(TKey key) => TryRemove(key, out _);

    // Enumerates without taking the map's locks (Values would take all of them).
    public bool ContainsValue(TValue value)
    {
        var comparer = EqualityComparer<TValue>.Default;
        foreach (var pair in this)
        {
            if (comparer.Equals(pair.Value, value)) return true;
        }
        return false;
    }
}
