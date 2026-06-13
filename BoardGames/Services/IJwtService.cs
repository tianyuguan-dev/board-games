using System.Security.Claims;
using BoardGames.Models;

namespace BoardGames.Services;

public interface IJwtService
{
    string GenerateJwtToken(User user);
    // Stateless guest token — no User row, identity carried in JWT only.
    // guestId is a random GUID used only to distinguish concurrent guest sessions.
    string GenerateGuestJwtToken(string guestId, string nickname);
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
}