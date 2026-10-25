# P.1a — Import a YubiKey vault in the app, and reach one from the CLI through the app

Completed 2026-10-28 on `t6`, source only; runs pending.

## Amendments

P.1's row was out of date: creating a vault that needs a YubiKey (`VaultCreation`, `AppVaultSession.TryCreate`, the create form) and D-0514's refusals of the standalone verbs and `keypaste approve` were already in source at `478b491a`. What code could still do was the app's KDBX import taking the key and a test that the CLI reaches such a vault through the app. P.1 is split as CLAUDE.md splits oversized work: this step, and the Human row P.1b (H-0026) for the checks only a physical key can make. F.53's limit, the key asked twice on `windows-2025`, was folded in. The founder decided on 2026-10-25 that hand checks no longer gate 1.0.0, so ROADMAP's 1.0.0 row names P.1a, and P.1b stays open as a follow-up outside 1.0.0's gates.

## What changed

- **Core.** `KdbxImport.Open` takes the hardware key a file also needs, through `KeePassInterop.OpenReadOnly`, and an `ImportSource` opened with one names `YubiKey` among its key factors. `keypaste import` passes none, since the CLI cannot ask a key (D-0366).
- **The import dialog.** Where the app reaches YubiKeys, a line under the key file offers Use a YubiKey, slot 2 unless Use slot 1 is chosen, and Remove. The key is the dialog's own, made over the session's devices but not through `AppVaultSession.NewHardwareKey`, so its touch shows in the dialog, as an amber waiting dot, Touch your YubiKey and Cancel with Unlock secondary, and not as the shell's dialog for a save waiting on the key. It is asked once, by the open, and disposed after it, since an import source is never saved; the dialog's Cancel and a lock stop the wait. A refusal names the password and the factors that went with it, and a missing key, more than one, an empty slot, a touch that never came and a cancel are each said, naming the file where the unlock screen names its vault. Keeping the file in place hands its slot to the unlock screen with its keyfile (`UnlockViewModel.Offer(path, keyfile, slot)`).
- **F.53.** The mechanism, read from the code: an access change, like every save, serialises the vault inside each write attempt, and serialising draws a new KDF seed, which is the key's challenge. When Windows refuses an attempt's move for a moment, as Transactional NTFS does (D-0119, D-0122), the save retries, serialises again and asks the key again. Nothing else asks: verifying the bytes and `ConfirmOnDisk` reuse the attempt's seed and the key's held answer. So the run that failed made two attempts. F.53 decided on no product change, and D-0554 records the behaviour; `HardwareKey`'s remarks, which said a slot is touched once per save, now say once per attempt.

## Evidence

Nothing was built or run on this machine but the compile check, which passed. Each class this step adds or changes passed in `dev.yml`, the first two at `a6a4768e` and the rest at `71533f1b`, which differ from the step's commit only in its records; the batch's full ci and app runs are pending:

| dev run | Class | Runner | Tests |
|---|---|---|---|
| 40193149893 | `Keypaste.Core.Tests.HardwareKeys.HardwareKeyVaultTests` | `ubuntu-24.04` | 19 passed |
| 40193157583 | `Keypaste.Core.Tests.HardwareKeys.HardwareKeyVaultTests` | `windows-2025` | 18 passed, 1 skipped (the landing save, as before) |
| 40743129051 | `Keypaste.App.Tests.ViewModels.KdbxImportHardwareKeyTests` | `ubuntu-24.04` | 7 passed |
| 40743148145 | `Keypaste.Cli.Tests.HardwareKeyVaultHeldByTheAppTests` | `ubuntu-24.04` | 2 passed |
| 40743160454 | `Keypaste.App.Tests.ViewModels.KdbxImportViewModelTests` | `ubuntu-24.04` | 12 passed |
| 40743176510 | `Keypaste.App.Tests.ViewModels.ShellImportTests` | `ubuntu-24.04` | 10 passed |
| 40743191664 | `Keypaste.App.Tests.Views.ImportDialogKeyboardTests` | `ubuntu-24.04` | 7 passed |
| 40743207808 | `Keypaste.App.Tests.Session.PlainWordsTests` | `ubuntu-24.04` | 3 passed |

What each shows:

- `HardwareKeyVaultTests.A_write_refused_after_the_key_answered_asks_it_again_on_the_retry` makes ci 37627277189's failure happen every run. A throw from `ChangeAccess`'s `duringAttempt` hook, which F.53's limit proposed, lands before the attempt serialises the vault and would ask nothing, so the refusal is staged where the write meets it: off Windows a directory at `<vault>.tmp`, the write's temporary file, and on Windows another transaction holding the vault's name, F.7's `HeldTransactedName`. Lifted when the second attempt begins, the change commits; the first attempt had asked the key before its refusal and the second asked it again with another challenge, and the vault opens with the key and not without. It ran on Windows, not skipped. `Adding_a_hardware_key_makes_it_required_and_asks_it_once_per_attempt` counts the attempts through the hook and asserts as many challenges. `A_file_imported_from_opens_only_with_its_hardware_key_asked_once_and_names_it` holds the Core change.
- `KdbxImportHardwareKeyTests` drives the dialog over a real file a software key protects: the password alone refused, the key asked once and the item imported, another key's secret, an empty slot and a missing key each said, the touch prompt with Unlock secondary and its Cancel opening nothing while the shell's save dialog never showed, the dialog's Cancel ending the wait, the slot handed to the unlock screen, and no key offered where the session reaches none.
- `HardwareKeyVaultHeldByTheAppTests` holds a YubiKey vault, which `recent.toml` remembers the app opening with slot 2, under the app's claim and serves it through Core's owner with the vault open under the key: `keypaste set` reaches it with no master password asked, the holder's save asks the key and the file opens only with it, and `keypaste run --session` starts the child with the set; neither prints the refusal that names the app.
- `KdbxImportViewModelTests`, `ShellImportTests`, `ImportDialogKeyboardTests` and `PlainWordsTests` take the hand-off's new slot; the plain-words sweep now reads the dialog's YubiKey line.

The batch's full runs are to show the rest on the three runners: these classes on macOS and Windows, `KeePassInterop`'s other opens, `keypaste import` and the app's other import and unlock classes.

## Decisions

D-0554.

## Limits and follow-ups

- **No physical key.** Unlock, save, reopen and KeePassXC interoperability, each refusal and the spare-key and lost-key limits have been exercised only against a software key; P.1b is the person's check, outside 1.0.0's gates.
- **The CLI's holder in its tests is Core's owner holding the app's claim**, the authority, editor and environments the app composes, not the app's process; `HardwareKeySessionTests` shows the app's own session asking the key on every save.
- **A touch-required key is touched again** when Windows refuses a write for a moment and the save retries (D-0554).
- `keypaste import` cannot open a file that needs a key (D-0366); the app's dialog is the way in.
- The dialog does not preselect a slot `recent.toml` remembers for the file, as the unlock screen does; it starts with no key.
- Source only.
