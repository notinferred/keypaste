# F.51 — Find why the app driver crashed after a refused unlock on macOS

Completed 2026-10-06 at `136bf9e` on `task/f51`, test driver only. Runs: ci 37438294611 and app 37438298130, dispatched so every job ran, and dev 37433423880 on all three runners with the integration and KeePassXC gates.

## Amendments

The row asked for a regression kept in the tree that fails as the observation did. The failure is a race inside Avalonia that no test can force, and after the repair the driver no longer calls the code that loses it, so the regression is the probe below, run and then deleted as a discovery reader is (CLAUDE.md).

## Evidence

**Observation.** Dev run 37397846023, `test (macos-15)`, `verify-session-lifecycle.sh`: `Keypaste.AppDriver hold` printed the refusal, the owner line and `status held by keypaste agent`, then `driver failed: NullReferenceException` and exit 3. Everything after the status line is teardown: the unlock screen, the Connect section, the authority, then the prompt screen.

**Site.** Reading those disposals left one dereference that can be null. In Avalonia 12.1.0, `HeadlessUnitTestSession.StartNew` builds the session inside `Task.Run` and passes it `task!`, the variable `task = Task.Run(...)` assigns only once `Task.Run` returns. When the pool thread reaches `SetResult` before the caller stores that variable, the session's `_dispatchTask` is null. Only `Dispose` and `DisposeAsync` read it, so the session works until `PromptScreen.Dispose` disposes it and `_dispatchTask.Wait()` throws `NullReferenceException` unwrapped, as observed. The same code is in 12.1.3 and on Avalonia's main branch.

**Probe.** `HeadlessStartRaceProbe` at `c2793aa` called `StartNew` then `Dispose` for three minutes on each runner, with twice as many spinning threads as cores, and counted sessions with a null `_dispatchTask` and `Dispose` calls that threw (dev 37405690909; 37405453986 failed its format check and measured nothing):

| Runner | Cores | Starts | Null dispatch task | `Dispose` threw |
|---|---|---|---|---|
| `ubuntu-24.04` | 4 | 775 | 1 | 1, at `Avalonia.Headless.HeadlessUnitTestSession.Dispose()` |
| `macos-15` | 3 | 304 | 0 | 0 |
| `windows-2025` | 4 | 64 | 0 | 0 |

The race is reachable on a hosted runner and throws where the macOS run failed. The macOS observation printed no stack, so it is attributed by the matching exception and the absence of any other null on its path.

**Repair.** The prompt screen's display is one static session for the process, as `HeadlessSession.Instance` already is in the app tests, and is never disposed: a second session cannot start in a process anyway, because Avalonia registers the `avares` URI parser globally. Disposing a prompt screen ends its own dispatch, which closes the app it drew, and the display's loop thread ends with the process. The driver now prints the whole exception when it fails unexpectedly, so a later crash names its site.

Dev 37433423880 passed `verify-session-lifecycle.sh`, `verify-lock-boundary.sh`, `verify-session-authority.sh` and the other gates that run `hold` or answer the app's prompt, and the KeePassXC gates, on all three runners.

## Decisions

None. How the driver holds its display binds only the driver.

## Limits and follow-ups

- The race stays in Avalonia. The app tests start their session the same way and never dispose it, so they cannot hit it; the app itself does not use `HeadlessUnitTestSession`. Reporting it upstream is outside this step.
- macOS and Windows did not reproduce it in three minutes each; how often it happens there is not measured.
