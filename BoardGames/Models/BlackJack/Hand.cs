using BoardGames.Models.Poker;

namespace BoardGames.Models.BlackJack;

public class Hand
{
    private readonly List<Card> _cards = new();
    public IReadOnlyList<Card> Cards => _cards;
    public Hand()
    {
    }

    public Hand(Card card1, Card card2)
    {
        _cards.Add(card1);
        _cards.Add(card2);
    }

    public void AddCard(Card card)
    {
        _cards.Add(card);
    }

    public int GetValue()
    {
        int value = 0;
        int aceCount = 0;
        foreach (var card in _cards)
        {
            int cardRank = (int)card.Rank;
            if (cardRank > 10)
            {
                cardRank = 10;
            }

            if (card.Rank.Equals(Rank.Ace))
            {
                aceCount++;
                cardRank = 11;
            }

            value = value + cardRank;
        }

        while (value > 21 && aceCount > 0)
        {
            value = value - 10;
            aceCount--;
        }

        return value;
    }

    public bool IsBust()
    {
        return GetValue() > 21;
    }

    public bool IsBlackJack()
    {
        return GetValue() == 21&&_cards.Count == 2;
    }
}