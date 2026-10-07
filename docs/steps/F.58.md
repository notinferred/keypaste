# F.58 — Delete the closed timing probe and covered tests, and export only the vault chosen

Completed 2026-10-07 at `efb7826` on `task/f58`: the dispatched ci 37658308457 and app 37658322268 runs passed every job on all three runners in 642 s. The integrated commit differs from that tree only in this record and BACKLOG.

It had no STEPS row: it acts on a review of F.55 and F.56 and on the read-only test audit the founder asked for on 2026-10-07, limited to findings whose deletion an independent check upheld, that lose no coverage and that lie off the secret, injection, sync and bridge paths (PRODUCT §4.5), each re-checked against main before deleting.

## What changed

- The pool-timeline instrument (`PoolSnapshot.cs` with `PoolWatch`, `PoolLiveness`, `PoolTimeline` and `PoolCounters`, and `PoolTimelineGuardTests`) is gone with its call sites, `SaveTimings.WriteOntoTheTimeline` and the `Microsoft.Diagnostics.NETCore.Client` package it alone used. F.9 added it to instrument the thread pool before repairing it; that repair is long closed, and its guide went with `docs/diagnostics.md` in the process trim. The tests that carried it keep their assertions, and `PoolStarver` and `PoolShortageTests` stay.
- The hidden `hello` verb, `CoreInfo.Hello()` and their two tests are gone.
- Thirteen groups of tests that another test already proves were deleted or folded into the test that covers them, across Core.Tests, Cli.Tests, Consistency.Tests, App.Tests and Mcp.Tests: mirrored setup and spelling cases, help-text substring checks, a test that the compatibility gate proves `RestoredToRoot`, and duplicated front-end round trips.
- Settings' export copies only the vault it was started on. F.55 moved it onto `AppVaultSession.Write`, which reads whichever vault is open when the picker returns, so a vault locked while the picker was open and another unlocked before it closed would have been copied under the first one's name; before F.55 nothing was written in that case, and that is restored.

## Evidence

- 37 files, 1,298 lines removed and 34 added, all under `tests/` but for the `hello` verb, the package pin and the lock files.
- `SaveTimingTests` keeps `SaveClock`: the F.10, F.12 and F.34 tests read it, so the audit's finding to delete it no longer held.

- A review of `6322ca5` through four lenses (notifications, the write path, Core rules and reserved groups, the owner, `AtomicFile` and the generated encoder), with a skeptic per finding, confirmed the export defect above and refuted four: a recorded wording change, the reference export now following the CLI's owner-only write, and a cache race described below. `VaultExportTests.A_vault_unlocked_while_the_picker_was_open_is_not_copied_in_its_place` holds the fix.

- A last review of keypaste against the founder's two engineering references (architecture invariants and engineering concepts), four readers and a skeptic, confirmed eleven gaps among fifteen. Three small ones went to F.59: settings files written in place, the unlock screen's stale Recent list, and the policy hourly cap counted on the wall clock. The other eight are BACKLOG investigation candidates.

## Limits and follow-ups

- The test jobs' time on Windows and macOS was measured from today's runs and earlier serial dev runs: each of Core.Tests and Cli.Tests alone takes about half its time beside the other (Core 218 to 271 s alone on Windows against a 428 s median together), so the runner is saturated by real Argon2 derivations, not held by a lock. The split it suggests is a BACKLOG row, because it amends D-0423.
- The shell sets `Countdown` from the idle timer's thread, as before F.56; `DependsOn`'s cache of each property's dependents is filled on first raise without a lock, so that first raise can race a raise on the UI thread. The skeptic judged it almost never reachable, since the UI thread then rarely raises anything with dependents; it is not fixed.

- The audit's other findings (merges, rewrites, speed-ups and the gate-script duplicates the gates changed today) were not acted on; the audit lives outside the repository, and none is a defect.
