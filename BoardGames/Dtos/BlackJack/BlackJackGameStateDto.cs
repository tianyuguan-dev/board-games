using BoardGames.Models.BlackJack;
using BoardGames.Models.Poker;

namespace BoardGames.Dtos.BlackJack;

public class BlackJackGameStateDto
{
    public List<BlackJackHandDto> PlayerHands { get; init; }
    public BlackJackHandDto DealerHand { get; init; }
    public int CurrentIndex { get; init; }
    public BlackJackGameState  State { get; init; }
    public List<BlackJackGameResult?> Results { get; init; }

    public BlackJackGameStateDto(BlackJackGame game)
    {
        PlayerHands = game.PlayerHands.Select(x => new BlackJackHandDto(x)).ToList();
        CurrentIndex = game.CurrentPlayerIndex;
        State = game.State;
        
        if (State ==BlackJackGameState.PlayerTurn)
        {
            var card = game.DealerBlackJackHand.Cards[0];
            IReadOnlyList<Card> dealerCards = [card];
            int dealerVaule = (int)card.Rank;
            if (dealerVaule > 10)
                dealerVaule = 10;
            else if (dealerVaule == 1) dealerVaule = 11;
            DealerHand = new BlackJackHandDto(dealerCards, dealerVaule);
        }
        else
        {
            DealerHand = new BlackJackHandDto(game.DealerBlackJackHand);
        }
        Results = game.Results;
    }
    
}