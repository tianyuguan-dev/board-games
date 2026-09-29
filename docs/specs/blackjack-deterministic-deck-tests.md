# BlackJack tests: deterministic decks instead of luck

## Problem

Five BlackJack tests assume that nobody is dealt a natural (a two-card 21). The decks are shuffled with `Random.Shared`, so each
test fails whenever a natural does come up. A natural for one hand is about 4.7% (4 decks: 2 × 16/52 × 4/52). `dotnet test` on
`main` fails about 1 run in 4 because of these tests, and the fails come and go at random. It will do the same on PR checks now that
`Backend tests (.NET 8)` gates merges. This affects whoever runs the suite (me, Claude Code, CI), not players.

Why a natural breaks them: `BlackJackGame.Start` (`BoardGames/Models/BlackJack/BlackJackGame.cs:57-84`) sets a result for a player
natural and `AdvancePastResolvedPlayers` skips that player. A dealer natural finishes the round at once. The hub then rejects the
test's `Hit`/`Stand` with "Not this player's turn" (`BlackJackHub.cs:346,367,388`), or the round ends without the event the test waits for.

| # | Test | Fails when | Rough rate |
|---|------|-----------|------------|
| F1 | `Models/Blackjack/BlackJackGameTests.ForfeitPlayer_DoesNotSkipWhenNotCurrentPlayer` | player 0 has a natural, so `CurrentPlayerIndex` is 1 (seen: "Expected 0, Actual 1") | ~5% |
| F2 | `Integration/BlackJackHubIntegrationTests.PlaceBet_And_Play_Round` | host or guest has a natural, so the next `Stand` is out of turn (seen) | ~9% |
| F3 | `Integration/TurnTimerServiceTests.TurnTimer_Cancelled_WhenPlayerActs` | host has a natural, so `Stand` is out of turn (seen) | ~5% |
| F4 | `Integration/TurnTimerServiceTests.TurnTimer_AutoStands_WhenPlayerDoesNotActInTime` | host or dealer has a natural, so the round finishes at the deal, no turn timer starts and `PlayerStand` never comes | ~9% |
| F5 | `Integration/BlackJackHubExtraTests.Hit_Throws_NotPlayersTurn` | host has a natural, so it is seat 1's turn and its `Hit` succeeds | ~5% |

F4 and F5 were found by reading the code, not by watching them fail. The other tests that use shuffled decks
(`Start_CreatesResultsForEachPlayer`, `Stand_SinglePlayer_FinishesGame`, the DTO tests, and so on) pass whether or not a natural
comes up. I checked each one.

Why the integration tests cannot be fixed test-only: the deck is created inside production code
(`BlackJackRoom` → `new BlackJackTable(maxPlayers)` → `new Deck(n)` + `Shuffle()`, `BlackJackTable.cs:15-31`). Tests have no way
to choose the cards. The unit test F1 can be fixed test-only, because `BlackJackGame` already takes a `Deck`.

## Scope

**In scope**
- Add an `IDeckFactory` seam for BlackJack decks (D1). Production keeps shuffling with `Random.Shared` exactly as today.
- Every integration test gets an unshuffled deck through `CustomWebApplicationFactory` (D2), so F2-F5 always see the same cards.
- Fix F1 test-only by using an unshuffled `new Deck()`.
- New unit tests that deal naturals on purpose, because these paths were only ever hit by luck (D3):
  a player natural skips that player's turn, and a dealer natural ends the round at the deal. This needs `Deck(IEnumerable<Card>)`.

**Out of scope**
- Any change to BlackJack rules, payouts, dealing order, turn order or messages.
- The two loop tests that search for an outcome (`Game_ResultIsPush_WhenSameValue` loops 1000 times, `Game_PlayerBust_DealerWins`
  loops 100 times). Their odds of failing are too small to matter, so they stay as they are.
- The Avalon evil-target assassination question (a separate backlog item).
- Hub-level natural tests (D3 chose unit tests only).
- What the hub does after a round finishes at the deal (D4: it went to the backlog).

## Behaviour

Players see no change. Production rounds are shuffled the same way as today: `Deck(deckCount)` + `Shuffle()` with `Random.Shared`.
This happens when the table is created and again when `NewRound` reshuffles below the threshold (a quarter of the shoe).

- **Seam (D1):** a `IDeckFactory` singleton in DI with `Deck Create(int deckCount)`.
  - The production implementation returns a new shuffled deck.
  - `BlackJackRoomManager` takes the factory and passes it to `BlackJackRoom` → `BlackJackTable`. The table calls it for
    its first deck and for every reshuffle.
  - The factory is stateless, so concurrent `CreateRoom` calls (outside any room lock) and `NewRound` calls (inside
    `room.Lock`) cannot race on it. `Random.Shared` is already thread-safe. This keeps the CLAUDE.md concurrency rules.
- **Test deck (D2):** `CustomWebApplicationFactory` replaces the factory with one that returns an unshuffled
  `new Deck(deckCount)`. Deals come off the end of the list (Diamonds King down to Ace, then Clubs, ...):
  - 1 player: player K♦ Q♦ = 20, dealer J♦ 10♦ = 20 (stands).
  - 2 players: player 0 K♦ Q♦ = 20, player 1 J♦ 10♦ = 20, dealer 9♦ 8♦ = 17 (stands).
  - No naturals for up to 6 hands, which covers every existing integration test (at most 2 players).
  - `FastTimerWebApplicationFactory` inherits it.
- **Stacked decks for natural tests (D3):** add `Deck(IEnumerable<Card> cards)` so a test can list the exact
  cards. `Deal()` still takes from the end.

Information isolation: the factory and the deck order stay on the server. No DTO fields change.
`TotalCards`, `CardsRemaining` and `ReshuffleThreshold` in the game DTO keep their meaning: an unshuffled test deck has the same
count as a shuffled one.

## Edge cases

- **Reshuffle mid-session:** when `NewRound` finds `Remaining <= threshold` it must call the factory again, not `new Deck` directly.
  Otherwise tests would only be deterministic until the first reshuffle.
- **Several rounds in one test:** the unshuffled deck keeps dealing from where it left off. Round 2 for 1 player is
  player 9♦ 8♦ = 17, dealer 7♦ 6♦ = 13, dealer draws 5♦ = 18. That is still deterministic and has no natural.
- **Concurrent room creation:** two players create rooms at the same time → two independent decks. Each room gets its own
  `Deck` instance, so rooms never share one.
- **Timers:** F4 relies on the round being in `PlayerTurn` after the deal. The test deck guarantees that, so the 1 s auto-stand
  fires within the existing 5 s wait. No new or longer sleeps.
- **Disconnect or forfeit during a natural round:** not changed by this spec. The existing forfeit tests keep passing. The new
  natural tests cover the skip itself.
- **Invalid input:** `Deck(IEnumerable<Card>)` with an empty list is allowed. `Deal()` already throws on an empty deck, so a test
  that stacks too few cards fails loudly instead of quietly.

## Acceptance criteria

1. F1-F5 pass 200 runs in a row each (`dotnet test --filter` loop). Their assertions are unchanged: no test is removed, skipped,
   weakened or given a retry.
2. In production DI, `BlackJackTable` gets its decks from the factory. The production factory returns a deck of `52 × deckCount`
   cards whose order is not the unshuffled order.
3. `BlackJackTable` uses the factory for the first deck and for the reshuffle in `NewRound`, and for nothing else.
4. With the test factory, a 2-player round deals 20 / 20 / dealer 17, and a 1-player round deals 20 / dealer 20.
5. A player natural dealt by `Start` gets `PlayerWin` straight away, and the turn goes to the next player (unit test with a stacked deck).
6. A dealer natural finishes the round at `Start`: a player natural becomes `Push`, everyone else `DealerWin` (unit test with a stacked deck).
7. The full `dotnet test` suite passes 20 runs in a row with no failures.
8. No change to game rules or messages: every existing BlackJack test passes unmodified, except F1's deck line.

## Test plan

| AC | Proof |
|----|-------|
| 1 | Shell loop: `for i in 1..200: dotnet test --no-build --filter "FullyQualifiedName~<test>"` for each of F1-F5, counting failures (must be 0). The F1-F5 diffs keep every `Assert`. |
| 2 | New unit test `DeckFactoryTests.Create_ReturnsShuffledShoeOfRequestedSize` (count = 52 × n, order differs from `new Deck(n)`; the chance of a false failure is 1/(52n)!). New integration test resolves `IDeckFactory` from the production `WebApplicationFactory<Program>` services and asserts it is the shuffling implementation. |
| 3 | New unit tests in `BlackJackTableTests` with a counting fake factory: the constructor calls it once, `NewRound` above the threshold does not call it, and `NewRound` at or below the threshold calls it once more. |
| 4 | New integration test `BlackJackDeterministicDeckTests.TwoPlayerRound_DealsKnownHands` (reads `GameDealt` hands), plus the same for 1 player. |
| 5 | New unit test `BlackJackGameTests.Start_PlayerNatural_WinsAndIsSkipped` with a stacked deck. |
| 6 | New unit tests `Start_DealerNatural_FinishesRound` and `Start_BothNatural_IsPush` with stacked decks. |
| 7 | Shell loop of 20 full `dotnet test` runs. Report pass/fail counts. |
| 8 | `git diff` of `BoardGames.Tests` shows only new tests, the factory registration and F1's deck line. Every other existing BlackJack test is untouched. |

## Decisions (2026-09-30)

- **D1. Seam:** `IDeckFactory` in DI (`Deck Create(int deckCount)`). `BlackJackRoomManager` → `BlackJackRoom` → `BlackJackTable`
  receive it through their constructors. The production implementation shuffles with `Random.Shared`. Seeded `Random` and
  swapping `room.BlackJackTable` from tests were rejected.
- **D2. Test deck scope:** `CustomWebApplicationFactory` registers an unshuffled-deck factory for every integration test, and
  `FastTimerWebApplicationFactory` inherits it.
- **D3. Natural-hand tests:** unit level only (AC5, AC6), using a new `Deck(IEnumerable<Card>)` constructor. No hub-level natural test.
- **D4. Hub accepts Hit/Stand/DoubleDown on a round that finished at the deal:** recorded in the backlog, not fixed here.

## Tasks

Every task is checked the same way: `dotnet build`, then the task's own tests, then the full `dotnet test`.
F2-F5 stay flaky until T6. If one of them fails in the full-suite run before T6, that is expected: name the failing test in
the report and rerun. Any other failure blocks the task.

Existing call sites stay unchanged. `new BlackJackTable(deckCount)`, `new BlackJackRoom(id, max)` and
`new BlackJackRoomManager()` appear in about 40 existing tests, so every new constructor parameter is optional and defaults to
the shuffled factory. That keeps AC8 true and the diff small.

No task touches a per-player DTO. **T5 touches a shared singleton** (`BlackJackRoomManager`, used by every connection) and
needs the concurrency review. The other tasks change models, which are always used under the room lock, or tests.

1. **`Deck(IEnumerable<Card> cards)` constructor** (D3)
   - Files: `BoardGames/Models/Poker/Deck.cs`, `BoardGames.Tests/Models/Poker/DeckTests.cs`.
   - The deck holds exactly the given cards, and `Deal()` still takes from the end. `Count` / `IncludeJokers` keep their defaults.
     Existing constructors are unchanged.
   - Tests (new): `Deck_FromCards_DealsLastCardFirst`, `Deck_FromCards_RemainingMatchesInput`,
     `Deck_FromCards_Empty_DealThrows`. Existing `DeckTests` unchanged.
   - Commit the spec file together with this task.

2. **Unit tests for dealt naturals** (AC5, AC6)
   - Files: `BoardGames.Tests/Models/Blackjack/BlackJackGameTests.cs` (tests only).
   - Add a helper that takes cards in deal order (P0 card 1, P0 card 2, P1 ..., dealer card 1, dealer card 2) and builds the
     reversed list for `Deck(IEnumerable<Card>)`.
   - Tests (new): `Start_PlayerNatural_WinsAndIsSkipped` (2 players, P0 A+K: `Results[0] == PlayerWin`, `CurrentPlayerIndex == 1`,
     `PlayerTurn`), `Start_DealerNatural_FinishesRound` (`Finished`, all `DealerWin`), `Start_BothNatural_IsPush`.

3. **Make F1 deterministic** (AC1 for F1)
   - Files: `BoardGames.Tests/Models/Blackjack/BlackJackGameTests.cs`. Only `ForfeitPlayer_DoesNotSkipWhenNotCurrentPlayer`
     switches from `CreateShuffledDeck()` to `new Deck()`. Its asserts are unchanged.
   - Proof: that test passes 200 filtered runs in a row.

4. **`IDeckFactory` + `ShuffledDeckFactory`; `BlackJackTable` uses it** (D1, AC2 part, AC3)
   - Files: new `BoardGames/Models/Poker/IDeckFactory.cs` and `BoardGames/Models/Poker/ShuffledDeckFactory.cs`,
     `BoardGames/Models/BlackJack/BlackJackTable.cs`, new `BoardGames.Tests/Models/Poker/DeckFactoryTests.cs`,
     `BoardGames.Tests/Models/Blackjack/BlackJackTableTests.cs`.
   - `BlackJackTable(int deckCount, IDeckFactory? deckFactory = null)` calls the factory for its first deck and for the reshuffle
     in `NewRound`. The default is `ShuffledDeckFactory`, which does `new Deck(n)` + `Shuffle()`, so production behaviour is identical.
   - Tests (new): `DeckFactoryTests.Create_ReturnsShuffledShoeOfRequestedSize`. In `BlackJackTableTests`, with a counting fake:
     `Constructor_CreatesDeckFromFactoryOnce`, `NewRound_AboveThreshold_DoesNotCallFactory`,
     `NewRound_AtThreshold_CallsFactoryAgain`. The 5 existing `BlackJackTableTests` stay unchanged and pass.

5. **Pass the factory through room and room manager; register it in DI** (D1). ⚠ Shared singleton: needs concurrency review.
   - Files: `BoardGames/Models/BlackJack/BlackJackRoom.cs` (optional `IDeckFactory?` constructor parameter, passed to the table),
     `BoardGames/Services/BlackJack/BlackJackRoomManager.cs` (optional constructor parameter, stored `readonly`, passed to each
     new room), `BoardGames/Program.cs` (`AddSingleton<IDeckFactory, ShuffledDeckFactory>()`),
     `BoardGames.Tests/Services/BlackJack/BlackJackRoomManagerTests.cs`.
   - Review points: the manager holds only a `readonly` reference to a stateless factory. `CreateRoom` builds the room before
     it is added to the `ConcurrentDictionary`, so no other connection can see a half-built room. No new mutable shared state.
   - Tests (new): `CreateRoom_UsesInjectedDeckFactory` (a counting fake is called once with `deckCount == maxPlayers`),
     `CreateRoom_ConcurrentCalls_EachRoomGetsOwnDeck` (parallel `CreateRoom` calls, one factory call per room, no exception).
     Existing `BlackJackRoomManagerTests` (`new()`) and `BlackJackHubTests` stay unchanged.

6. **Unshuffled deck for all integration tests** (D2, AC1 for F2-F5, AC2 DI part, AC4)
   - Files: new `BoardGames.Tests/Integration/UnshuffledDeckFactory.cs`,
     `BoardGames.Tests/Integration/CustomWebApplicationFactory.cs` (replace the `IDeckFactory` registration; a
     `protected virtual bool UseUnshuffledDeck => true` lets one test factory keep the production registration),
     new `BoardGames.Tests/Integration/BlackJackDeterministicDeckTests.cs`.
   - Tests (new): `TwoPlayerRound_DealsKnownHands` (20 / 20 / dealer 17 from `GameDealt`), `OnePlayerRound_DealsKnownHands`
     (20 / dealer 20), `ProductionRegistration_IsShuffledDeckFactory` (factory subclass with `UseUnshuffledDeck => false`),
     `TestFactory_RegistersUnshuffledDeckFactory`.
   - Proof for AC1: F2-F5 pass 200 filtered runs each. `FastTimerWebApplicationFactory` inherits the override, which covers F3 and F4.

7. **Verification pass** (AC7, AC8; no code)
   - Run the full `dotnet test` 20 times in a row and report the pass/fail counts.
   - `git diff main -- BoardGames.Tests` shows only new tests, the factory changes and F1's deck line.
   - Update the backlog memory: the flaky BlackJack item is done.
