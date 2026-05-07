using BoardGames.Models;

namespace BoardGames.Services;

public interface IAuthService
{
    Task<bool> UsernameExists(string username);
    Task<User> CreateUser(string username, string password);
    Task<User?> ValidateUser(string username, string password);
}