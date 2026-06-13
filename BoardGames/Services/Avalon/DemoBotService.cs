using BoardGames.Data;
using BoardGames.Dtos.Avalon;
using BoardGames.Hubs.Avalon;
using BoardGames.Models;
using BoardGames.Models.Avalon;
using Microsoft.AspNetCore.SignalR;

namespace BoardGames.Services.Avalon;

/// <summary>
/// Drives the scripted Avalon solo demo for guest users.
/// Demo layout (5-player):
///   Seat 0: Bot Alice    -> Merlin
///   Seat 1: Bot Bob      -> Morgana (start leader)
///   Seat 2: Guest        -> Percival
///   Seat 3: Bot Charlie  -> LoyalServant
///   Seat 4: Bot Dave     -> Assassin
///
/// Scripted arc:
///   1. NightReveal: bots auto-confirm, wait for guest.
///   2. Mission 1 proposal 1: Morgana proposes [1, 4] (obviously evil), all bots reject -> rejected.
///   3. Mission 1 proposal 2: leader = guest, guest picks team, all bots approve -> approved.
///   4. Mission 1 cards: bots play according to alignment (good = success, evil = fail).
///   5. After mission 1 resolves, BeginEarlyAssassination triggers.
///   6. Assassin targets guest (Percival, seat 2). Percival is not Merlin -> Good Wins.
/// </summary>
public class DemoBotService(IServiceScopeFactory scopeFactory, ILogger<DemoBotService> logger) : IDemoBotService
{
    private const int GuestSeat = 2;
    private const int AssassinSeat = 4;

    public Task TriggerNextBotActions(AvalonRoom room, IHubContext<AvalonHub> hubContext)
    {
        if (!room.IsDemo || room.Game == null) return Task.CompletedTask;
        var game = room.Game;

        switch (game.Phase)
        {
            case AvalonPhase.NightReveal:
                _ = ScheduleAsync(800, () => ConfirmBotsNightReveal(room, hubContext));
                break;

            case AvalonPhase.TeamProposal:
                // Mission 1 just resolved -> early assassination drama
                if (game.CurrentMissionIndex == 1 && !game.EarlyAssassination)
                {
                    _ = ScheduleAsync(1500, () => TriggerEarlyAssassination(room, hubContext));
                }
                else if (game.CurrentLeaderIndex != GuestSeat)
                {
                    _ = ScheduleAsync(2000, () => BotProposeTeam(room, hubContext));
                }
                break;

            case AvalonPhase.TeamVote:
                _ = ScheduleAsync(2000, () => BotsVote(room, hubContext));
                break;

            case AvalonPhase.Mission:
                _ = ScheduleAsync(2000, () => BotsPlayMissionCards(room, hubContext));
                break;

            case AvalonPhase.Assassination:
                _ = ScheduleAsync(1500, () => BotAssassinate(room, hubContext));
                break;
        }
        return Task.CompletedTask;
    }

    // --- Bot actions ---

    private async Task ConfirmBotsNightReveal(AvalonRoom room, IHubContext<AvalonHub> hubContext)
    {
        await WithLock(room, async () =>
        {
            if (room.Game == null || room.Game.Phase != AvalonPhase.NightReveal) return;

            foreach (var (connId, seat) in room.Players)
            {
                if (seat != GuestSeat && connId.StartsWith("BOT:"))
                    room.NightConfirmedPlayers.Add(connId);
            }

            // If guest already confirmed, this completes the phase
            if (room.NightConfirmedPlayers.Count >= room.Players.Count)
            {
                room.NightConfirmedPlayers.Clear();
                room.Game.StartProposalPhase();
                await BroadcastAndContinue(room, hubContext);
            }
            else
            {
                // Otherwise notify guest of progress
                var confirmed = room.NightConfirmedPlayers
                    .Select(c => room.Players.GetValueOrDefault(c, -1))
                    .Where(s => s >= 0)
                    .ToList();
                await hubContext.Clients.Group(room.RoomId).SendAsync("NightRevealProgress", confirmed);
            }
        });
    }

    private async Task BotProposeTeam(AvalonRoom room, IHubContext<AvalonHub> hubContext)
    {
        await WithLock(room, async () =>
        {
            if (room.Game == null || room.Game.Phase != AvalonPhase.TeamProposal) return;
            if (room.Game.CurrentLeaderIndex == GuestSeat) return;

            int teamSize = room.Game.GetRequiredTeamSize();
            var team = PickBotProposedTeam(room.Game.CurrentLeaderIndex, teamSize, room.Game.PlayerCount);
            room.Game.ProposeTeam(room.Game.CurrentLeaderIndex, team);
            await BroadcastAndContinue(room, hubContext);
        });
    }

    private List<int> PickBotProposedTeam(int leaderSeat, int teamSize, int playerCount)
    {
        // Morgana (seat 1) leader: propose obviously evil team [1, 4] for drama.
        if (leaderSeat == 1 && teamSize == 2) return new List<int> { 1, 4 };

        // Generic fallback (shouldn't fire in our scripted demo): leader + sequential seats.
        var team = new List<int> { leaderSeat };
        for (int i = 0; team.Count < teamSize && i < playerCount; i++)
        {
            if (!team.Contains(i)) team.Add(i);
        }
        return team.Take(teamSize).ToList();
    }

    private async Task BotsVote(AvalonRoom room, IHubContext<AvalonHub> hubContext)
    {
        await WithLock(room, async () =>
        {
            if (room.Game == null || room.Game.Phase != AvalonPhase.TeamVote) return;

            bool approve = ShouldBotsApprove(room.Game);
            for (int seat = 0; seat < room.Game.PlayerCount; seat++)
            {
                if (seat == GuestSeat) continue;
                // CastVote is idempotent for already-voted players.
                room.Game.CastVote(seat, approve);
            }
            await BroadcastAndContinue(room, hubContext);
        });
    }

    private static bool ShouldBotsApprove(AvalonGame game)
    {
        // MissionHistory[currentMission] contains already-resolved proposals.
        // 1st proposal of mission 1 (history count 0): bots reject for drama.
        // 2nd+ proposal: bots approve (lets the demo move forward after the guest takes over as leader).
        return game.MissionHistory[game.CurrentMissionIndex].Count >= 1;
    }

    private async Task BotsPlayMissionCards(AvalonRoom room, IHubContext<AvalonHub> hubContext)
    {
        await WithLock(room, async () =>
        {
            if (room.Game == null || room.Game.Phase != AvalonPhase.Mission) return;
            if (room.Game.CurrentProposal == null) return;

            foreach (var seat in room.Game.CurrentProposal.Team)
            {
                if (seat == GuestSeat) continue;
                var role = room.Game.Roles[seat];
                bool success = AvalonConfig.GetTeam(role) == AvalonTeam.Good;
                room.Game.PlayMissionCard(seat, success);
            }
            await BroadcastAndContinue(room, hubContext);
        });
    }

    private async Task TriggerEarlyAssassination(AvalonRoom room, IHubContext<AvalonHub> hubContext)
    {
        await WithLock(room, async () =>
        {
            if (room.Game == null || room.Game.EarlyAssassination) return;
            if (room.Game.Phase == AvalonPhase.GameOver) return;

            int assassinIdx = room.Game.GetAssassinIndex();
            if (assassinIdx < 0) return;

            room.Game.BeginEarlyAssassination(assassinIdx);
            await BroadcastAndContinue(room, hubContext);
        });
    }

    private async Task BotAssassinate(AvalonRoom room, IHubContext<AvalonHub> hubContext)
    {
        await WithLock(room, async () =>
        {
            if (room.Game == null || room.Game.Phase != AvalonPhase.Assassination) return;
            // Assassin always targets Percival (guest). Percival is not Merlin, so Good wins -> guest wins.
            room.Game.Assassinate(AssassinSeat, GuestSeat);
            await BroadcastAndContinue(room, hubContext);
        });
    }

    // --- Broadcast + settle ---

    private async Task BroadcastAndContinue(AvalonRoom room, IHubContext<AvalonHub> hubContext)
    {
        if (room.Game == null) return;

        // Push updated state to real (non-bot) players
        foreach (var (connId, seatIndex) in room.Players)
        {
            if (connId.StartsWith("BOT:")) continue;
            var dto = AvalonGameStateDto.Create(room.Game, seatIndex, room.GamePlayerNames);
            await hubContext.Clients.Client(connId).SendAsync("GameState", dto);
        }

        if (room.Game.Phase == AvalonPhase.GameOver)
        {
            await SettleDemoGame(room, hubContext);
        }
        else
        {
            // Continue the script
            await TriggerNextBotActions(room, hubContext);
        }
    }

    private async Task SettleDemoGame(AvalonRoom room, IHubContext<AvalonHub> hubContext)
    {
        if (!room.TrySetSettled()) return;

        using var scope = scopeFactory.CreateScope();
        var balanceRepo = scope.ServiceProvider.GetRequiredService<IGameBalanceRepository>();

        var game = room.Game!;
        bool bonusKill = game.BonusAssassination
                         && game.AssassinTarget.HasValue
                         && game.Roles[game.AssassinTarget.Value] == AvalonRole.Merlin;
        decimal points = bonusKill ? 2 : 1;
        bool shielded = game.Winner == GameWinner.Good
                        && game.AssassinTarget.HasValue
                        && game.Roles[game.AssassinTarget.Value] != AvalonRole.Merlin
                        && game.Roles[game.AssassinTarget.Value] != AvalonRole.Percival;

        for (int i = 0; i < game.PlayerCount; i++)
        {
            var userId = room.GamePlayerUserIds.ElementAtOrDefault(i);
            if (userId <= 0) continue; // skip bots

            var team = AvalonConfig.GetTeam(game.Roles[i]);
            bool isWinner = (team == AvalonTeam.Good && game.Winner == GameWinner.Good)
                            || (team == AvalonTeam.Evil && game.Winner == GameWinner.Evil);
            decimal delta = isWinner ? points : -points;
            if (shielded && game.AssassinTarget.HasValue && i == game.AssassinTarget.Value)
                delta += 0.5m;
            await balanceRepo.UpdateBalance(userId, GameType.Avalon, delta);
        }

        // Push balance update to guest
        foreach (var (connId, _) in room.Players)
        {
            if (connId.StartsWith("BOT:")) continue;
            var uid = room.PlayerUserIds.GetValueOrDefault(connId);
            if (uid > 0)
            {
                var balance = await balanceRepo.GetOrCreate(uid, GameType.Avalon);
                await hubContext.Clients.Client(connId).SendAsync("BalanceUpdate", balance.Balance);
            }
        }

        // Deliberately do not persist history: bots have no UserId and history would be incomplete + spammy.
    }

    // --- Helpers ---

    private async Task ScheduleAsync(int delayMs, Func<Task> action)
    {
        await Task.Delay(delayMs);
        try { await action(); }
        catch (Exception ex) { logger.LogError(ex, "Demo bot action failed"); }
    }

    private static async Task WithLock(AvalonRoom room, Func<Task> action)
    {
        await room.Lock.WaitAsync();
        try { await action(); }
        finally { room.Lock.Release(); }
    }
}
