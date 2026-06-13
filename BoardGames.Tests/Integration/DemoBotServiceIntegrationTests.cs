using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace BoardGames.Tests.Integration;

public class DemoBotServiceIntegrationTests : IClassFixture<CustomWebApplicationFactory>, IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _http;
    private readonly List<HubConnection> _connections = new();

    public DemoBotServiceIntegrationTests(CustomWebApplicationFactory factory)
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

    private HubConnection CreateHubConnection(string token)
    {
        var conn = new HubConnectionBuilder()
            .WithUrl($"{_http.BaseAddress!.ToString().TrimEnd('/')}/hub/avalon?access_token={token}",
                opts => opts.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler())
            .Build();
        _connections.Add(conn);
        return conn;
    }

    private async Task<string> GuestToken()
    {
        var resp = await _http.PostAsync("/api/auth/guest", null);
        var payload = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        return payload!["token"].ToString()!;
    }

    /// <summary>
    /// Runs the full scripted demo end-to-end. The bot script:
    ///   - All bots auto-confirm NightReveal
    ///   - Morgana (seat 1) proposes [1,4] → all bots reject → proposal 1 fails
    ///   - Guest (seat 2) becomes leader → proposes a team → all bots approve → mission runs
    ///   - Mission 1 cards played → assassin auto-triggers early kill → targets guest (Percival) → Good wins
    /// </summary>
    /// <summary>
    /// Runs the full scripted demo end-to-end. The demo bot service drives this:
    ///   1. Bots auto-confirm NightReveal (~800ms)
    ///   2. Morgana (seat 1) proposes [1,4] (~2s)
    ///   3. All bots vote reject (~2s) → leader rotates to guest (seat 2)
    ///   4. Guest proposes any team → bots approve → Mission
    ///   5. Bots play mission cards (~2s)
    ///   6. Early assassination triggers (~1.5s) → bot assassinates seat 2 (~1.5s) → Good wins
    /// </summary>
    [Fact]
    public async Task DemoFlow_EndsWithGoodWin_AfterMission1()
    {
        var token = await GuestToken();
        var conn = CreateHubConnection(token);

        // Latest state seen, and a TCS-list pattern for awaiting predicates without a channel
        // (channel doesn't fan-out to multiple consumers cleanly).
        System.Text.Json.JsonElement latestState = default;
        var lockObj = new object();
        var waiters = new List<(Func<System.Text.Json.JsonElement, bool> Pred, TaskCompletionSource Done)>();
        var observed = new List<string>();

        await conn.StartAsync();
        var demoInfo = await conn.InvokeAsync<object>("CreateDemoRoom");
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            demoInfo.ToString()!)!["roomId"].ToString()!;

        conn.On<System.Text.Json.JsonElement>("GameState", state =>
        {
            lock (lockObj)
            {
                latestState = state;
                var phase = state.GetProperty("phase").GetString();
                var leader = state.TryGetProperty("currentLeaderIndex", out var l) ? l.GetInt32() : -1;
                observed.Add($"{phase}@{leader}");
                if (phase == "TeamVote")
                {
                    // Fire-and-forget; CastVote is idempotent so duplicate is harmless.
                    _ = conn.InvokeAsync("CastVote", roomId, true);
                }
                foreach (var w in waiters.ToList())
                {
                    try
                    {
                        if (w.Pred(state)) { w.Done.TrySetResult(); waiters.Remove(w); }
                    }
                    catch { }
                }
            }
        });

        Task WaitFor(Func<System.Text.Json.JsonElement, bool> pred, int timeoutMs)
        {
            var tcs = new TaskCompletionSource();
            lock (lockObj)
            {
                // Check current state first
                try { if (latestState.ValueKind == System.Text.Json.JsonValueKind.Object && pred(latestState)) return Task.CompletedTask; }
                catch { }
                waiters.Add((pred, tcs));
            }
            return tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs))
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        throw new TimeoutException($"Timeout. Observed: [{string.Join(", ", observed)}]");
                });
        }

        await conn.InvokeAsync("ConfirmNightReveal", roomId);
        await WaitFor(s => s.GetProperty("phase").GetString() == "TeamProposal"
                        && s.GetProperty("currentLeaderIndex").GetInt32() == 2, 10000);

        await conn.InvokeAsync("ProposeTeam", roomId, new List<int> { 2, 0 });
        await WaitFor(s => s.GetProperty("phase").GetString() == "Mission", 6000);
        await conn.InvokeAsync("PlayMissionCard", roomId, true);

        await WaitFor(s => s.GetProperty("phase").GetString() == "GameOver", 10000);

        var winner = latestState.GetProperty("winner").GetString();
        Assert.Equal("Good", winner);
    }
}
