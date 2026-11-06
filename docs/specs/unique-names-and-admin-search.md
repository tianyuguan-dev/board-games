# Unique usernames and nicknames (case-insensitive, trimmed); case-insensitive admin search

## Problem

**Names are not unique in a way players can tell apart.** Nicknames are what other players see in room lists, the leaderboard
and game history.
- **Register** (`AuthController.Register`) checks `UserRepository.FindByUsername`, which is an exact match (`u.Username ==
  username`). So "tom", "TOM" and " tom " can all register. A new user's nickname defaults to the username
  (`AuthService.CreateUser`).
- **Change nickname** has two entry points, and neither checks anything:
  - the player, `PUT /api/auth/nickname` (`AuthController.cs:111-122`);
  - the admin, `PUT /api/admin/users/{id}/nickname` (`AdminController.cs:79-90`).

  Anyone can take another player's nickname, or another player's username (which is also their default nickname). That lets
  one player pose as another.
- **The same two endpoints also accept** an empty or whitespace-only nickname, and a nickname over 50 characters. The column is
  `HasMaxLength(50)` (`AppDbContext.cs:47`), so on PostgreSQL that save fails with a 500.
- **The client shows no reason when it fails.** `api.js updateNickname` throws a fixed "Update nickname failed", Profile shows
  "Failed to update nickname", and Admin shows "Failed to update nickname". So a duplicate would look like a random failure.

**Admin search is case-sensitive.** `AdminController.GetUsers` filters with `u.Username.Contains(search) ||
u.Nickname.Contains(search)`, which is case-sensitive on PostgreSQL. Searching "tom" misses "TOM" and "Tom".

Who it affects: every registered player (registering, changing their nickname) and the admin (search, editing nicknames). No
game state, room or DTO is involved.

## Scope

**In scope**
- **Name key:** two names are the same if they are equal after trimming leading and trailing whitespace and ignoring case (D1).
- **Register:** refuse when the new username's key matches another user's **username** or **nickname** (D2). The second part is
  needed because the new user's nickname will be that username.
- **Change nickname** (player and admin): refuse when the new nickname's key matches **another** user's nickname or username.
  Taking back your own username, or changing only the case or spacing of your own name, is allowed.
- **Nickname input checks** (player and admin, D3): not empty or whitespace-only; at most 50 characters after trimming.
  The nickname is stored trimmed.
- **Client messages:** Profile and Admin show the server's reason instead of the generic failure text.
- **Admin search:** case-insensitive, on both username and nickname, and the search term is trimmed.

**Out of scope**
- **Existing duplicates** are left as they are (your decision). No migration and no renaming. Only new registrations and
  nickname changes are checked, and a user who already shares a name can keep it and save unrelated changes.
- **A database unique index** (D4). It would fail on existing duplicates and needs a case-insensitive expression index, so the
  rule is enforced in the application.
- **Login.** It stays an exact username match, as typed.
- **How usernames are stored** stays as typed (login depends on it); only the comparison ignores case and edge spaces.
- **Guest nicknames** (`Guest_xxxxxx`). Guests have no database row, so they are not part of the check.

## Behaviour

**Name key:** `key(name) = name.Trim().ToLowerInvariant()`. The comparison runs in the database, as
`u.Username.Trim().ToLower() == key || u.Nickname.Trim().ToLower() == key`, which translates on PostgreSQL and on the in-memory
test database alike.

**Register** (`POST /api/auth/register`). The checks run in this order:
1. Empty or whitespace-only username or password: refused (from `login-register-validation`).
2. The key matches any user's username or nickname: 400 "Username already exists". This is the same text as today, so the
   client is unchanged.
3. Otherwise the user is created. The username is stored as typed, and the nickname is set to the same value, as today.

**Change nickname** (player `PUT /api/auth/nickname`, admin `PUT /api/admin/users/{id}/nickname`). For target user U and
input N:
1. `N.Trim()` is empty: 400 "Nickname cannot be empty".
2. `N.Trim()` is longer than 50 characters: 400 "Nickname is too long (max 50)".
3. The key matches the username or nickname of any user **other than U**: 400 "Nickname already taken".
4. Otherwise `N.Trim()` is saved.

The player call returns `{ nickname }` as today; the admin call returns its message as today.

**Admin search** (`GET /api/admin/users?search=`). `term = search.Trim().ToLower()`. It returns users whose
`Username.ToLower()` or `Nickname.ToLower()` contains `term`. An empty term after trimming returns all users, as today.

**Client:**
- `api.js updateNickname` throws the server's text when there is one.
- `Profile.jsx` shows `e.message`.
- `Admin.jsx saveNickname` shows the response text on a non-OK response.
- No client-side duplicate check; the server is the authority.

Who sees what: a player only learns that a name is taken. No other user's details are returned.

## Edge cases

- **Your own name in another case:** "tom" changes their nickname to "TOM" or " Tom ". Allowed, because user U is excluded from
  the comparison.
- **Another user's username:** A (username "alice") tries the nickname "ALICE". Refused, "Nickname already taken".
- **Swapping names:** A has nickname "x" and B wants "x". Refused until A changes away.
- **Existing duplicates:** two old accounts with the nickname "tom" can both keep it. If one of them changes their nickname to
  "Tom", it is refused, because it matches the other account. Unrelated changes (password, balance) are not affected.
- **Two people register "tom" at the same moment:** without a database index, both checks could pass. This is a known
  limitation; it is accepted (D4).
- **Whitespace inside a name** ("t om" and "tom"): different names. Only leading and trailing whitespace is ignored (D1).
- **Non-English letters:** case folding uses the database's `lower()`, and `ToLowerInvariant` for the input.
- **Admin search with spaces** ("  tom "): trimmed, then matched.

## Acceptance criteria

1. Register "TOM" or " tom " when "tom" exists (as a username) gives 400 "Username already exists", and creates nothing.
2. Register "tom" when another user's nickname is "Tom" gives 400 "Username already exists" (D2).
3. Player nickname change to another user's nickname or username, in any case or with edge spaces, gives 400 "Nickname already
   taken", and nothing changes.
4. Player nickname change to your own username or nickname in another case or with edge spaces succeeds, and saves the trimmed
   value.
5. Empty or whitespace-only nicknames are refused with "Nickname cannot be empty", and over 50 characters after trimming with
   "Nickname is too long (max 50)". This holds for both the player and the admin endpoints.
6. The admin nickname change follows AC3 to AC5 for the target user.
7. Admin search "tom" returns users named "tom", "TOM" and "Tom" (by username or nickname), and search "  TOM " returns the same.
8. Existing tests pass unchanged, including `UpdateNickname_Authenticated_ReturnsOk`, `GetUsers_SearchFilter_Works` and admin
   `UpdateNickname_Works`. The full `dotnet test` passes, and `npm run lint` and `npm run build` pass.
9. Profile and Admin show the server's reason for a refused nickname (manual browser check).

## Test plan

| AC | Proof |
|----|-------|
| 1, 2 | New `AuthIntegrationTests`: `Register_SameUsernameOtherCaseOrSpaces_ReturnsBadRequest` (Theory), `Register_MatchesAnotherUsersNickname_ReturnsBadRequest`. Checked red against today's code. |
| 3, 4, 5 | New `AuthIntegrationTests`: `UpdateNickname_TakenByOtherUser_ReturnsBadRequest` (Theory over the other user's nickname or username, in another case or with edge spaces), `UpdateNickname_OwnNameOtherCase_Succeeds`, `UpdateNickname_EmptyOrTooLong_ReturnsBadRequest`. Checked red. |
| 6 | New `AdminIntegrationTests`: `UpdateNickname_Duplicate_ReturnsBadRequest`, `UpdateNickname_EmptyOrTooLong_ReturnsBadRequest`. Checked red. |
| 7 | New `AdminIntegrationTests.GetUsers_Search_IsCaseInsensitive`: register "casesearch", "CASESEARCH" and "CaseSearch_x", then search "casesearch" and "  CASESEARCH " and expect all three. Checked red. Because "casesearch" and "CASESEARCH" now collide, the test creates the second and third users through the admin nickname path, or with distinct usernames whose nicknames differ in case. Settled in the tasks. |
| 8 | The existing suites; `git diff` shows no edits to existing tests. |
| 9 | A manual browser check with screenshots (the front end has no test runner). |

## Decisions (2026-11-06)

- **D1. Trim only the ends:** `" tom "` equals `"tom"`; `"t om"` does not.
- **D2. A new username must not match anyone's username or nickname.**
- **D3. Nickname input checks:** empty or whitespace-only and more than 50 characters (after trimming) are refused, on both
  the player and admin endpoints.
- **D4. Application-level check only, with no database unique index.** The race between two simultaneous identical
  registrations is accepted.

## Tasks

Every task is checked with `dotnet build`, then its own tests, then one full `dotnet test`. This is deterministic logic, so one
or two full runs are enough. The front-end task is also checked with `npm run lint`, `npm run build` and a browser check.
Not touched: game state, rooms, locks, per-player DTOs.

`AuthService.UsernameExists` and `UserRepository.FindByUsername` stay exactly as they are: login uses the exact match, and the
existing `AuthServiceTests` mock them. The new check is a separate path.

1. **Name rules and the "name taken" query; register uses them** (D1, D2, AC1, AC2)
   - Files: new `BoardGames/Services/NameRules.cs` (`Key`, nickname validation and messages), new
     `BoardGames/Data/UserNameQueries.cs` (an `IQueryable<User>.IsNameTakenAsync(name, exceptUserId)` extension that compares
     the trimmed, lowercased username and nickname), `IUserRepository`/`UserRepository` (`IsNameTaken`), `IAuthService`/
     `AuthService` (`IsNameTaken`), `AuthController.Register` (uses it instead of `UsernameExists`),
     `BoardGames.Tests/Integration/AuthIntegrationTests.cs`.
   - Tests (new): `Register_SameUsernameOtherCaseOrSpaces_ReturnsBadRequest`, `Register_MatchesAnotherUsersNickname_ReturnsBadRequest`,
     checked red. Commit the spec with this task.
2. **Player nickname change: empty, length and taken checks** (D3, AC3, AC4, AC5)
   - Files: `AuthController.UpdateNickname`, `AuthIntegrationTests`.
   - Tests (new): `UpdateNickname_TakenByOtherUser_ReturnsBadRequest`, `UpdateNickname_OwnNameOtherCase_Succeeds`,
     `UpdateNickname_EmptyOrTooLong_ReturnsBadRequest`, checked red.
3. **Admin nickname change: the same checks** (AC5, AC6)
   - Files: `AdminController.UpdateNickname`, `BoardGames.Tests/Integration/AdminIntegrationTests.cs`.
   - Tests (new): `UpdateNickname_Duplicate_ReturnsBadRequest`, `UpdateNickname_EmptyOrTooLong_ReturnsBadRequest`, checked red.
4. **Case-insensitive admin search** (AC7)
   - Files: `AdminController.GetUsers`, `AdminIntegrationTests`.
   - Tests (new): `GetUsers_Search_IsCaseInsensitive`, checked red.
5. **Clients show the server's reason** (AC9)
   - Files: `BoardGames.Web/src/services/api.js` (`updateNickname` throws the server's text),
     `BoardGames.Web/src/components/Profile.jsx`, `BoardGames.Web/src/components/Admin.jsx`.
   - Proof: `npm run lint` and `npm run build`; a browser check (a duplicate nickname in Profile and in Admin shows "Nickname
     already taken"; an admin search in another case finds both users).
6. **Final verification** (AC8): one or two full `dotnet test` runs. `git diff main` shows no edits to existing test methods.
