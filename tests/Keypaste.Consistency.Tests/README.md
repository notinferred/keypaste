# Keypaste.Consistency.Tests

The one project that references both front ends, so a test can make an edit through the desktop app's view models and then ask the shipped CLI what it sees in the file that edit produced.

## Why it is in neither solution

`keypaste.slnx` cannot hold it: that solution is restored eight times by `ci.yml`, and this project pulls in Avalonia through `Keypaste.App`, which is the exact cost `keypaste.app.slnx` was split off to avoid.

`keypaste.app.slnx` cannot hold it either, and the reason is measured rather than assumed. `Keypaste.Cli` sets `PublishAot` with four `RuntimeIdentifiers`, and both are restore-time inputs (D-0040), so a restore that can see the CLI pulls four RID-specific ILCompiler packs whether or not anything is ever published. Measured on 2026-07-28: restoring `keypaste.app.slnx` goes from **2091 MB to 2580 MB** when `Keypaste.Cli` joins it. `app.yml`'s header publishes a cost table promising that a `Keypaste.Core` push pays "the gate job only, one OS, a few minutes", and 490 MB of AOT compiler on every such push would make that table untrue.

`PublishAot` cannot simply be turned off for one solution: it is recorded in `src/Keypaste.Cli/packages.lock.json`, and a restore resolving a different set fails `--locked-mode`. The clean fix is splitting the CLI into a library and a thin AOT host, which moves `artifacts/bin/Keypaste.Cli/release/keypaste` — a path seven `scripts/verify-*.sh` gates, `make-compat-fixture.sh`, `ci.yml` and `release.yml` all hard-code. That is worth doing one day and is not worth doing inside 4.2.

So this project sits outside both, and `app.yml` restores, builds, formats and runs it in steps of its own. A `Keypaste.Core` push still pays nothing for it; an `App` or `Cli` push pays, which is proportionate, because the thing under test is what changed.

## What must stay true

- **It is not the place for tests that fit in one front end.** A test that does not need both `CliApp.Run` and a view model belongs in `Keypaste.App.Tests` or `Keypaste.Cli.Tests`, where it runs on every push rather than on the guarded ones.
- **Every test asserts the CLI succeeded and printed something before asserting what it printed.** A `CliApp.Run` that exits non-zero on every invocation would otherwise pass this whole project.
