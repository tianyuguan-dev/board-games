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

    // Registers, expects 400 with the given text, and checks no account was created (logging in with the same credentials fails).
    private async Task AssertRegisterRefused(string username, string password, string message)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequestDto { Username = username, Password = password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(message, await response.Content.ReadAsStringAsync());
        var login = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequestDto { Username = username, Password = password });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Register_EmptyUsername_ReturnsBadRequest()
    {
        await AssertRegisterRefused("", "pass123", "Username cannot be empty");
    }

    [Fact]
    public async Task Register_EmptyPassword_ReturnsBadRequest()
    {
        await AssertRegisterRefused("emptypassuser", "", "Password cannot be empty");
    }

    [Fact]
    public async Task Register_BothEmpty_ReturnsBadRequest()
    {
        await AssertRegisterRefused("", "", "Username cannot be empty");
    }

    [Theory]
    [InlineData("   ", "pass123", "Username cannot be empty")]
    [InlineData("spacepassuser", "   ", "Password cannot be empty")]
    public async Task Register_WhitespaceOnly_ReturnsBadRequest(string username, string password, string message)
    {
        await AssertRegisterRefused(username, password, message);
    }

    [Theory]
    [InlineData("CASEUSER")]
    [InlineData(" caseuser ")]
    [InlineData("CaseUser\t")]
    public async Task Register_SameUsernameOtherCaseOrSpaces_ReturnsBadRequest(string variant)
    {
        await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequestDto { Username = "caseuser", Password = "pass123" });

        await AssertRegisterRefused(variant, "pass456", "Username already exists");
    }

    [Fact]
    public async Task Register_MatchesAnotherUsersNickname_ReturnsBadRequest()
    {
        var (token, _, _) = await RegisterAndLogin("nickowner", "pass123");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await _client.PutAsJsonAsync("/api/auth/nickname", new UpdateNicknameDto { Nickname = "TakenNick" });
        _client.DefaultRequestHeaders.Authorization = null;

        // The new user's nickname would start as "takennick", which another player already uses.
        await AssertRegisterRefused("takennick", "pass123", "Username already exists");
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

    private async Task<HttpResponseMessage> PutNickname(string token, string nickname)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.PutAsJsonAsync("/api/auth/nickname", new UpdateNicknameDto { Nickname = nickname });
        _client.DefaultRequestHeaders.Authorization = null;
        return response;
    }

    // Logging in returns the stored nickname.
    private async Task<string> StoredNickname(string username, string password)
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequestDto { Username = username, Password = password });
        return (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Nickname;
    }

    [Theory]
    [InlineData("takenA1", "takenB1", "OtherNick1", "othernick1")] // other user's nickname, other case
    [InlineData("takenA2", "takenB2", "OtherNick2", " OTHERNICK2 ")] // ... with edge spaces
    [InlineData("takenA3", "takenB3", null, "TAKENB3")] // other user's username (their default nickname)
    [InlineData("takenA4", "takenB4", "SomethingElse4", "takenb4\t")] // other user's username after they renamed
    public async Task UpdateNickname_TakenByOtherUser_ReturnsBadRequest(
        string me, string other, string? otherNickname, string attempt)
    {
        var (otherToken, _, _) = await RegisterAndLogin(other, "pass123");
        if (otherNickname != null)
            Assert.Equal(HttpStatusCode.OK, (await PutNickname(otherToken, otherNickname)).StatusCode);
        var (myToken, _, _) = await RegisterAndLogin(me, "pass123");

        var response = await PutNickname(myToken, attempt);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Nickname already taken", await response.Content.ReadAsStringAsync());
        Assert.Equal(me, await StoredNickname(me, "pass123"));
    }

    [Theory]
    [InlineData("ownCase1", "OWNCASE1", "OWNCASE1")] // own username in another case
    [InlineData("ownCase2", " owncase2 ", "owncase2")] // edge spaces are trimmed before saving
    public async Task UpdateNickname_OwnNameOtherCase_Succeeds(string me, string attempt, string saved)
    {
        var (token, _, _) = await RegisterAndLogin(me, "pass123");

        var response = await PutNickname(token, attempt);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(saved, (await response.Content.ReadFromJsonAsync<NicknameResponse>())!.Nickname);
        Assert.Equal(saved, await StoredNickname(me, "pass123"));
    }

    [Theory]
    [InlineData("nickEmpty", "", "Nickname cannot be empty")]
    [InlineData("nickBlank", "   ", "Nickname cannot be empty")]
    [InlineData("nickLong", "123456789012345678901234567890123456789012345678901", "Nickname is too long (max 50)")]
    public async Task UpdateNickname_EmptyOrTooLong_ReturnsBadRequest(string me, string attempt, string message)
    {
        var (token, _, _) = await RegisterAndLogin(me, "pass123");

        var response = await PutNickname(token, attempt);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(message, await response.Content.ReadAsStringAsync());
        Assert.Equal(me, await StoredNickname(me, "pass123"));
    }

    [Fact]
    public async Task UpdateNickname_FiftyCharsWithEdgeSpaces_SavesTrimmed()
    {
        var (token, _, _) = await RegisterAndLogin("nickFifty", "pass123");
        var fifty = new string('x', 50);

        var response = await PutNickname(token, $"  {fifty}  ");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(fifty, await StoredNickname("nickFifty", "pass123"));
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

    [Theory]
    [InlineData("pwdempty", "")]
    [InlineData("pwdblank", "   ")]
    public async Task ChangePassword_EmptyNewPassword_ReturnsBadRequest(string username, string newPassword)
    {
        var (token, _, _) = await RegisterAndLogin(username, "oldpass");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PutAsJsonAsync("/api/auth/password",
            new ChangePasswordDto { OldPassword = "oldpass", NewPassword = newPassword });

        _client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Password cannot be empty", await response.Content.ReadAsStringAsync());
        // The password was not changed: the old one still logs in.
        var login = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequestDto { Username = username, Password = "oldpass" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
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
