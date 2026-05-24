using System.Security.Claims;
using BoardGames.Data;
using BoardGames.Hubs.BlackJack;
using BoardGames.Models;
using BoardGames.Models.BlackJack;
using BoardGames.Models.Poker;
using BoardGames.Services.BlackJack;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace BoardGames.Tests.Hubs.BlackJack;

public class BlackJackHubTests
{
    private readonly Mock<IBlackJackRoomManager> _mockRoomManager;
    private readonly Mock<IGroupManager> _mockGroups;
    private readonly Mock<IHubCallerClients> _mockClients;
    private readonly Mock<ISingleClientProxy> _mockClientProxy;
    private readonly BlackJackHub _hub;
    private const string ConnectionId = "test-conn-1";

    public BlackJackHubTests()
    {
        _mockRoomManager = new Mock<IBlackJackRoomManager>();
        _mockGroups = new Mock<IGroupManager>();
        _mockClients = new Mock<IHubCallerClients>();
        _mockClientProxy = new Mock<ISingleClientProxy>();

        var mockContext = new Mock<HubCallerContext>();
        mockContext.Setup(c => c.ConnectionId).Returns(ConnectionId);
        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(ClaimTypes.Name, "testuser"),
        }, "test"));
        mockContext.Setup(c => c.User).Returns(claims);

        _mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_mockClientProxy.Object);
        _mockClients.Setup(c => c.Client(It.IsAny<string>())).Returns(_mockClientProxy.Object);

        var mockUserRepo = new Mock<IUserRepository>();
        mockUserRepo.Setup(r => r.FindById(1)).ReturnsAsync(new User { Id = 1, Username = "testuser", Nickname = "TestPlayer" });

        var mockTurnTimer = new Mock<ITurnTimerService>();
        var mockBalanceRepo = new Mock<IGameBalanceRepository>();
        mockBalanceRepo.Setup(r => r.GetOrCreate(It.IsAny<int>(), It.IsAny<GameType>()))
            .ReturnsAsync(new GameBalance { Balance = 1000 });
        _hub = new BlackJackHub(_mockRoomManager.Object, mockUserRepo.Object, mockTurnTimer.Object, mockBalanceRepo.Object);
        _hub.Context = mockContext.Object;
        _hub.Groups = _mockGroups.Object;
        _hub.Clients = _mockClients.Object;
    }

    private static void SetupStartedGame(BlackJackRoom room, int playerCount)
    {
        room.BlackJackGame = new BlackJackGame(new Deck(), playerCount);
        for (int i = 0; i < playerCount; i++)
            room.BlackJackGame.PlaceBet(i, 10);
        room.BlackJackGame.Start();
    }

    // CreateRoom tests

    [Fact]
    public async Task CreateRoom_ReturnsCreatedRoom()
    {
        var room = new BlackJackRoom("12345", 4);
        _mockRoomManager.Setup(r => r.CreateRoom(4)).Returns(room);

        var result = await _hub.CreateRoom(4);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("12345", doc.RootElement.GetProperty("RoomId").GetString());
        Assert.Equal(4, doc.RootElement.GetProperty("MaxPlayers").GetInt32());
    }

    [Fact]
    public async Task CreateRoom_AddsCreatorToGroup()
    {
        var room = new BlackJackRoom("12345", 4);
        _mockRoomManager.Setup(r => r.CreateRoom(4)).Returns(room);

        await _hub.CreateRoom(4);

        _mockGroups.Verify(g => g.AddToGroupAsync(ConnectionId, "12345", default), Times.Once);
    }

    [Fact]
    public async Task CreateRoom_JoinsCreatorToRoom()
    {
        var room = new BlackJackRoom("12345", 4);
        _mockRoomManager.Setup(r => r.CreateRoom(4)).Returns(room);

        await _hub.CreateRoom(4);

        _mockRoomManager.Verify(r => r.JoinRoom("12345", ConnectionId), Times.Once);
    }

    [Fact]
    public async Task CreateRoom_ThrowsWhenMaxPlayersIsZero()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _hub.CreateRoom(0));
    }

    // JoinRoom tests

    [Fact]
    public async Task JoinRoom_AddsPlayerToGroupAndNotifies()
    {
        var room = new BlackJackRoom("12345", 4);
        room.Players.Add("other-conn", 0);
        _mockRoomManager.Setup(r => r.GetRoom("12345")).Returns(room);

        var result = await _hub.JoinRoom("12345");

        Assert.Equal(4, result.MaxPlayers);
        Assert.Equal(room.Players.Count, result.PlayerCount);
        _mockRoomManager.Verify(r => r.JoinRoom("12345", ConnectionId), Times.Once);
        _mockGroups.Verify(g => g.AddToGroupAsync(ConnectionId, "12345", default), Times.Once);
        _mockClientProxy.Verify(c => c.SendCoreAsync("PlayerJoined",
            It.IsAny<object[]>(), default), Times.Once);
    }

    // StartGame tests

    [Fact]
    public async Task StartGame_ThrowsWhenRoomNotFound()
    {
        _mockRoomManager.Setup(r => r.GetRoom("99999")).Returns((BlackJackRoom?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _hub.StartGame("99999"));
    }

    [Fact]
    public async Task StartGame_CreatesGameAndNotifiesGroup()
    {
        var room = new BlackJackRoom("12345", 4);
        room.HostConnectionId = ConnectionId;
        room.Players.Add(ConnectionId, 0);
        room.Players.Add("conn-2", 1);
        room.ReadyPlayers.Add("conn-2");
        room.PlayerUserIds[ConnectionId] = 1;
        room.PlayerUserIds["conn-2"] = 2;
        _mockRoomManager.Setup(r => r.GetRoom("12345")).Returns(room);

        await _hub.StartGame("12345");

        Assert.NotNull(room.BlackJackGame);
        Assert.Equal(BlackJackGameState.Betting, room.BlackJackGame.State);
        _mockClientProxy.Verify(c => c.SendCoreAsync("YourSeat",
            It.IsAny<object[]>(), default), Times.Exactly(room.Players.Count));
        _mockClientProxy.Verify(c => c.SendCoreAsync("StartGame",
            It.IsAny<object[]>(), default), Times.Once);
    }

    // Hit tests

    [Fact]
    public async Task Hit_ThrowsWhenRoomNotFound()
    {
        _mockRoomManager.Setup(r => r.GetRoom("99999")).Returns((BlackJackRoom?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _hub.BlackJackPlayerHit("99999"));
    }

    [Fact]
    public async Task Hit_ThrowsWhenGameNotStarted()
    {
        var room = new BlackJackRoom("12345", 4);
        _mockRoomManager.Setup(r => r.GetRoom("12345")).Returns(room);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _hub.BlackJackPlayerHit("12345"));
    }

    [Fact]
    public async Task Hit_ThrowsWhenPlayerNotInRoom()
    {
        var room = new BlackJackRoom("12345", 4);
        room.Players.Add("other-conn", 0);
        SetupStartedGame(room, 1);
        _mockRoomManager.Setup(r => r.GetRoom("12345")).Returns(room);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _hub.BlackJackPlayerHit("12345"));
    }

    [Fact]
    public async Task Hit_ThrowsWhenNotPlayersTurn()
    {
        var room = new BlackJackRoom("12345", 4);
        room.Players.Add("other-conn", 0);
        room.Players.Add(ConnectionId, 1);
        SetupStartedGame(room, 2);
        _mockRoomManager.Setup(r => r.GetRoom("12345")).Returns(room);

        // CurrentPlayerIndex is 0, but our ConnectionId maps to seat 1
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _hub.BlackJackPlayerHit("12345"));
    }

    [Fact]
    public async Task Hit_SucceedsAndNotifiesGroup()
    {
        var room = new BlackJackRoom("12345", 4);
        room.Players.Add(ConnectionId, 0);
        SetupStartedGame(room, 1);
        _mockRoomManager.Setup(r => r.GetRoom("12345")).Returns(room);

        await _hub.BlackJackPlayerHit("12345");

        _mockClientProxy.Verify(c => c.SendCoreAsync("PlayerHit",
            It.IsAny<object[]>(), default), Times.Once);
    }

    // Stand tests

    [Fact]
    public async Task Stand_ThrowsWhenRoomNotFound()
    {
        _mockRoomManager.Setup(r => r.GetRoom("99999")).Returns((BlackJackRoom?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _hub.BlackJackPlayerStand("99999"));
    }

    [Fact]
    public async Task Stand_ThrowsWhenGameNotStarted()
    {
        var room = new BlackJackRoom("12345", 4);
        _mockRoomManager.Setup(r => r.GetRoom("12345")).Returns(room);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _hub.BlackJackPlayerStand("12345"));
    }

    [Fact]
    public async Task Stand_ThrowsWhenNotPlayersTurn()
    {
        var room = new BlackJackRoom("12345", 4);
        room.Players.Add("other-conn", 0);
        room.Players.Add(ConnectionId, 1);
        SetupStartedGame(room, 2);
        _mockRoomManager.Setup(r => r.GetRoom("12345")).Returns(room);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _hub.BlackJackPlayerStand("12345"));
    }

    [Fact]
    public async Task Stand_ThrowsWhenPlayerNotInRoom()
    {
        var room = new BlackJackRoom("12345", 4);
        room.Players.Add("other-conn", 0);
        SetupStartedGame(room, 1);
        _mockRoomManager.Setup(r => r.GetRoom("12345")).Returns(room);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _hub.BlackJackPlayerStand("12345"));
    }

    [Fact]
    public async Task Stand_SucceedsAndNotifiesGroup()
    {
        var room = new BlackJackRoom("12345", 4);
        room.Players.Add(ConnectionId, 0);
        SetupStartedGame(room, 1);
        _mockRoomManager.Setup(r => r.GetRoom("12345")).Returns(room);

        await _hub.BlackJackPlayerStand("12345");

        _mockClientProxy.Verify(c => c.SendCoreAsync("PlayerStand",
            It.IsAny<object[]>(), default), Times.Once);
    }

    // OnDisconnectedAsync tests

    [Fact]
    public async Task OnDisconnectedAsync_NotifiesGroupWhenPlayerInRoom()
    {
        _mockRoomManager.Setup(r => r.FindAndRemoveByConnectionId(ConnectionId)).Returns(("12345", 0));
        _mockRoomManager.Setup(r => r.GetRoom("12345")).Returns(new BlackJackRoom("12345", 4));

        await _hub.OnDisconnectedAsync(null);

        _mockClientProxy.Verify(c => c.SendCoreAsync("PlayerLeft",
            It.IsAny<object[]>(), default), Times.Once);
    }

    [Fact]
    public async Task OnDisconnectedAsync_DoesNotNotifyWhenPlayerNotInAnyRoom()
    {
        _mockRoomManager.Setup(r => r.FindAndRemoveByConnectionId(ConnectionId)).Returns(((string?)null, -1));

        await _hub.OnDisconnectedAsync(null);

        _mockClientProxy.Verify(c => c.SendCoreAsync(It.IsAny<string>(),
            It.IsAny<object[]>(), default), Times.Never);
    }

    [Fact]
    public async Task OnDisconnectedAsync_RemovesRoomWhenEmpty()
    {
        var room = new BlackJackRoom("12345", 4);
        _mockRoomManager.Setup(r => r.FindAndRemoveByConnectionId(ConnectionId)).Returns(("12345", 0));
        _mockRoomManager.Setup(r => r.GetRoom("12345")).Returns(room);

        await _hub.OnDisconnectedAsync(null);

        _mockRoomManager.Verify(r => r.RemoveRoom("12345"), Times.Once);
    }

    // StartGame edge cases

    [Fact]
    public async Task StartGame_ThrowsWhenGameInProgress()
    {
        var room = new BlackJackRoom("12345", 4);
        room.HostConnectionId = ConnectionId;
        room.Players.Add(ConnectionId, 0);
        SetupStartedGame(room, 1);
        _mockRoomManager.Setup(r => r.GetRoom("12345")).Returns(room);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _hub.StartGame("12345"));
    }

    [Fact]
    public async Task StartGame_ThrowsWhenNotAllPlayersReady()
    {
        var room = new BlackJackRoom("12345", 4);
        room.HostConnectionId = ConnectionId;
        room.Players.Add(ConnectionId, 0);
        room.Players.Add("conn-2", 1);
        _mockRoomManager.Setup(r => r.GetRoom("12345")).Returns(room);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _hub.StartGame("12345"));
    }

    [Fact]
    public async Task StartGame_ThrowsWhenNotHost()
    {
        var room = new BlackJackRoom("12345", 4);
        room.HostConnectionId = "other-conn";
        room.Players.Add(ConnectionId, 0);
        _mockRoomManager.Setup(r => r.GetRoom("12345")).Returns(room);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _hub.StartGame("12345"));
    }

    [Fact]
    public async Task StartGame_ClearsReadyPlayersAfterStart()
    {
        var room = new BlackJackRoom("12345", 4);
        room.HostConnectionId = ConnectionId;
        room.Players.Add(ConnectionId, 0);
        room.Players.Add("conn-2", 1);
        room.ReadyPlayers.Add("conn-2");
        room.PlayerUserIds[ConnectionId] = 1;
        room.PlayerUserIds["conn-2"] = 2;
        _mockRoomManager.Setup(r => r.GetRoom("12345")).Returns(room);

        await _hub.StartGame("12345");

        Assert.Empty(room.ReadyPlayers);
    }

    // Ready tests

    [Fact]
    public async Task Ready_AddsPlayerToReadySet()
    {
        var room = new BlackJackRoom("12345", 4);
        room.HostConnectionId = "conn-2";
        room.Players.Add(ConnectionId, 0);
        room.Players.Add("conn-2", 1);
        _mockRoomManager.Setup(r => r.GetRoom("12345")).Returns(room);

        await _hub.Ready("12345");

        Assert.Contains(ConnectionId, room.ReadyPlayers);
    }

    [Fact]
    public async Task Ready_ThrowsWhenRoomNotFound()
    {
        _mockRoomManager.Setup(r => r.GetRoom("99999")).Returns((BlackJackRoom?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _hub.Ready("99999"));
    }

    [Fact]
    public async Task Ready_ThrowsWhenPlayerNotInRoom()
    {
        var room = new BlackJackRoom("12345", 4);
        _mockRoomManager.Setup(r => r.GetRoom("12345")).Returns(room);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _hub.Ready("12345"));
    }

    [Fact]
    public async Task Ready_NotifiesGroup()
    {
        var room = new BlackJackRoom("12345", 4);
        room.HostConnectionId = "conn-2";
        room.Players.Add(ConnectionId, 0);
        room.Players.Add("conn-2", 1);
        _mockRoomManager.Setup(r => r.GetRoom("12345")).Returns(room);

        await _hub.Ready("12345");

        _mockClientProxy.Verify(c => c.SendCoreAsync("RoomUpdate",
            It.IsAny<object[]>(), default), Times.AtLeastOnce);
    }

    // Unready tests

    [Fact]
    public async Task Unready_RemovesPlayerFromReadySet()
    {
        var room = new BlackJackRoom("12345", 4);
        room.Players.Add(ConnectionId, 0);
        room.ReadyPlayers.Add(ConnectionId);
        _mockRoomManager.Setup(r => r.GetRoom("12345")).Returns(room);

        await _hub.Unready("12345");

        Assert.DoesNotContain(ConnectionId, room.ReadyPlayers);
    }

    [Fact]
    public async Task Unready_ThrowsWhenPlayerNotReady()
    {
        var room = new BlackJackRoom("12345", 4);
        room.Players.Add(ConnectionId, 0);
        _mockRoomManager.Setup(r => r.GetRoom("12345")).Returns(room);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _hub.Unready("12345"));
    }

    // LeaveRoom tests

    [Fact]
    public async Task LeaveRoom_RemovesFromGroupAndNotifies()
    {
        _mockRoomManager.Setup(r => r.FindAndRemoveByConnectionId(ConnectionId)).Returns(("12345", 0));
        _mockRoomManager.Setup(r => r.GetRoom("12345")).Returns(new BlackJackRoom("12345", 4));

        await _hub.LeaveRoom();

        _mockGroups.Verify(g => g.RemoveFromGroupAsync(ConnectionId, "12345", default), Times.Once);
        _mockClientProxy.Verify(c => c.SendCoreAsync("PlayerLeft",
            It.IsAny<object[]>(), default), Times.Once);
    }

    [Fact]
    public async Task LeaveRoom_RemovesRoomWhenEmpty()
    {
        var room = new BlackJackRoom("12345", 4);
        _mockRoomManager.Setup(r => r.FindAndRemoveByConnectionId(ConnectionId)).Returns(("12345", 0));
        _mockRoomManager.Setup(r => r.GetRoom("12345")).Returns(room);

        await _hub.LeaveRoom();

        _mockRoomManager.Verify(r => r.RemoveRoom("12345"), Times.Once);
    }
}
