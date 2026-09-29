# Thread-safe cross-room lookups

## Problem

The room managers answer "which room is this connection / user in?" by scanning every room's membership collections. Those
collections are plain `Dictionary`s. Their writes happen under each room's own lock, but the scans take no lock, or only the lock
of a *different* room. This breaks the CLAUDE.md rule "Shared collections outside a room lock must be thread safe". A
`Dictionary` read while another thread writes to it can throw, return a wrong answer, or (during a resize) loop.

The collections that are read from outside their room's lock (the declarations come first, all `{ get; set; }`):
- `AvalonRoom.Players` (`Models/Avalon/AvalonRoom.cs:15`)
- `AvalonRoom.PlayerUserIds` (`:17`)
- `AvalonRoom.DisconnectedPlayers` (`:62`)
- `BlackJackRoom.Players` (`Models/BlackJack/BlackJackRoom.cs:9`)

The unlocked readers:

| Reader | Where it runs | Lock held |
|---|---|---|
| `AvalonRoomManager.JoinRoom` "already in a room" scan (`:28`) | `AvalonHub.JoinRoom` `:352` | the **target** room's only |
| `BlackJackRoomManager.IsInAnyRoom` via `JoinRoom` (`:28,:33`) | `BlackJackHub` `:209`, `:230` | the **target** room's only |
| `AvalonRoomManager.FindRoomByConnectionId` (`:52-56`) | `AvalonHub` `:846` for disconnect (`:775`) and leave (`:801`) | none |
| `BlackJackRoomManager.FindRoomByConnectionId` (`:58`) | `BlackJackHub.HandlePlayerLeave` `:414` (leave and disconnect) | none |
| `AvalonRoomManager.FindRoomByUserId` (`:75-80`) | `AvalonHub.GetActiveRoom` `:208` | none |
| `FindAndRemoveByConnectionId` (both managers) | **no production callers**. It also writes `Players.Remove` outside any lock | none |

Every write to those four collections is under the room's own lock. This was checked across the hubs, the models,
`TurnTimerService` and `DemoBotService`, including the reconnect grace timer, which re-locks before writing. So the fix is about the
readers.

Who it affects: any two players doing room things at the same time in different rooms. One disconnects while another joins,
creates or checks their active room. It is rare, but the failure is an unhandled exception in a disconnect handler, or a wrong
"you are in room X" answer.

A second, related gap: Avalon's `HandlePlayerLeave` does not re-check, after it takes the lock, that the connection is still in
the room (`AvalonHub.cs:804` onward). BlackJack does (`BlackJackHub.cs:420`). Because the lookup happens before the lock, two
leave events for one connection can both run the leave logic. That can happen when `LeaveRoom` and `OnDisconnectedAsync` overlap.

## Scope

**In scope**
- Make the four collections above `ConcurrentDictionary` (D1).
- Keep everything that depended on `Dictionary` enumeration order behaving the same. This means host transfer in 5 places and
  BlackJack `ReassignSeats`. Those now pick by lowest seat (D2).
- Avalon `HandlePlayerLeave` returns early if the connection is no longer in the room once the lock is held, like BlackJack (D4).
- Remove `FindAndRemoveByConnectionId` from both managers and interfaces, together with its 4 tests (D3).

**Out of scope** (to the backlog, D5)
- Avalon `CreateRoom` / `CreateDemoRoom` skip the "already in a room" check that `JoinRoom` does, so one connection could be in
  two Avalon rooms at once.
- A thread that looked a room up and is waiting on its lock can get the lock after the room was removed (`RemoveRoom`), and then
  act on a room nobody can find. No handler re-checks `GetRoom` after locking.
- Collections only ever read under their own room's lock (`SeatToConnection`, `PlayerNicknames`, `ReadyPlayers`,
  `NightConfirmedPlayers`, BlackJack `PlayerUserIds`) stay as they are.
- Game rules, DTOs and messages.

## Behaviour

Players see no change, whatever their role, with one exception: when the host leaves, the lowest seat becomes host (D2).

- **Collections (D1):** the four collections become `ConcurrentDictionary<...>`. Readers on other threads then get
  a consistent answer or a slightly stale one, never an exception or a torn read. Writers stay under the room lock, so the room's
  own logic is unchanged.
  - Call sites change mechanically: `Add` → `TryAdd`, `Remove` → `TryRemove`. Whole-collection reassignments (`ReassignSeats`,
    `Players = newPlayers`) build a new `ConcurrentDictionary`; that reference swap is atomic, so a reader sees the old or the new
    collection and nothing in between.
- **Order-sensitive code (D2):** `ConcurrentDictionary` has no reliable enumeration order. Today "first key" is roughly "earliest
  joiner still here", and even that is not guaranteed after removals. Every place that picks by enumeration order will pick by
  **lowest seat** instead:
  - host transfer at `AvalonHub.cs:827`, `:861` and `:907`, `AvalonRoom.cs:84` (`MarkDisconnected`), and `BlackJackHub.cs:408`;
  - `BlackJackRoom.ReassignSeats` (`:41`), which must compact seats in current seat order the way Avalon's does (`OrderBy(p => p.Value)`).
- **Stale lookups are fine:** a scan may say "room X" for a connection that just left. Every caller then takes room X's lock and
  checks membership: BlackJack at `:420`, Avalon disconnect through `MarkDisconnected` returning null, and Avalon leave after this
  change. So a stale answer never mutates anything.
- **Demo bots:** bot connection ids (`BOT:seat{n}`) repeat across demo rooms. Nothing ever looks a bot up by connection id, and
  this change does not add such a lookup.

## Edge cases

- **Disconnect while another player joins a different room:** the join's scan reads the disconnecting room's `Players` during its
  `TryRemove`. There is no exception; the scan sees the player or it does not, and both are correct at that moment.
- **Leave and disconnect overlapping for one connection:** both look up the room. The first one to take the lock removes the
  player; the second re-checks, finds nothing and returns. No double `PlayerLeft`, no second disband or seat reassignment.
- **Reconnect (`TryRejoin`) swaps a connection id under the lock:** a concurrent `FindRoomByUserId` sees the user through
  `PlayerUserIds` (old or new key) or through `DisconnectedPlayers`. In every case it returns the right room.
- **Grace timer expiry vs rejoin:** unchanged. Both run under the room lock.
- **Room being filled right after `RoomIds.Register` publishes it:** a scan sees an empty or partly filled `ConcurrentDictionary`,
  which is safe.
- **Host leaves:** the remaining player with the lowest seat becomes host (D2), whatever order people joined in.
- **Empty room:** host transfer gets null, as today (`FirstOrDefault` at `:907`, `AvalonRoom.cs:84`). The `First()` calls at `:827`
  and `:861` only run when players remain (the existing guards stay).

## Acceptance criteria

1. `AvalonRoom.Players`, `AvalonRoom.PlayerUserIds`, `AvalonRoom.DisconnectedPlayers` and `BlackJackRoom.Players` are
   `ConcurrentDictionary`.
2. Stress: for 3 s, writers keep joining and removing players in rooms A and B, taking each room's `Lock` as the hub does.
   Meanwhile readers call `FindRoomByConnectionId`, `FindRoomByUserId` (Avalon) and `JoinRoom` into room C for a connection
   that already sits in room D. No exception is thrown. The stable member of room D is always found in D, and joining C always
   fails with "already in a room". This is checked for both managers.
3. `BlackJackRoom.ReassignSeats` compacts seats in seat order: players at seats {5, 0, 2} (added in that order) end up at
   {2, 0, 1}, so the player at seat 0 stays at 0, the one at 2 moves to 1, and the one at 5 moves to 2.
4. When the host leaves or is dropped, the remaining player with the lowest seat becomes host. This holds for BlackJack leave,
   Avalon lobby leave, Avalon in-game leave, Avalon grace-timeout removal, and `AvalonRoom.MarkDisconnected`.
5. Avalon: a leave for a connection that is no longer in the room, once the lock is held, changes nothing and sends nothing.
6. `FindAndRemoveByConnectionId` no longer exists on either manager or interface (D3). Its tests go with it: 3 BlackJack
   tests and 1 Avalon test, removed together with the method.
7. Apart from AC6, existing tests pass unchanged.
8. The full `dotnet test` suite passes 20 runs in a row.

## Test plan

| AC | Proof |
|----|-------|
| 1 | Reflection guard `RoomMembershipCollections_AreConcurrent` over the four properties, in `RoomManagerStructureTests`. |
| 2 | `AvalonRoomManagerTests.CrossRoomLookups_DuringConcurrentMembershipChanges_DoNotThrow` and the BlackJack twin: `Task.WhenAll` of writer and reader loops with a 3 s deadline, asserting the invariants on every read. As in the last spec, this checks that the new code holds up; it cannot reliably make the old code fail, and AC1 carries that part of the proof. |
| 3 | `BlackJackRoomTests.ReassignSeats_CompactsInSeatOrder`. |
| 4 | Unit tests: `AvalonRoomTests.MarkDisconnected_Host_LowestSeatBecomesHost`. Integration tests: `BlackJackHubIntegrationTests.HostLeaves_LowestSeatBecomesHost`, and in `AvalonHubIntegrationTests`, `HostLeavesLobby_LowestSeatBecomesHost` and `HostLeavesMidGame_LowestSeatBecomesHost`. The grace-timeout path goes in `AvalonDisconnectGraceTests.HostGraceExpires_LowestSeatBecomesHost` (`FastTimerWebApplicationFactory`, 1 s timer). Seats are set up so that join order differs from seat order (via `ReorderPlayer`/`MovePlayer`). |
| 5 | `AvalonHubIntegrationTests.LeaveRoom_Twice_SecondIsNoOp`: the second `LeaveRoom` causes no second `RoomUpdate`/`PlayerLeft` and no error. The true race (two lookups before either lock) cannot be forced from a test, so the early-return line is also covered by code review. |
| 6 | Compile: the interfaces no longer declare the method. The removed tests are listed in the commit message, per CLAUDE.md ("explain the conflict"). |
| 7 | The full suite; `git diff` shows no other edits to existing tests. |
| 8 | Run the full suite 20 times and report the counts. |

## Decisions (2026-09-30)

- **D1. `ConcurrentDictionary`** for `AvalonRoom.Players`, `AvalonRoom.PlayerUserIds`, `AvalonRoom.DisconnectedPlayers` and
  `BlackJackRoom.Players`. No separate index.
- **D2. The new host is the remaining player with the lowest seat.** This applies to all 5 host-transfer sites. BlackJack
  `ReassignSeats` compacts in seat order.
- **D3. Delete `FindAndRemoveByConnectionId`** from both managers and interfaces, together with its 4 tests. The commit message
  explains why: the tests only exist for this dead method.
- **D4. Avalon `HandlePlayerLeave` re-checks membership** once it holds the lock, and returns early if the connection is gone.
- **D5. Backlog:** (1) Avalon `CreateRoom`/`CreateDemoRoom` skip the "already in a room" check; (2) handlers can act on a room that
  was removed while they waited for its lock. Each is its own backlog item.

## Tasks

Every task is checked with `dotnet build`, then its own tests, then the full `dotnet test`. Any failure blocks the task.

**Build note: drop-in replacement.** `ConcurrentDictionary` has no public `Add(key, value)`, `Remove(key)` or `ContainsValue`.
The code uses those on these collections in 24 production places, and in 29 places in existing tests. Instead of rewriting all
of them, T1 adds a small `MembershipMap<TKey, TValue> : ConcurrentDictionary<TKey, TValue>` that provides the three with the same
meaning as `Dictionary` (`Add` throws on a duplicate key). The type swap then touches only the declarations and the
whole-collection reassignments, and AC7 ("existing tests unchanged") stays true.

**Ordering: behaviour first, types second.** T2 and T3 switch host transfer and BlackJack seat compaction to "lowest seat"
while the collections are still `Dictionary`. That keeps the one behaviour change (D2) in its own reviewable commit. The type
swaps in T4 and T5 are then purely mechanical.

No task touches a per-player DTO. **T4 and T5 change room state that is shared across connections** and need the concurrency
review.

1. **`MembershipMap<TKey, TValue>`** (D1 groundwork)
   - Files: new `BoardGames/Models/MembershipMap.cs`, new `BoardGames.Tests/Models/MembershipMapTests.cs`.
   - A subclass of `ConcurrentDictionary`. `Add` throws `ArgumentException` on a duplicate key, like `Dictionary`. `Remove(key)`
     returns `bool` through `TryRemove`. `ContainsValue` enumerates without taking all the locks.
   - Tests (new): `Add_DuplicateKey_Throws`, `Remove_ReturnsWhetherRemoved`, `ContainsValue_FindsValue`,
     `ConcurrentAddRemoveAndReads_DoNotThrow`.
   - Nothing uses it yet. Commit the spec with this task.

2. **BlackJack: host transfer and seat compaction go by lowest seat** (D2, AC3, AC4 BlackJack part). Behaviour change.
   - Files: `BoardGames/Hubs/BlackJack/BlackJackHub.cs` (`:408`, the new host is the remaining player with the lowest seat),
     `BoardGames/Models/BlackJack/BlackJackRoom.cs` (`ReassignSeats` iterates `OrderBy(seat)`),
     `BoardGames.Tests/Models/Blackjack/BlackJackRoomTests.cs`, `BoardGames.Tests/Integration/BlackJackHubIntegrationTests.cs`.
   - Tests (new): `ReassignSeats_CompactsInSeatOrder` (seats {5, 0, 2} added in that order → {2, 0, 1}),
     `HostLeaves_LowestSeatBecomesHost`. Today, `Dictionary` order equals seat order in that test, so it passes before and after;
     it guards the rule once T4 removes any order.

3. **Avalon: host transfer goes by lowest seat** (D2, AC4 Avalon part). Behaviour change.
   - Files: `BoardGames/Hubs/Avalon/AvalonHub.cs` (`:827`, `:861`, `:907`), `BoardGames/Models/Avalon/AvalonRoom.cs` (`:84`
     `MarkDisconnected`), `BoardGames.Tests/Models/Avalon/AvalonRoomDisconnectTests.cs`,
     `BoardGames.Tests/Integration/AvalonHubIntegrationTests.cs`, `BoardGames.Tests/Integration/AvalonDisconnectGraceTests.cs`.
   - Tests (new): `MarkDisconnected_Host_LowestSeatBecomesHost`, `HostLeavesLobby_LowestSeatBecomesHost`,
     `HostLeavesMidGame_LowestSeatBecomesHost`, and `HostGraceExpires_LowestSeatBecomesHost` (with `FastTimerWebApplicationFactory`).
     Each test uses `ReorderPlayer` / `MovePlayer` so that join order differs from seat order. That way it fails on today's
     "first key" rule and passes on the new rule.
   - **As built:** only `MarkDisconnected_Host_LowestSeatBecomesHost` and `HostLeavesLobby_LowestSeatBecomesHost` fail on the
     old rule (checked). The two mid-game tests pass on both rules, because `StartGame` → `ReassignSeats` rebuilds `Players` in
     seat order, and a `Dictionary` re-add after a reconnect reuses the freed slot. They stay as end-to-end regression tests:
     after T5 removes any order, they guard the mid-game paths. The grace test is named `HostDisconnectsMidGame_...`, because the
     host moves at disconnect time (`MarkDisconnected`), not at grace expiry. It does not wait for `GameAborted`: that step depends on
     a pre-existing grace-timer bug (`Task.Delay` can finish up to ~0.6 ms before `DateTime.UtcNow` says the grace has passed, so
     `CheckDisconnectedPlayer` returns early and never retries). The bug was measured at 50/1000 early completions and is recorded in
     the backlog.

4. **`BlackJackRoom.Players` becomes `MembershipMap`** (D1, AC1 part). ⚠ Shared room state: needs concurrency review.
   - Files: `BoardGames/Models/BlackJack/BlackJackRoom.cs` (declaration, constructor, `ReassignSeats` builds a `MembershipMap`).
   - Review points: every write is still under `room.Lock` (the subagent inventory lists them). `IsInAnyRoom` and
     `FindRoomByConnectionId` now read a thread-safe map. Nothing else depends on enumeration order after T2.
   - Tests: the existing BlackJack suites, unchanged (`BlackJackRoomTests`, `BlackJackHubTests`, `BlackJackRoomManagerTests`,
     integration tests).

5. **Avalon `Players`, `PlayerUserIds` and `DisconnectedPlayers` become `MembershipMap`** (D1, AC1 part). ⚠ Shared room
   state: needs concurrency review.
   - Files: `BoardGames/Models/Avalon/AvalonRoom.cs` (declarations, `ReassignSeats` and any other reassignment),
     plus any production call site the compiler flags. The expected ones are few, because `MembershipMap` keeps
     `Add`/`Remove`/`ContainsValue`.
   - Review points: `TryRejoin` swaps keys under the lock. `FindRoomByUserId` still finds the user either in `PlayerUserIds`
     (old or new key) or in `DisconnectedPlayers`. `BuildSeatMap` only reads.
   - Tests: the existing Avalon suites, unchanged.

6. **Avalon leave re-checks membership under the lock** (D4, AC5)
   - Files: `BoardGames/Hubs/Avalon/AvalonHub.cs` (`HandlePlayerLeave`: return early if `!room.Players.ContainsKey(connectionId)`
     once `WithLock` is held), `BoardGames.Tests/Integration/AvalonHubIntegrationTests.cs`.
   - Tests (new): `LeaveRoom_Twice_SecondIsNoOp`.

7. **Delete `FindAndRemoveByConnectionId`** (D3, AC6)
   - Files: `IAvalonRoomManager.cs`, `AvalonRoomManager.cs`, `IBlackJackRoomManager.cs`, `BlackJackRoomManager.cs`, and the tests
     that exist only for it: `BlackJackRoomManagerTests.FindAndRemoveByConnectionId_RemovesPlayerAndReturnsRoomId`,
     `_ReturnsNullWhenPlayerNotFound`, `_DoesNotAffectOtherPlayers`, `AvalonRoomManagerTests.FindAndRemoveByConnectionId_RemovesPlayer`,
     plus the mock setup at `BlackJackHubTests.cs:300`, if it is only there for this method.
   - The commit message lists the removed tests and why (dead method), per CLAUDE.md.

8. **Guard, stress tests and final verification** (AC1, AC2, AC7, AC8)
   - Files: `BoardGames.Tests/Services/RoomManagerStructureTests.cs` (new `RoomMembershipCollections_AreConcurrent`),
     `BoardGames.Tests/Services/Avalon/AvalonRoomManagerTests.cs` and `BoardGames.Tests/Services/BlackJack/BlackJackRoomManagerTests.cs`
     (new `CrossRoomLookups_DuringConcurrentMembershipChanges_DoNotThrow`: writers take each room's `Lock`, readers don't,
     3 s deadline).
   - Verification: the full suite passes 20 runs in a row. `git diff main` shows the only removed test lines are those from T7.
