# F.65 — Watch the fields gate and make its same-second check deterministic

Completed 2026-10-18 at `14fc063` on `task/f65-windows-hang`: the dispatched ci 39133601367 and app 39133609361 runs passed every job on all three runners on their first attempt. The integrated commit differs from that tree only in this record and BACKLOG's row.

It had no STEPS row: the first attempt of ci 39128135977, K.6c's full run, cancelled `compat (windows-2025)` 32 minutes into `verify-keepassxc-fields.sh`, an observation [K.6c](K.6c.md) recorded, and the founder's direction of 2026-10-17 is that intermittent CI failures are fixed at their root.

## Diagnosis

- **The stop.** In job 117677493447 the fields step started at 12:42:12 and was still running when the job's 30-minute limit cancelled it; its Post steps never ran and the runner kept no log. In the 75 `compat (windows-2025)` jobs among the 80 ci runs before it, that step passed 73 times, in a median of 32 seconds and at most 55; the other failures were F.63's probes with short deadlines. Like the projects gate's 28-minute stop ([F.63](F.63.md)), it left nothing to read, so its cause is not known.
- **A clock race.** A probe running the gate over and over on `windows-2025` under a 120-second watchdog (dev 39131010090 at `39552ba`) passed nine runs in 32 to 46 seconds and failed the tenth in 27: "10 attempts could not save twice inside one second". The step checked D-0387, that each keypaste edit is stamped in a later second than the version before it, by waiting for a new second and running two `keypaste set` processes until both finished inside one second; on a loaded runner none of ten attempts did.
- With the step rewritten, a second probe (dev 39133580091 at `c7afb04`) passed all 45 runs on `windows-2025`, in 30 to 41 seconds with a median of 32. None stopped, so the stop did not recur there.

## What changed

- `verify-keepassxc-fields.sh` arms F.63's watchdog at 300 seconds, naming `keypaste`, `Keypaste.AppDriver`, `keepassxc-cli`, `node` and `workerd`, and `ci.yml` gives its step a 10-minute limit as a backstop.
- The same-second step no longer races the clock. KeePassXC merges a version of the entry stamped an hour ahead, longer than the gate can run; keypaste then saves the field twice, and KeePassXC's export must hold the merged version at exactly that second and the two saves in later seconds, each after the one before. A keypaste that stamped by the clock alone, or that did not move a second past the previous version, fails it on any machine. The newer-copy merge that follows, and its checks, are unchanged.

## Evidence

- The probes: dev 39131010090 and dev 39133580091, above. Both were deleted.
- ci 39133601367 and app 39133609361 at `14fc063`, green on all three runners, the rewritten step passing in each `compat` job.

## Limits and follow-ups

- The stop's cause is unknown and stays a BACKLOG investigation; the next one in either gate fails its step within five minutes with the phase and every process printed.
- The step no longer reaches two saves inside one clock second end to end. `VaultHistoryTests.EditsInOneSecond_EachReachTheFileInASecondOfTheirOwn` holds that case.
