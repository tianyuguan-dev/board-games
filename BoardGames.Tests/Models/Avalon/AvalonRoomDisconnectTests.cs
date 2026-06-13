using BoardGames.Models.Avalon;

namespace BoardGames.Tests.Models.Avalon;

public class AvalonRoomDisconnectTests
{
    private static AvalonRoom SeatedRoom(int playerCount = 5)
    {
        var room = new AvalonRoom("R1", playerCount);
        for (int i = 0; i < playerCount; i++)
        {
            var conn = $"c{i}";
            room.Players[conn] = i;
            room.PlayerNicknames[conn] = $"P{i}";
            room.PlayerUserIds[conn] = 100 + i;
        }
        room.HostConnectionId = "c0";
        return room;
    }

    [Fact]
    public void MarkDisconnected_MovesPlayerToDisconnectedList()
    {
        var room = SeatedRoom();
        var info = room.MarkDisconnected("c2");
        Assert.NotNull(info);
        Assert.Equal(2, info!.SeatIndex);
        Assert.Equal(102, info.UserId);
        Assert.False(room.Players.ContainsKey("c2"));
        Assert.True(room.DisconnectedPlayers.ContainsKey(102));
    }

    [Fact]
    public void MarkDisconnected_ReturnsNullForUnknownConnection()
    {
        var room = SeatedRoom();
        Assert.Null(room.MarkDisconnected("ghost"));
    }

    [Fact]
    public void MarkDisconnected_TransfersHostToFirstRemaining()
    {
        var room = SeatedRoom();
        room.MarkDisconnected("c0"); // host leaves
        Assert.NotNull(room.HostConnectionId);
        Assert.NotEqual("c0", room.HostConnectionId);
        Assert.Contains(room.HostConnectionId, room.Players.Keys);
    }

    [Fact]
    public void TryRejoin_RestoresOriginalSeat()
    {
        var room = SeatedRoom();
        room.MarkDisconnected("c2");

        var info = room.TryRejoin("new-conn", 102);
        Assert.NotNull(info);
        Assert.Equal(2, info!.SeatIndex);
        Assert.Equal(2, room.Players["new-conn"]);
        Assert.False(room.DisconnectedPlayers.ContainsKey(102));
    }

    [Fact]
    public void TryRejoin_SwapsConnectionId_OnRaceCondition()
    {
        // Case 2: user not in disconnected list but old connection still in Players
        var room = SeatedRoom();
        // Simulate page refresh: old connection still has the seat
        var info = room.TryRejoin("new-conn", 102);
        Assert.NotNull(info);
        Assert.Equal(2, info!.SeatIndex);
        Assert.False(room.Players.ContainsKey("c2"));
        Assert.True(room.Players.ContainsKey("new-conn"));
    }

    [Fact]
    public void TryRejoin_ReturnsNullForUnknownUser()
    {
        var room = SeatedRoom();
        Assert.Null(room.TryRejoin("new-conn", 9999));
    }

    [Fact]
    public void ReassignSeats_CompactsAfterMidGapDisconnect()
    {
        var room = SeatedRoom();
        room.MarkDisconnected("c1");
        room.MarkDisconnected("c3");
        room.ReassignSeats();
        Assert.Equal(new[] { 0, 1, 2 }, room.Players.Values.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void BuildSeatMap_ProducesSeatToConnectionMap()
    {
        var room = SeatedRoom();
        room.BuildSeatMap();
        Assert.Equal("c0", room.SeatToConnection[0]);
        Assert.Equal("c2", room.SeatToConnection[2]);
        Assert.Equal(5, room.GamePlayerNames.Count);
        Assert.Equal(5, room.GamePlayerUserIds.Count);
        Assert.Equal("P0", room.GamePlayerNames[0]);
        Assert.Equal(102, room.GamePlayerUserIds[2]);
    }

    [Fact]
    public void SwapSeats_SwapsTwoConnections()
    {
        var room = SeatedRoom();
        room.SwapSeats(1, 3);
        Assert.Equal(3, room.Players["c1"]);
        Assert.Equal(1, room.Players["c3"]);
    }

    [Fact]
    public void SwapSeats_NoOpForInvalidSeats()
    {
        var room = SeatedRoom();
        // Sanity: before
        var snapshot = room.Players.ToDictionary(p => p.Key, p => p.Value);
        room.SwapSeats(0, 99); // 99 not occupied
        Assert.Equal(snapshot, room.Players);
    }

    [Fact]
    public void MoveSeat_ShiftsPlayer()
    {
        var room = SeatedRoom();
        room.MoveSeat(0, 4); // move c0 from seat 0 to last
        Assert.Equal(4, room.Players["c0"]);
        Assert.Equal(0, room.Players["c1"]); // c1 shifted up
    }

    [Fact]
    public void MoveSeat_NoOpForOutOfRange()
    {
        var room = SeatedRoom();
        var snapshot = room.Players.ToDictionary(p => p.Key, p => p.Value);
        room.MoveSeat(0, 99);
        Assert.Equal(snapshot, room.Players);
    }

    [Fact]
    public void TrySetSettled_OnlyAllowsOneSettle()
    {
        var room = SeatedRoom();
        Assert.True(room.TrySetSettled());
        Assert.False(room.TrySetSettled());
        room.ResetSettled();
        Assert.True(room.TrySetSettled());
    }

    [Fact]
    public void ApplyDefaultRoles_PopulatesRoleConfig()
    {
        var room = SeatedRoom();
        room.ApplyDefaultRoles();
        Assert.Equal(5, room.RoleConfig.Count);
        Assert.Contains(AvalonRole.Merlin, room.RoleConfig);
        Assert.Contains(AvalonRole.Assassin, room.RoleConfig);
    }
}
