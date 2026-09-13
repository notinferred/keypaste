# Keypaste.Consistency.Tests

This project references both front ends. Tests edit a vault through desktop view models, then invoke the CLI implementation through `CliApp.Run` to inspect the saved file. They verify shared behavior, not a downloaded executable, native GUI rendering or installation.

## Why it is in neither solution

Putting it in `keypaste.slnx` would bring Avalonia into ordinary backend restores. Putting it in `keypaste.app.slnx` would bring the CLI's AOT compiler packs into ordinary desktop restores: `PublishAot` and four `RuntimeIdentifiers` are restore-time inputs (D-0040).

A measurement on 2026-07-28 found desktop restore size increased from 2091 MB to 2580 MB when the CLI joined the solution. That historical measurement explains the separation; it is not a current benchmark.

`.github/workflows/app.yml` uses the shared `desktop` verification profile to restore, format, build and run this project and the desktop solution on every qualifying workflow run, including pushes that touch `Keypaste.Core`. After the gate, the workflow packages on three operating systems. Its YAML owns the exact triggers.

From the repository root, run the same desktop and consistency checks locally:

```sh
bash scripts/verify.sh desktop
```

In PowerShell, use `./scripts/verify.ps1 desktop`. The default `all` profile includes this project too; [CLAUDE.md](../../CLAUDE.md#local-verification-and-delivery) owns local verification requirements.

## What must stay true

Tests belong here only when they need both `CliApp.Run` and a desktop view model. Other cases belong in `Keypaste.App.Tests` or `Keypaste.Cli.Tests`. Assert CLI success and nonempty output before checking its contents, so an error cannot satisfy a cross-frontend check.
