# V.4b — Restore and export an encrypted vault from the app

Completed 2026-09-20 in `00882fe`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Build:** show the backups V.4a actually produces, open a selected backup for validation, and restore only after confirmation while preserving the replaced vault until success. Offer a separate encrypted whole-vault copy/export. State that backups may require an earlier password/keyfile and cannot recover a lost unlock secret.

**Verify (V-V.4b):** change a credential, create its backup through a real save, restore it through the desktop and reopen the restored vault. Cancel, a wrong unlock secret, corrupt backup and destination failure preserve the live file. The encrypted export opens independently in KeePassXC; a file chooser or backup reader without the save-to-restore journey does not pass.

## What changed for users

The desktop app puts a backup back. On the unlock screen, with the app locked, a vault's backups are listed by when they were taken; typing the master password a copy was made under opens it, and the app shows when it was taken, how many entries, groups and env projects it holds and which file it will replace, and no name or value from inside it. Behind a confirmation the copy replaces the vault byte for byte, the app opens it, and a line says what happened. It works on a vault that opens, on one whose file is damaged or is no longer a KeePass file, and on one that is missing, which is selectable for restoring only and, when missing, only from the recent list. The file a restore replaces is kept as a backup first, unless a listed copy already holds exactly its bytes, and a restore never drops a copy: the next ordinary save trims back to five. A wrong password, a damaged copy, a copy changed after it was checked, a vault that changed meanwhile and a file that could not be replaced each leave the vault exactly as it was.

A restored vault opens with the password its backup was made under, not the one it replaced; nothing re-encrypts it, which is also why the copies can be trusted. So somebody who knows an earlier master password and can reach the disk can roll the vault back to that copy. They could already read that copy in KeePassXC, and the vault they replaced is kept. A password typed on the restore panel is cleared after the idle timeout, on minimize when that setting is on, and on every way out. KeePassXC and a terminal `keypaste agent` keep their own copy of the vault and do not notice a restore; close them first. Settings now says where the copies are kept and exports an exact encrypted copy of the vault to a new file, refusing an existing file, the vault itself and its backup directory. An exported copy is one more offline guessing target under the same master password, and it is the copy that survives losing the disk. The CLI has no restore or export verb, no keyfile can be supplied, and nothing recovers a forgotten password. This is source only: there is still no desktop download, and the published CLI is unchanged.

## Evidence

Locked restore over a healthy, damaged or missing vault, and an export: [core](../../tests/Keypaste.Core.Tests/VaultRestoreTests.cs) 39, screens 17, export 9, D-0099 3, cli 1; dropping keep fails 6, dedupe 3, digest 1, re-check 3; gate 2.7.10 locally; D-0264 to D-0270

## Decisions

Ledger rows from this step, which constrain later work, stay in [DECISIONS](../../DECISIONS.md): D-0264, D-0265. The rows below bind only this step's code and remain in force unless a later decision supersedes them.

| id | date | decision | supersedes |
|---|---|---|---|
| D-0266 | 2026-09-20 | A restore keeps the file it replaces as a floor-exempt backup unless a listed copy already holds its exact bytes, and never prunes, leaving that to the next save; `SaveOverwriting` keeps D-0259's rule because it discards somebody else's write, no longer because it is the restore path | D-0259's rationale, and a restore that pruned the copy it was restoring |
| D-0267 | 2026-09-20 | `VaultBackups.Restore` accepts only what `Inspect` returned, which carries the vault path and an internal digest of the bytes it opened, and re-reads the live file before every rename attempt; a backup swapped after its check, another vault's token and a vault that changed or appeared mid-restore are each refused with nothing replaced | a restore taking a path and a password |
| D-0268 | 2026-09-20 | `Vault.ExportTo` writes the saved file's bytes to a new path with a guard of its own: it refuses an existing file or directory, the vault under any spelling, the backup directory and anything inside it whatever the name, and a vault changed on disk; D-0092's KDBX-destination refusal stays `env export`'s, since that writer is plaintext | reusing or parameterising `TryRefuseTheVault` |
| D-0269 | 2026-09-20 | The unlock screen selects a missing or non-KDBX path, for restoring only, when `VaultBackups.List` names copies beside it; a missing vault is reachable from the recent list alone, and a forgotten one is recovered by opening a backup directly | `Offer` refusing every path that is not a readable vault |
| D-0270 | 2026-09-20 | A password typed on the restore panel, and a checked backup waiting on its confirmation, are zeroed after the session's idle timeout, on minimize when that setting is on, and on every other way out; a correct master password one click from an open vault does not wait indefinitely on a locked screen | the locked screen having no deadline of any kind |

## Limits and follow-ups

The CLI has no restore or export verb. A missing vault is reachable only from the recent list. The KeePassXC gate ran locally. No keyfile can be supplied to a restore until V.1b.
