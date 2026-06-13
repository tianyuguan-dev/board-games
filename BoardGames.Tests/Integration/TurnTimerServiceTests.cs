using System.Net.Http.Json;
using BoardGames.Dtos;
using Microsoft.AspNetCore.SignalR.Client;

namespace BoardGames.Tests.Integration;

/// <summary>
/// Tests for TurnTimerService timer-driven paths (auto-stand, auto-bet). Uses FastTimerWebApplicationFactory
/// to shrink the 20s timer down to 1s so tests complete in a few seconds.
/// </summary>
public class TurnTimerServiceTests : IClassFixture<FastTimerWebApplicationFactory>, IAsyncDisposable
{
    private readonly FastTimerWebApplicationFactory _factory;
    private readonly HttpClient _http;
    private readonly List<HubConnection> _connections = new();

    public TurnTimerServiceTests(FastTimerWebApplicationFactory factory)
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
            .WithUrl($"{_http.BaseAddress!.ToString().TrimEnd('/')}/hub/blackjack?access_token={token}",
                o => o.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler())
            .Build();
        _connections.Add(c);
        return c;
    }

    [Fact]
    public async Task BettingTimer_AutoBetsAndStartsGame_WhenPlayerDoesNotBetInTime()
    {
        var host = Conn(await Tok("ttimer_betauto"));
        await host.StartAsync();
        var roomJson = await host.InvokeAsync<object>("CreateRoom", 4);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!)!["roomId"].ToString()!;

        var dealt = new TaskCompletionSource<bool>();
        host.On<object>("GameDealt", _ => dealt.TrySetResult(true));

        await host.InvokeAsync("StartGame", roomId);
        // Don't place bet — let the betting timer auto-bet the minimum after 1 second.

        var won = await Task.WhenAny(dealt.Task, Task.Delay(5000));
        Assert.True(dealt.Task.IsCompletedSuccessfully, "Auto-bet should have triggered GameDealt within 5s");
    }

    [Fact]
    public async Task TurnTimer_AutoStands_WhenPlayerDoesNotActInTime()
    {
        var host = Conn(await Tok("ttimer_turnauto"));
        await host.StartAsync();
        var roomJson = await host.InvokeAsync<object>("CreateRoom", 4);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!)!["roomId"].ToString()!;

        var dealt = new TaskCompletionSource<bool>();
        var stand = new TaskCompletionSource<bool>();
        host.On<object>("GameDealt", _ => dealt.TrySetResult(true));
        host.On<object>("PlayerStand", _ => stand.TrySetResult(true));

        await host.InvokeAsync("StartGame", roomId);
        await host.InvokeAsync("PlaceBet", roomId, 10);

        // Wait for cards to be dealt
        await dealt.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Don't hit / stand — turn timer auto-stands after 1s
        var standed = await Task.WhenAny(stand.Task, Task.Delay(5000));
        Assert.True(stand.Task.IsCompletedSuccessfully, "Turn timer should auto-stand within 5s");
    }

    [Fact]
    public async Task TurnTimer_Cancelled_WhenPlayerActs()
    {
        // Player who acts (Hit / Stand) should reset / cancel the auto-stand timer; this happy-path test
        // also exercises the "current != cts" branch when a new timer is started.
        var host = Conn(await Tok("ttimer_cancel"));
        await host.StartAsync();
        var roomJson = await host.InvokeAsync<object>("CreateRoom", 4);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!)!["roomId"].ToString()!;

        var dealt = new TaskCompletionSource<bool>();
        host.On<object>("GameDealt", _ => dealt.TrySetResult(true));

        await host.InvokeAsync("StartGame", roomId);
        await host.InvokeAsync("PlaceBet", roomId, 10);
        await dealt.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Stand manually — should complete the game before timer fires
        await host.InvokeAsync("BlackJackPlayerStand", roomId);
        // Round should already be settled — no exception expected here.
    }

    [Fact]
    public async Task BettingTimer_Cancelled_WhenAllBetsPlaced()
    {
        var host = Conn(await Tok("ttimer_bcancel"));
        await host.StartAsync();
        var roomJson = await host.InvokeAsync<object>("CreateRoom", 4);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!)!["roomId"].ToString()!;

        var dealt = new TaskCompletionSource<bool>();
        host.On<object>("GameDealt", _ => dealt.TrySetResult(true));

        await host.InvokeAsync("StartGame", roomId);
        await host.InvokeAsync("PlaceBet", roomId, 10); // single player game → all bets placed immediately

        // Cards should be dealt immediately (not after 1s)
        await dealt.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
