# F.97 — A vault unlocks and saves on Linux with no ICU

Completed 2026-11-09 on `backlog-2026-11`, source only; the gate has not run, so there are no run IDs and the question stays open in BACKLOG until it passes.

## Amendments

The founder selected BACKLOG's no-ICU row for one CI check that a vault unlocks and saves, leaving the start's cost unmeasured.

## Evidence

`verify-no-icu.sh --unlock-and-save`, which `verify.sh desktop-gates` runs on Linux. On the runner, with ICU, the app driver makes a vault whose master password, titles and values are not ASCII. In `ubuntu:24.04`, which never had libicu and holds none after its two packages are installed, the driver, started through `IcuFallback` as the app is, unlocks that vault through the unlock screen, edits an item and saves, then unlocks again and adds an item. Back on the runner the driver opens the file and `keypaste show --reveal --json` must return every value exactly. `globalization`, a new driver act, reports whether the process can load ICU and how it compares text: `icu=none collation=ordinal` is required in the container and `icu=loads collation=linguistic` on the runner, so the check cannot pass with ICU present.

Here, on macOS, only: `verify.sh compile`, `bash -n` under bash 3.2, and the script with `docker`, `apt-get`, `ldconfig` and `setpriv` stubbed and the container half run under `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`, which passed. That exercises the invariant comparison and the script's flow, and not a process with no libicu.

## Limits and follow-ups

The driver is the app's view models and session on a framework-dependent build, not the published `keypaste-app`; the window on the published payload stays `verify-no-icu.sh`'s other leg. The reader, `keypaste`, is itself invariant, so the read-back with ICU is the driver's `open`.
