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

None in the ledger. Binding only this step's code:

- `IsolatedHome` runs as a module initializer, which takes no arguments, so its temporary-directory prefix comes from the name of the assembly compiling it instead of a parameter. The prefixes stay `keypaste-app-tests-home-` and `keypaste-consistency-tests-home-`.
- A linked helper keeps the namespace of the project that owns it, and each consumer imports that namespace with a `using` directive; neither a shared namespace nor a global using was added.
- `AppClock.Start` in App.Tests names the app suite's starting instant. `ManualClock`'s own default stays the one the core tests were written against.
- `ManualClock`'s class comment is shortened to the three constraints it records, and no longer points at the deleted App.Tests copy.

## Limits and follow-ups

- In Mcp.Tests, the linked guard test's fully qualified name is now `Keypaste.Core.Tests.PoolTimelineGuardTests`, so `dev.sh --class` with that name selects Core.Tests only. Mcp.Tests still runs the guard whenever the whole project runs.
- Nothing checks that `app.yml`'s push paths cover every file App.Tests links from another test project; a newly linked helper needs its path added by hand.
- **F.28.** On macOS at the base commit, four `EnvLaunchThroughAppTests` cases fail: the screen refuses first because the app opens terminals only on Windows and Linux, while these cases expect the confirmation or the set's own refusal. No CI job runs App.Tests on macOS.
- **F.33.** On Linux at the base commit, `SessionOwnershipTests` once found the app's endpoint still accepting a connection after a lock.
- **F.34.** On Windows in dev run 36736924521, `SaveTimingTests.ATransactedSave_DoesNotQueueBehindAnotherSavesRetryWait` found a refused save attempt holding the save gate 3.2 s, longer than its retry wait; the same code passed there in 36728844369.
- **No Windows baseline for App.Tests and Consistency.Tests.** The broad run's `all` dispatch shares the base run's concurrency group, so it cancelled the base run's Windows leg, and no CI job runs App.Tests on Windows. The backend total on Windows, 3119, differs from Linux's 3117, so totals are compared per platform.
