# F.20 — A withdrawal requested before the prompt is drawn keeps it off screen

Completed 2026-09-30 on branch `task/f20` above `a432ade`, source only. The confirming runs built `f8979e0` and `5cdda78`, which have this commit's `src` and `tests` and differ from it only in documents. Dev run 36738627473 at `5cdda78` passed the app and consistency tests on `ubuntu-24.04` and `windows-2025` (844 and 43 on each); on `macos-15` the regression passed and four launch tests failed, F.28, as in dev run 36732743823 at `f8979e0`. App run 36734160169 at `f8979e0` covered what dev runs leave to `app.yml` and passed every job: the desktop and consistency tests on `ubuntu-24.04`, the KeePassXC workflows on `ubuntu-24.04` and `windows-2025`, the KeePassXC first run and the publish, selftest and packaging on all three runners, and the clipboard markers on `ubuntu-24.04` and `macos-15`.

## Amendments

- The selection allowed a small repair with a regression that fails without it once the mechanism was confirmed, in place of a separate repair row. It is included.
- The row asked for the identical desktop test command. The first pair of runs used `dev.yml`'s desktop step over the whole `Keypaste.App.Tests` project, which is CI's desktop command without the consistency project; later runs ran the probe class alone.

## Preflight

Each probe iteration recorded whether the channel's token was cancelled when `Shown` fired, whether the request was already answered then, and whether one request was shown twice.

| Hypothesis | Predicts |
|---|---|
| H1, the row's: `CancelAsync` marks the token at once and runs the channel's callback later on the pool, and the awaiting test frees the UI thread, whose dispatcher runs the posted show job while `IsAnswered` is still false | draws only after the withdrawal was requested; none when the test cancels inline; every iteration drawn when the callback is held back; none once the show job also reads the token |
| H2: the show job ran before the withdrawal was requested | draws with the token not yet cancelled, including under an inline cancel |
| H3: a stray or second show | one request shown twice |

## Evidence

**The failure.** App run 36040583862, attempt 1, image `ubuntu24/20260920.314`: `Expected: 0, Actual: 1` at `DesktopApprovalTests.cs:142`, the project's only failure. At `f51fe5d` the test withdrew with `await withdrawn.CancelAsync()`. `bc3cd7a` (G1–G5) changed it to cancel inline, with a comment that awaiting would let the UI thread draw first, so the test on `main` lost the failing arrangement while the product kept the ordering.

**The probe.** A branch-only `WithdrawnPromptProbe` repeated the case in the headless session, each iteration with a new channel and token:

- *awaited*: the test as it was at `f51fe5d`; *inline*: the test as it is on `main`;
- *delayed*: awaited, with a callback registered after the channel's that sleeps 20 ms; callbacks run in reverse order of registration, so the channel's runs after it;
- *held*: `CancelAsync` not awaited, the channel's callback held behind one registered after it until the UI thread has been drained;
- *gate*: the product's own withdrawal. A real `ApprovalGate` over the channel, on a manual clock advanced through its 45-second window before the UI thread runs, calls `await withdraw.CancelAsync()` on the token it gave the channel (`ApprovalGate.cs:224`); the UI thread drains once that token is marked. *gate held* holds the channel's callback as *held* does.

`bash scripts/dev.sh` on `ubuntu-24.04`, 4 CPUs, .NET 10.0.12. Iterations drawn, one figure per run in the order the runs are listed below:

| Arrangement | Unrepaired | Repaired |
|---|---|---|
| awaited | 294/300, 293/300, 296/300 | 0/300, 0/300, 0/300 |
| inline | 0/300, 0/300, 0/300 | 0/300, 0/300, 0/300 |
| delayed | 50/50, 50/50, 50/50 | 0/50, 0/50, 0/50 |
| held | 50/50, 50/50, 50/50 | 0/50, 0/50, 0/50 |
| gate, waiting with `SpinWait.SpinUntil` | 0/100 in the second run | 0/100 in the second run |
| gate, spinning without yielding | 97/100 in the third run | 0/100 in the third run |
| gate held | 50/50 in the third run | 0/50 in the third run |

- **Runs.** Unrepaired: 36727465387 at `1e1efcc` over the whole project, image `20260920.314`, 848 tests, 5 failed: the four probe rows, which fail by design to report their counts, and the new regression; then the class alone in 36728458400 at `78d0599` and 36732915616 at `8e815b6`. Repaired: 36727196033 at `2d6c42b` over the whole project, image `20260927.320`, 848 tests, 4 failed, the probe rows; then 36728479312 at `35f1185` and 36733500148 at `af3363c`.
- **H1 holds.** Every request was denied and none was shown twice. Every draw came after the withdrawal was requested, which rejects H2 and H3. In *awaited*, 277, 293 and 296 of the drawn requests were already answered by `Shown`: the callback ran while the window was being built, after the show job's check.
- **The product drew a withdrawn prompt too.** When the UI thread drained as soon as it saw the gate's mark, the prompt was drawn after its window had closed in 97 of 100 iterations; waiting with `SpinUntil`, whose yields gave the pool time to run the callback first, it was drawn in none. The gate marked the token on the pool, or in some repaired iterations inline in the clock's advance on the UI thread; with the repair neither drew.

**The repair.** The show job in `WindowApprovalChannel.ShowAsync` returns when its token is cancelled as well as when the request is answered. The registration precedes the post, so a cancelled token's callback is certain to run and deny the request. `DesktopApprovalTests.A_withdrawal_requested_before_the_draw_stops_it_before_its_callback_runs` is *held* once: `Expected: 0, Actual: 1` without the repair in 36727465387, passing with it in 36727196033.

## Decisions

None in the ledger. Binding this code: the prompt's show job reads its token as well as the request, so a withdrawal counts from when it is requested rather than from when its callback runs.

## Limits and follow-ups

- **The unforced rate is the probe's.** A warm loop reached the show job first in 293 to 296 of 300 iterations; the single test at `f51fe5d` failed once in the recorded app runs, and a cold single test's rate was not measured.
- **A hang-up still draws: [F.32](../STEPS.md).** A bridge that hangs up withdraws with `CancelAsync` on an ancestor of the channel's token (`ApproverListener.cs:193`), and the tokens linked below it are cancelled only when the pool runs the ancestor's callbacks. A show job that runs in that gap, as long as the one repaired here, reads its token as live and draws the withdrawn prompt, which stays up until the posted take-down. With the repair, the probe drew it through a real gate whose token descends from the cancelled one in 99 of 100 iterations, and in 50 of 50 with the ancestor's callbacks held until the UI thread drained (dev run 36738139311 at `ee3f820`).
- **Linux only.** Windows and macOS ran the regression in 36732743823 and 36738627473, not the probe.
- **Found here.** The app's launch tests fail on `macos-15`, where the app refuses to open terminals: [F.28](../STEPS.md).
- **The probe was deleted.** Its last versions are `8e815b6` on `task/f20-probe`, unrepaired, and on `task/f20-probe-repaired` `af3363c` for the rows above and `ee3f820` for the hang-up.
- **No CHANGELOG entry.** Through the gate, a drawn prompt needs the UI thread to stall for the whole window, and none was reported.
