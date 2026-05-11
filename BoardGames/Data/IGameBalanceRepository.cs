using BoardGames.Models;

namespace BoardGames.Data;

public interface IGameBalanceRepository
{
    Task<GameBalance> GetOrCreate(int userId, GameType gameType);
    Task UpdateBalance(int userId, GameType gameType, int delta);
    Task<List<(string Nickname, int Balance)>> GetTopBalances(GameType gameType, int count);
}
