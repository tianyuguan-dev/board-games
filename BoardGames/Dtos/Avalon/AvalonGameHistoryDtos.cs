using BoardGames.Models.Avalon;

namespace BoardGames.Dtos.Avalon;

// ---- Recent games list ----

public class AvalonGameSummaryDto
{
    public int Id { get; set; }
    public DateTime EndedAt { get; set; }
    public int PlayerCount { get; set; }
    public string Winner { get; set; } = "None";       // "Good" / "Evil"
    public string? WinReason { get; set; }

    // Current user's perspective:
    public string MyRole { get; set; } = "";
    public bool MyIsWinner { get; set; }
    public decimal MyBalanceDelta { get; set; }

    public static AvalonGameSummaryDto From(AvalonGameHistory game, int myUserId)
    {
        var me = game.Players.FirstOrDefault(p => p.UserId == myUserId);
        return new AvalonGameSummaryDto
        {
            Id = game.Id,
            EndedAt = game.EndedAt,
            PlayerCount = game.PlayerCount,
            Winner = game.Winner.ToString(),
            WinReason = game.WinReason,
            MyRole = me?.Role.ToString() ?? "",
            MyIsWinner = me?.IsWinner ?? false,
            MyBalanceDelta = me?.BalanceDelta ?? 0
        };
    }
}

// ---- Single game detail ----

public class AvalonGameDetailDto
{
    public int Id { get; set; }
    public string RoomId { get; set; } = "";
    public int PlayerCount { get; set; }
    public int MaxRejects { get; set; }
    public string Winner { get; set; } = "None";
    public string? WinReason { get; set; }
    public bool BonusAssassination { get; set; }
    public bool EarlyAssassination { get; set; }
    public int? AssassinTargetSeat { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime EndedAt { get; set; }
    public int DurationSeconds { get; set; }

    // Current viewer's perspective (so the detail page can render the same "YOU WIN" banner as in-game settlement)
    public int MySeatIndex { get; set; }
    public string MyRole { get; set; } = "";
    public bool MyIsWinner { get; set; }
    public decimal MyBalanceDelta { get; set; }

    public List<AvalonPlayerDetailDto> Players { get; set; } = new();
    public List<AvalonMissionDetailDto> Missions { get; set; } = new();

    public static AvalonGameDetailDto From(AvalonGameHistory game, int myUserId)
    {
        var me = game.Players.FirstOrDefault(p => p.UserId == myUserId);
        var dto = new AvalonGameDetailDto
        {
            MySeatIndex = me?.SeatIndex ?? -1,
            MyRole = me?.Role.ToString() ?? "",
            MyIsWinner = me?.IsWinner ?? false,
            MyBalanceDelta = me?.BalanceDelta ?? 0,
            Id = game.Id,
            RoomId = game.RoomId,
            PlayerCount = game.PlayerCount,
            MaxRejects = game.MaxRejects,
            Winner = game.Winner.ToString(),
            WinReason = game.WinReason,
            BonusAssassination = game.BonusAssassination,
            EarlyAssassination = game.EarlyAssassination,
            AssassinTargetSeat = game.AssassinTargetSeat,
            StartedAt = game.StartedAt,
            EndedAt = game.EndedAt,
            DurationSeconds = (int)(game.EndedAt - game.StartedAt).TotalSeconds,
            Players = game.Players
                .OrderBy(p => p.SeatIndex)
                .Select(p => new AvalonPlayerDetailDto
                {
                    SeatIndex = p.SeatIndex,
                    Nickname = p.Nickname,
                    Role = p.Role.ToString(),
                    IsWinner = p.IsWinner,
                    BalanceDelta = p.BalanceDelta
                })
                .ToList()
        };

        // Group proposals by mission index
        var byMission = game.Proposals
            .GroupBy(p => p.MissionIndex)
            .OrderBy(g => g.Key);
        foreach (var grp in byMission)
        {
            var mission = new AvalonMissionDetailDto
            {
                MissionIndex = grp.Key,
                Proposals = grp
                    .OrderBy(p => p.ProposalIndex)
                    .Select(p => new AvalonProposalDetailDto
                    {
                        ProposalIndex = p.ProposalIndex,
                        LeaderSeatIndex = p.LeaderSeatIndex,
                        TeamSeats = p.TeamSeats.ToList(),
                        Approved = p.Approved,
                        SuccessCount = p.SuccessCount,
                        FailCount = p.FailCount,
                        MissionResult = p.MissionResult.ToString(),
                        Votes = p.Votes
                            .OrderBy(v => v.VoterSeatIndex)
                            .Select(v => new AvalonVoteDetailDto
                            {
                                VoterSeatIndex = v.VoterSeatIndex,
                                Approve = v.Approve
                            })
                            .ToList()
                    })
                    .ToList()
            };
            dto.Missions.Add(mission);
        }

        return dto;
    }
}

public class AvalonPlayerDetailDto
{
    public int SeatIndex { get; set; }
    public string Nickname { get; set; } = "";
    public string Role { get; set; } = "";
    public bool IsWinner { get; set; }
    public decimal BalanceDelta { get; set; }
}

public class AvalonMissionDetailDto
{
    public int MissionIndex { get; set; }
    public List<AvalonProposalDetailDto> Proposals { get; set; } = new();
}

public class AvalonProposalDetailDto
{
    public int ProposalIndex { get; set; }
    public int LeaderSeatIndex { get; set; }
    public List<int> TeamSeats { get; set; } = new();
    public bool? Approved { get; set; }
    public int SuccessCount { get; set; }
    public int FailCount { get; set; }
    public string MissionResult { get; set; } = "Pending";
    public List<AvalonVoteDetailDto> Votes { get; set; } = new();
}

public class AvalonVoteDetailDto
{
    public int VoterSeatIndex { get; set; }
    public bool Approve { get; set; }
}
