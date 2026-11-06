namespace BoardGames.Services;

// Usernames and nicknames are compared by key: leading/trailing whitespace ignored, case ignored.
// See docs/specs/unique-names-and-admin-search.md.
public static class NameRules
{
    public const int MaxNicknameLength = 50; // matches the Users.Nickname column (AppDbContext)

    public static string Key(string name) => name.Trim().ToLowerInvariant();

    // Null when the nickname is acceptable (callers store nickname.Trim()); otherwise the error to return.
    public static string? NicknameError(string? nickname)
    {
        var trimmed = nickname?.Trim() ?? "";
        if (trimmed.Length == 0) return "Nickname cannot be empty";
        if (trimmed.Length > MaxNicknameLength) return $"Nickname is too long (max {MaxNicknameLength})";
        return null;
    }
}
