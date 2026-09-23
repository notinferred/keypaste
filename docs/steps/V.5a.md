# V.5a — Add stable rename and move operations

Completed 2026-09-20 in `607f474`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Build:** core operations create/rename groups and rename/move entries while preserving UUID, fields and history. Refuse ambiguous destinations and invalid env names. Fresh resolution uses the resulting path for exposure and env validation; U.3 owns invalidation of existing session snapshots and cached grants after a move. Clone, tags and metadata depth are outside this slice.

**Verify (V-V.5a):** rename and move fixtures with duplicate titles and protected/unknown fields, reopen them and compare identity and content. Invalid or ambiguous operations write nothing; fresh resolution after a move cannot use the old path. Cross-process snapshots and already-cached authorization are verified in U.3, not inferred from these core operations. Each written fixture opens in real KeePassXC.

## What changed for users

keypaste renames and moves what is in a vault. Core creates a group, renames one, renames an entry and moves one between groups, changing what is there rather than making a copy, so an entry keeps its identity, its timestamps, its attachments, the fields keypaste does not model and every revision in its history. Renaming the group `env/billing` to `invoicing` is how a project is renamed: `keypaste run invoicing` finds it afterwards and `keypaste run billing` does not. A refusal writes nothing at all, on disk or in the vault that is open: a name the destination already answers to, a result that would make two entries answer to one path, a name no vault could address, a variable name nothing could export, two variables in one project differing only in case, and the two names keypaste assigns meaning to — `env` at the root and the recycle bin — which no group may be created as, renamed to, or renamed away from. A recycled entry is not there to rename, and the bin is not a destination: a move is not a delete.

A vault that has only been organized is still KDBX 4.0. Deleting raises a file to 4.1 so it can record where an entry came from, which costs every reader below KeePassXC 2.7 and KeePass 2.48; tidying a folder records nothing of the kind and so costs nothing. Restoring an old revision no longer renames the entry back to what it was called when that revision was taken: a revision is the values it held, and what the entry is called is not one of them.

## Evidence

One validated write, file still 4.0: [core](../../tests/Keypaste.Core.Tests/VaultOrganizeTests.cs) 13, refusals 41, history 18, cli 4, tripwire 1; dropping collisions fails 3, env 6, reserved 2, restore 2, EnsureGroup 3, 4.1 stamp 1; gate 2.7.10, ci 35558240988 on three at a80c187; D-0271 to D-0275

## Decisions

Ledger rows from this step, which constrain later work, stay in [DECISIONS](../../DECISIONS.md): D-0271, D-0273, D-0275. The rows below bind only this step's code and remain in force unless a later decision supersedes them.

| id | date | decision | supersedes |
|---|---|---|---|
| D-0272 | 2026-09-20 | Renaming and moving an entry are one write behind one validation pass that also runs for every entry a group rename re-paths, refusing a taken name, a path that would name two things, an unexportable env name and two KEYs differing only in case; every check is a read and the mutation is last, so a refusal writes nothing in memory either | mutating and undoing, and `EnsureGroup` resolving a move's destination |
| D-0274 | 2026-09-20 | Restoring a revision re-applies the entry's current title after `RestoreFromBackup`, so a revision restores values and never identity (D-0091); `ReadHistory` still reports the title each revision carried, which is what `EntryRevision` already documents | a restore assigning every stored string back, rename included |

## Limits and follow-ups

Clone, tags and metadata depth are outside the slice. Removing a group and moving one to another parent are not in core ([BACKLOG](../BACKLOG.md)). U.3 owns invalidating cached authorization after a move; THREATS T-13 records that a rename can widen a standing rule.
