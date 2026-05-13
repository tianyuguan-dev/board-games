using BoardGames.Models.Avalon;

namespace BoardGames.Services.Avalon;

public interface IAvalonRoomManager
{
    AvalonRoom CreateRoom(int maxPlayers);
    AvalonRoom? GetRoom(string roomId);
    void JoinRoom(string roomId, string connectionId);
    (string? roomId, int seatIndex) FindRoomByConnectionId(string connectionId);
    (string? roomId, int seatIndex) FindAndRemoveByConnectionId(string connectionId);
    string? FindRoomByUserId(int userId);
    void RemoveRoom(string roomId);
}
