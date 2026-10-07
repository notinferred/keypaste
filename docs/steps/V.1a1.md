# V.1a1 — Open a vault a keyfile protects

Completed 2026-09-21 in `9823be6`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Build:** core and CLI change a vault's master password and add, replace or remove a supported KDBX keyfile, verifying the old unlock secret before the change and the new one after it, through the existing atomic save. Name the supported keyfile forms and reject the rest without rewriting the vault. Say what the change costs an existing backup, which still opens with the credentials it was made under.

**Verify (V-V.1a):** change a password and a keyfile on fixtures, reopen each with the new secret and refuse the old one, and open both in real KeePassXC. A wrong current secret, an unsupported keyfile form and a failed save leave the vault byte-identical. Round-trip a keyfile KeePassXC created; a vault keypaste rewrote for an unsupported form fails this row.

## Evidence

Four forms, `--keyfile` on the eleven opening verbs: [core](../../tests/Keypaste.Core.Tests/VaultKeyfileTests.cs) 20, cli 23; dropping the empty-password guard fails 1, on a fixture the library keys rather than keypaste; [gate](../../scripts/verify-keepassxc-keyfile.sh) 2.7.10; D-0281 to D-0286

## Decisions

Ledger rows from this step, which constrain later work, stay in [DECISIONS](../../DECISIONS.md): D-0281, D-0282, D-0285. The row below binds only this step's code and remains in force unless a later decision supersedes it.

| id | date | decision | supersedes |
|---|---|---|---|
| D-0284 | 2026-09-21 | An unusable keyfile is refused before the master-password prompt and worded from `KeyfileOutcome` in `VaultSession`, and a refusal after the prompt names both factors when a keyfile was offered; being asked for a password and then told the keyfile was never there wastes the one thing the person had to supply | one wrong-unlock-secret message for every cause |

## Limits and follow-ups

V.1a was split on 2026-09-21: this step opens keyfile vaults and V.1a2 changes what unlocks one, so the scope above is V.1a's. The desktop cannot open a keyfile vault until V.1b.
