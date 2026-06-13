using BoardGames.Dtos.Avalon;
using BoardGames.Models.Avalon;

namespace BoardGames.Tests.Dtos.Avalon;

public class AvalonGameHistoryDtosTests
{
    private static AvalonGameHistory MakeGame()
    {
        var game = new AvalonGameHistory
        {
            Id = 7,
            RoomId = "ROOM7",
            PlayerCount = 5,
            MaxRejects = 4,
            Winner = GameWinner.Good,
            WinReason = "3 missions completed",
            BonusAssassination = false,
            EarlyAssassination = false,
            AssassinTargetSeat = 4,
            StartedAt = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc),
            EndedAt   = new DateTime(2026, 6, 1, 12, 14, 30, DateTimeKind.Utc),
        };
        game.Players = new List<AvalonGamePlayer>
        {
            new() { SeatIndex = 0, UserId = 100, Nickname = "Alice",   Role = AvalonRole.Merlin,       IsWinner = true,  BalanceDelta = 1m },
            new() { SeatIndex = 1, UserId = 101, Nickname = "Bob",     Role = AvalonRole.Morgana,      IsWinner = false, BalanceDelta = -1m },
            new() { SeatIndex = 2, UserId = 102, Nickname = "Charlie", Role = AvalonRole.Percival,     IsWinner = true,  BalanceDelta = 1m },
            new() { SeatIndex = 3, UserId = 103, Nickname = "Dave",    Role = AvalonRole.LoyalServant, IsWinner = true,  BalanceDelta = 1m },
            new() { SeatIndex = 4, UserId = 104, Nickname = "Eve",     Role = AvalonRole.Assassin,     IsWinner = false, BalanceDelta = -1m },
        };
        // Mission 1 with 2 proposals: 1st rejected, 2nd approved + success
        var p1 = new AvalonGameProposal
        {
            MissionIndex = 0, ProposalIndex = 0, LeaderSeatIndex = 0,
            TeamSeats = new[] { 0, 1 }, Approved = false,
            MissionResult = MissionOutcome.Pending,
        };
        p1.Votes = new List<AvalonGameVote>
        {
            new() { VoterSeatIndex = 0, Approve = true },
            new() { VoterSeatIndex = 1, Approve = false },
            new() { VoterSeatIndex = 2, Approve = false },
            new() { VoterSeatIndex = 3, Approve = false },
            new() { VoterSeatIndex = 4, Approve = false },
        };
        var p2 = new AvalonGameProposal
        {
            MissionIndex = 0, ProposalIndex = 1, LeaderSeatIndex = 1,
            TeamSeats = new[] { 0, 2 }, Approved = true,
            SuccessCount = 2, FailCount = 0,
            MissionResult = MissionOutcome.Success,
        };
        p2.Votes = new List<AvalonGameVote>
        {
            new() { VoterSeatIndex = 0, Approve = true },
            new() { VoterSeatIndex = 1, Approve = true },
            new() { VoterSeatIndex = 2, Approve = true },
            new() { VoterSeatIndex = 3, Approve = true },
            new() { VoterSeatIndex = 4, Approve = true },
        };
        game.Proposals = new List<AvalonGameProposal> { p1, p2 };
        return game;
    }

    // ---- Summary DTO ----

    [Fact]
    public void Summary_From_FillsMyPerspective_WhenParticipant()
    {
        var dto = AvalonGameSummaryDto.From(MakeGame(), myUserId: 101); // Bob (Morgana, lost)
        Assert.Equal(7, dto.Id);
        Assert.Equal("Evil", "Evil"); // sanity
        Assert.Equal("Good", dto.Winner);
        Assert.Equal("Morgana", dto.MyRole);
        Assert.False(dto.MyIsWinner);
        Assert.Equal(-1m, dto.MyBalanceDelta);
    }

    [Fact]
    public void Summary_From_LeavesMyFieldsEmpty_WhenNotParticipant()
    {
        var dto = AvalonGameSummaryDto.From(MakeGame(), myUserId: 999);
        Assert.Equal("", dto.MyRole);
        Assert.False(dto.MyIsWinner);
        Assert.Equal(0m, dto.MyBalanceDelta);
    }

    [Fact]
    public void Summary_From_CopiesMetadata()
    {
        var dto = AvalonGameSummaryDto.From(MakeGame(), myUserId: 100);
        Assert.Equal(5, dto.PlayerCount);
        Assert.Equal("3 missions completed", dto.WinReason);
    }

    // ---- Detail DTO ----

    [Fact]
    public void Detail_From_CopiesAllMetadata()
    {
        var dto = AvalonGameDetailDto.From(MakeGame(), myUserId: 100);
        Assert.Equal(7, dto.Id);
        Assert.Equal("ROOM7", dto.RoomId);
        Assert.Equal(5, dto.PlayerCount);
        Assert.Equal(4, dto.MaxRejects);
        Assert.Equal("Good", dto.Winner);
        Assert.Equal(4, dto.AssassinTargetSeat);
    }

    [Fact]
    public void Detail_From_ComputesDurationSeconds()
    {
        var dto = AvalonGameDetailDto.From(MakeGame(), myUserId: 100);
        Assert.Equal(14 * 60 + 30, dto.DurationSeconds); // 14m 30s
    }

    [Fact]
    public void Detail_From_FillsMyPerspective()
    {
        var dto = AvalonGameDetailDto.From(MakeGame(), myUserId: 100); // Alice = Merlin, winner
        Assert.Equal(0, dto.MySeatIndex);
        Assert.Equal("Merlin", dto.MyRole);
        Assert.True(dto.MyIsWinner);
        Assert.Equal(1m, dto.MyBalanceDelta);
    }

    [Fact]
    public void Detail_From_LeavesMyFieldsDefault_WhenNotParticipant()
    {
        var dto = AvalonGameDetailDto.From(MakeGame(), myUserId: 999);
        Assert.Equal(-1, dto.MySeatIndex);
        Assert.Equal("", dto.MyRole);
        Assert.False(dto.MyIsWinner);
        Assert.Equal(0m, dto.MyBalanceDelta);
    }

    [Fact]
    public void Detail_From_OrdersPlayersBySeat()
    {
        // Reverse player order in source, expect ordered output
        var game = MakeGame();
        game.Players.Reverse();
        var dto = AvalonGameDetailDto.From(game, myUserId: 100);
        for (int i = 0; i < dto.Players.Count; i++)
            Assert.Equal(i, dto.Players[i].SeatIndex);
    }

    [Fact]
    public void Detail_From_GroupsProposalsByMission_AndOrders()
    {
        var dto = AvalonGameDetailDto.From(MakeGame(), myUserId: 100);
        // Only mission 0 has proposals in test data
        Assert.Single(dto.Missions);
        Assert.Equal(0, dto.Missions[0].MissionIndex);
        Assert.Equal(2, dto.Missions[0].Proposals.Count);
        Assert.Equal(0, dto.Missions[0].Proposals[0].ProposalIndex);
        Assert.Equal(1, dto.Missions[0].Proposals[1].ProposalIndex);
    }

    [Fact]
    public void Detail_From_PreservesVotesAndMissionResult()
    {
        var dto = AvalonGameDetailDto.From(MakeGame(), myUserId: 100);
        var approved = dto.Missions[0].Proposals[1];
        Assert.True(approved.Approved);
        Assert.Equal(5, approved.Votes.Count);
        Assert.All(approved.Votes, v => Assert.True(v.Approve));
        Assert.Equal("Success", approved.MissionResult);
        Assert.Equal(2, approved.SuccessCount);
    }
}
