using BoardGames.Models.BlackJack;
using BoardGames.Models.Poker;

namespace BoardGames.Tests.Models.Blackjack;

public class BlackJackTableTests
{
    // Records every Create call. The first deck can be cut to a given size to put the table at its reshuffle threshold.
    private class CountingDeckFactory(int? firstDeckSize = null) : IDeckFactory
    {
        public List<int> Calls { get; } = new();

        public Deck Create(int deckCount)
        {
            Calls.Add(deckCount);
            if (Calls.Count == 1 && firstDeckSize is int size)
                return new Deck(Enumerable.Range(0, size).Select(_ => new Card { Suit = Suit.Spade, Rank = Rank.Two }));
            return new Deck(deckCount);
        }
    }

    private static void PlaceBetsAndStart(BlackJackGame game, int playerCount)
    {
        for (int i = 0; i < playerCount; i++)
            game.PlaceBet(i, 10);
        game.Start();
    }

    [Fact]
    public void NewRound_ReturnsGameInBettingState()
    {
        var table = new BlackJackTable(deckCount: 6);

        var game = table.NewRound(1);

        Assert.Equal(BlackJackGameState.Betting, game.State);
    }

    [Fact]
    public void NewRound_AfterBetsAndStart_ReturnsGameWithCorrectPlayerCount()
    {
        var table = new BlackJackTable(deckCount: 6);

        var game = table.NewRound(3);
        PlaceBetsAndStart(game, 3);

        Assert.Equal(3, game.Results.Count);
    }

    [Fact]
    public void NewRound_SharesDeckAcrossRounds()
    {
        var table = new BlackJackTable(deckCount: 6);

        var game1 = table.NewRound(1);
        PlaceBetsAndStart(game1, 1);
        game1.Stand();

        var game2 = table.NewRound(1);
        PlaceBetsAndStart(game2, 1);
        game2.Stand();

        Assert.Equal(BlackJackGameState.Finished, game1.State);
        Assert.Equal(BlackJackGameState.Finished, game2.State);
    }

    [Fact]
    public void NewRound_ReshufflesWhenDeckRunsLow()
    {
        // 1 deck = 52 cards, threshold = 13
        // Each round uses at least 4 cards (2 player + 2 dealer), ~10 rounds triggers reshuffle
        var table = new BlackJackTable(deckCount: 1);

        // Should not throw; deck auto-reshuffles when running low
        for (var i = 0; i < 50; i++)
        {
            var game = table.NewRound(1);
            PlaceBetsAndStart(game, 1);
            game.Stand();
        }
    }

    [Fact]
    public void NewRound_MultiplePlayersConsumesMoreCards()
    {
        // 3 players + dealer = at least 8 cards/round
        // 1 deck = 52 cards, threshold = 13, ~5 rounds triggers reshuffle
        var table = new BlackJackTable(deckCount: 1);

        for (var i = 0; i < 20; i++)
        {
            var game = table.NewRound(3);
            PlaceBetsAndStart(game, 3);
            // All players stand
            while (game.State == BlackJackGameState.PlayerTurn)
            {
                game.Stand();
            }
            Assert.Equal(BlackJackGameState.Finished, game.State);
        }
    }

    [Fact]
    public void Constructor_CreatesDeckFromFactoryOnce()
    {
        var factory = new CountingDeckFactory();

        _ = new BlackJackTable(deckCount: 2, factory);

        Assert.Equal([2], factory.Calls);
    }

    [Fact]
    public void NewRound_AboveThreshold_DoesNotCallFactory()
    {
        // 1 deck: threshold is 13, so 14 cards left is above it.
        var factory = new CountingDeckFactory(firstDeckSize: 14);
        var table = new BlackJackTable(deckCount: 1, factory);

        table.NewRound(1);

        Assert.Single(factory.Calls);
        Assert.Equal(14, table.CardsRemaining);
    }

    [Fact]
    public void NewRound_AtThreshold_CallsFactoryAgain()
    {
        var factory = new CountingDeckFactory(firstDeckSize: 13);
        var table = new BlackJackTable(deckCount: 1, factory);

        table.NewRound(1);

        Assert.Equal([1, 1], factory.Calls);
        Assert.Equal(52, table.CardsRemaining);
    }
}
