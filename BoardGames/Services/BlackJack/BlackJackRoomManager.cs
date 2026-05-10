using BoardGames.Models.BlackJack;

namespace BoardGames.Services.BlackJack;

public class BlackJackRoomManager: IBlackJackRoomManager
{
    private Dictionary<string, BlackJackRoom> _rooms = new();
    private readonly Random _random = new();
    
    public BlackJackRoom CreateRoom(int maxPlayers)
    {
        string roomId = _random.Next(10000, 100000).ToString();
        var roomsIds = _rooms.Keys;
        while (roomsIds.Contains(roomId))
        {
            roomId = _random.Next(10000, 100000).ToString();
        }

        var blackJackRoom = new BlackJackRoom(roomId, maxPlayers);
        _rooms.Add(roomId, blackJackRoom);
        return blackJackRoom;
    }

    public BlackJackRoom? GetRoom(string roomId)
    {
        _rooms.TryGetValue(roomId, out var room);
        return room;
    }

    public void JoinRoom(string roomId, string connectionId)
    {
        var blackJackRoom = GetRoom(roomId);
        if (blackJackRoom == null)
            throw new InvalidOperationException($"Cannot join room {roomId} because it doesn't exist");
        if (blackJackRoom.BlackJackGame!=null&&blackJackRoom.BlackJackGame.State!=BlackJackGameState.Finished)
        {
            throw new InvalidOperationException(
                $"Cannot join room {roomId} because the game is in progress");
        }

        if (blackJackRoom.Players.ContainsKey(connectionId))
            throw new InvalidOperationException($"Same player cannot join room twice");
        var maxPlayers = blackJackRoom.MaxPlayers;
        var players = blackJackRoom.Players;
        if (players.Count>= maxPlayers)
        {
            throw new InvalidOperationException(
                $"Cannot join room {roomId} because the maximum number of players has been reached");
        }
        players.Add(connectionId, players.Count);
    }

    public (string? roomId, int seatIndex) FindAndRemoveByConnectionId(string connectionId)
    {
        string roomId=null;
        int seatIndex=-1;
        _rooms.Values.ToList().ForEach(room =>
        {
            var connectionIds = room.Players.Keys;
            if (connectionIds.Contains(connectionId))
            {
                seatIndex = room.Players[connectionId];
                room.Players.Remove(connectionId);
                roomId = room.RoomId;
            }
        });
        return (roomId,seatIndex);
    }

    public void RemoveRoom(string roomId)
    {
        _rooms.Remove(roomId);
    }
}