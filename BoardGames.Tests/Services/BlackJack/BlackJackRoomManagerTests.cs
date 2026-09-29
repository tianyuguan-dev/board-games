using BoardGames.Models.Poker;
using BoardGames.Services.BlackJack;
using BoardGames.Tests.Services;

namespace BoardGames.Tests.Services.BlackJack;

public class BlackJackRoomManagerTests
{
    private readonly BlackJackRoomManager _roomManager = new();

    private class CountingDeckFactory : IDeckFactory
    {
        public List<int> Calls { get; } = new();

        public Deck Create(int deckCount)
        {
            Calls.Add(deckCount);
            return new Deck(deckCount);
        }
    }

    [Fact]
    public void CreateRoom_UsesInjectedDeckFactory()
    {
        var factory = new CountingDeckFactory();
        var manager = new BlackJackRoomManager(factory);

        var room = manager.CreateRoom(3);

        Assert.Equal([3], factory.Calls);
        Assert.Equal(3 * 52, room.BlackJackTable.CardsRemaining);
    }

    [Fact]
    public async Task CrossRoomLookups_DuringConcurrentMembershipChanges_DoNotThrow()
    {
        var manager = new BlackJackRoomManager();
        var busy = new[] { manager.CreateRoom(4), manager.CreateRoom(4) };
        var target = manager.CreateRoom(4);
        var home = manager.CreateRoom(4);
        home.Players["stable"] = 0;
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
                    room.Players[conn] = i % 4;
                    room.Players.Remove(conn);
                }
                finally { room.Lock.Release(); }
            }
        }));
        // Readers scan every room without those rooms' locks, as FindRoomByConnectionId and JoinRoom's check do.
        var readers = Enumerable.Range(0, 4).Select(r => Task.Run(async () =>
        {
            while (DateTime.UtcNow < until)
            {
                Assert.Same(home, manager.FindRoomByConnectionId("stable"));
                Assert.True(manager.IsInAnyRoom("stable"));
                await target.Lock.WaitAsync();
                try
                {
                    var ex = Assert.Throws<InvalidOperationException>(() => manager.JoinRoom(target.RoomId, "stable"));
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
        var manager = new BlackJackRoomManager();

        var tasks = Enumerable.Range(0, 64).Select(_ => Task.Run(() =>
            Enumerable.Range(0, 50).Select(_ => manager.CreateRoom(1)).ToList()));
        var rooms = (await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10))).SelectMany(r => r).ToList();

        Assert.Equal(3200, rooms.Select(r => r.RoomId).Distinct().Count());
        Assert.All(rooms, r => Assert.Same(r, manager.GetRoom(r.RoomId)));
        Assert.Equal(3200, rooms.Select(r => r.BlackJackTable).Distinct(ReferenceEqualityComparer.Instance).Count());
    }

    [Fact]
    public void CreateRoom_IdTaken_RetriesWithNextId()
    {
        var manager = new BlackJackRoomManager(nextRoomId: ScriptedIds.Of(12345, 12345, 23456));

        var first = manager.CreateRoom(4);
        var second = manager.CreateRoom(4);

        Assert.Equal("12345", first.RoomId);
        Assert.Equal("23456", second.RoomId);
        Assert.Same(first, manager.GetRoom("12345"));
    }

    [Fact]
    public void CreateRoom_AllIdsTaken_ThrowsAfterRetryLimit()
    {
        var manager = new BlackJackRoomManager(nextRoomId: ScriptedIds.Of(12345));
        var first = manager.CreateRoom(4);

        Assert.Throws<InvalidOperationException>(() => manager.CreateRoom(4));
        Assert.Same(first, manager.GetRoom("12345"));
    }

    [Fact]
    public void CreateRoom_ReturnsRoomWithCorrectMaxPlayers()
    {
        var room = _roomManager.CreateRoom(4);

        Assert.Equal(4, room.MaxPlayers);
    }

    [Fact]
    public void CreateRoom_ReturnsRoomWithEmptyPlayers()
    {
        var room = _roomManager.CreateRoom(4);

        Assert.Empty(room.Players);
    }

    [Fact]
    public void CreateRoom_RoomCanBeFoundByGetRoom()
    {
        var room = _roomManager.CreateRoom(4);

        var found = _roomManager.GetRoom(room.RoomId);

        Assert.Same(room, found);
    }

    [Fact]
    public void GetRoom_ReturnsNullForNonExistentRoom()
    {
        var result = _roomManager.GetRoom("99999");

        Assert.Null(result);
    }

    [Fact]
    public void JoinRoom_AddsPlayerToRoom()
    {
        var room = _roomManager.CreateRoom(4);

        _roomManager.JoinRoom(room.RoomId, "conn-1");

        Assert.Single(room.Players);
        Assert.Equal(0, room.Players["conn-1"]);
    }

    [Fact]
    public void JoinRoom_AssignsIncrementingSeatNumbers()
    {
        var room = _roomManager.CreateRoom(4);

        _roomManager.JoinRoom(room.RoomId, "conn-1");
        _roomManager.JoinRoom(room.RoomId, "conn-2");
        _roomManager.JoinRoom(room.RoomId, "conn-3");

        Assert.Equal(0, room.Players["conn-1"]);
        Assert.Equal(1, room.Players["conn-2"]);
        Assert.Equal(2, room.Players["conn-3"]);
    }

    [Fact]
    public void JoinRoom_ThrowsWhenRoomDoesNotExist()
    {
        Assert.Throws<InvalidOperationException>(
            () => _roomManager.JoinRoom("99999", "conn-1"));
    }

    [Fact]
    public void JoinRoom_ThrowsWhenRoomIsFull()
    {
        var room = _roomManager.CreateRoom(2);
        _roomManager.JoinRoom(room.RoomId, "conn-1");
        _roomManager.JoinRoom(room.RoomId, "conn-2");

        Assert.Throws<InvalidOperationException>(
            () => _roomManager.JoinRoom(room.RoomId, "conn-3"));
    }

    [Fact]
    public void JoinRoom_ThrowsWhenGameInProgress()
    {
        var room = _roomManager.CreateRoom(4);
        _roomManager.JoinRoom(room.RoomId, "conn-1");
        room.BlackJackGame = room.BlackJackTable.NewRound(1);

        Assert.Throws<InvalidOperationException>(
            () => _roomManager.JoinRoom(room.RoomId, "conn-2"));
    }

    [Fact]
    public void JoinRoom_ThrowsWhenSamePlayerJoinsTwice()
    {
        var room = _roomManager.CreateRoom(4);
        _roomManager.JoinRoom(room.RoomId, "conn-1");

        Assert.Throws<InvalidOperationException>(
            () => _roomManager.JoinRoom(room.RoomId, "conn-1"));
    }

    [Fact]
    public void JoinRoom_AllowsJoinAfterGameFinished()
    {
        var room = _roomManager.CreateRoom(4);
        _roomManager.JoinRoom(room.RoomId, "conn-1");
        room.BlackJackGame = room.BlackJackTable.NewRound(1);
        room.BlackJackGame.PlaceBet(0, 10);
        room.BlackJackGame.Start();
        room.BlackJackGame.Stand();

        _roomManager.JoinRoom(room.RoomId, "conn-2");

        Assert.Equal(2, room.Players.Count);
    }

    [Fact]
    public void RemoveRoom_RoomNoLongerAccessible()
    {
        var room = _roomManager.CreateRoom(4);

        _roomManager.RemoveRoom(room.RoomId);

        Assert.Null(_roomManager.GetRoom(room.RoomId));
    }
}
