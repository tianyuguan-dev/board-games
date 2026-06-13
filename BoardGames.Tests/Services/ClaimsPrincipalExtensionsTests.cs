using System.Security.Claims;
using BoardGames.Services;

namespace BoardGames.Tests.Services;

public class ClaimsPrincipalExtensionsTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));

    [Fact]
    public void IsGuest_ReturnsTrue_WhenIsGuestClaimPresent()
    {
        var user = Principal(new Claim("isGuest", "true"));
        Assert.True(user.IsGuest());
    }

    [Fact]
    public void IsGuest_ReturnsFalse_WhenClaimMissing()
    {
        var user = Principal(new Claim(ClaimTypes.NameIdentifier, "42"));
        Assert.False(user.IsGuest());
    }

    [Fact]
    public void IsGuest_ReturnsFalse_OnNullPrincipal()
    {
        ClaimsPrincipal? user = null;
        Assert.False(user.IsGuest());
    }

    [Fact]
    public void GetUserIdOrZero_ReturnsParsedInt_ForRealUser()
    {
        var user = Principal(new Claim(ClaimTypes.NameIdentifier, "42"));
        Assert.Equal(42, user.GetUserIdOrZero());
    }

    [Fact]
    public void GetUserIdOrZero_ReturnsZero_ForGuestSubject()
    {
        var user = Principal(new Claim(ClaimTypes.NameIdentifier, "guest:abc123"));
        Assert.Equal(0, user.GetUserIdOrZero());
    }

    [Fact]
    public void GetUserIdOrZero_ReturnsZero_WhenSubjectMissing()
    {
        var user = Principal();
        Assert.Equal(0, user.GetUserIdOrZero());
    }
}
