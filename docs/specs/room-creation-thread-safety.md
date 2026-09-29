# Thread-safe room creation

## Problem

Both room managers break the CLAUDE.md concurrency rule, "Shared collections outside a room lock must be thread safe". The code is
in `BoardGames/Services/BlackJack/BlackJackRoomManager.cs:10-25` and `BoardGames/Services/Avalon/AvalonRoomManager.cs:9-23`.

1. **Shared non-thread-safe `Random`.** Each manager is a DI singleton with `private readonly Random _random = new()`.
   `CreateRoom` calls `_random.Next(10000, 100000)`, and the hubs call `CreateRoom` outside any room lock, because the room does
   not exist yet (`BlackJackHub.cs:205`, `AvalonHub.cs:231`, and the demo room at `AvalonHub.cs:268`). Two players creating rooms
   at the same moment call `Next` on one `Random` from two threads. `System.Random` is not thread safe: its internal state can be
   corrupted and start returning the same value, often 0, every time. After that:
   - the `while (_rooms.ContainsKey(roomId))` retry loop can spin forever on the same id, so the `CreateRoom` hub call hangs,
     the thread is lost, and the player sees nothing;
   - or every new room gets the same id.
2. **Check-then-act on the room dictionary.** `ContainsKey(roomId)` and then `TryAdd(roomId, room)` are two separate steps, and
   the result of `TryAdd` is ignored. If two creates pick the same free id at the same time, both pass the check, one `TryAdd`
   loses, and that caller still gets its room back. The room was never registered: its host is in a room that `GetRoom` cannot
   find, so every later hub call for that room fails with "Room not found".

Who it affects: players creating rooms at the same time. It is rare with a handful of friends, but when it does happen it is a
hang, not an error message. It is the same kind of bug as the vote-tally lock-up that CLAUDE.md was written after.

## Scope

**In scope**
- Room ID generation in both managers uses a thread-safe random source.
- Registering a new room is atomic. If the id is already taken, a new id is picked and the add is retried. A caller only
  ever gets a room that `GetRoom` can find.
- One shared helper for both managers, so the fix is not written twice (D2).
- Tests that exercise concurrent creation, plus a deterministic test of the "id already taken" path (D1).

**Out of scope**
- Room IDs players see: still 5 digits, 10000-99999, chosen at random.
- Cross-room lookups that read another room's plain `Dictionary` outside its lock (D3, separate spec): `IsInAnyRoom`,
  `FindRoomByConnectionId`, `FindRoomByUserId`, and the "already in a room" check in `JoinRoom`.
- `FindAndRemoveByConnectionId`, which writes to `room.Players` outside any lock. No production code calls it (only tests do),
  so it is noted here and not changed.
- Any hub, DTO or game-rule change.

## Behaviour

Nothing that players see changes, for every role in either game.

- `CreateRoom(maxPlayers)` returns a new room whose id is a 5-digit string in [10000, 99999] and which is already registered.
  `GetRoom(id)` returns that same instance as soon as `CreateRoom` returns.
- Id choice: draw a random id from a thread-safe source (`Random.Shared`, or an injected source in tests; D1). Try to
  register it atomically (`TryAdd`). If that fails because the id is taken, draw again.
- Retry limit (D4): after 100 failed attempts, throw `InvalidOperationException("Could not allocate a room id")`
  instead of looping forever. The hub turns it into a normal HubException for the creator, like any other create error.
  With 90,000 ids and a few rooms, this never happens in practice.
- The Avalon `maxPlayers` clamp (5-10) and the demo-room path keep working as they do now. They only call `CreateRoom`.
- No lock is added: a `ConcurrentDictionary.TryAdd` with a thread-safe random source needs none. This keeps the rule
  "locks at hub entry points only".

## Edge cases

- **Concurrent creates, including many at once:** every call returns a distinct id, and every returned room is registered.
  No call hangs.
- **Id collision** (the random draw hits an existing room): pick a new id and register that. The existing room is never
  replaced or changed.
- **Room removed while another is being created** (`RemoveRoom` → `TryRemove`): independent. An id freed by a removal can be
  reused later, as today.
- **Retry limit reached:** a clear error for the creator. Nothing is registered, and no other room is affected.
- **Disconnect right after create:** unchanged. The room is registered before the hub's `WithLock` section runs, so the existing
  leave and disconnect paths find it.
- **Invalid `maxPlayers`:** unchanged. BlackJack validates it in the hub (1-7); Avalon clamps it in the manager.

## Acceptance criteria

1. Neither room manager has a `System.Random` instance field. Ids come from a thread-safe source.
2. 64 parallel tasks × 50 `CreateRoom` calls each, on one manager (per game), finish within 10 s. They return 3,200 distinct ids,
   all in [10000, 99999], and `GetRoom(id)` returns the same instance for every one.
3. When the id source returns an id already in use, `CreateRoom` registers the room under the next unused id, and the existing
   room is not changed (deterministic test, D1).
4. When the id source only ever returns taken ids, `CreateRoom` throws `InvalidOperationException` after the retry limit and
   registers nothing (D4).
5. Existing `BlackJackRoomManagerTests`, `AvalonRoomManagerTests`, and the hub and integration tests pass unchanged.
6. The full `dotnet test` suite passes 20 runs in a row.

## Test plan

| AC | Proof |
|----|-------|
| 1 | Unit test `RoomManagers_HaveNoRandomInstanceField`: reflection over `BlackJackRoomManager` and `AvalonRoomManager` finds no field of type `System.Random`. It guards against someone putting the old pattern back. |
| 2 | `BlackJackRoomManagerTests.CreateRoom_ConcurrentCalls_AllDistinctAndRegistered` and `AvalonRoomManagerTests.CreateRoom_ConcurrentCalls_AllDistinctAndRegistered` (`Task.WhenAll` with a 10 s `WaitAsync`). This also brings back the concurrent test dropped from task 5 of `blackjack-deterministic-deck-tests.md`, adding a one-deck-factory-call-per-room assertion for BlackJack. |
| 3 | `CreateRoom_IdTaken_RetriesWithNextId` for each manager: the scripted id source returns `"12345"` twice, then `"23456"`. The first create gets `12345`, the second gets `23456`, and room `12345` is the same object as before. |
| 4 | `CreateRoom_AllIdsTaken_ThrowsAfterRetryLimit` for each manager: the scripted source always returns `"12345"` after it is taken. The test expects `InvalidOperationException`, and `GetRoom` still returns only the original room. |
| 5 | Run the existing suites. `git diff` shows no edits to existing test methods. |
| 6 | Run the full suite 20 times and report the pass/fail counts. |

Note on AC2: a stress test cannot prove the old code wrong on every run, because the race is timing-dependent. That is why AC1
(structural) and AC3/AC4 (deterministic) carry the proof, and AC2 checks that the new code holds up under load.

## Decisions (2026-09-30)

- **D1. Injectable id source.** The shared helper takes an optional `Func<int>`, defaulting to `Random.Shared.Next(10000, 100000)`.
  Tests pass a scripted sequence, so the collision and retry-limit tests are deterministic.
- **D2. One shared helper.** A static helper in `BoardGames/Services/` does "draw id → `TryAdd` → retry". Both managers call it.
- **D3. Cross-room lookups get their own spec.** `IsInAnyRoom`, `FindRoomByConnectionId`, `FindRoomByUserId` and `JoinRoom`'s
  "already in a room" check are recorded in the backlog and are not changed here.
- **D4. Retry limit.** After 100 failed attempts, throw `InvalidOperationException("Could not allocate a room id")`. Nothing is
  registered.

## Tasks

Every task is checked with `dotnet build`, then its own tests, then the full `dotnet test`. The BlackJack flakiness is fixed on
the base branch, so any full-suite failure is real and blocks the task.

Build note: `RoomId` is `init`-only on both room types, so a room must be constructed with its final id. The helper checks
`ContainsKey` first, so it does not build rooms for ids it already knows are taken. It then builds the room and calls `TryAdd`.
If two threads race for the same free id, one extra room object is built and thrown away. That is harmless, but it also means
"exactly one deck-factory call per room" cannot be asserted. The BlackJack stress test instead asserts that each registered room
has its own table and deck instance.

No task touches a per-player DTO. **T2 and T3 change DI singletons that every connection shares** and need the concurrency review.

1. **Shared `RoomIds` helper** (D1, D2, D4)
   - Files: new `BoardGames/Services/RoomIds.cs`, new `BoardGames.Tests/Services/RoomIdsTests.cs`.
   - A static method that registers a new room atomically. It takes the room dictionary, a `Func<string, T>` that builds a room
     for an id, and an optional `Func<int>` id source (default `Random.Shared.Next(10000, 100000)`). It skips ids that are already
     taken, builds the room, calls `TryAdd`, retries on failure, and throws `InvalidOperationException("Could not allocate a room id")`
     after 100 attempts. It keeps no state of its own, so there is no shared mutable state.
   - Tests (new): `Register_ReturnsRegisteredRoomWithFiveDigitId`, `Register_IdTaken_UsesNextId_AndKeepsExistingRoom` (AC3
     at helper level), `Register_AllIdsTaken_ThrowsAfter100Attempts_AndRegistersNothing` (AC4 at helper level; counts calls to the
     id source), `Register_ConcurrentCalls_AllDistinctAndRegistered` (64 × 50 with a 10 s timeout).
   - Nothing calls the helper yet, so behaviour is unchanged. Commit the spec with this task.

2. **`BlackJackRoomManager` uses the helper** (AC1 part, AC2, AC3, AC4). ⚠ Shared singleton: needs concurrency review.
   - Files: `BoardGames/Services/BlackJack/BlackJackRoomManager.cs` (remove the `Random` field; add an optional `Func<int>? nextRoomId`
     constructor parameter; `CreateRoom` calls the helper), `BoardGames.Tests/Services/BlackJack/BlackJackRoomManagerTests.cs`.
   - DI: `Func<int>` is not registered, so production uses the default source. The existing `new()` call sites stay unchanged.
   - Tests (new): `CreateRoom_ConcurrentCalls_AllDistinctAndRegistered` (also asserts that each room has its own `BlackJackTable`,
     the concurrent test left out of the deck spec), `CreateRoom_IdTaken_RetriesWithNextId`,
     `CreateRoom_AllIdsTaken_ThrowsAfterRetryLimit`. The 16 existing tests stay unchanged.

3. **`AvalonRoomManager` uses the helper** (AC1 part, AC2, AC3, AC4). ⚠ Shared singleton: needs concurrency review.
   - Files: `BoardGames/Services/Avalon/AvalonRoomManager.cs` (same change; keep the `maxPlayers` clamp to 5-10 before the helper
     runs), `BoardGames.Tests/Services/Avalon/AvalonRoomManagerTests.cs`.
   - Tests (new): the same three as T2, for Avalon. The 13 existing tests, including `CreateRoom_GeneratesUniqueRoomIds`, stay
     unchanged. The demo room (`AvalonHub.CreateDemoRoom`) goes through the same `CreateRoom`, and the existing
     `DemoBotServiceIntegrationTests` cover it.

4. **Guard test and final verification** (AC1, AC5, AC6)
   - Files: new `BoardGames.Tests/Services/RoomManagerStructureTests.cs`.
   - Test (new): `RoomManagers_HaveNoRandomInstanceField`. Reflection over both manager types finds no instance field of type
     `System.Random`.
   - Verification (no code): the full `dotnet test` passes 20 runs in a row, and `git diff` shows no edits to existing test methods.
