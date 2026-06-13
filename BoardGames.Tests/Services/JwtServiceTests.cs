using System.IdentityModel.Tokens.Jwt;
using BoardGames.Models;
using BoardGames.Services;
using Microsoft.Extensions.Configuration;

namespace BoardGames.Tests.Services;

public class JwtServiceTests
{
    private readonly JwtService _jwtService;

    public JwtServiceTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Jwt:Key", "ThisIsA32CharacterLongSecretKey!" },
                { "Jwt:Issuer", "BoardGames" }
            })
            .Build();
        _jwtService = new JwtService(config);
    }

    [Fact]
    public void GenerateJwtToken_ReturnsValidToken()
    {
        var user = new User { Id = 1, Username = "terry" };

        var token = _jwtService.GenerateJwtToken(user);

        Assert.False(string.IsNullOrEmpty(token));
    }

    [Fact]
    public void GenerateJwtToken_ContainsCorrectClaims()
    {
        var user = new User { Id = 1, Username = "terry" };

        var token = _jwtService.GenerateJwtToken(user);
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        Assert.Equal("BoardGames", jwt.Issuer);
        Assert.Contains(jwt.Claims, c => c.Value == "1");
        Assert.Contains(jwt.Claims, c => c.Value == "terry");
    }

    [Fact]
    public void GenerateGuestJwtToken_ContainsGuestSubjectAndClaim()
    {
        var token = _jwtService.GenerateGuestJwtToken("abc123def456", "Guest_abc123");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal("BoardGames", jwt.Issuer);
        // NameIdentifier prefixed with "guest:" so hub guards can detect guests
        Assert.Contains(jwt.Claims, c =>
            c.Type == System.Security.Claims.ClaimTypes.NameIdentifier && c.Value == "guest:abc123def456");
        // Explicit isGuest=true claim
        Assert.Contains(jwt.Claims, c => c.Type == "isGuest" && c.Value == "true");
        // Nickname preserved
        Assert.Contains(jwt.Claims, c => c.Type == "nickname" && c.Value == "Guest_abc123");
    }

    [Fact]
    public void GenerateGuestJwtToken_ExpiresInAboutFourHours()
    {
        var before = DateTime.UtcNow;
        var token = _jwtService.GenerateGuestJwtToken("xyz", "Guest_xyz");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        // Allow 30s tolerance for clock drift in test runner
        var expected = before.AddHours(4);
        var diff = (jwt.ValidTo - expected).Duration();
        Assert.True(diff < TimeSpan.FromSeconds(30), $"Expected ~4h expiry, got {jwt.ValidTo - before}");
    }
}
