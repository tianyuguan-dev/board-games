using BoardGames.Services.BlackJack;

namespace BoardGames.Tests.Services.BlackJack;

public class BlackJackGuestSessionTests
{
    [Fact]
    public void GetOrInit_ReturnsStartingBalance_OnFirstCall()
    {
        var session = new BlackJackGuestSession();
        Assert.Equal(BlackJackGuestSession.StartingBalance, session.GetOrInit("conn-1"));
    }

    [Fact]
    public void GetOrInit_ReturnsSameBalance_ForSameConnection()
    {
        var session = new BlackJackGuestSession();
        session.GetOrInit("conn-1");
        session.ApplyDelta("conn-1", -30);
        Assert.Equal(BlackJackGuestSession.StartingBalance - 30, session.GetOrInit("conn-1"));
    }

    [Fact]
    public void GetOrInit_IsolatesBalancesPerConnection()
    {
        var session = new BlackJackGuestSession();
        session.ApplyDelta("conn-A", -40);
        session.ApplyDelta("conn-B", +25);
        Assert.Equal(BlackJackGuestSession.StartingBalance - 40, session.GetOrInit("conn-A"));
        Assert.Equal(BlackJackGuestSession.StartingBalance + 25, session.GetOrInit("conn-B"));
    }

    [Fact]
    public void ApplyDelta_InitializesBalance_IfMissing()
    {
        var session = new BlackJackGuestSession();
        var result = session.ApplyDelta("brand-new", 10);
        Assert.Equal(BlackJackGuestSession.StartingBalance + 10, result);
    }

    [Fact]
    public void Reset_RestoresStartingBalance()
    {
        var session = new BlackJackGuestSession();
        session.ApplyDelta("conn-1", -90);
        session.Reset("conn-1");
        Assert.Equal(BlackJackGuestSession.StartingBalance, session.GetOrInit("conn-1"));
    }

    [Fact]
    public void Remove_ClearsBalance_NextGetReturnsStartingBalance()
    {
        var session = new BlackJackGuestSession();
        session.ApplyDelta("conn-1", -50);
        session.Remove("conn-1");
        // Fresh init after removal
        Assert.Equal(BlackJackGuestSession.StartingBalance, session.GetOrInit("conn-1"));
    }
}
