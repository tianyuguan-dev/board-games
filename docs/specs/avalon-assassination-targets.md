# Avalon assassination: explicit refusals for invalid Assassinate calls

## Problem

`AvalonGame.Assassinate` (`AvalonGame.cs:287-292`) silently returns in four cases:
- the game is not in the Assassination phase;
- the caller is not the Assassin;
- the target is out of range;
- the target is evil.

The hub then broadcasts an unchanged `GameState` to everyone (`AvalonHub.cs:748-749`). Every other invalid Avalon action throws
(for example "Not the leader"), so a bad Assassinate looks to the caller as if it went through: nothing happens, and they get no
error. This goes against CLAUDE.md rule 3 ("explicit valid actions").

**Related, and accepted by decision D1 (not a bug to fix):** only Good players may be assassinated. The Assassin's
`AssassinationTargets` (`AvalonGameStateDto.cs:149-154`) therefore lists exactly the Good players. In games with Oberon (the default
7 and 10 player setups), the Assassin can deduce during the Assassination phase which unknown player is Oberon, because Oberon is
the one missing from the list. Evil players are otherwise not shown Oberon. Under the "Good players only" rule this cannot be
avoided: listing the valid targets, or refusing Oberon when he is picked, reveals him either way. The user chose this rule knowing
that (2026-09-30).

## Scope

**In scope**
- `AvalonHub.Assassinate` refuses invalid calls with explicit messages and broadcasts nothing (D2).
- One `AvalonGame.GetAssassinationTargets()` (the Good seats) is used by both the DTO list and the hub check, so the two cannot drift
  apart.

**Out of scope**
- The target rule itself: Good players only, unchanged (D1).
- Outcomes and scoring of normal, early and bonus assassinations.
- `AvalonGame.Assassinate` itself. It keeps its silent returns as a second line of defence, which also leaves the existing model
  tests (for example `Assassination_OnlyAssassinCanDo`) unchanged.
- The front end, which already offers only the listed targets.

## Behaviour

`AvalonHub.Assassinate(roomId, targetIndex)`, after the existing "No game" and "Not in this room" checks and before calling the
model, throws `InvalidOperationException`, and nothing is broadcast, in these cases:

| Case | Message |
|---|---|
| Phase is not Assassination | "Not in assassination phase" |
| Caller is not the Assassin | "Only the Assassin can assassinate" |
| Target not in `GetAssassinationTargets()` (evil, including Oberon and the Assassin themself, or out of range) | "Invalid assassination target" |

A valid call behaves exactly as today: the model resolves it, the game ends, and `GameState` goes to everyone.

Who sees what: only the caller gets the error. `AssassinationTargets` is unchanged: it goes to the Assassin only, during the
Assassination phase, and equals the Good seats.

## Edge cases

- **Stale or double click after the game ended:** "Not in assassination phase".
- **Another player's client calls Assassinate:** "Only the Assassin can assassinate".
- **A modified client targets Oberon or an ally:** "Invalid assassination target"; the phase continues and the Assassin can pick
  again.
- **Early and bonus assassination:** same checks.
- **Demo:** the bot Assassin calls the model directly (`DemoBotService.cs:195`) with a Good target (the guest), which is unaffected.
- **The Assassin disconnects during Assassination:** unchanged (grace rules).

## Acceptance criteria

1. `GetAssassinationTargets()` returns exactly the Good seats (unit test with fixed roles), and the Assassin's DTO lists the same
   seats.
2. Integration: each refusal above returns its message, and no `GameState` is broadcast for it. Then a valid Assassinate still ends
   the game. This fails on today's code (no errors are thrown).
3. Existing Avalon tests pass unchanged. The full suite passes 20 runs in a row.

## Test plan

| AC | Proof |
|----|-------|
| 1 | `AvalonGameTests.GetAssassinationTargets_AreTheGoodSeats` (fixed 7-player roles with Oberon, so Oberon is excluded), plus `AvalonGameIntegrationTests` reading the Assassin's `assassinationTargets` in the refusal test |
| 2 | `AvalonGameIntegrationTests.Assassinate_InvalidCalls_AreRefused`: 5 players; before `EarlyAssassinate` → wrong phase; after it → non-Assassin, the Morgana seat, the Assassin's own seat, index 99; count `GameState` messages across the refusals (0); then a valid target → GameOver. Checked red on the old hub. |
| 3 | Run the existing suites, then the full suite 20 times. |

## Decisions (2026-09-30)

- **D1. Only Good players may be assassinated;** every evil player, Oberon included, is not a valid target. As a consequence, the
  Assassin can deduce Oberon from the target list during Assassination. The user accepted this knowingly.
- **D2. Invalid Assassinate calls are refused explicitly**, with the messages above. The model's silent returns stay as a second
  line of defence.

## Tasks

Every task is checked with `dotnet build`, then its own tests, then the full `dotnet test`. No shared state is touched. The DTO's
list stays the same seats, so no role sees anything new.

1. **`AvalonGame.GetAssassinationTargets()`, used by the DTO** (AC1)
   - Files: `BoardGames/Models/Avalon/AvalonGame.cs`, `BoardGames/Dtos/Avalon/AvalonGameStateDto.cs` (same list as today),
     `BoardGames.Tests/Models/Avalon/AvalonGameTests.cs`.
   - Tests (new): `GetAssassinationTargets_AreTheGoodSeats`. Commit the spec with this task.
2. **The hub refuses invalid Assassinate calls** (D2, AC2)
   - Files: `BoardGames/Hubs/Avalon/AvalonHub.cs`, `BoardGames.Tests/Integration/AvalonGameIntegrationTests.cs`.
   - Tests (new): `Assassinate_InvalidCalls_AreRefused`, checked red on the old hub.
3. **Final verification** (AC3; no code)
   - The full suite passes 20 runs in a row. `git diff main` shows no edits to existing test methods. Update the backlog memory.
