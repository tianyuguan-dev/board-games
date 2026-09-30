using BoardGames.Models.Avalon;
using BoardGames.Services.Avalon;
using BoardGames.Tests.Services;

namespace BoardGames.Tests.Services.Avalon;

public class AvalonRoomManagerTests
{
    [Fact]
    public void CreateRoom_GeneratesUniqueRoomIds()
    {
        var mgr = new AvalonRoomManager();
        var ids = new HashSet<string>();
        for (int i = 0; i < 20; i++)
            ids.Add(mgr.CreateRoom(5).RoomId);
        Assert.Equal(20, ids.Count);
    }

    [Fact]
    public void IsInAnyRoom_TrueOnlyForSeatedConnection()
    {
        var mgr = new AvalonRoomManager();
        var room = mgr.CreateRoom(5);
        room.Players["seated"] = 0;

        Assert.True(mgr.IsInAnyRoom("seated"));
        Assert.False(mgr.IsInAnyRoom("stranger"));

        room.Players.Remove("seated");
        Assert.False(mgr.IsInAnyRoom("seated"));
    }

    [Fact]
    public async Task CrossRoomLookups_DuringConcurrentMembershipChanges_DoNotThrow()
    {
        var mgr = new AvalonRoomManager();
        var busy = new[] { mgr.CreateRoom(5), mgr.CreateRoom(5) };
        var target = mgr.CreateRoom(5);
        var home = mgr.CreateRoom(5);
        home.Players["stable"] = 0;
        home.PlayerUserIds["stable"] = 777;
        var until = DateTime.UtcNow.AddSeconds(3);

        // Writers change membership the way the hub does: only under the room's own lock.
        var writers = Enumerable.Range(0, 4).Select(w => Task.Run(async () =>
        {
            for (var i = 0; DateTime.UtcNow < until; i++)
            {
                var room = busy[i % 2];
                var conn = $"w{w}-{i % 20}";
                await room.Lock.WaitAsync();
                try
                {
                    room.Players[conn] = i % 10;
                    room.PlayerUserIds[conn] = 1000 + w;
                    room.DisconnectedPlayers[1000 + w] = new DisconnectedPlayer { UserId = 1000 + w };
                    room.Players.Remove(conn);
                    room.PlayerUserIds.Remove(conn);
                    room.DisconnectedPlayers.Remove(1000 + w);
                }
                finally { room.Lock.Release(); }
            }
        }));
        // Readers scan every room without those rooms' locks, as FindRoom* and JoinRoom's check do.
        var readers = Enumerable.Range(0, 4).Select(r => Task.Run(async () =>
        {
            while (DateTime.UtcNow < until)
            {
                Assert.Equal((home.RoomId, 0), mgr.FindRoomByConnectionId("stable"));
                Assert.Equal(home.RoomId, mgr.FindRoomByUserId(777));
                await target.Lock.WaitAsync();
                try
                {
                    var ex = Assert.Throws<InvalidOperationException>(() => mgr.JoinRoom(target.RoomId, "stable"));
                    Assert.Equal("Player is already in a room", ex.Message);
                }
                finally { target.Lock.Release(); }
            }
        }));

        await Task.WhenAll(writers.Concat(readers)).WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task CreateRoom_ConcurrentCalls_AllDistinctAndRegistered()
    {
        var mgr = new AvalonRoomManager();

        var tasks = Enumerable.Range(0, 64).Select(_ => Task.Run(() =>
            Enumerable.Range(0, 50).Select(_ => mgr.CreateRoom(5)).ToList()));
        var rooms = (await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10))).SelectMany(r => r).ToList();

        Assert.Equal(3200, rooms.Select(r => r.RoomId).Distinct().Count());
        Assert.All(rooms, r => Assert.Same(r, mgr.GetRoom(r.RoomId)));
    }

    [Fact]
    public void CreateRoom_IdTaken_RetriesWithNextId()
    {
        var mgr = new AvalonRoomManager(ScriptedIds.Of(12345, 12345, 23456));

        var first = mgr.CreateRoom(5);
        var second = mgr.CreateRoom(5);

        Assert.Equal("12345", first.RoomId);
        Assert.Equal("23456", second.RoomId);
        Assert.Same(first, mgr.GetRoom("12345"));
    }

    [Fact]
    public void CreateRoom_AllIdsTaken_ThrowsAfterRetryLimit()
    {
        var mgr = new AvalonRoomManager(ScriptedIds.Of(12345));
        var first = mgr.CreateRoom(5);

        Assert.Throws<InvalidOperationException>(() => mgr.CreateRoom(5));
        Assert.Same(first, mgr.GetRoom("12345"));
    }

    [Theory]
    [InlineData(4, 5)]    // clamped up
    [InlineData(11, 10)]  // clamped down
    [InlineData(5, 5)]
    [InlineData(10, 10)]
    public void CreateRoom_ClampsMaxPlayersTo_5_To_10(int requested, int expected)
    {
        var mgr = new AvalonRoomManager();
        var room = mgr.CreateRoom(requested);
        Assert.Equal(expected, room.MaxPlayers);
    }

    [Fact]
    public void GetRoom_ReturnsNullForUnknownId()
    {
        var mgr = new AvalonRoomManager();
        Assert.Null(mgr.GetRoom("nonexistent"));
    }

    [Fact]
    public void GetRoom_ReturnsCreatedRoom()
    {
        var mgr = new AvalonRoomManager();
        var created = mgr.CreateRoom(5);
        var fetched = mgr.GetRoom(created.RoomId);
        Assert.Same(created, fetched);
    }

    [Fact]
    public void JoinRoom_AddsPlayerWithLowestFreeSeat()
    {
        var mgr = new AvalonRoomManager();
        var room = mgr.CreateRoom(5);
        // Pretend seat 0 is occupied by another player
        room.Players["existing"] = 0;

        mgr.JoinRoom(room.RoomId, "new-conn");

        Assert.Equal(1, room.Players["new-conn"]);
    }

    [Fact]
    public void JoinRoom_FillsGapsInsteadOfPlayersCount()
    {
        var mgr = new AvalonRoomManager();
        var room = mgr.CreateRoom(5);
        room.Players["a"] = 0;
        room.Players["c"] = 2; // gap at seat 1

        mgr.JoinRoom(room.RoomId, "b");

        Assert.Equal(1, room.Players["b"]); // not Players.Count = 2
    }

    [Fact]
    public void JoinRoom_ThrowsIfAlreadyInAnyRoom()
    {
        var mgr = new AvalonRoomManager();
        var room1 = mgr.CreateRoom(5);
        var room2 = mgr.CreateRoom(5);
        room1.Players["dup"] = 0;
        Assert.Throws<InvalidOperationException>(() =>
            mgr.JoinRoom(room2.RoomId, "dup"));
    }

    [Fact]
    public void JoinRoom_ThrowsIfRoomFull()
    {
        var mgr = new AvalonRoomManager();
        var room = mgr.CreateRoom(5);
        for (int i = 0; i < 5; i++) room.Players["conn" + i] = i;
        Assert.Throws<InvalidOperationException>(() =>
            mgr.JoinRoom(room.RoomId, "overflow"));
    }

    [Fact]
    public void JoinRoom_ThrowsIfGameInProgress()
    {
        var mgr = new AvalonRoomManager();
        var room = mgr.CreateRoom(5);
        room.Game = new AvalonGame(5, AvalonConfig.GetDefaultRoles(5));
        // Default phase is NightReveal (not GameOver)
        Assert.Throws<InvalidOperationException>(() =>
            mgr.JoinRoom(room.RoomId, "latecomer"));
    }

    [Fact]
    public void FindRoomByConnectionId_ReturnsSeatAndRoom()
    {
        var mgr = new AvalonRoomManager();
        var room = mgr.CreateRoom(5);
        room.Players["x"] = 3;
        var (rid, seat) = mgr.FindRoomByConnectionId("x");
        Assert.Equal(room.RoomId, rid);
        Assert.Equal(3, seat);
    }

    [Fact]
    public void FindRoomByConnectionId_ReturnsNullForUnknownConn()
    {
        var mgr = new AvalonRoomManager();
        mgr.CreateRoom(5);
        var (rid, seat) = mgr.FindRoomByConnectionId("ghost");
        Assert.Null(rid);
        Assert.Equal(-1, seat);
    }

    [Fact]
    public void FindRoomByUserId_LooksAtPlayerUserIdsAndDisconnected()
    {
        var mgr = new AvalonRoomManager();
        var room = mgr.CreateRoom(5);
        room.PlayerUserIds["c1"] = 42;
        Assert.Equal(room.RoomId, mgr.FindRoomByUserId(42));

        var room2 = mgr.CreateRoom(5);
        room2.DisconnectedPlayers[55] = new DisconnectedPlayer { UserId = 55, SeatIndex = 0, Nickname = "x" };
        Assert.Equal(room2.RoomId, mgr.FindRoomByUserId(55));

        Assert.Null(mgr.FindRoomByUserId(9999));
    }

    [Fact]
    public void RemoveRoom_DeletesRoom()
    {
        var mgr = new AvalonRoomManager();
        var room = mgr.CreateRoom(5);
        mgr.RemoveRoom(room.RoomId);
        Assert.Null(mgr.GetRoom(room.RoomId));
    }
}
