# F.28 — Hold the app's launch tests on macOS

Completed 2026-09-30 on `task/f28` above `71a8843`, tests and documents only; dev run 36750849335 (`EnvLaunchThroughAppTests` on `ubuntu-24.04` and `macos-15`) at `21c7681`; the squashed commit adds one blank line to the test and the documents; ci 36753070272 and app 36753073956 passed at `07be53e` on `integrate`.

## Amendments

- The two tests that start a real terminal, `Run_starts_a_real_child_…` and `Open_terminal_starts_the_terminal_…`, skipped on macOS through `Assert.SkipUnless`. They now assert the same refusal as the other four, so no test in the class skips and each holds what its platform does, as the Verify asks.
- The Verify names `--os all`; the run covered `linux+macos`. On Windows `TerminalLaunch.IsSupported` is true, so every test takes the path it took before this step, unchanged in behaviour there.

## Evidence

On macOS `TerminalLaunch.ForThisMachine()` maps the platform to `Other`, and `ProjectLaunchViewModel.LaunchAsync` returns at `TryPlan` with "Nothing was started: the app opens terminals on Windows and Linux only; copy the run command instead." before it resolves the set or asks anyone (D-0340). The four failing tests expected the confirmation, the lock or the E.1a refusal, which on macOS the terminal refusal precedes.

Each of the six launch tests now calls `RefusedWithoutTerminal` after mapping the project. Where `launch.IsSupported` is false it runs the launch and asserts that it completed without waiting on anything, that the launcher was never called, that no confirmation is open, that nothing was started and that the screen shows the exact refusal; the test then ends there. Where it is true the helper returns false and the test runs its original assertions. The condition is the app's own, so once E.1d gives macOS a terminal these tests run their full journey there without an edit.

- Dev run 36750849335 at `21c7681`, `test (macos-15)`: the class passed 10 of 10 with none skipped, where dev run 36732743823 had failed four.
- The same run's `test (ubuntu-24.04)` leg was cancelled before it finished. On Linux the class ran in app 36753073956's `format + analyzers + tests` at `07be53e`, where `Keypaste.App.Tests` passed; there `IsSupported` is true and every test takes the path it took before this step.

## Decisions

None. The refusal the tests hold is D-0340's.

## Limits and follow-ups

- On macOS the cancellation, the two lock races and the E.1a refusal are not exercised through the app until E.1d; their logic in `ProjectLaunchViewModel` is platform-independent and runs on Linux and Windows.
- Removing F.28 leaves F.40 with no Needs and C.5a2 needing only C.5a1.
