using BoardGames.Models.BlackJack;
using BoardGames.Services.BlackJack;
using Microsoft.Extensions.DependencyInjection;

namespace BoardGames.Tests.Integration;

// Drives TurnTimerService directly, so the ordering is forced rather than timed: the test holds the room lock
// before the timer starts, and SpyBlackJackRoomManager tells it when the timer has fetched the room.
public class TurnTimerClosedRoomTests(RoomLookupSpyWebApplicationFactory factory) : IClassFixture<RoomLookupSpyWebApplicationFactory>
{
    [Fact]
    public async Task TurnTimer_RoomClosedWhileWaiting_DoesNothing()
    {
        var rooms = factory.Services.GetRequiredService<SpyBlackJackRoomManager>();
        var timers = factory.Services.GetRequiredService<ITurnTimerService>();
        var room = rooms.CreateRoom(1);
        room.Players["player"] = 0;
        room.BlackJackGame = room.BlackJackTable.NewRound(1);
        room.BlackJackGame.PlaceBet(0, 10);
        room.BlackJackGame.Start(); // unshuffled deck: player 20 vs dealer 20, so it is the player's turn
        Assert.Equal(BlackJackGameState.PlayerTurn, room.BlackJackGame.State);

        var timerLookedUpRoom = rooms.NextLookup(room.RoomId);
        await room.Lock.WaitAsync();
        try
        {
            timers.StartTurnTimer(room.RoomId);
            // After 1 s the timer fetches the room, then queues on the lock we hold.
            await timerLookedUpRoom.WaitAsync(TimeSpan.FromSeconds(10));
            rooms.RemoveRoom(room.RoomId);
        }
        finally { room.Lock.Release(); }

        // The timer now gets the lock on a closed room. Without the guard it would auto-stand (and finish the round).
        await Task.Delay(500);
        Assert.Equal(BlackJackGameState.PlayerTurn, room.BlackJackGame.State);
        Assert.Equal(0, room.BlackJackGame.CurrentPlayerIndex);
    }
}
