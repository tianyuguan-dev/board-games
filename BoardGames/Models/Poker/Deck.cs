namespace BoardGames.Models.Poker;

public class Deck
{
    public int Count { get; init; } = 1;
    public bool IncludeJokers{ get; init; } = false;
    private readonly List<Card> _cards = new();
    public int Remaining => _cards.Count;

    public Deck(int count =1, bool includeJokers = false)
    {
        Count = count;
        IncludeJokers = includeJokers;
        for (var i = 0; i < count; i++)
        {
            foreach (var suit in Enum.GetValues<Suit>())
            {
                if (suit.Equals(Suit.None))
                    continue;
                foreach (var rank in Enum.GetValues<Rank>())
                {
                    if (rank.Equals(Rank.BlackJoker) || rank.Equals(Rank.RedJoker))
                        continue;
                    _cards.Add(new Card { Suit = suit, Rank = rank });
                }
            }

            if (!includeJokers) continue;
            _cards.Add(new Card { Suit = Suit.None, Rank = Rank.BlackJoker });
            _cards.Add(new Card { Suit = Suit.None, Rank = Rank.RedJoker });
        }
    }

    public void Shuffle()
    {
        var rng = Random.Shared;
        for (var i = _cards.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (_cards[i], _cards[j]) = (_cards[j], _cards[i]);
        }
    }

    public Card Deal()
    {
        if (_cards.Count == 0)
            throw new InvalidOperationException("No cards left in the deck");
        var card = _cards[^1];
        _cards.RemoveAt(_cards.Count - 1);
        return card;
    }
}