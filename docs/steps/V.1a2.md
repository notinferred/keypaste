# V.1a2 — Change a vault's password and keyfile

Completed 2026-09-22 in `851a3d5`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Verify (V-V.1a2):** change a password and a keyfile on fixtures, reopen each with the new secret and refuse the old one, and open both in real KeePassXC. A wrong current secret, an unsupported keyfile form, a vault changed on disk, a removal that would leave no password, and a failed save each leave the vault byte-identical and write no backup. Round-trip a keyfile KeePassXC created and one keypaste generated; a vault keypaste rewrote for an unsupported form fails this row. A backup taken before the change still opens under the old secret, and the change took its own copy even inside the floor.

## Evidence

`keypaste access`: core 36, cli 21; three guards fail 1 each when dropped. AOT keyed XML keyfiles by hash: `KfxFile` trimmed, now rooted; both XML [gates](../../scripts/verify-keepassxc-xml-attach.sh) pass on a fresh AOT build. A flaky word test and D-0236 fixed; D-0287 to D-0294

## Decisions

Ledger rows from this step, which constrain later work, stay in [DECISIONS](../../DECISIONS.md): D-0287, D-0288, D-0292, D-0294. The rows below bind only this step's code and remain in force unless a later decision supersedes them.

| id | date | decision | supersedes |
|---|---|---|---|
| D-0289 | 2026-09-22 | An access change backs up like `SaveOverwriting` — always a copy, exempt from the floor and the per-unlock flag, which it does not set, pruned to five — and refuses a changed file like `Save`; a refusal keeps no copy, and a write that fails after the copy leaves the vault and names the copy | — |
| D-0290 | 2026-09-22 | `keypaste access` takes the new side from `--password`, `--new-keyfile` and `--remove-keyfile` alone; `--keyfile` and `KEYPASTE_KEYFILE` always name the keyfile that opens the vault now, so a variable left set in a shell can never become a vault's new key | — |
| D-0293 | 2026-09-22 | The re-key write serialises the vault into memory, opens those bytes with the new factors read again from disk, and only then commits them through `FileTransactionEx`, so a failed check leaves the vault byte-identical; once committed the `Vault` is sealed and refuses every write until the vault is reopened, and before that a failure puts the previous key back | — |

## Limits and follow-ups

The founder amended the scope above on 2026-09-22: keypaste never generates or writes a keyfile (D-0287), a keyfile-only vault may stay keyfile-only (D-0288), and compatibility is required at the file level (D-0292). Keyfile generation and deleting old backups after an access change are [BACKLOG](../BACKLOG.md) options. Three defects found during the step were repaired within it: the NativeAOT CLI read an XML keyfile as its hash because trimming removed `KfxFile`, a passphrase test matched list words inside ordinary prose, and D-0236 named a threat ID the rescope had removed and T-26 later reused.
