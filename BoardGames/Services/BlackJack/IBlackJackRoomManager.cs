using BoardGames.Models.BlackJack;

namespace BoardGames.Services.BlackJack;

public interface IBlackJackRoomManager
{
    BlackJackRoom CreateRoom(int maxPlayers);
    BlackJackRoom? GetRoom(string roomId);
    void JoinRoom(string roomId, string connectionId);
    (string? roomId, int seatIndex) FindAndRemoveByConnectionId(string connectionId);
    BlackJackRoom? FindRoomByConnectionId(string connectionId);
    void RemoveRoom(string roomId);
}