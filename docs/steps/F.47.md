# F.47 — Let app.yml skip a job a trusted run already passed

Completed 2026-10-02 in `ebaff92`, scripts and workflows only; full ci run 37056506477 and full app run 37055980627 dispatched on the `app-cache` branch. It had no STEPS row: the founder asked to add `app.yml` to the CI cache F.46 built for `ci.yml`.

## What changed

- **A mark per job.** Each `app.yml` job that passes leaves `pass--<runner>--<mark>`:
  - `desktop` from the format, analyzers and tests job, when it ran every desktop test;
  - `appcompat.workflows` and `appcompat.firstrun` from the two KeePassXC jobs;
  - `markers` from the clipboard job;
  - `package` from each publish and selftest leg.

  The appcompat lane runs as two jobs on different runners, so each job is marked on its own and one job's pass never stands in for the other's.
- **[lane-cache.sh](../../scripts/lane-cache.sh)** now trusts `app.yml` dispatches and pushes to main. It compares a mark such as `appcompat.firstrun` with its lane, `appcompat`, when it reads the diff.
- **The planner.** `verify.sh --plan` prints each app job's runners as `app_compat_os`, `app_firstrun_os`, `app_markers_os` and `app_package_os`, less the pairs in `VERIFY_SATISFIED`. It leaves `desktop_tests` empty once the desktop lane has passed. The package runners come from `release-targets.json`; when jq cannot read them the list is `all`, so no target is skipped.
- **`app.yml`.** For a push to main or a pull request, the scope job asks `lane-cache.sh` and plans with its answer; its token gains `actions: read`. Each job takes its matrix from its list, and the package targets are filtered by `app_package_os`. A dispatch or a tag never asks, so a release still rests on a full run. Nothing reads the packages a main push builds: release, install and upgrade read the tag's own run.
- **The release verifier.** `verify-release-matrix.sh` requires every upload in `app.yml` to carry the rehearsal guard and to come after the installer is signed. A pass marker uploads only the commit hash its job wrote, so `is_pass_marker` exempts exactly an upload named `pass--…` whose path is `${{ runner.temp }}/pass.txt`. The new case `marker-uploads-the-installer` refuses the same name over `artifacts/dist/*`.

## Evidence

- Full ci run 37056506477 on `ebaff92` passed every job and left its five markers. Its scripts and workflows job ran:
  - `verify-ci-scope.sh`, 35 cases, four of them the new app cases;
  - `verify-lane-cache.sh`, 4 cases;
  - `verify-release-matrix.sh`, which refused all 83 fixtures, `marker-uploads-the-installer` and `msi-sign-after-upload` among them, and accepted the real `app.yml`.
- Full app run 37055980627 on `ed9df9f` passed every job and left eleven marks: `desktop` on Ubuntu, `appcompat.workflows` on Ubuntu and Windows, `appcompat.firstrun` on all three systems, `markers` on Ubuntu and macOS, and `package` on all three. `ebaff92` changed only the release verifier, which maps to the rules and scripts lanes, so this run's marks stand for it.
- The first dispatch, ci run 37055977554 on `ed9df9f`, failed in `verify-release-matrix.sh`. The five marker uploads lacked the rehearsal guard, and the first came before the installer's signing. It was cancelled so the log could be read, and `ebaff92` adds the exemption.
- `verify-lane-cache.sh` adds three markers:
  - an `app.yml` dispatch's `appcompat.firstrun` stands on an identical tree;
  - on an `app.yml` main push whose diff changes a first-run script, the `appcompat.workflows` mark falls and the `markers` mark stands;
  - an `app.yml` pull request's mark is never read.
- `verify-ci-scope.sh` adds four app cases:
  - with nothing passed, every job keeps its runners;
  - with Linux's first run passed, only that leg drops;
  - with every job passed, nothing runs;
  - a desktop lane already passed leaves no desktop tests.

## Decisions

- D-0411.

## Limits and follow-ups

- `app.yml` has no schedule. A runner image change reaches a cached app job only through a dispatch, a tag, the next change to its inputs, or the marker's expiry after 30 days.
- The desktop mark is written only when every desktop test ran, so a pull request that runs part of them leaves none.
- `dev.yml` leaves no desktop mark.
