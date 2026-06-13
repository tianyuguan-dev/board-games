using System.Security.Claims;

namespace BoardGames.Services;

public static class ClaimsPrincipalExtensions
{
    /// <summary>True if the principal is a guest (stateless JWT, no Users row).</summary>
    public static bool IsGuest(this ClaimsPrincipal? user) =>
        user?.HasClaim("isGuest", "true") ?? false;

    /// <summary>Returns the user's database id, or 0 for guests. Callers must skip persistence when 0.</summary>
    public static int GetUserIdOrZero(this ClaimsPrincipal? user)
    {
        var sub = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(sub) || sub.StartsWith("guest:")) return 0;
        return int.Parse(sub);
    }
}
