using BoardGames.Models.Avalon;

namespace BoardGames.Tests.Models.Avalon;

public class AvalonGameTests
{
    private static List<AvalonRole> DefaultRoles(int count) => AvalonConfig.GetDefaultRoles(count);

    [Fact]
    public void Constructor_ThrowsForInvalidPlayerCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvalonGame(4, DefaultRoles(5)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvalonGame(11, DefaultRoles(10)));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(10)]
    public void Constructor_AssignsCorrectRoleCounts(int playerCount)
    {
        var game = new AvalonGame(playerCount, DefaultRoles(playerCount));
        var (goodCount, evilCount) = AvalonConfig.GetTeamSizes(playerCount);

        Assert.Equal(playerCount, game.Roles.Count);
        Assert.Equal(goodCount, game.Roles.Count(r => AvalonConfig.GetTeam(r) == AvalonTeam.Good));
        Assert.Equal(evilCount, game.Roles.Count(r => AvalonConfig.GetTeam(r) == AvalonTeam.Evil));
    }

    [Fact]
    public void Constructor_AlwaysIncludesMerlinAndAssassin()
    {
        var game = new AvalonGame(5, DefaultRoles(5));
        Assert.Contains(AvalonRole.Merlin, game.Roles);
        Assert.Contains(AvalonRole.Assassin, game.Roles);
    }

    [Fact]
    public void Constructor_StartsInNightReveal()
    {
        var game = new AvalonGame(5, DefaultRoles(5));
        Assert.Equal(AvalonPhase.NightReveal, game.Phase);
    }

    [Fact]
    public void Constructor_IncludesOptionalRoles()
    {
        var roles = new List<AvalonRole>
        {
            AvalonRole.Merlin, AvalonRole.Percival, AvalonRole.LoyalServant, AvalonRole.LoyalServant,
            AvalonRole.Assassin, AvalonRole.Morgana, AvalonRole.Mordred
        };
        var game = new AvalonGame(7, roles);
        Assert.Contains(AvalonRole.Percival, game.Roles);
        Assert.Contains(AvalonRole.Morgana, game.Roles);
        Assert.Contains(AvalonRole.Mordred, game.Roles);
    }

    // Night reveal visibility

    [Fact]
    public void Merlin_SeesEvilExceptMordred()
    {
        var roles = new List<AvalonRole>
        {
            AvalonRole.Merlin, AvalonRole.Percival, AvalonRole.LoyalServant, AvalonRole.LoyalServant,
            AvalonRole.Assassin, AvalonRole.Morgana, AvalonRole.Mordred
        };
        var game = new AvalonGame(7, roles);
        int merlinIndex = game.Roles.IndexOf(AvalonRole.Merlin);
        int mordredIndex = game.Roles.IndexOf(AvalonRole.Mordred);

        var visible = game.GetVisiblePlayers(merlinIndex);

        for (int i = 0; i < game.PlayerCount; i++)
        {
            if (i == merlinIndex) continue;
            var role = game.Roles[i];
            if (AvalonConfig.GetTeam(role) == AvalonTeam.Evil && role != AvalonRole.Mordred)
                Assert.Contains(i, visible);
            else
                Assert.DoesNotContain(i, visible);
        }
    }

    [Fact]
    public void Percival_SeesMerlinAndMorgana()
    {
        var roles = new List<AvalonRole>
        {
            AvalonRole.Merlin, AvalonRole.Percival, AvalonRole.LoyalServant, AvalonRole.LoyalServant,
            AvalonRole.Assassin, AvalonRole.Morgana, AvalonRole.Oberon
        };
        var game = new AvalonGame(7, roles);
        int percivalIndex = game.Roles.IndexOf(AvalonRole.Percival);

        var visible = game.GetVisiblePlayers(percivalIndex);

        Assert.Contains(game.Roles.IndexOf(AvalonRole.Merlin), visible);
        Assert.Contains(game.Roles.IndexOf(AvalonRole.Morgana), visible);
        Assert.Equal(2, visible.Count);
    }

    [Fact]
    public void Evil_SeeEachOtherExceptOberon()
    {
        var roles = new List<AvalonRole>
        {
            AvalonRole.Merlin, AvalonRole.Percival, AvalonRole.LoyalServant, AvalonRole.LoyalServant,
            AvalonRole.Assassin, AvalonRole.Morgana, AvalonRole.Oberon
        };
        var game = new AvalonGame(7, roles);
        int assassinIndex = game.Roles.IndexOf(AvalonRole.Assassin);
        int oberonIndex = game.Roles.IndexOf(AvalonRole.Oberon);

        var visible = game.GetVisiblePlayers(assassinIndex);
        Assert.DoesNotContain(oberonIndex, visible);

        var oberonVisible = game.GetVisiblePlayers(oberonIndex);
        Assert.Empty(oberonVisible);
    }

    [Fact]
    public void LoyalServant_SeesNobody()
    {
        var game = new AvalonGame(5, DefaultRoles(5));
        int servantIndex = game.Roles.IndexOf(AvalonRole.LoyalServant);
        if (servantIndex >= 0)
        {
            var visible = game.GetVisiblePlayers(servantIndex);
            Assert.Empty(visible);
        }
    }

    // Team proposal and voting

    [Fact]
    public void ProposeTeam_SetsPhaseToTeamVote()
    {
        var game = new AvalonGame(5, DefaultRoles(5), startLeader: 0);
        game.StartProposalPhase();

        game.ProposeTeam(0, new List<int> { 0, 1 });

        Assert.Equal(AvalonPhase.TeamVote, game.Phase);
    }

    [Fact]
    public void ProposeTeam_RejectsWrongSize()
    {
        var game = new AvalonGame(5, DefaultRoles(5), startLeader: 0);
        game.StartProposalPhase();

        game.ProposeTeam(0, new List<int> { 0 }); // needs 2

        Assert.Equal(AvalonPhase.TeamProposal, game.Phase);
    }

    [Fact]
    public void ProposeTeam_RejectsNonLeader()
    {
        var game = new AvalonGame(5, DefaultRoles(5), startLeader: 0);
        game.StartProposalPhase();

        game.ProposeTeam(1, new List<int> { 0, 1 });

        Assert.Equal(AvalonPhase.TeamProposal, game.Phase);
    }

    [Fact]
    public void Vote_ApprovedWhenMajority()
    {
        var game = new AvalonGame(5, DefaultRoles(5), startLeader: 0);
        game.StartProposalPhase();
        game.ProposeTeam(0, new List<int> { 0, 1 });

        game.CastVote(0, true);
        game.CastVote(1, true);
        game.CastVote(2, true);
        game.CastVote(3, false);
        game.CastVote(4, false);

        Assert.Equal(AvalonPhase.Mission, game.Phase);
    }

    [Fact]
    public void Vote_RejectedWhenNoMajority()
    {
        var game = new AvalonGame(5, DefaultRoles(5), startLeader: 0);
        game.StartProposalPhase();
        game.ProposeTeam(0, new List<int> { 0, 1 });

        game.CastVote(0, true);
        game.CastVote(1, true);
        game.CastVote(2, false);
        game.CastVote(3, false);
        game.CastVote(4, false);

        Assert.Equal(AvalonPhase.TeamProposal, game.Phase);
        Assert.Equal(1, game.ConsecutiveRejects);
        Assert.Equal(1, game.CurrentLeaderIndex);
    }

    [Fact]
    public void Vote_FiveRejectsEvilWins()
    {
        var game = new AvalonGame(5, DefaultRoles(5), startLeader: 0);

        for (int r = 0; r < 5; r++)
        {
            game.StartProposalPhase();
            game.ProposeTeam(game.CurrentLeaderIndex, new List<int> { 0, 1 });
            for (int p = 0; p < 5; p++)
                game.CastVote(p, false);
        }

        Assert.Equal(AvalonPhase.GameOver, game.Phase);
        Assert.Equal(GameWinner.Evil, game.Winner);
    }

    [Fact]
    public void Vote_DuplicateVoteIgnored()
    {
        var game = new AvalonGame(5, DefaultRoles(5), startLeader: 0);
        game.StartProposalPhase();
        game.ProposeTeam(0, new List<int> { 0, 1 });

        game.CastVote(0, true);
        game.CastVote(0, false); // duplicate, should be ignored

        Assert.True(game.CurrentProposal!.Votes[0]);
    }

    // Mission

    [Fact]
    public void Mission_SucceedsWhenAllSuccess()
    {
        var game = new AvalonGame(5, DefaultRoles(5), startLeader: 0);
        game.StartProposalPhase();
        game.ProposeTeam(0, new List<int> { 0, 1 });
        for (int p = 0; p < 5; p++) game.CastVote(p, true);

        game.PlayMissionCard(0, true);
        game.PlayMissionCard(1, true);

        Assert.Equal(MissionOutcome.Success, game.MissionResults[0]);
        Assert.Equal(1, game.CurrentMissionIndex);
    }

    [Fact]
    public void Mission_FailsWithOneFail()
    {
        var game = new AvalonGame(5, DefaultRoles(5), startLeader: 0);
        game.StartProposalPhase();
        game.ProposeTeam(0, new List<int> { 0, 1 });
        for (int p = 0; p < 5; p++) game.CastVote(p, true);

        game.PlayMissionCard(0, true);
        game.PlayMissionCard(1, false);

        Assert.NotEqual(MissionOutcome.Pending, game.MissionResults[0]);
    }

    [Fact]
    public void Mission_GoodPlayerForcedSuccess()
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            var game = new AvalonGame(5, DefaultRoles(5), startLeader: 0);
            var goodPlayers = Enumerable.Range(0, 5)
                .Where(i => AvalonConfig.GetTeam(game.Roles[i]) == AvalonTeam.Good)
                .Take(2).ToList();

            if (!goodPlayers.Contains(0)) continue;

            game.StartProposalPhase();
            game.ProposeTeam(0, goodPlayers);
            for (int p = 0; p < 5; p++) game.CastVote(p, true);

            game.PlayMissionCard(goodPlayers[0], false);
            game.PlayMissionCard(goodPlayers[1], false);

            Assert.Equal(MissionOutcome.Success, game.MissionResults[0]);
            return;
        }

        Assert.Fail("Could not find a game where player 0 is good after 100 attempts");
    }

    // Full game: 3 successes -> assassination

    [Fact]
    public void ThreeSuccesses_GoesToAssassination()
    {
        var game = new AvalonGame(5, DefaultRoles(5), startLeader: 0);

        var goodPlayers = Enumerable.Range(0, 5)
            .Where(i => AvalonConfig.GetTeam(game.Roles[i]) == AvalonTeam.Good)
            .ToList();

        int[] missionSizes = { 2, 3, 2 };
        for (int m = 0; m < 3; m++)
        {
            game.StartProposalPhase();
            var team = goodPlayers.Take(missionSizes[m]).ToList();
            game.ProposeTeam(game.CurrentLeaderIndex, team);
            for (int p = 0; p < 5; p++) game.CastVote(p, true);
            foreach (int t in team) game.PlayMissionCard(t, true);
        }

        Assert.Equal(AvalonPhase.Assassination, game.Phase);
    }

    // Assassination

    [Fact]
    public void Assassination_KillingMerlinEvilWins()
    {
        var game = CreateGameAtAssassination();
        int assassinIndex = game.GetAssassinIndex();
        int merlinIndex = game.Roles.IndexOf(AvalonRole.Merlin);

        game.Assassinate(assassinIndex, merlinIndex);

        Assert.Equal(AvalonPhase.GameOver, game.Phase);
        Assert.Equal(GameWinner.Evil, game.Winner);
    }

    [Fact]
    public void Assassination_MissingMerlinGoodWins()
    {
        var game = CreateGameAtAssassination();
        int assassinIndex = game.GetAssassinIndex();

        var target = Enumerable.Range(0, 5)
            .First(i => game.Roles[i] != AvalonRole.Merlin && AvalonConfig.GetTeam(game.Roles[i]) == AvalonTeam.Good);

        game.Assassinate(assassinIndex, target);

        Assert.Equal(AvalonPhase.GameOver, game.Phase);
        Assert.Equal(GameWinner.Good, game.Winner);
    }

    [Fact]
    public void Assassination_OnlyAssassinCanDo()
    {
        var game = CreateGameAtAssassination();
        int nonAssassin = Enumerable.Range(0, 5).First(i => game.Roles[i] != AvalonRole.Assassin);

        game.Assassinate(nonAssassin, 0);

        Assert.Equal(AvalonPhase.Assassination, game.Phase);
    }

    [Fact]
    public void History_RecordsAllProposals()
    {
        var game = new AvalonGame(5, DefaultRoles(5), startLeader: 0);
        game.StartProposalPhase();

        game.ProposeTeam(0, new List<int> { 0, 1 });
        for (int p = 0; p < 5; p++) game.CastVote(p, false);

        game.StartProposalPhase();
        game.ProposeTeam(1, new List<int> { 1, 2 });
        for (int p = 0; p < 5; p++) game.CastVote(p, true);

        Assert.Equal(2, game.MissionHistory[0].Count);
        Assert.False(game.MissionHistory[0][0].Approved);
        Assert.True(game.MissionHistory[0][1].Approved);
        Assert.Equal(5, game.MissionHistory[0][0].Votes.Count);
    }

    private AvalonGame CreateGameAtAssassination()
    {
        var game = new AvalonGame(5, DefaultRoles(5), startLeader: 0);
        var goodPlayers = Enumerable.Range(0, 5)
            .Where(i => AvalonConfig.GetTeam(game.Roles[i]) == AvalonTeam.Good)
            .ToList();

        int[] missionSizes = { 2, 3, 2 };
        for (int m = 0; m < 3; m++)
        {
            game.StartProposalPhase();
            var team = goodPlayers.Take(missionSizes[m]).ToList();
            game.ProposeTeam(game.CurrentLeaderIndex, team);
            for (int p = 0; p < 5; p++) game.CastVote(p, true);
            foreach (int t in team) game.PlayMissionCard(t, true);
        }

        return game;
    }
}
