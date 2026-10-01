# F.37 — Relay a SIGTERM that arrives as `keypaste run` starts its child

Completed 2026-10-01 on `task/f36-f37` above `ccb7cc1`, source and gate; dev runs 36864707313 (the probe) at `a8f4f64`, 36866639128 at `2402946`, 36868428946 at `8f1e1b7`, 36874890298 at `58fcc30` and 36881094776 at `ae0ae64`, and the controls 36866677010, 36866743619 and 36874924816; the integrated commit adds the second review's changes; final dev run 36890422000 at `c2c5d9a`, whose first Windows attempt failed only main's `SaveTimingTests` check that F.34 replaces; ci 36995550842 and app 36995553700 passed at `0319b47` on `integrate2`, with dev 36995556633 (all lanes and gates on all three runners). This record also closes F.36, the diagnosis.

## Amendments

- F.36 and F.37 close in one record, as F.30 closes F.39.
- The probe ran with `dev.sh --target cli --gates integration` instead of the auto selection. Auto run 36797154217 at `a8f4f64` selected the desktop process gates on macOS, which stopped at `verify-session-authority.sh:40` (`exec: {HOLD_IN}: not found`, F.40) before the integration gate; its Linux leg gave the same counts as below. The integration step, `verify.sh integration --prepare-only` then `--test-only`, is identical either way.
- F.37's Verify that F.36's variant pass in every iteration is met by the variant joining `verify-run-signals.sh`, 50 runs each time the gate runs.
- Review of `8f1e1b7` asked for four more changes, each made here: hold nothing on Windows, start nothing on a signal held before the start, dispose the traps if registering one throws, and test the relay deterministically in `Keypaste.Cli.Tests`, since the gate's variant cannot fail on Linux. Review of `ae0ae64` asked that a signal held as a start fails also stop the run, and that a stopped run say so on stderr.

## Cause (F.36)

`SystemProcessLauncher.Run` started the child, then registered its four traps. A SIGTERM in between took the runtime's default: keypaste died of it and the child never got it.

The probe (`scripts/probe-run-signals.sh` and timing in the launcher, both at `a8f4f64` and deleted here) ran inside the integration gate before `verify-run-signals.sh`. It repeated the gate 50 times, then 30 times a variant whose child sets a TERM trap and signals keypaste as its first act, with the traps after the start (as shipped) or before it, each with no delay or 500 ms between the start and the traps (or the attach). Run 36864707313 at `a8f4f64`, green in 869 s:

| Arm | `ubuntu-24.04` | `macos-15` |
|---|---|---|
| Gate as it stands | 50 of 50 passed | 50 of 50 passed |
| Variant, traps after the start | 30 relayed | 28 relayed, 2 keypaste died (143) |
| Same, 500 ms before the traps | 30 keypaste died | 30 keypaste died |
| Variant, traps before the start | 30 relayed | 30 relayed |
| Same, 500 ms before the attach | 30 relayed, each held then relayed | 30 relayed, each held then relayed |

Over the gate's 100 runs per platform, `Process.Start` took 2.32–2.85 ms (median 2.48) on Linux and 12.85–84.32 ms (median 25.25) on macOS, and the traps 0.46–0.71 ms and 0.29–3.96 ms after it returned.

Every death left the signature of dev run 36734081054: bash reported keypaste `Terminated` (`Terminated: 15` on macOS), the child never received SIGTERM, and keypaste wrote no timing line, which it wrote straight after the traps, so it died before they existed. A registered trap cancels SIGTERM, so a SIGTERM that kills keypaste after the child exists can only have arrived before the traps. F.36's criterion holds on both platforms: the delay made the variant fail in every iteration, and traps set before the start passed it in every iteration with and without the delay.

The observed failure is that window reached by the gate's own SIGTERM. On Linux the traps came about half a millisecond after a 2.5 ms start and the variant never lost. On macOS the start is ten times slower and varies by a factor of six, and the variant lost 2 of 30; in run 36734081054 keypaste was still short of its traps after the child had printed READY and the gate's poll had seen it. The probe does not split `Process.Start`'s time at the child's exec, so how much of a macOS stall falls after it is not measured.

## Repair (F.37)

[SignalRelay](../../src/Keypaste.Cli/Execution/SignalRelay.cs) registers the four traps before `Process.Start` and is disposed once the child is reaped; if registering one throws, those already made are disposed. On Unix, until `Attach` gives it the child, a signal `SignalPolicy` relays is held and cancelled. `SystemProcessLauncher` checks for a held signal just before the start: if there is one it starts nothing and returns the new `ChildOutcome.Interrupted` with 128 plus the signal's number, which `run` and `run --token` exit with after printing `keypaste run: received SIGTERM, so nothing was started` (the signal's name, never a variable), so a SIGTERM before the start still ends the run with 143. One held after that check is relayed on attach, or stops the run the same way if the start then fails. A signal the terminal delivered is never cancelled before the attach and ends keypaste, as before, and on Windows, where `NativeSignals` cannot deliver a held signal, none is held or cancelled before the attach, as on `main`. By .NET 10's `pal_signal.c` and `pal_process.c`, the runtime installs no handler over a disposition inherited as ignored, so a signal ignored under `nohup` stays ignored in the child, and its fork resets handled signals to default before the exec, so the child starts with the dispositions it had before.

[verify-run-signals.sh](../../scripts/verify-run-signals.sh) gains the variant, 50 runs, each requiring the child's 42. Its first case now checks the status before the child's output, because a keypaste without `context.Cancel = true` relays before it dies and the child's line may not be written yet.

[SignalRelayTests](../../tests/Keypaste.Cli.Tests/SignalRelayTests.cs) hold the same contract without a race, calling the trap's handler directly. A SIGTERM before the attach is cancelled and kills a `sleep` once attached (143); a terminal SIGINT is neither cancelled nor held; on Windows none of the four is; and a SIGTERM held before the start returns `Interrupted` with 143 and leaves `/usr/bin/touch` unstarted, where the same start without it creates the file; a SIGTERM handled inside a start that then throws not-found or not-executable, or returns no process, returns `Interrupted` with 143 where each failure alone returns its own outcome. A `RunCommandTests` and a `RunTokenTests` case check that `run` and `run --bundle` exit with an interrupted run's code, print that line and no value.

## Evidence

| Commit, command | Run | Result |
|---|---|---|
| `2402946`, the repair, `--target cli --gates integration --os linux+macos` | 36866639128 | Green: `Keypaste.Cli.Tests` 790 with none failed on each (786 and 787 passed, the rest skipped); the signal gate passed on both, the variant 50 of 50 in 20 s on Linux and 41 s on macOS; every integration gate passed on both. |
| `e8e8dfc`, the launcher as on `main` and `SignalRelay` removed, gate kept, `--os macos` | 36866677010 | Failed as expected: run 1 of 50 of the variant got 143, keypaste `Terminated: 15`. |
| `860b05f`, the repair without `context.Cancel = true`, `--os linux` | 36866743619 | Failed as expected: `expected the child's 42, got 143`. |
| `8f1e1b7`, `--target core,cli,consistency --gates both --os all` | 36868428946 | Green on all three: core, CLI and consistency tests, integration and all eleven compat gates; the variant 50 of 50 on Linux and macOS. |
| `58fcc30`, the review's changes, `--target core,cli,consistency --gates integration --os all` | 36874890298 | Green on all three: core, CLI (795, five new) and consistency tests and every integration gate; the relay tests ran three cases on Linux and macOS and two on Windows, each skipping the rest by platform; the variant 50 of 50 on Linux and macOS. |
| `ae0ae64`, the first review's changes squashed, `--target core,cli,consistency --gates integration --os all` | 36881094776 | Green on all three, with 36874890298's counts. |
| `4a329e9`, `58fcc30` with nothing trapped before the attach and no check before the start, `--class Keypaste.Cli.Tests.SignalRelayTests --os linux+macos` | 36874924816 | Failed as expected, the same on both: of the four relay tests, the SIGTERM before the attach was not cancelled, and the SIGTERM held before the start returned `Exited` 0 after `touch` ran; the terminal SIGINT case passed and the Windows case skipped. |

## Decisions

D-0394: the traps precede the child; on Unix what arrives first is held, relayed once the child starts and otherwise ending the run unstarted; it supersedes D-0016's traps set once the child had started.

## Limits and follow-ups

- On Linux the gate's variant passed 30 of 30 without the repair; `SignalRelayTests` is the deterministic regression there, and macOS's gate also caught the old order at its first run in 36866677010.
- A signal that arrives between the check and the fork starts the child and is relayed to it. A signal held before the attach can reach the child after one that arrived just after the attach.
- Windows still has no signal verifier (SECURITY.md).
- No new row.
