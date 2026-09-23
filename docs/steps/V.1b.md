# V.1b — Manage vault access from the desktop

Completed 2026-09-22 on `e5d739c`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Build:** desktop controls that choose an existing keyfile when unlocking and creating, and change the master password or keyfile through V.1a's core operation behind a confirmation that says what the change costs the backups already taken. The unlock screen's restore prompt accepts the keyfile a backup was made under. No hosted recovery claim.

**Verify (V-V.1b):** through the app change a fixture's password and its keyfile, lock, reopen with the new secret and be refused with the old. Restore a backup made under the earlier password and keyfile from the unlock screen and land in it. A wrong current secret, a cancelled confirmation and a failed save leave the vault byte-identical. Each new master-password field holds to D-0099. A view model asserting over a mocked change does not pass.

The founder decided two questions during planning on 2026-09-22. D-0285 had left the first to this step: the desktop remembers each vault's keyfile location, as KeePassXC does (D-0295). The second follows KeePassXC too: the app stays unlocked after an access change (D-0296).

## What changed for users

The desktop opens a vault that needs a keyfile. Keyfile… under the password chooses one, and all four KeePass forms work, including a vault a keyfile alone opens with the password left empty. A file that is missing, empty, unreadable, a vault, or an XML keyfile this build cannot read is refused the moment it is chosen, before any password is spent on it. A refusal after that names both factors when a keyfile was given. A vault keyed to an ordinary file opens with a notice in the app that editing that file loses the vault. The app remembers where the keyfile each vault opened with is, beside the vault's path in `recent.toml`, and offers it at the next unlock. No keyfile stops using it for one unlock. Nothing records the keyfile's contents, and the CLI still records nothing.

Create takes an optional keyfile you already have and makes a vault that needs the password and the keyfile. keypaste still makes no keyfile and no vault a keyfile alone opens, and it refuses an ordinary file that would be keyed by its bytes, writing nothing.

Settings has a Master password and keyfile section. It states what the vault opens with now. It asks for the current master password even though the vault is open, and checks it against the file on disk before anything is written. It offers a new password, adding or replacing a keyfile, and stopping using the keyfile, alone or together. Change… shows what the vault will open with afterwards and what the change costs: the vault as it was is kept in `<vault>.backups`, and that copy and every earlier one still open with the old password and keyfile, so delete them if those were exposed. Attaching a keyfile adds a reminder to keep a copy of it away from the vault. After the change the app stays where it was, on the changed vault, which it has reopened under the new factors. The next unlock needs the new ones, and the recent list offers the new keyfile. Each of these leaves the vault file and its backups untouched: a wrong current password, Leave access as it is, mismatched new passwords, a refused keyfile, a vault changed on disk since it was unlocked, and a save that fails. Every password typed into the form is cleared on every outcome, on cancel and on lock.

The restore panel on the unlock screen takes the keyfile a backup was made under. It starts with the one the unlock screen had chosen, and it can be changed or dropped. A restored vault opens with that keyfile, and the app remembers it.

There are now seven master-password fields: the three new ones in Settings join unlock, create's pair and the restore panel's. None of them takes a paste.

## Evidence

Tests, all passing on Windows 10 at the source above:

- Core [creation](../../tests/Keypaste.Core.Tests/VaultCreationTests.cs) has 5 new cases: a keyfile vault reopens only with both factors, and a missing, empty, hashed or self keyfile is refused with the parent directory never created. [Recent vaults](../../tests/Keypaste.Core.Tests/RecentVaultsTests.cs) has 2: the keyfile round trip and its replacement, and a malformed key costing only the keyfile.
- [Access journeys](../../tests/Keypaste.App.Tests/ViewModels/VaultAccessTests.cs) has 9 tests, each driven through `SettingsViewModel` over a real vault and read back through `Vault.Open` or the unlock screen:
  - A password change keeps the session open. A later save succeeds, and after a lock the old password is refused and the new one opens.
  - A keyfile added, replaced and removed is each time what the next unlock needs, and the recent list follows it.
  - A backup made under the password and keyfile between two changes is restored from the unlock screen and lands in the app.
  - A wrong current password, a cancelled confirmation, mismatched passwords, a hashed keyfile (refused at the picker and by the session's route to the core), a vault changed on disk, and a failed save leave the vault's digest and backup list unchanged. The failed save is a file where `<vault>.backups` must go.
- [Unlock and create](../../tests/Keypaste.App.Tests/ViewModels/UnlockKeyfileTests.cs) has 9 tests: both factors and the remembered keyfile, keyfile alone, wrong keyfile wording, refusals at the picker, a remembered keyfile that has gone, the hashed-file notice, create with a keyfile, a refused hashed keyfile on create writing nothing, and create not inheriting the selected vault's keyfile.
- [D-0099](../../tests/Keypaste.App.Tests/Controls/VaultAccessAutomationTests.cs) has 10 cases on the rendered Settings view. Each of the three fields has a length-not-characters differential, a mask-and-placeholder anti-vacuity guard and a refused paste, and a whole-window sweep runs with all three filled.
- The existing unlock, restore, settings, export, hygiene, session and D-0099 classes still pass: 124.

Dropping the current-password check in `AppVaultSession.ChangeAccess` fails `A_wrong_current_password_changes_nothing`.

Verification: `./scripts/verify.ps1` on the finished tree passed workflows, scripts, backend and integration, and failed desktop on `MinimizeLockTests`, which found the Settings checkbox by being the only one; it now finds it by its label, and `--from desktop` passed. `compat` was not run. No KeePassXC run was made for this step. The re-key and keyfile writers are V.1a's, whose gates cover them, and a keyfile vault the desktop creates goes through `Vault.CreateWith`, the same writer those fixtures use.

## Decisions

Ledger rows from this step, which constrain later work, stay in [DECISIONS](../../DECISIONS.md): D-0295, D-0296, D-0297. The rows below bind only this step's code and remain in force unless a later decision supersedes them.

| id | date | decision | supersedes |
|---|---|---|---|
| D-0298 | 2026-09-22 | The Settings access form has no idle expiry of its own: it lives in the unlocked shell, so idleness locks the session and the lock disposes it, zeroing its three buffers; the restore panel keeps its expiry because the locked screen has no lock to rely on | — |
| D-0299 | 2026-09-22 | Starting a create clears the keyfile the selected vault would use and cancelling brings the remembered one back, so a vault's keyfile is never carried silently into a new vault; the unlock screen, the create form and the restore panel each say what they will use | — |

## Limits and follow-ups

`keypaste init` still takes no keyfile, so the CLI makes a password vault and attaches a keyfile with `keypaste access`. A keyfile-only vault's access change is guarded by possession of the keyfile alone, because there is no password to ask for. If the session cannot reopen the vault under the new factors after a committed change, it locks with its own reason and the unlock screen says why. That path is defensive and has no test, because the check of the current password refuses before a vanished keyfile could reach it. A running `keypaste agent` keeps the vault it opened until restarted, as after `keypaste access`. The drawn Settings section was exercised headless through Avalonia's test platform; no person has used it on a real desktop, and the guide's checks 42–45 record what to observe.
