using BoardGames.Models;
using Microsoft.EntityFrameworkCore;

namespace BoardGames.Data;

public class GameBalanceRepository(AppDbContext db) : IGameBalanceRepository
{
    public async Task<GameBalance> GetOrCreate(int userId, GameType gameType)
    {
        var balance = await db.GameBalances
            .FirstOrDefaultAsync(b => b.UserId == userId && b.GameType == gameType);
        if (balance == null)
        {
            balance = new GameBalance
            {
                UserId = userId,
                GameType = gameType,
                Balance = gameType == GameType.Avalon ? 0 : 1000
            };
            db.GameBalances.Add(balance);
            await db.SaveChangesAsync();
        }
        return balance;
    }

    public async Task UpdateBalance(int userId, GameType gameType, decimal delta)
    {
        var balance = await GetOrCreate(userId, gameType);
        balance.Balance += delta;
        await db.SaveChangesAsync();
    }

    public async Task<List<(string Nickname, decimal Balance)>> GetTopBalances(GameType gameType, int count)
    {
        var results = await db.GameBalances
            .Where(b => b.GameType == gameType)
            .OrderByDescending(b => b.Balance)
            .Take(count)
            .Join(db.Users, b => b.UserId, u => u.Id, (b, u) => new { u.Nickname, b.Balance })
            .ToListAsync();
        return results.Select(x => (x.Nickname, x.Balance)).ToList();
    }
}
