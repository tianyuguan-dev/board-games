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
    public List<int>? MissionPlayersPlayed { get; set; }

    // Assassination
    public int? AssassinIndex { get; set; }
    public int? AssassinTarget { get; set; }
    public bool BonusAssassination { get; set; }
    public string? BonusLossReason { get; set; }
    public bool EarlyAssassination { get; set; }
    public List<int>? AssassinationTargets { get; set; }

    // Game over
    public string? Winner { get; set; }
    public string? WinReason { get; set; }
    public List<string>? AllRoles { get; set; } // revealed at game end
    public List<decimal>? BalanceDeltas { get; set; } // ±1, ±2 (double points), or ±0.5 (shielded), indexed by seat

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
            AssassinIndex = game.Roles.Contains(AvalonRole.Assassin) ? game.GetAssassinIndex() : null,
            EarlyAssassination = game.EarlyAssassination,
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
            dto.MissionPlayersPlayed = game.GetMissionPlayersPlayed();
        }

        // Assassination
        if (game.Phase == AvalonPhase.Assassination)
        {
            dto.BonusAssassination = game.BonusAssassination;
            if (game.BonusAssassination) dto.BonusLossReason = game.BonusLossReason;
            if (game.Roles[playerIndex] == AvalonRole.Assassin)
            {
                dto.AssassinationTargets = Enumerable.Range(0, game.PlayerCount)
                    .Where(i => AvalonConfig.GetTeam(game.Roles[i]) == AvalonTeam.Good)
                    .ToList();
            }
        }

        // Game over: reveal everything
        if (game.Phase == AvalonPhase.GameOver)
        {
            dto.Winner = game.Winner.ToString();
            dto.WinReason = game.WinReason;
            dto.AllRoles = game.Roles.Select(r => r.ToString()).ToList();
            dto.AssassinTarget = game.AssassinTarget;
            dto.BonusAssassination = game.BonusAssassination;

            // Mirror SettleGame scoring so the settlement screen shows ±n next to each role.
            bool bonusKill = game.BonusAssassination
                             && game.AssassinTarget.HasValue
                             && game.Roles[game.AssassinTarget.Value] == AvalonRole.Merlin;
            decimal points = bonusKill ? 2 : 1;
            bool shielded = game.Winner == GameWinner.Good
                            && game.AssassinTarget.HasValue
                            && game.Roles[game.AssassinTarget.Value] != AvalonRole.Merlin
                            && game.Roles[game.AssassinTarget.Value] != AvalonRole.Percival;

            var deltas = new List<decimal>(game.PlayerCount);
            for (int i = 0; i < game.PlayerCount; i++)
            {
                var team = AvalonConfig.GetTeam(game.Roles[i]);
                bool isWinner = (team == AvalonTeam.Good && game.Winner == GameWinner.Good)
                                || (team == AvalonTeam.Evil && game.Winner == GameWinner.Evil);
                decimal delta = isWinner ? points : -points;
                if (shielded && game.AssassinTarget.HasValue && i == game.AssassinTarget.Value)
                    delta += 0.5m;
                deltas.Add(delta);
            }
            dto.BalanceDeltas = deltas;
        }

        // History
        foreach (var missionProposals in game.MissionHistory)
        {
            dto.History.Add(missionProposals.Select(p => AvalonProposalDto.From(p, true)).ToList());
        }

        return dto;
    }
}
