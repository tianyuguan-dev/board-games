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

    // Ranks in deal order: P0 card 1, P0 card 2, P1 card 1, ..., dealer card 1, dealer card 2, then any draws.
    private static Deck CreateStackedDeck(params Rank[] dealOrder)
    {
        return new Deck(dealOrder.Reverse().Select(r => new Card { Suit = Suit.Spade, Rank = r }));
    }

    [Fact]
    public void Constructor_ThrowsException_WhenPlayerCountLessThan1()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BlackJackGame(CreateShuffledDeck(), playerCount: 0));
    }

    [Fact]
    public void Start_SetsStateToPlayerTurn()
    {
        var game = new BlackJackGame(new Deck());
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
    public void Start_PlayerNatural_WinsAndIsSkipped()
    {
        var deck = CreateStackedDeck(
            Rank.Ace, Rank.King,   // P0: natural
            Rank.Ten, Rank.Nine,   // P1: 19
            Rank.Ten, Rank.Eight); // dealer: 18
        var game = new BlackJackGame(deck, playerCount: 2);
        game.Start();

        Assert.Equal(BlackJackGameResult.PlayerWin, game.Results[0]);
        Assert.Null(game.Results[1]);
        Assert.Equal(1, game.CurrentPlayerIndex);
        Assert.Equal(BlackJackGameState.PlayerTurn, game.State);
    }

    [Fact]
    public void Start_DealerNatural_FinishesRound()
    {
        var deck = CreateStackedDeck(
            Rank.Ten, Rank.Nine,   // P0: 19
            Rank.Ten, Rank.Eight,  // P1: 18
            Rank.Ace, Rank.King);  // dealer: natural
        var game = new BlackJackGame(deck, playerCount: 2);
        game.Start();

        Assert.Equal(BlackJackGameState.Finished, game.State);
        Assert.Equal(BlackJackGameResult.DealerWin, game.Results[0]);
        Assert.Equal(BlackJackGameResult.DealerWin, game.Results[1]);
    }

    [Fact]
    public void Start_BothNatural_IsPush()
    {
        var deck = CreateStackedDeck(
            Rank.Ace, Rank.King,   // P0: natural
            Rank.Ace, Rank.Queen); // dealer: natural
        var game = new BlackJackGame(deck);
        game.Start();

        Assert.Equal(BlackJackGameState.Finished, game.State);
        Assert.Equal(BlackJackGameResult.Push, game.Results[0]);
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
        var game = new BlackJackGame(new Deck(), playerCount: 2);
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
    public void ForfeitPlayer_SkipsCurrentPlayer()
    {
        var game = new BlackJackGame(new Deck(), playerCount: 3);
        game.Start();

        // Player 0 is current, forfeit player 0
        Assert.Equal(0, game.CurrentPlayerIndex);
        game.ForfeitPlayer(0);

        // Should skip to player 1 (or further if player 1 has blackjack)
        Assert.NotEqual(0, game.CurrentPlayerIndex);
    }

    [Fact]
    public void ForfeitPlayer_DoesNotSkipWhenNotCurrentPlayer()
    {
        var game = new BlackJackGame(CreateShuffledDeck(), playerCount: 3);
        game.Start();

        // Player 0 is current, forfeit player 2
        Assert.Equal(0, game.CurrentPlayerIndex);
        game.ForfeitPlayer(2);

        // Current player should still be 0
        Assert.Equal(0, game.CurrentPlayerIndex);
    }

    [Fact]
    public void ForfeitPlayer_SkippedWhenNextPlayerReaches()
    {
        var game = new BlackJackGame(new Deck(), playerCount: 3);
        game.Start();

        // Forfeit player 1 while player 0 is active
        game.ForfeitPlayer(1);

        // Player 0 stands, should skip player 1 and go to player 2
        game.Stand();

        // Should be at player 2 or finished (if player 2 had blackjack)
        Assert.True(game.CurrentPlayerIndex >= 2);
    }

    [Fact]
    public void ForfeitPlayer_AllForfeited_FinishesGame()
    {
        var game = new BlackJackGame(CreateShuffledDeck(), playerCount: 2);
        game.Start();

        game.ForfeitPlayer(0);
        game.ForfeitPlayer(1);

        Assert.Equal(BlackJackGameState.Finished, game.State);
    }

    // Betting tests

    [Fact]
    public void Constructor_InitializesBettingState()
    {
        var game = new BlackJackGame(new Deck());
        Assert.Equal(BlackJackGameState.Betting, game.State);
    }

    [Fact]
    public void PlaceBet_SetsBetAmount()
    {
        var game = new BlackJackGame(new Deck(), playerCount: 2);
        game.PlaceBet(0, 50);
        game.PlaceBet(1, 30);

        Assert.Equal(50, game.Bets[0]);
        Assert.Equal(30, game.Bets[1]);
    }

    [Fact]
    public void PlaceBet_IgnoresInvalidAmount()
    {
        var game = new BlackJackGame(new Deck());
        game.PlaceBet(0, 0);
        Assert.Equal(0, game.Bets[0]);

        game.PlaceBet(0, 200);
        Assert.Equal(0, game.Bets[0]);
    }

    [Fact]
    public void PlaceBet_IgnoresInvalidPlayerIndex()
    {
        var game = new BlackJackGame(new Deck());
        game.PlaceBet(-1, 10);
        game.PlaceBet(5, 10);
        // No exception thrown
    }

    [Fact]
    public void PlaceBet_IgnoresWhenNotBetting()
    {
        var game = new BlackJackGame(new Deck());
        game.PlaceBet(0, 10);
        game.Start();

        game.PlaceBet(0, 50);
        Assert.Equal(10, game.Bets[0]);
    }

    [Fact]
    public void AllBetsPlaced_ReturnsTrueWhenAllBet()
    {
        var game = new BlackJackGame(new Deck(), playerCount: 2);
        Assert.False(game.AllBetsPlaced());

        game.PlaceBet(0, 10);
        Assert.False(game.AllBetsPlaced());

        game.PlaceBet(1, 10);
        Assert.True(game.AllBetsPlaced());
    }

    [Fact]
    public void AllBetsPlaced_SkipsForfeitedPlayers()
    {
        var game = new BlackJackGame(new Deck(), playerCount: 2);
        game.ForfeitPlayer(1);
        game.PlaceBet(0, 10);

        Assert.True(game.AllBetsPlaced());
    }

    [Fact]
    public void AutoBetRemaining_FillsUnplacedBets()
    {
        var game = new BlackJackGame(new Deck(), playerCount: 3);
        game.PlaceBet(0, 50);
        game.AutoBetRemaining(5);

        Assert.Equal(50, game.Bets[0]);
        Assert.Equal(5, game.Bets[1]);
        Assert.Equal(5, game.Bets[2]);
    }

    [Fact]
    public void Start_IgnoredWhenNotBetting()
    {
        var game = new BlackJackGame(new Deck());
        game.PlaceBet(0, 10);
        game.Start();
        var state = game.State;

        game.Start(); // second call should be ignored
        Assert.Equal(state, game.State);
    }

    // Double Down tests

    [Fact]
    public void CanDoubleDown_TrueWithTwoCards()
    {
        var game = new BlackJackGame(new Deck());
        game.PlaceBet(0, 10);
        game.Start();

        if (game.State == BlackJackGameState.PlayerTurn)
            Assert.True(game.CanDoubleDown());
    }

    [Fact]
    public void CanDoubleDown_FalseAfterHit()
    {
        var game = new BlackJackGame(new Deck());
        game.PlaceBet(0, 10);
        game.Start();

        if (game.State == BlackJackGameState.PlayerTurn)
        {
            game.Hit();
            if (game.State == BlackJackGameState.PlayerTurn)
                Assert.False(game.CanDoubleDown());
        }
    }

    [Fact]
    public void DoubleDown_DoublesBetAndDealsOneCard()
    {
        var game = new BlackJackGame(new Deck());
        game.PlaceBet(0, 10);
        game.Start();

        if (game.State == BlackJackGameState.PlayerTurn)
        {
            game.DoubleDown();
            Assert.Equal(20, game.Bets[0]);
            Assert.Equal(3, game.PlayerHands[0].Cards.Count);
        }
    }

    [Fact]
    public void DoubleDown_AutoStandsAfter()
    {
        var game = new BlackJackGame(new Deck());
        game.PlaceBet(0, 10);
        game.Start();

        if (game.State == BlackJackGameState.PlayerTurn)
        {
            game.DoubleDown();
            // Single player: game should be finished after double down
            Assert.Equal(BlackJackGameState.Finished, game.State);
        }
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
