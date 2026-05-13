using BoardGames.Models.Avalon;

namespace BoardGames.Dtos.Avalon;

public class AvalonProposalDto
{
    public int LeaderIndex { get; set; }
    public List<int> Team { get; set; } = new();
    public Dictionary<int, bool>? Votes { get; set; } // null until all votes in
    public bool? Approved { get; set; }
    public int? SuccessCount { get; set; }
    public int? FailCount { get; set; }
    public string? MissionResult { get; set; }

    public static AvalonProposalDto From(AvalonProposal p, bool includeVotes)
    {
        var dto = new AvalonProposalDto
        {
            LeaderIndex = p.LeaderIndex,
            Team = p.Team,
            Approved = p.Approved,
        };

        if (includeVotes && p.Approved.HasValue)
            dto.Votes = new Dictionary<int, bool>(p.Votes);

        if (p.MissionResult != MissionOutcome.Pending)
        {
            dto.SuccessCount = p.SuccessCount;
            dto.FailCount = p.FailCount;
            dto.MissionResult = p.MissionResult.ToString();
        }

        return dto;
    }
}

public class AvalonGameStateDto
{
    public string Phase { get; set; } = "";
    public int PlayerCount { get; set; }
    public List<string> PlayerNames { get; set; } = new();

    // Player's own info
    public int MyIndex { get; set; }
    public string? MyRole { get; set; }
    public string? MyTeam { get; set; }
    public List<int>? VisiblePlayers { get; set; }
    public string? VisibleHint { get; set; } // e.g. "These players are evil" or "One of these is Merlin"
    public Dictionary<int, string>? VisiblePlayerRoles { get; set; } // evil allies' specific roles

    // Game state
    public int CurrentLeaderIndex { get; set; }
    public int CurrentMissionIndex { get; set; }
    public int RequiredTeamSize { get; set; }
    public int ConsecutiveRejects { get; set; }
    public int MaxConsecutiveRejects { get; set; }
    public string[] MissionResults { get; set; } = new string[AvalonGame.TotalMissions];
    public List<int> ProposedTeam { get; set; } = new();

    // Voting state
    public List<int>? PlayersWhoVoted { get; set; } // indices of who has voted (not how)
    public Dictionary<int, bool>? VoteResults { get; set; } // only after all voted

    // Mission state
    public int? MissionCardsPlayed { get; set; }
    public int? MissionCardsTotal { get; set; }

    // Assassination
    public int? AssassinIndex { get; set; }
    public int? AssassinTarget { get; set; }
    public bool BonusAssassination { get; set; }

    // Game over
    public string? Winner { get; set; }
    public string? WinReason { get; set; }
    public List<string>? AllRoles { get; set; } // revealed at game end

    // History
    public List<List<AvalonProposalDto>> History { get; set; } = new();

    public static AvalonGameStateDto Create(AvalonGame game, int playerIndex, List<string> playerNames)
    {
        var dto = new AvalonGameStateDto
        {
            Phase = game.Phase.ToString(),
            PlayerCount = game.PlayerCount,
            PlayerNames = playerNames,
            MyIndex = playerIndex,
            MyRole = game.Roles[playerIndex].ToString(),
            MyTeam = AvalonConfig.GetTeam(game.Roles[playerIndex]).ToString(),
            CurrentLeaderIndex = game.CurrentLeaderIndex,
            CurrentMissionIndex = game.CurrentMissionIndex,
            RequiredTeamSize = game.GetRequiredTeamSize(),
            ConsecutiveRejects = game.ConsecutiveRejects,
            MaxConsecutiveRejects = game.MaxConsecutiveRejects,
            ProposedTeam = game.ProposedTeam,
        };

        // Mission results
        for (int i = 0; i < AvalonGame.TotalMissions; i++)
            dto.MissionResults[i] = game.MissionResults[i].ToString();

        // Night reveal info
        var visible = game.GetVisiblePlayers(playerIndex);
        if (visible.Count > 0)
        {
            dto.VisiblePlayers = visible;
            var myRole = game.Roles[playerIndex];
            dto.VisibleHint = myRole switch
            {
                AvalonRole.Merlin => "These players are evil",
                AvalonRole.Percival => "One of these is Merlin",
                _ when AvalonConfig.GetTeam(myRole) == AvalonTeam.Evil => "Your evil allies",
                _ => null
            };
            // Evil players can see their allies' specific roles
            if (AvalonConfig.GetTeam(myRole) == AvalonTeam.Evil && myRole != AvalonRole.Oberon)
            {
                dto.VisiblePlayerRoles = visible.ToDictionary(i => i, i => game.Roles[i].ToString());
            }
        }

        // Vote phase: show who voted but not how, until all in
        if (game.Phase == AvalonPhase.TeamVote && game.CurrentProposal != null)
        {
            dto.PlayersWhoVoted = game.CurrentProposal.Votes.Keys.ToList();
        }

        // Mission phase
        if (game.Phase == AvalonPhase.Mission && game.CurrentProposal != null)
        {
            dto.MissionCardsTotal = game.CurrentProposal.Team.Count;
            // We don't expose individual mission actions count to prevent info leak
            // Just show it's in progress
        }

        // Assassination
        if (game.Phase == AvalonPhase.Assassination)
        {
            dto.AssassinIndex = game.GetAssassinIndex();
            dto.BonusAssassination = game.BonusAssassination;
        }

        // Game over: reveal everything
        if (game.Phase == AvalonPhase.GameOver)
        {
            dto.Winner = game.Winner.ToString();
            dto.WinReason = game.WinReason;
            dto.AllRoles = game.Roles.Select(r => r.ToString()).ToList();
            dto.AssassinTarget = game.AssassinTarget;
        }

        // History
        foreach (var missionProposals in game.MissionHistory)
        {
            dto.History.Add(missionProposals.Select(p => AvalonProposalDto.From(p, true)).ToList());
        }

        return dto;
    }
}
