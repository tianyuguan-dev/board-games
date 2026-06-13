namespace BoardGames.Services.BlackJack;

// In-memory BlackJack balance for guest connections. Resets to 1000 every time a guest
// disconnects (or each new guest session via /auth/guest). No DB write, no leaderboard impact.
public interface IBlackJackGuestSession
{
    decimal GetOrInit(string connectionId);
    decimal ApplyDelta(string connectionId, decimal delta);
    void Reset(string connectionId);
    void Remove(string connectionId);
}
