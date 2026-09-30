using System.Net.Http.Json;
using BoardGames.Dtos;
using Microsoft.AspNetCore.SignalR.Client;

namespace BoardGames.Tests.Integration;

/// <summary>
/// Timer tests that place a bet by hand before the betting timer fires. Uses ManualBetTimerWebApplicationFactory
/// (5 s betting timer) so the manual bet cannot lose the race on a slow runner; the turn timer stays at 1 s.
/// </summary>
public class ManualBetTimerTests : IClassFixture<ManualBetTimerWebApplicationFactory>, IAsyncDisposable
{
    private readonly ManualBetTimerWebApplicationFactory _factory;
    private readonly HttpClient _http;
    private readonly List<HubConnection> _connections = new();

    public ManualBetTimerTests(ManualBetTimerWebApplicationFactory factory)
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

        // Cards are dealt immediately, not by the 5 s betting timer
        await dealt.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
