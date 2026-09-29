using BoardGames.Models.Poker;

namespace BoardGames.Tests.Models.Poker;

public class DeckFactoryTests
{
    private static List<(Suit, Rank)> DealAll(Deck deck)
    {
        var cards = new List<(Suit, Rank)>();
        while (deck.Remaining > 0)
        {
            var card = deck.Deal();
            cards.Add((card.Suit, card.Rank));
        }
        return cards;
    }

    [Fact]
    public void Create_ReturnsShuffledShoeOfRequestedSize()
    {
        var deck = new ShuffledDeckFactory().Create(2);

        Assert.Equal(104, deck.Remaining);
        // Same cards as an unshuffled shoe, in a different order (identical by chance: 1 in 104!).
        var shuffled = DealAll(deck);
        var unshuffled = DealAll(new Deck(2));
        Assert.Equal(unshuffled.OrderBy(c => c), shuffled.OrderBy(c => c));
        Assert.NotEqual(unshuffled, shuffled);
    }
}
