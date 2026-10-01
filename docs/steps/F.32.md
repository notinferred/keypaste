# F.32 — Keep a prompt a bridge hang-up withdrew off the screen

Completed 2026-10-01 on `task/f32` above `ccb7cc1`, source only. The dev runs below built the commits named in them, on `ubuntu-24.04` unless stated; dev run 36890277908 at `3d5601e` passed `core`, `app` and `consistency` on all three runners, and this commit differs from it only in this record and in the tests' assertion that each hold ran first. Final dev run 36898207268 at `0cf1fff`, whose first Windows attempt failed only the cleanup race F.42 repaired; ci 36995550842 and app 36995553700 passed at `0319b47` on `integrate2`, with dev 36995556633 (all lanes and gates on all three runners).

## Amendments

- By the founder's direction of 2026-10-01, relayed during the step, the two limits the first draft of this record listed were closed here instead: a test now carries a hang-up through the app's own composition along every path that asks a person, and a lock was examined, found to draw a withdrawn prompt when it ends the lifetime off the UI thread, and repaired. D-0396 covers both.
- The regressions hold the withdrawal as well as racing it. A callback registered on the cancelled token after the tokens below are linked to it runs first and keeps the withdrawal from the gate's token until the UI thread has run its queue, as F.20's `hangupheld` arrangement did. .NET runs those callbacks newest first without documenting the order, so each regression records the gate's token and asserts it is still live once the UI thread has run, before it lets the hold go; a change in that order fails it rather than letting it pass unheld. Without the hold the outcome depends on which thread runs first; with it only a show job that reads the cancelled token passes, so cancelling inline instead of with `CancelAsync` would not.
- The Verify's regression puts the request straight to a real `ApprovalGate` over the app's `WindowApprovalChannel`, because the token the listener gives its handler has to be in the test's reach to be held. The path tests run the app's own composition, `AppAuthority`, and hold the token they find among the withdrawals the show job reads.

## Evidence

**The cause.** A withdrawal requested above the gate marks its own token at once and reaches the gate's token, the only one F.20's show job read, only as the callbacks linked in between run. A hang-up withdraws with `exchange.CancelAsync()` in `ApproverListener.AnswerWhileWaitedForAsync`, which runs them later on the thread pool. A lock ends the session lifetime with `Cancel()`, which runs them on the locking thread: in the app the idle timer locks on a pool thread, so the UI thread can run the show job in between. `SessionAuthority`, for a field, and the env resolver, for an env set, a run and a token's set, each link the gate's token from the exchange's and the lifetime's.

**The repair.** `Withdrawals`, in `Keypaste.Core.Approval`, keeps in an `AsyncLocal` the tokens whose cancellation withdraws the request the calling flow answers. The listener adds the exchange's token, and `SessionAuthority.RequestAsync` and `SessionEnvResolver.ResolveAsync` add the lifetime's where they link from it. `WindowApprovalChannel.ShowAsync`, which runs in that flow, captures them, and its show job returns when any is cancelled, as it already did for its own token and an answered request. The channel's registration on its own token still decides the answer once the withdrawal reaches it. The listener's stop token is not added: in the app and in `keypaste agent` every stop follows the end of the lifetime it served.

**The regressions**, in `DesktopApprovalTests`:

- `A_bridge_that_hangs_up_before_the_draw_keeps_the_prompt_off_the_screen`, the Verify: 100 iterations through a real `ApproverListener` on its own pipe. A peer sends a credential request; the handler asks the gate, which queues the show job, and holds the token the listener gave it; the peer hangs up while the UI thread has not run; the UI thread spins until that token is cancelled, runs its queue and releases the hold. Each iteration must end `Cancelled` with no prompt drawn.
- `A_hang_up_reaches_the_prompt_along_the_path_that_asked`, for a field, an env set and a run through `AppAuthority`: a raw peer attaches, sends the request and hangs up once it is queued; the token held is the one other than the lifetime's among those the show job reads, and the hang-up must cancel it before the UI thread runs its queue, with no prompt drawn.
- `A_lock_off_the_UI_thread_keeps_a_queued_prompt_off_the_screen`, for the same three: the attached bridge's request is queued, the lifetime's token is held, and `Lock(VaultLockReason.Idle)` runs on a pool thread as the idle timer's does; the UI thread runs its queue as soon as the lifetime ends, nothing is released and no prompt is drawn.

| Run | Commit | Built with | Result |
|---|---|---|---|
| 36868489235 | `7db188c` | the Verify's regression, no repair; image `20260920.314` | `Expected: 0, Actual: 100` in 2.4 s, every iteration `Cancelled`; the other 21 passed |
| 36871519878 | `83b0c9a` | the hang-up repair as first written, reading the exchange's token alone | the class's 22 passed on `ubuntu-24.04`, `windows-2025` and `macos-15` |
| 36883738591 | `74c6420` | every repair, with the listener starting its handler under `ExecutionContext.SuppressFlow()` in `Task.Run` | the three path tests failed with "the show job reads nothing the hang-up cancels" and the Verify's drew 100 of 100; the three lock tests and the other 21 passed |
| 36889335666 | `379ac0c` | the hang-up repair and every test, without the lifetime added | each lock test drew its prompt: `ApprovalWindow`, `EnvApprovalWindow`, `RunApprovalWindow`; the other 25 passed |
| 36890277908 | `3d5601e` | this commit's `src`, and its `tests` without the hold-order assertion; `core`, `app` and `consistency` on `ubuntu-24.04`, `windows-2025` and `macos-15`, no gates | backend 2304, desktop 865 and consistency 44 passed on Linux and macOS, 2313, 866 and 44 on Windows; none failed |

Dev run 36867986971 at `ec62ec6` built nothing, as CA1068 refused a record whose token was not its last parameter; 36883625094 at `f3f2ac9` was cancelled by the next dispatch on the branch, which shares its concurrency group. 36872402896 at `d88402e`, the first squash, passed the backend, desktop and consistency suites on all three runners and the process gates on Linux and Windows, and stopped at `verify-session-authority.sh:40` on macOS, which is F.40.

## Decisions

D-0396.

## Limits and follow-ups

- No CHANGELOG entry: a drawn prompt needed the UI thread to reach the show job between the withdrawal's mark and its callbacks, it came down when they ran, and none was reported.
