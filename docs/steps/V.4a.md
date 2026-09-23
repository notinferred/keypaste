# V.4a — Produce recoverable encrypted vault backups

Completed 2026-09-20 in `c4a4b3d`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Build:** before replacing an existing vault, preserve its last readable encrypted bytes as a versioned backup with a bounded retention rule. First creation is separate. A failed required backup prevents the overwrite; a failed save cannot destroy the prior vault or last good backup. Reuse the existing concurrent-save guard and keep unlock secrets out of filenames and metadata.

**Verify (V-V.4a):** actual saves produce backups that reopen with the appropriate vault credentials and contain the previous values. Exercise full/read-only storage, retention, an external writer and termination at backup/save boundaries. A directory listing alone does not prove a valid backup. Both surviving vault and backups open in real KeePassXC.

## What changed for users

Keypaste keeps the vault it is about to replace. Before a save writes over an existing vault, the file that is there is copied into a `<vault>.backups` directory beside it, and the last five copies are kept. The first save after each unlock takes one, and never a second within fifteen minutes of the newest — so a desktop session of a dozen edits leaves one copy of how the vault looked when it was opened, and a script firing off six `keypaste env set` commands leaves one too, rather than five from the same minute and nothing older. Creating a vault takes none: there is nothing yet to keep. The copies are the vault's own encrypted bytes, unread and unrewritten, so each one opens in KeePassXC and in `keypaste get --vault` exactly as the vault did, and a copy of a vault that has recycled anything is KDBX 4.1 like its source. The terminal says where the directory is the once, when it makes it.

A save that cannot take its copy does not happen. A full disk, a read-only folder or a wrong permission leaves the vault byte-identical and says which it was, and there is no flag, setting or environment variable that turns this off — a backup you can switch off is a backup you find switched off. A save that overwrites somebody else's change is exempt from waiting: it always copies what it replaces, because that file is exactly what somebody needs when overwriting turns out to have been the wrong choice. Nothing prunes until the new copy is written and named, so a bad day costs a save and never the last good copy.

The cost is worth stating plainly: there are now up to five more encrypted copies of the vault on the disk. Each is an offline guessing target in its own right, under whatever master password it was made with — change the password and the copies already taken still open with the old one, and none of them recovers a password nobody remembers. Dropping the sixth deletes a directory entry and nothing else; the bytes stay where the storage left them. Five copies in a folder beside the file do not survive losing the disk, the folder or the machine, so keeping protected copies elsewhere is still the advice. The CLI has no verb for them. This is source only: there is still no desktop download, and the published CLI is unchanged.

## Evidence

A copy inside the save gate, after the re-read: [core](../../tests/Keypaste.Core.Tests/VaultBackupTests.cs) 23, dropping `gated` fails 19, the future stamp 1, the floor exemption 2, early pruning 1; [gate](../../scripts/verify-keepassxc-backup.sh) 2.7.10; D-0257 to D-0263

## Decisions

Ledger rows from this step, which constrain later work, stay in [DECISIONS](../../DECISIONS.md): D-0257, D-0258, D-0263. The rows below bind only this step's code and remain in force unless a later decision supersedes them.

| id | date | decision | supersedes |
|---|---|---|---|
| D-0259 | 2026-09-20 | `SaveOverwriting` always backs up, exempt from both the per-unlock flag and the floor, and does not set the flag; it is the restore path, and the vault a restore replaces is the copy needed when the restore was the wrong one | one suppression rule shared by both save paths |
| D-0260 | 2026-09-20 | The CLI names the backup directory once, on stderr, on the save that creates it, and is silent after; a directory of encrypted vaults appearing beside somebody's file unannounced is the discovery PRODUCT §6.1 calls a risk to trust, and a line on every save is noise a script would silence | silent creation, or a line on every save |
| D-0261 | 2026-09-20 | `GenerateFlagTests` asks that no word of a generated passphrase reaches stderr, instead of that stderr holds no full stop; the separator is a full stop and stderr now carries a file path, so absence of the character was a proxy a sentence could break with nothing leaked | the separator's absence as the stderr claim |
| D-0262 | 2026-09-20 | A backup is a byte copy and not a KDBX write, so D-0050's condition for giving `app.yml` a KeePassXC job of its own does not fire; `ci.yml`'s compat job covers the copy on all three operating systems | a second KeePassXC job on app.yml |

## Limits and follow-ups

Five copies beside the vault do not survive losing the disk or the directory. No copy recovers a forgotten password.
