# V.1a1 — Open a vault a keyfile protects

Completed 2026-09-21 in `9823be6`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Build:** core and CLI change a vault's master password and add, replace or remove a supported KDBX keyfile, verifying the old unlock secret before the change and the new one after it, through the existing atomic save. Name the supported keyfile forms and reject the rest without rewriting the vault. Say what the change costs an existing backup, which still opens with the credentials it was made under.

**Verify (V-V.1a):** change a password and a keyfile on fixtures, reopen each with the new secret and refuse the old one, and open both in real KeePassXC. A wrong current secret, an unsupported keyfile form and a failed save leave the vault byte-identical. Round-trip a keyfile KeePassXC created; a vault keypaste rewrote for an unsupported form fails this row.

## What changed for users

keypaste opens a vault a keyfile protects. Every command that opens a vault now takes `--keyfile <path>`, or reads `KEYPASTE_KEYFILE` when it does not — `ls`, `get`, `add`, `rm`, the whole of `env`, `run` and `agent`, so a vault that gains a keyfile does not quietly stop working in the terminal or for an AI client. All four forms KeePass accepts open: a KeePass XML keyfile, a 32-byte file, a 64-character hex file, and any other file at all, keyed by the hash of its contents. So does a vault with a keyfile and no password, which KeePassXC has always been able to make: pressing Enter at the password prompt is how you say there is none, and an empty password on a vault that has one is still wrong.

That fourth form deserves its warning and gets one. A vault keyed to an ordinary file is keyed to that file's exact bytes, so editing it, re-encoding it or letting something sync it over loses the vault permanently, and nothing but the file's former contents brings it back. keypaste says so on stderr, once, each time it opens such a vault, and never on stdout, where a pipe or a launched program would get it. It will not attach a keyfile of that kind. Two lengths are worth knowing about and draw no warning, because nothing can tell them from a deliberate choice: a file of exactly 32 bytes is used as key material as it stands, and one of exactly 64 hexadecimal characters is decoded, by keypaste and KeePassXC alike.

A keyfile that is not there, is empty, cannot be read, or is itself a KeePass vault is refused by name before you are asked for a password — being made to type it and only then told the keyfile was missing wastes the one thing you had to supply. After the prompt, a refusal says "wrong master password or keyfile" when a keyfile was given, because a good password and the wrong file otherwise sends you to retype the half that was right. Nothing is written by any refusal. A vault keypaste saves still needs its keyfile afterwards, and so does each of the five copies kept beside it.

`keypaste setup` takes no keyfile. It configures `keypaste-mcp`, which never opens a vault, so a keyfile there would write down where your second factor lives and buy nothing; the keyfile belongs to the `keypaste agent` you start. Nothing else records one either — the recent-vault list keeps vault paths and nothing more, and every command is told again. Both `--keyfile` on a command line and `KEYPASTE_KEYFILE` in the environment are readable by anything running as you, and a command line lingers in shell history; what they disclose is where the file is, not what is in it.

`keypaste init` takes no keyfile; `keypaste access` attaches one afterwards. The desktop cannot open a keyfile vault yet, and nothing recovers a keyfile you have lost any more than a password you have forgotten. This is source only: there is still no desktop download, and the published CLI is unchanged.

## Evidence

Four forms, `--keyfile` on the eleven opening verbs: [core](../../tests/Keypaste.Core.Tests/VaultKeyfileTests.cs) 20, cli 23; dropping the empty-password guard fails 1, on a fixture the library keys rather than keypaste; [gate](../../scripts/verify-keepassxc-keyfile.sh) 2.7.10; D-0281 to D-0286

## Decisions

Ledger rows from this step, which constrain later work, stay in [DECISIONS](../../DECISIONS.md): D-0281, D-0282, D-0285. The rows below bind only this step's code and remain in force unless a later decision supersedes them.

| id | date | decision | supersedes |
|---|---|---|---|
| D-0283 | 2026-09-21 | Opening a vault keyed to an arbitrary hashed file writes one line on stderr, once per open, because `keypaste get` is piped and `keypaste run` hands stdout to a child; the form is not refused, since KeePassXC wrote such vaults for years | refusing the form, or saying nothing, or saying it on stdout |
| D-0284 | 2026-09-21 | An unusable keyfile is refused before the master-password prompt and worded from `KeyfileOutcome` in `VaultSession`, and a refusal after the prompt names both factors when a keyfile was offered; being asked for a password and then told the keyfile was never there wastes the one thing the person had to supply | one wrong-unlock-secret message for every cause |
| D-0286 | 2026-09-21 | `keypaste setup` takes no `--keyfile`: it registers `keypaste-mcp`, which opens no vault, so a keyfile in a client's configuration file would record where the second factor is kept and change nothing the bridge can do | a keyfile on every verb that names a vault |

## Limits and follow-ups

V.1a was split on 2026-09-21: this step opens keyfile vaults and V.1a2 changes what unlocks one, so the scope above is V.1a's. The desktop cannot open a keyfile vault until V.1b.
