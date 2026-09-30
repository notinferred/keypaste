# Diagnose a failure without rebuilding the measuring tools

Record the failing behavior and the question the next observation must answer. Distinguish product failures, harness failures and missing evidence before choosing a repair. Retain the command, commit, environment, result and next question with the evidence.

## Use the retained instruments

| Instrument | Entry point | What it establishes |
|---|---|---|
| Arranged pool shortage | `PoolShortageTests` in `Keypaste.Mcp.Tests` | The connection classification and idle harness channels work in a child process with two pinned workers. |
| Pool sampling, timelines and optional dumps | [PoolSnapshot.cs](../tests/Keypaste.Core.Tests/PoolSnapshot.cs) | Queue/timer lateness while a wait is happening; dumps can identify occupied workers. |
| CLI null-reference stacks | [ExceptionTrace.cs](../tests/Keypaste.Cli.Tests/ExceptionTrace.cs) | Optional first-chance stacks before the CLI converts an exception into a user-facing error. |
| Save decomposition | [SaveClock](../src/Keypaste.Core/Internal/SaveClock.cs), `SaveTimingTests` in `Keypaste.Core.Tests` | Each save's check, redirect and stamp, and per attempt its gate wait, the operation holding the gate, its hold, re-read, work and following sleep; the tests prove a held gate and slow work read differently. |

The hosted pool comparison, its failure-count and timeline readers and the Transactional NTFS probe were removed once F.6, F.7, F.9, F.10 and F.12 closed; commit `2721661` holds their final versions, and their results live in the step records.

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

## Retain one hosted run

Use Git Bash on Windows, or Bash on macOS/Linux, with `gh` and `jq` available, from the repository root:

```bash
probe_run='RUN_ID'
gh run watch "$probe_run"
evidence="artifacts/diagnostics/${probe_run}-$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$evidence"
gh run view "$probe_run" \
  --json databaseId,headSha,event,status,conclusion,createdAt,updatedAt,jobs,url \
  > "$evidence/run.json"
gh run view "$probe_run" --log > "$evidence/workflow.log"
gh run download "$probe_run" --dir "$evidence"
```

Download even when a job failed: the diagnostic error and raw logs are the evidence. Requested environment values alone do not prove the runtime honored them. A cancelled or incomplete job is not a complete sample, and a host crash is incomplete measurement, not zero failing tests. Overlap in a single trace does not establish causation.

Keep downloads while investigating because GitHub artifacts expire. Commit a minimal sanitized fixture or deterministic regression once it explains the defect. Record lasting decisions and acceptance evidence in their owner documents with the commit and run ID.

## Measure with the narrowest test that produces the condition

A mechanism a targeted test can reproduce is measured with that test; the full CI command is the confirming run (D-0135). F.9's starvation existed only under suite load. F.10a's gate contention did not: `SaveTimingTests` creates it in seconds, and one full-suite preflight was enough to read the decomposition.

To record save decompositions from a full suite, set `KEYPASTE_F9_TIMELINE` to a directory. Each test process writes `f9-timeline-<pid>.jsonl` there; every save writes a `save-timing` line, and `ASaveThatCannotSucceed_GivesUpQuickly` and `ADoomedSave_SpendsItsBudgetWhereThisSays` mark theirs with `save-op`. On Git Bash, pass that variable as a Windows path when a native launcher such as `cmd /c start` sits between the shell and `dotnet`; `MSYS_NO_PATHCONV` otherwise leaves a POSIX path that Windows resolves under `C:\c\`. A run built from uncommitted source retains `git diff`, `git status --porcelain` and the untracked instrument files beside its timelines, because the SHA alone does not identify it.
