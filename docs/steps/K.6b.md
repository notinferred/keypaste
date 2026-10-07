# K.6b — Run only the jobs a pull request can break

Completed 2026-09-30 on `task/k6b`, source only; runs ci 36735736913 and app 36735741467 at `9c2cdba`, whose tree differs from the integrated commit's only in this record, with the earlier dispatch, pull request and dev runs below. The Verify's push to `main`, and `require-green-gates.sh` on it, follow integration.

## Amendments

- **`dotnet format` is unchanged.** The row allowed shrinking it to its whitespace check only once the build caught every style rule `.editorconfig` sets, and it does not: IDE0003 (`this.` qualification) goes unreported by the build even at `dotnet_diagnostic.IDE0003.severity = error`, and import order, which the full `dotnet format` reports as `IMPORTS`, has no build diagnostic and is not checked by `dotnet format whitespace --folder`. `.editorconfig` is unchanged too, since promoting IDE1006 matters only once format is gone.
- **Every job needs only `scope`.** The app workflow's KeePassXC, first-run, clipboard and package jobs no longer wait for its gate, and the package matrix comes from `scope`, so a gate that `scope` skips cannot skip a lane it selected; `ci ok` and `app ok` carry the verdict.
- **`checks` runs `verify.sh scripts`,** so the seven selftests the old gate did not list (desktop candidate, pinned asset, AppImage, minimize observer, clipboard markers, desktop install and upgrade) now run in hosted CI; `verify-release-destination.sh --with-real-aws` stays its own step.
- **Each test leg runs `verify-mcp-run.sh` when integration runs.** `verify.sh integration` has listed it since the run tool arrived in `8bed9f5`, and T-35 cites it, but no hosted job had run it; this step's review found the gap, and the leg now runs it beside the other process gates with the same eight-minute limit.

## Evidence

- **Format experiment.** Throwaway branches, since deleted, added one file per style class to `src/Keypaste.Core/StyleProbe/` and ran `dev.yml` with target `build` on Linux, `prepare` running the full format, `dotnet format whitespace --folder . --exclude third_party artifacts` and the build in turn. Runs 36726900478 (`.editorconfig` as is, `ebc3dc9`) and 36726903771 (rules promoted, `78c7aec`); the full format took 55 s on the backend solution and the whitespace check 8 s.

| Rule | Full format | Whitespace | Build as is | Build, promoted |
|---|---|---|---|---|
| Block namespace IDE0161, `using` inside it IDE0065 | yes | no | yes | yes |
| Readonly field IDE0044, accessibility modifier IDE0040 | yes | no | yes | yes |
| Braces IDE0011, unused `using` IDE0005 | yes | no | yes | yes |
| Whitespace IDE0055 | yes | yes | yes | yes |
| Private field naming IDE1006 | yes | no | no | yes |
| `this.` qualification IDE0003 | yes | no | no | no |
| Import order | yes | no | no | no |

- **Pull requests based on `task/k6b` at `1952ca0`,** opened as drafts, closed unmerged and their branches deleted. Job-seconds sum the jobs that ran; wall time was mostly queueing behind other branches' runs, such as PR 8's ci taking 1,678 s for 219 job-seconds.

| PR | Change | ci | app | Job-seconds, ci + app |
|---|---|---|---|---|
| 5 | `docs/BACKLOG.md` | scope, checks (workflows only), ci ok; test and aot skipped | scope, app ok; five skipped | 28 + 12 |
| 6 | comment in a Core.Tests file | scope, checks, test on three OSes building and running only `tests/Keypaste.Core.Tests` and its helpers, ci ok; aot skipped | scope, app ok | 1,001 + 10 |
| 7 | comment in `src/Keypaste.Core` | all 7 jobs, each test leg with the process, demo and compat steps | all 13 jobs | 2,574 + 2,125 |
| 8 | comment in `install.yml` | scope, checks with the script fixtures, test (ubuntu-24.04) running `WorkflowRulesTests`, ci ok | scope, app ok | 219 + 10 |

  In PR 7, `test (ubuntu-24.04)` failed on F.27's merge assertion (ci 36728151037) and the app gate on `AppAuthorityTests.Quitting_answers_a_waiting_request_as_locked_before_the_endpoint_stops` (app 36728150916, filed as F.29); `ci ok` and `app ok` each failed on them, as designed.
- **Full runs.** Ci 36727971505 ran 7 jobs, all green, in 2,920 job-seconds: three test legs of 604, 847 and 997 s with one build each, aot 395 s, checks 69 s. App 36727975787 ran 13 jobs; on its first attempt `keepassxc workflows (ubuntu-24.04)` hung in `apt-get update` until its 20-minute limit, and the rerun of that job passed in 154 s, leaving the run green in 2,397 job-seconds, or 3,616 counting the cancelled job and the `app ok` that failed on it. A full run now compiles the backend 4 times instead of 8, 15 compiles across both workflows instead of 19, in 20 jobs instead of 19.
- **Release gate.** `GITHUB_REPOSITORY=notinferred/keypaste scripts/require-green-gates.sh 1952ca0…` printed `every required gate is green on 1952ca0…: ci app`, counting the two dispatch runs. No push to `main` was made; a dispatch runs the same `--all` selection.
- **Review fixes at `9c2cdba`.** Ci 36735736913 ran 7 jobs green in 2,819 job-seconds; `verify-mcp-run.sh` passed on Linux, macOS and Windows in 2, 6 and 6 s, printing its approve, reuse and deny verdict, and the demo check passed over the rewritten README and site sentences. App 36735741467 ran 13 jobs green in 2,478 job-seconds. `require-green-gates.sh 9c2cdba…` printed `every required gate is green … ci app`.
- **Dev loop.** `dev.yml` with `WorkflowRulesTests`: run 36728015862 on `1952ca0`, green in 326 s, and run 36735778374 on `9c2cdba`, green in 380 s; the script fixtures and actionlint ran green in each full ci run's `scripts + workflows`.

## Decisions

D-0384.

## Limits and follow-ups

- **Some documents a check reads still skip pushes to `main`.** `paths-ignore` already listed `THIRD_PARTY_NOTICES.md`, `CHANGELOG.md`, `SECURITY.md` and `docs/desktop.md`, which `verify.sh` maps to core, scripts or package lanes; a pull request still runs those lanes, and tagging such a commit needs a dispatched run, as before.
- **A full run is not cheaper yet.** The dispatch runs took 5,317 job-seconds at `1952ca0`, 6,536 counting the hung attempt, and 5,297 at `9c2cdba`, against the baseline's 4,716: they restored cold on the new cache keys, the runners were shared with other branches, and the backend format, once on Linux, now runs in each OS's build step. The savings measured here are in pull requests; a warm run on `main` would show whether a full run costs less.
- **New intermittent failures:** F.29, a waiting request left unanswered when the app's endpoint stops, and F.30, a stalled apt mirror; `ci.yml`'s Linux KeePassXC install now stops after ten minutes rather than holding its 45-minute leg. ROADMAP's 0.5.0 goal that CI stays green without retries names F.27 but not these two, and placing them is the founder's. F.27 recurred in PR 7.
