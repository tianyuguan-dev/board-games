# BlackJack: Hit / Stand / DoubleDown only during the player-turn phase

## Problem

`BlackJackHub.BlackJackPlayerHit` and `BlackJackPlayerStand` (`BlackJackHub.cs:358-398`) accept an action when the caller's seat
equals `game.CurrentPlayerIndex`. They never check that the round is in `PlayerTurn`. `CurrentPlayerIndex` is 0 in two states where
nobody may act:
- **Betting.** `StartGame` creates the round, and `CurrentPlayerIndex` starts at 0 before `Start()` deals.
- **Finished at the deal.** A dealer natural finishes the round inside `Start()`, before `AdvancePastResolvedPlayers`
  (`BlackJackGame.cs:71-80`), so the index stays 0.

In those states seat 0's Hit or Stand gets through. The game model ignores it (`Hit`/`Stand` return unless the state is
`PlayerTurn`), but the hub still:
- broadcasts `PlayerHit` / `PlayerStand` to the whole room with an unchanged state,
- calls `SettleIfFinished` again (harmless, because `TrySetSettled` blocks a second settlement),
- calls `ManageTurnTimer` (a no-op cancel).

`BlackJackPlayerDoubleDown` is already refused there by `CanDoubleDown()` (which checks the state). But the message is wrong for
this case: "Cannot double down after hitting".

**Impact is small.** The UI cannot reach this path: the betting view replaces the action buttons, and the buttons are hidden when
the round is finished (`Game.jsx:222-230,355`). It takes a stale click or a direct hub call, and the result is a redundant
broadcast. The reason to fix it is CLAUDE.md rule 3 ("explicit valid actions" per phase): the server should refuse
out-of-phase actions and not rely on the UI or the model silently ignoring them.

## Scope

**In scope**
- Hit, Stand and DoubleDown refuse unless `game.State == PlayerTurn`, with one clear message (D1), **before** the turn check.
- Nothing is broadcast, settled or re-timed when an action is refused.

**Out of scope**
- `PlaceBet` during `PlayerTurn` (already refused: "Not in betting phase").
- The game model. `BlackJackGame.Hit/Stand/DoubleDown` keep ignoring out-of-phase calls, as a second line of defence.
- The front end (it already hides the buttons).
- The turn timer's auto-stand, which already checks `State != PlayerTurn` (`TurnTimerService.cs:83`).

## Behaviour

For every player, in every phase:

| Phase | Hit / Stand / DoubleDown by the seat at `CurrentPlayerIndex` | By another seat |
|---|---|---|
| Betting | refused: "No turn in progress" (D1) | refused: "No turn in progress" |
| PlayerTurn | allowed, as today | "Not this player's turn", as today |
| Finished (including a dealer natural at the deal) | refused: "No turn in progress" | refused: "No turn in progress" |

`DealerTurn` only exists inside a single locked call, so no invocation can see it.

- The phase check comes after "game not started" and "player not in room", and before the turn check. An out-of-phase call gets
  the phase message, whoever makes it.
- DoubleDown's "Cannot double down after hitting" stays for its real case (`PlayerTurn` with more than two cards).
- Messages and payloads of allowed actions are unchanged. No DTO change.

## Edge cases

- **Dealer natural at the deal:** seat 0's stale Stand is refused. No `PlayerStand` is broadcast, and the settlement already ran.
- **A last Stand races with the turn timer:** unchanged. Both run under the room lock; whichever comes second sees the next
  player's index, or `Finished`, and is refused.
- **Double click on Stand on your last turn:** the second click sees the next index or `Finished`, and is refused.
- **Direct invoke during betting:** refused, with no effect on the betting timer.
- **A player who left or was kicked:** "Player not in room", as today (this check runs first).

## Acceptance criteria

1. In Betting (round created, not dealt), seat 0's Hit, Stand and DoubleDown each throw "No turn in progress", and nothing is sent to
   the group.
2. In a round finished by a dealer natural (stacked deck), seat 0's Hit, Stand and DoubleDown each throw "No turn in progress", and
   nothing is sent. This fails on today's code for Hit and Stand.
3. In `PlayerTurn`, the current seat's Hit, Stand and DoubleDown work as before, and another seat still gets "Not this player's
   turn". This is covered by the existing tests.
4. Existing BlackJack tests pass unchanged. The full suite passes 20 runs in a row.

## Test plan

| AC | Proof |
|----|-------|
| 1 | `BlackJackHubTests.Actions_DuringBetting_AreRefused` (Theory over Hit/Stand/DoubleDown; mock room with a not-started round; verify no `SendCoreAsync`) |
| 2 | `BlackJackHubTests.Actions_AfterDealerNatural_AreRefused` (Theory; `BlackJackGame` built on a stacked `Deck` with a dealer natural). Checked red on the old hub for Hit and Stand. |
| 3 | Existing `BlackJackHubTests` Hit/Stand/DoubleDown tests, `Hit_Throws_NotPlayersTurn`, `PlaceBet_And_Play_Round` |
| 4 | Run the existing suites, then the full suite 20 times. |

## Decisions (2026-09-30)

- **D1.** The refusal message is "No turn in progress".

## Tasks

Every task is checked with `dotnet build`, then its own tests, then the full `dotnet test`. No task touches a per-player DTO or shared
state; the new check runs inside the existing room lock.

1. **Phase check in Hit, Stand and DoubleDown** (AC1 to AC3)
   - Files: `BoardGames/Hubs/BlackJack/BlackJackHub.cs`, `BoardGames.Tests/Hubs/BlackJack/BlackJackHubTests.cs`.
   - Tests (new): `Actions_DuringBetting_AreRefused`, `Actions_AfterDealerNatural_AreRefused`, checked red on the old hub where they
     apply.
   - Commit the spec with this task.
2. **Final verification** (AC4; no code)
   - The full suite passes 20 runs in a row. `git diff main` shows no edits to existing test methods. Update the backlog memory.
