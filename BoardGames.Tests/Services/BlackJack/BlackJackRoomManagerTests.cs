using BoardGames.Services.BlackJack;

namespace BoardGames.Tests.Services.BlackJack;

public class BlackJackRoomManagerTests
{
    private readonly BlackJackRoomManager _roomManager = new();

    [Fact]
    public void CreateRoom_ReturnsRoomWithCorrectMaxPlayers()
    {
        var room = _roomManager.CreateRoom(4);

        Assert.Equal(4, room.MaxPlayers);
    }

    [Fact]
    public void CreateRoom_ReturnsRoomWithEmptyPlayers()
    {
        var room = _roomManager.CreateRoom(4);

        Assert.Empty(room.Players);
    }

    [Fact]
    public void CreateRoom_RoomCanBeFoundByGetRoom()
    {
        var room = _roomManager.CreateRoom(4);

        var found = _roomManager.GetRoom(room.RoomId);

        Assert.Same(room, found);
    }

    [Fact]
    public void GetRoom_ReturnsNullForNonExistentRoom()
    {
        var result = _roomManager.GetRoom("99999");

        Assert.Null(result);
    }

    [Fact]
    public void JoinRoom_AddsPlayerToRoom()
    {
        var room = _roomManager.CreateRoom(4);

        _roomManager.JoinRoom(room.RoomId, "conn-1");

        Assert.Single(room.Players);
        Assert.Equal(0, room.Players["conn-1"]);
    }

    [Fact]
    public void JoinRoom_AssignsIncrementingSeatNumbers()
    {
        var room = _roomManager.CreateRoom(4);

        _roomManager.JoinRoom(room.RoomId, "conn-1");
        _roomManager.JoinRoom(room.RoomId, "conn-2");
        _roomManager.JoinRoom(room.RoomId, "conn-3");

        Assert.Equal(0, room.Players["conn-1"]);
        Assert.Equal(1, room.Players["conn-2"]);
        Assert.Equal(2, room.Players["conn-3"]);
    }

    [Fact]
    public void JoinRoom_ThrowsWhenRoomDoesNotExist()
    {
        Assert.Throws<InvalidOperationException>(
            () => _roomManager.JoinRoom("99999", "conn-1"));
    }

    [Fact]
    public void JoinRoom_ThrowsWhenRoomIsFull()
    {
        var room = _roomManager.CreateRoom(2);
        _roomManager.JoinRoom(room.RoomId, "conn-1");
        _roomManager.JoinRoom(room.RoomId, "conn-2");

        Assert.Throws<InvalidOperationException>(
            () => _roomManager.JoinRoom(room.RoomId, "conn-3"));
    }

    [Fact]
    public void FindAndRemoveByConnectionId_RemovesPlayerAndReturnsRoomId()
    {
        var room = _roomManager.CreateRoom(4);
        _roomManager.JoinRoom(room.RoomId, "conn-1");

        var (roomId, seatIndex) = _roomManager.FindAndRemoveByConnectionId("conn-1");

        Assert.Equal(room.RoomId, roomId);
        Assert.Equal(0, seatIndex);
        Assert.Empty(room.Players);
    }

    [Fact]
    public void FindAndRemoveByConnectionId_ReturnsNullWhenPlayerNotFound()
    {
        var (roomId, seatIndex) = _roomManager.FindAndRemoveByConnectionId("conn-999");

        Assert.Null(roomId);
        Assert.Equal(-1, seatIndex);
    }

    [Fact]
    public void FindAndRemoveByConnectionId_DoesNotAffectOtherPlayers()
    {
        var room = _roomManager.CreateRoom(4);
        _roomManager.JoinRoom(room.RoomId, "conn-1");
        _roomManager.JoinRoom(room.RoomId, "conn-2");

        _roomManager.FindAndRemoveByConnectionId("conn-1");

        Assert.Single(room.Players);
        Assert.True(room.Players.ContainsKey("conn-2"));
    }
}
