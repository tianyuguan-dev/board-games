using BoardGames.Models.Avalon;
using Microsoft.EntityFrameworkCore;

namespace BoardGames.Data;

public class AvalonGameHistoryRepository(AppDbContext db) : IAvalonGameHistoryRepository
{
    public async Task PersistGame(AvalonRoom room)
    {
        var game = room.Game!;
        if (game.Phase != AvalonPhase.GameOver) return;

        // Mirror SettleGame scoring so history records carry the same delta the user saw.
        bool bonusKill = game.BonusAssassination
                         && game.AssassinTarget.HasValue
                         && game.Roles[game.AssassinTarget.Value] == AvalonRole.Merlin;
        decimal points = bonusKill ? 2 : 1;
        bool shielded = game.Winner == GameWinner.Good
                        && game.AssassinTarget.HasValue
                        && game.Roles[game.AssassinTarget.Value] != AvalonRole.Merlin
                        && game.Roles[game.AssassinTarget.Value] != AvalonRole.Percival;

        var history = new AvalonGameHistory
        {
            RoomId = room.RoomId,
            PlayerCount = game.PlayerCount,
            MaxRejects = game.MaxConsecutiveRejects,
            Winner = game.Winner,
            WinReason = game.WinReason,
            BonusAssassination = game.BonusAssassination,
            EarlyAssassination = game.EarlyAssassination,
            AssassinTargetSeat = game.AssassinTarget,
            StartedAt = game.StartedAt,
            EndedAt = DateTime.UtcNow
        };

        for (int i = 0; i < game.PlayerCount; i++)
        {
            var userId = room.GamePlayerUserIds.ElementAtOrDefault(i);
            if (userId == 0) continue;

            var team = AvalonConfig.GetTeam(game.Roles[i]);
            bool isWinner = (team == AvalonTeam.Good && game.Winner == GameWinner.Good)
                            || (team == AvalonTeam.Evil && game.Winner == GameWinner.Evil);
            decimal delta = isWinner ? points : -points;
            if (shielded && game.AssassinTarget.HasValue && i == game.AssassinTarget.Value)
                delta += 0.5m;

            history.Players.Add(new AvalonGamePlayer
            {
                UserId = userId,
                SeatIndex = i,
                Nickname = room.GamePlayerNames.ElementAtOrDefault(i) ?? "Player",
                Role = game.Roles[i],
                IsWinner = isWinner,
                BalanceDelta = delta
            });
        }

        for (int missionIdx = 0; missionIdx < game.MissionHistory.Count; missionIdx++)
        {
            var proposals = game.MissionHistory[missionIdx];
            for (int proposalIdx = 0; proposalIdx < proposals.Count; proposalIdx++)
            {
                var p = proposals[proposalIdx];
                var entity = new AvalonGameProposal
                {
                    MissionIndex = missionIdx,
                    ProposalIndex = proposalIdx,
                    LeaderSeatIndex = p.LeaderIndex,
                    TeamSeats = p.Team.ToArray(),
                    Approved = p.Approved,
                    SuccessCount = p.SuccessCount,
                    FailCount = p.FailCount,
                    MissionResult = p.MissionResult
                };
                foreach (var (voterSeat, approve) in p.Votes)
                {
                    entity.Votes.Add(new AvalonGameVote
                    {
                        VoterSeatIndex = voterSeat,
                        Approve = approve
                    });
                }
                history.Proposals.Add(entity);
            }
        }

        db.AvalonGameHistories.Add(history);
        await db.SaveChangesAsync();
    }

    public async Task<List<AvalonGameHistory>> GetMyRecentGames(int userId, int limit, int offset)
    {
        // Find game IDs the user participated in, then load those games with their players (for opponent list + my role).
        var gameIds = await db.AvalonGamePlayers
            .Where(p => p.UserId == userId)
            .Select(p => p.GameId)
            .Distinct()
            .ToListAsync();

        return await db.AvalonGameHistories
            .Where(g => gameIds.Contains(g.Id))
            .OrderByDescending(g => g.EndedAt)
            .Skip(offset)
            .Take(limit)
            .Include(g => g.Players)
            .AsSplitQuery()
            .ToListAsync();
    }

    public async Task<AvalonGameHistory?> GetGameDetail(int gameId, int userId)
    {
        bool participated = await db.AvalonGamePlayers
            .AnyAsync(p => p.GameId == gameId && p.UserId == userId);
        if (!participated) return null;

        return await GetGameDetailById(gameId);
    }

    public async Task<AvalonGameHistory?> GetGameDetailById(int gameId)
    {
        return await db.AvalonGameHistories
            .Where(g => g.Id == gameId)
            .Include(g => g.Players)
            .Include(g => g.Proposals.OrderBy(p => p.MissionIndex).ThenBy(p => p.ProposalIndex))
                .ThenInclude(p => p.Votes.OrderBy(v => v.VoterSeatIndex))
            .AsSplitQuery()
            .FirstOrDefaultAsync();
    }
}
