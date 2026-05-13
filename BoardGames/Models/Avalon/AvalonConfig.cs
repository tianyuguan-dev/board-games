namespace BoardGames.Models.Avalon;

public static class AvalonConfig
{
    // [playerCount] => (goodCount, evilCount)
    private static readonly Dictionary<int, (int Good, int Evil)> TeamSizes = new()
    {
        [5] = (3, 2),
        [6] = (4, 2),
        [7] = (4, 3),
        [8] = (5, 3),
        [9] = (6, 3),
        [10] = (6, 4),
    };

    // [playerCount] => mission team sizes for missions 1-5
    private static readonly Dictionary<int, int[]> MissionSizes = new()
    {
        [5] = [2, 3, 2, 3, 3],
        [6] = [2, 3, 4, 3, 4],
        [7] = [2, 3, 3, 4, 4],
        [8] = [3, 4, 4, 5, 5],
        [9] = [3, 4, 4, 5, 5],
        [10] = [3, 4, 4, 5, 5],
    };

    public static (int Good, int Evil) GetTeamSizes(int playerCount) => TeamSizes[playerCount];

    public static int GetMissionSize(int playerCount, int missionIndex) => MissionSizes[playerCount][missionIndex];

    public static bool IsValidPlayerCount(int count) => count >= 5 && count <= 10;

    // Mission 4 requires 2 fails for 7+ players
    public static int GetFailsRequired(int playerCount, int missionIndex)
    {
        return (missionIndex == 3 && playerCount >= 7) ? 2 : 1;
    }

    // Default role list per player count
    private static readonly Dictionary<int, List<AvalonRole>> DefaultRoles = new()
    {
        [5] = [AvalonRole.Merlin, AvalonRole.Percival, AvalonRole.LoyalServant,
               AvalonRole.Assassin, AvalonRole.Morgana],
        [6] = [AvalonRole.Merlin, AvalonRole.Percival, AvalonRole.LoyalServant, AvalonRole.LoyalServant,
               AvalonRole.Assassin, AvalonRole.Morgana],
        [7] = [AvalonRole.Merlin, AvalonRole.Percival, AvalonRole.LoyalServant, AvalonRole.LoyalServant,
               AvalonRole.Assassin, AvalonRole.Morgana, AvalonRole.Oberon],
        [8] = [AvalonRole.Merlin, AvalonRole.Percival, AvalonRole.LoyalServant, AvalonRole.LoyalServant, AvalonRole.LoyalServant,
               AvalonRole.Assassin, AvalonRole.Morgana, AvalonRole.MinionOfMordred],
        [9] = [AvalonRole.Merlin, AvalonRole.Percival, AvalonRole.LoyalServant, AvalonRole.LoyalServant, AvalonRole.LoyalServant, AvalonRole.LoyalServant,
               AvalonRole.Assassin, AvalonRole.Morgana, AvalonRole.Mordred],
        [10] = [AvalonRole.Merlin, AvalonRole.Percival, AvalonRole.LoyalServant, AvalonRole.LoyalServant, AvalonRole.LoyalServant, AvalonRole.LoyalServant,
                AvalonRole.Assassin, AvalonRole.Morgana, AvalonRole.Mordred, AvalonRole.Oberon],
    };

    public static List<AvalonRole> GetDefaultRoles(int playerCount)
    {
        return IsValidPlayerCount(playerCount) ? new List<AvalonRole>(DefaultRoles[playerCount]) : new();
    }


    public static AvalonTeam GetTeam(AvalonRole role)
    {
        return role switch
        {
            AvalonRole.Merlin or AvalonRole.Percival or AvalonRole.LoyalServant => AvalonTeam.Good,
            _ => AvalonTeam.Evil
        };
    }
}
