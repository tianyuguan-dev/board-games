using BoardGames.Models.Poker;

namespace BoardGames.Models.BlackJack;

public class BlackJackGame
{
    private Deck _deck;
    private List<BlackJackHand> _playerHands=new ();
    private BlackJackHand _dealerBlackJackHand=new ();
    public int CurrentPlayerIndex { get; private set; } = 0;
    private int _playerCount;
    
    public GameState State { get; private set; }
    public List<GameResult?> Results { get; } = new();

    public BlackJackGame(Deck deck,int playerCount = 1)
    {
        if (playerCount < 1)
            throw  new ArgumentOutOfRangeException(nameof(playerCount), playerCount, "playerCount must be greater than 0");
        _deck = deck;
        _playerCount= playerCount;
    }

    public void Start()
    {
        
        for (int i = 0; i < _playerCount; i++)
        {
            var hand = new BlackJackHand(_deck.Deal(),_deck.Deal());
            _playerHands.Add(hand);
            Results.Add(null);
            if (hand.IsBlackJack())
            {
                Results[i] = GameResult.PlayerWin;
            }
        }
        _dealerBlackJackHand = new BlackJackHand(_deck.Deal(), _deck.Deal());
        State = GameState.PlayerTurn;
    }

    public void Hit()
    {
        if (State != GameState.PlayerTurn)
            return;
        var currentPlayerHand = _playerHands[CurrentPlayerIndex];
        currentPlayerHand.AddCard(_deck.Deal());
        if (currentPlayerHand.IsBust())
        {
            Results[CurrentPlayerIndex] = GameResult.DealerWin;
            NextPlayer();
        }
    }
    public void Stand()
    {
        if (State != GameState.PlayerTurn)
            return;
        NextPlayer();
    }

    private void NextPlayer()
    {
        CurrentPlayerIndex++;
        while (CurrentPlayerIndex < _playerCount && Results[CurrentPlayerIndex].HasValue)
        {
            CurrentPlayerIndex++;
        }
        if (CurrentPlayerIndex >= _playerCount)
        {
            DealerPlay();
        }
    }

    private void DealerPlay()
    {
        State = GameState.DealerTurn;
        while (_dealerBlackJackHand.GetValue()<17)
        {
            _dealerBlackJackHand.AddCard(_deck.Deal());
        }

        for (int i = 0; i < _playerCount; i++)
        {
            if (!Results[i].HasValue)
            {
                var dealerValue = _dealerBlackJackHand.GetValue();
                var playerValue = _playerHands[i].GetValue();
                if (_dealerBlackJackHand.IsBust()||dealerValue < playerValue)
                {
                    Results[i] = GameResult.PlayerWin;
                }else if (dealerValue > playerValue)
                {
                    Results[i] = GameResult.DealerWin;
                }else
                {
                    Results[i] = GameResult.Push;
                }
            }
        }
        State = GameState.Finished;
    }
}
public enum GameResult
{
    PlayerWin,
    DealerWin,
    Push
}
public enum GameState
{
    PlayerTurn,
    DealerTurn,
    Finished
}