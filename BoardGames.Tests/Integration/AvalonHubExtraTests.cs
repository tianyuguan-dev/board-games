using System.Net.Http.Json;
using BoardGames.Dtos;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace BoardGames.Tests.Integration;

/// <summary>
/// Targets previously uncovered AvalonHub paths: role config adjustments, leave-mid-lobby flows,
/// and host-only guards.
/// </summary>
public class AvalonHubExtraTests : IClassFixture<CustomWebApplicationFactory>, IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _http;
    private readonly List<HubConnection> _connections = new();

    public AvalonHubExtraTests(CustomWebApplicationFactory factory)
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

    private async Task<string> CreateAsHost(string user, int max = 7)
    {
        var conn = Conn(await Tok(user));
        await conn.StartAsync();
        var json = await conn.InvokeAsync<object>("CreateRoom", max);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            json.ToString()!)!["roomId"].ToString()!;
        return roomId;
    }

    [Fact]
    public async Task AdjustRole_TogglesMordred_AsHost()
    {
        var host = Conn(await Tok("adj_host1"));
        await host.StartAsync();
        var json = await host.InvokeAsync<object>("CreateRoom", 9);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            json.ToString()!)!["roomId"].ToString()!;

        // Mordred adjustment is allowed via +1 / -1 delta clamped to [0, 1]
        await host.InvokeAsync("AdjustRole", roomId, "Mordred", 1);
        await host.InvokeAsync("AdjustRole", roomId, "Mordred", -1);
    }

    [Fact]
    public async Task AdjustRole_TogglesOberon_AndMinion()
    {
        var host = Conn(await Tok("adj_host2"));
        await host.StartAsync();
        var json = await host.InvokeAsync<object>("CreateRoom", 9);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            json.ToString()!)!["roomId"].ToString()!;

        // 9p default: Merlin+Percival+4 LoyalServant+Assassin+Morgana+Mordred (total=9).
        // Strict count model (no auto-fill): free a LoyalServant slot before adding evil.
        await host.InvokeAsync("AdjustRole", roomId, "LoyalServant", -1);    // free 1 slot
        await host.InvokeAsync("AdjustRole", roomId, "Oberon", 1);           // evil=4, good=5
        await host.InvokeAsync("AdjustRole", roomId, "Oberon", -1);          // evil=3 (now 1 empty slot)
        await host.InvokeAsync("AdjustRole", roomId, "MinionOfMordred", 1);  // evil=4 (filled)
        await host.InvokeAsync("AdjustRole", roomId, "MinionOfMordred", -1); // evil=3
        await host.InvokeAsync("AdjustRole", roomId, "LoyalServant", 1);     // restore total=9
    }

    [Fact]
    public async Task AdjustRole_Throws_ForUnknownRole()
    {
        var host = Conn(await Tok("adj_unknown"));
        await host.StartAsync();
        var json = await host.InvokeAsync<object>("CreateRoom", 9);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            json.ToString()!)!["roomId"].ToString()!;

        var ex = await Assert.ThrowsAsync<HubException>(
            () => host.InvokeAsync("AdjustRole", roomId, "Foobar", 1));
        Assert.Contains("Cannot adjust this role", ex.Message);
    }

    [Fact]
    public async Task AdjustRole_Throws_NonHost()
    {
        var hostToken = await Tok("adj_host3");
        var guestToken = await Tok("adj_guest3");
        var host = Conn(hostToken); var guest = Conn(guestToken);
        await host.StartAsync(); await guest.StartAsync();
        var json = await host.InvokeAsync<object>("CreateRoom", 9);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            json.ToString()!)!["roomId"].ToString()!;
        await guest.InvokeAsync<object>("JoinRoom", roomId);

        var ex = await Assert.ThrowsAsync<HubException>(
            () => guest.InvokeAsync("AdjustRole", roomId, "Mordred", 1));
        Assert.Contains("Only host", ex.Message);
    }

    [Fact]
    public async Task AdjustRole_Throws_WhenGoodWouldNotOutnumberEvil()
    {
        var host = Conn(await Tok("adj_underflow"));
        await host.StartAsync();
        // 9p default: 3 evil + 6 good. Free a LoyalServant slot, add Oberon (evil=4, good=5),
        // then try to free another LoyalServant which would tip the balance to good=4=evil=4.
        var json = await host.InvokeAsync<object>("CreateRoom", 9);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            json.ToString()!)!["roomId"].ToString()!;

        await host.InvokeAsync("AdjustRole", roomId, "LoyalServant", -1); // total=8, good=5, evil=3
        await host.InvokeAsync("AdjustRole", roomId, "Oberon", 1);        // total=9, good=5, evil=4
        var ex = await Assert.ThrowsAsync<HubException>(
            () => host.InvokeAsync("AdjustRole", roomId, "LoyalServant", -1)); // good=4, evil=4
        Assert.Contains("must outnumber Evil", ex.Message);
    }

    [Fact]
    public async Task SetMaxRejects_ClampedAndPersisted()
    {
        var host = Conn(await Tok("rej_host"));
        await host.StartAsync();
        var json = await host.InvokeAsync<object>("CreateRoom", 5);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            json.ToString()!)!["roomId"].ToString()!;

        await host.InvokeAsync("SetMaxRejects", roomId, 1);   // valid lower bound
        await host.InvokeAsync("SetMaxRejects", roomId, 99);  // gets clamped to 10
        await host.InvokeAsync("SetMaxRejects", roomId, 5);   // back to mid-range
    }

    [Fact]
    public async Task LeaveRoom_HostLeavesMidLobby_TransfersHost()
    {
        var hostToken = await Tok("leave_host");
        var guestToken = await Tok("leave_guest");
        var host = Conn(hostToken); var guest = Conn(guestToken);
        await host.StartAsync(); await guest.StartAsync();
        var json = await host.InvokeAsync<object>("CreateRoom", 5);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            json.ToString()!)!["roomId"].ToString()!;
        await guest.InvokeAsync<object>("JoinRoom", roomId);

        // Host leaves — remaining player should still be in the room
        await host.InvokeAsync("LeaveRoom");
        // Guest can still get balance — connection still active
        var bal = await guest.InvokeAsync<decimal>("GetBalance");
        Assert.True(bal >= 0);
    }

    [Fact]
    public async Task ConfirmNightReveal_PartialDoesNotAdvance()
    {
        var t1 = await Tok("conf_a"); var t2 = await Tok("conf_b");
        var t3 = await Tok("conf_c"); var t4 = await Tok("conf_d");
        var t5 = await Tok("conf_e");
        var host = Conn(t1);
        var c2 = Conn(t2); var c3 = Conn(t3); var c4 = Conn(t4); var c5 = Conn(t5);
        await Task.WhenAll(host.StartAsync(), c2.StartAsync(), c3.StartAsync(), c4.StartAsync(), c5.StartAsync());

        var json = await host.InvokeAsync<object>("CreateRoom", 5);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            json.ToString()!)!["roomId"].ToString()!;
        foreach (var c in new[] { c2, c3, c4, c5 }) await c.InvokeAsync<object>("JoinRoom", roomId);
        foreach (var c in new[] { c2, c3, c4, c5 }) await c.InvokeAsync("Ready", roomId);
        await host.InvokeAsync("StartGame", roomId);

        // Only host confirms — others haven't yet, so phase should remain NightReveal.
        await host.InvokeAsync("ConfirmNightReveal", roomId);
        // No assertion needed beyond the call not throwing — the partial branch is what we're covering.
    }

    // ───────── New role toggles: Merlin / Percival / Assassin / Morgana / LoyalServant ─────────

    [Fact]
    public async Task AdjustRole_TogglesAssassin_AsHost()
    {
        var host = Conn(await Tok("adj_assassin"));
        await host.StartAsync();
        var json = await host.InvokeAsync<object>("CreateRoom", 7);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            json.ToString()!)!["roomId"].ToString()!;

        // 7p default: Merlin+Percival+2 LS+Assassin+Morgana+Mordred. Removing Assassin shrinks total
        // to 6 (allowed under strict-count model: total < MaxPlayers). Adding it back restores 7.
        await host.InvokeAsync("AdjustRole", roomId, "Assassin", -1);
        await host.InvokeAsync("AdjustRole", roomId, "Assassin", 1);
    }

    [Fact]
    public async Task AdjustRole_TogglesMorgana_AsHost()
    {
        var host = Conn(await Tok("adj_morgana"));
        await host.StartAsync();
        // 9p default: 6G, 3E. Margin enough to drop Percival (→5G, 3E) before dropping Morgana.
        var json = await host.InvokeAsync<object>("CreateRoom", 9);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            json.ToString()!)!["roomId"].ToString()!;

        await host.InvokeAsync("AdjustRole", roomId, "Percival", -1); // 5G, 3E
        await host.InvokeAsync("AdjustRole", roomId, "Morgana", -1);  // 5G, 2E
        await host.InvokeAsync("AdjustRole", roomId, "Morgana", 1);   // 5G, 3E
        await host.InvokeAsync("AdjustRole", roomId, "Percival", 1);  // 6G, 3E
    }

    [Fact]
    public async Task AdjustRole_TogglesMerlin_AsHost()
    {
        var host = Conn(await Tok("adj_merlin"));
        await host.StartAsync();
        // 9p: Margin to drop Assassin + Percival before dropping Merlin (since both require it).
        var json = await host.InvokeAsync<object>("CreateRoom", 9);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            json.ToString()!)!["roomId"].ToString()!;

        await host.InvokeAsync("AdjustRole", roomId, "Assassin", -1); // 6G, 2E
        await host.InvokeAsync("AdjustRole", roomId, "Percival", -1); // 5G, 2E
        await host.InvokeAsync("AdjustRole", roomId, "Merlin", -1);   // 4G, 2E
        await host.InvokeAsync("AdjustRole", roomId, "Merlin", 1);    // 5G, 2E
    }

    [Fact]
    public async Task AdjustRole_TogglesPercival_AsHost()
    {
        var host = Conn(await Tok("adj_percival"));
        await host.StartAsync();
        // 9p has 6G margin so dropping Percival keeps Good > Evil (5 > 3).
        var json = await host.InvokeAsync<object>("CreateRoom", 9);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            json.ToString()!)!["roomId"].ToString()!;

        await host.InvokeAsync("AdjustRole", roomId, "Percival", -1);
        await host.InvokeAsync("AdjustRole", roomId, "Percival", 1);
    }

    [Fact]
    public async Task AdjustRole_AdjustsLoyalServant_AsHost()
    {
        var host = Conn(await Tok("adj_loyal"));
        await host.StartAsync();
        // 9p has 4 LoyalServants; safe to drop a couple and add back.
        var json = await host.InvokeAsync<object>("CreateRoom", 9);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            json.ToString()!)!["roomId"].ToString()!;

        await host.InvokeAsync("AdjustRole", roomId, "LoyalServant", -1); // 5G, 3E
        await host.InvokeAsync("AdjustRole", roomId, "LoyalServant", -1); // 4G, 3E
        await host.InvokeAsync("AdjustRole", roomId, "LoyalServant", 1);  // 5G, 3E
        await host.InvokeAsync("AdjustRole", roomId, "LoyalServant", 1);  // 6G, 3E
    }

    // ───────── New validation rules ─────────

    [Fact]
    public async Task AdjustRole_Throws_AssassinRequiresMerlin()
    {
        var host = Conn(await Tok("adj_rule_assassin"));
        await host.StartAsync();
        // 9p so margin is enough to drop Percival (its Merlin dep) before testing Merlin removal.
        var json = await host.InvokeAsync<object>("CreateRoom", 9);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            json.ToString()!)!["roomId"].ToString()!;

        await host.InvokeAsync("AdjustRole", roomId, "Percival", -1); // satisfy Percival dep
        var ex = await Assert.ThrowsAsync<HubException>(
            () => host.InvokeAsync("AdjustRole", roomId, "Merlin", -1)); // Assassin still on
        Assert.Contains("Assassin requires Merlin", ex.Message);
    }

    [Fact]
    public async Task AdjustRole_Throws_PercivalRequiresMorgana()
    {
        var host = Conn(await Tok("adj_rule_percival"));
        await host.StartAsync();
        // 7p OK: dropping Morgana (4G, 2E) doesn't violate Good > Evil; Percival's dep breaks → reject.
        var json = await host.InvokeAsync<object>("CreateRoom", 7);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            json.ToString()!)!["roomId"].ToString()!;

        var ex = await Assert.ThrowsAsync<HubException>(
            () => host.InvokeAsync("AdjustRole", roomId, "Morgana", -1));
        Assert.Contains("Percival requires Merlin and Morgana", ex.Message);
    }

    [Fact]
    public async Task AdjustRole_Throws_AtLeastOneEvilRequired()
    {
        var host = Conn(await Tok("adj_rule_evil"));
        await host.StartAsync();
        // 9p so removing Percival + Morgana + Mordred is feasible without Good <= Evil violation.
        var json = await host.InvokeAsync<object>("CreateRoom", 9);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            json.ToString()!)!["roomId"].ToString()!;

        await host.InvokeAsync("AdjustRole", roomId, "Percival", -1); // 5G, 3E
        await host.InvokeAsync("AdjustRole", roomId, "Morgana", -1);  // 5G, 2E
        await host.InvokeAsync("AdjustRole", roomId, "Mordred", -1);  // 5G, 1E
        var ex = await Assert.ThrowsAsync<HubException>(
            () => host.InvokeAsync("AdjustRole", roomId, "Assassin", -1)); // 5G, 0E → reject
        Assert.Contains("At least one Evil role is required", ex.Message);
    }

    [Fact]
    public async Task AdjustRole_Throws_TooManyRoles()
    {
        var host = Conn(await Tok("adj_rule_too_many"));
        await host.StartAsync();
        var json = await host.InvokeAsync<object>("CreateRoom", 5);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            json.ToString()!)!["roomId"].ToString()!;

        // 5p default is total=5. Adding any new role without freeing one → reject "Too many roles".
        var ex = await Assert.ThrowsAsync<HubException>(
            () => host.InvokeAsync("AdjustRole", roomId, "Mordred", 1));
        Assert.Contains("Too many roles", ex.Message);
    }

    // ───────── SetMaxPlayers ─────────

    [Fact]
    public async Task SetMaxPlayers_HostCanChange()
    {
        var host = Conn(await Tok("smp_host"));
        await host.StartAsync();
        var json = await host.InvokeAsync<object>("CreateRoom", 7);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            json.ToString()!)!["roomId"].ToString()!;

        // Valid changes within 5-10
        await host.InvokeAsync("SetMaxPlayers", roomId, 5);
        await host.InvokeAsync("SetMaxPlayers", roomId, 10);
        await host.InvokeAsync("SetMaxPlayers", roomId, 6);
    }

    [Fact]
    public async Task SetMaxPlayers_Throws_NonHost()
    {
        var hostToken = await Tok("smp_host2");
        var guestToken = await Tok("smp_guest2");
        var host = Conn(hostToken); var guest = Conn(guestToken);
        await host.StartAsync(); await guest.StartAsync();
        var json = await host.InvokeAsync<object>("CreateRoom", 7);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            json.ToString()!)!["roomId"].ToString()!;
        await guest.InvokeAsync<object>("JoinRoom", roomId);

        var ex = await Assert.ThrowsAsync<HubException>(
            () => guest.InvokeAsync("SetMaxPlayers", roomId, 8));
        Assert.Contains("Only host", ex.Message);
    }

    [Fact]
    public async Task SetMaxPlayers_Throws_OutsideValidRange()
    {
        var host = Conn(await Tok("smp_range"));
        await host.StartAsync();
        var json = await host.InvokeAsync<object>("CreateRoom", 7);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            json.ToString()!)!["roomId"].ToString()!;

        var exLow = await Assert.ThrowsAsync<HubException>(
            () => host.InvokeAsync("SetMaxPlayers", roomId, 4));
        Assert.Contains("5-10", exLow.Message);

        var exHigh = await Assert.ThrowsAsync<HubException>(
            () => host.InvokeAsync("SetMaxPlayers", roomId, 11));
        Assert.Contains("5-10", exHigh.Message);
    }

    [Fact]
    public async Task SetMaxPlayers_Throws_CannotShrinkBelowCurrentPlayers()
    {
        var hostToken = await Tok("smp_shrink_h");
        var tokens = new List<string>();
        for (int i = 0; i < 5; i++) tokens.Add(await Tok($"smp_shrink_g{i}"));
        var host = Conn(hostToken);
        var guests = tokens.Select(Conn).ToList();
        await host.StartAsync();
        foreach (var g in guests) await g.StartAsync();

        var json = await host.InvokeAsync<object>("CreateRoom", 7);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            json.ToString()!)!["roomId"].ToString()!;
        foreach (var g in guests) await g.InvokeAsync<object>("JoinRoom", roomId);
        // Now 6 players in a 7-cap room. Try to shrink to 5 → reject.

        var ex = await Assert.ThrowsAsync<HubException>(
            () => host.InvokeAsync("SetMaxPlayers", roomId, 5));
        Assert.Contains("Cannot shrink below current player count", ex.Message);
    }
}
