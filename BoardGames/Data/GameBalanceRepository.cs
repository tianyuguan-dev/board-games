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
            balance = new GameBalance { UserId = userId, GameType = gameType };
            db.GameBalances.Add(balance);
            await db.SaveChangesAsync();
        }
        return balance;
    }

    public async Task UpdateBalance(int userId, GameType gameType, int delta)
    {
        var balance = await GetOrCreate(userId, gameType);
        balance.Balance += delta;
        await db.SaveChangesAsync();
    }
}
