using BoardGames.Dtos.BlackJack;
using BoardGames.Models.BlackJack;
using BoardGames.Models.Poker;

namespace BoardGames.Tests.Dtos.BlackJack;

public class BlackJackDtoTests
{
    private static Deck CreateShuffledDeck()
    {
        var deck = new Deck();
        deck.Shuffle();
        return deck;
    }

    // BlackJackHandDto tests

    [Fact]
    public void HandDto_ContainsCorrectCards()
    {
        var hand = new BlackJackHand(
            new Card { Suit = Suit.Spade, Rank = Rank.Ace },
            new Card { Suit = Suit.Heart, Rank = Rank.King });

        var dto = new BlackJackHandDto(hand);

        Assert.Equal(2, dto.Cards.Count);
        Assert.Equal(Rank.Ace, dto.Cards[0].Rank);
        Assert.Equal(Rank.King, dto.Cards[1].Rank);
    }

    [Fact]
    public void HandDto_ContainsCorrectValue()
    {
        var hand = new BlackJackHand(
            new Card { Suit = Suit.Spade, Rank = Rank.Ace },
            new Card { Suit = Suit.Heart, Rank = Rank.King });

        var dto = new BlackJackHandDto(hand);

        Assert.Equal(21, dto.Value);
    }

    [Fact]
    public void HandDto_ConstructedWithExplicitValues()
    {
        IReadOnlyList<Card> cards = [new Card { Suit = Suit.Spade, Rank = Rank.Five }];
        var dto = new BlackJackHandDto(cards, 5);

        Assert.Single(dto.Cards);
        Assert.Equal(5, dto.Value);
    }

    // BlackJackGameStateDto tests

    [Fact]
    public void GameStateDto_ContainsAllPlayerHands()
    {
        var game = new BlackJackGame(CreateShuffledDeck(), playerCount: 3);
        game.Start();

        var dto = new BlackJackGameStateDto(game);

        Assert.Equal(3, dto.PlayerHands.Count);
    }

    [Fact]
    public void GameStateDto_DuringPlayerTurn_DealerShowsOneCard()
    {
        var game = new BlackJackGame(CreateShuffledDeck(), playerCount: 1);
        game.Start();

        Assert.Equal(BlackJackGameState.PlayerTurn, game.State);
        var dto = new BlackJackGameStateDto(game);

        Assert.Single(dto.DealerHand.Cards);
    }

    [Fact]
    public void GameStateDto_WhenFinished_DealerShowsAllCards()
    {
        var game = new BlackJackGame(CreateShuffledDeck(), playerCount: 1);
        game.Start();
        game.Stand();

        Assert.Equal(BlackJackGameState.Finished, game.State);
        var dto = new BlackJackGameStateDto(game);

        Assert.True(dto.DealerHand.Cards.Count >= 2);
    }

    [Fact]
    public void GameStateDto_ContainsCurrentPlayerIndex()
    {
        var game = new BlackJackGame(CreateShuffledDeck(), playerCount: 2);
        game.Start();

        var dto = new BlackJackGameStateDto(game);

        Assert.Equal(game.CurrentPlayerIndex, dto.CurrentIndex);
    }

    [Fact]
    public void GameStateDto_ContainsGameState()
    {
        var game = new BlackJackGame(CreateShuffledDeck(), playerCount: 1);
        game.Start();

        var dto = new BlackJackGameStateDto(game);

        Assert.Equal(BlackJackGameState.PlayerTurn, dto.State);
    }

    [Fact]
    public void GameStateDto_ContainsResults()
    {
        var game = new BlackJackGame(CreateShuffledDeck(), playerCount: 1);
        game.Start();
        game.Stand();

        var dto = new BlackJackGameStateDto(game);

        Assert.Single(dto.Results);
        Assert.True(dto.Results[0].HasValue);
    }
}
