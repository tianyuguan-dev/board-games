using BoardGames.Models.Avalon;

namespace BoardGames.Data;

public interface IAvalonGameHistoryRepository
{
    Task PersistGame(AvalonRoom room);

    Task<List<AvalonGameHistory>> GetMyRecentGames(int userId, int limit, int offset);

    // Returns null if game does not exist or current user did not participate (used for 403 vs 404).
    Task<AvalonGameHistory?> GetGameDetail(int gameId, int userId);

    // Admin path: returns game detail without participation check; null only if game does not exist.
    Task<AvalonGameHistory?> GetGameDetailById(int gameId);
}
