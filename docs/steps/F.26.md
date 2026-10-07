# F.26 — Read KeePassXC's export from a file in the workflows gate

Completed 2026-09-29 on `main` above `2631e99`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

The founder asked for the fix on 2026-09-29, after N.2's record reported the failure, so it had no STEPS row.

`app.yml`'s `keepassxc workflows (ubuntu-24.04)` job had failed at C.2's notes step since C.2. The job printed `tr: write error: Broken pipe` and exited 1 with no gate message. Three runs showed it:

- run 36607877850 on `main` at `2e8a5c5`;
- runs 36612520326 and 36614156422 on `n2-first-run`.

The same job passed on `windows-2025` in all three, and on `ubuntu-24.04` at `c23574f`, before C.2.

## Evidence

**Mechanism.** The gate's `kx` wrapper ends in `tr -d '\r'`. C.2's `revisions()` piped `kx export` into an `awk` that `exit`s once it has counted the entry's revisions, and the notes step piped another export into `grep -qF`. Both readers can close the pipe before the export is written. On Linux, where the vault's XML export, with its attachments, is larger than the pipe buffer, `tr` then fails on the closed pipe; under `pipefail` the pipeline fails, and `set -e` ends the gate at `listed=$(revisions …)`. On Windows the same pipeline returned 0.

A preflight in `ubuntu:24.04` with a 400 KB producer through `tr` returned 141 when `awk` exited after two lines, and 0 when the same read came from a file.

**Repair.** Both places write the export to a file in the gate's directory and read it from there, as `check_intact` already did. N.4's templates step counts revisions through the same function, so it now runs on Linux too.

**Runs.**

- **Local, Windows 10 Pro 19045:** the workflows gate passed against KeePassXC 2.7.10.
- **Hosted:** `app.yml` run 36623590468 at `03afd6d`, dispatched on branch `f26-workflows-pipe`, passed every job. `keepassxc workflows (ubuntu-24.04)` passed the notes step and N.4's templates step on KeePassXC 2.7.6, the first hosted run of either on Linux, and `windows-2025` passed them on 2.7.12.
- **The command:** `bash scripts/verify.sh`, given the step's paths, ran workflows, scripts, backend and integration and passed in 293 s; the backend suites ran 3,101 tests, 3,091 passed and 10 skipped. Desktop was skipped because no changed path maps to it.

## Decisions

None.

## Limits and follow-ups

- Other gates pipe `keepassxc-cli` into readers that read to the end, and they pass on `ubuntu-24.04` in `ci.yml`; none was changed.
