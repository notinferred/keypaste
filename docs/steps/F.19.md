# F.19 — Stop the env run gate failing on the expiry it found

Completed 2026-09-24 on `main` above `f51fe5d`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

The row was opened and selected on 2026-09-24 when the push of E.1a to F.17 turned `ci.yml` red on `main`, before F.2b3's observation could start.

**Build:** ci run 36040583797 at `f51fe5d`, `keepassxc compat (ubuntu-24.04)`, failed [verify-keepassxc-run.sh](../../scripts/verify-keepassxc-run.sh) with "the vault KeePassXC made carries no expiry" directly after `tr: write error: Broken pipe`; macOS and Windows passed the same commit. Match the expiry against the export held whole rather than a pipe `grep -q` can close, and give the gate's other pipelines the same shape where they end in an early-exiting reader. Traces to PRODUCT §4.5.

**Verify (V-F.19):** the gate passes in all three `keepassxc compat` jobs at the repair commit, and still fails, naming the expiry, when the fixture carries no expiry.

Amendment: the row as drafted made the negative control write only EXPIRED with `<Expires>False</Expires>`. VALID also expires (in 2999), so that fixture still carries an expiry, and the gate then fails later, at the naming check. The check asks whether KeePassXC kept any expiry, so the control writes both with `False`.

## What changed for users

Nothing. Only the gate changed.

## Evidence

**Mechanism.** The expiry check piped `keepassxc-cli export` through `tr -d '\t'` into `grep -q` under `set -euo pipefail`. `grep -q` exits at its first match. When the export is still being written, `tr` gets `SIGPIPE`, prints "write error: Broken pipe" and exits non-zero, and `pipefail` fails the whole pipeline, so the gate reported a missing expiry precisely when it found one. It depends on how much of the export `tr` has written when `grep` exits, which is why the same commit passed on macOS and Windows. The log line from `tr` is the observation: `grep -q` only closes its input early on a match.

**Repair.** The export is read into a variable, with its own failure named, and `grep -q` reads it from a here-string, which no writer can be cut off from. No other pipeline in the gate ends in a reader that can exit early; `kpxc` and the `tree` listing end in `tr`, which reads everything. No other `verify-keepassxc-*.sh` has the shape.

**Local, Windows 10 Pro 22H2 19045.6332, KeePassXC 2.7.10.** The gate passed. A copy whose fixture writes VALID and EXPIRED with `<Expires>False</Expires>` failed with "RUN GATE FAILED: the vault KeePassXC made carries no expiry". `bash scripts/verify.sh` ran workflows, scripts, backend and integration and passed in 285 s; desktop was skipped because no changed path maps to it. `KPXC_CLI=… bash scripts/verify.sh compat` then passed all ten gates on 2.7.10, RUN among them.

**Hosted.** ci run 36042741149, dispatched on the `f19-probe` branch at `941f4fa` (`f51fe5d` plus this repair): `keepassxc compat` passed on `ubuntu-24.04` (image `ubuntu24/20260920.314`, KeePassXC `2.7.6+dfsg.1-1build3`), `macos-15` and `windows-2025`, and the Ubuntu log has "RUN GATE PASSED" and no broken pipe. The branch was deleted once this evidence was kept.

## Decisions

None.

## Limits and follow-ups

The failure was seen once, and no probe repeated the old line to measure how often it failed; the repair removes the mechanism rather than lowering its rate. The same push also failed `DesktopApprovalTests.A_request_withdrawn_before_its_prompt_is_drawn_never_draws_it` on Linux in app run 36040583862, which is F.20 and is not examined here.
