namespace BoardGames.Services.BlackJack;

// Configurable countdown durations for BlackJack turn / betting phases.
// Production defaults are 20s; integration tests override to ~1s so timer-driven paths complete quickly.
public class BlackJackTimerSettings
{
    public int TurnTimeSeconds { get; set; } = 20;
    public int BettingTimeSeconds { get; set; } = 20;
}
