# Closed-room guard: never act on a room that was removed while waiting for its lock

## Problem

Almost every room action follows the same pattern: look the room up (`GetRoom` / `FindRoomByConnectionId`), then take its lock
(`WithLock` / `room.Lock.WaitAsync()`), then act. `RemoveRoom` also runs under that lock. So another action can look the room up,
wait for the lock while the room is being removed, and then get the lock on a room that no longer exists in the manager. No handler
checks for that after locking. It then mutates and broadcasts on a room nobody can find.

Where the lock is taken:
- `AvalonHub.WithLock` (`AvalonHub.cs:17-29`): every Avalon invocation, plus the grace check `CheckDisconnectedPlayer`, which runs
  in a fire-and-forget `Task.Run`
- `BlackJackHub.WithLock` (`BlackJackHub.cs:39-51`): every BlackJack invocation
- `DemoBotService.WithLock` (`DemoBotService.cs:280-285`): scheduled bot moves, which hold the room from a closure and never look it up again
- `TurnTimerService`, which calls `room.Lock.WaitAsync()` directly at `:69` (turn timer) and `:112` (betting timer)

Where rooms are removed: all under the same room's lock. Avalon at `AvalonHub.cs:820` (disband at GameOver), `:833` and
`:866` (last player left), and `:907` (grace expiry emptied the room). BlackJack at `BlackJackHub.cs:439` and `TurnTimerService.cs:216`.

What goes wrong when it happens:
- **Joining a room as it is removed puts the player in a ghost room.** Example: the last player leaves a BlackJack or Avalon lobby
  at the moment another player joins. The joiner's `JoinRoom` passes `GetRoom`, waits, then gets the lock on the removed room. It
  seats them and returns success. From then on, every call they make (Ready, StartGame, PlaceBet...) fails with "Room not found",
  and their client sits in a room screen that can never start. Since the room id is free again, `RoomIds` can also hand it to a new
  room later.
- **Other actions on a removed room** (Ready, Kick, SetRanked, game moves, a late timer or bot move) mutate the orphan object and
  broadcast to its SignalR group. This is mostly wasted work, and occasionally a stray message to someone still in that group.

Who it affects: players whose action races with a room closing. It is rare, but the ghost-room join leaves the player stuck until
they refresh.

## Scope

**In scope**
- Rooms know when they are closed: `RemoveRoom` marks the removed room object closed. It already runs under that room's lock.
- All four lock sites check that flag right after they get the lock:
  - Hub invocations fail like a missing room (D1).
  - Background work (the grace check, bot moves, BlackJack timers) stops without side effects.
- Tests that force the race deterministically. The test holds the room lock itself, so it controls the ordering.

**Out of scope** (backlog)
- **Demo rooms that never get removed.** When the human leaves mid-demo, the 4 bots stay in `Players`, so `Players.Count` never
  reaches 0, the room is never removed, and a bot becomes host. A guest disconnecting is not handled at all
  (`MarkDisconnected` returns null for `userId <= 0`). This is a leak, the opposite problem, and needs its own rule.
- A room removed *before* the lookup. That path already fails cleanly ("Room not found") and is unchanged.

## Behaviour

- `AvalonRoomManager.RemoveRoom` / `BlackJackRoomManager.RemoveRoom`: if `TryRemove` succeeds, mark that room closed.
  - Callers hold the room lock, so the flag is set under the lock.
  - Readers check it under the same lock, so no extra synchronisation is needed. It is still written with `Volatile` for clarity.
- Hub `WithLock` (both hubs): after acquiring, if the room is closed, release and throw
  `InvalidOperationException("Room not found")` (D1). The client sees the same error as for a room that did not exist.
  Nothing is mutated or sent.
- Background paths (Avalon `CheckDisconnectedPlayer`, `DemoBotService.WithLock`, the two `TurnTimerService` sites): after
  acquiring, if the room is closed, release and return. No exception is thrown on a fire-and-forget task.
- Work that already holds the lock and removes the room *itself* (for example BlackJack leave: `RemoveRoom` and then sending
  `PlayerLeft`) is unchanged. The check runs only when the lock is acquired.

Which roles see what: only the caller of a raced invocation sees "Room not found". Everyone else sees nothing new, and ghost
broadcasts stop.

## Edge cases

- **Join races with the last leave (either game):** the joiner gets "Room not found" and is not seated anywhere
  (`IsInAnyRoom` is false), so they can create or join again straight away.
- **Leave races with the disband at GameOver (Avalon):** the second leave hits the closed room → "Room not found". The existing
  membership re-check would have returned early anyway, and the client ignores LeaveRoom errors (`AvalonGame.jsx:291`).
- **Grace expiry races with the room being emptied:** it hits the closed room and returns. There is no double removal and no
  broadcast.
- **A BlackJack turn or betting timer fires after the room closed:** it stops. Removal already cancels the timers
  (`BlackJackHub.cs:437-438`); this also covers a timer that was already waiting on the lock.
- **A bot move is scheduled on a demo room that was disbanded at GameOver:** it stops.
- **A room id is reused by a new room:** a stale handler holds the old object, which is closed, so it cannot touch the new room.
- **A new room is created:** it is never closed. `RoomIds.Register` publishes a fresh object.

## Acceptance criteria

1. `RemoveRoom` marks the removed room closed, in both managers. Removing an unknown id does nothing.
2. Integration (Avalon): the test holds room R's lock. Player B starts `JoinRoom(R)`. The test removes R and releases the lock.
   B's call fails with "Room not found", and B is in no room (`GetActiveRoom` returns null).
3. Integration (BlackJack): the same race gives B "Room not found", and B can then create a new room successfully.
4. Background: with the room closed while a BlackJack turn timer waits on the lock, the timer does nothing (no `PlayerStand` is
   sent). Proved with `FastTimerWebApplicationFactory` and the lock held by the test.
5. Existing tests pass unchanged. The full suite passes 20 runs in a row.

## Test plan

| AC | Proof |
|----|-------|
| 1 | `AvalonRoomManagerTests.RemoveRoom_MarksRoomClosed` and `BlackJackRoomManagerTests.RemoveRoom_MarksRoomClosed` |
| 2 | `AvalonHubIntegrationTests.JoinRoom_RacingRoomRemoval_FailsAndSeatsNobody`. The test gets `IAvalonRoomManager` from `_factory.Services`, takes `room.Lock`, starts B's invoke, waits ~200 ms so B is queued on the lock, calls `RemoveRoom`, then releases. If B has not yet looked up the room, it still gets "Room not found", so the test cannot flake; it can only become less sharp. Checked red on the old code. |
| 3 | `BlackJackHubIntegrationTests.JoinRoom_RacingRoomRemoval_FailsThenCanCreate`, same technique. |
| 4 | `TurnTimerServiceTests.TurnTimer_RoomClosedWhileWaiting_DoesNothing`. Deal a 1-player round (deterministic deck), hold the lock past the 1 s turn timer, remove the room, release, and assert no `PlayerStand` within 2 s. |
| 5 | Run the existing suites, then the full suite 20 times. |

## Decisions (2026-09-30)

- **D1. A raced hub invocation fails with "Room not found"**, the same as a room that is already gone.
- **D2. All four lock sites get the guard**, including the background ones (grace check, bot moves, BlackJack timers). Those
  return silently instead of throwing.

## Tasks

Every task is checked with `dotnet build`, then its own tests, then the full `dotnet test`. Any failure blocks the task.
No task touches a per-player DTO. **T2 and T3 change the lock helpers every room action goes through** and need the concurrency
review: the flag is only written under the room lock (inside `RemoveRoom`, whose callers hold it) and only read right after
acquiring it. The helpers must still release on every path, and nothing may nest locks.

1. **Rooms know they are closed** (AC1)
   - Files: `BoardGames/Models/Avalon/AvalonRoom.cs`, `BoardGames/Models/BlackJack/BlackJackRoom.cs` (`IsClosed` plus
     `MarkClosed()`, written with `Volatile`), `BoardGames/Services/Avalon/AvalonRoomManager.cs`,
     `BoardGames/Services/BlackJack/BlackJackRoomManager.cs` (`RemoveRoom`: mark closed when `TryRemove` succeeds), and the two
     manager test files.
   - Tests (new): `RemoveRoom_MarksRoomClosed` (both managers). Removing an unknown id does nothing.
   - Nothing reads the flag yet. Commit the spec with this task.

2. **Hub lock helpers refuse closed rooms** (D1, AC2, AC3). ⚠ Lock helpers: needs concurrency review.
   - Files: `BoardGames/Hubs/Avalon/AvalonHub.cs` (both `WithLock` overloads throw "Room not found" after acquiring a closed
     room's lock). `CheckDisconnectedPlayer` switches to a non-throwing `WithLockIfOpen`, because it runs on a fire-and-forget
     task. Also `BoardGames/Hubs/BlackJack/BlackJackHub.cs` (both `WithLock` overloads), and the two hub integration test files.
   - Tests (new): `AvalonHubIntegrationTests.JoinRoom_RacingRoomRemoval_FailsAndSeatsNobody` and
     `BlackJackHubIntegrationTests.JoinRoom_RacingRoomRemoval_FailsThenCanCreate`, each checked red against the old helpers.

3. **Background lock sites skip closed rooms** (D2, AC4). ⚠ Timer and bot paths: needs concurrency review.
   - Files: `BoardGames/Services/Avalon/DemoBotService.cs` (`WithLock` returns when the room is closed),
     `BoardGames/Services/BlackJack/TurnTimerService.cs` (both `room.Lock.WaitAsync()` sites return when it is closed), and
     `BoardGames.Tests/Integration/TurnTimerServiceTests.cs`.
   - Tests (new): `TurnTimer_RoomClosedWhileWaiting_DoesNothing`, checked red against the old timer code.

4. **Final verification** (AC5; no code)
   - The full suite passes 20 runs in a row. `git diff main` shows no edits to existing test methods. Update the backlog memory.
