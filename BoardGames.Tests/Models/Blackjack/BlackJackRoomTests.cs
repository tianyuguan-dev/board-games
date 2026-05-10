using BoardGames.Models.BlackJack;

namespace BoardGames.Tests.Models.Blackjack;

public class BlackJackRoomTests
{
    [Fact]
    public void ReassignSeats_ReindexesFromZero()
    {
        var room = new BlackJackRoom("12345", 4);
        room.Players.Add("conn-1", 0);
        room.Players.Add("conn-3", 2);
        room.Players.Add("conn-5", 4);

        room.ReassignSeats();

        Assert.Equal(0, room.Players["conn-1"]);
        Assert.Equal(1, room.Players["conn-3"]);
        Assert.Equal(2, room.Players["conn-5"]);
    }

    [Fact]
    public void ReassignSeats_NoChangeWhenAlreadySequential()
    {
        var room = new BlackJackRoom("12345", 4);
        room.Players.Add("conn-1", 0);
        room.Players.Add("conn-2", 1);

        room.ReassignSeats();

        Assert.Equal(0, room.Players["conn-1"]);
        Assert.Equal(1, room.Players["conn-2"]);
    }

    [Fact]
    public void ReassignSeats_WorksWithEmptyPlayers()
    {
        var room = new BlackJackRoom("12345", 4);

        room.ReassignSeats();

        Assert.Empty(room.Players);
    }
}
