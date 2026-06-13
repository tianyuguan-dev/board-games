using BoardGames.Data;
using BoardGames.Models;
using BoardGames.Models.Avalon;
using Microsoft.EntityFrameworkCore;

namespace BoardGames.Tests.Data;

public class AvalonGameHistoryRepositoryTests
{
    private AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("Hist_" + Guid.NewGuid())
            .Options);

    /// <summary>
    /// Builds a 5-player Avalon room that has just played to GameOver with Good winning.
    /// Seat layout: Merlin, Morgana, Percival, LoyalServant, Assassin (fixed via shuffleRoles=false).
    /// </summary>
    private static AvalonRoom BuildFinishedRoom(int[] userIds)
    {
        var room = new AvalonRoom("ROOM1", 5)
        {
            RoleConfig = new List<AvalonRole>
            {
                AvalonRole.Merlin, AvalonRole.Morgana, AvalonRole.Percival,
                AvalonRole.LoyalServant, AvalonRole.Assassin,
            },
            GamePlayerNames = new List<string> { "Alice", "Bob", "Charlie", "Dave", "Eve" },
            GamePlayerUserIds = userIds.ToList(),
        };
        room.Game = new AvalonGame(5, room.RoleConfig, startLeader: 0, maxRejects: 4, shuffleRoles: false);
        room.Game.StartProposalPhase();

        // Mission 1: leader 0 proposes [0, 2] (Merlin + Percival = both good)
        room.Game.ProposeTeam(0, new List<int> { 0, 2 });
        // All approve
        for (int i = 0; i < 5; i++) room.Game.CastVote(i, true);
        // Both good → both play success
        room.Game.PlayMissionCard(0, true);
        room.Game.PlayMissionCard(2, true);

        // Force early assassination to end the game quickly
        room.Game.BeginEarlyAssassination(4); // Assassin at seat 4
        room.Game.Assassinate(4, 3);          // targets LoyalServant → wrong target → Good wins

        return room;
    }

    [Fact]
    public async Task PersistGame_WritesGamePlayersProposalsAndVotes()
    {
        using var db = NewDb();
        var repo = new AvalonGameHistoryRepository(db);

        var room = BuildFinishedRoom(new[] { 11, 12, 13, 14, 15 });
        await repo.PersistGame(room);

        Assert.Equal(1, await db.AvalonGameHistories.CountAsync());
        Assert.Equal(5, await db.AvalonGamePlayers.CountAsync());
        Assert.True(await db.AvalonGameProposals.CountAsync() >= 1);
        Assert.True(await db.AvalonGameVotes.CountAsync() >= 5);
    }

    [Fact]
    public async Task PersistGame_SkipsZeroUserIds_ForBotsAndGuests()
    {
        using var db = NewDb();
        var repo = new AvalonGameHistoryRepository(db);

        // Seat 0 is a guest (userId=0); rest are real users
        var room = BuildFinishedRoom(new[] { 0, 12, 13, 14, 15 });
        await repo.PersistGame(room);

        var players = await db.AvalonGamePlayers.ToListAsync();
        Assert.Equal(4, players.Count);
        Assert.DoesNotContain(players, p => p.UserId == 0);
    }

    [Fact]
    public async Task PersistGame_RecordsWinnerAndAssassinTarget()
    {
        using var db = NewDb();
        var repo = new AvalonGameHistoryRepository(db);

        var room = BuildFinishedRoom(new[] { 11, 12, 13, 14, 15 });
        await repo.PersistGame(room);

        var hist = await db.AvalonGameHistories.FirstAsync();
        Assert.Equal(GameWinner.Good, hist.Winner);
        Assert.Equal(3, hist.AssassinTargetSeat);   // assassin chose seat 3 (LoyalServant, wrong)
        Assert.True(hist.EarlyAssassination);
    }

    [Fact]
    public async Task PersistGame_RecordsBalanceDeltas_ForWinnersAndLosers()
    {
        using var db = NewDb();
        var repo = new AvalonGameHistoryRepository(db);

        var room = BuildFinishedRoom(new[] { 11, 12, 13, 14, 15 });
        await repo.PersistGame(room);

        var players = await db.AvalonGamePlayers.OrderBy(p => p.SeatIndex).ToListAsync();
        // Good wins → seats 0,2,3 (Merlin, Percival, Loyal) winners; 1,4 (Morgana, Assassin) losers
        Assert.True(players[0].IsWinner);
        Assert.True(players[2].IsWinner);
        Assert.True(players[3].IsWinner);  // LoyalServant
        Assert.False(players[1].IsWinner);
        Assert.False(players[4].IsWinner);
        // Winners +1, losers -1 (no bonus kill since Merlin survived)
        Assert.Equal(1m, players[0].BalanceDelta);
        Assert.Equal(-1m, players[1].BalanceDelta);
        // Assassin missed Merlin AND missed Percival, so target (LoyalServant seat 3) gets the +0.5 shield
        Assert.Equal(1.5m, players[3].BalanceDelta);
    }

    [Fact]
    public async Task GetMyRecentGames_ReturnsOnlyParticipantGames()
    {
        using var db = NewDb();
        var repo = new AvalonGameHistoryRepository(db);

        var roomA = BuildFinishedRoom(new[] { 11, 12, 13, 14, 15 });
        await repo.PersistGame(roomA);
        var roomB = BuildFinishedRoom(new[] { 21, 22, 23, 24, 25 });
        await repo.PersistGame(roomB);

        var forUser11 = await repo.GetMyRecentGames(11, limit: 10, offset: 0);
        Assert.Single(forUser11);

        var forUser99 = await repo.GetMyRecentGames(99, limit: 10, offset: 0);
        Assert.Empty(forUser99);
    }

    [Fact]
    public async Task GetGameDetail_ReturnsNull_IfUserDidNotParticipate()
    {
        using var db = NewDb();
        var repo = new AvalonGameHistoryRepository(db);

        var room = BuildFinishedRoom(new[] { 11, 12, 13, 14, 15 });
        await repo.PersistGame(room);
        var gameId = (await db.AvalonGameHistories.FirstAsync()).Id;

        var asOutsider = await repo.GetGameDetail(gameId, userId: 999);
        Assert.Null(asOutsider);

        var asParticipant = await repo.GetGameDetail(gameId, userId: 11);
        Assert.NotNull(asParticipant);
        Assert.Equal(5, asParticipant!.Players.Count);
    }
}
