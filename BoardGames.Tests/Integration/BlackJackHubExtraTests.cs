using System.Net.Http.Json;
using BoardGames.Dtos;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace BoardGames.Tests.Integration;

public class BlackJackHubExtraTests : IClassFixture<CustomWebApplicationFactory>, IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _http;
    private readonly List<HubConnection> _connections = new();

    public BlackJackHubExtraTests(CustomWebApplicationFactory factory)
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
    public async Task DoubleDown_Throws_WhenGameNotStarted()
    {
        var host = Conn(await Tok("dd_nostart"));
        await host.StartAsync();
        var roomJson = await host.InvokeAsync<object>("CreateRoom", 4);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!)!["roomId"].ToString()!;

        var ex = await Assert.ThrowsAsync<HubException>(
            () => host.InvokeAsync("BlackJackPlayerDoubleDown", roomId));
        Assert.Contains("not start yet", ex.Message);
    }

    [Fact]
    public async Task DoubleDown_Throws_ForPlayerNotInRoom()
    {
        var hostToken = await Tok("dd_inhost");
        var outsiderToken = await Tok("dd_outsider");
        var host = Conn(hostToken);
        var outsider = Conn(outsiderToken);
        await host.StartAsync();
        await outsider.StartAsync();
        var roomJson = await host.InvokeAsync<object>("CreateRoom", 4);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!)!["roomId"].ToString()!;
        await host.InvokeAsync("StartGame", roomId);
        await host.InvokeAsync("PlaceBet", roomId, 10);

        // outsider hasn't joined → DoubleDown invoke should throw
        var ex = await Assert.ThrowsAsync<HubException>(
            () => outsider.InvokeAsync("BlackJackPlayerDoubleDown", roomId));
        Assert.Contains("Player not in room", ex.Message);
    }

    [Fact]
    public async Task Hit_Throws_NotPlayersTurn()
    {
        // 2-player game: only current player can hit; other player gets "not your turn"
        var t1 = await Tok("hit_host"); var t2 = await Tok("hit_other");
        var host = Conn(t1); var other = Conn(t2);
        await host.StartAsync(); await other.StartAsync();
        var roomJson = await host.InvokeAsync<object>("CreateRoom", 4);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!)!["roomId"].ToString()!;
        await other.InvokeAsync<object>("JoinRoom", roomId);
        await other.InvokeAsync("Ready", roomId);

        var dealtTcs = new TaskCompletionSource<bool>();
        host.On<object>("GameDealt", _ => dealtTcs.TrySetResult(true));

        await host.InvokeAsync("StartGame", roomId);
        await host.InvokeAsync("PlaceBet", roomId, 10);
        await other.InvokeAsync("PlaceBet", roomId, 10);

        await dealtTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // First player (seat 0 = host) is current. `other` (seat 1) can't hit yet.
        var ex = await Assert.ThrowsAsync<HubException>(
            () => other.InvokeAsync("BlackJackPlayerHit", roomId));
        Assert.Contains("Not this player's turn", ex.Message);
    }

    [Fact]
    public async Task PlaceBet_Throws_WhenAmountExceedsBalance()
    {
        var host = Conn(await Tok("bet_over"));
        await host.StartAsync();
        var roomJson = await host.InvokeAsync<object>("CreateRoom", 4);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!)!["roomId"].ToString()!;
        await host.InvokeAsync("StartGame", roomId);

        var ex = await Assert.ThrowsAsync<HubException>(
            () => host.InvokeAsync("PlaceBet", roomId, 999999));
        Assert.Contains("Bet exceeds", ex.Message);
    }

    [Fact]
    public async Task KickPlayer_Throws_KickingSelf()
    {
        var host = Conn(await Tok("kick_self"));
        await host.StartAsync();
        var roomJson = await host.InvokeAsync<object>("CreateRoom", 4);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!)!["roomId"].ToString()!;

        var ex = await Assert.ThrowsAsync<HubException>(
            () => host.InvokeAsync("KickPlayer", roomId, 0));
        Assert.Contains("Cannot kick yourself", ex.Message);
    }

    [Fact]
    public async Task KickPlayer_Throws_SeatNotFound()
    {
        var host = Conn(await Tok("kick_nf"));
        await host.StartAsync();
        var roomJson = await host.InvokeAsync<object>("CreateRoom", 4);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!)!["roomId"].ToString()!;

        var ex = await Assert.ThrowsAsync<HubException>(
            () => host.InvokeAsync("KickPlayer", roomId, 99));
        Assert.Contains("Player not found", ex.Message);
    }

    [Fact]
    public async Task KickPlayer_RemovesPlayer()
    {
        var host = Conn(await Tok("kick_host"));
        var guest = Conn(await Tok("kick_target"));
        await host.StartAsync(); await guest.StartAsync();
        var roomJson = await host.InvokeAsync<object>("CreateRoom", 4);
        var roomId = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!)!["roomId"].ToString()!;
        await guest.InvokeAsync<object>("JoinRoom", roomId);

        var kickedTcs = new TaskCompletionSource<bool>();
        // KickPlayer sends "Kicked" with no payload, so register a parameterless handler.
        guest.On("Kicked", () => kickedTcs.TrySetResult(true));

        await host.InvokeAsync("KickPlayer", roomId, 1);

        await kickedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }
}
