# F.21 — End the session when the main window closes with a prompt open

Completed 2026-09-24 on `main` above `272a517`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

Observed in [F.2b3a](F.2b3a.md) on Windows 10 at `15b2c47`: with a credential request waiting in the prompt window, closing the main window with its close button left the process running with the prompt as its only window and the vault unlocked and served. The request ended `timed-out` rather than `vault-locked`, and in a repeat Approve on that prompt released the value to the client. Build: closing the main window quits whatever else is open: the authority ends, a waiting request is denied as `vault-locked`, every prompt window is taken down and the process exits. A regression composes the app with a prompt waiting, closes the main window and asserts the `vault-locked` reply and that no window remains. Verify: the regression fails at `15b2c47` and passes after the repair in `app.yml` on all three operating systems.

Amendment, on the founder's direction on 2026-09-24: `app.yml` runs the desktop tests on `ubuntu-24.04` only, and `ci.yml`'s three-OS job builds `keypaste.slnx`, which holds no app tests, so no workflow runs them on Windows or macOS. The Verify became `app.yml` on Linux plus a local Windows run. macOS was not run.

## What changed for users

Closing the desktop's main window now quits the app even while an agent's request waits in its prompt window. The request is refused as `vault-locked`, the prompt closes and the process exits. Before, the prompt kept the process running with the vault unlocked and served, and Approve on it still released the value.

## Evidence

**Mechanism.** Avalonia's `ClassicDesktopStyleApplicationLifetime` defaults to `ShutdownMode.OnLastWindowClose`. With `WindowApprovalChannel`'s prompt open, closing `MainWindow` was not the last close, so the lifetime raised no `ShutdownRequested` and `App.OnShutdownRequested`, which ends the authority, never ran. Launch now sets `ShutdownMode.OnMainWindowClose`, whose shutdown closes the remaining unowned windows without letting them cancel.

**Regression.** [ClosingTheMainWindowTests](../../tests/Keypaste.App.Tests/Session/ClosingTheMainWindowTests.cs) composes the app through `App.Launch`, the method launch now calls, into a real `ClassicDesktopStyleApplicationLifetime`. It unlocks a vault, sends a credential request over the app's endpoint, waits for the prompt window and closes the main window. It asserts that the app was asked to quit, that the reply is `denied` with `vault-locked` and no value, and that neither window remains open or in the lifetime's list.

| Run | Source | Result |
|---|---|---|
| Local, Windows 10 Pro 22H2 19045.6332 | the fix with `ShutdownMode` removed | failed: "closing the main window did not ask the app to quit" |
| Local, Windows 10 Pro 22H2 19045.6332 | the fix | passed |
| `app.yml` run 36080707226, `ubuntu-24.04`, `bash scripts/verify.sh desktop` | `8fde202` on `f21` | passed: 532 of 533 desktop tests, 1 skipped as before, against 531 of 532 in app run 36044104913 at `15b2c47`; 41 of 41 consistency tests |

## Decisions

D-0343: closing the main window quits the app whatever other window is open.

`App.Launch` holds what `OnFrameworkInitializationCompleted` composed, so a test runs launch's composition; `App.Authority` exposes the authority it built. `OnShutdownRequested` calls `Shutdown` on the lifetime `Launch` was given rather than on `ApplicationLifetime`.

## Limits and follow-ups

- **How the test differs from a launch.** Avalonia attaches a lifetime's window tracking only in `StartWithClassicDesktopLifetime`, so the test calls its internal `SubscribeGlobalEvents` by reflection, and fails naming it if Avalonia removes it. A completed shutdown ends the dispatcher every test in the assembly shares, so the test cancels the shutdown after the app has answered it. For the same reason the session is unlocked directly rather than through the unlock screen: with no shell showing, quitting ends the authority at once, while with a shell it first clears the clipboard and then calls `Shutdown`, which cannot be cancelled. That path's ordering is unchanged and untested here.
- **Platforms.** macOS was not run. Repeating the close on a real desktop after the repair is [F.2b3b](../STEPS.md)'s.
