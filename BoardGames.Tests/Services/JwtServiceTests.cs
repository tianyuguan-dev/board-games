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
}
