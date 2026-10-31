# K.8 — Test classes run on the founder's machine

Completed 2026-10-31 at `7f0f93bd` on `k8`, source only; runs ci 41190890162 and app 41190894923 at that commit. The integrated commit differs from `7f0f93bd` only in this record.

## Amendments

- With the plan (founder, 2026-10-30): a `test` mode, verify.sh finding the pinned SDK in `~/.dotnet` for `test` and `compile`, and the founder's CLAUDE.md edit committed on its own first (`87ff3a56`).
- After the first build, which grew the code, the founder had K.8 remove what a machine with the SDK makes redundant (2026-10-31, D-0676): `dev.sh --relock` and dev.yml's relock job, `dev.sh --class` and dev.yml's class job, `dev.sh --linux` with dev.yml's ci and app callers, ci.yml's and app.yml's `workflow_call` and `linux_only` and the two `WorkflowRulesTests` that guarded it; and slim K.8's own code, dropping the check for programs the tests left running and the helper map, test projects building the programs their tests start through their own references.

## What changed

- `bash scripts/verify.sh test <class or tests/project>...` builds each named class's project, its namespace up to `.Tests`, and runs every class named in it with one `--filter-class`; a project named as `tests/<project>` runs whole. It sets `MSBUILDDISABLENODEREUSE` and stops the build servers on exit as `compile` does. `test` and `compile` take global.json's SDK from `~/.dotnet` when the `dotnet` on PATH lacks it, so CLAUDE.md's commands carry no `DOTNET_ROOT` prefix.
- Core.Tests, Mcp.Tests and App.Tests reference TxfContender, VaultSaver, PoolStarver, EnvReporter and the CLI with `ReferenceOutputAssembly="false"`, as Core.Tests already did EnvReporter, so `dotnet build` builds them and verify.sh's `runtime_helpers` is gone. The references leave every lock file unchanged.
- Lock files are regenerated locally with `dotnet restore --force-evaluate` (CLAUDE.md, README, CONTRIBUTING). `dev.yml` keeps its profile and acts modes; a failure only one runner shows goes through `dev.sh --profile`. backend's and desktop's `--project` and `--filter-class` are gone. The screen renderer runs locally with `test`.
- Against `main` at `87ff3a56`, outside this record: 18 files, 124 lines added and 430 removed.

## Evidence

- On the founder's Mac (macOS 26.6, arm64, PATH's SDK 10.0.401, SDK 10.0.302 in `~/.dotnet`), before and after the amendment, with the same counts:

  | Class | Total | Passed | Failed | Skipped |
  |---|---|---|---|---|
  | `Keypaste.Core.Tests.VaultBackupTests` | 23 | 23 | 0 | 0 |
  | `Keypaste.Cli.Tests.EnvExportTests` | 30 | 30 | 0 | 0 |
  | `Keypaste.App.Tests.Session.EnvLaunchThroughAppTests` | 28 | 28 | 0 | 0 |

  On CI, at `8341dfcd` through the then-current `dev.sh --class` on `macos-15`, each gave the same counts: dev 41153044246, 41153052693 and 41153060968. After the amendment, with the helpers' build output deleted first, the local run rebuilt every helper through the references and also passed `Keypaste.Mcp.Tests.PoolShortageTests` (2) and, with `EnvProfilesTests` beside `EnvLaunchThroughAppTests` in one run, 93. `ConcurrentVaultSaveTests`, first chosen for Core, skips its one test off Windows, so its run reported zero tests with exit 8.
- `pgrep` found no MSBuild node, compiler server or program from `artifacts/bin/` after each run.
- `dotnet restore --force-evaluate` over both solutions and the consistency project on the Mac left all 26 lock files unchanged, before and after the new references.
- `verify.sh compile` passed in 58 s without the SDK prefix; `WorkflowRulesTests` passed its remaining 5.
- ci 41153042960 and app 41153049781 at `8341dfcd`, before the amendment: every job passed but `keepassxc first run (macos-15)`, BACKLOG's former F.74, with its `Trace/BPT trap: 5` after the Qt font-alias warning.
- ci 41190890162 at `7f0f93bd` passed every job. app 41190894923 at the same commit failed `keepassxc first run (macos-15)` again as F.74, on its rerun too, and `desktop gates (windows-2025)`, whose rerun passed: in `verify-crash-report.sh` the UI-thread crash left two reports where one was expected. That job had passed in the 24 app runs before it, and nothing K.8 changed reaches the app, `AppDriver` or the crash code; the gate prints only the reports' names, so the second one's kind is unknown. This is its first occurrence (D-0673).

## Decisions

D-0676; D-0460 is retired, and D-0423's and D-0468's clauses naming the removed modes are rewritten.

## Limits and follow-ups

- Nothing checks that a test leaves no program running; Ctrl+C reaches the test's helpers with the test host.
- A platform-specific class is checked on another runner only through its whole profile, `dev.sh --profile <p> --os <runner>`, and the screen renders come only from this Mac.
