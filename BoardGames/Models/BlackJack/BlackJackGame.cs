using BoardGames.Models.Poker;

namespace BoardGames.Models.BlackJack;

public class BlackJackGame
{
    public const int MinBet = 1;
    public const int MaxBet = 100;
    private Deck _deck;
    private List<BlackJackHand> _playerHands=new ();
    private BlackJackHand _dealerBlackJackHand=new ();
    public IReadOnlyList<BlackJackHand> PlayerHands => _playerHands;
    public BlackJackHand DealerBlackJackHand => _dealerBlackJackHand;
    public int CurrentPlayerIndex { get; private set; } = 0;
    private int _playerCount;
    private HashSet<int> _forfeitedPlayers=new();
    public BlackJackGameState State { get; private set; }
    public List<BlackJackGameResult?> Results { get; } = new();
    private List<int> _bets;
    public IReadOnlyList<int> Bets => _bets;

    public BlackJackGame(Deck deck,int playerCount = 1)
    {
        if (playerCount < 1)
            throw  new ArgumentOutOfRangeException(nameof(playerCount), playerCount, "playerCount must be greater than 0");
        _deck = deck;
        _playerCount= playerCount;
        _bets = Enumerable.Repeat(0, playerCount).ToList();
        State = BlackJackGameState.Betting;
    }

    public void PlaceBet(int playerIndex, int amount)
    {
        if (State != BlackJackGameState.Betting) return;
        if (playerIndex < 0 || playerIndex >= _playerCount) return;
        if (amount < MinBet || amount > MaxBet) return;
        _bets[playerIndex] = amount;
    }

    public bool AllBetsPlaced()
    {
        return Enumerable.Range(0, _playerCount)
            .All(i => _bets[i] > 0 || _forfeitedPlayers.Contains(i));
    }

    public void AutoBetRemaining(int amount)
    {
        if (State != BlackJackGameState.Betting) return;
        for (int i = 0; i < _playerCount; i++)
        {
            if (_bets[i] == 0 && !_forfeitedPlayers.Contains(i))
                _bets[i] = amount;
        }
    }

    public void Start()
    {
        if (State != BlackJackGameState.Betting) return;
        for (int i = 0; i < _playerCount; i++)
        {
            var hand = new BlackJackHand(_deck.Deal(),_deck.Deal());
            _playerHands.Add(hand);
            Results.Add(null);
            if (hand.IsBlackJack())
            {
                Results[i] = BlackJackGameResult.PlayerWin;
            }
        }
        _dealerBlackJackHand = new BlackJackHand(_deck.Deal(), _deck.Deal());
        if (_dealerBlackJackHand.IsBlackJack())
        {
            for (int i = 0; i < _playerCount; i++)
            {
                if (!Results[i].HasValue)
                    Results[i] = BlackJackGameResult.DealerWin;
                else if (Results[i] == BlackJackGameResult.PlayerWin)
                    Results[i] = BlackJackGameResult.Push;
            }
            State = BlackJackGameState.Finished;
            return;
        }
        State = BlackJackGameState.PlayerTurn;
        AdvancePastResolvedPlayers();
    }

    public void Hit()
    {
        if (State != BlackJackGameState.PlayerTurn)
            return;
        var currentPlayerHand = _playerHands[CurrentPlayerIndex];
        currentPlayerHand.AddCard(_deck.Deal());
        if (currentPlayerHand.IsBust())
        {
            Results[CurrentPlayerIndex] = BlackJackGameResult.DealerWin;
            NextPlayer();
        }
        else if (currentPlayerHand.GetValue() == 21)
        {
            NextPlayer();
        }
    }
    public void Stand()
    {
        if (State != BlackJackGameState.PlayerTurn)
            return;
        NextPlayer();
    }

    public bool CanDoubleDown()
    {
        if (State != BlackJackGameState.PlayerTurn) return false;
        return _playerHands[CurrentPlayerIndex].Cards.Count == 2;
    }

    public void DoubleDown()
    {
        if (!CanDoubleDown()) return;
        _bets[CurrentPlayerIndex] *= 2;
        var hand = _playerHands[CurrentPlayerIndex];
        hand.AddCard(_deck.Deal());
        if (hand.IsBust())
            Results[CurrentPlayerIndex] = BlackJackGameResult.DealerWin;
        NextPlayer();
    }

    private void NextPlayer()
    {
        CurrentPlayerIndex++;
        AdvancePastResolvedPlayers();
    }

    private void AdvancePastResolvedPlayers()
    {
        while (CurrentPlayerIndex < _playerCount && (Results[CurrentPlayerIndex].HasValue||_forfeitedPlayers.Contains(CurrentPlayerIndex)))
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
        State = BlackJackGameState.DealerTurn;
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
                    Results[i] = BlackJackGameResult.PlayerWin;
                }else if (dealerValue > playerValue)
                {
                    Results[i] = BlackJackGameResult.DealerWin;
                }else
                {
                    Results[i] = BlackJackGameResult.Push;
                }
            }
        }
        State = BlackJackGameState.Finished;
    }

    public void ForfeitPlayer(int playerIndex)
    {
        _forfeitedPlayers.Add(playerIndex);
        if (CurrentPlayerIndex == playerIndex && State == BlackJackGameState.PlayerTurn) NextPlayer();
    }
}
public enum BlackJackGameResult
{
    PlayerWin,
    DealerWin,
    Push
}
public enum BlackJackGameState
{
    PlayerTurn,
    DealerTurn,
    Finished,
    Betting
}