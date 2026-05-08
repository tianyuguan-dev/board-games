using BoardGames.Models.Poker;

namespace BoardGames.Models.BlackJack;

public class BlackJackGame
{
    private Deck _deck;
    private List<Hand> _playerHands=new ();
    private Hand _dealerHand=new ();
    private int _currentPlayerIndex=0;
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
            var hand = new Hand(_deck.Deal(),_deck.Deal());
            _playerHands.Add(hand);
            Results.Add(null);
            if (hand.IsBlackJack())
            {
                Results[i] = GameResult.PlayerWin;
            }
        }
        _dealerHand = new Hand(_deck.Deal(), _deck.Deal());
        State = GameState.PlayerTurn;
    }

    public void Hit()
    {
        if (State != GameState.PlayerTurn)
            return;
        var currentPlayerHand = _playerHands[_currentPlayerIndex];
        currentPlayerHand.AddCard(_deck.Deal());
        if (currentPlayerHand.IsBust())
        {
            Results[_currentPlayerIndex] = GameResult.DealerWin;
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
        _currentPlayerIndex++;
        while (_currentPlayerIndex < _playerCount && Results[_currentPlayerIndex].HasValue)
        {
            _currentPlayerIndex++;
        }
        if (_currentPlayerIndex >= _playerCount)
        {
            DealerPlay();
        }
    }

    private void DealerPlay()
    {
        State = GameState.DealerTurn;
        while (_dealerHand.GetValue()<17)
        {
            _dealerHand.AddCard(_deck.Deal());
        }

        for (int i = 0; i < _playerCount; i++)
        {
            if (!Results[i].HasValue)
            {
                var dealerValue = _dealerHand.GetValue();
                var playerValue = _playerHands[i].GetValue();
                if (_dealerHand.IsBust()||dealerValue < playerValue)
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