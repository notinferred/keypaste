# K.6c — A branch's Linux jobs run in one dev run, and class checks no longer cancel each other

Completed 2026-10-18 at `56adf93` on `task/k6c-dev-loop`, source only; runs ci 39128135977 (attempt 2) and app 39128143006, dispatched in full at that commit, and dev 39127675071 in linux mode. The integrated commit differs from that tree only in this record.

It had no STEPS row; the founder approved its plan on 2026-10-17.

## Amendments

- Scope as selected (founder, 2026-10-17): `bash scripts/dev.sh --linux` runs every Linux job of `ci.yml` and `app.yml` in parallel in one `dev.yml` run, except the jobs of `verify.sh`'s compat profile (ci's `compat`, app's `compat`, which is named keepassxc workflows, and app's `first-run`) and ci's `tools`, with `aot` kept on `ubuntu-22.04`; class checks of different classes no longer cancel each other; a full `bash scripts/dev.sh` stays the only integration and release evidence; `WorkflowRulesTests` holds where `linux_only` may appear (D-0460).
- CLAUDE.md, by the founder's direction (2026-10-17): it describes `--linux` for intermediate iterations, says a class check runs on one runner unless the change is platform-specific, and says the founder's machine where it said a development machine.

## What changed

- `ci.yml` and `app.yml` declare a boolean `linux_only` under `workflow_call` alone, false by default. With it, test, integration, desktop tests, desktop gates and clipboard markers run on `ubuntu-24.04` only, the targets job gives package only the `linux-` targets, and both `compat` jobs and `first-run` are skipped; `tools` is skipped already, since a call carries no `tools` input. Without it every matrix is the literal list it was. The targets job fails when the package matrix holds fewer targets than `release-targets.json` declares, unless `linux_only` came from `dev.yml`'s call, which it tells by `github.workflow`, the caller's name in a called workflow.
- `dev.yml`'s `relock` boolean becomes `mode`: class, linux or relock. Linux mode calls both workflows at the dispatched commit from the jobs `linux ci` and `linux app`, on a branch ref only, since on a tag ref app's package job attests. The app caller grants `id-token` and `attestations` because GitHub checks a called job's permissions against its caller's before the run starts. Runs are grouped by ref, mode, class and runner.
- `dev.sh --linux` dispatches linux mode, prints once which jobs it leaves out, follows the run as before, and ends a green run by saying it checked Linux only and is not integration evidence. It refuses `--class`, `--os`, `--relock` and `--tools`. The hint about the test runner's exit code 8 is printed only for a class check.
- `WorkflowRulesTests` flags `linux_only` anywhere but a ci or app `workflow_call` input and `dev.yml`, in any case under ci's and app's `on:`, where GitHub reads input names case-insensitively, and flags any YAML alias or merge key there, which could hand the input to another trigger.

## Evidence

- Linux mode: dev 39127675071 at `56adf93`, green 367 s after its dispatch by `dev.sh`'s count, in 12 jobs: ci's scripts + workflows, format, test, integration, aot publish and ci ok, and app's targets, desktop tests, desktop gates, clipboard markers, publish + selftest (`ubuntu-24.04`) and app ok. ci's `compat` and `tools` and app's keepassxc workflows and keepassxc first run were skipped. The full iteration before it, F.64's ci 39125458882 and app 39125465297, was green 807 s after its dispatch.
- Two class checks of different classes dispatched together: dev 39127678653 (`WorkflowRulesTests`, green in 83 s) and dev 39127678532 (`PlainWordsTests`, 61 s), whose jobs ran from 12:32:08 to 12:33:18 and from 12:32:07 to 12:33:02 UTC; neither was cancelled.
- Full runs at `56adf93`: app 39128143006 passed all 18 jobs on its first attempt, and ci 39128135977 every job on its second, with `tools` not asked for.
- `WorkflowRulesTests`: all 5 passed in dev 39127678653, the new rule and its fixture among them, and again in ci 39128135977's test jobs.
- On the founder's machine only `bash -n scripts/dev.sh` ran.

## Decisions

D-0460.

## Limits and follow-ups

- Linux mode runs nothing on Windows or macOS and no compat job, so it does not check the desktop gates under macOS's bash 3.2 (D-0398), `verify.ps1 --list`, Windows signing, the MSI, the app bundle, the DMG or the compat profile's gates; only a full run judges a change to those.
- actionlint no longer checks the runner labels of the narrowed matrices, which are now expressions.
- A matrix job added later runs every leg in linux mode until it is narrowed like the others, which is slower but checks more.
- Found while planning and left outside K.6c: `scripts/verify-release-destination.sh` says a test asserts both workflows pass `--with-real-aws`, and none does. `ci.yml`'s step that publishes the CLI with NativeAOT piped `dotnet publish` into `tee` without `shell: bash`, so it ran without `pipefail`; it now runs under `shell: bash`, as release.yml's does (gaps-1c, RUNS-PENDING).
- ci 39128135977's first attempt cancelled `compat (windows-2025)` at its 30-minute limit, 32 minutes into `verify-keepassxc-fields.sh`, which K.6c does not change; the step normally takes a median of 32 seconds, the runner kept no log, and the re-run of that job passed. F.65 watches that gate and probes it; the passing re-run does not close it.
