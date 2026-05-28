using BoardGames.Models.Avalon;
using BoardGames.Services.Avalon;

namespace BoardGames.Tests.Models.Avalon;

public class AvalonRoomSeatTests
{
    [Fact]
    public void JoinAfterMiddleDisconnect_DoesNotProduceDuplicateSeats()
    {
        var mgr = new AvalonRoomManager();
        var room = mgr.CreateRoom(5);

        for (int i = 0; i < 5; i++)
        {
            var conn = $"c{i}";
            mgr.JoinRoom(room.RoomId, conn);
            room.PlayerUserIds[conn] = i + 1;
        }
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, room.Players.Values.OrderBy(x => x).ToArray());

        // Middle player (seat 2) disconnects, leaving a gap. Seat 2 is reserved.
        var info = room.MarkDisconnected("c2");
        Assert.NotNull(info);
        Assert.Equal(2, info!.SeatIndex);

        // New player joins. Must take the gap (seat 2), not Players.Count (which would collide with seat 4).
        mgr.JoinRoom(room.RoomId, "c5");

        var seats = room.Players.Values.OrderBy(x => x).ToList();
        Assert.Equal(seats.Count, seats.Distinct().Count());
        Assert.Equal(2, room.Players["c5"]);
    }

    [Fact]
    public void ReassignSeats_NormalizesDuplicatesAndGaps_SoBuildSeatMapSucceeds()
    {
        var room = new AvalonRoom("12345", 5);
        // Corrupted state: duplicate seat 4, gap at 2 (the exact shape that crashed BuildSeatMap)
        room.Players["a"] = 0;
        room.Players["b"] = 1;
        room.Players["c"] = 4;
        room.Players["d"] = 4;
        room.Players["e"] = 3;

        room.ReassignSeats();

        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, room.Players.Values.OrderBy(x => x).ToArray());
        var ex = Record.Exception(() => room.BuildSeatMap());
        Assert.Null(ex);
    }

    [Fact]
    public void ReassignSeats_PreservesRelativeOrderBySeat()
    {
        var room = new AvalonRoom("12345", 5);
        // Gapped but ordered: a<b<c<d by seat
        room.Players["a"] = 0;
        room.Players["b"] = 2;
        room.Players["c"] = 5;
        room.Players["d"] = 9;

        room.ReassignSeats();

        Assert.Equal(0, room.Players["a"]);
        Assert.Equal(1, room.Players["b"]);
        Assert.Equal(2, room.Players["c"]);
        Assert.Equal(3, room.Players["d"]);
    }
}
