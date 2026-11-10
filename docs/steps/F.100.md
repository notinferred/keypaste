# F.100 — Listen for the next bridge without the thread pool

Completed 2026-11-10 on `backlog-knock`, source only. Dev runs below; the exact code's Windows evidence is app 42822347029 and ci 42822341787 at the final code `4fa4c1e9`, both green on Windows, macOS and Linux.

## Amendments

Founder's selection of 2026-11-10: run F.70's next experiment, the bridge's `Connect` replaced by raw `CreateFile` and `WaitNamedPipe` in an in-class probe, find the mechanism and fix the cause, with no retry. There is no STEPS row.

Settled in the step, all reversible: at most 1,024 connected peers wait for the pool before no further instance is made, so a process of this user that connects and holds cannot grow the pipe without limit (T-29 already lets it add instances of its own).

## Diagnosis

`Probe_RawKnocks`, in `LockedAppEndpointTests`, alternated raw `CreateFile`/`WaitNamedPipe` knocks with `NamedPipeClientStream.Connect` over 170 s, while the rest of Core's tests ran beside it, with a ring of the listener's steps, each instance's state and a thread beside the listener timing the pool.

- **Run 42810689677**, before the fix: 3 misses in 606,299 knocks, 2 raw and 1 through `Connect`, so the runtime's `Connect` is not the cause.
  - Every miss read `ERROR_PIPE_BUSY` (231) for its whole 200 ms; neither `ERROR_FILE_NOT_FOUND` nor a lost wake-up occurred.
  - In every miss both waiting instances were closing (state 4): two bridges had connected and hung up within about 900 µs of the listener's last step, and the third found none listening.
  - The listener's next step came 213 ms, 826 ms and over 1.5 s later, with 8–10 pool work items pending, a pool turnaround of up to 1,941 ms, and a plain dedicated thread never late by more than 49 ms.
- **The mechanism.** F.70's second instance covers one bridge, and the next waits for a pool thread to make it an instance, the limit F.70 recorded. The "spare" F.70 thought should have accepted knock 230 had already been taken by the knock before it. .NET completes a pipe's connection on the worker pool, so every step that kept an instance listening needed a pool thread, and a pool that stood still for 200 ms lost the bridge. Nothing was wrong with the pipe.

## What changed

- **`KnockListener` on Windows** now has a dedicated thread that does nothing but keep instances listening: it waits for each arrival with `ConnectNamedPipe` and an event of its own (`PipeArrival`), which needs no pool thread, makes the next instance, and hands the connected pipe to the async answering loop through a bounded channel. Answering stays one at a time. Unix is unchanged.
- **Who reaches the app.** A knock no longer depends on the pool to find an instance; a bridge is answered and heard when the pool frees.
- **`Dispose`** keeps its token when the answering loop has not finished by the grace, since that loop still reads it.
- **Regression.** `LockedAppStarvedPoolTests.BridgesKnockingWhileNoPoolThreadIsFree_ReachTheApp_AndAreHeardOnceOneIs` now sends five bridges with every pool thread held, and each must reach the app and be heard once a thread is free. Before the fix, Windows failed it at the third bridge in run 42812107331, deterministically.

## Evidence

Dev runs, `backend` on `windows-2025`, each Core's whole test project:

| Run | Commit | Code | Result |
|---|---|---|---|
| 42810689677 | `d1bc4781` | before the fix, 170 s probe | 3 misses of 606,299, above |
| 42812107331 | `db516c8a` | before the fix, five-bridge regression | failed at bridge 2 |
| 42812965658 | `b51f5b4a` | fix, 32-peer bound | regression passed; 1 miss of 509,700: a `Connect` when 32 peers had filled the bound during a 702 ms pool stall, which is why the bound rose; every other test but the probes passed |
| 42814080354 | `687536af` | the fix with bound 1,024, plus the probes' trace | 0 misses of 981,000, 2 knocks found no instance at first and were reached; every other test passed. The pool never stalled here, at most 6 ms, so this run says nothing about a stall |

The probe's knocks arrived about 3,000 a second, so its stalls could fill any bound; `BridgesInQuickSuccession_AreEachHeard` sends 300 and cannot.

## Effect on a person

Before this, on Windows, with a locked app whose thread pool stood still for 200 ms or more, an agent's request in a burst of three or more was told no keypaste app was open, though the app was open and locked. The knocks before it had reached the app, so the person still saw the one notice a locked spell gets. A second start of the app in that burst met the same wait.

## Limits and follow-ups

- Windows only, on GitHub's runners. No test holds a real person's machine in that state.
- A clean soak does not prove an intermittent race absent, and the last run met no stall at all. The regression is what holds the mechanism, deterministically; run 42812965658 shows the bound, not the pool, was what remained under a real stall.
- `LockedAppEndpointTests.APeerThatNeverHangsUp_HoldsNothingUp`, ci 42637103496's failure, failed on stopping within a second. Probe in run 42812965658 measured `Dispose` at 2,001 ms with the pool held, its full grace, so that failure fits a pool stall of a second or more; the failure itself was not reproduced, and the fix does not change it. BACKLOG holds what is open. Its earlier failure, ci 39507285378, is the race F.66 fixed. The fix's probes are deleted, and this record keeps their results.
