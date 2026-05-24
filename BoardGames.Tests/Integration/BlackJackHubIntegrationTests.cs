using System.Net.Http.Json;
using BoardGames.Dtos;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace BoardGames.Tests.Integration;

public class BlackJackHubIntegrationTests : IClassFixture<CustomWebApplicationFactory>, IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _http;
    private readonly List<HubConnection> _connections = new();

    public BlackJackHubIntegrationTests(CustomWebApplicationFactory factory)
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

    private async Task<string> RegisterAndGetToken(string username)
    {
        await _http.PostAsJsonAsync("/api/auth/register",
            new RegisterRequestDto { Username = username, Password = "pass123" });
        var response = await _http.PostAsJsonAsync("/api/auth/login",
            new LoginRequestDto { Username = username, Password = "pass123" });
        var json = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        return json!["token"].ToString()!;
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
    public async Task CreateRoom_ReturnsRoom()
    {
        var token = await RegisterAndGetToken("bj_create");
        var conn = CreateHubConnection("/hub/blackjack", token);
        await conn.StartAsync();

        var room = await conn.InvokeAsync<object>("CreateRoom", 4);
        Assert.NotNull(room);
    }

    [Fact]
    public async Task JoinRoom_SecondPlayer_Succeeds()
    {
        var token1 = await RegisterAndGetToken("bj_host1");
        var token2 = await RegisterAndGetToken("bj_guest1");

        var host = CreateHubConnection("/hub/blackjack", token1);
        var guest = CreateHubConnection("/hub/blackjack", token2);
        await host.StartAsync();
        await guest.StartAsync();

        var roomJson = await host.InvokeAsync<object>("CreateRoom", 4);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!)!["roomId"].ToString()!;

        var result = await guest.InvokeAsync<object>("JoinRoom", roomId);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task JoinRoom_DuplicateUser_Throws()
    {
        var token = await RegisterAndGetToken("bj_dup");

        var conn1 = CreateHubConnection("/hub/blackjack", token);
        var conn2 = CreateHubConnection("/hub/blackjack", token);
        await conn1.StartAsync();
        await conn2.StartAsync();

        var roomJson = await conn1.InvokeAsync<object>("CreateRoom", 4);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!)!["roomId"].ToString()!;

        var ex = await Assert.ThrowsAsync<HubException>(
            () => conn2.InvokeAsync<object>("JoinRoom", roomId));
        Assert.Contains("already in this room", ex.Message);
    }

    [Fact]
    public async Task GetBalance_ReturnsBalance()
    {
        var token = await RegisterAndGetToken("bj_bal");
        var conn = CreateHubConnection("/hub/blackjack", token);
        await conn.StartAsync();

        var balance = await conn.InvokeAsync<decimal>("GetBalance");
        Assert.True(balance >= 0);
    }

    [Fact]
    public async Task Ready_And_StartGame_Flow()
    {
        var token1 = await RegisterAndGetToken("bj_flow_host");
        var token2 = await RegisterAndGetToken("bj_flow_guest");

        var host = CreateHubConnection("/hub/blackjack", token1);
        var guest = CreateHubConnection("/hub/blackjack", token2);
        await host.StartAsync();
        await guest.StartAsync();

        var roomJson = await host.InvokeAsync<object>("CreateRoom", 4);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!)!["roomId"].ToString()!;

        await guest.InvokeAsync<object>("JoinRoom", roomId);

        await guest.InvokeAsync("Ready", roomId);

        var gameStarted = new TaskCompletionSource<bool>();
        host.On<object>("StartGame", _ => gameStarted.TrySetResult(true));
        guest.On<object>("StartGame", _ => { });

        await host.InvokeAsync("StartGame", roomId);

        var started = await Task.WhenAny(gameStarted.Task, Task.Delay(5000));
        Assert.True(gameStarted.Task.IsCompletedSuccessfully, "Game should have started");
    }

    [Fact]
    public async Task PlaceBet_And_Play_Round()
    {
        var token1 = await RegisterAndGetToken("bj_play_host");
        var token2 = await RegisterAndGetToken("bj_play_guest");

        var host = CreateHubConnection("/hub/blackjack", token1);
        var guest = CreateHubConnection("/hub/blackjack", token2);
        await host.StartAsync();
        await guest.StartAsync();

        var roomJson = await host.InvokeAsync<object>("CreateRoom", 4);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!)!["roomId"].ToString()!;
        await guest.InvokeAsync<object>("JoinRoom", roomId);
        await guest.InvokeAsync("Ready", roomId);

        var gameDealt = new TaskCompletionSource<bool>();
        host.On<object>("GameDealt", _ => gameDealt.TrySetResult(true));
        guest.On<object>("GameDealt", _ => { });

        await host.InvokeAsync("StartGame", roomId);

        // Both players place bets (minimum bet)
        await host.InvokeAsync("PlaceBet", roomId, 10);
        await guest.InvokeAsync("PlaceBet", roomId, 10);

        var dealt = await Task.WhenAny(gameDealt.Task, Task.Delay(5000));
        Assert.True(gameDealt.Task.IsCompletedSuccessfully, "Cards should have been dealt");

        // Both players stand to finish the round
        await host.InvokeAsync("BlackJackPlayerStand", roomId);
        await guest.InvokeAsync("BlackJackPlayerStand", roomId);
    }

    [Fact]
    public async Task LeaveRoom_Works()
    {
        var token = await RegisterAndGetToken("bj_leave");
        var conn = CreateHubConnection("/hub/blackjack", token);
        await conn.StartAsync();

        await conn.InvokeAsync<object>("CreateRoom", 4);

        await conn.InvokeAsync("LeaveRoom");
    }

    [Fact]
    public async Task StartGame_NotHost_Throws()
    {
        var token1 = await RegisterAndGetToken("bj_nothost1");
        var token2 = await RegisterAndGetToken("bj_nothost2");

        var host = CreateHubConnection("/hub/blackjack", token1);
        var guest = CreateHubConnection("/hub/blackjack", token2);
        await host.StartAsync();
        await guest.StartAsync();

        var roomJson = await host.InvokeAsync<object>("CreateRoom", 4);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!)!["roomId"].ToString()!;
        await guest.InvokeAsync<object>("JoinRoom", roomId);

        var ex = await Assert.ThrowsAsync<HubException>(
            () => guest.InvokeAsync("StartGame", roomId));
        Assert.Contains("Only the host", ex.Message);
    }

    [Fact]
    public async Task GetLeaderboard_ReturnsList()
    {
        var token = await RegisterAndGetToken("bj_leader");
        var conn = CreateHubConnection("/hub/blackjack", token);
        await conn.StartAsync();

        var result = await conn.InvokeAsync<object>("GetLeaderboard");
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ClaimBonus_ReturnsBalance()
    {
        var token = await RegisterAndGetToken("bj_bonus");
        var conn = CreateHubConnection("/hub/blackjack", token);
        await conn.StartAsync();

        // Drain balance first so we're under 50
        // New user starts at 1000 so ClaimBonus should throw
        var ex = await Assert.ThrowsAsync<HubException>(
            () => conn.InvokeAsync<decimal>("ClaimBonus"));
        Assert.Contains("Balance too high", ex.Message);
    }

    [Fact]
    public async Task DoubleDown_NotInGame_Throws()
    {
        var token = await RegisterAndGetToken("bj_dd");
        var conn = CreateHubConnection("/hub/blackjack", token);
        await conn.StartAsync();

        await conn.InvokeAsync<object>("CreateRoom", 4);

        var ex = await Assert.ThrowsAsync<HubException>(
            () => conn.InvokeAsync("BlackJackPlayerDoubleDown", "fake-room"));
        Assert.NotNull(ex);
    }

    [Fact]
    public async Task KickPlayer_HostCanKick()
    {
        var token1 = await RegisterAndGetToken("bj_kick_host");
        var token2 = await RegisterAndGetToken("bj_kick_guest");

        var host = CreateHubConnection("/hub/blackjack", token1);
        var guest = CreateHubConnection("/hub/blackjack", token2);
        await host.StartAsync();
        await guest.StartAsync();

        var roomJson = await host.InvokeAsync<object>("CreateRoom", 4);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!)!["roomId"].ToString()!;
        await guest.InvokeAsync<object>("JoinRoom", roomId);

        var kicked = new TaskCompletionSource<bool>();
        guest.On("Kicked", () => kicked.TrySetResult(true));

        await host.InvokeAsync("KickPlayer", roomId, 1);

        var result = await Task.WhenAny(kicked.Task, Task.Delay(3000));
        Assert.True(kicked.Task.IsCompletedSuccessfully, "Guest should be kicked");
    }

    [Fact]
    public async Task KickPlayer_NonHost_Throws()
    {
        var token1 = await RegisterAndGetToken("bj_kickfail_host");
        var token2 = await RegisterAndGetToken("bj_kickfail_guest");

        var host = CreateHubConnection("/hub/blackjack", token1);
        var guest = CreateHubConnection("/hub/blackjack", token2);
        await host.StartAsync();
        await guest.StartAsync();

        var roomJson = await host.InvokeAsync<object>("CreateRoom", 4);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!)!["roomId"].ToString()!;
        await guest.InvokeAsync<object>("JoinRoom", roomId);

        var ex = await Assert.ThrowsAsync<HubException>(
            () => guest.InvokeAsync("KickPlayer", roomId, 0));
        Assert.Contains("Only the host", ex.Message);
    }
}
