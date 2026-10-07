# V.5b — Organize and find credentials in the app

Completed 2026-09-21 in `6c22aa4`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Verify (V-V.5b):** through the app rename a group holding an env project and move an entry into it, then reopen the vault and read the moved entry at its new path with its history intact; `keypaste run` sees the renamed project. Search finds an entry by username and by URL and finds nothing by its password. A refused or cancelled rename leaves the bytes unchanged. Locking clears the query, the results and the selection. A view model asserting over a fixture list does not establish the move that produced it.

## Evidence

Search: title, group path, username and URL, never passwords, notes or protected fields. [rename an env project, move an entry into it, reopen, `keypaste run` sees it](../../tests/Keypaste.Consistency.Tests/GuiOrganizeIsVisibleToTheCliTests.cs); gate 2.7.10 on the combined write; D-0276 to D-0280

## Decisions

Ledger rows from this step, which constrain later work, stay in [DECISIONS](../../DECISIONS.md): D-0278. The row below binds only this step's code and remains in force unless a later decision supersedes it.

| id | date | decision | supersedes |
|---|---|---|---|
| D-0276 | 2026-09-21 | Organizing states, without blocking, that policy rules and agent exposures match paths and can stop applying or start applying because of it (THREATS T-13), with the specific consequence named where a group under `env` is renamed; D-0275 refuses a check in the write path, which is not a reason to say nothing on the form | organizing silently, or refusing it |

## Limits and follow-ups

The CLI has no organize or search verb. Tag search is unimplemented.
