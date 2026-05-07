using BoardGames.Data;
using BoardGames.Models;

namespace BoardGames.Services;

public class AuthService(IUserRepository userRepository) : IAuthService
{
    public async Task<bool> UsernameExists(string username)
    {
        return await userRepository.FindByUsername(username) != null;
    }

    public async Task<User> CreateUser(string username, string password)
    {
        User user = new User
        {
            Username = username,
            PasswordHash =  BCrypt.Net.BCrypt.HashPassword(password),
        };
        return await userRepository.Add(user);
    }

    public async Task<User?> ValidateUser(string username, string password)
    {
        var user = await userRepository.FindByUsername(username);
        if (user == null || !BCrypt.Net.BCrypt.Verify(password,user.PasswordHash))
            return null;
        else
            return user;
    }
}