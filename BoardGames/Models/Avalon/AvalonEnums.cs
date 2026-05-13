namespace BoardGames.Models.Avalon;

public enum AvalonRole
{
    // Good
    Merlin,
    Percival,
    LoyalServant,
    // Evil
    Assassin,
    Morgana,
    Mordred,
    Oberon,
    MinionOfMordred
}

public enum AvalonTeam
{
    Good,
    Evil
}

public enum AvalonPhase
{
    WaitingToStart,
    NightReveal,
    TeamProposal,
    TeamVote,
    Mission,
    Assassination,
    GameOver
}

public enum MissionOutcome
{
    Pending,
    Success,
    Fail
}

public enum GameWinner
{
    None,
    Good,
    Evil
}
