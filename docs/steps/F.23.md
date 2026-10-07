# F.23 — Open the approval window before the person is asked

Completed 2026-09-24 on `main` above `c5fa7b1`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

Amendment, on the founder's direction on 2026-09-24 that the Agent Activity failure in [F.22](F.22.md)'s run 36084643134 be fixed as part of finishing F.22 rather than left open: the mechanism was found by reading the gate and reproduced deterministically in-process, so no branch probe ran, and the repair was made in the same step.

## Evidence

**Mechanism.** `ApprovalGate.AskOnceAsync` called the channel and only then created the window's `Task.Delay` on the gate's clock. The test's scripted person completes its `Waiting` signal inside that call, and the test then advances its `ManualClock` by 5 and 40 seconds. When the test thread ran before the gate's thread reached `Task.Delay`, the delay was created after both advances, measured 45 seconds from the later time and never fired, so the request never ended. `ApprovalGateTests.NoAnswerBeforeTheWindowCloses_IsADenial` waits on the same kind of signal and was exposed to the same order.

**Repair.** The gate creates the window's delay before asking the channel, and a channel that throws or cancels synchronously cancels it on the way out.

**Regression.** `ApprovalGateTests.TheWindowIsOpenBeforeThePersonIsAsked` asks through a channel that advances the gate's clock by the whole window while putting its prompt up and then never answers. It expects `TimedOut` within 10 seconds.

| Run | Source | Result |
|---|---|---|
| Local, Windows 10 Pro 22H2 19045.6332 | `c5fa7b1`'s gate with the regression | failed: `System.TimeoutException`, the window never closed |
| Local, Windows 10 | the repair | `ApprovalGateTests` 20 of 20 |
| Local, Windows 10, `bash scripts/verify.sh` | the repair | VERIFY |

## Decisions

None.

## Limits and follow-ups

- **Hosted evidence.** The repair has not run on a hosted runner. The failure it explains was seen once, on `ubuntu-24.04`, and a pass there after the repair would not by itself show the race is gone; the deterministic regression does.
- **Other clocks.** Other timers created after a signal a test waits on were not audited.
