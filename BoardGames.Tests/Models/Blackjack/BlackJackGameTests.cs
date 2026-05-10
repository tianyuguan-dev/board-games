using BoardGames.Models.BlackJack;
using BoardGames.Models.Poker;

namespace BoardGames.Tests.Models.Blackjack;

public class BlackJackGameTests
{
    private static Deck CreateShuffledDeck()
    {
        var deck = new Deck();
        deck.Shuffle();
        return deck;
    }

    [Fact]
    public void Constructor_ThrowsException_WhenPlayerCountLessThan1()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BlackJackGame(CreateShuffledDeck(), playerCount: 0));
    }

    [Fact]
    public void Start_SetsStateToPlayerTurn()
    {
        var game = new BlackJackGame(CreateShuffledDeck());
        game.Start();

        Assert.Equal(BlackJackGameState.PlayerTurn, game.State);
    }

    [Fact]
    public void Start_CreatesResultsForEachPlayer()
    {
        var game = new BlackJackGame(CreateShuffledDeck(), playerCount: 3);
        game.Start();

        Assert.Equal(3, game.Results.Count);
    }

    [Fact]
    public void Hit_DoesNothing_WhenGameFinished()
    {
        var game = new BlackJackGame(CreateShuffledDeck());
        game.Start();
        game.Stand();

        var stateBefore = game.State;
        game.Hit();

        Assert.Equal(stateBefore, game.State);
    }

    [Fact]
    public void Stand_SinglePlayer_FinishesGame()
    {
        var game = new BlackJackGame(CreateShuffledDeck());
        game.Start();
        game.Stand();

        Assert.Equal(BlackJackGameState.Finished, game.State);
        Assert.True(game.Results[0].HasValue);
    }

    [Fact]
    public void Stand_MultiplePlayer_FinishesAfterAllStand()
    {
        var game = new BlackJackGame(CreateShuffledDeck(), playerCount: 2);
        game.Start();

        game.Stand();
        Assert.NotEqual(BlackJackGameState.Finished, game.State);

        game.Stand();
        Assert.Equal(BlackJackGameState.Finished, game.State);
        Assert.True(game.Results[0].HasValue);
        Assert.True(game.Results[1].HasValue);
    }

    [Fact]
    public void Game_ResultIsPush_WhenSameValue()
    {
        var pushFound = false;
        for (var i = 0; i < 1000; i++)
        {
            var game = new BlackJackGame(CreateShuffledDeck());
            game.Start();
            game.Stand();
            if (game.Results[0] == BlackJackGameResult.Push)
            {
                pushFound = true;
                break;
            }
        }
        Assert.True(pushFound, "Push result should be possible");
    }

    [Fact]
    public void Game_PlayerBust_DealerWins()
    {
        var bustFound = false;
        for (var i = 0; i < 100; i++)
        {
            var game = new BlackJackGame(CreateShuffledDeck());
            game.Start();

            while (game.State == BlackJackGameState.PlayerTurn)
            {
                game.Hit();
            }

            if (game.Results[0] == BlackJackGameResult.DealerWin)
            {
                bustFound = true;
                break;
            }
        }
        Assert.True(bustFound, "Player bust leading to dealer win should be possible");
    }

    [Fact]
    public void Game_AllResultsHaveValue_WhenFinished()
    {
        var game = new BlackJackGame(CreateShuffledDeck(), playerCount: 3);
        game.Start();

        for (var i = 0; i < 3; i++)
        {
            if (game.State == BlackJackGameState.PlayerTurn)
                game.Stand();
        }

        Assert.Equal(BlackJackGameState.Finished, game.State);
        Assert.All(game.Results, r => Assert.True(r.HasValue));
    }

    [Fact]
    public void Game_ResultIsValid_OnlyThreeOutcomes()
    {
        var game = new BlackJackGame(CreateShuffledDeck());
        game.Start();
        game.Stand();

        var result = game.Results[0];
        Assert.True(
            result == BlackJackGameResult.PlayerWin ||
            result == BlackJackGameResult.DealerWin ||
            result == BlackJackGameResult.Push
        );
    }

    [Fact]
    public void Game_SharesDeck_AcrossMultipleRounds()
    {
        var deck = new Deck(count: 6);
        deck.Shuffle();
        var initialRemaining = deck.Remaining;

        var game1 = new BlackJackGame(deck);
        game1.Start();
        game1.Stand();

        var afterFirstRound = deck.Remaining;
        Assert.True(afterFirstRound < initialRemaining);

        var game2 = new BlackJackGame(deck);
        game2.Start();
        game2.Stand();

        Assert.True(deck.Remaining < afterFirstRound);
    }
}
