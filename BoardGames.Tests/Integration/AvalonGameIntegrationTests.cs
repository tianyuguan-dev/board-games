using System.Net.Http.Json;
using System.Text.Json;
using BoardGames.Dtos;
using Microsoft.AspNetCore.SignalR.Client;

namespace BoardGames.Tests.Integration;

public class AvalonGameIntegrationTests : IClassFixture<CustomWebApplicationFactory>, IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _http;
    private readonly List<HubConnection> _connections = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public AvalonGameIntegrationTests(CustomWebApplicationFactory factory)
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

    private HubConnection CreateHubConnection(string token)
    {
        var conn = new HubConnectionBuilder()
            .WithUrl($"{_http.BaseAddress!.ToString().TrimEnd('/')}/hub/avalon?access_token={token}",
                opts => opts.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler())
            .Build();
        _connections.Add(conn);
        return conn;
    }

    private class PlayerState
    {
        public HubConnection Connection { get; set; } = null!;
        public string Token { get; set; } = "";
        public int SeatIndex { get; set; } = -1;
        public string? Phase { get; set; }
        public string? MyRole { get; set; }
        public string? MyTeam { get; set; }
        public int CurrentLeaderIndex { get; set; }
        public int CurrentMissionIndex { get; set; }
        public int RequiredTeamSize { get; set; }
        public int? AssassinIndex { get; set; }
        public string? Winner { get; set; }
        public TaskCompletionSource<bool> StateUpdated { get; set; } = new();

        public void ResetWaiter() => StateUpdated = new TaskCompletionSource<bool>();
    }

    private void AttachHandlers(PlayerState player)
    {
        player.Connection.On<int>("YourSeat", seat => player.SeatIndex = seat);
        player.Connection.On<JsonElement>("GameState", state =>
        {
            player.Phase = state.GetProperty("phase").GetString();
            player.MyRole = state.GetProperty("myRole").GetString();
            player.MyTeam = state.GetProperty("myTeam").GetString();
            player.CurrentLeaderIndex = state.GetProperty("currentLeaderIndex").GetInt32();
            player.CurrentMissionIndex = state.GetProperty("currentMissionIndex").GetInt32();
            player.RequiredTeamSize = state.GetProperty("requiredTeamSize").GetInt32();
            if (state.TryGetProperty("assassinIndex", out var ai) && ai.ValueKind == JsonValueKind.Number)
                player.AssassinIndex = ai.GetInt32();
            if (state.TryGetProperty("winner", out var w) && w.ValueKind == JsonValueKind.String)
                player.Winner = w.GetString();
            player.StateUpdated.TrySetResult(true);
        });
    }

    private async Task WaitForAllStates(PlayerState[] players, int timeoutMs = 5000)
    {
        var tasks = players.Select(p => p.StateUpdated.Task).ToArray();
        await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(timeoutMs));
        foreach (var p in players) p.ResetWaiter();
    }

    [Fact]
    public async Task FullGame_5Players_CompletesSuccessfully()
    {
        var players = new PlayerState[5];
        for (int i = 0; i < 5; i++)
        {
            var token = await RegisterAndGetToken($"av_game_{i}");
            var conn = CreateHubConnection(token);
            players[i] = new PlayerState { Connection = conn, Token = token };
            AttachHandlers(players[i]);
            await conn.StartAsync();
        }

        // Host creates room
        var roomJson = await players[0].Connection.InvokeAsync<object>("CreateRoom", 5);
        var roomId = JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!, JsonOpts)!["roomId"].ToString()!;

        // Others join
        for (int i = 1; i < 5; i++)
            await players[i].Connection.InvokeAsync<object>("JoinRoom", roomId);

        // Non-host players ready
        for (int i = 1; i < 5; i++)
            await players[i].Connection.InvokeAsync("Ready", roomId);

        // Start game — triggers NightReveal
        await players[0].Connection.InvokeAsync("StartGame", roomId);
        await WaitForAllStates(players);
        Assert.Equal("NightReveal", players[0].Phase);

        // All confirm night reveal
        for (int i = 0; i < 5; i++)
            await players[i].Connection.InvokeAsync("ConfirmNightReveal", roomId);
        await WaitForAllStates(players);
        Assert.Equal("TeamProposal", players[0].Phase);

        // Play up to 5 missions (game ends at 3 wins for either side)
        int maxRounds = 25;
        while (players[0].Phase != "GameOver" && players[0].Phase != "Assassination" && maxRounds-- > 0)
        {
            if (players[0].Phase == "TeamProposal")
            {
                var leader = players.First(p => p.SeatIndex == players[0].CurrentLeaderIndex);
                var teamSize = players[0].RequiredTeamSize;
                var team = Enumerable.Range(0, teamSize).ToList();
                await leader.Connection.InvokeAsync("ProposeTeam", roomId, team);
                await WaitForAllStates(players);
            }

            if (players[0].Phase == "TeamVote")
            {
                for (int i = 0; i < 5; i++)
                    await players[i].Connection.InvokeAsync("CastVote", roomId, true);
                await WaitForAllStates(players);
            }

            if (players[0].Phase == "Mission")
            {
                var teamSize = players[0].RequiredTeamSize;
                for (int i = 0; i < teamSize; i++)
                {
                    var isEvil = players[i].MyTeam == "Evil";
                    await players[i].Connection.InvokeAsync("PlayMissionCard", roomId, !isEvil);
                }
                await WaitForAllStates(players);
            }
        }

        // Handle assassination if needed
        if (players[0].Phase == "Assassination")
        {
            var assassinSeat = players[0].AssassinIndex!.Value;
            var assassin = players.First(p => p.SeatIndex == assassinSeat);
            var target = assassin.SeatIndex == 0 ? 1 : 0;
            await assassin.Connection.InvokeAsync("Assassinate", roomId, target);
            await WaitForAllStates(players);
        }

        Assert.Equal("GameOver", players[0].Phase);
        Assert.NotNull(players[0].Winner);
        Assert.True(players[0].Winner == "Good" || players[0].Winner == "Evil");
    }

    [Fact]
    public async Task Game_ConsecutiveRejects_EvilWins()
    {
        var players = new PlayerState[5];
        for (int i = 0; i < 5; i++)
        {
            var token = await RegisterAndGetToken($"av_reject_{i}");
            var conn = CreateHubConnection(token);
            players[i] = new PlayerState { Connection = conn, Token = token };
            AttachHandlers(players[i]);
            await conn.StartAsync();
        }

        var roomJson = await players[0].Connection.InvokeAsync<object>("CreateRoom", 5);
        var roomId = JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!, JsonOpts)!["roomId"].ToString()!;

        for (int i = 1; i < 5; i++)
            await players[i].Connection.InvokeAsync<object>("JoinRoom", roomId);
        for (int i = 1; i < 5; i++)
            await players[i].Connection.InvokeAsync("Ready", roomId);

        await players[0].Connection.InvokeAsync("StartGame", roomId);
        await WaitForAllStates(players);

        for (int i = 0; i < 5; i++)
            await players[i].Connection.InvokeAsync("ConfirmNightReveal", roomId);
        await WaitForAllStates(players);

        // Reject proposals 5 times in a row — evil wins
        for (int reject = 0; reject < 5 && players[0].Phase == "TeamProposal"; reject++)
        {
            var leader = players.First(p => p.SeatIndex == players[0].CurrentLeaderIndex);
            var team = Enumerable.Range(0, players[0].RequiredTeamSize).ToList();
            await leader.Connection.InvokeAsync("ProposeTeam", roomId, team);
            await WaitForAllStates(players);

            // Majority rejects (3 out of 5)
            for (int i = 0; i < 5; i++)
                await players[i].Connection.InvokeAsync("CastVote", roomId, false);
            await WaitForAllStates(players);
        }

        Assert.Equal("GameOver", players[0].Phase);
        Assert.Equal("Evil", players[0].Winner);
    }

    [Fact]
    public async Task KickPlayer_BeforeGame_Works()
    {
        var players = new PlayerState[3];
        for (int i = 0; i < 3; i++)
        {
            var token = await RegisterAndGetToken($"av_kick_{i}");
            var conn = CreateHubConnection(token);
            players[i] = new PlayerState { Connection = conn };
            AttachHandlers(players[i]);
            await conn.StartAsync();
        }

        var roomJson = await players[0].Connection.InvokeAsync<object>("CreateRoom", 5);
        var roomId = JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!, JsonOpts)!["roomId"].ToString()!;

        await players[1].Connection.InvokeAsync<object>("JoinRoom", roomId);
        await players[2].Connection.InvokeAsync<object>("JoinRoom", roomId);

        var kicked = new TaskCompletionSource<bool>();
        players[1].Connection.On<string>("Kicked", _ => kicked.TrySetResult(true));

        await players[0].Connection.InvokeAsync("KickPlayer", roomId, 1);

        var result = await Task.WhenAny(kicked.Task, Task.Delay(3000));
        Assert.True(kicked.Task.IsCompletedSuccessfully, "Player should have been kicked");
    }

    [Fact]
    public async Task AdjustRoles_HostCanModify()
    {
        var token = await RegisterAndGetToken("av_roles");
        var conn = CreateHubConnection(token);
        await conn.StartAsync();

        var roomJson = await conn.InvokeAsync<object>("CreateRoom", 9);
        var roomId = JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!, JsonOpts)!["roomId"].ToString()!;

        await conn.InvokeAsync("AdjustRole", roomId, "Oberon", 1);
        await conn.InvokeAsync("AdjustRole", roomId, "Oberon", -1);
    }

    [Fact]
    public async Task GetActiveRoom_ReturnsNull_WhenNotInRoom()
    {
        var token = await RegisterAndGetToken("av_noroom");
        var conn = CreateHubConnection(token);
        await conn.StartAsync();

        var result = await conn.InvokeAsync<string?>("GetActiveRoom");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetActiveRoom_ReturnsRoomId_WhenInRoom()
    {
        var token = await RegisterAndGetToken("av_active");
        var conn = CreateHubConnection(token);
        await conn.StartAsync();

        var roomJson = await conn.InvokeAsync<object>("CreateRoom", 5);
        var roomId = JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!, JsonOpts)!["roomId"].ToString()!;

        var result = await conn.InvokeAsync<string?>("GetActiveRoom");
        Assert.Equal(roomId, result);
    }

    private async Task<(PlayerState[] players, string roomId)> SetupGameInProgress(string prefix)
    {
        var players = new PlayerState[5];
        var tokens = new string[5];
        for (int i = 0; i < 5; i++)
        {
            tokens[i] = await RegisterAndGetToken($"{prefix}_{i}");
            var conn = CreateHubConnection(tokens[i]);
            players[i] = new PlayerState { Connection = conn, Token = tokens[i] };
            AttachHandlers(players[i]);
            await conn.StartAsync();
        }

        var roomJson = await players[0].Connection.InvokeAsync<object>("CreateRoom", 5);
        var roomId = JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!, JsonOpts)!["roomId"].ToString()!;

        for (int i = 1; i < 5; i++)
            await players[i].Connection.InvokeAsync<object>("JoinRoom", roomId);
        for (int i = 1; i < 5; i++)
            await players[i].Connection.InvokeAsync("Ready", roomId);

        await players[0].Connection.InvokeAsync("StartGame", roomId);
        await WaitForAllStates(players);

        for (int i = 0; i < 5; i++)
            await players[i].Connection.InvokeAsync("ConfirmNightReveal", roomId);
        await WaitForAllStates(players);

        return (players, roomId);
    }

    [Fact]
    public async Task LeaveRoom_DuringGame_AbortsGame()
    {
        var (players, roomId) = await SetupGameInProgress("av_leave_game");

        var aborted = new TaskCompletionSource<string>();
        players[0].Connection.On<string>("GameAborted", reason => aborted.TrySetResult(reason));

        // Player 2 leaves during game
        await players[2].Connection.InvokeAsync("LeaveRoom");

        var result = await Task.WhenAny(aborted.Task, Task.Delay(5000));
        Assert.True(aborted.Task.IsCompletedSuccessfully, "Game should be aborted");
        Assert.Contains("left during the game", aborted.Task.Result);
    }

    [Fact]
    public async Task Disconnect_DuringGame_TriggersGracePeriod()
    {
        var (players, roomId) = await SetupGameInProgress("av_disc");

        var disconnectNotified = new TaskCompletionSource<string>();
        players[0].Connection.On<string>("PlayerDisconnected", name =>
            disconnectNotified.TrySetResult(name));

        // Player 3 disconnects (stop connection = simulate disconnect)
        await players[3].Connection.StopAsync();

        var result = await Task.WhenAny(disconnectNotified.Task, Task.Delay(5000));
        Assert.True(disconnectNotified.Task.IsCompletedSuccessfully,
            "Should notify others of disconnect");
    }

    [Fact]
    public async Task Reconnect_DuringGame_RestoresState()
    {
        var (players, roomId) = await SetupGameInProgress("av_reconn");

        var disconnectNotified = new TaskCompletionSource<string>();
        players[0].Connection.On<string>("PlayerDisconnected", name =>
            disconnectNotified.TrySetResult(name));

        // Player 4 disconnects
        await players[4].Connection.StopAsync();
        await Task.WhenAny(disconnectNotified.Task, Task.Delay(5000));

        // Player 4 reconnects with a new connection using same token
        var newConn = CreateHubConnection(players[4].Token);
        var reconnectedState = new TaskCompletionSource<JsonElement>();
        newConn.On<JsonElement>("GameState", state => reconnectedState.TrySetResult(state));
        await newConn.StartAsync();

        var rejoinResult = await newConn.InvokeAsync<object>("Rejoin", roomId);
        Assert.NotNull(rejoinResult);

        var stateResult = await Task.WhenAny(reconnectedState.Task, Task.Delay(5000));
        Assert.True(reconnectedState.Task.IsCompletedSuccessfully,
            "Reconnected player should receive game state");

        var state = reconnectedState.Task.Result;
        Assert.Equal("TeamProposal", state.GetProperty("phase").GetString());
    }

    [Fact]
    public async Task Reconnect_ViaJoinRoom_DuringGame_Works()
    {
        var (players, roomId) = await SetupGameInProgress("av_rejoin");

        // Player 3 disconnects
        await players[3].Connection.StopAsync();
        await Task.Delay(500);

        // Player 3 reconnects via JoinRoom (the race condition path — old connection still lingers)
        var newConn = CreateHubConnection(players[3].Token);
        var gameStateReceived = new TaskCompletionSource<JsonElement>();
        newConn.On<JsonElement>("GameState", state => gameStateReceived.TrySetResult(state));
        await newConn.StartAsync();

        var joinResult = await newConn.InvokeAsync<object>("JoinRoom", roomId);
        Assert.NotNull(joinResult);

        var stateResult = await Task.WhenAny(gameStateReceived.Task, Task.Delay(5000));
        Assert.True(gameStateReceived.Task.IsCompletedSuccessfully,
            "Rejoined player should receive game state");
    }

    [Fact]
    public async Task SetMaxRejects_HostCanModify()
    {
        var token = await RegisterAndGetToken("av_rejects");
        var conn = CreateHubConnection(token);
        await conn.StartAsync();

        var roomJson = await conn.InvokeAsync<object>("CreateRoom", 5);
        var roomId = JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!, JsonOpts)!["roomId"].ToString()!;

        await conn.InvokeAsync("SetMaxRejects", roomId, 3);
    }

    [Fact]
    public async Task MovePlayer_HostCanReorder()
    {
        var players = new PlayerState[3];
        for (int i = 0; i < 3; i++)
        {
            var token = await RegisterAndGetToken($"av_move_{i}");
            var conn = CreateHubConnection(token);
            players[i] = new PlayerState { Connection = conn };
            AttachHandlers(players[i]);
            await conn.StartAsync();
        }

        var roomJson = await players[0].Connection.InvokeAsync<object>("CreateRoom", 5);
        var roomId = JsonSerializer.Deserialize<Dictionary<string, object>>(
            roomJson.ToString()!, JsonOpts)!["roomId"].ToString()!;

        await players[1].Connection.InvokeAsync<object>("JoinRoom", roomId);
        await players[2].Connection.InvokeAsync<object>("JoinRoom", roomId);

        await players[0].Connection.InvokeAsync("MovePlayer", roomId, 0, 1);
        await players[0].Connection.InvokeAsync("ReorderPlayer", roomId, 0, 2);
    }

    [Fact]
    public async Task EarlyAssassination_DuringGame()
    {
        var (players, roomId) = await SetupGameInProgress("av_early_kill");

        // Find the assassin
        var assassin = players.FirstOrDefault(p => p.MyRole == "Assassin");
        if (assassin == null) return; // skip if no assassin (shouldn't happen with default roles)

        // Assassin triggers early assassination
        await assassin.Connection.InvokeAsync("EarlyAssassinate", roomId);
        await WaitForAllStates(players);

        Assert.Equal("Assassination", players[0].Phase);

        // Assassin picks target (just pick seat 0 or 1, doesn't matter)
        var target = assassin.SeatIndex == 0 ? 1 : 0;
        await assassin.Connection.InvokeAsync("Assassinate", roomId, target);
        await WaitForAllStates(players);

        Assert.Equal("GameOver", players[0].Phase);
    }
}
