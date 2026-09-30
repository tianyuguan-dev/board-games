using BoardGames.Models.Poker;

namespace BoardGames.Models.BlackJack;

public class BlackJackRoom
{
    public string RoomId { get; init; }
    public int MaxPlayers { get; init; }
    // Read by room-manager lookups from other threads; written only under Lock.
    public MembershipMap<string, int> Players { get; set; }
    public BlackJackTable BlackJackTable { get; set; }
    public BlackJackGame? BlackJackGame { get; set; }
    public HashSet<string> ReadyPlayers { get; init; } = new();
    public Dictionary<string, string> PlayerNicknames { get; set; } = new();
    public Dictionary<string, int> PlayerUserIds { get; set; } = new();
    public string? HostConnectionId { get; set; }
    public List<string> GamePlayerNames { get; set; } = new();
    public List<int> GamePlayerUserIds { get; set; } = new();
    // Connection IDs snapshot at game start, indexed by seat. Used for guest balance routing (in-memory
    // per-connection store). Stays stable across mid-game disconnects since we never mutate it after start.
    public List<string> GamePlayerConnectionIds { get; set; } = new();
    public List<decimal> GamePlayerBalances { get; set; } = new();
    private int _isSettled;
    public bool TrySetSettled() => Interlocked.CompareExchange(ref _isSettled, 1, 0) == 0;
    public void ResetSettled() => Interlocked.Exchange(ref _isSettled, 0);

    // Serializes all mutations/reads of this room's state across concurrent SignalR threads
    // and the background turn/betting timers. Plain Dictionary is not thread-safe.
    public SemaphoreSlim Lock { get; } = new(1, 1);

    // Set by the room manager's RemoveRoom (its callers hold Lock). Lock helpers check it right after acquiring Lock,
    // so work that was queued on the lock of a removed room does nothing.
    private int _closed;
    public bool IsClosed => Volatile.Read(ref _closed) == 1;
    public void MarkClosed() => Volatile.Write(ref _closed, 1);
    public BlackJackRoom(string roomId, int maxPlayers, IDeckFactory? deckFactory = null)
    {
        RoomId = roomId;
        MaxPlayers = maxPlayers;
        BlackJackTable = new BlackJackTable(maxPlayers, deckFactory);
        Players = new MembershipMap<string, int>();
    }

    // The player who takes over as host: the lowest seat still in the room, or null if the room is empty.
    public string? LowestSeatConnectionId() =>
        Players.OrderBy(p => p.Value).Select(p => p.Key).FirstOrDefault();

    public void ReassignSeats()
    {
        MembershipMap<string, int> newPlayers = new();
        int seatIndex = 0;
        // Compact in current seat order so relative seating is kept.
        foreach (var player in Players.OrderBy(p => p.Value))
        {
            newPlayers.Add(player.Key, seatIndex);
            seatIndex++;
        }
        Players =  newPlayers;
    }

}