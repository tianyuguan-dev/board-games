using BoardGames.Models;
using BoardGames.Services;
using Microsoft.EntityFrameworkCore;

namespace BoardGames.Data;

public static class UserNameQueries
{
    // True if any user other than exceptUserId has a username or nickname with the same key as name
    // (trimmed, case-insensitive). Runs in the database (btrim/lower on PostgreSQL).
    public static Task<bool> IsNameTakenAsync(this IQueryable<User> users, string name, int? exceptUserId = null)
    {
        var key = NameRules.Key(name);
        if (exceptUserId is int id) users = users.Where(u => u.Id != id);
        return users.AnyAsync(u => u.Username.Trim().ToLower() == key || u.Nickname.Trim().ToLower() == key);
    }
}
