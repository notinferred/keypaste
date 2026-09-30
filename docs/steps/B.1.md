# B.1 — The gates share one script library

Completed 2026-09-30, source only; its code ran at `7365a6a` in dev run 36728274698, ci 36728284946 and app 36728288438, and its desktop process gates ran on macOS in probe dev run 36740325891 at `959ac5a`.

## Amendments

- Binary resolution (`resolve`, `keypaste_bin`, `keypaste_mcp`, `app_driver`, `vault_restorer`) lives in `scripts/lib/common.sh` rather than `kpxc.sh`, because the process gates resolve the same binaries as the KeePassXC gates.
- `kpxc.sh` also holds `header`, `copies` and `entry_uuid`, which several KeePassXC gates defined for themselves; `make-compat-fixture.sh` sources `common.sh`.
- The selecting instruction added one sentence to CLAUDE.md's Local verification section: a step's cross-process check extends the gate that already covers its surface and sources `scripts/lib/`, and a script gets a `--selftest` only when it guards the release path.
- `app.yml` also pushes on `scripts/lib/**`, because the desktop process gates and the app-only KeePassXC gates source it. ARCHITECTURE names the library beside the gates.

## Evidence

- **What was shared.** Every function in the in-scope gates was extracted and its body hashed before unifying. Identical copies: `native` 17, `step` 14, `resolve` 13, `kill_process` 9, `wait_for` 8, `process_of` 7, `session_of` 6, `uuid` 4, `copies` 2. Trivially unified: `bytes` (8, three reading `$db` implicitly, plus 6 inline `od` calls), `entry_uuid` (2, both reading `$db` implicitly, now given the vault), `kpxc` (8, differing in spacing), `kx` (3, differing in a parameter name), `header` (2, one lowering case with `A-Z` rather than `[:upper:]`, plus 8 inline `od` calls), the 20 six-line blocks that found the CLI, restorer and app driver and the three shorter ones in `verify-log-chain.sh` and `verify-mcp-stdio.sh`, the projects gate's `binary`, `copies` in the keyfile gate (`ls` rather than `find`), `native` in `verify-mcp-run.sh` (`uname` rather than `cygpath`), `wait_for` in `verify-connect-client.sh` (60 s, now `WAIT_SECONDS=60`), and 30 `die` bodies that differed only in their prefix and the files they print (`DIE_PREFIX`, `DIE_FILES`).
- **Kept local, because they differ.** The workflows gate's `kx` follows the vault's current factors and replaces the library's; the lifecycle gate's status readers became `serving_session` and `serving_process`; `verify-mcp-run.sh`'s reply waiter became `wait_for_reply`; `digest`, `kpset` and every `cleanup`.
- **Lines.** 37 files, 330 added and 908 removed. The 14 KeePassXC gates lost 407 and gained 97; the nine desktop process gates lost 361 and gained 58; the eight integration gates lost 126 and gained 39; the library is 115 lines.
- **Messages.** Every assertion message and negative control is unchanged, and so is each gate's failure prefix and the files it prints on failure. `verify-mcp-stdio.sh` and `verify-log-chain.sh` label those files by path rather than "stdout" or "audit log", and a missing binary now names the variable that overrides it.
- **Offline, on macOS bash 3.2.** Each of the 14 KeePassXC gates exits 1 with "keepassxc-cli not found … must never be skipped" without it; the first-run gate fails naming KeePassXC's app when only the CLI exists; usage and a missing password keep their messages; `DIE_FILES` prints the files that exist; `wait_for` gives up after `WAIT_SECONDS`; `resolve` finds the `.exe` and prefers the variable. `verify-ci-scope.sh` passes 25 cases, two new: `scripts/lib/common.sh` selects rules, pages, integration, compat, aot, scripts, desktop and appcompat, and `scripts/lib/kpxc.sh` rules, compat, aot, scripts and appcompat.
- **Runs.** The first three tested `7365a6a`. The two probes on `task/b1-probe` tested the final scripts with `SkipWithoutTerminal()` added to F.28's four cases, and the second also had `dev.yml` put Homebrew's bash first on the macOS runner and raise the job limit to 45 minutes; neither change is in the final commit.

| Run | What it covers | Result |
|---|---|---|
| dev 36728274698, `--os all --gates both` | Scripts and workflows (shellcheck over `scripts/lib`, `bash -n`, the offline fixtures); per OS the backend and desktop suites, the nine desktop process gates, the integration gates and eleven KeePassXC gates | Scripts and workflows passed. `ubuntu-24.04` passed every step. `windows-2025` passed every step, then reached the job's 30-minute limit and was marked cancelled. `macos-15` failed four `EnvLaunchThroughAppTests` cases unrelated to scripts (F.28), so its desktop process gates, integration and compat steps did not run there |
| ci 36728284946, dispatched | Integration gates on three OSes, the KeePassXC compat gates on three OSes, and the AOT job's gates including `verify-keepassxc-xml-attach.sh` | All 8 jobs passed |
| app 36728288438, dispatched | `verify-keepassxc-first-run.sh` on three OSes, `verify-keepassxc-workflows.sh` on Linux and Windows, the desktop suite and its process gates on Linux | All 11 jobs passed |
| dev 36738332068 at `16ce05f`, `--os macos` | The macOS leg: backend and desktop suites, the nine desktop process gates, the integration and compat gates | The suites passed (App.Tests 833 passed and 10 skipped, Consistency 43). The first gate, `verify-session-authority.sh`, stopped at line 40 with `exec: {HOLD_IN}: not found` under the runner's bash 3.2 (F.40), so no later gate ran |
| dev 36740325891 at `959ac5a`, `--os macos` | The same leg under Homebrew's bash 5.3.15 | The macOS job passed every step in 26.5 minutes: the suites, all nine desktop process gates, the integration gates and the eleven KeePassXC gates. The run is marked failed only because actionlint's shellcheck flagged the probe's own line in `dev.yml` (SC2016); the scripts check passed |

## Decisions

None.

## Limits and follow-ups

- shellcheck covers `scripts/lib/*.sh`; the gates that source the library were outside its coverage before and still are.
- `dev.yml`'s compat step runs neither `verify-keepassxc-xml-attach.sh`, `-workflows.sh` nor `-first-run.sh`; the ci and app dispatches above ran them.
- On macOS the nine desktop process gates passed only in the second probe. They open descriptors with `exec {NAME}>`, which needs bash 4.1, as they did before B.1 (F.40), and dev's macOS leg stops at F.28 before reaching them; no other workflow runs them there.
- A dev run that selects everything outlasts `dev.yml`'s 30-minute job limit on `windows-2025` (F.39); in dev 36728274698 every step had passed first.
- GitHub's API refused this account's calls for about 45 minutes while several branches polled their runs, so `dev.sh` exited without reporting its run and the results above were read once the limit reset.
- The final commit adds these documents to the code the runs at `7365a6a` tested and removes one comment line from `verify-session-authority.sh`; the probes ran the final scripts.
