# Diagnose a failure without rebuilding the measuring tools

Record the failing behavior and the question the next observation must answer. Distinguish product failures, harness failures and missing evidence before choosing a repair. Retain the command, commit, environment, result and next question with the evidence.

## Use the retained instruments

| Instrument | Entry point | What it establishes |
|---|---|---|
| Arranged pool shortage | `PoolShortageTests` in `Keypaste.Mcp.Tests` | The connection classification and idle harness channels work in a child process with two pinned workers. |
| Pool sampling, timelines and optional dumps | [PoolSnapshot.cs](../tests/Keypaste.Core.Tests/PoolSnapshot.cs) | Queue/timer lateness while a wait is happening; dumps can identify occupied workers. |
| Failure counts | `bash scripts/probe-results.sh <failed-log>...` | Counts per test method from retained failed suite logs; unreadable input fails the reader. |
| CLI null-reference stacks | [ExceptionTrace.cs](../tests/Keypaste.Cli.Tests/ExceptionTrace.cs) | Optional first-chance stacks before the CLI converts an exception into a user-facing error. |
| Timeline correlation | `bash scripts/f9-timeline.sh <iteration-directory>...` | Instrumented intervals, overlap and same-process activity. At depth above one, pairing is ambiguous. |
| Save decomposition | [SaveClock](../src/Keypaste.Core/Internal/SaveClock.cs), `SaveTimingTests` in `Keypaste.Core.Tests` | Each save's check, redirect, gate wait, attempt work, retry waits, re-reads and stamp, and which operation held the gate; the tests prove a held gate and slow work read differently. |
| Save readings | `bash scripts/f9-timeline.sh --saves <iteration-directory>...` | Each labelled save's first interval split by component with the dominant one named, and the operations holding the gate while it waited. `gate=-` is a save that never took the gate; `gatedat` is the attempt it was taken before (D-0136). |
| Hosted comparison | [pool-probe.yml](../.github/workflows/pool-probe.yml) | The full CI test command at three pool floors on the selected Windows runner. |

Run the readers' offline fixtures before using new analysis code:

```bash
bash scripts/probe-results.sh --selftest
bash scripts/f9-timeline.sh --selftest
```

For the already-understood F.9 mechanisms, use the isolated regressions after building Release:

```bash
dotnet test tests/Keypaste.Mcp.Tests/Keypaste.Mcp.Tests.csproj --no-build -c Release \
  -- --filter-class 'Keypaste.Mcp.Tests.PoolShortageTests'
```

This regression arranges a shortage in a child process. Hosted load comparisons still use the full CI test command because narrowing the suite changes contention. Isolate new mechanisms once their preconditions are understood.

When a CLI test reports only a null-reference message from inside a save, capture the original throwing stack while retaining the full suite's startup and concurrency (build Release first):

```bash
KEYPASTE_TEST_EXCEPTION_TRACE="$PWD/artifacts/diagnostics/cli-exceptions" \
  dotnet test keypaste.slnx --no-build -c Release
```

The CLI test host creates `cli-null-reference-<pid>.log` in that directory. It records only exception types and stacks, without exception messages or argument values. The hook is disabled unless the variable is set and never runs in a shipped binary. Writing is best effort: an empty file means no stack was recorded; a missing file can mean tracing could not start. Retain the suite log beside it.

## Start with a short hosted sample

Use Git Bash on Windows, or Bash on macOS/Linux, with `gh` and `jq` available. Run from the repository root. The diagnostic workflow and scripts must already exist at the pushed branch or tag selected by `--ref`; the workflow must also exist on the default branch for dispatch to be available.

Vary only the worker floor. Keep the maximum and `DOTNET_PROCESSOR_COUNT` unchanged: they change worker injection and runtime parallelism, confounding the comparison.

```bash
probe_ref='YOUR_PUSHED_DIAGNOSTICS_BRANCH_OR_TAG'
probe_sha="$(git rev-parse "${probe_ref}^{commit}")"
printf 'Expected commit: %s\n' "$probe_sha"
gh workflow run pool-probe.yml --ref "$probe_ref" -f iterations=1
gh run list --workflow pool-probe.yml --event workflow_dispatch --limit 10 \
  --json databaseId,headSha,createdAt,status,url
```

Select the dispatched run with the expected `headSha` and set its ID below. One iteration produces four counted suite runs per arm across four shards. Each job also runs a preliminary suite to verify instrumentation, excluded from sample counts.

The workflow rejects iteration counts outside 1–100, runs the parser fixtures, and requires valid timeline JSON from both participating assemblies before sampling. Inspect the short sample's artifacts to confirm the marks needed for the question actually exist. That preflight establishes that the hosts wrote timelines; it does not establish that an as-yet-unmarked operation was timed.

## Retain and replay one run

```bash
probe_run='RUN_ID_FROM_LIST'
gh run watch "$probe_run"
evidence="artifacts/diagnostics/${probe_run}-$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$evidence"
gh run view "$probe_run" \
  --json databaseId,headSha,event,status,conclusion,createdAt,updatedAt,jobs,url \
  > "$evidence/run.json"
gh run view "$probe_run" --log > "$evidence/workflow.log"
gh run download "$probe_run" --dir "$evidence"
```

Download even when a job failed: the diagnostic error and raw logs are the evidence. Each artifact contains its ref/SHA, run attempt, arm/shard, requested count, runner image, SDK/runtime information and selected pool environment in `probe-logs/environment-*.txt`. The process-level pool readbacks remain in the timeline/failure diagnostics; requested environment values alone do not prove the runtime honored them. `results-*.tsv` records every completed iteration, its outcome and elapsed seconds. Check those tables against the requested count and all twelve job conclusions before using the denominator. A cancelled or incomplete shard is not a complete sample.

Replay the failure tally per arm from the downloaded raw logs:

```bash
set -euo pipefail
for arm in starved as-found relieved; do
  failed_logs=()
  while IFS= read -r -d '' log; do
    failed_logs+=("$log")
  done < <(find "$evidence" -type f -path "*/pool-probe-${arm}-*/probe-logs/*-run-*.log" -print0)

  if [ "${#failed_logs[@]}" -gt 0 ]; then
    bash scripts/probe-results.sh "${failed_logs[@]}" > "$evidence/shapes-${arm}.txt"
  else
    printf '%s: no retained failed logs; verify all jobs and result tables before recording zero.\n' "$arm"
  fi
done
```

The reader accepts plain, ANSI-colored and CRLF logs. It emits no partial tally and exits nonzero if an input is missing/empty, reports an unrecognized failure line, or has no readable failing test name. A host crash is incomplete measurement, not zero failing tests. Counts are failing test instances grouped by method, not failed-suite counts: one suite can contribute several methods.

For a failing iteration, compare its timeline with the retained passing control from the same arm and shard. Replace the example directory names with the artifact's actual iteration:

```bash
bash scripts/f9-timeline.sh \
  "$evidence/pool-probe-starved-1/probe-timeline/starved-1-1" \
  "$evidence/pool-probe-starved-1/probe-timeline/control-starved-1" \
  > "$evidence/timeline-comparison.txt"
```

If every iteration failed, record that no passing control exists. Overlap in a single trace does not establish causation. Stall-triggered dumps are stored beside timelines. Retain debugger/version, commands and output for stack analysis; this recipe installs no dump analyzer.

Keep downloads while investigating because GitHub artifacts expire. Commit a minimal sanitized fixture or deterministic regression once it explains the defect. Record lasting decisions and acceptance evidence in their owner documents with the commit and run ID.

## Spend a larger sample only on a defined question

Once the short sample proves the required evidence is present, use `iterations=20` if the question needs the original 80-run-per-arm comparison. Repeat the same dispatch/download recipe and compare named failure shapes at the recorded commits. Read a green probe as "the measurement completed": ordinary test failures are its result. Missing or unparseable evidence makes the job fail.

## Measure with the narrowest test that produces the condition

A mechanism a targeted test can reproduce is measured with that test; the full CI command is the confirming run (D-0135). F.9's starvation existed only under suite load. F.10a's gate contention did not: `SaveTimingTests` creates it in seconds, and one full-suite preflight was enough to read the decomposition.

To read save decompositions from a full suite, set `KEYPASTE_F9_TIMELINE` to a directory and read it with `--saves`. Every save writes a `save-timing` line, and `ASaveThatCannotSucceed_GivesUpQuickly` and `ADoomedSave_SpendsItsBudgetWhereThisSays` mark theirs with `save-op`. On Git Bash, pass that variable as a Windows path when a native launcher such as `cmd /c start` sits between the shell and `dotnet`; `MSYS_NO_PATHCONV` otherwise leaves a POSIX path that Windows resolves under `C:\c\`. A run built from uncommitted source retains `git diff`, `git status --porcelain` and the untracked instrument files beside its timelines, because the SHA alone does not identify it.
