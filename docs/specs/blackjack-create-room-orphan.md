# BlackJack: a refused CreateRoom must not leave an empty room

## Problem

`BlackJackHub.CreateRoom` (`BlackJackHub.cs:199-217`) creates and registers the room first (`roomManager.CreateRoom`, `:205`).
Only then, inside the lock, does it call `roomManager.JoinRoom` (`:209`), which throws "Player is already in a room" when the
connection is seated elsewhere. The throw leaves the new room behind: registered in the manager's `ConcurrentDictionary`, with no
players, no host, and no timer to clean it up. It stays until the server restarts.

The realistic trigger is the same as in Avalon: a double click on Create (the lobby button has no pending state). The second call
is refused correctly, but it leaks one empty room each time. The connection was also added to the orphan room's SignalR group at
`:208` before the throw. Nothing broadcasts to that group, so nobody sees it, but the membership is stale.

This affects nobody visibly today. The problem is the leaked rooms and ids, and the pattern differs from Avalon, which now checks
before creating (`avalon-create-room-check`).

## Scope

**In scope**
- `IsInAnyRoom(connectionId)` goes on `IBlackJackRoomManager`. It exists on the class already, but hubs cannot call it.
- `BlackJackHub.CreateRoom` checks it **before** `roomManager.CreateRoom` and throws the same "Player is already in a room".
  The existing checks keep their order: the `maxPlayers` range first, then the balance check.

**Out of scope**
- The `JoinRoom` check inside `CreateRoom`. It stays as it is, and cannot fire any more for a fresh room.
- Per-account checks, as decided for Avalon (D2 there).
- Any front-end pending state on the button.

## Behaviour

Players see no change:
- A connection that is not in any room creates a room exactly as today.
- A seated connection gets "Player is already in a room", as today.

The only difference: no room is created and the caller is not added to any group. Other players see nothing, as before.

## Edge cases

- **Double click on Create:** the first call creates and seats; the second is refused and creates nothing. SignalR runs a
  connection's invocations one after another, so the check and the create cannot interleave.
- **Invalid `maxPlayers` or insufficient balance:** refused by the existing checks, which still come first and also create
  nothing.
- **After `LeaveRoom` or being kicked:** the connection is not in any room, so creating works.
- **Concurrent creates from different connections:** unaffected.

## Acceptance criteria

1. Hub unit test: with `IsInAnyRoom(conn)` true, `CreateRoom` throws "Player is already in a room" and never calls
   `roomManager.CreateRoom` (Moq `Times.Never`). This fails on today's code.
2. Integration: the same connection creates twice. The second call fails with the same message; the first room still works (a
   second player can join it).
3. `IBlackJackRoomManager` declares `IsInAnyRoom`, and the existing `BlackJackRoomManager` implementation is unchanged.
4. Existing BlackJack tests pass unchanged. The full suite passes 20 runs in a row.

## Test plan

| AC | Proof |
|----|-------|
| 1 | `BlackJackHubTests.CreateRoom_WhenAlreadyInARoom_ThrowsAndCreatesNothing` (checked red against the old hub) |
| 2 | `BlackJackHubIntegrationTests.CreateRoom_Twice_SecondIsRefused_FirstStillWorks` |
| 3 | Compile. AC1 mocks it through the interface. |
| 4 | Run the existing suites, then the full suite 20 times. |

## Open questions

None. The rule and the error text match `JoinRoom` and the Avalon decisions (refuse, per connection).

## Tasks

Every task is checked with `dotnet build`, then its own tests, then the full `dotnet test`. No task touches a per-player DTO or adds
shared state.

1. **Check before creating** (AC1 to AC3)
   - Files: `BoardGames/Services/BlackJack/IBlackJackRoomManager.cs` (declare `IsInAnyRoom`), `BoardGames/Hubs/BlackJack/BlackJackHub.cs`
     (the check comes after the balance check and before `roomManager.CreateRoom`), `BoardGames.Tests/Hubs/BlackJack/BlackJackHubTests.cs`,
     `BoardGames.Tests/Integration/BlackJackHubIntegrationTests.cs`.
   - Tests (new): `CreateRoom_WhenAlreadyInARoom_ThrowsAndCreatesNothing` and `CreateRoom_Twice_SecondIsRefused_FirstStillWorks`.
   - Commit the spec with this task.

2. **Final verification** (AC4; no code)
   - The full suite passes 20 runs in a row. `git diff main` shows no edits to existing test methods. Update the backlog memory.
