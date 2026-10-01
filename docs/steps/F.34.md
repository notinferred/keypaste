# F.34 — Check a refused save's gate release whatever its attempt takes

Completed 2026-10-01 on `task/f34` above `ccb7cc1`, tests and documents only; probe dev runs 36864642445 and 36868119087, red dev run 36866394512, green dev runs 36867014443 at `0927ee5` and 36871742859 at `e9121f7` on all three runners, and dev run 36875050606 against a gate kept through the wait. 36868119087 concluded failure on the probe's report while `SaveTimingTests` passed. The squashed commit differs from `e9121f7` by the two test changes in the amendments below and this record; final dev run 36875719752 at `e1a0e59`; main's old check also failed in f41-probe 36866181557 and 36872143938 and in the first Windows attempt of dev 36890422000, each the runner stall described here; ci 36995550842 and app 36995553700 passed at `0319b47` on `integrate2`, with dev 36995556633 (all lanes and gates on all three runners).

## Amendments

- The founder directed on 2026-09-30 that rows left open that day be finished now with no new steps, so the repair this row left to a later row is made here, on the cause the probe measured.
- The measurement found the product right and the test's bound wrong, so the repair is to the test; `src` and the vendored library are unchanged.
- On the same direction, `ASaveHeldAtTheGate_IsReportedAsGateWait_AndNamesTheHolder` lost its assertion that the queued save's gate wait exceeds its own work. It compared a save's work with the 1000 ms hold, the failure mode found here, on every platform; the gate wait of at least half the hold, the holder it names and `AssertComponentsFitTheTotal` still show a held gate read as gate wait.
- On the same direction, the waiting save's check in the F.34 test is now that it took the gate with no holder named, `HeldBy == 0`, where it was a gate wait under 50 ms, a time a stall could also reach. `KeePassInterop` sets `_gateHolder` right after taking the gate and clears it right before releasing it, and `SaveClock` records the value a save reads as it begins to wait, so a save arriving during a wait that keeps the gate names the holder whatever the runner's speed. Dev run 36875050606 at `27b021d`, whose `KeePassInterop` kept the gate through a refused attempt's wait, failed that check with `gate=1020 heldby=3`, the holder's operation, while the class's other five tests passed; the product change was then removed.

## Evidence

**The failure.** Dev run 36736924521 at `d5ea3f3`, `windows-2025`, failed `SaveTimingTests.ATransactedSave_DoesNotQueueBehindAnotherSavesRetryWait` at line 139 with `held=3224/1001 work=3222/1001 waits=1964/- gate=0/318 heldby=0/793 total=6508`. The test compared the refused attempt's whole hold with the retry wait, a 1000 ms hold the test arranges.

**The probe.** A branch probe marked each phase of `PwDatabase.Save`, the Argon2 lanes' queue delay and compute, both moves of the transacted fallback with their native codes, GC pauses and system CPU, and ran the test's arrangement 24 times in the `SavesTimedAlone` collection under the identical backend command, `dotnet test keypaste.slnx --no-build -c Release`, on `windows-2025`. Its first run, 36797114984 at `229e2c7`, measured nothing: a mark between the fallback's `MoveFileEx` and its `throw new Win32Exception()` reset the thread's last error, so the save failed with code 0, which it does not retry. Dev run 36864642445 at `b5b7813` kept the code and ran all 24 iterations beside `Keypaste.Cli.Tests`; 36868119087 at `a73a468`, the probe beside the repaired test, ran 22 of its 24 after that suite had ended:

| Refused attempt, ms (range, median) | 36864642445, 2.3–3.9 of 4 cores busy | 36868119087, 0.5–2.4 busy |
|---|---|---|
| Whole attempt, the gate's work | 162–392, 249 | 108–180, 115 |
| Key derivation | 138–359, 207 (58–94%) | 91–159, 97 (65–88%) |
| of which Argon2 lanes queued in the thread pool | 0–151, 51 | 0 |
| Encryption, write and close | 3–49, 4 | 2–16, 2 |
| The refused transacted move, the first hop and the refused second hop, both 6800 | 15–110, 40 | 11–40, 14 |
| Gate held outside the attempt's work (re-read, backup copy, sweep) | 2–20, 2 | 2–3, 2 |
| Retry wait, for the 1000 ms hold | 1018–1119, 1076 | 1010–1053, 1020 |

The second attempt's successful move took 10–52 ms. In all 48 iterations the waiting save took the gate in 0 ms with no holder named, the holder's second attempt never waited for it, and the refused attempt's hold stayed under its wait, at most 38% of it. The probe's source was removed; `b5b7813` holds it.

**The cause.** A refused attempt is the key-derivation-bound attempt every save makes, about twice as long beside the CLI suite as after it. Meeting the held name adds tens of milliseconds, and outside the attempt's work the gate covered only the re-read and sweep D-0137 puts under it and V.4a's backup copy, together 2 ms in the failing save, the probe's median. The 3222 ms is the attempt's own work, slowed with the rest of the process: in the same save the 1000 ms hold became a 1964 ms wait against the probe's 1119 at most, the unrefused second attempt took 1001 ms against 388 at most, and that attempt then waited 318 ms at the gate for the waiting save, which in every probed iteration had finished long before. The runner stalled the whole process for those 6.5 s, and a bound that compared the attempt's work with the arranged wait failed though the gate had been released before that wait.

**The repair.** The test bounds what the gate covers outside the refused attempt's re-read and work, `Held − Reread − Work`, by the wait, so the attempt's own duration no longer enters it. The bound fails by construction when the gate is held through the wait: `SaveClock` measures `Held` from taking the gate to releasing it, so `Held ≥ Reread + Work + Wait`. No run measured that case. The regression makes the holder's refused attempt sleep twice the hold inside it, so on every run the attempt outlasts its wait as the stalled runner made it.

| Run | Source | Result |
|---|---|---|
| dev 36866394512, `--class …SaveTimingTests --os windows` | `a9a4aaa`, the regression with the old bound | failed at line 139: `held=2204/178 work=2193/178 waits=1032/-`; the other five passed |
| dev 36867014443, auto `--os windows` | `0927ee5`, the repair | `Keypaste.Core.Tests` passed, 2313 with 8 skipped, none of them this class |
| dev 36868119087, auto `--os windows`, the identical backend command | `a73a468`, the repair with the probe | 3243 passed and 11 skipped; the one failure is the probe's report |
| dev 36871742859, auto `--os all` | `e9121f7`, the repair and this record | `Keypaste.Core.Tests` passed: 2313 with 8 skipped on Windows; 2304 with 15 skipped on Linux and macOS, this Windows-only test among them |

## Decisions

None in the ledger. Binding only this test: a refused attempt's hold is bounded by what the gate covers outside the attempt, never by the attempt's duration against a sleep, and the waiting save is judged by the holder it names, not by how long it waited.

## Limits and follow-ups

- What stalled the runner in 36736924521 is not identified. The probe's 48 iterations under the same command saw no stall, and its slowest attempt was 392 ms.
