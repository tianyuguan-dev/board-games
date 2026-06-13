using BoardGames.Models.Avalon;

namespace BoardGames.Tests.Models.Avalon;

public class AvalonGameEdgeCaseTests
{
    [Fact]
    public void Constructor_Throws_WhenRoleConfigCountMismatch()
    {
        var roles = new List<AvalonRole> { AvalonRole.Merlin, AvalonRole.Assassin };
        Assert.Throws<ArgumentException>(() => new AvalonGame(5, roles));
    }

    [Fact]
    public void AllVotesIn_ReturnsFalse_WhenNoCurrentProposal()
    {
        var roles = new List<AvalonRole>
        {
            AvalonRole.Merlin, AvalonRole.Percival, AvalonRole.LoyalServant,
            AvalonRole.Morgana, AvalonRole.Assassin
        };
        var game = new AvalonGame(5, roles, shuffleRoles: false);
        // Still in NightReveal phase — no CurrentProposal yet
        Assert.False(game.AllVotesIn());
    }

    [Fact]
    public void AllMissionCardsIn_ReturnsFalse_WhenNoCurrentProposal()
    {
        var roles = new List<AvalonRole>
        {
            AvalonRole.Merlin, AvalonRole.Percival, AvalonRole.LoyalServant,
            AvalonRole.Morgana, AvalonRole.Assassin
        };
        var game = new AvalonGame(5, roles, shuffleRoles: false);
        Assert.False(game.AllMissionCardsIn());
    }

    [Fact]
    public void GetMissionPlayersPlayed_StartsEmpty()
    {
        var roles = new List<AvalonRole>
        {
            AvalonRole.Merlin, AvalonRole.Percival, AvalonRole.LoyalServant,
            AvalonRole.Morgana, AvalonRole.Assassin
        };
        var game = new AvalonGame(5, roles, shuffleRoles: false);
        Assert.Empty(game.GetMissionPlayersPlayed());
    }

    /// <summary>
    /// Custom role config with no Assassin: 3 mission wins by Good → Game over immediately,
    /// no Assassination phase. This exercises a branch unreachable in default configs.
    /// </summary>
    [Fact]
    public void GoodWinsWithoutAssassin_EndsGameDirectly()
    {
        var roles = new List<AvalonRole>
        {
            AvalonRole.Merlin, AvalonRole.Percival, AvalonRole.LoyalServant,
            AvalonRole.Morgana, AvalonRole.Mordred  // no Assassin
        };
        var game = new AvalonGame(5, roles, shuffleRoles: false);
        SimulateGoodWins(game);
        Assert.Equal(AvalonPhase.GameOver, game.Phase);
        Assert.Equal(GameWinner.Good, game.Winner);
        Assert.Contains("3 missions completed", game.WinReason);
    }

    [Fact]
    public void EvilWinsWithoutAssassin_EndsGameDirectly()
    {
        var roles = new List<AvalonRole>
        {
            AvalonRole.Merlin, AvalonRole.Percival, AvalonRole.LoyalServant,
            AvalonRole.Morgana, AvalonRole.Mordred  // no Assassin
        };
        var game = new AvalonGame(5, roles, shuffleRoles: false);
        SimulateEvilWins(game);
        Assert.Equal(AvalonPhase.GameOver, game.Phase);
        Assert.Equal(GameWinner.Evil, game.Winner);
        Assert.Contains("3 missions failed", game.WinReason);
    }

    // --- Helpers ---

    /// <summary>Runs 3 missions where good plays all good teams → 3 successes → Good wins.</summary>
    private static void SimulateGoodWins(AvalonGame game)
    {
        game.StartProposalPhase();
        for (int mission = 0; mission < 3; mission++)
        {
            int leader = game.CurrentLeaderIndex;
            int teamSize = game.GetRequiredTeamSize();
            // Pick the first `teamSize` good seats (Merlin=0, Percival=1, LoyalServant=2)
            var team = new[] { 0, 1, 2 }.Take(teamSize).ToList();
            game.ProposeTeam(leader, team);
            for (int s = 0; s < 5; s++) game.CastVote(s, true);
            foreach (var s in team) game.PlayMissionCard(s, true);
        }
    }

    /// <summary>Runs 3 missions where evil sabotages → 3 fails → Evil wins.</summary>
    private static void SimulateEvilWins(AvalonGame game)
    {
        game.StartProposalPhase();
        for (int mission = 0; mission < 3; mission++)
        {
            int leader = game.CurrentLeaderIndex;
            int teamSize = game.GetRequiredTeamSize();
            // Include at least one evil seat (Morgana=3, Mordred=4)
            var team = new[] { 3, 0, 1 }.Take(teamSize).ToList();
            game.ProposeTeam(leader, team);
            for (int s = 0; s < 5; s++) game.CastVote(s, true);
            foreach (var s in team)
            {
                bool isGood = AvalonConfig.GetTeam(game.Roles[s]) == AvalonTeam.Good;
                game.PlayMissionCard(s, isGood);  // good plays success; evil plays fail
            }
        }
    }
}
