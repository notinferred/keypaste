# K.9 — Retries and timeouts declared as resilience policies

Completed 2026-10-31 at `68b3a172` on `k9`, source only; runs ci 41196182751 and app 41196189194 at that commit. The integrated commit differs from `68b3a172` only in this record.

## Amendments

- With the plan (founder, 2026-10-31, D-0677):
  - The package is the one that removes more code. That is Polly.Core 8.9.0: Microsoft.Extensions.Resilience's retry and timeout strategies are Polly.Core's, and it adds dependency-injection and telemetry wiring that keypaste has no host for, with 18 more packages.
  - The adoption is recorded with no justification text.
  - Four groups of sites are replaced:
    - the retry-with-wait loops;
    - the deadline loops;
    - the polls on a clock;
    - the crash sender's request bound.
- During the build (founder, 2026-10-31): Polly waits a synchronous retry by blocking on `Task.Delay`, a pool timer, which is the failure F.9 fixed in the bridge. The five synchronous sites therefore keep a `Thread.Sleep`, which the policy calls from its retry hook. Polly declares their attempts and what is retried.

## What changed

- **The package.** `Keypaste.Core` references `Polly.Core` 8.9.0, and the CLI, the bridge, the browser relay and the app get it through Core. All 18 lock files that reach Core gained only that package, with the content hash of the package nuget.org serves.
- **The synchronous retries.** `Internal/Retries.cs` builds these: up to N attempts while an outcome says try again, with the caller's wait run between attempts and Polly's own delay zero.
  - The vault save keeps `TryAttempt` as it was. The save loop and its sleep, of `SaveRetryDelayMilliseconds` times the attempt number, become one such policy. `waitBetweenAttempts` still stands in for the sleep, and `SaveClock` still times it.
  - The backup restore's `ObserveReadable` and `MoveIntoPlace` use the same policy, and their `Wait` is gone.
  - The audit-log lock retries `IOException` and `UnauthorizedAccessException`; the access-caps lock retries `IOException` only. Each makes 50 attempts 20 ms apart.
  - The CLI's Windows clipboard declares its own policy: 10 attempts, 10 ms apart.
- **The deadline loops.** `ApproverClient`'s sliced pipe connect retries `TimeoutException` with no wait while its budget lasts, still on its own long-running thread. `AppClaim.AskToShow` asks, then tries to take the home, and asks again while the answer is not yet in and the budget lasts.
- **The polls.**
  - The bridge's handshake poll retries on the injected clock every `HandshakePoll` until `grace`, its first ask at once.
  - Windows Hello looks for its prompt at once and then `_promptChecks` more times, 500 ms apart, so its last look still comes at 2 s.
- **The crash sender.** A timeout policy of `RequestTime` on the sender's clock replaces the timeout token source and its link.
- **The publisher check.** Polly's assembly copyright, `Copyright (c) 2015-2026, App vNext`, is in the NativeAOT `keypaste`, so `verify-publisher-metadata.sh` now accepts that one line beside keypaste's, KeePassLib's, the .NET Foundation's and Microsoft's. A probe file carrying the same line with another holder is still refused.
- **Trimming.** Every pipeline is Polly's non-generic one. The generic `AddRetry<T>` asks the trimmer to keep every member of `T`, while the non-generic one keeps only its options.
- **No test file changed.**

## Evidence

- On the founder's Mac (macOS 26.6, arm64, SDK 10.0.302 from `~/.dotnet`):
  - `verify.sh compile` passed: both solutions and the consistency project, with warnings as errors and the format check. The app's Windows target compiles there too.
  - One `verify.sh test` run passed 241 Core tests, with 3 skipped off Windows, across `SaveTimingTests`, `VaultSaveTests`, `VaultBackupTests`, `VaultRestoreTests`, `VaultMergeSaveTests`, `VaultAccessChangeTests`, `SessionAuthorityMergeTests`, `AuditLogTests`, `AccessCapsTests`, `AppClaimTests`, `ApproverClientTests` and `CrashSenderTests`.
  - The same run passed 6 bridge tests, in `HandshakeGraceTests` and `PoolShortageTests`.
  - `Keypaste.Cli.Tests.ApproveAuditLogTests` passed 1.
  - Nothing was left running after either run.
- Polly's source at tag 8.9.0 holds no `ModuleInitializer`, so THREATS' property of the binary's closure holds.
- **At `3735788d`:**
  - ci 41194776431 passed every job.
  - app 41194782712 failed `publish + selftest` on all three runners, on Polly's copyright line, which `68b3a172` allows.
  - In the same run, `desktop tests (windows-2025)` was cancelled at its 13-minute timeout. `Keypaste.App.Tests` printed nothing after 11:00:56, about a minute into its run, and the log does not name the test it was in. That job had passed in the 29 app runs before it.
- **At `68b3a172`:**
  - ci 41196182751 passed every job.
  - app 41196189194 passed every job but `keepassxc first run (macos-15)`, BACKLOG's former F.74, where KeePassXC exited before opening the vaults after the Qt font-alias warning.
  - `desktop tests (windows-2025)` passed 1354 of 1373 App tests with 19 skipped, and the 39 consistency tests, in 506 s.

## Decisions

D-0677.

## Limits and follow-ups

- The Windows App tests' hang at `3735788d` is unexplained: its first occurrence, in the first run at K.9's code (D-0673). Run with `--xunit-diagnostics on` beside `--long-running 120`, the runner names the test that has run past 120 s.

- The source shrank by about nothing: 17 fewer lines in the files changed, plus the 33 lines of `Retries.cs`. The thread-sleeping waits and the non-generic pipelines took what a fully declarative policy would have saved.
- The reconnect-once in the bridge and the relay, `MergeAnotherSave`'s re-merge, the waits for a launched process, `ChildContainment`'s reaping, heartbeats and arming delays stay hand-written, because none of them retries a failed operation after a wait.
- `Keypaste.Browser.Tests` cannot run on this Mac without OpenSSL 3, which the libsodium build's signature check needs, so the relay's connect was checked only on CI.
