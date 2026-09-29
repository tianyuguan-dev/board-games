# Avalon reconnect grace timer: never skip the expiry

## Problem

When a registered Avalon player disconnects, `AvalonHub.HandlePlayerDisconnect` (`AvalonHub.cs:773-796`) calls
`AvalonRoom.MarkDisconnected`. That call stores a `DisconnectedPlayer` with `DisconnectedAt = DateTime.UtcNow`
(`AvalonRoom.cs:77`). The hub then starts a fire-and-forget `Task.Delay(ReconnectGraceSeconds)` and, when it finishes, calls
`CheckDisconnectedPlayer`. That check (`AvalonHub.cs:872-883`) returns without doing anything when

```
(DateTime.UtcNow - info.DisconnectedAt).TotalSeconds < ReconnectGraceSeconds
```

The check exists to ignore **stale** timers. Say a player disconnects, reconnects and disconnects again. The first timer then
fires while the second disconnect's grace is still running, and must do nothing.

It also ignores the **current** timer whenever that timer is a little early by the wall clock. `Task.Delay` runs on a monotonic
millisecond tick, and `DateTime.UtcNow` is a separate clock. I measured this on this machine with 50 concurrent loops of
`Task.Delay(100ms)`: **50 of 1000** finished before `UtcNow` showed 100 ms had passed, the earliest at 99.44 ms. Nothing
reschedules the check. So when it happens, the player is never removed:
- **In a game:** it is never aborted. The remaining players wait forever for someone who is gone. There is no "did not
  reconnect in time" message and no seat cleanup. (Host handover is not affected: `MarkDisconnected` already moves host at
  disconnect time.)
- **In the lobby:** the ghost player stays in `DisconnectedPlayers`. Their seat is gone from `Players`, but `FindRoomByUserId`
  still reports the room as their active game.

A wall-clock jump makes it worse: after an NTP correction backwards, `UtcNow - DisconnectedAt` can stay below the grace for as long
as the jump, and the check never runs again.

This affects every Avalon player whose friend drops and does not come back. In production the grace is 7200 s
(`AvalonHubSettings.cs:7`), so the game only hangs after two hours, but it then hangs for good. In tests (1 s grace) it makes
`AvalonDisconnectGraceTests.PlayerDisconnect_MidGame_TriggersGameAbortAfterGrace` potentially flaky. While building
`cross-room-lookup-thread-safety` it made a new test's `GameAborted` wait time out.

## Scope

**In scope**
- Decide whether an expiring timer is still the current one by identity, not by comparing clocks (D1).
- Move that decision into `AvalonRoom`, so it can be unit-tested without timers.
- Tests that prove an "early" expiry of the current timer still expires, and that a stale timer never does.

**Out of scope**
- The grace length and what happens on expiry: abort the game, the `GameAborted` / `PlayerLeft` / `RoomUpdate` messages,
  seat compaction and host handover. None of this changes.
- Guests and demo bots (`userId <= 0`). `MarkDisconnected` returns null for them and no timer starts, as today.
- BlackJack timers. `TurnTimerService` already uses `CancellationTokenSource` identity and no clock comparison.
- Replacing the fire-and-forget `Task.Run` with a hosted sweeper or `TimeProvider`.

## Behaviour

What players see does not change, for any role. When a disconnected player has not rejoined by the end of the grace:
- In a game, the room's players get `GameAborted("<nickname> did not reconnect in time")` and `BalanceUpdate`, then the
  player is removed, seats are compacted, the host moves if needed, and everyone gets `PlayerLeft` + `RoomUpdate`.
- In the lobby, the same happens without `GameAborted`.

These are the same messages and the same order as today. The difference is that this now **always** happens.

The rule for which timer counts (D1, **identity**):
- `MarkDisconnected` creates a new `DisconnectedPlayer` for every disconnect, as it does now.
- The grace timer keeps a reference to that exact instance.
- On expiry, under the room lock, it removes the player only if `DisconnectedPlayers[userId]` is **the same instance**
  (`ReferenceEquals`). A rejoin removes the entry, and a later disconnect stores a new instance. So a stale timer finds a
  different instance, or none, and does nothing.
- The elapsed-time check is removed. The timer's own duration is what counts.
- `DisconnectedAt` stays on the object for information; nothing decides anything from it anymore.
- This is the same pattern `TurnTimerService` uses (`current != cts` at `TurnTimerService.cs:73`).

Where it lives: a new `AvalonRoom` method, for example `bool TryExpireDisconnected(int userId, DisconnectedPlayer expected)`. It
removes the entry and returns true only when the identity matches. The hub's `CheckDisconnectedPlayer` calls it under the lock
and then runs the unchanged abort / cleanup code.

## Edge cases

- **The timer fires early by the wall clock:** it is still the current instance, so the player expires. This is the bug being
  fixed.
- **The wall clock jumps backwards or forwards:** it does not matter, because no clock is compared.
- **Disconnect → rejoin within the grace:** `TryRejoin` removes the entry. The timer finds nothing and does nothing, as today.
- **Disconnect → rejoin → disconnect again:** the first timer finds the second instance and does nothing. The second timer
  expires the player a full grace after the second disconnect.
- **Two timers for the same instance:** this cannot happen, because each disconnect starts exactly one timer. If it ever did, the
  first one removes the entry and the second finds nothing, so it is idempotent.
- **Rejoin and expiry at the same moment:** both run under the room lock. Whichever gets the lock first wins; the other one sees
  the entry gone and does nothing.
- **The room was removed before the timer fires:** `GetRoom` returns null and nothing happens, as today. The broader "acts on a
  removed room" issue is a separate backlog item.
- **Host disconnects:** `MarkDisconnected` already moved host to the lowest seat. On expiry, the existing
  `HostConnectionId == null || !Players.ContainsKey(...)` guard still applies.

## Acceptance criteria

1. The expiry decision does not read `DateTime.UtcNow` or `DisconnectedAt`. `CheckDisconnectedPlayer` has no clock comparison.
2. Unit: straight after `MarkDisconnected`, with no time passed, expiring with the returned instance succeeds and removes the
   entry. So the current timer expires however early the clock says it is.
3. Unit: after `MarkDisconnected` → `TryRejoin` → `MarkDisconnected`, expiring with the **first** instance fails and leaves the
   entry, and expiring with the **second** instance succeeds.
4. Unit: after `MarkDisconnected` → `TryRejoin`, expiring with that instance fails. Nothing is left to remove.
5. Integration (1 s grace): a player disconnects mid-game and never returns. The others get `GameAborted` containing "did not
   reconnect in time". The existing test keeps its assertions and passes 100 runs in a row.
6. Integration (1 s grace): disconnect → rejoin → disconnect. The game is aborted once, after the second grace, and the first
   timer does not abort it early.
7. Existing Avalon disconnect, rejoin and grace tests pass unchanged. The full suite passes 20 runs in a row.

## Test plan

| AC | Proof |
|----|-------|
| 1 | Code review of the diff. A unit test cannot observe "does not read the clock", but AC2 would fail if a clock check remained, because it expires with zero elapsed time. |
| 2 | `AvalonRoomDisconnectTests.TryExpireDisconnected_CurrentInstance_ExpiresImmediately` |
| 3 | `AvalonRoomDisconnectTests.TryExpireDisconnected_StaleInstance_IsIgnored` |
| 4 | `AvalonRoomDisconnectTests.TryExpireDisconnected_AfterRejoin_IsIgnored` |
| 5 | Existing `AvalonDisconnectGraceTests.PlayerDisconnect_MidGame_TriggersGameAbortAfterGrace`, run 100 times with `--filter`. Report the count. |
| 6 | New `AvalonDisconnectGraceTests.DisconnectRejoinDisconnect_AbortsOnceAfterSecondGrace`. After the rejoin it records the time of the second disconnect and asserts `GameAborted` arrives exactly once and no earlier than ~1 s after that. It waits at most 5 s, with no sleep longer than the timers. |
| 7 | Run the existing suites, then the full suite 20 times. |

## Decisions (2026-09-30)

- **D1. Identity check.** The grace timer keeps the `DisconnectedPlayer` instance that `MarkDisconnected` returned. On expiry it
  removes the player only if that same instance is still stored (`ReferenceEquals`). No clock comparison.
- **D2. The 100-run check is local verification only.** CI keeps running `PlayerDisconnect_MidGame_TriggersGameAbortAfterGrace`
  once per build.

## Tasks

Every task is checked with `dotnet build`, then its own tests, then the full `dotnet test`. Any failure blocks the task.
No task touches a per-player DTO. **T2 changes the fire-and-forget timer path that mutates shared room state** and needs the
concurrency review. It must still take the room lock before touching the room (the rule for timers in CLAUDE.md), and it must
not nest locks.

1. **`AvalonRoom.TryExpireDisconnected(int userId, DisconnectedPlayer expected)`** (D1, AC2, AC3, AC4)
   - Files: `BoardGames/Models/Avalon/AvalonRoom.cs`, `BoardGames.Tests/Models/Avalon/AvalonRoomDisconnectTests.cs`.
   - Removes `DisconnectedPlayers[userId]` and returns true only if the stored value is the same instance as `expected`.
     Otherwise it changes nothing and returns false. It reads no clock. The caller holds the room lock.
   - Tests (new): `TryExpireDisconnected_CurrentInstance_ExpiresImmediately`, `TryExpireDisconnected_StaleInstance_IsIgnored`,
     `TryExpireDisconnected_AfterRejoin_IsIgnored`.
   - Nothing calls it yet, so behaviour is unchanged. Commit the spec with this task.

2. **The grace timer uses it; the clock check goes** (D1, AC1, AC5, AC6). ⚠ Timer path on shared room state: needs
   concurrency review.
   - Files: `BoardGames/Hubs/Avalon/AvalonHub.cs`: `HandlePlayerDisconnect` passes `info` to the timer closure;
     `CheckDisconnectedPlayer(roomId, userId, expected)` calls `room.TryExpireDisconnected` under `WithLock` instead of the
     `TryGetValue` + `DisconnectedAt` check. The abort / cleanup code after it is unchanged.
     Also `BoardGames.Tests/Integration/AvalonDisconnectGraceTests.cs`.
   - Tests (new): `DisconnectRejoinDisconnect_AbortsOnceAfterSecondGrace`. Existing: `PlayerDisconnect_MidGame_TriggersGameAbortAfterGrace`
     (100 filtered runs, local only, D2) and `PlayerDisconnect_DuringLobby_DoesNotAbortAnything`.

3. **Final verification** (AC7; no code)
   - The full suite passes 20 runs in a row. `git diff main` shows no edits to existing test methods. Update the backlog
     memory: grace timer item done.
