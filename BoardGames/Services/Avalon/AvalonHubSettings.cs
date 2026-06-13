namespace BoardGames.Services.Avalon;

// Configurable reconnect grace period for Avalon disconnects. Default 7200s (2 hours);
// integration tests override to ~1s so timer-driven CheckDisconnectedPlayer paths complete quickly.
public class AvalonHubSettings
{
    public int ReconnectGraceSeconds { get; set; } = 7200;
}
