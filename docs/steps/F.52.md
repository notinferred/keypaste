# F.52 — Run every check in parallel jobs and stop skipping

Completed 2026-10-07 at `b074854` on `task/f52`, scripts, workflows and documents only. Pull request #10's ci 37607983508 and app 37607983374 ran every job green there, and the integrated commit differs from that tree only in this record. Dev runs 37609347187 and 37609499808 exercised `dev.sh --class`.

It had no STEPS row. On 2026-10-06 the founder asked for CI and testing to be audited after waiting up to half an hour per fix ("I can't be waiting 30 mins each time testing"), and on 2026-10-07 chose to delete the skipping machinery rather than extend it (D-0423).

## Amendments

- Planned as two steps, the dev loop and then `ci.yml` and `app.yml`. Taking `dotnet format` out of the build and splitting the desktop profile changed all three workflows at once, so it became one.
- The first version, `56e804e`, kept the path planner, lane cache and pass markers and gave each new job its own marker. Once dev 37489140641 showed every Linux check in 361 s, the founder chose to delete that layer: with every job in parallel, skipping saves minutes, and the layer cost about 2,500 lines that F.45, F.46 and F.47 had each had to repair.
- The plan's single restore and build in `app.yml`'s KeePassXC workflows job was dropped. The CLI and the app driver share no solution, and the second pass already reuses the first's packages and Core.
- A review, three reviewers with a skeptic checking each finding, found that slimming `dev.yml` removed the only run of the desktop tests and gates on macOS and Windows (F.40's limit, and D-0398's only enforcement). `app.yml` now runs both on all three runners. It also found `dev.sh` would report green for a run that ended with no job, and `verify.sh --list` printing the desktop build twice; both are fixed.

## What changed

- Every `ci.yml` and `app.yml` run runs every job, each `verify.sh` profile as its own job per runner. `ci.yml` runs checks, format, test, integration and compat on three runners, and aot. `app.yml` runs the desktop tests and desktop gates on three runners, KeePassXC workflows, first run, clipboard markers and package.
- Deleted: `lane-cache.sh`, `verify-lane-cache.sh`, `verify-ci-scope.sh`, `verify.sh`'s planner (`--plan`, `--since`, `--all` and the path and project-graph map), every pass marker, both scope jobs, `dev.yml`'s planner and its auto, build, target and gates modes, `verify-release-matrix.sh`'s marker exemption and its fixture, and every NuGet cache.
- `verify.sh`: `format` runs `dotnet format` once over both solutions and the consistency project. `prepare` only restores and builds, so a compile error is no longer held behind a minute of format. `desktop` runs the tests and `desktop-gates` the nine process gates. With no argument it runs every profile but compat.
- `dev.sh` pushes, opens a draft pull request when the branch has no open one, follows that push's ci and app runs, prints each job as it finishes and stops at the first failure with its log. It reads the log through `gh api`, because `gh run view` refuses logs while a run goes on, and a run that ends with no job counts as failed. `--class` and `--relock` dispatch `dev.yml`, which keeps only those two jobs, so a filtered run never counts as release evidence.
- On a branch a newer run cancels the one before it; `main` and tags keep every run.

## Evidence

| | Before | After |
|---|---|---|
| Every check, Linux | dev 37478702958: one serial job, 1182 s | dev 37489140641: 361 s, longest job 296 s |
| Every check, three runners | dev 37478702958 1743 s, then ci 37438294611 18 min and app 37438298130 10 min | the pull request's runs: ci 713 s and app 490 s at once; `dev.sh` green in 728 s |
| One test class | — | dev 37609347187: green in 82 s |
| `dotnet format` per Linux run | about 205 s in five calls, before every build | 162 s once, in its own job |

- The pull request's run had 31 jobs. The longest, `test (windows-2025)`, ran 605 s after waiting 104 s for a runner: 31 jobs exceed a free account's 20 concurrent jobs and 5 macOS jobs, and `integration (macos-15)` waited 307 s.
- The desktop gates passed under macOS's bash 3.2 in 327 s and on Windows in 430 s; App.Tests passed on Windows in 454 s and macOS in 474 s.
- Dev 37609499808, a misspelt class: `dev.sh` printed the job's log ending in `Exit code: 8` 72 s after dispatch and exited 1 while the run went on.
- Dev 37489140641's scripts job failed on shellcheck SC2100, which read `lane=desktop-all` as arithmetic; quoting the value fixed it.
- In the pull request's checks job, `verify-release-matrix.sh` passed without the marker case and actionlint passed over the three rewritten workflows.

## Decisions

D-0423.

## Limits and follow-ups

- The time to all green is set by Core.Tests and Cli.Tests on Windows and macOS, 5 to 7 minutes against about 2.5 on Linux. Of the process gates' roughly 7 minutes, about 5 are fixed waits. Both are BACKLOG investigation candidates.
- A full run queues behind GitHub's concurrency limits; about 100 s of the pull request's 728 s was waiting for runners.
- Nothing is cached, so each Windows job restores in about 50 s.
- A release still needs a ci and app dispatch at the commit, since `require-green-gates.sh` is unchanged, though every push to `main` now runs every job.
- A documents-only push to a pull request runs every job.
- A read-only audit of the tests followed; its findings go to the founder before anything is removed.
