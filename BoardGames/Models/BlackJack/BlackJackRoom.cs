namespace BoardGames.Models.BlackJack;

public class BlackJackRoom
{
    public string RoomId { get; init; }
    public int MaxPlayers { get; init; }
    public Dictionary<string, int> Players { get; set; }
    public BlackJackTable BlackJackTable { get; set; }
    public BlackJackGame? BlackJackGame { get; set; }
    
    public BlackJackRoom(string roomId, int maxPlayers)
    {
        RoomId = roomId;
        MaxPlayers = maxPlayers;
        BlackJackTable = new BlackJackTable(maxPlayers);
        Players = new Dictionary<string, int>();
    }

    public void ReassignSeats()
    {
        Dictionary<string, int> newPlayers = new();
        int seatIndex = 0;
        foreach (var player in Players)
        {
            newPlayers.Add(player.Key, seatIndex);
            seatIndex++;
        }
        Players =  newPlayers;
    }

}