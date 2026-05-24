using BoardGames.Models;

namespace BoardGames.Data;

public interface IGameBalanceRepository
{
    Task<GameBalance> GetOrCreate(int userId, GameType gameType);
    Task UpdateBalance(int userId, GameType gameType, decimal delta);
    Task<List<(string Nickname, decimal Balance)>> GetTopBalances(GameType gameType, int count);
}
