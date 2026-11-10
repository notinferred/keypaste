# F.106 — macOS jobs cancelled on 2026-11-09: no runner took them

Completed 2026-11-10, documents only; no new runs. Read: ci 42620315237, app 42637103393, ci 42647916917 and app 42647916987, their macOS jobs' queue, start and cancel times and check-run annotations.

## Evidence

The `macos-15` jobs went unassigned until GitHub gave up on them. Thirteen macOS jobs in these four runs were cancelled having never started: each has no runner and no step, and carries GitHub's annotation "The job was not acquired by Runner of type hosted even after multiple attempts" beside "Due to capacity constraints, jobs targeting macOS arm64 runners may experience longer queue times."

They were cancelled 901 to 1502 s after being queued, several at one instant across two runs (13:05:17 in ci 42620315237; 16:05:52 and 16:10:52 in ci 42647916917 and app 42647916987), and not at their own limits: `keepassxc first run (macos-15)`, limit 6 minutes, ended at 1202 s and `clipboard markers (macos-15)`, limit 5, at 1502 s. None of these jobs ran a step. app 42637103393 and ci 42647916917 passed once their failed jobs were rerun; ci 42620315237, a scheduled run, has only its first attempt; and app 42647916987's rerun had another macOS job not acquired (`clipboard markers`, 1086 s), the same cause.

In the 1210 macOS job records read back to 2026-10-14, the same shape, no runner and no step, appears 28 times: 1 on 10-28, 1 on 10-30, 3 on 11-04, 1 on 11-05, 9 on 11-06 and 13 on 11-09, so it is recurring and clusters. That count is by shape; only 11-09's 13 were read for the annotation.

## Limits and follow-ups

This is GitHub's macOS capacity, neither keypaste's jobs nor their limits. None of this run data shows who else was queued.
