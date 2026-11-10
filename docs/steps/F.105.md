# F.105 — The KeePassXC install limits, fitted to measured runs

Completed 2026-11-10 on `backlog-ci`, source only; ci 42822341787 at the final code `4fa4c1e9` ran `aot publish (linux-x64)` under this limit, green. Read: `gh api` job and step times of `keepassxc first run (ubuntu-24.04)` since 2026-10-14 and `aot publish (linux-x64)` since 2026-11-04, and the job logs of ci 42266260601, ci 42299832656, ci 42156288498, app 42157932479, app 41891465844 and app 41904307592.

## Evidence

The download is the slow part. `apt` fetched from `azure.archive.ubuntu.com` at about 40 kB/s in the slow runs: `Fetched 16.4 MB in 7min 11s (38.2 kB/s)` in ci 42299832656, cancelled, and `in 6min 44s (40.7 kB/s)` in ci 42156288498, green; the install step's median is 11 s for `aot` and 15 s for the first-run job.

| Job | Green executions | Seconds: median, p95, slowest | Limit before | Twice the slowest | Limit now |
|---|---|---|---|---|---|
| `keepassxc first run (ubuntu-24.04)` | 170 | 106, 151, 258 (app 41904307592) | 6 min | 516 s | 9 min, set by [F.104](F.104.md) for all three runners |
| `aot publish (linux-x64)` | 52 | 213, 256, 568 (ci 42156288498, its install 412 s) | 10 min | 1136 s | 19 min |

The cancelled runs were all in or just after the install: `aot` in ci 42266260601 (install cut at 424 s) and ci 42299832656 (441 s, finished, then the limit), and first run in app 42157932479 (383 s) and app 41891465844 (372 s), with app 41926224435 (267 s) losing its last seconds to the same limit in the cache's save.

## Limits and follow-ups

`aot`'s limit rests on one slow green run; the next slowest is 270 s, so most of the 19 minutes exists to outlast a slow mirror. `apt-install.sh`'s 30-second request timeout does not help, because the bytes keep flowing. What makes that mirror slow is not known from these logs.
