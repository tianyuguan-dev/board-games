using BoardGames.Models.Avalon;

namespace BoardGames.Tests.Models.Avalon;

public class AvalonConfigTests
{
    [Theory]
    [InlineData(5, 3, 2)]
    [InlineData(6, 4, 2)]
    [InlineData(7, 4, 3)]
    [InlineData(8, 5, 3)]
    [InlineData(9, 6, 3)]
    [InlineData(10, 6, 4)]
    public void GetTeamSizes_ReturnsCorrectCounts(int players, int good, int evil)
    {
        var (g, e) = AvalonConfig.GetTeamSizes(players);
        Assert.Equal(good, g);
        Assert.Equal(evil, e);
    }

    [Theory]
    [InlineData(5, 0, 2)]
    [InlineData(5, 1, 3)]
    [InlineData(7, 3, 4)]
    public void GetMissionSize_ReturnsCorrectSize(int players, int mission, int expected)
    {
        Assert.Equal(expected, AvalonConfig.GetMissionSize(players, mission));
    }

    [Theory]
    [InlineData(5, 3, 1)]
    [InlineData(7, 3, 2)] // Mission 4 with 7+ players requires 2 fails
    [InlineData(8, 3, 2)]
    [InlineData(7, 0, 1)]
    public void GetFailsRequired_CorrectForMission4(int players, int mission, int expected)
    {
        Assert.Equal(expected, AvalonConfig.GetFailsRequired(players, mission));
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void IsValidPlayerCount(int count, bool expected)
    {
        Assert.Equal(expected, AvalonConfig.IsValidPlayerCount(count));
    }

    [Theory]
    [InlineData(AvalonRole.Merlin, AvalonTeam.Good)]
    [InlineData(AvalonRole.Percival, AvalonTeam.Good)]
    [InlineData(AvalonRole.LoyalServant, AvalonTeam.Good)]
    [InlineData(AvalonRole.Assassin, AvalonTeam.Evil)]
    [InlineData(AvalonRole.Morgana, AvalonTeam.Evil)]
    [InlineData(AvalonRole.Mordred, AvalonTeam.Evil)]
    [InlineData(AvalonRole.Oberon, AvalonTeam.Evil)]
    [InlineData(AvalonRole.MinionOfMordred, AvalonTeam.Evil)]
    public void GetTeam_ReturnsCorrectTeam(AvalonRole role, AvalonTeam expected)
    {
        Assert.Equal(expected, AvalonConfig.GetTeam(role));
    }
}
