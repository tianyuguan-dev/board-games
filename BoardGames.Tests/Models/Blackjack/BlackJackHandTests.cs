using BoardGames.Models.BlackJack;
using BoardGames.Models.Poker;

namespace BoardGames.Tests.Models.Blackjack;

public class BlackJackHandTests
{
    [Fact]
    public void GetValue_NumberCards_ReturnsSum()
    {
        var hand = new BlackJackHand();
        hand.AddCard(new Card { Suit = Suit.Heart, Rank = Rank.Five });
        hand.AddCard(new Card { Suit = Suit.Spade, Rank = Rank.Seven });

        Assert.Equal(12, hand.GetValue());
    }

    [Fact]
    public void GetValue_FaceCards_Count10()
    {
        var hand = new BlackJackHand();
        hand.AddCard(new Card { Suit = Suit.Heart, Rank = Rank.Jack });
        hand.AddCard(new Card { Suit = Suit.Spade, Rank = Rank.Queen });
        hand.AddCard(new Card { Suit = Suit.Club, Rank = Rank.King });

        Assert.Equal(30, hand.GetValue());
    }

    [Fact]
    public void GetValue_AceAs11_WhenNotBust()
    {
        var hand = new BlackJackHand();
        hand.AddCard(new Card { Suit = Suit.Heart, Rank = Rank.Ace });
        hand.AddCard(new Card { Suit = Suit.Spade, Rank = Rank.Six });

        Assert.Equal(17, hand.GetValue());
    }

    [Fact]
    public void GetValue_AceAs1_WhenWouldBust()
    {
        var hand = new BlackJackHand();
        hand.AddCard(new Card { Suit = Suit.Heart, Rank = Rank.Ace });
        hand.AddCard(new Card { Suit = Suit.Spade, Rank = Rank.Six });
        hand.AddCard(new Card { Suit = Suit.Club, Rank = Rank.Eight });

        Assert.Equal(15, hand.GetValue()); 
    }

    [Fact]
    public void GetValue_TwoAces_OneCountsAs1()
    {
        var hand = new BlackJackHand();
        hand.AddCard(new Card { Suit = Suit.Heart, Rank = Rank.Ace });
        hand.AddCard(new Card { Suit = Suit.Spade, Rank = Rank.Ace });
        hand.AddCard(new Card { Suit = Suit.Club, Rank = Rank.Nine });

        Assert.Equal(21, hand.GetValue());
    }

    [Fact]
    public void IsBust_ReturnsTrue_WhenOver21()
    {
        var hand = new BlackJackHand();
        hand.AddCard(new Card { Suit = Suit.Heart, Rank = Rank.Ten });
        hand.AddCard(new Card { Suit = Suit.Spade, Rank = Rank.Eight });
        hand.AddCard(new Card { Suit = Suit.Club, Rank = Rank.Five });

        Assert.True(hand.IsBust());
    }

    [Fact]
    public void IsBust_ReturnsFalse_WhenExactly21()
    {
        var hand = new BlackJackHand();
        hand.AddCard(new Card { Suit = Suit.Heart, Rank = Rank.Ten });
        hand.AddCard(new Card { Suit = Suit.Spade, Rank = Rank.Five });
        hand.AddCard(new Card { Suit = Suit.Club, Rank = Rank.Six });

        Assert.False(hand.IsBust());
    }

    [Fact]
    public void IsBlackJack_ReturnsTrue_WhenAceAndTen()
    {
        var hand = new BlackJackHand(
            new Card { Suit = Suit.Heart, Rank = Rank.Ace },
            new Card { Suit = Suit.Spade, Rank = Rank.Ten }
        );

        Assert.True(hand.IsBlackJack());
    }

    [Fact]
    public void IsBlackJack_ReturnsTrue_WhenAceAndKing()
    {
        var hand = new BlackJackHand(
            new Card { Suit = Suit.Heart, Rank = Rank.Ace },
            new Card { Suit = Suit.Spade, Rank = Rank.King }
        );

        Assert.True(hand.IsBlackJack());
    }

    [Fact]
    public void IsBlackJack_ReturnsFalse_WhenThreeCards21()
    {
        var hand = new BlackJackHand();
        hand.AddCard(new Card { Suit = Suit.Heart, Rank = Rank.Seven });
        hand.AddCard(new Card { Suit = Suit.Spade, Rank = Rank.Seven });
        hand.AddCard(new Card { Suit = Suit.Club, Rank = Rank.Seven });

        Assert.False(hand.IsBlackJack());
    }

    [Fact]
    public void IsBlackJack_ReturnsFalse_WhenTwoCardsNot21()
    {
        var hand = new BlackJackHand(
            new Card { Suit = Suit.Heart, Rank = Rank.Ten },
            new Card { Suit = Suit.Spade, Rank = Rank.Five }
        );

        Assert.False(hand.IsBlackJack());
    }

    [Fact]
    public void Constructor_WithTwoCards_SetsCorrectValue()
    {
        var hand = new BlackJackHand(
            new Card { Suit = Suit.Heart, Rank = Rank.Ten },
            new Card { Suit = Suit.Spade, Rank = Rank.Eight }
        );

        Assert.Equal(18, hand.GetValue());
    }
}
