# F.43 — Give the install gates' app the timeout they set

Found while finishing F.38 and finished in one step on 2026-10-01 at the founder's direction of 2026-09-30 that work finish with no new open steps, so it had no STEPS row; on `task/f43` above `ccb7cc1`, scripts and one guide. upgrade-desktop 36896400963 and 36896405912 at `1dee956` passed every check on `windows-2025` and `ubuntu-24.04`, and dev 36896396158 at `1dee956` passed the scripts' self-tests; the integrated commit differs from `1dee956` only in this record. final upgrade-desktop runs 36898196690 and 36898200118 and dev 36898203465 at `b0363ef`; ci 36995550842 and app 36995553700 passed at `0319b47` on `integrate2`, with dev 36995556633 (all lanes and gates on all three runners).

## Amendments

There was no row. Both gates wrote `idle_timeout_seconds = 28800` at the top of `app.toml`, where `Toml.TryParse` refuses a setting outside any `[[section]]`, so `AppSettings.TryLoad` gave the installed app the 300-second default. Since G.1 (D-0389) the app's first unlock may write `vault` into `app.toml`, which 4.7d's preservation check compares byte for byte. The brief was to put the timeout where the app reads it, establish whether the upgrade gate passes on `main`, decide what the check requires of `app.toml`, and pass both gates through their workflows.

- **The MSI log reader was repaired here.** This step found that the upgrade gate misreads the Windows installer's log, and the coordinator folded the repair into F.43 under the same direction: `msi_log_says` piped `tr -d '\0'` into `grep -q` under `set -o pipefail`, so when grep matched and exited while tr was still writing, tr failed on the closed pipe and the pipeline read as no match. It now runs `grep -iE … > /dev/null`, which reads to the end. It serves both the interrupted-install and the refused-downgrade checks.
- **The scripts were scanned for the same reader.** Every `| grep -q` or `-m` in `scripts/` and the workflows was read. No other one stops reading a large producer: the rest take a `--version` line, a `tail -1` line, a few audit lines, a small test vault's `keepassxc-cli ls`, `mount`, `find -quit`, a commit's trailers, or a short string or self-test message through `printf`. `verify-release-matrix.sh:1629` reproduces the race on purpose as a control and was left.
- **[desktop.md](../desktop.md)'s checklist item 10** now says to set the timeout under `[[settings]]`, since the same line at the top of a new file is ignored.
- `install-desktop.yml` could not be run; see Limits.

[exercise-desktop-install.sh](../../scripts/exercise-desktop-install.sh) and [exercise-desktop-upgrade.sh](../../scripts/exercise-desktop-upgrade.sh) now write `[[settings]]` above the timeout, so the installed app runs at eight hours, and in the install gate a candidate built after G.1 records the gate's vault in the gate's own `KEYPASTE_HOME` at its first unlock.

## Evidence

Every upgrade run used the published CLI/MCP 0.3.0 on `windows-2025` and `ubuntu-24.04`.

- **Before**, 36882262351 at `ccb7cc1`: Linux passed every check. Windows passed install-lower, fixture, upgrade and data-after-upgrade, with `app.toml` at `95b04a51…` after the upgrade and the interrupt as at the baseline; interrupted was unreached, "the log names no failed action", and the last two checks with it.
- **The timeout**, 36883004072 at `d330291`: every check passed on both, and the new `app.toml`, `d2e3152f…`, was identical after the upgrade, the interrupt and the uninstall.
- **`app.toml` negative control**, 36883054900 at `1daea8a`, whose `upgrade()` then reset `app.toml` to `idle_timeout_seconds = 300` on Windows and deleted it on Linux: data-after-upgrade was a contradiction on both, `d2e3152f…` to `585b92bc…` and to `absent`. The probe is not in the integrated commit.
- **The reader.** 36882262351 and both attempts of 36890049032 at `034454a` recorded `interrupted_failed no` from Windows logs naming `Action ended …: InstallFinalize. Return value 3.` at line 1446 or 1447; 36883004072 read the same 604,272-byte log as yes. Over a downloaded log, the old pipeline failed 200 of 200 times on macOS and matched 200 of 200 without `-q`.
- **Regression**, dev 36894022710 at `eac91e6`, the new self-test cases over the old reader: 3 of 48 cases failed, the logs naming an InstallFinalize failure, an InstallFiles failure and the refused downgrade each read as no, with `tr: write error: Broken pipe`. The three cases that must read no passed: a log whose install ended with `Return value 1`, one without the downgrade message, and no log. Each fixture log is UTF-16 with two megabytes after its line.
- **Repair**, dev 36896396158 at `1dee956`: `exercise-desktop-upgrade.sh --selftest` 48 cases, `exercise-desktop-install.sh --selftest` 33, actionlint and shellcheck, and `WorkflowRulesTests` 3. upgrade-desktop 36896400963 and 36896405912 at `1dee956`: on Windows all seven checks passed in both, with the interrupt and the refused downgrade read from their logs; on Linux six passed and downgrade-refused was not applicable.

## Decisions

None. 4.7d's rule is kept: its check still requires `app.toml` byte-identical across every transition.

## Limits and follow-ups

- **`install-desktop.yml` was not run.** It takes a tag whose CLI/MCP is in `release-targets.json` `published[]` with provenance, and that tag's app.yml run, whose `app-win-x64` and `app-linux-x64` artifacts [verify-desktop-candidate.sh](../../scripts/verify-desktop-candidate.sh) checks against an app.yml attestation for `refs/tags/v<version>`. app.yml keeps them seven days, and the runs for `v0.3.1-rc.2` (35247267811) and `v0.3.0` (35046281067) hold none; dispatch 36890084619 with `v0.3.1-rc.2` and 35247267811 stopped at "Artifact not found for name: app-linux-x64", and the same for `app-win-x64`. Running it needs a founder action: push a new tag, whose app.yml run makes attested candidates and which also starts `release.yml`, or re-run app.yml run 35247267811, which rebuilds and re-attests `v0.3.1-rc.2`'s candidates and which GitHub allows until 2026-10-17; then dispatch install-desktop with that tag and run. Until then the install gate's change is unverified on a runner.
- 4.7d's run 35284227304 compared an `app.toml` the app reads as the defaults and, under D-0028, never rewrites, so that identity could not have caught a rewrite by the app; these runs compare one it reads.
- The upgrade gate never opens the upgraded app's window, so a settings migration at the first launch after an upgrade is outside it, as before.
- The scan covered `grep` readers. `| head` readers stop early the same way; those seen take small outputs or end in `|| true`, and they were not reviewed one by one.
