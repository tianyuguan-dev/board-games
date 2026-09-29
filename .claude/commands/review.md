Review the current uncommitted change (`git diff` plus new files) against CLAUDE.md.

Check, and report findings in this order:

1. **Concurrency**: any state touched by more than one connection outside a room lock? Any plain
   List / Dictionary / HashSet shared across connections? Any lock taken inside a private helper?
2. **Information leaks**: does any DTO or broadcast now expose data a role should not see?
3. **Server authority**: did any game rule or validation move to the client?
4. **Tests**: does every behaviour change have a test? Are unhappy paths covered (disconnect, duplicate
   or out-of-turn actions, timers)? Was any test deleted or weakened?
5. **Edge cases** from the spec that the change does not handle.
6. Readability and naming, briefly.

For each finding give the file and line, why it matters, and a concrete fix.
Then run `dotnet test` and report the result. Finish with a one-line verdict: ready to commit, or not yet.
Do not fix anything yourself unless I ask.
