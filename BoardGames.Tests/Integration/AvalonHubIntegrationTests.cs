using System.Net.Http.Json;
using BoardGames.Dtos;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace BoardGames.Tests.Integration;

public class AvalonHubIntegrationTests : IClassFixture<CustomWebApplicationFactory>, IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _http;
    private readonly List<HubConnection> _connections = new();

    public AvalonHubIntegrationTests(CustomWebApplicationFactory factory)
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
    public async Task CreateRoom_ReturnsRoomInfo()
    {
        var token = await RegisterAndGetToken("av_create");
        var conn = CreateHubConnection("/hub/avalon", token);
        await conn.StartAsync();

        var result = await conn.InvokeAsync<object>("CreateRoom", 5);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task JoinRoom_Succeeds()
    {
        var token1 = await RegisterAndGetToken("av_host1");
        var token2 = await RegisterAndGetToken("av_guest1");

        var host = CreateHubConnection("/hub/avalon", token1);
        var guest = CreateHubConnection("/hub/avalon", token2);
        await host.StartAsync();
        await guest.StartAsync();

        var roomJson = await host.InvokeAsync<object>("CreateRoom", 5);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!)!["roomId"].ToString()!;

        var result = await guest.InvokeAsync<object>("JoinRoom", roomId);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task JoinRoom_DuplicateUser_Throws()
    {
        var token = await RegisterAndGetToken("av_dup");

        var conn1 = CreateHubConnection("/hub/avalon", token);
        var conn2 = CreateHubConnection("/hub/avalon", token);
        await conn1.StartAsync();
        await conn2.StartAsync();

        var roomJson = await conn1.InvokeAsync<object>("CreateRoom", 5);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!)!["roomId"].ToString()!;

        var ex = await Assert.ThrowsAsync<HubException>(
            () => conn2.InvokeAsync<object>("JoinRoom", roomId));
        Assert.Contains("already in this room", ex.Message);
    }

    [Fact]
    public async Task GetBalance_ReturnsBalance()
    {
        var token = await RegisterAndGetToken("av_bal");
        var conn = CreateHubConnection("/hub/avalon", token);
        await conn.StartAsync();

        var balance = await conn.InvokeAsync<int>("GetBalance");
        Assert.True(balance >= 0);
    }

    [Fact]
    public async Task StartGame_NotEnoughPlayers_Throws()
    {
        var token = await RegisterAndGetToken("av_toofew");
        var conn = CreateHubConnection("/hub/avalon", token);
        await conn.StartAsync();

        var roomJson = await conn.InvokeAsync<object>("CreateRoom", 5);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!)!["roomId"].ToString()!;

        var ex = await Assert.ThrowsAsync<HubException>(
            () => conn.InvokeAsync("StartGame", roomId));
        Assert.Contains("5-10 players", ex.Message);
    }

    [Fact]
    public async Task LeaveRoom_Works()
    {
        var token = await RegisterAndGetToken("av_leave");
        var conn = CreateHubConnection("/hub/avalon", token);
        await conn.StartAsync();

        await conn.InvokeAsync<object>("CreateRoom", 5);
        await conn.InvokeAsync("LeaveRoom");
    }

    [Fact]
    public async Task Ready_And_Unready_Work()
    {
        var token1 = await RegisterAndGetToken("av_ready_host");
        var token2 = await RegisterAndGetToken("av_ready_guest");

        var host = CreateHubConnection("/hub/avalon", token1);
        var guest = CreateHubConnection("/hub/avalon", token2);
        await host.StartAsync();
        await guest.StartAsync();

        var roomJson = await host.InvokeAsync<object>("CreateRoom", 5);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!)!["roomId"].ToString()!;
        await guest.InvokeAsync<object>("JoinRoom", roomId);

        await guest.InvokeAsync("Ready", roomId);
        await guest.InvokeAsync("Unready", roomId);
    }

    [Fact]
    public async Task Unauthenticated_Connection_Fails()
    {
        var conn = new HubConnectionBuilder()
            .WithUrl($"{_http.BaseAddress!.ToString().TrimEnd('/')}/hub/avalon",
                opts => opts.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler())
            .Build();
        _connections.Add(conn);

        var ex = await Record.ExceptionAsync(() => conn.StartAsync());
        Assert.NotNull(ex);
    }
}
