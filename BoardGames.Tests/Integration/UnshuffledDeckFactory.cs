using BoardGames.Models.Poker;

namespace BoardGames.Tests.Integration;

// Deals from an unshuffled shoe: K♦ Q♦, J♦ 10♦, 9♦ 8♦, ... No hand is a natural for the first 6 hands,
// so integration tests never have a turn skipped by luck. Stateless, like the production factory.
public class UnshuffledDeckFactory : IDeckFactory
{
    public Deck Create(int deckCount) => new(deckCount);
}
