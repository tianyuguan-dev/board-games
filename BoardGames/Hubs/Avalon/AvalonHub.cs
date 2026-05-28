using System.Security.Claims;
using BoardGames.Data;
using BoardGames.Dtos.Avalon;
using BoardGames.Models;
using BoardGames.Models.Avalon;
using BoardGames.Services.Avalon;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BoardGames.Hubs.Avalon;

[Authorize]
public class AvalonHub(IAvalonRoomManager roomManager, IUserRepository userRepository, IGameBalanceRepository balanceRepository, IHubContext<AvalonHub> hubContext, IServiceScopeFactory scopeFactory) : Hub
{
    // Serialize all operations on a single room. Acquire at public entry points only;
    // private helpers below assume the caller already holds the lock (no re-entry -> no deadlock).
    private static async Task WithLock(AvalonRoom room, Func<Task> body)
    {
        await room.Lock.WaitAsync();
        try { await body(); }
        finally { room.Lock.Release(); }
    }

    private static async Task<T> WithLock<T>(AvalonRoom room, Func<Task<T>> body)
    {
        await room.Lock.WaitAsync();
        try { return await body(); }
        finally { room.Lock.Release(); }
    }

    public override async Task OnConnectedAsync()
    {
        var user = await userRepository.FindById(GetUserId());
        if (user != null) { user.LastActiveAt = DateTime.UtcNow; await userRepository.Update(user); }
        await base.OnConnectedAsync();
    }

    private int GetUserId() =>
        int.Parse(Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    private async Task<string> GetNickname()
    {
        var user = await userRepository.FindById(GetUserId());
        if (user == null) return "Anonymous";
        return string.IsNullOrEmpty(user.Nickname) ? user.Username : user.Nickname;
    }

    // Assumes the room lock is held by the caller.
    private async Task BroadcastRoomPlayers(string roomId)
    {
        var room = roomManager.GetRoom(roomId);
        if (room == null) return;
        var players = room.Players
            .OrderBy(p => p.Value)
            .Select(p => new
            {
                Nickname = room.PlayerNicknames.GetValueOrDefault(p.Key, "Player " + p.Value),
                IsReady = room.ReadyPlayers.Contains(p.Key),
                IsHost = p.Key == room.HostConnectionId,
                SeatIndex = p.Value
            })
            .ToList();
        foreach (var connId in room.Players.Keys)
        {
            await Clients.Client(connId).SendAsync("RoomUpdate", new
            {
                Players = players,
                IsHost = connId == room.HostConnectionId,
                RoleConfig = room.RoleConfig.Select(r => r.ToString()).ToList(),
                MaxRejects = room.MaxRejects
            });
        }
    }

    // Assumes the room lock is held by the caller.
    private async Task SendGameStateToAll(AvalonRoom room)
    {
        var game = room.Game!;
        foreach (var (connId, seatIndex) in room.Players)
        {
            if (room.SeatToConnection.ContainsValue(connId))
            {
                var dto = AvalonGameStateDto.Create(game, seatIndex, room.GamePlayerNames);
                await Clients.Client(connId).SendAsync("GameState", dto);
            }
        }

        // Settle scores and refresh room state on game over
        if (game.Phase == AvalonPhase.GameOver)
        {
            await SettleGame(room);
            await BroadcastRoomPlayers(room.RoomId);
        }
    }

    // Assumes the room lock is held by the caller.
    private async Task SettleGame(AvalonRoom room)
    {
        if (!room.TrySetSettled()) return;

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
            if (userId == 0) continue;

            var team = AvalonConfig.GetTeam(game.Roles[i]);
            bool isWinner = (team == AvalonTeam.Good && game.Winner == GameWinner.Good)
                            || (team == AvalonTeam.Evil && game.Winner == GameWinner.Evil);
            decimal delta = isWinner ? points : -points;
            if (shielded && game.AssassinTarget.HasValue && i == game.AssassinTarget.Value)
                delta += 0.5m;
            await balanceRepository.UpdateBalance(userId, GameType.Avalon, delta);
        }

        await SendBalancesToAll(room);
    }

    // Assumes the room lock is held by the caller.
    private async Task SendBalancesToAll(AvalonRoom room)
    {
        foreach (var (connId, _) in room.Players)
        {
            var uid = room.PlayerUserIds.GetValueOrDefault(connId);
            if (uid > 0)
            {
                var balance = await balanceRepository.GetOrCreate(uid, GameType.Avalon);
                await Clients.Client(connId).SendAsync("BalanceUpdate", balance.Balance);
            }
        }
    }

    // Assumes the room lock is held by the caller.
    private async Task SendGameStateToPlayer(AvalonRoom room, string connectionId, int seatIndex)
    {
        var dto = AvalonGameStateDto.Create(room.Game!, seatIndex, room.GamePlayerNames);
        await Clients.Client(connectionId).SendAsync("GameState", dto);
    }

    public async Task GetGameState(string roomId)
    {
        var room = roomManager.GetRoom(roomId);
        if (room == null) return;
        await WithLock(room, async () =>
        {
            if (room.Game == null) return;
            if (!room.Players.TryGetValue(Context.ConnectionId, out int seatIndex)) return;
            await SendGameStateToPlayer(room, Context.ConnectionId, seatIndex);
        });
    }

    public string? GetActiveRoom()
    {
        return roomManager.FindRoomByUserId(GetUserId());
    }

    public async Task<decimal> GetBalance()
    {
        var balance = await balanceRepository.GetOrCreate(GetUserId(), GameType.Avalon);
        return balance.Balance;
    }

    public async Task<List<object>> GetLeaderboard()
    {
        var top = await balanceRepository.GetTopBalances(GameType.Avalon, 100);
        return top.Select(x => (object)new { nickname = x.Nickname, balance = x.Balance }).ToList();
    }

    // --- Room management ---

    public async Task<object> CreateRoom(int maxPlayers)
    {
        var room = roomManager.CreateRoom(maxPlayers);
        return await WithLock(room, async () =>
        {
            room.Players.Add(Context.ConnectionId, 0);
            room.HostConnectionId = Context.ConnectionId;
            room.PlayerNicknames[Context.ConnectionId] = await GetNickname();
            room.PlayerUserIds[Context.ConnectionId] = GetUserId();
            room.ApplyDefaultRoles();
            await Groups.AddToGroupAsync(Context.ConnectionId, room.RoomId);
            await BroadcastRoomPlayers(room.RoomId);
            return (object)new { room.RoomId, room.MaxPlayers };
        });
    }

    public async Task<object> JoinRoom(string roomId)
    {
        var room = roomManager.GetRoom(roomId)
            ?? throw new InvalidOperationException("Room not found");

        return await WithLock(room, async () =>
        {
            var userId = GetUserId();

            // Try rejoin if player is in disconnected list (lobby or in-game)
            if (room.DisconnectedPlayers.ContainsKey(userId))
            {
                var info = room.TryRejoin(Context.ConnectionId, userId);
                if (info != null)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
                    await Clients.Group(roomId).SendAsync("PlayerReconnected", info.Nickname);
                    await BroadcastRoomPlayers(roomId);
                    if (room.Game != null)
                    {
                        await Clients.Client(Context.ConnectionId).SendAsync("YourSeat", info.SeatIndex);
                        await SendGameStateToPlayer(room, Context.ConnectionId, info.SeatIndex);
                    }
                    return (object)new { room.MaxPlayers, PlayerCount = room.Players.Count, GameInProgress = room.Game != null };
                }
            }

            // Game in progress but player not in disconnected list
            if (room.Game != null)
                throw new InvalidOperationException("Game is in progress");

            if (room.PlayerUserIds.ContainsValue(userId))
                throw new InvalidOperationException("You are already in this room");

            roomManager.JoinRoom(roomId, Context.ConnectionId);
            room.PlayerNicknames[Context.ConnectionId] = await GetNickname();
            room.PlayerUserIds[Context.ConnectionId] = userId;
            room.RebuildRoleConfig();
            await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
            await Clients.Group(roomId).SendAsync("PlayerJoined", room.Players.Count);
            await BroadcastRoomPlayers(roomId);
            return (object)new { room.MaxPlayers, PlayerCount = room.Players.Count };
        });
    }

    public async Task Ready(string roomId)
    {
        var room = roomManager.GetRoom(roomId)
            ?? throw new InvalidOperationException("Room not found");
        await WithLock(room, async () =>
        {
            if (!room.Players.ContainsKey(Context.ConnectionId))
                throw new InvalidOperationException("Not in this room");
            room.ReadyPlayers.Add(Context.ConnectionId);
            await BroadcastRoomPlayers(roomId);
        });
    }

    public async Task Unready(string roomId)
    {
        var room = roomManager.GetRoom(roomId)
            ?? throw new InvalidOperationException("Room not found");
        await WithLock(room, async () =>
        {
            if (!room.ReadyPlayers.Contains(Context.ConnectionId))
                throw new InvalidOperationException("Not ready");
            room.ReadyPlayers.Remove(Context.ConnectionId);
            await BroadcastRoomPlayers(roomId);
        });
    }

    public async Task AdjustRole(string roomId, string roleName, int delta)
    {
        var room = roomManager.GetRoom(roomId)
            ?? throw new InvalidOperationException("Room not found");
        await WithLock(room, async () =>
        {
            if (room.HostConnectionId != Context.ConnectionId)
                throw new InvalidOperationException("Only host can adjust roles");

            int oldMordred = room.MordredCount, oldOberon = room.OberonCount, oldMinion = room.MinionCount;

            switch (roleName)
            {
                case "Mordred":
                    room.MordredCount = Math.Clamp(room.MordredCount + delta, 0, 1);
                    break;
                case "Oberon":
                    room.OberonCount = Math.Clamp(room.OberonCount + delta, 0, 1);
                    break;
                case "MinionOfMordred":
                    room.MinionCount = Math.Max(0, room.MinionCount + delta);
                    break;
                default:
                    throw new InvalidOperationException("Cannot adjust this role");
            }

            // Validate: good >= evil, and good >= 2 (Merlin + Percival)
            int newEvil = 2 + room.MordredCount + room.OberonCount + room.MinionCount;
            int newGood = room.MaxPlayers - newEvil;
            if (newGood < 2 || newGood - newEvil < 1)
            {
                room.MordredCount = oldMordred;
                room.OberonCount = oldOberon;
                room.MinionCount = oldMinion;
                throw new InvalidOperationException("Not enough good slots (need at least 2)");
            }

            room.RebuildRoleConfig();
            await BroadcastRoomPlayers(roomId);
        });
    }

    public async Task SetMaxRejects(string roomId, int value)
    {
        var room = roomManager.GetRoom(roomId)
            ?? throw new InvalidOperationException("Room not found");
        await WithLock(room, async () =>
        {
            if (room.HostConnectionId != Context.ConnectionId)
                throw new InvalidOperationException("Only host can change settings");
            room.MaxRejects = Math.Clamp(value, 1, 10);
            await BroadcastRoomPlayers(roomId);
        });
    }

    public async Task MovePlayer(string roomId, int seatIndex, int direction)
    {
        var room = roomManager.GetRoom(roomId)
            ?? throw new InvalidOperationException("Room not found");
        await WithLock(room, async () =>
        {
            if (room.HostConnectionId != Context.ConnectionId)
                throw new InvalidOperationException("Only host can reorder");
            if (room.Game != null && room.Game.Phase != AvalonPhase.GameOver)
                throw new InvalidOperationException("Cannot reorder during game");

            int targetSeat = seatIndex + direction;
            if (targetSeat < 0 || targetSeat >= room.Players.Count) return;

            room.SwapSeats(seatIndex, targetSeat);
            await BroadcastRoomPlayers(roomId);
        });
    }

    public async Task ReorderPlayer(string roomId, int fromSeat, int toSeat)
    {
        var room = roomManager.GetRoom(roomId)
            ?? throw new InvalidOperationException("Room not found");
        await WithLock(room, async () =>
        {
            if (room.HostConnectionId != Context.ConnectionId)
                throw new InvalidOperationException("Only host can reorder");
            if (room.Game != null && room.Game.Phase != AvalonPhase.GameOver)
                throw new InvalidOperationException("Cannot reorder during game");
            if (fromSeat == toSeat) return;

            room.MoveSeat(fromSeat, toSeat);
            await BroadcastRoomPlayers(roomId);
        });
    }

    public async Task KickPlayer(string roomId, int seatIndex)
    {
        var room = roomManager.GetRoom(roomId)
            ?? throw new InvalidOperationException("Room not found");
        await WithLock(room, async () =>
        {
            if (room.HostConnectionId != Context.ConnectionId)
                throw new InvalidOperationException("Only host can kick");
            var target = room.Players.FirstOrDefault(p => p.Value == seatIndex).Key;
            if (target == null || target == room.HostConnectionId) return;

            room.Players.Remove(target);
            room.PlayerNicknames.Remove(target);
            room.PlayerUserIds.Remove(target);
            room.ReadyPlayers.Remove(target);
            room.ReassignSeats();
            room.RebuildRoleConfig();
            await Groups.RemoveFromGroupAsync(target, roomId);
            await Clients.Client(target).SendAsync("Kicked", "You were kicked from the room");
            await Clients.Group(roomId).SendAsync("PlayerLeft", room.Players.Count);
            await BroadcastRoomPlayers(roomId);
        });
    }

    public async Task StartGame(string roomId)
    {
        var room = roomManager.GetRoom(roomId)
            ?? throw new InvalidOperationException("Room not found");
        await WithLock(room, async () =>
        {
            if (room.HostConnectionId != Context.ConnectionId)
                throw new InvalidOperationException("Only host can start");
            if (room.Game != null && room.Game.Phase != AvalonPhase.GameOver)
                throw new InvalidOperationException("Game already in progress");

            int playerCount = room.Players.Count;
            if (playerCount != room.MaxPlayers)
                throw new InvalidOperationException($"Need {room.MaxPlayers} players, currently {playerCount}");

            // Check all non-host players are ready
            if (room.Players.Where(p => p.Key != room.HostConnectionId).Any(p => !room.ReadyPlayers.Contains(p.Key)))
                throw new InvalidOperationException("Not all players are ready");

            room.ReassignSeats();
            room.BuildSeatMap();
            room.RebuildRoleConfig(playerCount);
            room.Game = new AvalonGame(playerCount, room.RoleConfig, new Random().Next(playerCount), room.MaxRejects);
            room.ReadyPlayers.Clear();

            // Send seat assignments
            foreach (var (connId, seatIndex) in room.Players)
                await Clients.Client(connId).SendAsync("YourSeat", seatIndex);

            await SendGameStateToAll(room);
        });
    }

    // --- Game actions ---

    public async Task ConfirmNightReveal(string roomId)
    {
        // Player confirms they've seen their role info, when all confirm -> start proposals
        var room = roomManager.GetRoom(roomId)
            ?? throw new InvalidOperationException("Room not found");
        await WithLock(room, async () =>
        {
            if (room.Game == null || room.Game.Phase != AvalonPhase.NightReveal)
                throw new InvalidOperationException("Not in night reveal phase");

            room.ReadyPlayers.Add(Context.ConnectionId);

            if (room.ReadyPlayers.Count >= room.Players.Count)
            {
                room.ReadyPlayers.Clear();
                room.Game.StartProposalPhase();
                await SendGameStateToAll(room);
            }
            else
            {
                // Notify others who has confirmed (seat indices), not just the count
                var confirmed = room.ReadyPlayers
                    .Select(c => room.Players.GetValueOrDefault(c, -1))
                    .Where(s => s >= 0)
                    .ToList();
                await Clients.Group(roomId).SendAsync("NightRevealProgress", confirmed);
            }
        });
    }

    public async Task ProposeTeam(string roomId, List<int> team)
    {
        var room = roomManager.GetRoom(roomId)
            ?? throw new InvalidOperationException("Room not found");
        await WithLock(room, async () =>
        {
            var game = room.Game ?? throw new InvalidOperationException("No game");
            if (!room.Players.TryGetValue(Context.ConnectionId, out int seatIndex))
                throw new InvalidOperationException("Not in this room");
            if (seatIndex != game.CurrentLeaderIndex)
                throw new InvalidOperationException("Not the leader");

            game.ProposeTeam(seatIndex, team);

            if (game.Phase == AvalonPhase.TeamVote)
                await SendGameStateToAll(room);
        });
    }

    public async Task CastVote(string roomId, bool approve)
    {
        var room = roomManager.GetRoom(roomId)
            ?? throw new InvalidOperationException("Room not found");
        await WithLock(room, async () =>
        {
            var game = room.Game ?? throw new InvalidOperationException("No game");
            if (!room.Players.TryGetValue(Context.ConnectionId, out int seatIndex))
                throw new InvalidOperationException("Not in this room");

            var phaseBefore = game.Phase;
            game.CastVote(seatIndex, approve);

            if (game.Phase != phaseBefore)
            {
                // Vote resolved — send full state (includes vote results via history)
                await SendGameStateToAll(room);
            }
            else
            {
                // Just notify who has voted (not how)
                await SendVoteProgress(room);
            }
        });
    }

    // Assumes the room lock is held by the caller.
    private async Task SendVoteProgress(AvalonRoom room)
    {
        var voted = room.Game!.CurrentProposal?.Votes.Keys.ToList() ?? new();
        await Clients.Group(room.RoomId).SendAsync("VoteProgress", voted);
    }

    public async Task PlayMissionCard(string roomId, bool success)
    {
        var room = roomManager.GetRoom(roomId)
            ?? throw new InvalidOperationException("Room not found");
        await WithLock(room, async () =>
        {
            var game = room.Game ?? throw new InvalidOperationException("No game");
            if (!room.Players.TryGetValue(Context.ConnectionId, out int seatIndex))
                throw new InvalidOperationException("Not in this room");

            var phaseBefore = game.Phase;
            game.PlayMissionCard(seatIndex, success);

            if (game.Phase != phaseBefore)
            {
                await SendGameStateToAll(room);
            }
            else
            {
                await Clients.Group(room.RoomId).SendAsync("MissionProgress",
                    game.GetMissionPlayersPlayed(), game.CurrentProposal?.Team.Count ?? 0);
            }
        });
    }

    public async Task EarlyAssassinate(string roomId)
    {
        var room = roomManager.GetRoom(roomId)
            ?? throw new InvalidOperationException("Room not found");
        await WithLock(room, async () =>
        {
            var game = room.Game ?? throw new InvalidOperationException("No game");
            if (!room.Players.TryGetValue(Context.ConnectionId, out int seatIndex))
                throw new InvalidOperationException("Not in this room");

            if (!game.BeginEarlyAssassination(seatIndex))
                throw new InvalidOperationException("Cannot assassinate now");

            await SendGameStateToAll(room);
        });
    }

    public async Task Assassinate(string roomId, int targetIndex)
    {
        var room = roomManager.GetRoom(roomId)
            ?? throw new InvalidOperationException("Room not found");
        await WithLock(room, async () =>
        {
            var game = room.Game ?? throw new InvalidOperationException("No game");
            if (!room.Players.TryGetValue(Context.ConnectionId, out int seatIndex))
                throw new InvalidOperationException("Not in this room");

            game.Assassinate(seatIndex, targetIndex);
            await SendGameStateToAll(room);
        });
    }

    // --- Reconnect ---

    private const int ReconnectGraceSeconds = 7200;

    public async Task<object> Rejoin(string roomId)
    {
        var room = roomManager.GetRoom(roomId);
        if (room == null) throw new InvalidOperationException("Room not found");

        return await WithLock(room, async () =>
        {
            var userId = GetUserId();
            var info = room.TryRejoin(Context.ConnectionId, userId);
            if (info == null) throw new InvalidOperationException("No disconnected slot for you");

            await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
            await Clients.Group(roomId).SendAsync("PlayerReconnected", info.Nickname);
            await BroadcastRoomPlayers(roomId);

            if (room.Game != null)
            {
                await Clients.Client(Context.ConnectionId).SendAsync("YourSeat", info.SeatIndex);
                await SendGameStateToPlayer(room, Context.ConnectionId, info.SeatIndex);
            }

            return (object)new { room.MaxPlayers, PlayerCount = room.Players.Count };
        });
    }

    // --- Disconnect ---

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await HandlePlayerDisconnect(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task LeaveRoom()
    {
        await HandlePlayerLeave(Context.ConnectionId);
    }

    private async Task HandlePlayerDisconnect(string connectionId)
    {
        var room = FindRoomByConnectionId(connectionId);
        if (room == null) return;

        await WithLock(room, async () =>
        {
            var roomId = room.RoomId;
            await Groups.RemoveFromGroupAsync(connectionId, roomId);

            var info = room.MarkDisconnected(connectionId);
            if (info == null) return;

            await Clients.Group(roomId).SendAsync("PlayerDisconnected", info.Nickname);
            await BroadcastRoomPlayers(roomId);

            var userId = info.UserId;
            // Fire-and-forget: runs after the grace period, re-acquires the lock then (no nesting).
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(ReconnectGraceSeconds));
                await CheckDisconnectedPlayer(roomId, userId);
            });
        });
    }

    private async Task HandlePlayerLeave(string connectionId)
    {
        var room = FindRoomByConnectionId(connectionId);
        if (room == null) return;

        await WithLock(room, async () =>
        {
            var roomId = room.RoomId;
            await Groups.RemoveFromGroupAsync(connectionId, roomId);

            if (room.Game != null && room.Game.Phase == AvalonPhase.GameOver)
            {
                await Clients.Group(roomId).SendAsync("RoomDisbanded");
                roomManager.RemoveRoom(roomId);
                return;
            }

            if (room.Game != null)
            {
                room.Game = null;
                room.Players.Remove(connectionId);
                room.PlayerNicknames.Remove(connectionId);
                room.PlayerUserIds.Remove(connectionId);
                room.ReadyPlayers.Remove(connectionId);

                if (room.Players.Count == 0) { roomManager.RemoveRoom(roomId); return; }
                if (room.HostConnectionId == connectionId)
                    room.HostConnectionId = room.Players.Keys.First();
                room.ReassignSeats();
                room.RebuildRoleConfig();

                await Clients.Group(roomId).SendAsync("GameAborted", "A player left during the game");
                await SendBalancesToAll(room);
                await Clients.Group(roomId).SendAsync("PlayerLeft", room.Players.Count);
                await BroadcastRoomPlayers(roomId);
                return;
            }

            room.Players.Remove(connectionId);
            await FinishPlayerRemoval(room, connectionId);
        });
    }

    private AvalonRoom? FindRoomByConnectionId(string connectionId)
    {
        // Check via room manager — don't remove yet
        var (roomId, _) = roomManager.FindRoomByConnectionId(connectionId);
        return roomId != null ? roomManager.GetRoom(roomId) : null;
    }

    // Assumes the room lock is held by the caller.
    private async Task FinishPlayerRemoval(AvalonRoom room, string connectionId)
    {
        room.PlayerNicknames.Remove(connectionId);
        room.PlayerUserIds.Remove(connectionId);
        room.ReadyPlayers.Remove(connectionId);

        if (room.Players.Count == 0) { roomManager.RemoveRoom(room.RoomId); return; }

        if (room.HostConnectionId == connectionId)
            room.HostConnectionId = room.Players.Keys.First();

        room.ReassignSeats();
        room.RebuildRoleConfig();

        await Clients.Group(room.RoomId).SendAsync("PlayerLeft", room.Players.Count);
        await BroadcastRoomPlayers(room.RoomId);
    }

    private async Task CheckDisconnectedPlayer(string roomId, int userId)
    {
        var room = roomManager.GetRoom(roomId);
        if (room == null) return;

        await WithLock(room, async () =>
        {
            if (!room.DisconnectedPlayers.TryGetValue(userId, out var info)) return;

            if ((DateTime.UtcNow - info.DisconnectedAt).TotalSeconds < ReconnectGraceSeconds)
                return;

            room.DisconnectedPlayers.Remove(userId);

            if (room.Game != null && room.Game.Phase != AvalonPhase.GameOver)
            {
                room.Game = null;

                await hubContext.Clients.Group(roomId).SendAsync("GameAborted",
                    $"{info.Nickname} did not reconnect in time");

                using var scope = scopeFactory.CreateScope();
                var balanceRepo = scope.ServiceProvider.GetRequiredService<IGameBalanceRepository>();
                foreach (var (connId, _) in room.Players)
                {
                    var uid = room.PlayerUserIds.GetValueOrDefault(connId);
                    if (uid > 0)
                    {
                        var balance = await balanceRepo.GetOrCreate(uid, GameType.Avalon);
                        await hubContext.Clients.Client(connId).SendAsync("BalanceUpdate", balance.Balance);
                    }
                }
            }

            if (room.Players.Count == 0) { roomManager.RemoveRoom(roomId); return; }

            if (room.HostConnectionId == null || !room.Players.ContainsKey(room.HostConnectionId))
                room.HostConnectionId = room.Players.Keys.FirstOrDefault();

            room.ReassignSeats();
            room.RebuildRoleConfig();
            await hubContext.Clients.Group(roomId).SendAsync("PlayerLeft", room.Players.Count);

            foreach (var (connId, _) in room.Players)
            {
                var isHost = connId == room.HostConnectionId;
                await hubContext.Clients.Client(connId).SendAsync("RoomUpdate", new
                {
                    players = room.Players.OrderBy(p => p.Value).Select(p => new
                    {
                        nickname = room.PlayerNicknames.GetValueOrDefault(p.Key, "Player"),
                        isReady = room.ReadyPlayers.Contains(p.Key),
                        isHost = p.Key == room.HostConnectionId,
                        seatIndex = p.Value,
                        isDisconnected = false
                    }),
                    isHost,
                    roleConfig = room.RoleConfig.Select(r => r.ToString()).ToList(),
                    maxRejects = room.MaxRejects
                });
            }
        });
    }
}
