using BoardGames.Models.BlackJack;

namespace BoardGames.Services.BlackJack;

public interface IBlackJackRoomManager
{
    BlackJackRoom CreateRoom(int maxPlayers);
    BlackJackRoom? GetRoom(string roomId);
    bool IsInAnyRoom(string connectionId);
    void JoinRoom(string roomId, string connectionId);
    BlackJackRoom? FindRoomByConnectionId(string connectionId);
    void RemoveRoom(string roomId);
}