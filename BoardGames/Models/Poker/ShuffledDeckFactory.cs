namespace BoardGames.Models.Poker;

public class ShuffledDeckFactory : IDeckFactory
{
    public Deck Create(int deckCount)
    {
        var deck = new Deck(deckCount);
        deck.Shuffle();
        return deck;
    }
}
