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
    public List<string> PlayerNames { get; set; } = new();
    public int TotalCards { get; set; }
    public int CardsRemaining { get; set; }
    public int ReshuffleThreshold { get; set; }
    public List<int> Bets { get; init; }
    public List<int?>? Winnings { get; init; }

    public BlackJackGameStateDto(BlackJackGame game)
    {
        CurrentIndex = game.CurrentPlayerIndex;
        State = game.State;
        Results = game.Results;
        Bets = game.Bets.ToList();

        if (State == BlackJackGameState.Betting)
        {
            PlayerHands = new();
            DealerHand = new BlackJackHandDto(new List<Card>(), 0);
            return;
        }

        PlayerHands = game.PlayerHands.Select(x => new BlackJackHandDto(x)).ToList();

        if (State == BlackJackGameState.PlayerTurn)
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

        if (State == BlackJackGameState.Finished)
        {
            Winnings = new List<int?>();
            for (int i = 0; i < game.Bets.Count; i++)
            {
                if (game.Bets[i] <= 0)
                {
                    Winnings.Add(null);
                    continue;
                }
                var bet = game.Bets[i];
                Winnings.Add(game.Results[i] switch
                {
                    BlackJackGameResult.PlayerWin => game.PlayerHands[i].IsBlackJack() ? bet * 3 / 2 : bet,
                    BlackJackGameResult.DealerWin => -bet,
                    _ => 0
                });
            }
        }
    }

}
