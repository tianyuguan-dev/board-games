namespace BoardGames.Models.Avalon;

public class AvalonGameHistory
{
    public int Id { get; set; }
    public string RoomId { get; set; } = string.Empty;
    public int PlayerCount { get; set; }
    public int MaxRejects { get; set; }
    public GameWinner Winner { get; set; }
    public string? WinReason { get; set; }
    public bool BonusAssassination { get; set; }
    public bool EarlyAssassination { get; set; }
    public int? AssassinTargetSeat { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime EndedAt { get; set; }
    public bool IsRanked { get; set; } = true;

    public List<AvalonGamePlayer> Players { get; set; } = new();
    public List<AvalonGameProposal> Proposals { get; set; } = new();
}

public class AvalonGamePlayer
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public AvalonGameHistory Game { get; set; } = null!;

    public int UserId { get; set; }
    public int SeatIndex { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public AvalonRole Role { get; set; }
    public bool IsWinner { get; set; }
    public decimal BalanceDelta { get; set; }
}

public class AvalonGameProposal
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public AvalonGameHistory Game { get; set; } = null!;

    public int MissionIndex { get; set; }     // 0-4
    public int ProposalIndex { get; set; }    // 0-N within mission
    public int LeaderSeatIndex { get; set; }
    public int[] TeamSeats { get; set; } = Array.Empty<int>();
    public bool? Approved { get; set; }       // null = vote did not complete
    public int SuccessCount { get; set; }
    public int FailCount { get; set; }
    public MissionOutcome MissionResult { get; set; } = MissionOutcome.Pending;

    public List<AvalonGameVote> Votes { get; set; } = new();
}

public class AvalonGameVote
{
    public int Id { get; set; }
    public int ProposalId { get; set; }
    public AvalonGameProposal Proposal { get; set; } = null!;

    public int VoterSeatIndex { get; set; }
    public bool Approve { get; set; }
}
