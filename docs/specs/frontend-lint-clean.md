# Front-end lint clean, lint gates CI

## Problem

`npm run lint` (`eslint .` in `BoardGames.Web/`) exits 1 today with **13 errors and 3 warnings**, so CI cannot run it.
`.github/workflows/ci.yml` has a `# TODO: add npm run lint here` in the `frontend` job. So lint regressions,
including real hook bugs that `eslint-plugin-react-hooks` v7 catches, reach `main` without anyone noticing. This affects
whoever works on the front end (me and Claude Code). Players are not affected directly.

Baseline (ESLint 10.2, react-hooks 7.1, run 2026-09-30):

| # | File:line | Rule | Severity | What it is |
|---|-----------|------|----------|------------|
| 1 | `components/Admin.jsx:51` | `react-hooks/set-state-in-effect` | error | effect calls `fetchUsers`, which calls `setError("")` synchronously |
| 2 | `components/Admin.jsx:52` | `react-hooks/exhaustive-deps` | warning | deps `[authed]` leave out `fetchUsers`, `search` |
| 3-6 | `components/Game.jsx:120,135,139,150` | `no-empty` | error | `catch {}` on Unready / Hit / Stand / LeaveRoom |
| 7 | `components/avalon/AvalonGame.jsx:158` | `react-hooks/immutability` | error | writes `needsRejoin.current`: the prop is a ref, but its name does not end in `Ref` |
| 8 | `components/avalon/AvalonGame.jsx:180` | `react-hooks/exhaustive-deps` | warning | handler-registration effect deps `[connection]` leave out `gameInProgress`, `needsRejoin`, `onLeave`, `roomId` |
| 9-12 | `components/avalon/AvalonGame.jsx:188,246,277,290` | `no-empty` | error | `catch {}` on Unready / ReorderPlayer (x2) / LeaveRoom |
| 13 | `components/avalon/AvalonGame.jsx:670` | `no-unused-vars` | error | `canAddMore` is computed and never used |
| 14 | `components/avalon/AvalonGameDetail.jsx:57` | `react-hooks/exhaustive-deps` | warning | deps `[gameId]` leave out the `fetchDetail` prop |
| 15 | `services/api.js:178` | `no-unused-vars` | error | `getBalances(token)` ignores `token` (it uses `authFetch`) |
| 16 | `services/api.js:221` | `no-empty` | error | `catch {}` on the logout POST |

## Scope

**In scope**
- Fix all 13 errors in `BoardGames.Web/src` so `npm run lint` exits 0.
- Silence the 3 `exhaustive-deps` warnings with line-level disables that give a reason (D1), and make warnings fail lint:
  the `lint` script in `package.json` becomes `eslint . --max-warnings 0`, so running it locally matches CI.
- Set `no-empty` to `{ allowEmptyCatch: true }` in `eslint.config.js` (D2).
- Add a `Lint` step (`npm run lint`) to the `frontend` job in `ci.yml`, before `Build`, and remove the TODO.
- Remove the unused `token` argument from `getBalances` and from its one caller (`Profile.jsx:19`).

**Out of scope**
- Any change to behaviour that players or admins can see. This is a lint and CI change only.
- Backend code, hubs, DTOs, game rules. No rule moves to the client (CLAUDE.md rule 1), and no DTO fields change (rule 2).
- Upgrading ESLint or plugins, or adding new rules or presets (e.g. TypeScript, a11y, Prettier).
- Adding a front-end unit test framework (Vitest / RTL). See D4.
- Refactoring the effects past the minimum needed to satisfy the rule.

## Behaviour

Nothing that players see changes. For each finding, the behaviour that must be kept:

- **Empty `catch {}` (#3-6, #9-12, #16).** These swallow errors on purpose. For example, a Hit sent after the turn timer
  fired is rejected by the server, and the UI waits for the next `GameState` push. Keep swallowing them. Fix: set `no-empty`
  to `{ allowEmptyCatch: true }` in `eslint.config.js` (D2). The code in these blocks does not change.
- **`needsRejoin` prop (#7).** App passes `needsRejoinRef` as the `needsRejoin` prop. Rename the prop to `needsRejoinRef` at both
  ends (`App.jsx:278`, `AvalonGame.jsx:76,157-158`). The rejoin flow stays exactly the same:
  - after a page refresh with `roomId` in sessionStorage, AvalonGame calls `Rejoin` once. If that fails it calls `JoinRoom`,
    and if that also fails it clears `roomId` and returns to the lobby.
  - after App's `onclose` handler sets the ref to true and the connection is rebuilt, the same one-shot rejoin runs.
  - after a normal Create/Join (`handleJoinRoom` sets the ref to false), AvalonGame sends no Rejoin. If `gameInProgress`, it sends
    `GetGameState` instead. The lobby's "Rejoin Room" button sends its own single `Rejoin` before `handleJoinRoom` runs; that one is expected.
  - the ref is cleared **before** the invoke, so re-running the effect never sends a second Rejoin.
- **`canAddMore` (#13).** Delete it. It is dead code and was never meant to gate a button (D3). The role-config +/- buttons
  at `AvalonGame.jsx:702-716` do not read it. The server's `AdjustRole` enforces role limits.
- **`getBalances(token)` (#15).** Drop the parameter. The request stays the same, because `authFetch` already attaches the token.
- **Admin `fetchUsers` effect (#1, #2).** Current behaviour to keep: fetch users once when `authed` becomes true, using the
  current `search`. Do **not** refetch on every keystroke in the search box. Also do not refetch on every render, which would
  happen if `fetchUsers` were added as a dep while `password` changes during login. A fix that keeps this behaviour is fine.
  Adding `search` to the deps is not allowed. For the `set-state-in-effect` error, prefer a small change that keeps the
  behaviour. If no such change exists, a line-level disable that names the rule and gives a reason is acceptable. The
  `exhaustive-deps` warning gets a line-level disable (D1).
- **AvalonGame handler-registration effect (#8).** Must keep running only when `connection` changes. Adding `onLeave`, `roomId`,
  etc. to the deps would unregister and re-register every SignalR handler, and would resend `Rejoin`/`GetGameState` whenever App
  re-renders with a new `onLeave`. That is a behaviour change, so it is **not allowed**. Fix: a line-level disable (D1).
- **AvalonGameDetail effect (#14).** Must keep fetching once per `gameId`. Admin passes a new `fetchAdminGameDetail` function on every
  render, so adding `fetchDetail` to the deps would refetch in a loop. **Not allowed.** Fix: a line-level disable (D1).
- **Disable comment format (D1):** `// eslint-disable-next-line react-hooks/exhaustive-deps -- <reason>`. The reason says why
  the effect must not re-run, e.g. `-- register hub handlers once per connection; props read at registration time`.

Which roles see what: unchanged for every role (Merlin, Percival, evil, loyal, spectator, admin). No payloads change.

## Edge cases

The code paths these fixes touch, which must behave exactly as they do today:

- **Refresh mid-game** (any phase NightReveal ... GameOver): exactly one `Rejoin` is sent. The player gets back their per-role `GameState`.
- **Connection drops and SignalR reconnects:** `onclose` sets `needsRejoinRef`, the rebuilt connection sends one Rejoin, and no second one follows.
- **Rejoin rejected** (room gone, or the reconnect grace timer already expired): falls back to `JoinRoom`. If that fails, clears `roomId` and returns to the lobby.
- **Out-of-turn or late actions** (Hit/Stand after the turn timeout, Unready after start, Reorder by a non-host): the server rejects
  them and the client still swallows the rejection silently, with no new alert.
- **LeaveRoom fails** (connection already closed): `onLeave()` still runs and the player still returns to the lobby.
- **Logout while offline:** the logout POST fails silently, and `forceLogout()` still clears tokens.
- **Admin enters a wrong token:** the first fetch returns 401. `authed` goes back to false and "Unauthorized" is shown. No retry loop.
- **Concurrency:** no shared server state is touched, so the CLAUDE.md concurrency rules are not affected. On the client, the effect
  that registers hub handlers must still run once per connection, or duplicate handlers would apply each `GameState` twice.

## Acceptance criteria

1. `cd BoardGames.Web && npm run lint` exits 0 with 0 errors.
2. The `lint` script is `eslint . --max-warnings 0`, and `npm run lint` reports 0 warnings.
3. `.github/workflows/ci.yml` `frontend` job runs `npm run lint` after `Install` and before `Build`. The TODO comment is removed.
   A PR that brings in a lint error fails the `frontend` job.
4. `npm run build` still succeeds.
5. The rules are not weakened across the board. `eslint.config.js` still extends `js.configs.recommended`,
   `reactHooks.configs.flat.recommended` and `reactRefresh.configs.vite`. No `eslint-disable` comment covers a whole file.
   Any line-level disable names its rule and gives a reason. The only rule option change is `no-empty: { allowEmptyCatch: true }`.
   The line-level disables in `src` are exactly these 5, all `react-hooks/exhaustive-deps` with a reason (D5): the 3 baseline
   warnings (#2 Admin, #8 AvalonGame, #14 AvalonGameDetail) and the 2 pre-existing ones in `DatePickerEN.jsx` and
   `AvalonHistory.jsx`. The Admin `set-state-in-effect` error was fixed in code (T5), so it has no disable.
6. The `needsRejoin` prop is renamed to `needsRejoinRef` in `App.jsx` and `AvalonGame.jsx`. The rejoin behaviour in "Behaviour" is unchanged.
7. `getBalances` takes no arguments, and `Profile.jsx` calls it with none.
8. `canAddMore` is removed. The role-config UI renders and behaves the same.
9. The AvalonGame handler-registration effect still depends only on `[connection]`. The AvalonGameDetail effect still fetches once
   per `gameId`. The Admin user list still fetches once on login and not on each search keystroke.
10. `dotnet test` passes with no test removed or weakened. No backend files change.

## Test plan

This repo has no front-end test runner. As agreed (D4), the lint gate and CI are the automated proof, and the flows ESLint
cannot check get the manual script below.

| AC | Proof |
|----|-------|
| 1, 2 | Run `npm run lint` locally: exits 0 with `0 problems`. The same command runs in CI on the PR. |
| 3 | Look at the `ci.yml` diff. Push a throwaway branch with an unused variable, check that the `frontend` job fails, then delete the branch. |
| 4 | `npm run build` locally and in CI. |
| 5 | Review the `eslint.config.js` diff, and `grep -rn "eslint-disable" BoardGames.Web/src`: each hit names a rule and gives a reason. |
| 6 | Manual, with 2 browsers on `npm run dev` plus the backend. (a) Start an Avalon game, refresh one tab in TeamVote: it rejoins, sees its own role only, and DevTools WS shows exactly one `Rejoin`. (b) Stop the backend mid-game until auto-reconnect gives up and `onclose` fires (about 150 s with `RECONNECT_DELAYS`), then restart it: the rebuilt connection sends exactly one `Rejoin`. Rooms are in memory, so it then falls back to one `JoinRoom` and lands in the lobby. (c) Refresh after the room is gone: it lands back in the lobby. (d) Join a game in progress from the lobby with the "Rejoin Room" button: the lobby sends exactly one `Rejoin`, AvalonGame sends none of its own and requests `GetGameState`. The backend Rejoin/grace behaviour is already covered by the existing integration tests (`FastTimerWebApplicationFactory`), which must still pass. |
| 7 | Manual: open Profile, balances load. Network tab shows `GET /api/auth/balances` with a `Bearer` header. |
| 8 | Manual: host adjusts roles in an Avalon room. The +/- buttons and the server errors (`roleError`) behave as before. |
| 9 | Review the diff: the dependency arrays for these three effects are unchanged. Manual: log into Admin, type in the search box (no request per keystroke), open a game detail (one request in the Network tab). |
| 10 | `dotnet test` from the repo root. Report the result. `git diff --stat` shows no `BoardGames/` or `BoardGames.Tests/` changes. |

## Decisions (2026-09-30)

- **D1. Warnings fail lint.** The `lint` script uses `--max-warnings 0`. Each of the 3 `exhaustive-deps` warnings is silenced
  with a line-level disable that names the rule and gives a reason. No dependency arrays change.
- **D2. Empty catches.** `no-empty: ["error", { allowEmptyCatch: true }]` in `eslint.config.js`. No comment in each block.
- **D3. `canAddMore` is dead code.** It was never meant to gate a button. Delete it. There is no game-rule follow-up.
- **D4. Tests.** The CI lint gate plus the manual script in the Test plan count as this change's tests. No Vitest for now.
- **D5. Pre-existing disables (found in T5 review).** `DatePickerEN.jsx:20` and `AvalonHistory.jsx:36` already had
  `exhaustive-deps` disables with no reason. They get a reason in T6, so every disable in `src` follows one format.

## Tasks

Every task is checked the same way:
- `cd BoardGames.Web && npm run lint`: the problem count must equal the "lint after" figure for that task.
- `npm run build` succeeds.
- `dotnet test` passes. No backend files change, but CLAUDE.md requires the run.
- The task's own manual check passes.

Until T7, CI does not run lint, so CI stays green after every task. T7 turns the gate on only after lint is clean.

There are no shared model or DTO changes, so nothing has to go first for that reason. **No task touches server-side shared
mutable state or a per-player DTO.** T4 changes the client's reconnect/rejoin flow and needs a closer look (see its note).

Baseline: **13 errors, 3 warnings.**

1. **Allow empty catch blocks** (#3-6, #9-12, #16, D2)
   - Files: `BoardGames.Web/eslint.config.js`. Add `rules: { 'no-empty': ['error', { allowEmptyCatch: true }] }`.
   - Proves: AC5 (only this rule option changes).
   - Lint after: 4 errors, 3 warnings. Manual check: none, since no source code changes.

2. **Delete the unused `canAddMore`** (#13, D3)
   - Files: `BoardGames.Web/src/components/avalon/AvalonGame.jsx`.
   - Proves: AC8. Manual check: host adjusts roles, and the +/- buttons and `roleError` behave as before.
   - Lint after: 3 errors, 3 warnings.

3. **Drop the unused `token` argument from `getBalances`** (#15)
   - Files: `BoardGames.Web/src/services/api.js`, `BoardGames.Web/src/components/Profile.jsx`.
   - Proves: AC7. Manual check: Profile shows balances, and `GET /api/auth/balances` carries a `Bearer` header.
   - Lint after: 2 errors, 3 warnings.

4. **Rename the `needsRejoin` prop to `needsRejoinRef`** (#7). ⚠ Needs a closer look: reconnect flow.
   - Files: `BoardGames.Web/src/App.jsx` (line 278), `BoardGames.Web/src/components/avalon/AvalonGame.jsx` (prop list line 76, lines 157-158).
   - This is a rename only. The ref is still cleared before the `Rejoin` invoke, and the effect deps stay `[connection]`.
     The ref is client-side mutable state shared by App (`onclose`, `handleJoinRoom`) and AvalonGame. The JS thread is single,
     so this is not the CLAUDE.md concurrency case, but a wrong write here means a double Rejoin or none at all.
   - Proves: AC6. Manual check: the four rejoin scenarios in Test plan AC6 (a-d). Existing backend Rejoin and grace-timer
     integration tests still pass under `dotnet test`.
   - Lint after: 1 error, 3 warnings. The #8 warning remains and now names `needsRejoinRef`.

5. **Admin user-list effect** (#1 error, #2 warning)
   - Files: `BoardGames.Web/src/components/Admin.jsx`.
   - Fix `set-state-in-effect` with the smallest change that keeps the behaviour: fetch once when `authed` turns true, never per
     search keystroke, and a 401 resets `authed` with no retry loop. If no such change exists, use a line-level disable with a
     reason. Add the D1 `exhaustive-deps` disable. Deps stay `[authed]`.
   - Proves: AC5 (disable format), AC9 (Admin part). Manual check: log in (one `GET /users`), type in search (no request),
     log in with a bad token (shows "Unauthorized", no loop).
   - Lint after: 0 errors, 2 warnings.

6. **Silence the two remaining `exhaustive-deps` warnings** (#8, #14, D1), and give reasons to the 2 pre-existing disables (D5)
   - Files: `BoardGames.Web/src/components/avalon/AvalonGame.jsx` (handler-registration effect),
     `BoardGames.Web/src/components/avalon/AvalonGameDetail.jsx`, `BoardGames.Web/src/components/DatePickerEN.jsx` (line 20),
     `BoardGames.Web/src/components/avalon/AvalonHistory.jsx` (line 36).
   - Line-level disables with reasons only: add two, and append a `-- <reason>` to the two existing ones. No dependency array changes.
   - Proves: AC5, AC9. Manual check: open an admin game detail and see exactly one request. A refresh mid-game still sends exactly one `Rejoin`.
   - Lint after: 0 errors, 0 warnings.

7. **Turn on the lint gate** (D1, CI)
   - Files: `BoardGames.Web/package.json` (`"lint": "eslint . --max-warnings 0"`), `.github/workflows/ci.yml` (add a `Lint`
     step `npm run lint` after `Install` and before `Build`, and delete the TODO).
   - Proves: AC1, AC2, AC3, AC4, AC10. Checks: `npm run lint` exits 0 with 0 problems. A throwaway branch with an unused
     variable fails the `frontend` job (then delete the branch). `git diff --stat main` shows no `BoardGames/` or `BoardGames.Tests/` changes.
   - Depends on T1-T6.
