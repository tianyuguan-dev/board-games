using BoardGames.Models.Poker;

namespace BoardGames.Models.BlackJack;

public class BlackJackTable
{
    private Deck _deck;
    private  readonly int _deckCount;
    private readonly int _deckThreshold;

    public int TotalCards => _deckCount * 52;
    public int CardsRemaining => _deck.Remaining;
    public int ReshuffleThreshold => _deckThreshold;

    public BlackJackTable(int deckCount)
    {
        _deckCount = deckCount;
        _deckThreshold = _deckCount*52 / 4;
        _deck = new Deck(_deckCount);
        _deck.Shuffle();
    }

    public BlackJackGame NewRound(int playerCount)
    {
        if (_deck.Remaining <= _deckThreshold)
        {
            _deck = new Deck(_deckCount);
            _deck.Shuffle();
        }
        var blackJackGame = new BlackJackGame(_deck, playerCount);
        blackJackGame.Start();
        return blackJackGame;
    }
}