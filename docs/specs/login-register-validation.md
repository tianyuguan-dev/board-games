# Login and register: empty-field hint and validation

## Problem

**Login with nothing filled in.**
- `Login.jsx` `handleLogin` sends whatever is in the two fields straight to `POST /api/auth/login`. The server answers 401
  ("Username or password is incorrect"), and the page shows a generic "Login failed".
- A first-time visitor who just clicks Login gets no hint that they need an account. They are not told to fill the fields in or
  to register.

**Registering with empty fields is accepted.**
- `AuthController.Register` (`AuthController.cs:21-30`) only checks that the username is not taken. `RegisterRequestDto` defaults
  both fields to `""` and has no validation attributes, and ASP.NET only rejects `null` for non-nullable strings, not `""`.
- So `{"username":"","password":""}` creates a real account, with an empty username, an empty nickname (the nickname defaults to
  the username, `AuthService.cs:13-21`) and the hash of an empty password.
- On the client, `handleRegister` only checks that the two passwords match, and two empty passwords match.

Who it affects: visitors on the login page (not logged in). No game, room, role or DTO is involved.

## Scope

**In scope**
- **Login page:** clicking Login with either field empty shows a short hint, and no request is sent (D1, D2).
- **Register, server side (authoritative):** `POST /api/auth/register` refuses an empty or whitespace-only username or password with
  400 and creates nothing (D3).
- **Change password, server side:** `PUT /api/auth/password` refuses an empty or whitespace-only new password with 400 and changes
  nothing (D4). The client check in `Profile.jsx` also treats whitespace-only as empty.
- **Register, client side:** the same check runs before calling the server, so the user gets the message without a round trip.
  The server check stays the authority (CLAUDE.md rule 1).

**Out of scope**
- Existing accounts. Any account already created with an empty username or password keeps working; there is no migration.
- Other username or password rules (minimum length, allowed characters, strength).
- The "Login failed" message for wrong credentials.
- The guest login button.

## Behaviour

**Login page (`Login.jsx`), in Login mode:**

| Username | Password | Click Login |
|---|---|---|
| empty | empty | show the hint: "Enter your username and password, or register." No request is sent |
| filled | filled | unchanged: request; success logs in, failure shows "Login failed" |
| one empty | the other filled | the same hint, no request (D2) |

**Register (server, `AuthController.Register`)**, checked before the "Username already exists" lookup:

| Input | Response |
|---|---|
| empty username | 400 "Username cannot be empty" |
| empty password | 400 "Password cannot be empty" |
| both empty | 400 "Username cannot be empty" (the first failing check) |
| both filled | unchanged (duplicate check, then create) |

No user row is created when it refuses.

**Register (client, `Login.jsx` `handleRegister`):** the same two checks run first, with the same messages. Then come the existing
"Passwords do not match" check and the server call. The server's 400 text is shown as today, through `register()` throwing
`Error(msg)`.

"Empty" means empty or whitespace-only (D3): `string.IsNullOrWhiteSpace` on the server, and `value.trim() === ""` on the client.
Values that pass are still stored exactly as typed (not trimmed).

**Change password (server, `AuthController.ChangePassword`):** an empty or whitespace-only `NewPassword` returns 400 "Password cannot
be empty". The old password is not checked first, and the stored hash is unchanged. The client (`Profile.jsx`) keeps its message
"New password cannot be empty", now also for whitespace-only input.

## Edge cases

- **Double click on Login with empty fields:** the hint is shown again; no request is sent.
- **The user types after the hint:** the hint stays until the next click, as other errors on this page do.
- **Switching between Login and Register:** the existing buttons clear the error, unchanged.
- **A client calls the API directly with empty fields:** the server refuses (400), whatever the UI does.
- **Whitespace-only input** (`"   "`): refused as empty, on both the server and the client (D3).
- **Leading or trailing spaces around a real name** (`" tom "`): not trimmed; stored as typed, as today.
- **A username that already exists and is empty** (from before this change): registering `""` again gets "Username cannot be
  empty" instead of "Username already exists". Either way it is refused.

## Acceptance criteria

1. `POST /api/auth/register` with an empty username returns 400 "Username cannot be empty" and creates no user.
2. With an empty password it returns 400 "Password cannot be empty" and creates no user.
3. With both empty it returns 400 and creates no user.
4. A whitespace-only username or password is refused the same way.
4b. `PUT /api/auth/password` with an empty or whitespace-only new password returns 400 "Password cannot be empty", and the old
   password still works afterwards.
5. Valid registration and the duplicate-username check are unchanged. The existing `AuthIntegrationTests` pass unchanged.
6. Login page: Login with either field empty, or both, shows the hint and sends no request. Register with an empty field shows the matching
   message before any request.
7. Front-end lint passes (`npm run lint`, the CI gate) and `npm run build` succeeds. The full `dotnet test` suite passes.

## Test plan

| AC | Proof |
|----|-------|
| 1-4 | New `AuthIntegrationTests`: `Register_EmptyUsername_ReturnsBadRequest`, `Register_EmptyPassword_ReturnsBadRequest`, `Register_BothEmpty_ReturnsBadRequest` and `Register_WhitespaceOnly_ReturnsBadRequest`, each asserting status and body text, and that logging in with those credentials then fails (no user was created). Checked red against today's controller. |
| 4b | New `AuthIntegrationTests.ChangePassword_EmptyNewPassword_ReturnsBadRequest` (a Theory over `""` and `"   "`): 400 with that text, then logging in with the old password still works. Checked red. |
| 5 | The existing `Register_ReturnsOk`, `Register_DuplicateUsername_ReturnsBadRequest` and login tests. |
| 6 | The front end has no test runner (the agreed approach since the lint spec). Manual check in the browser: run `npm run dev` with the backend, click Login with empty fields (hint, and no request in the Network tab), then Register with an empty username, then with an empty password (each message, no request). Screenshots in the PR. |
| 7 | `npm run lint`, `npm run build`, the full `dotnet test`. |

## Decisions (2026-10-25)

- **D1. English, short:** the login hint is "Enter your username and password, or register." It is not a literal translation.
- **D2. Either field empty on Login** shows the hint and sends no request.
- **D3. Whitespace-only counts as empty**, on both the server and the client. Valid values are stored as typed.
- **D4. Change password refuses an empty new password** on the server (400 "Password cannot be empty"). The client also treats
  whitespace-only as empty.

## Tasks

Every task is checked with `dotnet build`, then its own tests, then the full `dotnet test`. Front-end tasks are also checked with
`npm run lint` and `npm run build`. Not touched: game state, rooms, locks, per-player DTOs.

1. **Register refuses empty fields on the server** (D3, AC1 to AC5)
   - Files: `BoardGames/Controllers/AuthController.cs` (the checks come before the duplicate lookup),
     `BoardGames.Tests/Integration/AuthIntegrationTests.cs`.
   - Tests (new): `Register_EmptyUsername_ReturnsBadRequest`, `Register_EmptyPassword_ReturnsBadRequest`,
     `Register_BothEmpty_ReturnsBadRequest`, `Register_WhitespaceOnly_ReturnsBadRequest`, checked red. Commit the spec with this task.
2. **Change password refuses an empty new password on the server** (D4, AC4b)
   - Files: `BoardGames/Controllers/AuthController.cs`, `BoardGames.Tests/Integration/AuthIntegrationTests.cs`.
   - Tests (new): `ChangePassword_EmptyNewPassword_ReturnsBadRequest` (Theory: `""`, `"   "`), checked red.
3. **Login page hint and client-side checks** (D1, D2, D3, AC6, AC7)
   - Files: `BoardGames.Web/src/components/Login.jsx` (`handleLogin` hint; `handleRegister` empty checks first),
     `BoardGames.Web/src/components/Profile.jsx` (the new-password check treats whitespace-only as empty).
   - Proof: `npm run lint` and `npm run build`; a manual browser check (screenshots) for the empty-login hint, each register
     message and the profile message, with no request in the Network tab.
4. **Final verification** (AC7; no code)
   - The full suite passes 20 runs in a row. `git diff main` shows no edits to existing test methods.
