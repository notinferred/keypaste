# F.42 — Let the launch tests' child finish before cleanup

Completed 2026-10-02 on `task/f42` above `ccb7cc1`, tests only. The defect was found and finished in this one step at the founder's direction of 2026-09-30, so it never had a STEPS row. Dev run 36890422097 reproduced the failure on main's cleanup. Dev runs 36898503209, 36899623293 and 36905544009 passed the repair and its two closures. At the squashed commit, which cannot hold their ids, a `--os all` run of the class and a Linux run of the whole App.Tests suite passed. final dev runs 36992254097 and 36993363698 at `896a855`; ci 36995550842 and app 36995553700 passed at `0319b47` on `integrate2`, with dev 36995556633 (all lanes and gates on all three runners).

## Amendments

No row was selected. As directed:
- repair the test so that it waits for or ends the child it started before it deletes the project directory;
- change the product only if the product leaves a child running that it should not;
- show the cause with a reproduction;
- pass `EnvLaunchThroughAppTests` repeatedly on windows-2025 and on Linux and macOS.

After the first squash (`ace75e0`), the coordinator's direction of 2026-10-01 closed the two limits that record listed:

- **The file scan skips empty files.** A FIFO reports length 0, so the `clr-debug-pipe-*` FIFOs of a running or killed .NET process no longer block the scan on Linux, and a file holding a value still fails it. .NET 10 does not cheaply report a path's file type, because `FileSystemInfo.Attributes` and `UnixFileMode` carry none, so the check uses the length.
- **`Dispose` retries the directory delete.** On `IOException` or `UnauthorizedAccessException` it retries for 3 s, which covers a terminal that finished before the launcher returned and so was never held. If the delete still fails, the error names the processes that Windows reports as holding the directory (`FileProcessIdsUsingFileInformation`). The regression's child now lingers 6 s, twice the retry, so the regression still fails unless the test waits for the child itself.

## Observation

In dev run 36867370896 at `6d0d1f4` (`task/f40`), job `test (windows-2025)`, `Run_starts_a_real_child_…` failed in `Dispose` at `EnvLaunchThroughAppTests.cs:69`. `Directory.Delete` of the project directory threw `IOException`: the file was "being used by another process". The same test passed on Windows in runs 36865305910 and 36871580142. It failed the same way, at the same line and as 1 of 869, in attempt 1 of dev run 36898207268 at `0cf1fff` (`task/f32`), `test (windows-2025)`.

## Cause

The cleanup ended the terminal by its process id. `EndTree` looked the id up, called `Kill(entireProcessTree: true)` and waited for the terminal alone, and an id that .NET reported as not running counted as gone. Nothing waited for the reporter, the real child that runs in the project directory, and on Windows a process holds its working directory until it has finished exiting. A holder could therefore outlive the cleanup in two ways. The tree kill terminates descendants without waiting for them. A process whose exit has begun is reported as not running while its handles are still open, so `EndTree` returned at once, for the terminal as well as the child.

On the runner `cmd /k` reads end of input and exits as soon as its command does, so the cleanup often met a terminal that was already gone or going. The product is not at fault: the app's terminal is meant to outlive the launch (D-0340), and on a person's console `cmd /k` stays at its prompt.

The exact holder in run 36867370896 was not captured again. On windows-2025, 128 probe rounds on main's cleanup, 96 of them under the full suite's load, did not fail. This cause is the mechanism that the holder readings and the reproduction establish.

## Evidence

The probes in this list were temporary test code, removed before the squash; their source is in the branch's earlier commits.

- **36875290014** at `4a437a1`, windows-2025, the class alone: 32 rounds of the Run flow, 8 of them with a child lingering 3 s after it reported, each deleting the directory straight after `EndTree`. No process held the directory and every first delete succeeded. The tree kill ended the lingering child before `EndTree` returned.
- **36876108277** at `edc7d0f`, windows-2025, the whole App.Tests suite (910 tests): 48 such rounds gave the same result, and the real test passed.
- **36883431403** at `c48cb59`, windows-2025, the whole suite: the real Run test ran 48 times, reading the directory's holders through `NtQueryInformationFile(FileProcessIdsUsingFileInformation)`, and none failed. In 25 rounds the terminal had exited before the cleanup, and `EndTree` found its id not running and waited for nothing. In 9 rounds, when the report had been read, a process .NET already reported as not running was still listed as holding the directory; in 3 of them it was the terminal, and `EndTree` returned at once. Only `cmd.exe` and the reporter ever held the directory, never `conhost`. Every holder was gone 5 ms after `EndTree`, so the observed failure needs an exit that lags further than that, as it can under load.
- **36890422097** at `f02a4e7`, windows-2025, with main's cleanup unchanged. A variant whose child outlives its terminal (`start "" /b`, lingering 3 s after reporting) failed 5 of 5 in `Dispose` with the observed `IOException`. Each time the holder was `Keypaste.EnvReporter`. The class's 10 tests passed.
- **36891161208** at `056c994`, the first repair, which waited for the child only at disposal:
  - windows-2025 passed the class and 20 more rounds each of the Run and background tests. The only failures were three probes that skipped the wait for the child, each with the observed `IOException`.
  - macos-15 passed.
  - ubuntu-24.04 hung until the job's 50-minute limit, because the Run test's file scan now ran while the child was still alive.
- **36898503209** at `e0e40fd`, which waits for the child before the scan:
  - A Linux probe read a running child's `/tmp/clr-debug-pipe-7369-25787-in` and `-out`; the read of each blocked, and neither file remained after the child exited.
  - windows-2025 passed 52 of 52 (the class plus 20 Run and 20 background rounds), and macos-15 passed 52 of 52.
  - ubuntu-24.04 passed 51 of 52, failing only that probe, which always fails so that it prints.
- **36899623293** at `e0e40fd`, windows-2025, the whole App.Tests suite: 901 of 904 passed, including those 40 rounds; the 3 skipped are the screen renderers.
- **36905544009** at `2d19760`, the two closures, on all three operating systems. The probes here always fail so that they print.
  - **The FIFO skip, on ubuntu-24.04.** While a child ran, its `-in` and `-out` FIFOs reported length 0, and the scan finished in 2 ms.
  - **A real leak is still caught.** On all three, the scan failed on a file in the project directory holding a value, naming it.
  - **The delete retry, on windows-2025:**
    - A child the test did not wait for, lingering 1 s, was outlasted by the retry, and the test passed.
    - One lingering 6 s failed after the retry with "is still held by: Keypaste.EnvReporter 3128".
  - Every other test in the class passed on every system.

## What changed

- **`Keypaste.EnvReporter`'s report mode** now:
  - reports its process id;
  - stays connected until the test hangs up;
  - with `--linger S`, waits S seconds more before it exits.
- **`EnvLaunchThroughAppTests`** now:
  - holds each terminal by its handle as soon as the launcher has started it;
  - reads the report up to `end`, holds the child, hangs up and waits for the child to exit, before any assertion that scans files;
  - ends each held terminal in `Dispose` and waits for it;
  - deletes the directory with the bounded retry above;
  - skips empty files in the scan.

  No process is looked up by an id it may have released, so the cleanup can no longer kill an unrelated process that has reused the id.
- **`A_command_run_in_the_background_has_the_set_in_the_directory_after_its_terminal_returns`** holds that a backgrounded command gets the set in the directory (PRODUCT §2). It is also the regression: its child outlives its terminal and lingers 6 s, longer than the delete retry, so on Windows it fails unless the test waits for the child itself.

## Decisions

None: the repair binds only this test class and its helper.

## Limits and follow-ups

None.
