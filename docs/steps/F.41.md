# F.41 — The connection check's scripted listing no longer waits on the thread pool

Completed 2026-10-01 on `task/f41` above `ccb7cc1`, tests and documents only; dev runs 36872142099 at `15949f6`, 36872193827 at `4065fad`, the control, 36883736902 at `e4a7584` and 36891388792 at `ebba6fa`, whose tests differ from the squashed commit's only by a probe; final dev run 36899609919 at `5a87a42`; ci 36995550842 and app 36995553700 passed at `0319b47` on `integrate2`, with dev 36995556633 (all lanes and gates on all three runners).

## Amendments

- The row asked for a diagnosis. Under the founder's direction of 2026-09-30 that rows left open that day be finished with no new steps, the repair of the measured cause lands here rather than in a separate repair row.
- Every `McpConnectionCheckTests` case that indexes a listing first asserts that its `Problem` is null, so an empty listing now fails with its reason.
- The repair is in the test's scripted bridge, not in `McpConnectionCheck.StepTimeout`: the ten seconds were spent in the test host's thread pool, with no bridge process involved.
- At the founder's direction relayed on 2026-10-01, the scripted bridge in `ConnectClientViewModelTests`, which had the same anonymous pipes and `Task.Run` but had not been seen to fail, was repaired too. Both bridges now speak over [ScriptedBridgeChannels](../../tests/Keypaste.Core.Tests/ScriptedBridgeChannels.cs), which `Keypaste.App.Tests` compiles in as it does `ManualClock`, and each class has the regression below.
- At review on 2026-10-01, tests over real anonymous pipes were added for the check's step timeout, its exit and its request deadline. The move to in-memory channels had left no test of those paths over an operating-system pipe, and the step timeout had never been tested; PRODUCT §4.5 keeps bridge-path tests mandatory.

## Evidence

**The failure.** In ci run 36771067113's first attempt at `703ca27`, the case started about 2.5 s after the test run began and failed after 10.4 s with no entries.

**Under the suite's load.** f41-probe run 36866181557 at `31a1ed2` ran ci.yml's backend command, `dotnet test keypaste.slnx --no-build -c Release`, twice on each of eight `windows-2025` runners. For the life of each Core test host a sampler listed one at a time over three channel kinds, each listing started on a pool worker that then returned to the pool as an awaiting test does. The kinds were the class's anonymous pipes with the bridge started by `Task.Run`, in-memory `Pipe`s on the default scheduler as `HarnessChannels` builds them, and in-memory pipes with inline schedulers and the bridge started in its constructor.

| Channels | Listings | Over 1 s | Over 5 s | 10 s or more | Longest |
|---|---|---|---|---|---|
| Anonymous | 4,755 | 185 | 59 | 6, in 5 of 16 suites | 10,952 ms |
| Default-scheduled `Pipe` | 4,748 | 7 | 2 | 0 | 5,742 ms |
| Inline `Pipe` | 4,742 | 0 | 0 | 0 | 479 ms, 77 ms on runners not arming dumps; every one complete when `ListAsync` returned |

Each of the six returned no entries and "the bridge did not answer within 10 seconds". In each, the bridge's serving loop had not started, and the check's read ended in `OperationCanceledException` at the deadline. Of the 80 slowest anonymous listings, five per suite, 78 spent all but 85 ms or less waiting for the bridge's loop to start, as with 9,825 ms of 9,826, or ended with it never started; in the other two the loop started at once, and where their hops were kept the check's first write waited 5.2 s to run. Three triage dumps taken three seconds into held listings each show the check's read parked in `ReadFile` under `PipeStream.AsyncOverSyncRead` on a pool worker, no thread in the bridge, three or four workers waiting in Argon2's `FillMemoryBlocks` and at most one idle. The default-scheduled pipe's slow listings waited between exchanges for a worker to run a continuation, once for 5.5 s. The same sampler on `ubuntu-24.04` in that run found every kind under 300 ms. The class's own cases, still on anonymous pipes there, listed in up to 5.9 s.

**Reproduced.** f41-probe run 36869083098 at `13e39cc` listed from a test body while 32 one-millisecond work items re-queued themselves, so the pool's global queue never emptied. On `windows-2025` the anonymous listing failed 3 of 3 at 10,067 to 10,132 ms with the same problem and the bridge never started. The default-scheduled pipe took 23 to 94 ms and the inline pipe completed within the call. On `ubuntu-24.04` the anonymous listing took 24 to 43 ms, its bridge starting within 9 ms.

**Mechanism.** On Windows each read and write on an anonymous pipe runs as a pool work item, and a read holds its worker in `ReadFile` until data arrives. The bridge's loop, queued by `Task.Run` from the test's worker, started only when a worker took it; under the flood, and at times under the suite, none did for ten seconds, and the check's read then ended at its deadline. The original failure's `Problem` was never recorded, so attributing it to this mechanism rests on its matching 10.4 s and empty listing. That the loop waited in the parked worker's own queue, which other workers take from only when the global queue is empty, is inferred from .NET's dispatch order and the flood, not observed: triage dumps carry no heap, so none lists queue contents. Under the same flood on Linux the bridge started within 9 ms.

**Product.** No bridge process existed in the failure, so nothing measured here bears on ten seconds for a real bridge's first answer, and `StepTimeout` is unchanged.

**Real pipes.** `SilentBridge` gives the check a real anonymous pipe pair with no serving task: a dedicated thread reads the one request, closes that end, moves a `ManualClock` to one second short of the deadline, records that the call is still pending and moves it the last second. `A_listing_nobody_answers_fails_at_the_step_timeout_and_a_closed_pipe_is_an_exit` gets "the bridge did not answer within 10 seconds" and then, with the far ends closed, "the bridge exited without answering"; `A_request_nobody_answers_fails_at_its_deadline` gets "no answer within 60 seconds". No serving task needs a worker and the deadline arrives from that thread, so a busy pool can slow these tests but not change their outcome. f41-probe run 36891378706 at `ebba6fa` ran ci.yml's backend command twice on each of eight `windows-2025` runners, and `McpConnectionCheckTests`, both new tests included, passed in all 16 suites; one suite failed only `SaveTimingTests.ASaveHeldAtTheGate_IsReportedAsGateWait_AndNamesTheHolder`, which this step does not touch. f41-probe run 36898795789 at `8d53c79` ran the class 20 times on each of `windows-2025`, `ubuntu-24.04` and `macos-15` while 32 one-millisecond work items re-queued themselves, read back at 20 to 33 items pending: 60 of 60 hosts passed all nine tests, in 1 to 3 s each.

**Repair and regression.** Each scripted bridge speaks over two `System.IO.Pipelines` pipes whose reader and writer schedulers are inline, and its loop starts in its constructor. Each write runs the other side to its next read on the writing thread, so a listing completes within `ListAsync`. `The_scripted_bridge_answers_within_the_checks_own_call`, in both classes, asserts the listing is complete when `ListAsync` returns.

- Dev run 36872193827 at `4065fad`, `McpConnectionCheckTests` on `windows-2025` with the bridge reverted to anonymous pipes and the regression kept: the regression failed and the other seven passed.
- Dev run 36872142099 at `15949f6`: `McpConnectionCheckTests` passed 8 of 8 on `ubuntu-24.04`, `windows-2025` and `macos-15`.
- f41-probe run 36872143938 at `15949f6` ran ci.yml's backend command twice on each of eight `windows-2025` runners: `McpConnectionCheckTests` passed in all 16 suites. One suite failed only `SaveTimingTests.ATransactedSave_DoesNotQueueBehindAnotherSavesRetryWait`, as one did in run 36866181557: main's old check, which F.34 replaces.
- Dev run 36883736902 at `e4a7584`, with both bridges on the shared channels: `Keypaste.Core.Tests` and `Keypaste.App.Tests` passed on `ubuntu-24.04`, `windows-2025` and `macos-15`.
- Dev run 36891388792 at `ebba6fa`, with the real-pipe tests: the same, 2,321 core tests on Linux and macOS and 2,323 on Windows, 863 app tests on each.

## Decisions

None in the ledger. Binding these two classes only: their scripted bridges need no thread-pool worker to answer, which their regressions hold.

## Limits and follow-ups

- Where the bridge's queued loop waited is inferred, as stated above.
- Whether a busy pool in the app could hold a real bridge's answer was not measured; on Windows the check reads the bridge's output through a synchronous pipe too.
- The sampler, the flood and the probe workflow were deleted; their results are above.
