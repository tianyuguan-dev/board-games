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

        var roomJson = await host.InvokeAsync<object>("CreateRoom", 5);
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
        var roomJson = await host.InvokeAsync<object>("CreateRoom", 5);
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
}
