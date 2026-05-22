using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BoardGames.Dtos;

namespace BoardGames.Tests.Integration;

public class AuthIntegrationTests : IClassFixture<CustomWebApplicationFactory>, IDisposable
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private async Task<(string token, string refreshToken, string nickname)> RegisterAndLogin(
        string username = "testuser", string password = "testpass123")
    {
        await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequestDto { Username = username, Password = password });

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequestDto { Username = username, Password = password });
        var loginData = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        return (loginData!.Token, loginData.RefreshToken, loginData.Nickname);
    }

    [Fact]
    public async Task Register_ReturnsOk()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequestDto { Username = "newuser", Password = "pass123" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Register_DuplicateUsername_ReturnsBadRequest()
    {
        await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequestDto { Username = "dupuser", Password = "pass123" });

        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequestDto { Username = "dupuser", Password = "pass456" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsTokens()
    {
        await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequestDto { Username = "loginuser", Password = "pass123" });

        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequestDto { Username = "loginuser", Password = "pass123" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(string.IsNullOrEmpty(data!.Token));
        Assert.False(string.IsNullOrEmpty(data.RefreshToken));
    }

    [Fact]
    public async Task Login_InvalidCredentials_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequestDto { Username = "nobody", Password = "wrong" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_ValidTokens_ReturnsNewTokens()
    {
        var (token, refreshToken, _) = await RegisterAndLogin("refreshuser", "pass123");

        var response = await _client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshRequestDto { AccessToken = token, RefreshToken = refreshToken });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await response.Content.ReadFromJsonAsync<RefreshResponse>();
        Assert.False(string.IsNullOrEmpty(data!.Token));
        Assert.False(string.IsNullOrEmpty(data.RefreshToken));
    }

    [Fact]
    public async Task Refresh_InvalidRefreshToken_ReturnsUnauthorized()
    {
        var (token, _, _) = await RegisterAndLogin("refreshfail", "pass123");

        var response = await _client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshRequestDto { AccessToken = token, RefreshToken = "invalid-token" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_RevokedToken_ReturnsUnauthorized()
    {
        var (token, refreshToken, _) = await RegisterAndLogin("revokeuser", "pass123");

        await _client.PostAsJsonAsync("/api/auth/logout",
            new RefreshRequestDto { AccessToken = token, RefreshToken = refreshToken });

        var response = await _client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshRequestDto { AccessToken = token, RefreshToken = refreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        var (token, refreshToken, _) = await RegisterAndLogin("logoutuser", "pass123");

        var response = await _client.PostAsJsonAsync("/api/auth/logout",
            new RefreshRequestDto { AccessToken = token, RefreshToken = refreshToken });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpdateNickname_Authenticated_ReturnsOk()
    {
        var (token, _, _) = await RegisterAndLogin("nickuser", "pass123");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PutAsJsonAsync("/api/auth/nickname",
            new UpdateNicknameDto { Nickname = "CoolNick" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await response.Content.ReadFromJsonAsync<NicknameResponse>();
        Assert.Equal("CoolNick", data!.Nickname);

        _client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task UpdateNickname_Unauthenticated_ReturnsUnauthorized()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.PutAsJsonAsync("/api/auth/nickname",
            new UpdateNicknameDto { Nickname = "Hacker" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_ValidOldPassword_ReturnsOk()
    {
        var (token, _, _) = await RegisterAndLogin("pwduser", "oldpass");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PutAsJsonAsync("/api/auth/password",
            new ChangePasswordDto { OldPassword = "oldpass", NewPassword = "newpass" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        _client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task ChangePassword_WrongOldPassword_ReturnsBadRequest()
    {
        var (token, _, _) = await RegisterAndLogin("pwdfail", "realpass");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PutAsJsonAsync("/api/auth/password",
            new ChangePasswordDto { OldPassword = "wrongpass", NewPassword = "newpass" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        _client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task GetBalances_Authenticated_ReturnsBalances()
    {
        var (token, _, _) = await RegisterAndLogin("baluser", "pass123");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/auth/balances");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        _client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task GetBalances_Unauthenticated_ReturnsUnauthorized()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync("/api/auth/balances");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private record LoginResponse(string Token, string RefreshToken, string Nickname);
    private record RefreshResponse(string Token, string RefreshToken);
    private record NicknameResponse(string Nickname);
}
