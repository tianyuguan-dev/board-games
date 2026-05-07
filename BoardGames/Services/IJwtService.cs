using BoardGames.Models;

namespace BoardGames.Services;

public interface IJwtService
{
    string GenerateJwtToken(User user);
}