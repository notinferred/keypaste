# F.70 — Hear every bridge that knocks at a locked app on Windows

Completed 2026-10-28 on `f70`, source only; runs pending.

## Amendments

The brief that started the task also asked that the bridge's `InvalidOperationException`, which [G.4b](G.4b.md) saw thrown by the runtime's check of who made the pipe, be covered, with a test if one could be made deterministic. D-0555 records both repairs.

## Diagnosis

- **What the failure meant.** "bridge 247 did not reach the app" in 507 ms is `TellAsync` returning false: 247 knocks and a 200 ms connect bound spent. With nothing able to fail the accept loop in that test, the false came from `Connect`'s timeout or an exception `Tell` turned into false.
- **The mechanism.** On Windows the pipe had one instance, made anew for each connection. After a knock, no instance listened until a pool thread ran the listener's continuation, which .NET 10 queues to the worker pool when the connection completes. A pool that had no thread free for 200 ms used up the next bridge's whole bound. Unix was not exposed, because one listening socket queues bridges in the kernel.
- **Probe run 40189674325** at `73071c92`, windows-2025, with the pool capped at its floor of 4 and all four held:
  - the first knock reached the app in 1.9 ms;
  - the second gave up after 203.6 ms on `TimeoutException` (0x80131505), with no thread free and one work item pending;
  - once the pool was released, only the first was heard, five later knocks were all heard, and `unreachable` never fired, so the loop had not died.
- **A second fault.** `Tell` did not catch the `InvalidOperationException` ("The pipe has been disconnected") that the runtime's owner check throws when the listener hangs up before the check has run. Each tool turns an exception into its failure: "the request could not be completed", recorded `failed`. The app waits up to 100 ms for the bridge to hang up, so with keypaste's own listener this needs a bridge held up longer than that between connecting and its check. In run 40190273992, a listener that hangs up at once made 178 of 200 knocks throw it, and the other 22 reached it. The first attempt at that measurement, in 40189674325, was inconclusive, because the probe's own server stopped on `ERROR_NO_DATA` after one connection.
- **Whether a real bridge could go unheard.** Yes, on Windows: a bridge that knocked within 200 ms of another, while the app had no pool thread free, told its agent that no keypaste app was open, though the app was open and locked. The person still saw the notice, since the knock before it showed the one notice a locked spell gets (D-0466). A bridge that hit the second fault failed its call rather than refusing it as `no-approver`.

## What changed

- **`KnockListener` on Windows** keeps a second instance waiting beside the one the app is taking, so the next peer connects without any thread of the app. The app makes another as soon as it takes a connection, before answering it.
  - The first instance is made with `PipeOptions.FirstPipeInstance`, so a pipe somebody else made under the name is still refused.
  - Each later one is made while another of the app's holds the name, so it can only join the app's pipe; the access list from `CurrentUserOnly` is unchanged.
  - No instance limit is set, since an instance the app has closed lives on while its peer holds it, as a second start reading its reply does.
  - `AppClaim`'s second starts share this. Unix keeps one instance answering connection after connection.
- **`LockedAppEndpoint.Tell`** takes an `InvalidOperationException` from connecting as no app reached, since who made the pipe was not checked, and tells its agent what it would with no app open.
- **Tests.**
  - `LockedAppStarvedPoolTests`, alone in its collection, caps the pool at its floor, holds every thread of it, and has two bridges knock: both must reach the app, both must be heard once a thread is free, five more must follow, and `unreachable` must never fire.
  - `LockedAppEndpointTests.AListenerThatHangsUpAtOnce_FailsNoBridge` sends 50 bridges at a listener that hangs up on each at once, and none may fail.
  - `BridgesInQuickSuccession_AreEachHeard` now names the reason when the app stops listening, and fails if it ever does.

## Evidence

Dev runs, each one class on the runners named:

| Run | Commit | Class | Result |
|---|---|---|---|
| 40189674325 | `73071c92` | starved-pool probe, Windows | the mechanism, above |
| 40190273992 | `71ee9e62` | the regression with the repair, Windows | passed; the hang-up measurement above |
| 40190674348 | `17bb4fa3` | `LockedAppEndpointTests`, all three | Ubuntu and macOS passed; on Windows `BridgesInQuickSuccession_AreEachHeard` failed at bridge 187 in 362 ms, with the listener alive, and every other test passed |
| 40190676334 | `17bb4fa3` | `LockedAppStarvedPoolTests`, all three | passed |
| 40190678723 | `17bb4fa3` | `AppClaimTests`, Windows | passed |
| 40191141460 | `849c0af6` | wake probe, Windows | a bridge waiting on a busy pipe was woken 31–86 ms after it began waiting, by a new instance whether waited on or not, by the busy instance disconnected and waited on again, and by a new first instance, each made or reopened 30 ms in |
| 40191585635 | `23e38908` | 10 bursts of 300 knocks, alone | all heard |
| 40191988078 | `e135335f` | `LockedAppEndpointTests` with 10 bursts | 1 of 3300 timed out, below |
| 40192616323 | `a491706a` | the same with 20 bursts | 0 of 6300 |
| 40193150535 | `ac7133c2` | the same, and a hand-run rotation | 0 of 6300; in 200 of 200 rounds, an instance made after earlier ones were taken and closed accepted the next bridge while the connection before it went unanswered |

Every commit from `17bb4fa3` on held the integrated code and only added or removed probes. The batch's ci and app runs are pending. They are to show `LockedAppStarvedPoolTests`, `LockedAppEndpointTests`, `AppClaimTests` and `SecondStartTests` green on all three runners.

## Decisions

D-0555 in [DECISIONS](../../DECISIONS.md). T-10 and T-29 in [THREATS](../../THREATS.md) now say how the Windows instances are made, and that a process of the same user can add an instance of its own beside them, as it could already take the name first.

## Limits and follow-ups

- **A burst can still miss the app.** Two waiting instances cover one bridge while the app has no thread free. A third waits for a thread, as every bridge after the first did before.
- **Two timeouts are unexplained.** With the repair, `BridgesInQuickSuccession_AreEachHeard` and the burst probe timed out twice in about 19,000 back-to-back knocks on Windows: bridge 187 in 40190674348, and knock 1.230 in 40191988078.
  - In the second, knock 229 reached the app in 0.33 ms, and the listener then recorded nothing for 207 ms, while knock 230 found no instance to connect to.
  - The spare made 0.54 ms before it should have taken that knock, as 40193150535's rotation shows a spare does.
  - The diagnosis stays open. The next experiment is to replace the bridge's `Connect` in an in-class probe with raw `CreateFile` and `WaitNamedPipe` calls, recording each attempt's error and time, and to repeat the bursts until a timeout recurs. That would show whether the pipe read as busy or as absent while the spare existed. [F.71](../STEPS.md) holds it.
  - The batch's full run can therefore still fail that test on Windows. It needs a STEPS row of its own.
- `AppClaim.Ask` catches no `InvalidOperationException` either. Whether a second start can meet one is not known, because the app answers it only after writing its reply.
- Source only.
