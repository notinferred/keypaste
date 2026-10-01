# F.40 — Run the desktop process gates on macOS

Completed 2026-10-01 on `task/f40` above `ccb7cc1`, scripts and documents only; dev run 36865305910 at `328e8f1`, auto with `--os all`, passed the nine gates on `macos-15`, `ubuntu-24.04` and `windows-2025`, and the squashed commit adds only documents to its scripts; final dev run 36871580142 at `6d0d1f4` passed the nine gates on all three runners; its first run there, 36867370896, failed on Windows in `EnvLaunchThroughAppTests`' cleanup, which F.42 repaired; ci 36995550842 and app 36995553700 passed at `0319b47` on `integrate2`, with dev 36995556633 (all lanes and gates on all three runners).

## Amendments

The row left the repair open: make the gates bash 3.2 compatible, or put a newer bash first where the workflows run them on macOS. The step made them compatible (D-0398). `ci.yml` already runs the integration and compat gates on `macos-15` under the runner's `/bin/bash` 3.2, `verify-keepassxc-projects.sh` among them with a fixed `exec 7> >(…)`, `app.yml` already avoids an empty array for bash 3.2, and CLAUDE.md names no newer bash among a contributor's requirements. A Homebrew bash in `dev.yml` would have made the desktop leg the only one that needs it, added an install to every macOS run, and left `verify.sh desktop` failing for a contributor on macOS with the pinned SDK.

## What changed

The nine gates open their long-lived inputs on fixed descriptors where they opened `{HOLD_IN}`, `{MCP_IN}` and `{AGENT_IN}`: 7 is the held app's input, 8 the open bridge's and 9 keypaste agent's, and one comment line in each gate names them. `verify-held-saves.sh` reads a verb's arguments with a `read` loop instead of `mapfile`.

Every process substitution now closes the descriptors its command must not inherit with a bare `exec N>&-`, then `exec`s the command, and `verify-run-session.sh`'s background run does the same. The closes are the ones each command had before; a substitution that closed nothing now only `exec`s its command, so no subshell outlives what it started.

No assertion, wait, pattern, refusal text or negative control changed. Read with the three names as 7, 8 and 9, the diff against `ccb7cc1` holds only the changes above.

## Evidence

- Dev run 36797268922 at `0f13b48`, the first probe, auto `--os all`, fixed descriptors only. The nine passed on Linux and Windows. On macOS the desktop suites passed (App.Tests 858 passed and 4 skipped, Consistency 44) and `verify-session-authority.sh` passed; `verify-lock-boundary.sh` then timed out waiting for `^shut down` after closing the app's input with a request waiting, because the bridge started for that request still held the app's input open, as the experiment below found.
- A local experiment under macOS's `/bin/bash` 3.2.57, with `sh` stand-ins and nothing built, found two leaks. `>(cmd 7>&-)` forks, and the substitution's subshell keeps 7 while it waits on `cmd`. `>(exec cmd 7>&-)` hands `cmd` the copy of 7 that bash saved before closing it, at descriptor 10 or above, because the saved copy survives `exec`. With either form the process reading 7 saw no end of input; with `>(exec 7>&-; exec cmd)` no copy remained and it saw the end at once. Saved standard output and error did not leak.
- Dev run 36865305910 at `328e8f1`, auto `--os all`, the runner as `dev.yml` sets it up: each of the nine gates printed its `ok:` line on all three runners, on macOS under `/bin/bash` 3.2 with no workflow change. `verify-lock-boundary.sh` ran keypaste agent's SIGTERM step on macOS and Linux and skipped it on Windows, as before. App.Tests passed 858 with 4 skipped on macOS and Linux and 859 with 3 skipped on Windows; Consistency passed 44 on each. No log carried a shell error or a killed job.

## Decisions

D-0398: the gates run under macOS's stock bash 3.2 with fixed descriptors, no `mapfile`, and a bare close before each substitution's `exec`; no workflow installs a newer bash.

## Limits and follow-ups

- Only `dev.yml` runs the desktop gates on macOS: `app.yml`'s desktop gate runs on Linux and `ci.yml` does not run them. A bash 4 construct added to these gates later fails only in a dev run with `--os macos`.
- Nothing checks the descriptor numbers: a gate that opens a fourth long-lived input must pick a number the others do not use and close it in the substitutions that must not hold it.
