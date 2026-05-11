using BoardGames.Models;

namespace BoardGames.Data;

public interface IUserRepository
{
                                
    Task<User?> FindByUsername(string username);
    Task<User?> FindById(int id);
    Task<User> Add(User user);
    Task Update(User user);
}