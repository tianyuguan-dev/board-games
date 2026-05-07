using BoardGames.Models;

namespace BoardGames.Data;

public interface IUserRepository
{
                                
    Task<User?> FindByUsername(string username);
    Task<User> Add(User user);   
}