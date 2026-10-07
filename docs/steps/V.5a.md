# V.5a — Add stable rename and move operations

Completed 2026-09-20 in `607f474`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Evidence

One validated write, file still 4.0: [core](../../tests/Keypaste.Core.Tests/VaultOrganizeTests.cs) 13, refusals 41, history 18, cli 4, tripwire 1; dropping collisions fails 3, env 6, reserved 2, restore 2, EnsureGroup 3, 4.1 stamp 1; gate 2.7.10, ci 35558240988 on three at a80c187; D-0271 to D-0275

## Decisions

Ledger rows from this step, which constrain later work, stay in [DECISIONS](../../DECISIONS.md): D-0271, D-0273, D-0275. The row below binds only this step's code and remains in force unless a later decision supersedes it.

| id | date | decision | supersedes |
|---|---|---|---|
| D-0272 | 2026-09-20 | Renaming and moving an entry are one write behind one validation pass that also runs for every entry a group rename re-paths, refusing a taken name, a path that would name two things, an unexportable env name and two KEYs differing only in case; every check is a read and the mutation is last, so a refusal writes nothing in memory either | mutating and undoing, and `EnsureGroup` resolving a move's destination |

## Limits and follow-ups

Clone, tags and metadata depth are outside the slice. Removing a group and moving one to another parent are not in core ([BACKLOG](../BACKLOG.md)). U.3 owns invalidating cached authorization after a move; THREATS T-13 records that a rename can widen a standing rule.
