using System.Net.Http.Json;
using System.Text.Json;
using BoardGames.Dtos;
using BoardGames.Models.BlackJack;
using BoardGames.Models.Poker;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace BoardGames.Tests.Integration;

// The test factory deals from an unshuffled shoe, so every BlackJack integration test sees the same cards.
public class BlackJackDeterministicDeckTests : IClassFixture<CustomWebApplicationFactory>, IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _http;
    private readonly List<HubConnection> _connections = new();

    public BlackJackDeterministicDeckTests(CustomWebApplicationFactory factory)
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

    private async Task<HubConnection> Connect(string username)
    {
        await _http.PostAsJsonAsync("/api/auth/register",
            new RegisterRequestDto { Username = username, Password = "pass123" });
        var response = await _http.PostAsJsonAsync("/api/auth/login",
            new LoginRequestDto { Username = username, Password = "pass123" });
        var token = (await response.Content.ReadFromJsonAsync<Dictionary<string, object>>())!["token"].ToString()!;
        var conn = new HubConnectionBuilder()
            .WithUrl($"{_http.BaseAddress!.ToString().TrimEnd('/')}/hub/blackjack?access_token={token}",
                opts => opts.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler())
            .Build();
        _connections.Add(conn);
        await conn.StartAsync();
        return conn;
    }

    private static string RoomIdOf(object roomJson) =>
        JsonSerializer.Deserialize<Dictionary<string, object>>(roomJson.ToString()!)!["roomId"].ToString()!;

    private static List<int> PlayerValues(JsonElement dto) =>
        dto.GetProperty("playerHands").EnumerateArray().Select(h => h.GetProperty("value").GetInt32()).ToList();

    private static TaskCompletionSource<JsonElement> WaitForFinished(HubConnection conn)
    {
        var finished = new TaskCompletionSource<JsonElement>();
        conn.On<JsonElement>("PlayerStand", dto =>
        {
            if (dto.GetProperty("state").GetInt32() == (int)BlackJackGameState.Finished)
                finished.TrySetResult(dto);
        });
        return finished;
    }

    [Fact]
    public async Task TwoPlayerRound_DealsKnownHands()
    {
        var host = await Connect("det_two_host");
        var guest = await Connect("det_two_guest");
        var roomId = RoomIdOf(await host.InvokeAsync<object>("CreateRoom", 4));
        await guest.InvokeAsync<object>("JoinRoom", roomId);
        await guest.InvokeAsync("Ready", roomId);

        var dealt = new TaskCompletionSource<JsonElement>();
        host.On<JsonElement>("GameDealt", dto => dealt.TrySetResult(dto));
        var finished = WaitForFinished(host);

        await host.InvokeAsync("StartGame", roomId);
        await host.InvokeAsync("PlaceBet", roomId, 10);
        await guest.InvokeAsync("PlaceBet", roomId, 10);
        var deal = await dealt.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // P0 K♦ Q♦ = 20, P1 J♦ 10♦ = 20; seat 0 to act.
        Assert.Equal([20, 20], PlayerValues(deal));
        Assert.Equal(0, deal.GetProperty("currentIndex").GetInt32());
        Assert.Equal((int)BlackJackGameState.PlayerTurn, deal.GetProperty("state").GetInt32());

        await host.InvokeAsync("BlackJackPlayerStand", roomId);
        await guest.InvokeAsync("BlackJackPlayerStand", roomId);
        var end = await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Dealer 9♦ 8♦ = 17 stands without drawing.
        var dealer = end.GetProperty("dealerHand");
        Assert.Equal(17, dealer.GetProperty("value").GetInt32());
        Assert.Equal(2, dealer.GetProperty("cards").GetArrayLength());
    }

    [Fact]
    public async Task OnePlayerRound_DealsKnownHands()
    {
        var host = await Connect("det_one_host");
        var roomId = RoomIdOf(await host.InvokeAsync<object>("CreateRoom", 4));

        var dealt = new TaskCompletionSource<JsonElement>();
        host.On<JsonElement>("GameDealt", dto => dealt.TrySetResult(dto));
        var finished = WaitForFinished(host);

        await host.InvokeAsync("StartGame", roomId);
        await host.InvokeAsync("PlaceBet", roomId, 10);
        var deal = await dealt.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal([20], PlayerValues(deal)); // K♦ Q♦

        await host.InvokeAsync("BlackJackPlayerStand", roomId);
        var end = await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Dealer J♦ 10♦ = 20 against the player's 20.
        Assert.Equal(20, end.GetProperty("dealerHand").GetProperty("value").GetInt32());
        Assert.Equal((int)BlackJackGameResult.Push, end.GetProperty("results")[0].GetInt32());
    }

    [Fact]
    public void TestFactory_RegistersUnshuffledDeckFactory()
    {
        Assert.IsType<UnshuffledDeckFactory>(_factory.Services.GetRequiredService<IDeckFactory>());
    }

    private class ProductionDeckFactory : CustomWebApplicationFactory
    {
        protected override bool UseUnshuffledDeck => false;
    }

    [Fact]
    public async Task ProductionRegistration_IsShuffledDeckFactory()
    {
        await using var production = new ProductionDeckFactory();

        Assert.IsType<ShuffledDeckFactory>(production.Services.GetRequiredService<IDeckFactory>());
    }
}
