using BoardGames.Models;
using Microsoft.EntityFrameworkCore;

namespace BoardGames.Data;

public class UserRepository(AppDbContext appDbContext):IUserRepository
{
    public async Task<User?> FindByUsername(string username)
    {
        return await appDbContext.Users.FirstOrDefaultAsync(u => u.Username == username);
    }

    public async Task<User?> FindById(int id)
    {
        return await appDbContext.Users.FindAsync(id);
    }

    public async Task<User> Add(User user)
    {
        appDbContext.Users.Add(user);
        await appDbContext.SaveChangesAsync();
        return user;
    }

    public async Task Update(User user)
    {
        appDbContext.Users.Update(user);
        await appDbContext.SaveChangesAsync();
    }
}