using BoardGames.Models;

namespace BoardGames.Services;

public interface IAuthService
{
    Task<bool> UsernameExists(string username);
    // Case-insensitive, trimmed: matches any user's username or nickname (optionally except one user).
    Task<bool> IsNameTaken(string name, int? exceptUserId = null);
    Task<User> CreateUser(string username, string password);
    Task<User?> ValidateUser(string username, string password);
    Task<bool> ChangePassword(int userId, string oldPassword, string newPassword);
}