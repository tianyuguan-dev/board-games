namespace BoardGames.Models.Poker;

public class Card
{
    public Suit Suit { get; init; }
    public Rank Rank { get; init; }
}
public enum Suit
{
    None=0,Heart, Spade, Club, Diamond
}

public enum Rank
{
    Ace = 1,
    Two = 2,
    Three = 3,
    Four = 4,
    Five = 5,
    Six = 6,
    Seven = 7,
    Eight = 8,
    Nine = 9,
    Ten = 10,
    Jack = 11,
    Queen = 12,
    King = 13,
    BlackJoker = 14,
    RedJoker = 15
}