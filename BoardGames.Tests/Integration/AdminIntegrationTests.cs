using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BoardGames.Dtos;

namespace BoardGames.Tests.Integration;

public class AdminIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public AdminIntegrationTests(CustomWebApplicationFactory factory)
    {
        _http = factory.CreateClient();
    }

    private async Task RegisterUser(string username)
    {
        await _http.PostAsJsonAsync("/api/auth/register",
            new RegisterRequestDto { Username = username, Password = "pass123" });
    }

    private HttpRequestMessage WithAdminToken(HttpRequestMessage request, string token = "admin123")
    {
        request.Headers.Add("X-Admin-Token", token);
        return request;
    }

    [Fact]
    public async Task GetUsers_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _http.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUsers_WithWrongToken_ReturnsUnauthorized()
    {
        var request = WithAdminToken(new HttpRequestMessage(HttpMethod.Get, "/api/admin/users"), "wrong");
        var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUsers_ReturnsUserList()
    {
        await RegisterUser("admin_list_user");
        var request = WithAdminToken(new HttpRequestMessage(HttpMethod.Get, "/api/admin/users"));
        var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var users = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(users.GetArrayLength() > 0);
    }

    [Fact]
    public async Task GetUsers_SearchFilter_Works()
    {
        await RegisterUser("admin_search_target");
        var request = WithAdminToken(
            new HttpRequestMessage(HttpMethod.Get, "/api/admin/users?search=admin_search_target"));
        var response = await _http.SendAsync(request);
        var users = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(users.GetArrayLength() >= 1);
        Assert.Contains("admin_search_target", users[0].GetProperty("username").GetString());
    }

    [Theory]
    [InlineData("admcasefind")]
    [InlineData("  ADMCASEFIND ")]
    [InlineData("AdmCaseFind")]
    public async Task GetUsers_Search_IsCaseInsensitive(string search)
    {
        // Usernames that differ only in case can no longer both register, so the matches differ by a suffix.
        await RegisterUser("AdmCaseFind1");
        await RegisterUser("ADMCASEFIND2");
        await RegisterUser("adm_cs_by_nick");
        await RegisterUser("adm_cs_no_match");
        var request = WithAdminToken(new HttpRequestMessage(HttpMethod.Put,
            $"/api/admin/users/{await UserIdOf("adm_cs_by_nick")}/nickname")
        {
            Content = JsonContent.Create(new { nickname = "admCASEfind3" })
        });
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(request)).StatusCode);

        var response = await _http.SendAsync(WithAdminToken(new HttpRequestMessage(HttpMethod.Get,
            $"/api/admin/users?search={Uri.EscapeDataString(search)}")));

        var users = await response.Content.ReadFromJsonAsync<JsonElement>();
        var usernames = users.EnumerateArray().Select(u => u.GetProperty("username").GetString()).ToHashSet();
        Assert.Equal(new HashSet<string?> { "AdmCaseFind1", "ADMCASEFIND2", "adm_cs_by_nick" }, usernames);
    }

    private async Task<int> UserIdOf(string username)
    {
        var request = WithAdminToken(new HttpRequestMessage(HttpMethod.Get, $"/api/admin/users?search={username}"));
        var users = await (await _http.SendAsync(request)).Content.ReadFromJsonAsync<JsonElement>();
        return users.EnumerateArray().First(u => u.GetProperty("username").GetString() == username)
            .GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task GetUserDetail_ReturnsUserWithBalances()
    {
        await RegisterUser("admin_detail_user");

        var listRequest = WithAdminToken(
            new HttpRequestMessage(HttpMethod.Get, "/api/admin/users?search=admin_detail_user"));
        var listResponse = await _http.SendAsync(listRequest);
        var users = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var userId = users[0].GetProperty("id").GetInt32();

        var request = WithAdminToken(new HttpRequestMessage(HttpMethod.Get, $"/api/admin/users/{userId}"));
        var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var detail = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("admin_detail_user", detail.GetProperty("username").GetString());
        Assert.True(detail.TryGetProperty("balances", out _));
    }

    [Fact]
    public async Task GetUserDetail_NotFound_Returns404()
    {
        var request = WithAdminToken(new HttpRequestMessage(HttpMethod.Get, "/api/admin/users/99999"));
        var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_Works()
    {
        await RegisterUser("admin_reset_pw");

        var listRequest = WithAdminToken(
            new HttpRequestMessage(HttpMethod.Get, "/api/admin/users?search=admin_reset_pw"));
        var listResponse = await _http.SendAsync(listRequest);
        var users = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var userId = users[0].GetProperty("id").GetInt32();

        var request = WithAdminToken(new HttpRequestMessage(HttpMethod.Post, "/api/admin/reset-password")
        {
            Content = JsonContent.Create(new { userId, newPassword = "newpass456" })
        });
        var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify new password works
        var loginResponse = await _http.PostAsJsonAsync("/api/auth/login",
            new LoginRequestDto { Username = "admin_reset_pw", Password = "newpass456" });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [Fact]
    public async Task UpdateNickname_Works()
    {
        await RegisterUser("admin_nick_user");

        var listRequest = WithAdminToken(
            new HttpRequestMessage(HttpMethod.Get, "/api/admin/users?search=admin_nick_user"));
        var listResponse = await _http.SendAsync(listRequest);
        var users = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var userId = users[0].GetProperty("id").GetInt32();

        var request = WithAdminToken(new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}/nickname")
        {
            Content = JsonContent.Create(new { nickname = "NewNick" })
        });
        var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify nickname changed
        var detailRequest = WithAdminToken(new HttpRequestMessage(HttpMethod.Get, $"/api/admin/users/{userId}"));
        var detailResponse = await _http.SendAsync(detailRequest);
        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NewNick", detail.GetProperty("nickname").GetString());
    }

    private async Task<int> RegisterAndGetId(string username)
    {
        await RegisterUser(username);
        return await UserIdOf(username);
    }

    private Task<HttpResponseMessage> PutNickname(int userId, string nickname) =>
        _http.SendAsync(WithAdminToken(new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}/nickname")
        {
            Content = JsonContent.Create(new { nickname })
        }));

    private async Task<string?> StoredNickname(int userId)
    {
        var request = WithAdminToken(new HttpRequestMessage(HttpMethod.Get, $"/api/admin/users/{userId}"));
        var detail = await (await _http.SendAsync(request)).Content.ReadFromJsonAsync<JsonElement>();
        return detail.GetProperty("nickname").GetString();
    }

    [Theory]
    [InlineData("adm_dupA1", "adm_dupB1", "AdminOther1", " adminother1 ")] // other user's nickname, other case, edge spaces
    [InlineData("adm_dupA2", "adm_dupB2", null, "ADM_DUPB2")] // other user's username
    public async Task UpdateNickname_Duplicate_ReturnsBadRequest(
        string target, string other, string? otherNickname, string attempt)
    {
        var otherId = await RegisterAndGetId(other);
        if (otherNickname != null)
            Assert.Equal(HttpStatusCode.OK, (await PutNickname(otherId, otherNickname)).StatusCode);
        var targetId = await RegisterAndGetId(target);

        var response = await PutNickname(targetId, attempt);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Nickname already taken", await response.Content.ReadAsStringAsync());
        Assert.Equal(target, await StoredNickname(targetId));
    }

    [Fact]
    public async Task UpdateNickname_TargetsOwnNameOtherCase_SavesTrimmed()
    {
        var id = await RegisterAndGetId("adm_owncase");

        var response = await PutNickname(id, "  ADM_OWNCASE ");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ADM_OWNCASE", await StoredNickname(id));
    }

    [Theory]
    [InlineData("adm_nickEmpty", "", "Nickname cannot be empty")]
    [InlineData("adm_nickBlank", "  \t ", "Nickname cannot be empty")]
    [InlineData("adm_nickLong", "123456789012345678901234567890123456789012345678901", "Nickname is too long (max 50)")]
    public async Task UpdateNickname_EmptyOrTooLong_ReturnsBadRequest(string target, string attempt, string message)
    {
        var id = await RegisterAndGetId(target);

        var response = await PutNickname(id, attempt);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(message, await response.Content.ReadAsStringAsync());
        Assert.Equal(target, await StoredNickname(id));
    }

    [Fact]
    public async Task UpdateBalance_Works()
    {
        await RegisterUser("admin_bal_user");

        var listRequest = WithAdminToken(
            new HttpRequestMessage(HttpMethod.Get, "/api/admin/users?search=admin_bal_user"));
        var listResponse = await _http.SendAsync(listRequest);
        var users = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var userId = users[0].GetProperty("id").GetInt32();

        var request = WithAdminToken(new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}/balance")
        {
            Content = JsonContent.Create(new { gameType = "Avalon", balance = 5.5 })
        });
        var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify balance changed
        var detailRequest = WithAdminToken(new HttpRequestMessage(HttpMethod.Get, $"/api/admin/users/{userId}"));
        var detailResponse = await _http.SendAsync(detailRequest);
        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        var balances = detail.GetProperty("balances");
        var avalonBalance = balances.EnumerateArray()
            .First(b => b.GetProperty("gameType").GetString() == "Avalon");
        Assert.Equal(5.5m, avalonBalance.GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task UpdateBalance_InvalidGameType_ReturnsBadRequest()
    {
        await RegisterUser("admin_bal_invalid");

        var listRequest = WithAdminToken(
            new HttpRequestMessage(HttpMethod.Get, "/api/admin/users?search=admin_bal_invalid"));
        var listResponse = await _http.SendAsync(listRequest);
        var users = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var userId = users[0].GetProperty("id").GetInt32();

        var request = WithAdminToken(new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}/balance")
        {
            Content = JsonContent.Create(new { gameType = "FakeGame", balance = 100 })
        });
        var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
