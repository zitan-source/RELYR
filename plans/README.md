# Animation implementation plans

| Plan | Title | Severity | Status |
| --- | --- | --- | --- |
| 001 | Centralize safe optional motion | HIGH | DONE |
| 002 | Keep layer editing direct and stable | HIGH | DONE |
| 003 | Give Deck a restrained arrival and scale-fade departure | HIGH | DONE |
| 004 | Reveal Action drops from the target center | HIGH | DONE |

Recommended order: 001, 002, 003. Plan 001 supplies the failure-contained primitives
used by the user-facing plans. Plan 004 can run after 001 and is independent of Plans 002
and 003.
