# V.3a — Implement reversible deletion

Completed 2026-09-20 in `b9016ba`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Verify (V-V.3a):** delete, close, reopen and restore a fixture entry with history and duplicate titles; its UUID and data survive and the right entry returns. Fresh core credential/env resolution excludes recycled values; U.3 separately proves invalidation of an already-open session and its grants. Permanent deletion, a refused save and an interrupted save have their stated outcomes. Real KeePassXC opens the resulting vault and sees the same recycle/recovery state.

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
