using System.Net;
using System.Net.Http.Json;
using BoardGames.Data;
using BoardGames.Dtos;
using BoardGames.Models;
using BoardGames.Models.Avalon;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BoardGames.Tests.Integration;

public class AvalonGameHistoryControllerTests : IClassFixture<CustomWebApplicationFactory>, IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _http;

    public AvalonGameHistoryControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _http = _factory.CreateClient();
    }

    public ValueTask DisposeAsync()
    {
        _http.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<(string token, int userId)> RegisterAndGetToken(string username)
    {
        await _http.PostAsJsonAsync("/api/auth/register",
            new RegisterRequestDto { Username = username, Password = "pass123" });
        var resp = await _http.PostAsJsonAsync("/api/auth/login",
            new LoginRequestDto { Username = username, Password = "pass123" });
        var json = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var token = json!["token"].ToString()!;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FirstAsync(u => u.Username == username);
        return (token, user.Id);
    }

    private async Task<int> SeedFinishedGame(params int[] participantUserIds)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var game = new AvalonGameHistory
        {
            RoomId = "SEED" + Guid.NewGuid().ToString("N").Substring(0, 4),
            PlayerCount = participantUserIds.Length,
            MaxRejects = 4,
            Winner = GameWinner.Good,
            WinReason = "Seeded",
            BonusAssassination = false,
            EarlyAssassination = false,
            AssassinTargetSeat = null,
            StartedAt = DateTime.UtcNow.AddMinutes(-10),
            EndedAt = DateTime.UtcNow,
        };
        for (int i = 0; i < participantUserIds.Length; i++)
        {
            game.Players.Add(new AvalonGamePlayer
            {
                SeatIndex = i,
                UserId = participantUserIds[i],
                Nickname = "User" + participantUserIds[i],
                Role = AvalonRole.LoyalServant,
                IsWinner = true,
                BalanceDelta = 1m,
            });
        }
        db.AvalonGameHistories.Add(game);
        await db.SaveChangesAsync();
        return game.Id;
    }

    private void AuthAs(string token) =>
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

    [Fact]
    public async Task GetRecent_Unauthenticated_Returns401()
    {
        _http.DefaultRequestHeaders.Authorization = null;
        var resp = await _http.GetAsync("/api/avalon/games/recent");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task GetRecent_ReturnsEmpty_ForNewUser()
    {
        var (token, _) = await RegisterAndGetToken("hist_empty");
        AuthAs(token);

        var resp = await _http.GetAsync("/api/avalon/games/recent");
        resp.EnsureSuccessStatusCode();
        var games = await resp.Content.ReadFromJsonAsync<List<object>>();
        Assert.Empty(games!);
    }

    [Fact]
    public async Task GetRecent_ReturnsParticipantGames()
    {
        var (token, userId) = await RegisterAndGetToken("hist_part");
        await SeedFinishedGame(userId, 99999); // user participated
        await SeedFinishedGame(88888, 99999);  // user did not
        AuthAs(token);

        var resp = await _http.GetAsync("/api/avalon/games/recent");
        var games = await resp.Content.ReadFromJsonAsync<List<Dictionary<string, object>>>();
        Assert.Single(games!);
    }

    [Fact]
    public async Task GetRecent_ReturnsEmpty_ForGuest()
    {
        var guestResp = await _http.PostAsync("/api/auth/guest", null);
        var token = (await guestResp.Content.ReadFromJsonAsync<Dictionary<string, object>>())!["token"].ToString()!;
        AuthAs(token);

        var resp = await _http.GetAsync("/api/avalon/games/recent");
        resp.EnsureSuccessStatusCode();
        var games = await resp.Content.ReadFromJsonAsync<List<object>>();
        Assert.Empty(games!);
    }

    [Fact]
    public async Task GetDetail_ReturnsDetail_ForParticipant()
    {
        var (token, userId) = await RegisterAndGetToken("hist_detail");
        var gameId = await SeedFinishedGame(userId, 77777);
        AuthAs(token);

        var resp = await _http.GetAsync($"/api/avalon/games/{gameId}");
        resp.EnsureSuccessStatusCode();
        var detail = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.NotNull(detail);
        Assert.Equal(gameId, ((System.Text.Json.JsonElement)detail!["id"]).GetInt32());
    }

    [Fact]
    public async Task GetDetail_Returns404_ForNonParticipant()
    {
        var (intruderToken, _) = await RegisterAndGetToken("hist_intruder");
        var gameId = await SeedFinishedGame(55555, 66666);
        AuthAs(intruderToken);

        var resp = await _http.GetAsync($"/api/avalon/games/{gameId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetDetail_Returns404_ForGuest()
    {
        var guestResp = await _http.PostAsync("/api/auth/guest", null);
        var token = (await guestResp.Content.ReadFromJsonAsync<Dictionary<string, object>>())!["token"].ToString()!;
        var gameId = await SeedFinishedGame(11111, 22222);
        AuthAs(token);

        var resp = await _http.GetAsync($"/api/avalon/games/{gameId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetRecent_RespectsLimitAndOffset()
    {
        var (token, userId) = await RegisterAndGetToken("hist_paging");
        for (int i = 0; i < 5; i++) await SeedFinishedGame(userId, 33333);
        AuthAs(token);

        var page1 = await _http.GetAsync("/api/avalon/games/recent?limit=2&offset=0");
        var page2 = await _http.GetAsync("/api/avalon/games/recent?limit=2&offset=2");
        var games1 = await page1.Content.ReadFromJsonAsync<List<object>>();
        var games2 = await page2.Content.ReadFromJsonAsync<List<object>>();

        Assert.Equal(2, games1!.Count);
        Assert.Equal(2, games2!.Count);
    }
}
