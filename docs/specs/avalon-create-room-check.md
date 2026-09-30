# Avalon: creating a room while already in one

## Problem

Joining a room checks that the connection is not already in any room. `AvalonRoomManager.JoinRoom` scans every room's
`Players` and throws "Player is already in a room" (`AvalonRoomManager.cs:28`). Creating a room does not check at all:
- `AvalonHub.CreateRoom` (`AvalonHub.cs:227-244`) calls `roomManager.CreateRoom` and writes `room.Players` directly.
- `AvalonHub.CreateDemoRoom` (`:261-310`) does the same.

So one connection can sit in two Avalon rooms at once. The realistic way this happens is a **double click on Create**. The
lobby button has no pending state (`AvalonLobby.jsx:22,99`), and SignalR runs one connection's invocations one after another, so
the second `CreateRoom` runs after the first has seated the connection and creates a second room around it. The same goes for
the demo button, or for any client that calls `CreateRoom` while seated.

Once a connection is in two rooms:
- **Disconnect and leave clean up only one of them.** `FindRoomByConnectionId` returns the first room it finds. The rooms live
  in a `ConcurrentDictionary`, so which one is arbitrary. The other room keeps a ghost host that never leaves, and it is never
  removed.
- **`GetActiveRoom` (by user) points at either room**, so "Rejoin room X" in the lobby may send the player to the empty one.
- **The ghost room holds a room id** until the server restarts.

This affects any registered player who double-clicks Create, and guests who double-click the solo demo.

BlackJack refuses this case, because its `CreateRoom` goes through the manager's `JoinRoom`, which checks. But it checks only
*after* `roomManager.CreateRoom` has registered the new room (`BlackJackHub.cs:205-209`), so a refused BlackJack create leaves an
empty room registered for good. That is recorded as a separate backlog item.

## Scope

**In scope**
- `CreateRoom` and `CreateDemoRoom` refuse when the calling connection is already in an Avalon room (D1, D3). The check runs
  **before** a room is created, so a refused call leaves no empty room behind.
- One `IsInAnyRoom(connectionId)` on `IAvalonRoomManager`, used by `JoinRoom`, `CreateRoom` and `CreateDemoRoom`, so the three
  cannot drift apart.

**Out of scope**
- User-level checks, i.e. the same account on a second tab or device in another room (D2). Today `JoinRoom` does not check that
  either.
- A front-end pending/disabled state on the Create and demo buttons. It would be nice UX, but the server must refuse anyway
  (server authority).
- BlackJack's orphan empty room on a refused create (backlog).

## Behaviour

- A connection that is **not** in any Avalon room creates a room or demo exactly as today.
- A connection that **is** in an Avalon room calls `CreateRoom` or `CreateDemoRoom`. The call throws
  `InvalidOperationException("Player is already in a room")`, the same text `JoinRoom` uses. The client's existing `catch` shows it
  as the lobby error. No room is created, and nothing is sent to anyone else.
- A double click: the first click creates the room and seats the player; the second is refused. The player ends up in exactly one
  room, and the UI has already moved to it on the first click.
- Guests calling `CreateRoom` still get the existing "Guests can only play the solo demo" error first. That check stays first.

Which roles see what: only the caller sees the error. Other rooms and their players see nothing.

## Edge cases

- **Double-clicking Create or the demo:** the second call is refused, as above. SignalR runs one connection's invocations one
  after another, so the check and the create cannot interleave for the same connection.
- **After `LeaveRoom`:** the connection is no longer in `Players`, so creating works.
- **After `GameOver` without leaving:** the player is still in the room, so creating is refused until they leave. That matches
  `JoinRoom` today, and the UI's Leave does call `LeaveRoom`.
- **`LeaveRoom` failed on the client, but the client went back to the lobby anyway** (`AvalonGame.jsx:291-292`): the server
  still has the player seated. The lobby then shows "Rejoin room X" instead of Create (`GetActiveRoom`), so this path does not
  reach `CreateRoom` through the UI. If a client calls it anyway, it is refused.
- **Disconnect and reconnect:** the new connection has a new id and is not in any room, so creating works. The old seat follows
  the grace rules.
- **Concurrent create from two different connections:** unaffected. Each checks only itself.

## Acceptance criteria

1. `IAvalonRoomManager.IsInAnyRoom(connectionId)` is true when the connection is in some room's `Players`, and false otherwise.
2. Integration: the same connection calls `CreateRoom` twice. The second call fails with "Player is already in a room", and
   only one room exists for that user (`GetActiveRoom` returns the first room's id).
3. Integration: a connection that joined room A calls `CreateRoom`. It fails with the same message, and A is unchanged.
4. Integration: a connection in a room calls `CreateDemoRoom`. It fails with the same message (D3).
5. Integration: after `LeaveRoom`, the same connection can `CreateRoom` again.
6. A refused create registers no room. Unit test: after the refusal, a scan of the manager finds no extra room.
7. Existing Avalon tests pass unchanged. The full suite passes 20 runs in a row.

## Test plan

| AC | Proof |
|----|-------|
| 1 | `AvalonRoomManagerTests.IsInAnyRoom_TrueOnlyForSeatedConnection` |
| 2 | `AvalonHubIntegrationTests.CreateRoom_Twice_SecondIsRefused` |
| 3 | `AvalonHubIntegrationTests.CreateRoom_WhileInAnotherRoom_IsRefused` |
| 4 | `AvalonHubIntegrationTests.CreateDemoRoom_WhileInRoom_IsRefused` |
| 5 | `AvalonHubIntegrationTests.CreateRoom_AfterLeave_Works` |
| 6 | Covered by AC2's `GetActiveRoom` check, plus code review that the check runs before `roomManager.CreateRoom`. The hub has no room-count API, and a test-only one is not worth adding. |
| 7 | Run the existing suites, then the full suite 20 times. |

## Decisions (2026-09-30)

- **D1. Refuse** with "Player is already in a room". No automatic leave.
- **D2. Check per connection**, matching `JoinRoom`. A per-account rule is a separate, later decision.
- **D3. The solo demo is refused too** while the connection is seated.

## Tasks

Every task is checked with `dotnet build`, then its own tests, then the full `dotnet test`. Any failure blocks the task.
No task touches a per-player DTO or adds shared state. `IsInAnyRoom` only reads the thread-safe `MembershipMap`s, and a
connection's own invocations run one after another.

1. **`IAvalonRoomManager.IsInAnyRoom(connectionId)`** (AC1)
   - Files: `BoardGames/Services/Avalon/IAvalonRoomManager.cs`, `BoardGames/Services/Avalon/AvalonRoomManager.cs` (`JoinRoom`
     calls it instead of its inline scan; same result), `BoardGames.Tests/Services/Avalon/AvalonRoomManagerTests.cs`.
   - Tests (new): `IsInAnyRoom_TrueOnlyForSeatedConnection`. Existing: the `JoinRoom` "already in a room" tests and
     `CrossRoomLookups_DuringConcurrentMembershipChanges_DoNotThrow`.
   - Commit the spec with this task.

2. **`CreateRoom` and `CreateDemoRoom` refuse a seated connection before creating** (D1, D3, AC2 to AC6)
   - Files: `BoardGames/Hubs/Avalon/AvalonHub.cs` (the check comes after the guest check in `CreateRoom` and before
     `roomManager.CreateRoom` in both), `BoardGames.Tests/Integration/AvalonHubIntegrationTests.cs`.
   - Tests (new): `CreateRoom_Twice_SecondIsRefused`, `CreateRoom_WhileInAnotherRoom_IsRefused`,
     `CreateDemoRoom_WhileInRoom_IsRefused`, `CreateRoom_AfterLeave_Works`. Each is checked red against the old hub code.

3. **Final verification** (AC7; no code)
   - The full suite passes 20 runs in a row. `git diff main` shows no edits to existing test methods. Update the backlog memory.
