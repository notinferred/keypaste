# V.5b — Organize and find credentials in the app

Completed 2026-09-21 in `6c22aa4`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Build:** desktop controls that create and rename a group, rename an entry and move one between groups through V.5a's core operations, and a search over titles, group paths, usernames and URLs. Secret values are never indexed, matched or shown in a result. The selected entry survives a rename, a move, a search and a change of view, and lock clears the query and the results with everything else.

**Verify (V-V.5b):** through the app rename a group holding an env project and move an entry into it, then reopen the vault and read the moved entry at its new path with its history intact; `keypaste run` sees the renamed project. Search finds an entry by username and by URL and finds nothing by its password. A refused or cancelled rename leaves the bytes unchanged. Locking clears the query, the results and the selection. A view model asserting over a fixture list does not establish the move that produced it.

## What changed for users

The desktop app organizes and finds. The group tree offers New group, which makes a group inside whichever one is selected or at the top level, and Rename, which is not offered for "All entries" because the root is not a group anybody named. Organize on an entry opens one form with its name and its group, and one Save changes either or both in a single write, so an entry moved and renamed at once keeps its identity, its history and everything keypaste does not model. A refusal writes nothing at all and says which refusal it was; the form stays open with what was typed, and nothing is applied by clicking away. Renaming a group follows it in the sidebar, creating one leaves you where you were, and an entry that was moved takes the filter with it.

Search now covers usernames and URLs as well as titles and group paths. It never covers a password, a note or a protected custom field: the matching happens in the vault, which does not read those fields at all, and what comes back is an entry name and the names of the fields that matched. So a row can say it matched on a username without the username reaching the screen, and no query can confirm a recovery code somebody pasted into a note. A search no longer clears what you had selected, and neither does changing group; an entry renamed out of its own result stays listed until the query changes. Locking takes the query, the results and the selection with everything else.

Organizing can change which standing authorization covers an entry, and both forms say so. A policy rule and an agent exposure are patterns matched against a group path and a title, so they follow neither: renaming away from a granted path stops the rule matching, and renaming onto one starts it. Where the group being renamed is a project, the form also names what it costs — `keypaste run <old name>` stops finding it. Neither line blocks anything.

Removing a group or moving one to a different parent is still KeePassXC's, and the CLI has no verb for any of this, nor for search. This is source only: there is still no desktop download, and the published CLI is unchanged.

## Evidence

Search: title, group path, username and URL, never passwords, notes or protected fields. [rename an env project, move an entry into it, reopen, `keypaste run` sees it](../../tests/Keypaste.Consistency.Tests/GuiOrganizeIsVisibleToTheCliTests.cs); gate 2.7.10 on the combined write; D-0276 to D-0280

## Decisions

Ledger rows from this step, which constrain later work, stay in [DECISIONS](../../DECISIONS.md): D-0278. The rows below bind only this step's code and remain in force unless a later decision supersedes them.

| id | date | decision | supersedes |
|---|---|---|---|
| D-0276 | 2026-09-21 | Organizing states, without blocking, that policy rules and agent exposures match paths and can stop applying or start applying because of it (THREATS T-13), with the specific consequence named where a group under `env` is renamed; D-0275 refuses a check in the write path, which is not a reason to say nothing on the form | organizing silently, or refusing it |
| D-0277 | 2026-09-21 | The Entries screen holds its selection as an `EntryName` and ignores the null the list writes back when a row leaves it, so a search, a group change or a rename cannot clear the detail pane; an entry renamed out of its own result stays listed until the query changes | a selection held as the row object |
| D-0279 | 2026-09-21 | `OrganizeOutcome.RenamedAndMoved` reports a write that changed both halves of a name, and `Relocate` computes which of the three success members it returns from what actually varied rather than taking it from its caller | the caller declaring `Renamed` or `Moved` for a combined change |
| D-0280 | 2026-09-21 | The desktop performs one core organize operation per confirm, through `Vault.Relocate`, which takes the whole target name; composing `RenameEntry` and `MoveEntry` would let the rename commit in the open vault while the move beside it was refused, for the next unrelated save to write out | a form with a submit per field, or a separate validation call before two writes |

## Limits and follow-ups

The CLI has no organize or search verb. Tag search is unimplemented.
