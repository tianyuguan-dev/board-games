namespace BoardGames.Models;

public enum GameType
{
    BlackJack,
    Avalon
}

public class GameBalance
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public GameType GameType { get; set; }
    public int Balance { get; set; } = 1000;
}
