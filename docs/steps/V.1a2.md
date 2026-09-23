# V.1a2 — Change a vault's password and keyfile

Completed 2026-09-22 in `851a3d5`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Build:** `keypaste access` changes a vault's master password and adds, replaces, removes or generates a supported keyfile, through the existing atomic save. The current unlock secret is verified by re-opening the file from disk before the change, and the new one after it. keypaste writes only the XML keyfile form, refuses to attach any other form, and leaves the vault unchanged when it refuses. It refuses to leave a vault with no master password — so removing or replacing the keyfile of a keyfile-only vault is refused unless a password is set in the same operation. An access change is refused when the file changed on disk since it was opened. Its save always keeps the file it replaces, exempt from the backup floor, and the command says what the change costs the backups already taken, which still open with the credentials they were made under.

**Verify (V-V.1a2):** change a password and a keyfile on fixtures, reopen each with the new secret and refuse the old one, and open both in real KeePassXC. A wrong current secret, an unsupported keyfile form, a vault changed on disk, a removal that would leave no password, and a failed save each leave the vault byte-identical and write no backup. Round-trip a keyfile KeePassXC created and one keypaste generated; a vault keypaste rewrote for an unsupported form fails this row. A backup taken before the change still opens under the old secret, and the change took its own copy even inside the floor.

## What changed for users

`keypaste access` changes what unlocks a vault. `--password` sets a new master password, asked for twice; `--new-keyfile <path>` adds a keyfile or replaces the current one; `--remove-keyfile` stops requiring it. It asks for the current password first, and `--keyfile` or `KEYPASTE_KEYFILE` still name the keyfile that opens the vault now, never the new one, so a variable left set in your shell cannot become a vault's new key. Piped, it reads the current password, then the new one and its confirmation, one line each. Before the vault is replaced, the new bytes are opened with the new credentials; after it, the file on disk is opened again with them. The vault keeps its own cipher, key derivation and format version.

keypaste never creates a keyfile and never takes a password away. It attaches a keyfile you already have — a KeePass XML file such as KeePassXC makes, or a 32-byte or 64-character hex file — and refuses any other file, because a vault keyed to an ordinary file's hash is one edit from lost. A vault that has a password keeps one. A vault a keyfile alone protects can change that keyfile and stay that way, but its keyfile goes only if a password is set in the same command. A vault whose existing keyfile is an arbitrary file can still have its password changed, keeping that file. A wrong current secret, a refused keyfile, a vault changed on disk, a removal that would leave nothing, an empty or mismatched new password and a failed backup each leave the vault byte-identical and keep no copy; a write that fails after the copy was taken leaves the vault as it was and says where the copy is.

Every access change keeps the file it replaces in `<vault>.backups`, whatever the fifteen-minute floor says, and says where. That copy and every earlier one still open with the credentials they were made under, so if an old password or keyfile was exposed, delete them; ordinary saves keep only the last five, so they leave over time, the change's own copy included. Losing an attached keyfile locks the vault, so keep a copy of it somewhere other than beside the vault. A `keypaste agent` already running keeps the vault it opened until you restart it. A vault that also needs a hardware key cannot be opened, and a wrong-secret refusal from `access` says so. Nothing yet changes access from the desktop, which is V.1b. The native-compiled CLI read a KeePass XML keyfile as the hash of the whole file, because trimming removed what its XML parser needs; it now reads the key inside, and a build that ever cannot again refuses XML keyfiles by name, exit 3, rather than key a vault with the wrong material.

## Evidence

`keypaste access`: core 36, cli 21; three guards fail 1 each when dropped. AOT keyed XML keyfiles by hash: `KfxFile` trimmed, now rooted; both XML [gates](../../scripts/verify-keepassxc-xml-attach.sh) pass on a fresh AOT build. A flaky word test and D-0236 fixed; D-0287 to D-0294

## Decisions

Ledger rows from this step, which constrain later work, stay in [DECISIONS](../../DECISIONS.md): D-0287, D-0288, D-0292, D-0294. The rows below bind only this step's code and remain in force unless a later decision supersedes them.

| id | date | decision | supersedes |
|---|---|---|---|
| D-0289 | 2026-09-22 | An access change backs up like `SaveOverwriting` — always a copy, exempt from the floor and the per-unlock flag, which it does not set, pruned to five — and refuses a changed file like `Save`; a refusal keeps no copy, and a write that fails after the copy leaves the vault and names the copy | — |
| D-0290 | 2026-09-22 | `keypaste access` takes the new side from `--password`, `--new-keyfile` and `--remove-keyfile` alone; `--keyfile` and `KEYPASTE_KEYFILE` always name the keyfile that opens the vault now, so a variable left set in a shell can never become a vault's new key | — |
| D-0291 | 2026-09-22 | An access change keeps the vault's own KDF and its parameters, cipher and KDBX version; only the seeds every save redraws and `MasterKeyChanged` change. Upgrading a weak KDF stays a BACKLOG option | — |
| D-0293 | 2026-09-22 | The re-key write serialises the vault into memory, opens those bytes with the new factors read again from disk, and only then commits them through `FileTransactionEx`, so a failed check leaves the vault byte-identical; once committed the `Vault` is sealed and refuses every write until the vault is reopened, and before that a failure puts the previous key back | — |

## Limits and follow-ups

The founder amended the scope above on 2026-09-22: keypaste never generates or writes a keyfile (D-0287), a keyfile-only vault may stay keyfile-only (D-0288), and compatibility is required at the file level (D-0292). Keyfile generation and deleting old backups after an access change are [BACKLOG](../BACKLOG.md) options. Three defects found during the step were repaired within it: the NativeAOT CLI read an XML keyfile as its hash because trimming removed `KfxFile`, a passphrase test matched list words inside ordinary prose, and D-0236 named a threat ID the rescope had removed and T-26 later reused.
