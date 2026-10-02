# F.44 — Wait for the listener's recorded disconnection before asserting it

Completed 2026-10-02, tests only; found and finished in one step at the founder's direction of 2026-09-30 that work leave no open rows, so it never had a STEPS row. Runs 36992647190 and 36992650327 below; final dev run 36993052630 at `ad03dd8`; ci 36995550842 and app 36995553700 passed at `0319b47` on `integrate2`, with dev 36995556633 (all lanes and gates on all three runners).

## Amendments

None: the defect was found and repaired in the same step.

## Evidence

**Observation.** Dev run 36881371090 at `d1e1aaa` on `task/c5a2`, job `test (windows-2025)`: `ApproverListenerTests.APeerThatSpeaksOutOfTurn_LosesItsRequestAndItsConnection` failed with `Assert.Single() Failure: The collection was empty` at `ApproverListenerTests.cs:328`. It passed on Windows in run 36904562317.

**Cause.** `ApproverListener.ServeAsync`'s `finally` disposes the connection's framer, which closes the pipe, and only then calls `IApproverHandler.Disconnected`. The test read the end of the stream and asserted the recorded disconnection at once, so on a loaded runner it could look before the listener recorded it. `AHangUpWhileARequestWaits_WithdrawsIt` and the test at line 123 wait on the handler's `Gone` first and do not race.

**Repair.** The test waits on `handler.Gone` before asserting the one disconnection. The other tests that assert `Disconnections` either wait the same way or assert it empty while the connection is still open.

| Run | Commit | Result | What it showed |
|---|---|---|---|
| 36992647190 | `4f44593` | failed, as intended | With a 200 ms delay in the test handler's `Disconnected`, the old test failed with the observed message; the class's other tests passed |
| 36992650327 | `482e9d6` | passed | The same delay with the repair: the class passed on Linux |

## Decisions

None.

## Limits and follow-ups

None.
