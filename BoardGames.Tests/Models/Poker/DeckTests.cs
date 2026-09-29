using BoardGames.Models.Poker;

namespace BoardGames.Tests.Models.Poker;

public class DeckTests
{
    [Fact]
    public void Deck_Default_Has52Cards()
    {
        var deck = new Deck();

        Assert.Equal(52, deck.Remaining);
    }

    [Fact]
    public void Deck_WithJokers_Has54Cards()
    {
        var deck = new Deck(includeJokers: true);

        Assert.Equal(54, deck.Remaining);
    }

    [Fact]
    public void Deck_MultipleDecks_HasCorrectCount()
    {
        var deck = new Deck(count: 2);

        Assert.Equal(104, deck.Remaining);
    }

    [Fact]
    public void Deck_MultipleDecksWithJokers_HasCorrectCount()
    {
        var deck = new Deck(count: 2, includeJokers: true);

        Assert.Equal(108, deck.Remaining);
    }

    [Fact]
    public void Deal_ReturnsCard_AndReducesRemaining()
    {
        var deck = new Deck();

        var card = deck.Deal();

        Assert.NotNull(card);
        Assert.Equal(51, deck.Remaining);
    }

    [Fact]
    public void Deal_AllCards_EmptiesDeck()
    {
        var deck = new Deck();

        for (var i = 0; i < 52; i++)
            deck.Deal();

        Assert.Equal(0, deck.Remaining);
    }

    [Fact]
    public void Deal_EmptyDeck_ThrowsException()
    {
        var deck = new Deck();
        for (var i = 0; i < 52; i++)
            deck.Deal();

        Assert.Throws<InvalidOperationException>(() => deck.Deal());
    }

    [Fact]
    public void Shuffle_DoesNotChangeCardCount()
    {
        var deck = new Deck();

        deck.Shuffle();

        Assert.Equal(52, deck.Remaining);
    }

    [Fact]
    public void Deck_DoesNotContainJokers_ByDefault()
    {
        var deck = new Deck();
        var cards = new List<Card>();
        for (var i = 0; i < 52; i++)
            cards.Add(deck.Deal());

        Assert.DoesNotContain(cards, c => c.Rank == Rank.BlackJoker);
        Assert.DoesNotContain(cards, c => c.Rank == Rank.RedJoker);
    }

    [Fact]
    public void Deck_FromCards_DealsLastCardFirst()
    {
        var deck = new Deck([
            new Card { Suit = Suit.Heart, Rank = Rank.Two },
            new Card { Suit = Suit.Spade, Rank = Rank.Ace },
        ]);

        var first = deck.Deal();
        var second = deck.Deal();

        Assert.Equal((Suit.Spade, Rank.Ace), (first.Suit, first.Rank));
        Assert.Equal((Suit.Heart, Rank.Two), (second.Suit, second.Rank));
    }

    [Fact]
    public void Deck_FromCards_RemainingMatchesInput()
    {
        var deck = new Deck([
            new Card { Suit = Suit.Club, Rank = Rank.King },
            new Card { Suit = Suit.Club, Rank = Rank.Queen },
            new Card { Suit = Suit.Club, Rank = Rank.Jack },
        ]);

        Assert.Equal(3, deck.Remaining);
    }

    [Fact]
    public void Deck_FromCards_Empty_DealThrows()
    {
        var deck = new Deck(Array.Empty<Card>());

        Assert.Equal(0, deck.Remaining);
        Assert.Throws<InvalidOperationException>(() => deck.Deal());
    }
}
