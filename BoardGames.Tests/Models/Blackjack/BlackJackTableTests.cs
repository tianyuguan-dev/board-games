using BoardGames.Models.BlackJack;

namespace BoardGames.Tests.Models.Blackjack;

public class BlackJackTableTests
{
    [Fact]
    public void NewRound_ReturnsGameInPlayerTurnState()
    {
        var table = new BlackJackTable(deckCount: 6);

        var game = table.NewRound(1);

        Assert.Equal(BlackJackGameState.PlayerTurn, game.State);
    }

    [Fact]
    public void NewRound_ReturnsGameWithCorrectPlayerCount()
    {
        var table = new BlackJackTable(deckCount: 6);

        var game = table.NewRound(3);

        Assert.Equal(3, game.Results.Count);
    }

    [Fact]
    public void NewRound_SharesDeckAcrossRounds()
    {
        var table = new BlackJackTable(deckCount: 6);

        var game1 = table.NewRound(1);
        game1.Stand();

        var game2 = table.NewRound(1);
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
            // All players stand
            while (game.State == BlackJackGameState.PlayerTurn)
            {
                game.Stand();
            }
            Assert.Equal(BlackJackGameState.Finished, game.State);
        }
    }
}
