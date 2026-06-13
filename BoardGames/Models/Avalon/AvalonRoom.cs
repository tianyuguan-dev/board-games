namespace BoardGames.Models.Avalon;

public class DisconnectedPlayer
{
    public int UserId { get; set; }
    public int SeatIndex { get; set; }
    public string Nickname { get; set; } = "";
    public DateTime DisconnectedAt { get; set; }
}

public class AvalonRoom
{
    public string RoomId { get; init; }
    public int MaxPlayers { get; init; }
    public Dictionary<string, int> Players { get; set; } = new(); // connectionId => seatIndex
    public Dictionary<string, string> PlayerNicknames { get; set; } = new();
    public Dictionary<string, int> PlayerUserIds { get; set; } = new(); // connectionId => userId
    public string? HostConnectionId { get; set; }
    public HashSet<string> ReadyPlayers { get; init; } = new();        // Lobby ready (before game / game over)
    public HashSet<string> NightConfirmedPlayers { get; init; } = new(); // ConfirmNightReveal during game
    public AvalonGame? Game { get; set; }
    public int MordredCount { get; set; }
    public int OberonCount { get; set; }
    public int MinionCount { get; set; }
    public int MaxRejects { get; set; } = 4;
    public List<AvalonRole> RoleConfig { get; set; } = new();

    // Demo mode: room is auto-filled with scripted bots, guest plays Percival, plot ends after mission 1 with early assassination.
    public bool IsDemo { get; set; }

    // Maps seatIndex => connectionId for game-time lookups
    public Dictionary<int, string> SeatToConnection { get; set; } = new();
    public List<string> GamePlayerNames { get; set; } = new();
    public List<int> GamePlayerUserIds { get; set; } = new();
    private int _isSettled;
    public bool TrySetSettled() => Interlocked.CompareExchange(ref _isSettled, 1, 0) == 0;
    public void ResetSettled() => Interlocked.Exchange(ref _isSettled, 0);

    // Serializes all mutations/reads of this room's state across concurrent SignalR threads.
    // Plain Dictionary is not thread-safe; concurrent writes corrupt it (phantom null keys, etc.).
    public SemaphoreSlim Lock { get; } = new(1, 1);

    // Disconnected players awaiting reconnection (userId => info)
    public Dictionary<int, DisconnectedPlayer> DisconnectedPlayers { get; set; } = new();

    public DisconnectedPlayer? MarkDisconnected(string connectionId)
    {
        if (!Players.TryGetValue(connectionId, out int seatIndex)) return null;
        var userId = PlayerUserIds.GetValueOrDefault(connectionId);
        if (userId <= 0) return null;
        var nickname = PlayerNicknames.GetValueOrDefault(connectionId, "Player");
        var info = new DisconnectedPlayer
        {
            UserId = userId,
            SeatIndex = seatIndex,
            Nickname = nickname,
            DisconnectedAt = DateTime.UtcNow
        };
        DisconnectedPlayers[userId] = info;
        Players.Remove(connectionId);
        PlayerNicknames.Remove(connectionId);
        PlayerUserIds.Remove(connectionId);
        ReadyPlayers.Remove(connectionId);
        NightConfirmedPlayers.Remove(connectionId);
        if (HostConnectionId == connectionId)
            HostConnectionId = Players.Keys.FirstOrDefault();
        return info;
    }

    public DisconnectedPlayer? TryRejoin(string newConnectionId, int userId)
    {
        // Case 1: Player is in DisconnectedPlayers (normal reconnect)
        if (DisconnectedPlayers.TryGetValue(userId, out var info))
        {
            DisconnectedPlayers.Remove(userId);
            Players[newConnectionId] = info.SeatIndex;
            PlayerNicknames[newConnectionId] = info.Nickname;
            PlayerUserIds[newConnectionId] = userId;
            if (SeatToConnection.ContainsKey(info.SeatIndex))
                SeatToConnection[info.SeatIndex] = newConnectionId;
            if (HostConnectionId == null || !Players.ContainsKey(HostConnectionId))
                HostConnectionId = newConnectionId;
            return info;
        }

        // Case 2: Old connection still in Players (race condition on refresh)
        var oldConn = PlayerUserIds.FirstOrDefault(p => p.Value == userId).Key;
        if (oldConn != null && oldConn != newConnectionId)
        {
            var seatIndex = Players[oldConn];
            var nickname = PlayerNicknames.GetValueOrDefault(oldConn, "Player");
            // Swap connectionId
            Players.Remove(oldConn);
            PlayerNicknames.Remove(oldConn);
            PlayerUserIds.Remove(oldConn);
            ReadyPlayers.Remove(oldConn);
            NightConfirmedPlayers.Remove(oldConn);
            Players[newConnectionId] = seatIndex;
            PlayerNicknames[newConnectionId] = nickname;
            PlayerUserIds[newConnectionId] = userId;
            if (SeatToConnection.ContainsKey(seatIndex))
                SeatToConnection[seatIndex] = newConnectionId;
            if (HostConnectionId == oldConn)
                HostConnectionId = newConnectionId;
            return new DisconnectedPlayer
            {
                UserId = userId,
                SeatIndex = seatIndex,
                Nickname = nickname,
                DisconnectedAt = DateTime.UtcNow
            };
        }

        return null;
    }

    public AvalonRoom(string roomId, int maxPlayers)
    {
        RoomId = roomId;
        MaxPlayers = maxPlayers;
    }

    public void ReassignSeats()
    {
        var newPlayers = new Dictionary<string, int>();
        int seatIndex = 0;
        foreach (var player in Players.OrderBy(p => p.Value))
        {
            newPlayers[player.Key] = seatIndex;
            seatIndex++;
        }
        Players = newPlayers;
    }

    public void ApplyDefaultRoles()
    {
        if (!AvalonConfig.IsValidPlayerCount(MaxPlayers)) return;
        var defaults = AvalonConfig.GetDefaultRoles(MaxPlayers);
        MordredCount = defaults.Count(r => r == AvalonRole.Mordred);
        OberonCount = defaults.Count(r => r == AvalonRole.Oberon);
        MinionCount = defaults.Count(r => r == AvalonRole.MinionOfMordred);
        RebuildRoleConfig();
    }

    public void RebuildRoleConfig(int? overrideCount = null)
    {
        int total = overrideCount ?? MaxPlayers;
        if (!AvalonConfig.IsValidPlayerCount(total)) return;

        var evilRoles = new List<AvalonRole> { AvalonRole.Assassin, AvalonRole.Morgana };
        for (int i = 0; i < MordredCount; i++) evilRoles.Add(AvalonRole.Mordred);
        for (int i = 0; i < OberonCount; i++) evilRoles.Add(AvalonRole.Oberon);
        for (int i = 0; i < MinionCount; i++) evilRoles.Add(AvalonRole.MinionOfMordred);

        // Trim evil if it exceeds what's possible
        while (evilRoles.Count >= total) evilRoles.RemoveAt(evilRoles.Count - 1);

        int goodCount = total - evilRoles.Count;
        var goodRoles = new List<AvalonRole> { AvalonRole.Merlin, AvalonRole.Percival };
        while (goodRoles.Count > goodCount && goodRoles.Count > 1)
            goodRoles.RemoveAt(goodRoles.Count - 1);
        while (goodRoles.Count < goodCount) goodRoles.Add(AvalonRole.LoyalServant);

        RoleConfig = new List<AvalonRole>();
        RoleConfig.AddRange(goodRoles);
        RoleConfig.AddRange(evilRoles);
    }

    public void SwapSeats(int seatA, int seatB)
    {
        var connA = Players.FirstOrDefault(p => p.Value == seatA).Key;
        var connB = Players.FirstOrDefault(p => p.Value == seatB).Key;
        if (connA == null || connB == null) return;
        Players[connA] = seatB;
        Players[connB] = seatA;
    }

    public void MoveSeat(int fromSeat, int toSeat)
    {
        // Get ordered list of connectionIds by seat
        var ordered = Players.OrderBy(p => p.Value).Select(p => p.Key).ToList();
        if (fromSeat < 0 || fromSeat >= ordered.Count || toSeat < 0 || toSeat >= ordered.Count) return;
        var conn = ordered[fromSeat];
        ordered.RemoveAt(fromSeat);
        ordered.Insert(toSeat, conn);
        // Reassign sequential seats
        for (int i = 0; i < ordered.Count; i++)
            Players[ordered[i]] = i;
    }

    public void BuildSeatMap()
    {
        SeatToConnection = Players.ToDictionary(p => p.Value, p => p.Key);
        GamePlayerNames = Players
            .OrderBy(p => p.Value)
            .Select(p => PlayerNicknames.GetValueOrDefault(p.Key, "Player"))
            .ToList();
        GamePlayerUserIds = Players
            .OrderBy(p => p.Value)
            .Select(p => PlayerUserIds.GetValueOrDefault(p.Key, 0))
            .ToList();
        ResetSettled();
    }
}
