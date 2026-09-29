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
