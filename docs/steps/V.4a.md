# V.4a — Produce recoverable encrypted vault backups

Completed 2026-09-20 in `c4a4b3d`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Evidence

A copy inside the save gate, after the re-read: [core](../../tests/Keypaste.Core.Tests/VaultBackupTests.cs) 23, dropping `gated` fails 19, the future stamp 1, the floor exemption 2, early pruning 1; [gate](../../scripts/verify-keepassxc-backup.sh) 2.7.10; D-0257 to D-0263

## Decisions

Ledger rows from this step, which constrain later work, stay in [DECISIONS](../../DECISIONS.md): D-0257, D-0258, D-0263. The row below binds only this step's code and remains in force unless a later decision supersedes it.

| id | date | decision | supersedes |
|---|---|---|---|
| D-0260 | 2026-09-20 | The CLI names the backup directory once, on stderr, on the save that creates it, and is silent after; a directory of encrypted vaults appearing beside somebody's file unannounced is the discovery PRODUCT §6.1 calls a risk to trust, and a line on every save is noise a script would silence | silent creation, or a line on every save |

## Limits and follow-ups

Five copies beside the vault do not survive losing the disk or the directory. No copy recovers a forgotten password.
