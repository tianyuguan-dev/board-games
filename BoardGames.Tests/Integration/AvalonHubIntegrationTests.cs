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


    // The first RoomUpdate with this many players (i.e. after someone left) says whether the connection is host.
    private static TaskCompletionSource<bool> IsHostOnceRoomHas(HubConnection conn, int playerCount)
    {
        var tcs = new TaskCompletionSource<bool>();
        conn.On<System.Text.Json.JsonElement>("RoomUpdate", u =>
        {
            if (u.GetProperty("players").GetArrayLength() == playerCount)
                tcs.TrySetResult(u.GetProperty("isHost").GetBoolean());
        });
        return tcs;
    }

    [Fact]
    public async Task HostLeavesLobby_LowestSeatBecomesHost()
    {
        var conns = new List<HubConnection>();
        for (int i = 0; i < 3; i++)
        {
            var c = CreateHubConnection("/hub/avalon", await RegisterAndGetToken($"av_hostlobby_{i}"));
            await c.StartAsync();
            conns.Add(c);
        }
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            (await conns[0].InvokeAsync<object>("CreateRoom", 5, true)).ToString()!)!["roomId"].ToString()!;
        for (int i = 1; i < 3; i++) await conns[i].InvokeAsync<object>("JoinRoom", roomId);
        // Seats become host 0, p2 1, p1 2, so join order (p1 before p2) differs from seat order.
        await conns[0].InvokeAsync("ReorderPlayer", roomId, 2, 1);

        var p2Host = IsHostOnceRoomHas(conns[2], 2);
        var p1Host = IsHostOnceRoomHas(conns[1], 2);
        await conns[0].InvokeAsync("LeaveRoom");

        Assert.True(await p2Host.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(await p1Host.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task HostLeavesMidGame_LowestSeatBecomesHost()
    {
        var conns = new List<HubConnection>();
        for (int i = 0; i < 5; i++)
        {
            var c = CreateHubConnection("/hub/avalon", await RegisterAndGetToken($"av_hostmid_{i}"));
            await c.StartAsync();
            conns.Add(c);
        }
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            (await conns[0].InvokeAsync<object>("CreateRoom", 5, true)).ToString()!)!["roomId"].ToString()!;
        for (int i = 1; i < 5; i++) await conns[i].InvokeAsync<object>("JoinRoom", roomId);
        // p4 moves to seat 1: seats host 0, p4 1, p1 2, p2 3, p3 4.
        await conns[0].InvokeAsync("ReorderPlayer", roomId, 4, 1);
        for (int i = 1; i < 5; i++) await conns[i].InvokeAsync("Ready", roomId);
        await conns[0].InvokeAsync("StartGame", roomId);

        // End-to-end regression only: StartGame rebuilds Players in seat order, so the old "first key"
        // rule gives the same answer mid-game. HostLeavesLobby_LowestSeatBecomesHost pins the rule.
        var p4Host = IsHostOnceRoomHas(conns[4], 4);
        var p1Host = IsHostOnceRoomHas(conns[1], 4);
        await conns[0].InvokeAsync("LeaveRoom");

        Assert.True(await p4Host.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(await p1Host.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task LeaveRoom_Twice_SecondIsNoOp()
    {
        var host = CreateHubConnection("/hub/avalon", await RegisterAndGetToken("av_leave2_host"));
        var guest = CreateHubConnection("/hub/avalon", await RegisterAndGetToken("av_leave2_guest"));
        await host.StartAsync(); await guest.StartAsync();
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            (await host.InvokeAsync<object>("CreateRoom", 5, true)).ToString()!)!["roomId"].ToString()!;
        await guest.InvokeAsync<object>("JoinRoom", roomId);

        var playerLeft = 0;
        host.On<int>("PlayerLeft", _ => Interlocked.Increment(ref playerLeft));

        await guest.InvokeAsync("LeaveRoom");
        await guest.InvokeAsync("LeaveRoom"); // no error, no second removal
        await host.InvokeAsync<decimal>("GetBalance"); // round trip: earlier messages to host are delivered first

        Assert.Equal(1, playerLeft);
    }

    private static string RoomIdOf(object roomJson) =>
        System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(roomJson.ToString()!)!["roomId"].ToString()!;

    [Fact]
    public async Task CreateRoom_Twice_SecondIsRefused()
    {
        var conn = CreateHubConnection("/hub/avalon", await RegisterAndGetToken("av_create_twice"));
        await conn.StartAsync();
        var roomId = RoomIdOf(await conn.InvokeAsync<object>("CreateRoom", 5, true));

        var ex = await Assert.ThrowsAsync<HubException>(() => conn.InvokeAsync<object>("CreateRoom", 5, true));

        Assert.Contains("Player is already in a room", ex.Message);
        Assert.Equal(roomId, await conn.InvokeAsync<string?>("GetActiveRoom"));
    }

    [Fact]
    public async Task CreateRoom_WhileInAnotherRoom_IsRefused()
    {
        var host = CreateHubConnection("/hub/avalon", await RegisterAndGetToken("av_create_other_host"));
        var guest = CreateHubConnection("/hub/avalon", await RegisterAndGetToken("av_create_other_guest"));
        await host.StartAsync(); await guest.StartAsync();
        var roomId = RoomIdOf(await host.InvokeAsync<object>("CreateRoom", 5, true));
        await guest.InvokeAsync<object>("JoinRoom", roomId);

        var ex = await Assert.ThrowsAsync<HubException>(() => guest.InvokeAsync<object>("CreateRoom", 5, true));

        Assert.Contains("Player is already in a room", ex.Message);
        Assert.Equal(roomId, await guest.InvokeAsync<string?>("GetActiveRoom"));
    }

    [Fact]
    public async Task CreateDemoRoom_WhileInRoom_IsRefused()
    {
        var conn = CreateHubConnection("/hub/avalon", await RegisterAndGetToken("av_demo_seated"));
        await conn.StartAsync();
        var roomId = RoomIdOf(await conn.InvokeAsync<object>("CreateRoom", 5, true));

        var ex = await Assert.ThrowsAsync<HubException>(() => conn.InvokeAsync<object>("CreateDemoRoom"));

        Assert.Contains("Player is already in a room", ex.Message);
        Assert.Equal(roomId, await conn.InvokeAsync<string?>("GetActiveRoom"));
    }

    [Fact]
    public async Task CreateRoom_AfterLeave_Works()
    {
        var conn = CreateHubConnection("/hub/avalon", await RegisterAndGetToken("av_create_after_leave"));
        await conn.StartAsync();
        var first = RoomIdOf(await conn.InvokeAsync<object>("CreateRoom", 5, true));
        await conn.InvokeAsync("LeaveRoom");

        var second = RoomIdOf(await conn.InvokeAsync<object>("CreateRoom", 5, true));

        Assert.NotEqual(first, second);
        Assert.Equal(second, await conn.InvokeAsync<string?>("GetActiveRoom"));
    }

    [Fact]
    public async Task CreateRoom_ReturnsRoomInfo()
    {
        var token = await RegisterAndGetToken("av_create");
        var conn = CreateHubConnection("/hub/avalon", token);
        await conn.StartAsync();

        var result = await conn.InvokeAsync<object>("CreateRoom", 5, true);
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

        var roomJson = await host.InvokeAsync<object>("CreateRoom", 5, true);
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

        var roomJson = await conn1.InvokeAsync<object>("CreateRoom", 5, true);
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

        var balance = await conn.InvokeAsync<decimal>("GetBalance");
        Assert.True(balance >= 0);
    }

    [Fact]
    public async Task StartGame_NotEnoughPlayers_Throws()
    {
        var token = await RegisterAndGetToken("av_toofew");
        var conn = CreateHubConnection("/hub/avalon", token);
        await conn.StartAsync();

        var roomJson = await conn.InvokeAsync<object>("CreateRoom", 5, true);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!)!["roomId"].ToString()!;

        var ex = await Assert.ThrowsAsync<HubException>(
            () => conn.InvokeAsync("StartGame", roomId));
        Assert.Contains("Need 5 players", ex.Message);
    }

    [Fact]
    public async Task LeaveRoom_Works()
    {
        var token = await RegisterAndGetToken("av_leave");
        var conn = CreateHubConnection("/hub/avalon", token);
        await conn.StartAsync();

        await conn.InvokeAsync<object>("CreateRoom", 5, true);
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

        var roomJson = await host.InvokeAsync<object>("CreateRoom", 5, true);
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

    [Fact]
    public async Task GetLeaderboard_ReturnsList()
    {
        var token = await RegisterAndGetToken("av_leader");
        var conn = CreateHubConnection("/hub/avalon", token);
        await conn.StartAsync();

        var result = await conn.InvokeAsync<object>("GetLeaderboard");
        Assert.NotNull(result);
    }
}
