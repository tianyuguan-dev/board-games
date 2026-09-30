using System.Net.Http.Json;
using BoardGames.Dtos;
using Microsoft.AspNetCore.SignalR.Client;

namespace BoardGames.Tests.Integration;

/// <summary>
/// Tests for AvalonHub.CheckDisconnectedPlayer — the grace-period sweeper that aborts a game when a
/// disconnected player does not return. Uses FastTimerWebApplicationFactory to shrink the 2-hour grace
/// to 1 second.
/// </summary>
public class AvalonDisconnectGraceTests : IClassFixture<FastTimerWebApplicationFactory>, IAsyncDisposable
{
    private readonly FastTimerWebApplicationFactory _factory;
    private readonly HttpClient _http;
    private readonly List<HubConnection> _connections = new();

    public AvalonDisconnectGraceTests(FastTimerWebApplicationFactory factory)
    {
        _factory = factory;
        _http = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var c in _connections) await c.DisposeAsync();
        _http.Dispose();
    }

    private async Task<string> Tok(string user)
    {
        await _http.PostAsJsonAsync("/api/auth/register",
            new RegisterRequestDto { Username = user, Password = "pass123" });
        var resp = await _http.PostAsJsonAsync("/api/auth/login",
            new LoginRequestDto { Username = user, Password = "pass123" });
        return (await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>())!["token"].ToString()!;
    }

    private HubConnection Conn(string token)
    {
        var c = new HubConnectionBuilder()
            .WithUrl($"{_http.BaseAddress!.ToString().TrimEnd('/')}/hub/avalon?access_token={token}",
                o => o.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler())
            .Build();
        _connections.Add(c);
        return c;
    }

    [Fact]
    public async Task PlayerDisconnect_DuringLobby_DoesNotAbortAnything()
    {
        // Two players in lobby. One disconnects. After grace period, the other is still fine
        // because there's no in-progress game to abort.
        var hostToken = await Tok("disc_lobby_host");
        var guestToken = await Tok("disc_lobby_guest");
        var host = Conn(hostToken); var guest = Conn(guestToken);
        await host.StartAsync(); await guest.StartAsync();

        var roomJson = await host.InvokeAsync<object>("CreateRoom", 5, true);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!)!["roomId"].ToString()!;
        await guest.InvokeAsync<object>("JoinRoom", roomId);

        // Guest disconnects
        await guest.DisposeAsync();
        _connections.Remove(guest);

        // Wait past grace period
        await Task.Delay(2500);

        // Host should still be functional
        var bal = await host.InvokeAsync<decimal>("GetBalance");
        Assert.True(bal >= 0);
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
    public async Task HostDisconnectsMidGame_LowestSeatBecomesHost()
    {
        var conns = new List<HubConnection>();
        for (int i = 0; i < 5; i++)
        {
            var c = Conn(await Tok($"disc_host_p{i}"));
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

        var p4Host = IsHostOnceRoomHas(conns[4], 4);
        var p1Host = IsHostOnceRoomHas(conns[1], 4);

        await conns[0].DisposeAsync();
        _connections.Remove(conns[0]);

        // The host moves at disconnect time (MarkDisconnected), before any grace timer runs.
        // End-to-end regression only: after StartGame, Players is already in seat order, so the old
        // "first key" rule gives the same answer here. MarkDisconnected_Host_LowestSeatBecomesHost pins the rule.
        Assert.True(await p4Host.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(await p1Host.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task PlayerDisconnect_MidGame_TriggersGameAbortAfterGrace()
    {
        // Set up a 5-player game and disconnect one player mid-game. After the grace period the
        // server should send GameAborted to remaining players (covers the abort branch).
        var tokens = new List<string>();
        for (int i = 0; i < 5; i++) tokens.Add(await Tok($"disc_mid_p{i}"));
        var conns = tokens.Select(Conn).ToList();
        foreach (var c in conns) await c.StartAsync();

        var host = conns[0];
        var roomJson = await host.InvokeAsync<object>("CreateRoom", 5, true);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!)!["roomId"].ToString()!;

        for (int i = 1; i < 5; i++) await conns[i].InvokeAsync<object>("JoinRoom", roomId);
        for (int i = 1; i < 5; i++) await conns[i].InvokeAsync("Ready", roomId);
        await host.InvokeAsync("StartGame", roomId);

        // Track GameAborted broadcast to host
        var aborted = new TaskCompletionSource<string>();
        host.On<string>("GameAborted", reason => aborted.TrySetResult(reason));

        // Player 1 disconnects mid-game (state is NightReveal — game is in-progress)
        await conns[1].DisposeAsync();
        _connections.Remove(conns[1]);

        // Wait past grace period (1s) for sweeper to fire
        var done = await Task.WhenAny(aborted.Task, Task.Delay(5000));
        Assert.True(aborted.Task.IsCompletedSuccessfully, "GameAborted should fire after grace period");
        Assert.Contains("did not reconnect", aborted.Task.Result);
    }

    [Fact]
    public async Task DisconnectRejoinDisconnect_AbortsOnceAfterSecondGrace()
    {
        var tokens = new List<string>();
        var conns = new List<HubConnection>();
        for (int i = 0; i < 5; i++)
        {
            tokens.Add(await Tok($"disc_twice_p{i}"));
            var c = Conn(tokens[i]);
            await c.StartAsync();
            conns.Add(c);
        }
        var host = conns[0];
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            (await host.InvokeAsync<object>("CreateRoom", 5, true)).ToString()!)!["roomId"].ToString()!;
        for (int i = 1; i < 5; i++) await conns[i].InvokeAsync<object>("JoinRoom", roomId);
        for (int i = 1; i < 5; i++) await conns[i].InvokeAsync("Ready", roomId);
        await host.InvokeAsync("StartGame", roomId);

        var disconnected = System.Threading.Channels.Channel.CreateUnbounded<bool>();
        host.On<string>("PlayerDisconnected", _ => disconnected.Writer.TryWrite(true));
        var abortCount = 0;
        var aborted = new TaskCompletionSource<long>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        host.On<string>("GameAborted", _ =>
        {
            Interlocked.Increment(ref abortCount);
            aborted.TrySetResult(clock.ElapsedMilliseconds);
        });
        var rejoiner = Conn(tokens[1]);
        await rejoiner.StartAsync(); // ready before the first disconnect, so the rejoin is one quick call

        // Disconnect #1, then rejoin well inside the 1 s grace.
        await conns[1].DisposeAsync();
        _connections.Remove(conns[1]);
        await disconnected.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await rejoiner.InvokeAsync<object>("Rejoin", roomId);

        // Disconnect #2 a while later, so the stale timer from #1 expires ~0.5 s before #2's grace ends.
        await Task.Delay(500);
        await rejoiner.DisposeAsync();
        _connections.Remove(rejoiner);
        await disconnected.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var secondDisconnectAt = clock.ElapsedMilliseconds;

        var abortedAt = await aborted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await host.InvokeAsync<decimal>("GetBalance"); // round trip: any earlier message to host is delivered

        Assert.Equal(1, abortCount);
        // Only disconnect #2's timer may abort: ~1 s after it, not ~0.45 s (when #1's stale timer fires).
        // secondDisconnectAt is when the host heard about it, a little after the server's timer started,
        // so the lower bound leaves ~250 ms of slack on both sides.
        Assert.InRange(abortedAt - secondDisconnectAt, 700, 5000);
    }
}
