namespace BoardGames.Models.Avalon;

public class AvalonProposal
{
    public int LeaderIndex { get; init; }
    public List<int> Team { get; init; } = new();
    public Dictionary<int, bool> Votes { get; } = new(); // playerIndex => approve
    public bool? Approved { get; set; }
    public int SuccessCount { get; set; }
    public int FailCount { get; set; }
    public MissionOutcome MissionResult { get; set; } = MissionOutcome.Pending;

    public bool AllVotesIn(int playerCount) => Votes.Count == playerCount;
}
