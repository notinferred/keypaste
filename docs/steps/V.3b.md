# V.3b — Add trash and recovery to the app

Completed 2026-09-20 in `d8c1a5e`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Build:** desktop Delete uses reversible deletion; a trash view restores a selected entry and offers separately confirmed permanent deletion. State the outcome of each action and clear entry/trash data on lock.

**Verify (V-V.3b):** through the app delete and restore an existing password and an env variable, then reopen the vault and use the restored values. Cancel leaves bytes unchanged; permanent deletion requires its own confirmation. Duplicate names resolve the selected identity, and locking clears displayed/revealed trash values. A populated test-only trash view is insufficient without exercising the deletion that creates it. Until this row lands, a deletion made in the app is recoverable only by opening the vault in KeePassXC, and the app says so.

## What changed for users

The desktop app takes a deletion back. Delete now says where the entry went — to the vault's recycle bin, or nowhere in a vault whose bin is switched off — and while that line is on screen a Restore button beside it puts a recycled entry straight back. Everything still in the bin is on a new Trash screen, `Ctrl/Cmd+6`: each entry's name, the group it came from and when it went. Restore returns one to that group, or to the root when the group has gone since, and says which. Delete for good erases one entry and its history behind a second confirmation, which is the only route to erasing a value keypaste wrote; emptying the whole bin is still KeePassXC's. A restore is refused outright when something else has taken the name in the meantime, and nothing is written. The trash shows no passwords: recovering is a choice between names, and a locked app has no list, no selection and no outcome line left. This is source only: there is still no desktop download.

## Evidence

A sixth destination over V.3a's operations, with core naming what it recycled: [screens](../../tests/Keypaste.App.Tests/ViewModels/TrashTests.cs) 15, core 35, hygiene 11, cli 4. verify.ps1 failed on line endings, then passed on `--from scripts`, records and workflows by name; D-0251 to D-0256

## Decisions

Ledger rows from this step, which constrain later work, stay in [DECISIONS](../../DECISIONS.md): D-0253. The rows below bind only this step's code and remain in force unless a later decision supersedes them.

| id | date | decision | supersedes |
|---|---|---|---|
| D-0251 | 2026-09-20 | Trash is the sixth sidebar destination, built and disposed by the shell like every other, so navigation and lock clear it by the rule every screen already follows | a trash pane inside Entries |
| D-0252 | 2026-09-20 | A trash row carries a title, the group it came from and when it went, and no field value, so a lock clears the list, the selection, the pending confirmation and the outcome line rather than a revealed value; V-V.3b is amended to that claim | V-V.3b's requirement to clear displayed trash values |
| D-0254 | 2026-09-20 | The compatibility gate keeps driving the trash through `tests/Keypaste.VaultRestorer` now that the desktop restores and purges, because a bash gate cannot press a button; the driver goes when a shipped command-line surface performs them | retiring the driver on the strength of a GUI surface |
| D-0255 | 2026-09-20 | `SetRecyclesDeletedEntries` stays internal and visible to the test projects rather than becoming public API, because KeePassXC owns a vault's recycle-bin setting and keypaste has no supported way to write it; a vault with the bin off is buildable for tests and this is no precedent for keypaste writing that setting | a public setter, or an untested bin-off path |
| D-0256 | 2026-09-20 | `RemoveEntry` returns the RecycledEntryId it produced, so the app's Undo restores the row it just made rather than inferring it from a before-and-after diff of the bin, and which row a delete produced stays core's answer (PRODUCT §4.2) | an adapter inferring a deletion's identity |

## Limits and follow-ups

Emptying the whole bin stays in KeePassXC. `verify.ps1` first failed on line endings, then passed resumed with `--from scripts`.
