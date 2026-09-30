# BlackJack: unique seats on join

## Problem

`BlackJackRoomManager.JoinRoom` gives a new player seat `players.Count` (`BlackJackRoomManager.cs:53`). Seats only stay dense
(0..n-1) until someone leaves, because leaving, kicking and the broke-player kick all remove a seat without compacting the rest:
- `HandlePlayerLeave`, `BlackJackHub.cs:421`
- `KickPlayer`, `:528`
- `TurnTimerService.SettleIfFinished`, `TurnTimerService.cs:204`

After that, the next joiner can get a seat that is already taken. For example:

> Seats A=0, B=1, C=2. B leaves in the lobby, so Count is 2. D joins and gets **seat 2, the same as C**.

Joins are allowed in the lobby and after a round has finished (`JoinRoom` rejects only rounds in progress), so this happens in
normal play. The shared seat lasts until the next `StartGame` compacts seats with `ReassignSeats`. Until then:
- **The host kicks the wrong player.** The lobby list sends `KickPlayer(roomId, p.seatIndex)` (`Game.jsx:171`), and the hub
  kicks `Players.FirstOrDefault(p => p.Value == seatIndex)` (`BlackJackHub.cs:522`). With two players on seat 2, clicking Kick on
  D can remove C. Since PR #4, `Players` is a `ConcurrentDictionary`, so which one gets kicked is arbitrary.
- **The lobby list order between the two is arbitrary.** It is sorted by seat (`BlackJackHub.cs:90`), and the tie can flip.
- **Seating at the next start is arbitrary between them.** `ReassignSeats` orders by seat, and the tie decides who plays first.
- **Host handover can pick either of them** when they hold the lowest seat.

Avalon fixed the same bug by giving the lowest free seat (`AvalonRoomManager.JoinRoom`, whose comment explains exactly this
collision).

**A gap from the previous spec, fixed here too:** `cross-room-lookup-thread-safety` (D2) moved host handover to "lowest seat" in
5 places, but missed a sixth one. `TurnTimerService.cs:209` still does `room.Players.Keys.FirstOrDefault()` when it kicks a broke
host after a round. Since PR #4 made `Players` a `ConcurrentDictionary`, that picks an arbitrary player instead of the lowest
seat.

## Scope

**In scope**
- `JoinRoom` never hands out an occupied seat: it gives the lowest free seat (D1).
- The broke-host handover in `TurnTimerService` uses the lowest seat, like the other 5 sites. A shared
  `BlackJackRoom.LowestSeatConnectionId()` is used by both BlackJack sites, mirroring `AvalonRoom`.

**Out of scope**
- Compacting seats when someone leaves in the lobby (D2). Seats keep their gaps until `StartGame`, as today; gaps are harmless
  once seats are unique.
- Mid-round behaviour. Joins are blocked while a round is in progress. A mid-round leave keeps the other players' seats, which
  are the game's player indices, as today.
- The front end. Seat numbers are not shown; the list is ordered by seat and Kick sends the seat.

## Behaviour

What players see, for every player and the host:
- A new player joins the lobby, or a finished round, at a seat nobody else holds. The lobby list (`RoomUpdate`) shows them at that
  seat's position, sorted by seat.
- Kick removes exactly the player whose row the host clicked.
- When the host is kicked for insufficient balance after a round, the remaining player with the lowest seat becomes host. The
  same rule already applies when the host leaves.
- `StartGame` compacts seats in seat order (unchanged since PR #4). Seats are now unique, so the resulting turn order is
  deterministic.

Messages (`PlayerJoined`, `RoomUpdate`, `PlayerLeft`, `Kicked`, `StartGame`, `YourSeat`) and their payloads are unchanged.
No per-player DTO field is added.

## Edge cases

- **Join into a full room:** it is still rejected with the existing message. The seat is chosen only after the capacity check.
- **Several joins at once:** each join runs under the target room's lock (`BlackJackHub.cs:226`), so seat choices are serialized
  and cannot collide.
- **Join after a finished round in which players left or were kicked:** this gets a free seat as well. The next `StartGame`
  compacts seats in seat order.
- **Leave during a round:** unchanged. The seat is freed; joins stay blocked until the round finishes.
- **Kick a seat nobody holds:** "Player not found at that seat", as today.
- **Broke-kick of the host when every other player is also broke:** the room empties and is removed (existing branch). Host
  handover returns null, as today.
- **Reconnect:** BlackJack has no reconnect flow. A returning player simply joins again.

## Acceptance criteria

1. With seats {0, 1, 2}, the player on 1 leaves and a new player joins. All seats are distinct, and the new player gets seat **1**
   (D1).
2. Over any sequence of joins, leaves and kicks in the lobby (a seeded random sequence of 200 operations, max 7 players), seats
   are always unique.
3. Integration: A (host), B and C join; B leaves; D joins; the host kicks D's seat (read from `RoomUpdate`). D gets `Kicked`,
   C stays in the room, and the next `RoomUpdate` lists A and C.
4. `BlackJackRoom.LowestSeatConnectionId()` returns the connection with the lowest seat, or null for an empty room. Both
   `TransferHostIfNeeded` and the `TurnTimerService` broke-kick use it.
5. Existing BlackJack tests pass unchanged. The full suite passes 20 runs in a row.

## Test plan

| AC | Proof |
|----|-------|
| 1 | `BlackJackRoomManagerTests.JoinRoom_AfterLeave_GetsFreeSeat` |
| 2 | `BlackJackRoomManagerTests.JoinLeaveKickSequence_SeatsStayUnique` (fixed `Random(42)` seed, so it is deterministic) |
| 3 | `BlackJackHubIntegrationTests.KickAfterRejoinGap_KicksTheChosenPlayer` |
| 4 | `BlackJackRoomTests.LowestSeatConnectionId_PicksLowestSeat` and `_EmptyRoom_ReturnsNull`. For the timer call site: code review, plus a grep in the review that no `Players.Keys.First` is left anywhere in `BoardGames/`. A broke-host integration test would need scripted losing hands and balances, which is much more setup for a one-line call. |
| 5 | Run the existing suites, then the full suite 20 times. |

## Decisions (2026-09-30)

- **D1. A new joiner gets the lowest free seat**, as in Avalon.
- **D2. No compaction on lobby leave or kick.** Gaps stay until `StartGame` compacts in seat order.

## Tasks

Every task is checked with `dotnet build`, then its own tests, then the full `dotnet test`. Any failure blocks the task.
No task touches a per-player DTO. Every change runs under the room's lock (JoinRoom, TransferHostIfNeeded, SettleIfFinished), and
no shared collection changes type.

1. **`BlackJackRoom.LowestSeatConnectionId()` for both host-transfer sites** (AC4)
   - Files: `BoardGames/Models/BlackJack/BlackJackRoom.cs`, `BoardGames/Hubs/BlackJack/BlackJackHub.cs`
     (`TransferHostIfNeeded` uses the helper, with the same result), `BoardGames/Services/BlackJack/TurnTimerService.cs:209`
     (the site the cross-room spec missed), `BoardGames.Tests/Models/Blackjack/BlackJackRoomTests.cs`.
   - Tests (new): `LowestSeatConnectionId_PicksLowestSeat`, `LowestSeatConnectionId_EmptyRoom_ReturnsNull`. Existing:
     `HostLeaves_LowestSeatBecomesHost`.
   - Check: `grep -rn "Keys.First" BoardGames/Hubs BoardGames/Services BoardGames/Models` returns nothing.
   - Commit the spec with this task.

2. **`JoinRoom` gives the lowest free seat** (D1, AC1, AC2, AC3)
   - Files: `BoardGames/Services/BlackJack/BlackJackRoomManager.cs`, `BoardGames.Tests/Services/BlackJack/BlackJackRoomManagerTests.cs`,
     `BoardGames.Tests/Integration/BlackJackHubIntegrationTests.cs`.
   - Tests (new): `JoinRoom_AfterLeave_GetsFreeSeat`, `JoinLeaveKickSequence_SeatsStayUnique`,
     `KickAfterRejoinGap_KicksTheChosenPlayer`. Existing `JoinRoom_*` tests pass unchanged: with no gaps, the lowest free seat
     equals Count.

3. **Final verification** (AC5; no code)
   - The full suite passes 20 runs in a row. `git diff main` shows no edits to existing test methods. Update the backlog memory.
