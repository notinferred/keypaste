# F.62 — `keypaste lock` takes the holder's answer as the confirmation

Completed 2026-10-11 at `21a55e7` on `task/f62`: the dispatched ci 38199647123 and app 38199654563 runs passed every job on all three runners. The integrated commit differs from that tree only in this record.

It had no STEPS row: G.2's CI found the defect in desktop gates on `windows-2025` (app run 38170856945, attempt 1, job 114563062577), where `verify-held-saves.sh` failed with `keypaste lock: the keypaste process holding … did not lock` while the agent's own log ended `the agent has stopped. The vault is locked and every grant is gone.` The re-run passed. The founder chose on 2026-10-11 to fix it as its own task.

## Amendments

- Scope as selected (founder, 2026-10-11): diagnose the lock's three-second settle, show a regression failing on a control branch, fix it and give it its own record. It ran alongside G.2, whose record keeps the observation and links this one.

## Diagnosis

After the holder answered that it was locking, `keypaste lock` attached again on the same connection every 100 ms until the holder refused or was gone, and reported `did not lock` once three seconds had passed. `keypaste agent`'s lock ends its session and closes every connection, so that next attach is written into a connection the agent is closing. The failing job's log could not say which side took the three seconds, so four probe runs of `dev.yml` on `task/f62` timed both sides (the probe is removed):

| Run | What ran on `windows-2025` | Result |
|---|---|---|
| 38187178639 | `verify-held-saves.sh` 100 times on 4 runners, 2 of them with every core kept busy | No failure; every agent lock confirmed within 86 ms |
| 38188819422 | 4,754 agent locks and 1,819 app locks in tight loops on 6 runners | No failure; at most 78 ms |
| 38191117068 | The nine desktop gates in CI's order, 97 passes on 8 runners, beside a watch for whole-machine pauses | No failure; one agent lock took 2,360 ms with no pause seen then; pauses of up to 3,253 ms elsewhere |
| 38194761889 | The same on 10 runners, timing both processes | Reproduced in job 114634876094, pass 1 |

In the reproduction the agent replied, ended its session and closed the lock's connection 1.4 ms after the verb read the reply, and reported itself stopped 8 ms after that. The verb's next attach, written into that connection, failed only 3,057 ms after the reply, once the settle had run out, and the verb printed `did not lock`. The same write took 2,031 ms in another pass. Over the last two runs, 3 of 219 agent locks took more than 2 s, each in the verb's first exchange after the answer; the app's 218 locks in the same gate runs, whose connection stays open and answers, took at most 19 ms; in the last run the verb's 743 other timed steps took at most 134 ms. The delay sits in that write, racing the agent's close of its connection; neither process paused anywhere else.

## What changed

- The holder answers `lock` only once its lock action has ended the session the request was made under. The lock still runs on its own, since a lock waits for its connections to end (D-0392), and the answer waits for it; `LockReply.Locking` is now `Locked`.
- `keypaste lock` prints `locked` on that answer and sends nothing more, so the three-second settle and its `did not lock` are gone. A holder that has not answered within the verb's ten-second bound still gets `did not answer`.

## Evidence

- Regressions, run on a control branch carrying the new tests over the old code (`0b376f7` over `c10fbc8`, since deleted) and failing there:

| Test | Control (old code) | Fix |
|---|---|---|
| `LockVerbTests.Lock_TakesTheOwnersReply_AndAsksNothingMoreOnItsConnection`: a holder that answers the lock and then neither answers nor closes its connection | dev 38198542258: failed on all three runners with `did not lock` | ci 38199647123: passed on all three |
| `SessionAuthorityGrantsTests.Lock_RepliesOnceTheLifetimeHasEnded`: no answer while the lock action has not ended the session | dev 38198748349: failed on all three, the answer arriving while the session was live | ci 38199647123: passed on all three |

- `Lock_RepliesThenEndsTheLifetime`, which held the old order, is replaced by the second regression. `verify-held-saves.sh` passed in app 38199654563's desktop gates on all three runners; it meets the delay only when the race falls that way, so the regressions are the evidence.

## Decisions

D-0434.

## Limits and follow-ups

- Why `windows-2025` delivers the closed connection to that write seconds late is not established; `keypaste lock` no longer depends on it.
- Whether the bridge or `keypaste run --session`, which also write to a holder that may be closing, meet the same delay was not measured.
