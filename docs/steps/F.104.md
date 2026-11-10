# F.104 — `keepassxc first run`'s limit, after finding where its time goes

Completed 2026-11-10 on `backlog-ci`, source only; no new runs. Read: `gh api` job and step times of 181 runs of the job since 2026-10-14 (172 green), and the 58 Windows job logs since B.5's cache began on 2026-11-04, including app 42223860031 and app 42251019859.

## Amendments

The founder asked for the cause of the build's time before the limit was touched.

## Evidence

The compile did not vary much: a green `Restore and build the app driver` took 59–155 s (median 95), its `dotnet build` 39–94 s of that since 2026-11-04. The two cancelled attempts lost their time elsewhere.

- app 42223860031 attempt 1, cancelled at 390 s: that runner was slow from the start, checkout 56 s against a median of 9 and `build-argon2.sh` 184 s against a median of 24 and a green maximum of 60, and `dotnet restore` had printed nothing after 118 s. Its rerun passed in 212 s with the same cache miss.
- app 42251019859 attempt 1, cancelled at 365 s: `dotnet restore` took 3.81 min where a green restore on a cache miss took 36–79 s, and the compile had reached `Keypaste.Infrastructure` when the limit ended it. Its rerun passed in 269 s, restoring in 71 s.

NuGet's cache did not separate them: both attempts missed, as nearly every run does. Of 58 executions since 2026-11-04, 4 hit (restore 9–28 s) and 54 missed (green restores 36–79 s, 51 s on average). Each key is per OS, workflow, job and lock-file hash at 0.7–1.5 GB, against the repository's 10 GB store, which held 10.4 GB and no key for this job on 2026-11-10; that fits eviction, and B.5 records that a branch's cache is not readable elsewhere.

The rule supports a change. The job's slowest green execution took 269 s on `windows-2025` (app 42251019859, attempt 2), 258 s on `ubuntu-24.04` and 260 s on `macos-15`; twice 269 is 538 s, so the matrix's limit in `app.yml` moves from 6 to 9 minutes. Both cancelled attempts' work would have fitted.

## Limits and follow-ups

Whether that 3.81-minute restore waited on nuget.org or on the runner cannot be read from these logs; it takes NuGet's HTTP timings (`dotnet restore -v n`) on an attempt that is slow, which is left in BACKLOG with the cache's misses. The 6-minute limit was also cutting Linux installs, measured in [F.105](F.105.md).
