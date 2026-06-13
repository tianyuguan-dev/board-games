using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using BoardGames.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace BoardGames.Tests.Integration;

public class GuestAuthIntegrationTests : IClassFixture<CustomWebApplicationFactory>, IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _http;
    private readonly List<HubConnection> _connections = new();

    public GuestAuthIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _http = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var conn in _connections)
            await conn.DisposeAsync();
        _http.Dispose();
    }

    private HubConnection CreateHubConnection(string hubPath, string token)
    {
        var conn = new HubConnectionBuilder()
            .WithUrl($"{_http.BaseAddress!.ToString().TrimEnd('/')}{hubPath}?access_token={token}",
                opts => opts.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler())
            .Build();
        _connections.Add(conn);
        return conn;
    }

    [Fact]
    public async Task GuestLogin_ReturnsTokenAndGuestNickname()
    {
        var response = await _http.PostAsync("/api/auth/guest", null);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.NotNull(payload);
        var token = payload!["token"].ToString();
        var nickname = payload["nickname"].ToString();

        Assert.False(string.IsNullOrEmpty(token));
        Assert.StartsWith("Guest_", nickname);
        // refreshToken is null for guests
        Assert.True(payload["refreshToken"] is null
            || payload["refreshToken"].ToString() == ""
            || payload["refreshToken"].ToString() == "null");

        // Token must carry guest sub + isGuest claim
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Contains(jwt.Claims, c => c.Type == "isGuest" && c.Value == "true");
    }

    [Fact]
    public async Task GuestLogin_IssuesUniqueNicknamesAcrossCalls()
    {
        var r1 = await _http.PostAsync("/api/auth/guest", null);
        var r2 = await _http.PostAsync("/api/auth/guest", null);

        var p1 = await r1.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var p2 = await r2.Content.ReadFromJsonAsync<Dictionary<string, object>>();

        Assert.NotEqual(p1!["nickname"].ToString(), p2!["nickname"].ToString());
    }

    [Fact]
    public async Task GuestRefresh_IsRejected()
    {
        var guestResp = await _http.PostAsync("/api/auth/guest", null);
        var payload = await guestResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var token = payload!["token"].ToString();

        var refresh = await _http.PostAsJsonAsync("/api/auth/refresh",
            new { AccessToken = token, RefreshToken = "" });

        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task GuestCannotCreateAvalonRoom()
    {
        var resp = await _http.PostAsync("/api/auth/guest", null);
        var token = (await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>())!["token"].ToString()!;

        var conn = CreateHubConnection("/hub/avalon", token);
        await conn.StartAsync();

        var ex = await Assert.ThrowsAsync<HubException>(
            () => conn.InvokeAsync<object>("CreateRoom", 5));
        Assert.Contains("Guests can only play the solo demo", ex.Message);
    }

    [Fact]
    public async Task GuestCannotJoinAvalonRoom()
    {
        // Real user creates a room
        await _http.PostAsJsonAsync("/api/auth/register",
            new { Username = "guest_join_host", Password = "pass123" });
        var hostLogin = await _http.PostAsJsonAsync("/api/auth/login",
            new { Username = "guest_join_host", Password = "pass123" });
        var hostToken = (await hostLogin.Content.ReadFromJsonAsync<Dictionary<string, object>>())!["token"].ToString()!;

        var host = CreateHubConnection("/hub/avalon", hostToken);
        await host.StartAsync();
        var roomJson = await host.InvokeAsync<object>("CreateRoom", 5);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!)!["roomId"].ToString()!;

        // Guest tries to join
        var guestResp = await _http.PostAsync("/api/auth/guest", null);
        var guestToken = (await guestResp.Content.ReadFromJsonAsync<Dictionary<string, object>>())!["token"].ToString()!;
        var guest = CreateHubConnection("/hub/avalon", guestToken);
        await guest.StartAsync();

        var ex = await Assert.ThrowsAsync<HubException>(
            () => guest.InvokeAsync<object>("JoinRoom", roomId));
        Assert.Contains("Guests can only play the solo demo", ex.Message);
    }

    [Fact]
    public async Task GuestCanCreateAvalonDemoRoom()
    {
        var resp = await _http.PostAsync("/api/auth/guest", null);
        var token = (await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>())!["token"].ToString()!;

        var conn = CreateHubConnection("/hub/avalon", token);
        await conn.StartAsync();

        var room = await conn.InvokeAsync<object>("CreateDemoRoom");
        Assert.NotNull(room);
    }

    [Fact]
    public async Task GuestDoesNotCreateUserRow()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = db.Users.Count();

        await _http.PostAsync("/api/auth/guest", null);
        await _http.PostAsync("/api/auth/guest", null);
        await _http.PostAsync("/api/auth/guest", null);

        var after = db.Users.Count();
        Assert.Equal(before, after);
    }
}
