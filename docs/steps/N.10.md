# N.10 — Make every saving verb take the vault's claim

Completed 2026-09-29 on `main` above `6dd2468`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Build:** `add`, `rm`, `access`, `env set`, `env rm`, `env pull` and `import`, the verbs that still save inside a `VaultSession.Open` body, open the vault through `OpenHeld`, as `set`, `rotate`, `field rm` and `env tag` already do. `import`'s own claim moves onto `OpenHeld`, and `import --dry-run` still reads without the claim.

While the app or `keypaste agent` holds the vault, each is refused before its password is read. The refusal names the holder, its process and the next step:
- for the app, make the change there or run `keypaste lock` and try again;
- for `keypaste agent`, run `keypaste lock` and try again.

A rule test over `src/Keypaste.Cli` holds that `VaultSession.Open` is called only by verbs that never save. Traces to PRODUCT §2 and T2.

**Verify (V-N.10):** with the app, through `Keypaste.AppDriver hold`, and then `keypaste agent` holding a vault, each of the seven verbs is refused naming the holder's process and the next step. With stdin closed it reports the holder rather than a missing password, and with the right answers piped the vault's bytes and backup count are unchanged. The holder then answers an agent request instead of refusing it as `vault-changed`. With nothing holding the vault each verb saves as before. The rule test fails on a copy that adds a save through `Open`. A refusal shown only against a claim the test takes itself does not pass.

The row was corrected against the code before it was built (`8192987`):
- `import` already took the claim itself;
- the T2 intro counted six verbs where there are seven;
- a piped CLI prints no password prompt, so "no prompt on stderr" became the closed-stdin check above.

**Founder direction while building.** The founder asked that all three steps be built before anything more was tested. N.1b and N.12 were each verified as they finished; this step's code, its tests and its gate were written first and then run with the whole tree.

## What changed for users

- **Seven more verbs wait for the vault's owner.** `add`, `rm`, `access`, `env set`, `env rm`, `env pull` and `import` join `set`, `rotate`, `field rm`, `env tag`, `share` and `token`. While the desktop app or `keypaste agent` holds the vault, each is refused before it asks for any password. Before, each saved behind the holder, whose copy then refused agents as `vault-changed` until it was unlocked again.
- **The refusal says what to do.**
  - Under the app: `keypaste: this vault is already unlocked in the keypaste desktop app (process N). Make the change there, or run `keypaste lock` and try again.`
  - Under `keypaste agent`: `… in keypaste agent (process N). Run `keypaste lock` and try again.`
  - Under another saving command: `this vault is in use by a keypaste command (process N). Try again when it finishes.`
- **Scripts that saved while the app was unlocked now stop.** A script that ran `keypaste env set` with the app open now gets that refusal; N.11 is what would let the app approve such an edit instead.
- **`import --dry-run` still reads a held vault,** and saves nothing.
- **`access` no longer tells you to restart a running `keypaste agent`,** since it is refused while one holds the vault.

## Evidence

Local, Windows 10 Pro 19045, on `main` above `6dd2468` with the step's changes uncommitted.

**The rule test.** [SavingVerbSourceRulesTests](../../tests/Keypaste.Cli.Tests/SavingVerbSourceRulesTests.cs) reads every source file of `src/Keypaste.Cli` but `VaultSession.cs`.
- **How it reads.** Strings and comments are blanked first. It takes each `VaultSession.Open`, `OpenThen` and `OpenHeld` call's arguments, and adds the body of every method of the same file they call, repeatedly.
- **What it holds.** No `Open` or `OpenThen` call reaches `.Save()`, `.ChangeAccess(`, `.CreateAsync(` or `.RevokeAsync(`, and every `OpenHeld` call does, fifteen or more of them. It also finds an offender in `AddCommand.cs` with `OpenHeld` turned back into `Open`, and in `ListCommand.cs` with a `vault.Save()` added to its body.

**In process.** [HeldVaultRefusalTests](../../tests/Keypaste.Cli.Tests/HeldVaultRefusalTests.cs) holds the claim in the test process as the app and as `keypaste agent`. For each of the seven verbs it checks exit 2 with no prompt asked, unchanged bytes, and the holder's name and next step; a dry-run `import` still reads. `VaultSessionTests`, `SetVerbTests`, `RotateVerbTests` and `TokenVerbTests` hold the new wording.

**Across processes.** [verify-held-saves.sh](../../scripts/verify-held-saves.sh) runs the shipped `keypaste` and `keypaste-mcp` against two holders in turn:
- `Keypaste.AppDriver hold --approving-prompt`, then `keypaste agent`;
- under each, all seven verbs are refused with stdin closed and then with their passwords piped, naming the holder's process and the next step;
- a bridge's `request_credential` is then released and audited as granted, and no line of the audit log says `vault-changed`;
- `keypaste lock --vault` ends each hold;
- with nothing holding the vault, each verb saves.

It runs in the desktop profile and in `app.yml`. [verify-current-state.sh](../../scripts/verify-current-state.sh) had its CLI save behind the app refused. Its outside writer is now the CLI under another `KEYPASTE_HOME`, which does not see this home's claim (T-29).

**The command.**
- The filtered run of `SavingVerbSourceRulesTests`, `HeldVaultRefusalTests`, `VaultSessionTests`, `SetVerbTests`, `RotateVerbTests`, `TokenVerbTests`, `ImportVerbTests`, `AccessCommandTests`, `EnvPullTests` and `ShareVerbTests` passed 158 of 158.
- `bash scripts/verify.sh` then selected every profile but `compat`, with N.12 already committed:
  - workflows, scripts, backend and integration passed;
  - scripts included the release-matrix self-test that N.12's first run had failed;
  - desktop failed at the new gate.
- **What failed and why.** `keypaste lock` was refused by `Keypaste.AppDriver hold` as "cannot be locked from outside". The app itself passes `AppAuthority.RequestLock` at launch, and the driver's hold passed nothing. The driver now passes it, on the thread its hold runs on, printing `locked` as its own lock command does.
- **After the fix.** The gate passed. `--from desktop` then passed in 367 seconds: the desktop suite 840 of 843, with the renderer's 3 skipped; consistency 43 of 43; and all nine desktop gates, `verify-current-state.sh` and `verify-held-saves.sh` among them.

## Decisions

[D-0382](../../DECISIONS.md) records the rule. These bind only this step's code:

- **The claim is taken in one place.** `OpenHeld` and its generic form, which runs `import`'s source step between the claim and the vault's password, both go through `Held`. `HeldRefusal` words the refusal by the holder's kind and keeps the claim's own sentence when nothing is known of it.
- **The driver's hold can be locked from outside, as the app can.** `Keypaste.AppDriver hold` passes `AppAuthority.RequestLock` as launch does, so a gate's `keypaste lock` reaches it.
- **`VaultClaim`'s wording is unchanged,** so the app's and the agent's refusal of a second owner, and the gates that read it, say what they said.
- **The rule test is a text scan within one file.** A save moved into another class would pass it; the verbs keep their writes in their own files.

## Limits and follow-ups

- **A terminal edit while the app is unlocked is refused, not approved.** N.11 is that.
- **Another `KEYPASTE_HOME`, or KeePassXC, can still save behind the holder,** which then refuses agents as `vault-changed` until it is unlocked again (T-29).
