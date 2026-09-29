# CLAUDE.md

Project rules for Claude Code. Read this before changing anything.

## What this is

A real-time multiplayer board game platform (Avalon, BlackJack) that my friends and I actually play.
Live at https://guantianyu.cc.

- `BoardGames/` ASP.NET Core 8 backend: SignalR hubs (`Hubs/`), room managers and services (`Services/`),
  domain models and game state machines (`Models/`), per-player DTOs (`Dtos/`), EF Core + PostgreSQL (`Data/`, `Migrations/`)
- `BoardGames.Web/` React 19 + Vite front end, plain JavaScript (.jsx) and plain CSS, no UI library
- `BoardGames.Tests/` xUnit + Moq; integration tests in `Integration/` use `CustomWebApplicationFactory`

## Commands

- Backend tests: `dotnet test`
- Run locally: `cd BoardGames.Web && npm install && npm run build`, then `cd ../BoardGames && dotnet run`
- Front end dev server: `cd BoardGames.Web && npm run dev`
- Everything in Docker: `docker-compose up -d`

## Architecture rules (do not break these)

1. **The server is authoritative.** All game rules, phase transitions and validation live in the backend.
   The front end only renders state and sends intents. Never move a rule to the client.
2. **Information is isolated per player.** Every state push goes through a per-player DTO built from that
   player's role. A change that adds a field to a DTO must be checked against every role: could Merlin,
   Percival, an evil player or a spectator see something they should not?
3. **Avalon is a state machine.** Phases: NightReveal -> TeamProposal -> TeamVote -> Mission ->
   Assassination -> GameOver. New behaviour goes into the phase that owns it, with explicit valid actions.

## Concurrency rules

Hub methods run concurrently: several players vote or play cards at the same moment.

- All mutations of a room happen inside that room's lock (`WithLock(room, ...)` in the hubs, `room.Lock`).
  Acquire the lock at public hub entry points only; private helpers assume the caller holds it.
- Shared collections outside a room lock must be thread safe (`ConcurrentDictionary`, `Interlocked`).
  Never use a plain `List`, `Dictionary` or `HashSet` for state touched by more than one connection.
- This rule exists because an AI-written vote tally once used a non thread-safe collection and the game
  locked up when several players voted at once. Treat any shared mutable state as a review blocker.

## Testing rules

- Every change ships with tests. Game logic gets unit tests; hub behaviour gets integration tests through
  `CustomWebApplicationFactory` (in-memory database, no Postgres needed).
- Timer behaviour (turn timeouts, reconnect grace) uses `FastTimerWebApplicationFactory` with 1 second timers.
  Do not add sleeps longer than the configured timers.
- Cover the unhappy paths: disconnects mid-phase, duplicate or out-of-turn actions, players leaving, invalid input.
- Run `dotnet test` and report the result before saying a task is done. Do not delete or weaken an existing
  test to make a change pass; explain the conflict instead.

## How we work: spec first, small tasks, two reviews

1. `/spec <feature>` writes `docs/specs/<feature>.md`: problem, scope, behaviour, edge cases, acceptance criteria, test plan.
2. `/breakdown <spec file>` splits the spec into small tasks, each independently testable.
3. Implement **one task at a time**. Keep each change small enough to review in a few minutes.
4. `/review` reviews the current change against this file before it is committed.
5. I review the change myself before it merges.

Ask when something is ambiguous. Do not guess at game rules.
