# F.15 — Make Open work from the locked screen

Completed 2026-09-23 on `00614af`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Build:** `Ctrl/Cmd+O` on the unlock screen invokes the vault picker. `App.OnShortcut` returns when the shell is null, so the locked screen has no binding at all today. A path the picker returns goes through the same offer the Browse button uses, including the restore-only path a missing or damaged vault takes, and the guide describes whatever is true afterwards rather than the current limitation.

**Verify (V-F.15):** a key event delivered to the locked window reaches the picker and lands in the vault it returns; cancelling leaves the screen, the recent list and every file untouched; the desktop guide's description of the binding matches the code. A view model holding a command nothing invokes does not pass.

No amendment.

## What changed for users

On the unlock screen, `Ctrl+O` (`Cmd+O` on macOS) opens the same picker as Browse. A vault it returns is selected ready for its password, a file that is not a vault is refused as Browse refuses it, and a damaged vault with backups beside it is selected for restoring. Cancelling the picker changes nothing. The shortcut does nothing while the create or restore form is showing, while an unlock is running, or once a vault is open. The desktop guide's Opening a vault section and keyboard table say so.

## Evidence

The window's chords moved out of `App` into [Shortcuts](../../src/Keypaste.App/Shortcuts.cs), armed by `App.Bind`, which launch calls, as `App.Watch` and `App.Observe` already are. `Ctrl/Cmd+L` and `Ctrl/Cmd+1`…`6` are unchanged. `BrowseCommand` is now off wherever the Browse button is hidden, and its enabled state is re-raised on busy, create and restore transitions, which it was not for busy before.

[Shortcut tests](../../tests/Keypaste.App.Tests/Views/UnlockOpenShortcutTests.cs), 5, each sending the chord through the headless window's keyboard to the real `UnlockView` with `App.Bind` armed:

- The chord reaches the picker once and selects the returned vault, and the password then opens the session on that path.
- A cancelled picker is reached once, and the selection, message, `recent.toml`, the vault's bytes and the directory listing are unchanged.
- A vault damaged after a real save left a backup is selected restore-only with the restore offer and its message.
- With the create form open the picker is never reached.
- A plain `o` goes into the password field and does not open the picker.

Mutations, each restored afterwards: removing the `O` branch fails the first three; dropping the visibility condition from `BrowseCommand` fails the create-form test. The shortcut class with the `ActivityWatch`, `MinimizeLock`, `StartupSettings`, `UnlockCreate`, `RestoreBackup` and `UnlockFocus` classes pass together: 63.

Verification: `./scripts/verify.ps1` on the finished tree ran workflows, scripts and desktop and passed all three; desktop ran 410 app tests and 40 consistency tests, none failing. Backend and integration were skipped because no changed path maps to them, and `compat` was not run because this step writes no vault content.

## Decisions

None. The binding is a choice with no future cost.

## Limits and follow-ups

The key event is raised through Avalonia's headless keyboard, not typed on a native desktop; the guide's keyboard-only checks remain the native observation. A missing vault still cannot be chosen through a picker, since native pickers return existing files, so it stays reachable only from the recent list as FEATURES says. The shortcut does nothing in the unlocked shell, where opening another vault is not offered.
