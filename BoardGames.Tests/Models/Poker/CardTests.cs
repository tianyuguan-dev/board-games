using BoardGames.Models.Poker;

namespace BoardGames.Tests.Models.Poker;

public class CardTests
{
    [Fact]
    public void Card_SetsProperties_Correctly()
    {
        var card = new Card { Suit = Suit.Heart, Rank = Rank.Ace };

        Assert.Equal(Suit.Heart, card.Suit);
        Assert.Equal(Rank.Ace, card.Rank);
    }

    [Fact]
    public void Card_DefaultSuit_IsNone()
    {
        var card = new Card { Rank = Rank.Ace };

        Assert.Equal(Suit.None, card.Suit);
    }

    [Fact]
    public void Rank_CastsToInt_Correctly()
    {
        Assert.Equal(1, (int)Rank.Ace);
        Assert.Equal(10, (int)Rank.Ten);
        Assert.Equal(13, (int)Rank.King);
    }
}
