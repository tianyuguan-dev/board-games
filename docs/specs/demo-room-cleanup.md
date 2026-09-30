# Avalon demo rooms: remove the room when the human is gone

## Problem

`AvalonHub.CreateDemoRoom` (`AvalonHub.cs:284-331`) seats one human at seat 2 and four bots (`BOT:seat0/1/3/4`, userId 0) in
`Players`, and starts the game straight away. A room is only ever removed when `Players.Count` reaches 0, or at the GameOver
disband. The bots never leave, so a demo room is removed **only if the human clicks Leave at GameOver**. Every other way the human
goes away leaks the room for good:

| How the human goes | What happens today | Leak |
|---|---|---|
| Leave mid-demo | In-game leave branch (`HandlePlayerLeave`): the game is aborted and the human removed. 4 bots remain, so no removal, and a bot becomes host (`LowestSeatConnectionId` → `BOT:seat0`) | yes |
| Guest disconnects or closes the tab (any phase) | `MarkDisconnected` returns null for `userId <= 0`, so **nothing** happens. The human's seat stays, and there is no grace timer | yes |
| Registered user disconnects (any phase) | Held for the grace period (7200 s). After that, the human is removed and a running game is aborted; 4 bots remain, so no removal | yes, after 2 h |
| Leave at GameOver | Disband: `RoomDisbanded` + `RemoveRoom` | no |

Guests can only play the demo (`CreateRoom` refuses them), and the demo is the landing experience, so most demo sessions end
with the tab closed. Each one leaves a room holding 5 seats, a finished or aborted `AvalonGame`, and a room id, until the server
restarts. With the closed-room guard, the bots' own scheduled moves on such rooms stop once a game ends. The rooms themselves
stay.

## Scope

**In scope**
- A demo room is removed as soon as its human is gone for good (D1 defines "gone").
- The shared "is this a demo whose human has left?" check lives in `AvalonRoom`, so it can be unit-tested.

**Out of scope**
- The scripted demo itself: roles, seats, bot timing, and how it ends.
- Multiplayer rooms: they keep all their rules.
- Admin or monitoring views of room counts.

## Behaviour

What the human sees does not change: leaving returns them to the lobby, and a closed tab is simply gone. There are no other players
to notify, because bots have no connection. The server side changes:
- **Leave (any phase):** the room is removed under the lock, right away. The leave path for demo rooms skips the multiplayer
  in-game branch. Nothing visible changes: today that branch first removes the leaver from the SignalR group
  (`Groups.RemoveFromGroupAsync`), so its `GameAborted` / `PlayerLeft` reach only the bots, which have no connection, and
  `SendBalancesToAll` skips userId 0. So the human already receives nothing, and removing silently keeps it that way.
- **Guest disconnect (any phase):** the room is removed right away. Guests cannot rejoin anyway: `Rejoin` refuses guests.
- **Registered user disconnect** (D1):
  - **Chosen (a):** Keep today's grace: the seat is held, so a refresh or `Rejoin` resumes the demo. When the grace expires
    with the human still gone, `CheckDisconnectedPlayer` removes the room instead of leaving the bots behind.
  - Not chosen (b): remove the room right away, like a guest.
- **GameOver:** unchanged for Leave (disband). A disconnect at GameOver follows the rows above, so a guest's room is removed right
  away and a registered user's room at grace expiry.
- **Bots:** nothing to do for them. Once the room is closed, their pending moves stop (closed-room guard, `DemoBotService.WithLock`).

## Edge cases

- **The human leaves while a bot move is pending:** the move queues on the lock, finds the room closed, and does nothing.
- **Leave and disconnect overlap:** the first removes the room. The second finds it gone (`FindRoomByConnectionId` null) or
  closed (`WithLockIfOpen`) and does nothing.
- **A registered user refreshes mid-demo (D1):** unchanged. `Rejoin` restores the seat within the grace.
- **A registered user never comes back (D1):** the room is removed at grace expiry, not 2 h plus forever.
- **A guest refreshes:** today's behaviour stays (the rejoin is refused and they land in the lobby); the difference is that the old
  room is now removed.
- **Room-id reuse:** a removed demo id can be reused, and stale bot moves hold the closed object.

## Acceptance criteria

1. `AvalonRoom.IsAbandonedDemo()` is true for a demo room whose `Players` are all `BOT:` connections **and** whose
   `DisconnectedPlayers` is empty, meaning no human is seated or waiting to rejoin. It is false for any room with a seated or
   disconnected human, and for non-demo rooms.
2. Integration: a guest creates a demo and calls `LeaveRoom` mid-game, so the room is removed (`IAvalonRoomManager.GetRoom` returns
   null).
3. Integration: a guest creates a demo and disconnects, so the room is removed within 5 s.
4. Integration (D1, `FastTimerWebApplicationFactory` with a 1 s grace): a registered user creates a demo and disconnects. The room
   still exists straight after, and is removed after the grace.
5. Integration (D1): a registered user disconnects mid-demo and `Rejoin`s within the grace. The demo continues (`Rejoin` returns
   and the room still exists).
6. Leave at GameOver still disbands (existing behaviour, regression test).
7. Existing tests, including `DemoBotServiceIntegrationTests`, pass unchanged. The full suite passes 20 runs in a row.

## Test plan

| AC | Proof |
|----|-------|
| 1 | `AvalonRoomDisconnectTests` (new): `IsAbandonedDemo_OnlyBots_True`, `_HumanSeated_False`, `_HumanWaitingToRejoin_False`, `_NonDemoRoom_False` |
| 2 | `DemoBotServiceIntegrationTests.GuestLeavesMidDemo_RoomRemoved` (gets the manager from `_factory.Services`) |
| 3 | `DemoBotServiceIntegrationTests.GuestDisconnectsMidDemo_RoomRemoved`. Polls `GetRoom` for up to 5 s, then fails if the room is still there. |
| 4 | `AvalonDisconnectGraceTests.RegisteredUserLeavesDemo_RoomRemovedAfterGrace` |
| 5 | `AvalonDisconnectGraceTests.RegisteredUserRejoinsDemoWithinGrace_DemoContinues`. The rejoin must happen well inside the 1 s grace, using the same "connection started first" technique as the grace-timer tests. |
| 6 | `DemoBotServiceIntegrationTests.LeaveAtGameOver_Disbands`, or the existing test if one covers it |
| 7 | Run the existing suites, then the full suite 20 times. |

## Decisions (2026-09-30)

- **D1. A registered user keeps the grace.** A refresh or `Rejoin` resumes the demo; at grace expiry the room is removed. A guest
  disconnect or any Leave removes the room right away.

## Tasks

Every task is checked with `dotnet build`, then its own tests, then the full `dotnet test`. Any failure blocks the task.
No task touches a per-player DTO. All removals run under the room lock, through `RemoveRoom`, which also closes the room, so
pending bot moves stop. No new shared state.

1. **`AvalonRoom.IsAbandonedDemo()`** (AC1)
   - Files: `BoardGames/Models/Avalon/AvalonRoom.cs`, `BoardGames.Tests/Models/Avalon/AvalonRoomDisconnectTests.cs`.
   - Tests (new): the four `IsAbandonedDemo_*` cases.
   - Nothing calls it yet. Commit the spec with this task.

2. **Leave removes a demo room** (AC2, AC6)
   - Files: `BoardGames/Hubs/Avalon/AvalonHub.cs`. In `HandlePlayerLeave`, for `room.IsDemo`, remove the human and remove the
     room, before the GameOver and in-game branches. Also `BoardGames.Tests/Integration/DemoBotServiceIntegrationTests.cs`.
   - Tests (new): `GuestLeavesMidDemo_RoomRemoved` (checked red on the old code).
   - *As built:* no separate `LeaveAtGameOver_Disbands`. The demo branch runs before the GameOver and in-game branches, so a
     GameOver leave takes the same code path as the mid-demo leave that the test covers. Reaching GameOver needs the full scripted
     demo (~10 s of bot delays), and it would prove nothing more. For a demo, both paths end in `RemoveRoom`; `RoomDisbanded` was
     only ever seen by bots, because the leaver is removed from the group first.

3. **Disconnect removes a demo room: guests at once, registered users at grace expiry** (D1, AC3, AC4, AC5)
   - Files: `BoardGames/Hubs/Avalon/AvalonHub.cs`. In `HandlePlayerDisconnect`, if a demo's human is a guest (`MarkDisconnected`
     would return null), remove the room. In `CheckDisconnectedPlayer`, after a successful expiry, if `room.IsAbandonedDemo()`,
     remove the room and return, skipping the multiplayer abort and cleanup. Also
     `BoardGames.Tests/Integration/DemoBotServiceIntegrationTests.cs` and `BoardGames.Tests/Integration/AvalonDisconnectGraceTests.cs`.
   - Tests (new): `GuestDisconnectsMidDemo_RoomRemoved`, `RegisteredUserLeavesDemo_RoomRemovedAfterGrace`,
     `RegisteredUserRejoinsDemoWithinGrace_DemoContinues`. Each is checked red where it applies.

4. **Final verification** (AC7; no code)
   - The full suite passes 20 runs in a row. `git diff main` shows no edits to existing test methods. Update the backlog memory.
