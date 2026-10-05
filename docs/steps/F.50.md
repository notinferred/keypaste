# F.50 — Let install.yml's Windows job reach the install check

Completed 2026-10-05 at `227def9` on `task/f50`, workflow only. Runs:

- install 37385483971, dispatched on the branch. All four runners and the new `origin` job passed.
- dev 37385239898, the workflow checks.

## Amendments

None. It was found during F.48 and repaired at the founder's direction of 2026-10-05.

## Evidence

**Observation.** Since the schedule of 2026-09-21 (35608188051, 36442040660, 37346630656), `install (windows-2025)` was cancelled at its 15-minute limit inside "Verify the published releases are still on the origin" and never ran its install block. On 2026-09-16 (35047059579) that step took about five minutes (02:12:16 → 02:17:11).

**Cause.** That step ran `verify-release-matrix.sh --with-public-origin` in each of the four matrix jobs, and the script also runs its own fixture self-test. In job 111886718232 of run 37346630656:

- The repository and origin checks finished in under two minutes (17:12:18 → 17:14:05).
- The fixtures, about a hundred definitions, then took about six seconds each under Git Bash, until the cancellation at 17:27.
- The fixtures had grown since 2026-09-16.

Neither the origin's answer nor the fixtures depend on the runner, and ci.yml's scripts lane already runs the fixtures.

**Repair.** `install.yml` gains an `origin` job on `ubuntu-24.04` with a full checkout, which runs the check once per run. The four install jobs no longer run it and check out at depth 1, since none of their steps reads history.

| Run | Job | Took | What it showed |
|---|---|---|---|
| 37385483971 | `origin` | 71 s | the published releases are on the origin |
| 37385483971 | `install (windows-2025)` | 19 s | "the windows install block works: keypaste 0.3.0 and keypaste-mcp, from a clean machine, and the installed binary creates a vault and injects into a child process", and its negative control |
| 37385483971 | the other three runners | under 1 min each | their install blocks and negative controls |

## Decisions

None. Where the origin check runs binds only this workflow.

## Limits and follow-ups

The fixtures still take about six seconds each under Git Bash. Contributors on Windows already run them in the Linux container (CLAUDE.md), and no workflow runs them on Windows now.
