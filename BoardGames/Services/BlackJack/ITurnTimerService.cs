namespace BoardGames.Services.BlackJack;

public interface ITurnTimerService
{
    void StartTurnTimer(string roomId);
    void CancelTurnTimer(string roomId);
}
