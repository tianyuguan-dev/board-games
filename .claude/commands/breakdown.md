Break the spec in $ARGUMENTS into implementation tasks.

Read the spec and CLAUDE.md first. Then write a numbered task list at the end of the spec file under
`## Tasks`. Each task must:

- change one thing, small enough to review in a few minutes
- name the files it expects to touch
- name the tests that will prove it (new or existing)
- be independently buildable and testable, so the suite stays green after every task

Order tasks so that shared model or DTO changes come before hub and UI changes.
Flag any task that touches shared mutable state or a per-player DTO, because those need extra review.
Do not start implementing.
