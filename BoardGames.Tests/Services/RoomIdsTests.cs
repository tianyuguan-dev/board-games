using System.Collections.Concurrent;
using BoardGames.Services;

namespace BoardGames.Tests.Services;

public class RoomIdsTests
{
    private record TestRoom(string Id);

    [Fact]
    public void Register_ReturnsRegisteredRoomWithFiveDigitId()
    {
        var rooms = new ConcurrentDictionary<string, TestRoom>();

        var room = RoomIds.Register(rooms, id => new TestRoom(id));

        Assert.InRange(int.Parse(room.Id), 10000, 99999);
        Assert.Same(room, rooms[room.Id]);
    }

    [Fact]
    public void Register_IdTaken_UsesNextId_AndKeepsExistingRoom()
    {
        var existing = new TestRoom("12345");
        var rooms = new ConcurrentDictionary<string, TestRoom>();
        rooms.TryAdd("12345", existing);
        var created = new List<string>();

        var room = RoomIds.Register(rooms, id => { created.Add(id); return new TestRoom(id); }, ScriptedIds.Of(12345, 23456));

        Assert.Equal("23456", room.Id);
        Assert.Same(existing, rooms["12345"]);
        Assert.Equal(["23456"], created); // no room is built for the taken id
    }

    [Fact]
    public void Register_AllIdsTaken_ThrowsAfter100Attempts_AndRegistersNothing()
    {
        var rooms = new ConcurrentDictionary<string, TestRoom>();
        rooms.TryAdd("12345", new TestRoom("12345"));
        var draws = 0;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            RoomIds.Register(rooms, id => new TestRoom(id), () => { draws++; return 12345; }));

        Assert.Equal("Could not allocate a room id", ex.Message);
        Assert.Equal(RoomIds.MaxAttempts, draws);
        Assert.Single(rooms);
    }

    [Fact]
    public async Task Register_ConcurrentCalls_AllDistinctAndRegistered()
    {
        var rooms = new ConcurrentDictionary<string, TestRoom>();

        var tasks = Enumerable.Range(0, 64).Select(_ => Task.Run(() =>
            Enumerable.Range(0, 50).Select(_ => RoomIds.Register(rooms, id => new TestRoom(id))).ToList()));
        var results = (await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10))).SelectMany(r => r).ToList();

        Assert.Equal(3200, results.Count);
        Assert.Equal(3200, results.Select(r => r.Id).Distinct().Count());
        Assert.All(results, r => Assert.Same(r, rooms[r.Id]));
    }
}
