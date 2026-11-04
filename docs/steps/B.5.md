# B.5 — CI caches NuGet restores, and builds with the patched runtime

Completed 2026-11-04 in `9ae7e2ce`, source only; runs ci 41895008202, app 41895024991.

## Amendments

- **SDK 10.0.302 → 10.0.403.** GHSA-p3jp-9v83-76w9 (CVE-2026-111497, an out-of-bounds write in the runtime's Brotli decoder, published 2026-11-03) covers the 10.0.10 runtime packages 10.0.302 brings, and NuGet audit as an error failed every restore, `main`'s included (ci 41891448938). The founder chose to fix it inside B.5: `global.json` and the demo capture's image move to 10.0.403, whose runtime is 10.0.14, and every lock file was regenerated with `--force-evaluate`.
- **Merged before the macOS jobs finished**, by the founder's direction, which also replaces the branch-then-`main` double run with a fast branch tier and one full run on `main` in a later change.

## Evidence

- Only verification jobs cache: ci's and dev's dotnet jobs and app's but `package`, each keyed by OS, workflow, job (and dev's profile) and the hash of every `packages.lock.json`, with no fallback key, so a changed lock file restores fresh; `release.yml` and `package` restore from nuget.org, because NuGet checks a package already in its folder against the hash stored beside it (THREATS T-21).
- ci 41891448938 and app 41891465844 at `54813308`: every restore failed on the advisory; app was cancelled.
- ci 41895008202 and app 41895024991 at `9ae7e2ce`, every job a cache miss: every Ubuntu and Windows job passed, and macOS `compat` too; macOS `test` and `integration` never got a runner and were cancelled, and `keepassxc first run (macos-15)` failed as F.74 does. A miss costs about 1 s to look up and 6–20 s to save (integration ubuntu 6 s, test ubuntu 9 s, test windows 11 s, format 14 s, compat windows 20 s). Job times against `main`'s ci 41371800604 differ by the runners' apt mirrors, not the cache: KeePassXC's install took 289 s in aot and 122 s in compat ubuntu, against under 18 s.
- On the founder's Mac (macOS 26.6, arm64, SDK 10.0.403 from `~/.dotnet`): `verify.sh compile` passed; `verify.sh test Keypaste.Core.Tests.WorkflowRulesTests` passed.

## Limits and follow-ups

- A cache saved on a branch is not readable from `main`; the restore from a hit, and its job times, come from `main`'s second full run.
- `verify.sh compile` skipped the advisory: its restore was a no-op ("All projects are up-to-date for restore"), and a no-op restore runs no audit; a fresh restore of `8381d498` fails at once.
- `v0.3.0` was built with the affected runtime, and SECURITY lists 0.3.x as supported.
