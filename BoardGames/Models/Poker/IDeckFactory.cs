namespace BoardGames.Models.Poker;

// Creates the shoe a BlackJack table deals from. Implementations must be stateless:
// one instance is shared by every room. Tests register a deterministic one.
public interface IDeckFactory
{
    Deck Create(int deckCount);
}
