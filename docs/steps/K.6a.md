# K.6a — Build and test a pushed branch on GitHub from a machine with no SDK

Completed 2026-09-30 in `d0a903b`, source only; pull request runs ci 36711854555 and app 36711854220 at `7222d90`, and dev runs 36725106579, 36725394981 and 36725768126.

## Amendments

- The founder directed on 2026-09-30 that the development Mac builds nothing; this row was added for that reason and selected the same day.
- `scripts/verify-keepassxc-projects.sh` passes `-` to `paste`, because BSD `paste` requires a file operand. That missing operand had kept `keepassxc compat (macos-15)` red on `main` since `2e8a5c5` (runs 36607877538, 36629888034 and 36646739875).
- `require-green-gates.sh` also ignores pull-request runs in `--run-id` mode.

## Evidence

- **Baseline before this step.** A push to `main` at `3e29da7` ran 19 jobs taking 4,716 job-seconds: ci 36646739875 had 8 jobs and took 916 s of wall time, and app 36646739852 had 11 jobs and took 915 s.
- **Change map.** `verify-ci-scope.sh` passes 23 cases. It fails when the reading of `Compile … Link` is disabled, and when the git-error branch is removed.
- **Release gate.** `verify-green-gates.sh` passes 14 cases, including `ci-green-only-on-a-pr` and `run-id-from-a-pr`, each of which fails once its filter is removed.
- **Pull-request runs.** Every job passed in ci 36711854555 and app 36711854220, including `keepassxc compat (macos-15)`. `keepassxc compat (ubuntu-24.04)` failed on its first attempt; that is filed as F.27, and its second attempt passed.
- **Dev loop, run from a Mac with only Git and `gh`:**

| Command | Run | Result |
|---|---|---|
| `dev.sh --class Keypaste.Core.Tests.WorkflowRulesTests` | 36725106579 | Green in 130 s |
| a branch commit asserting `1 == 2` | 36725394981 | Failed, and `gh run view` printed `failed Keypaste.Core.Tests.DevLoopProofTests.ADeliberateFailureIsNamed` |
| `--class …WorkflowRulesTestz` | 36725768126 | Failed with the runner's exit code 8 |

## Decisions

D-0383.

## Limits and follow-ups

- **No job scoping.** Pull requests and `main` run every job: K.6b added scoping, and [F.52](F.52.md) removed it (D-0423).
- **Warnings only.** In auto mode, dev runs only warned about the aot, appcompat, markers and package lanes and about the Windows and macOS runners they skipped. Auto mode is gone: `dev.yml` runs one test class, one profile on one runner, every Linux job of ci and app but compat and tools, or a relock, and is never release or integration evidence ([F.52](F.52.md), [K.6c](K.6c.md), [F.66](F.66.md), D-0460).
- **`dotnet format` was unverified.** It formatted each project in a dev run's closure, and that it skips referenced projects came from its source and was not observed. Since [F.52](F.52.md) the `format` profile runs it once over both solutions and the consistency project (D-0423).
- **Flaky compat gate:** F.27, closed by [F.27](F.27.md), which keeps the revision KeePassXC's merge dropped when two saves shared a second and repaired the gate's own check.
