# B.2 — Keep one copy of each shared test helper

Completed 2026-09-30 on `main` above `a432ade`, source only; dev runs 36727300997, 36728844369, 36734380617, 36736924521 and 36738721808.

## Amendments

None.

## Evidence

- **Tested source.** The commit that lands this record carries the `src`, `tests`, `scripts` and `.github` trees of `d5ea3f3` and `a301109`; only documents changed after them. Dev run 36736924521 ran `auto` on `d5ea3f3` on all three runners: green on Linux, including the scripts and workflows checks; on macOS only F.28's four cases failed; on Windows one Core.Tests timing case failed (F.34), so the desktop projects never ran there. Dev run 36738721808 repeated `auto` on Windows at `a301109` and was green, the desktop projects included. Dev runs 36727300997 at `97d5284`, 36728844369 at `9fae2b3` and 36734380617 at `d8f2f94` tested the same `src`, `tests` and `scripts`; `d5ea3f3` only adds three trigger paths to `app.yml`.
- **One source each.** `FakeShareServer` and `ManualClock` live in Core.Tests, `IsolatedHome` in App.Tests and `PoolTimelineGuardTests` in Core.Tests. Each is compiled into its other projects through `<Compile … Link=…>`, keeping its own namespace the way `PoolSnapshot` and `PublisherMetadata` do. The five deleted copies differed from the kept ones only in namespace, in doc comments, in `IsolatedHome`'s prefix, and in `ManualClock`'s default start and its missing `RewindWallOnly`.
- **Nothing a test asserts changed.** App.Tests used to default its clock to 2026-07-28 09:12:44 UTC, while Core.Tests defaults to 2026-07-26 14:03:11. The 189 `new ManualClock()` calls in App.Tests that relied on that default, plus the ten `ManualClock` fields and properties initialised with `new()`, now pass `AppClock.Start` explicitly, so every App test and rendered screen starts at the same instant as before.
- **Test counts.** Before is dev run 36726742125 at the base `a432ade`, except where a row names another run. After is dev runs 36727300997, 36728844369 and 36734380617; 36736924521 and 36738721808 repeated every total they reached. Totals include skips.

| Suite | Before | After |
|---|---|---|
| Core.Tests, Linux | 2227, 14 skipped | 2227, 14 skipped |
| Cli.Tests, Linux | 758, 4 skipped | 758, 4 skipped |
| Mcp.Tests, Linux | 132 | 132 |
| Core, Cli and Mcp together, macOS | 3117, 17 skipped | 3117, 17 skipped |
| Core, Cli and Mcp together, Windows | 3119 in ci 36646739875 at `3e29da7` | 3119, 11 skipped |
| App.Tests, Linux | 843, 1 failed (F.33) | 843, 4 skipped, none failed |
| App.Tests, macOS | 843, 4 failed (F.28) | 843, the same 4 failed |
| App.Tests, Windows | not run: cancelled | 843, 3 skipped |
| Consistency.Tests | 43 on Linux in app 36646739852 at `3e29da7` | 43 on all three runners |

- **Change map.** `verify-ci-scope.sh` passes 24 cases, locally and in the broad runs' scripts check. The new case, "the share server fake", holds a change to `tests/Keypaste.Core.Tests/FakeShareServer.cs` to the core, cli and desktop lanes: Core.Tests and Cli.Tests on three runners, and App.Tests. The planner already selected `ManualClock` for core and desktop, `IsolatedHome` for App.Tests and Consistency.Tests, and `PoolTimelineGuardTests` for core and mcp, the same closures the existing `SoftwareYubiKey`, clipboard-fake and pool-reporter cases hold.
- **Push to `main`.** App.Tests runs only in `app.yml`, whose push paths now list the three Core.Tests files App.Tests compiles, `ManualClock`, `FakeShareServer` and `SoftwareYubiKey`, beside `PublisherMetadata`. Without the first two, moving the app's copies out of `tests/Keypaste.App.Tests/` would have let a push changing only the shared file skip App.Tests; `SoftwareYubiKey` had been missing since `a023446` linked it.

## Decisions

None.

## Limits and follow-ups

- In Mcp.Tests, the linked guard test's fully qualified name is now `Keypaste.Core.Tests.PoolTimelineGuardTests`, so `dev.sh --class` with that name selects Core.Tests only. Mcp.Tests still runs the guard whenever the whole project runs.
- `WorkflowRulesTests` checks that `app.yml`'s push paths cover every project folder and linked file that the projects `app.yml` builds compile, so a helper linked without its path fails ci's backend jobs; the five Core.Tests helpers App.Tests had linked without a path are now listed (gaps-1c, RUNS-PENDING).
- **F.28.** On macOS at the base commit, four `EnvLaunchThroughAppTests` cases failed: the screen refused first because the app then opened terminals only on Windows and Linux, while these cases expected the confirmation or the set's own refusal. [F.28](F.28.md) held each to the refusal its platform gives, and since [F.52](F.52.md) app.yml's `desktop tests` job runs App.Tests on macos-15.
- **F.33.** On Linux at the base commit, `SessionOwnershipTests` once found the app's endpoint still accepting a connection after a lock. [F.35](F.35.md) repaired the cause it measured and closed F.33 with it.
- **F.34.** On Windows in dev run 36736924521, `SaveTimingTests.ATransactedSave_DoesNotQueueBehindAnotherSavesRetryWait` found a refused save attempt holding the save gate 3.2 s, longer than its retry wait; the same code passed there in 36728844369. [F.34](F.34.md) measured the product right and the test's bound wrong, and repaired the test.
- **No Windows baseline for App.Tests and Consistency.Tests.** The broad run's `all` dispatch shared the base run's concurrency group, so it cancelled the base run's Windows leg. Since [F.52](F.52.md) app.yml's `desktop tests` job runs both on windows-2025 and macos-15 as on Linux. The backend total on Windows, 3119, differs from Linux's 3117, so totals are compared per platform.
