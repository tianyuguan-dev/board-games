using BoardGames.Models.Poker;

namespace BoardGames.Models.BlackJack;

public class BlackJackTable
{
    private Deck _deck;
    private  readonly int _deckCount;
    private readonly int _deckThreshold;
    private readonly IDeckFactory _deckFactory;

    public int TotalCards => _deckCount * 52;
    public int CardsRemaining => _deck.Remaining;
    public int ReshuffleThreshold => _deckThreshold;

    public BlackJackTable(int deckCount, IDeckFactory? deckFactory = null)
    {
        _deckCount = deckCount;
        _deckThreshold = _deckCount*52 / 4;
        _deckFactory = deckFactory ?? new ShuffledDeckFactory();
        _deck = _deckFactory.Create(_deckCount);
    }

    public BlackJackGame NewRound(int playerCount)
    {
        if (_deck.Remaining <= _deckThreshold)
        {
            _deck = _deckFactory.Create(_deckCount);
        }
        var blackJackGame = new BlackJackGame(_deck, playerCount);
        return blackJackGame;
    }
}