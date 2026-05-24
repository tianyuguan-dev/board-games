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
