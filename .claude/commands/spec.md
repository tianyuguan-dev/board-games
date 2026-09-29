Write a feature spec for: $ARGUMENTS

First read CLAUDE.md and the code this feature touches. Then save the spec as
`docs/specs/<kebab-case-feature-name>.md` with these sections:

## Problem
What is wrong or missing today, and for whom.

## Scope
In scope and explicitly out of scope.

## Behaviour
The rules, phase transitions and messages involved. For anything players see, state which roles see what.

## Edge cases
Disconnects and reconnects, concurrent actions from several players, out-of-turn or duplicate actions,
players leaving, timers expiring, invalid input.

## Acceptance criteria
A numbered list. Each item must be checkable by a test.

## Test plan
For each acceptance criterion, the unit or integration test that proves it.

Rules:
- Do not write implementation code.
- List open questions at the end instead of guessing at game rules, and stop for my answers.
