namespace BoardGames.Models.Avalon;

public class AvalonGame
{
    public const int MissionsToWin = 3;
    public const int TotalMissions = 5;

    public int MaxConsecutiveRejects { get; }
    public int PlayerCount { get; }
    public AvalonPhase Phase { get; private set; } = AvalonPhase.NightReveal;
    public List<AvalonRole> Roles { get; } = new();
    public int CurrentLeaderIndex { get; private set; }
    public int CurrentMissionIndex { get; private set; }
    public int ConsecutiveRejects { get; private set; }
    public MissionOutcome[] MissionResults { get; } = new MissionOutcome[TotalMissions];
    public List<int> ProposedTeam { get; private set; } = new();
    public GameWinner Winner { get; private set; } = GameWinner.None;
    public string? WinReason { get; private set; }
    public int? AssassinTarget { get; private set; }
    public bool BonusAssassination { get; private set; } // true when evil already won 3 missions but gets a chance to find Merlin for double points
    public bool EarlyAssassination { get; private set; } // true when assassin chose to assassinate mid-game
    public DateTime StartedAt { get; } = DateTime.UtcNow;

    // History: all proposals grouped by mission
    public List<List<AvalonProposal>> MissionHistory { get; } = new();

    // Current proposal (during TeamVote / Mission phases)
    public AvalonProposal? CurrentProposal { get; private set; }

    // Mission phase: tracks who has played their card
    private readonly Dictionary<int, bool> _missionActions = new(); // playerIndex => success

    // When BonusAssassination is true, describes how Evil already won (used by Assassinate to build WinReason)
    private string _bonusLossReason = "3 missions failed";
    public string BonusLossReason => _bonusLossReason;

    public AvalonGame(int playerCount, List<AvalonRole> roleConfig, int startLeader = 0, int maxRejects = 4, bool shuffleRoles = true)
    {
        if (!AvalonConfig.IsValidPlayerCount(playerCount))
            throw new ArgumentOutOfRangeException(nameof(playerCount), "Must be 5-10");
        if (roleConfig.Count != playerCount)
            throw new ArgumentException("Role config count must match player count");

        PlayerCount = playerCount;
        MaxConsecutiveRejects = maxRejects;
        CurrentLeaderIndex = startLeader % playerCount;

        for (int i = 0; i < TotalMissions; i++)
        {
            MissionResults[i] = MissionOutcome.Pending;
            MissionHistory.Add(new List<AvalonProposal>());
        }

        var roles = new List<AvalonRole>(roleConfig);
        if (shuffleRoles)
        {
            // Fisher-Yates shuffle
            var rng = new Random();
            for (int i = roles.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (roles[i], roles[j]) = (roles[j], roles[i]);
            }
        }
        Roles.AddRange(roles);
    }

    // Night reveal: what each player can see
    public List<int> GetVisiblePlayers(int playerIndex)
    {
        var role = Roles[playerIndex];
        var visible = new List<int>();

        switch (role)
        {
            case AvalonRole.Merlin:
                // Sees all evil except Mordred
                for (int i = 0; i < PlayerCount; i++)
                {
                    if (i == playerIndex) continue;
                    var r = Roles[i];
                    if (AvalonConfig.GetTeam(r) == AvalonTeam.Evil && r != AvalonRole.Mordred)
                        visible.Add(i);
                }
                break;

            case AvalonRole.Percival:
                // Sees Merlin and Morgana (can't tell which is which)
                for (int i = 0; i < PlayerCount; i++)
                {
                    if (i == playerIndex) continue;
                    if (Roles[i] is AvalonRole.Merlin or AvalonRole.Morgana)
                        visible.Add(i);
                }
                break;

            case AvalonRole.Assassin:
            case AvalonRole.Morgana:
            case AvalonRole.MinionOfMordred:
            case AvalonRole.Mordred:
                // Evil players see each other (except Oberon)
                for (int i = 0; i < PlayerCount; i++)
                {
                    if (i == playerIndex) continue;
                    var r = Roles[i];
                    if (AvalonConfig.GetTeam(r) == AvalonTeam.Evil && r != AvalonRole.Oberon)
                        visible.Add(i);
                }
                break;

            // Oberon: evil but sees nobody
            // LoyalServant: sees nobody
        }

        return visible;
    }

    public void StartProposalPhase()
    {
        Phase = AvalonPhase.TeamProposal;
        ProposedTeam = new List<int>();
    }

    public void ProposeTeam(int leaderIndex, List<int> team)
    {
        if (Phase != AvalonPhase.TeamProposal) return;
        if (leaderIndex != CurrentLeaderIndex) return;

        int requiredSize = AvalonConfig.GetMissionSize(PlayerCount, CurrentMissionIndex);
        if (team.Count != requiredSize) return;
        if (team.Distinct().Count() != team.Count) return;
        if (team.Any(i => i < 0 || i >= PlayerCount)) return;

        ProposedTeam = team;
        CurrentProposal = new AvalonProposal
        {
            LeaderIndex = CurrentLeaderIndex,
            Team = new List<int>(team)
        };
        Phase = AvalonPhase.TeamVote;
    }

    public void CastVote(int playerIndex, bool approve)
    {
        if (Phase != AvalonPhase.TeamVote) return;
        if (playerIndex < 0 || playerIndex >= PlayerCount) return;
        if (CurrentProposal == null) return;
        if (CurrentProposal.Votes.ContainsKey(playerIndex)) return;

        CurrentProposal.Votes[playerIndex] = approve;

        if (CurrentProposal.AllVotesIn(PlayerCount))
            ResolveVote();
    }

    public bool AllVotesIn() => CurrentProposal?.AllVotesIn(PlayerCount) ?? false;

    private void ResolveVote()
    {
        int approves = CurrentProposal!.Votes.Count(v => v.Value);
        bool approved = approves > PlayerCount / 2;
        CurrentProposal.Approved = approved;
        MissionHistory[CurrentMissionIndex].Add(CurrentProposal);

        if (approved)
        {
            ConsecutiveRejects = 0;
            _missionActions.Clear();
            Phase = AvalonPhase.Mission;
        }
        else
        {
            ConsecutiveRejects++;
            if (ConsecutiveRejects >= MaxConsecutiveRejects)
            {
                // Evil wins by reject limit, but assassin gets a bonus chance to find Merlin (double points)
                if (Roles.Contains(AvalonRole.Assassin))
                {
                    _bonusLossReason = $"{MaxConsecutiveRejects} proposals rejected";
                    BonusAssassination = true;
                    Phase = AvalonPhase.Assassination;
                    return;
                }

                Winner = GameWinner.Evil;
                WinReason = $"{MaxConsecutiveRejects} proposals rejected!";
                Phase = AvalonPhase.GameOver;
                return;
            }

            AdvanceLeader();
            Phase = AvalonPhase.TeamProposal;
            ProposedTeam = new List<int>();
            CurrentProposal = null;
        }
    }

    public void PlayMissionCard(int playerIndex, bool success)
    {
        if (Phase != AvalonPhase.Mission) return;
        if (CurrentProposal == null) return;
        if (!CurrentProposal.Team.Contains(playerIndex)) return;
        if (_missionActions.ContainsKey(playerIndex)) return;

        // Good players must play success
        if (AvalonConfig.GetTeam(Roles[playerIndex]) == AvalonTeam.Good)
            success = true;

        _missionActions[playerIndex] = success;

        if (_missionActions.Count == CurrentProposal.Team.Count)
            ResolveMission();
    }

    public bool AllMissionCardsIn()
    {
        return CurrentProposal != null && _missionActions.Count == CurrentProposal.Team.Count;
    }

    public List<int> GetMissionPlayersPlayed() => _missionActions.Keys.ToList();

    private void ResolveMission()
    {
        int fails = _missionActions.Count(a => !a.Value);
        int successes = _missionActions.Count(a => a.Value);
        int failsRequired = AvalonConfig.GetFailsRequired(PlayerCount, CurrentMissionIndex);

        CurrentProposal!.SuccessCount = successes;
        CurrentProposal.FailCount = fails;

        bool missionSuccess = fails < failsRequired;
        CurrentProposal.MissionResult = missionSuccess ? MissionOutcome.Success : MissionOutcome.Fail;
        MissionResults[CurrentMissionIndex] = CurrentProposal.MissionResult;

        int goodWins = MissionResults.Count(r => r == MissionOutcome.Success);
        int evilWins = MissionResults.Count(r => r == MissionOutcome.Fail);

        if (evilWins >= MissionsToWin)
        {
            // Evil wins by missions, but assassin gets a bonus chance to find Merlin (double points)
            if (Roles.Contains(AvalonRole.Assassin))
            {
                BonusAssassination = true;
                Phase = AvalonPhase.Assassination;
                return;
            }

            Winner = GameWinner.Evil;
            WinReason = "3 missions failed!";
            Phase = AvalonPhase.GameOver;
            return;
        }

        if (goodWins >= MissionsToWin)
        {
            // Good wins missions, but assassin gets a chance
            if (Roles.Contains(AvalonRole.Assassin))
            {
                Phase = AvalonPhase.Assassination;
                return;
            }

            Winner = GameWinner.Good;
            WinReason = "3 missions completed successfully!";
            Phase = AvalonPhase.GameOver;
            return;
        }

        // Next mission
        CurrentMissionIndex++;
        ConsecutiveRejects = 0;
        AdvanceLeader();
        Phase = AvalonPhase.TeamProposal;
        ProposedTeam = new List<int>();
        CurrentProposal = null;
    }

    public bool BeginEarlyAssassination(int assassinIndex)
    {
        if (Phase == AvalonPhase.NightReveal || Phase == AvalonPhase.Assassination || Phase == AvalonPhase.GameOver) return false;
        if (Roles[assassinIndex] != AvalonRole.Assassin) return false;
        EarlyAssassination = true;
        Phase = AvalonPhase.Assassination;
        return true;
    }

    public void Assassinate(int assassinIndex, int targetIndex)
    {
        if (Phase != AvalonPhase.Assassination) return;
        if (Roles[assassinIndex] != AvalonRole.Assassin) return;
        if (targetIndex < 0 || targetIndex >= PlayerCount) return;
        if (AvalonConfig.GetTeam(Roles[targetIndex]) == AvalonTeam.Evil) return;

        AssassinTarget = targetIndex;

        if (EarlyAssassination && !BonusAssassination)
        {
            // Mid-game assassination
            if (Roles[targetIndex] == AvalonRole.Merlin)
            {
                Winner = GameWinner.Evil;
                WinReason = "Merlin has been assassinated mid-game!";
            }
            else
            {
                Winner = GameWinner.Good;
                WinReason = "Assassin struck early but chose the wrong target!";
            }
        }
        else if (BonusAssassination)
        {
            // Evil already won (by missions or reject limit); finding Merlin = double points
            Winner = GameWinner.Evil;
            WinReason = Roles[targetIndex] == AvalonRole.Merlin
                ? $"{_bonusLossReason} + Merlin assassinated! Double points!"
                : $"{_bonusLossReason}. Assassin missed Merlin.";
        }
        else
        {
            if (Roles[targetIndex] == AvalonRole.Merlin)
            {
                Winner = GameWinner.Evil;
                WinReason = "Merlin has been assassinated!";
            }
            else
            {
                Winner = GameWinner.Good;
                WinReason = "Merlin survived! Assassin chose the wrong target.";
            }
        }

        Phase = AvalonPhase.GameOver;
    }

    public int GetAssassinIndex()
    {
        return Roles.IndexOf(AvalonRole.Assassin);
    }

    public int GetRequiredTeamSize()
    {
        return AvalonConfig.GetMissionSize(PlayerCount, CurrentMissionIndex);
    }

    private void AdvanceLeader()
    {
        CurrentLeaderIndex = (CurrentLeaderIndex + 1) % PlayerCount;
    }
}
