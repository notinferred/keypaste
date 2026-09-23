# V.3a — Implement reversible deletion

Completed 2026-09-20 in `b9016ba`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Build:** add core operations to move an entry to the KDBX recycle bin, list recoverable entries and restore one by stable identity with its fields and history. Keep permanent deletion explicit and separate, preserve existing tombstones, and exclude recycled entries from normal listing, env injection and credential release. Save through the current stale-write and atomic-write boundary.

**Verify (V-V.3a):** delete, close, reopen and restore a fixture entry with history and duplicate titles; its UUID and data survive and the right entry returns. Fresh core credential/env resolution excludes recycled values; U.3 separately proves invalidation of an already-open session and its grants. Permanent deletion, a refused save and an interrupted save have their stated outcomes. Real KeePassXC opens the resulting vault and sees the same recycle/recovery state.

## What changed for users

Deleting an entry no longer destroys it. `keypaste rm` and the desktop's Delete move the entry to the vault's KDBX recycle bin, keeping its identity, its fields and every revision in its history, and both say so rather than saying there is no undo. KeePassXC opens the same file and shows the same bin, because it is KeePass's own: the entry is where KeePassXC would have put it, and KeePassXC can restore it. A vault whose owner switched the recycle bin off in KeePassXC still deletes permanently, and the confirmation says that instead. Nothing else in keypaste can see a recycled entry — not `keypaste ls`, not the app's list, not `keypaste run`, and not an agent, whatever it has been exposed. Core lists what is in the bin, puts one back where it came from and removes one for good, and the app's Trash screen above is where a person reaches all three. `keypaste rm` still has no counterpart in the CLI, so recovering from a terminal alone means KeePassXC.

The cost is worth stating plainly: a deleted value is still in the file. Erasing something keypaste wrote is now two deliberate acts, the delete and then the purge, where it used to be one. A restore is refused outright if another entry has taken the name in the meantime, because two entries answering to one name is the condition keypaste refuses everywhere else, and a recovery must not create it. An entry whose group is gone comes back at the root.

A vault that has recycled anything is written as KDBX 4.1 instead of 4.0. The field that records where an entry came from exists only in 4.1, and the vendored writer did not ask for 4.1 on its account, so a save dropped it and a restore after reopening the file had nowhere to put the entry. KeePassXC 2.7 and KeePass 2.48 and later read 4.1; older readers do not, and a vault only changes once something has actually been deleted. This is source only: there is still no desktop download, and the published CLI is unchanged.

## Evidence

One bin behind five `Vault` operations, hidden from every traversal a read uses: [core](../../tests/Keypaste.Core.Tests/VaultRecycleBinTests.cs) 33, dropping the filter fails 8 and the 4.1 guard 3; [gate](../../scripts/verify-keepassxc-recyclebin.sh) 2.7.10, ci 35521077857 green on three; D-0246 to D-0250

## Decisions

Ledger rows from this step, which constrain later work, stay in [DECISIONS](../../DECISIONS.md): D-0246, D-0247, D-0248. The rows below bind only this step's code and remain in force unless a later decision supersedes them.

| id | date | decision | supersedes |
|---|---|---|---|
| D-0249 | 2026-09-20 | A restore is refused whole when another entry already answers to the name it would produce, and lands at the root when the group it came from is gone or is itself recycled | inventing a destination for a recovery; recreating the ambiguity D-0091 refuses |
| D-0250 | 2026-09-20 | A recycled entry is addressed by an opaque identity carried on its trash row rather than by name, path or list position, because two entries of one title land in one bin and an index addresses a reading rather than a thing | name-only addressing, for a deleted entry |

## Limits and follow-ups

The CLI has no trash verb, so recovery from a terminal alone means KeePassXC. A vault that has recycled anything is written as KDBX 4.1. U.3 owns invalidating an already-open session and its grants after a delete.
