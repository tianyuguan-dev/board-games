using BoardGames.Models.BlackJack;
using BoardGames.Models.Poker;

namespace BoardGames.Dtos.BlackJack;

public class BlackJackHandDto
{
    public IReadOnlyList<Card> Cards { get; init; }
    public int Value { get; init; }

    public BlackJackHandDto(BlackJackHand hand)
    {
        Cards = hand.Cards;
        Value = hand.GetValue();
    }

    public BlackJackHandDto(IReadOnlyList<Card> cards, int value)
    {
        Cards = cards;
        Value = value;
    }
}