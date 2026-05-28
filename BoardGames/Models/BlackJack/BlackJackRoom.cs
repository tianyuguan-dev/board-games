namespace BoardGames.Models.BlackJack;

public class BlackJackRoom
{
    public string RoomId { get; init; }
    public int MaxPlayers { get; init; }
    public Dictionary<string, int> Players { get; set; }
    public BlackJackTable BlackJackTable { get; set; }
    public BlackJackGame? BlackJackGame { get; set; }
    public HashSet<string> ReadyPlayers { get; init; } = new();
    public Dictionary<string, string> PlayerNicknames { get; set; } = new();
    public Dictionary<string, int> PlayerUserIds { get; set; } = new();
    public string? HostConnectionId { get; set; }
    public List<string> GamePlayerNames { get; set; } = new();
    public List<int> GamePlayerUserIds { get; set; } = new();
    public List<decimal> GamePlayerBalances { get; set; } = new();
    private int _isSettled;
    public bool TrySetSettled() => Interlocked.CompareExchange(ref _isSettled, 1, 0) == 0;
    public void ResetSettled() => Interlocked.Exchange(ref _isSettled, 0);

    // Serializes all mutations/reads of this room's state across concurrent SignalR threads
    // and the background turn/betting timers. Plain Dictionary is not thread-safe.
    public SemaphoreSlim Lock { get; } = new(1, 1);
    public BlackJackRoom(string roomId, int maxPlayers)
    {
        RoomId = roomId;
        MaxPlayers = maxPlayers;
        BlackJackTable = new BlackJackTable(maxPlayers);
        Players = new Dictionary<string, int>();
    }

    public void ReassignSeats()
    {
        Dictionary<string, int> newPlayers = new();
        int seatIndex = 0;
        foreach (var player in Players)
        {
            newPlayers.Add(player.Key, seatIndex);
            seatIndex++;
        }
        Players =  newPlayers;
    }

}