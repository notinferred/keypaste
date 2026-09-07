# Keypaste.Consistency.Tests

This project references both front ends. Tests edit a vault through desktop view models, then invoke the CLI implementation through `CliApp.Run` to inspect the saved file. They verify shared behavior, not a downloaded executable, native GUI rendering or installation.

## Why it is in neither solution

Putting it in `keypaste.slnx` would bring Avalonia into ordinary backend restores. Putting it in `keypaste.app.slnx` would bring the CLI's AOT compiler packs into ordinary desktop restores: `PublishAot` and four `RuntimeIdentifiers` are restore-time inputs (D-0040).

A measurement on 2026-07-28 found desktop restore size increased from 2091 MB to 2580 MB when the CLI joined the solution. That historical measurement explains the separation; it is not a current benchmark.

`.github/workflows/app.yml` restores, formats, builds and runs this project in separate gate steps on every qualifying workflow run, including pushes that touch `Keypaste.Core`. After the gate, the workflow packages on three operating systems. Its push path filters, pull-request runs and tag triggers are documented in [CLAUDE.md](../../CLAUDE.md); the YAML is the executable authority.

To run the consistency gate locally with the repository's selected SDK:

```sh
dotnet restore tests/Keypaste.Consistency.Tests --locked-mode
dotnet format tests/Keypaste.Consistency.Tests --no-restore --verify-no-changes --severity warn
dotnet build tests/Keypaste.Consistency.Tests -c Release --no-restore
dotnet test tests/Keypaste.Consistency.Tests -c Release --no-build
```

## What must stay true

- A test that does not need both `CliApp.Run` and a view model belongs in `Keypaste.App.Tests` or `Keypaste.Cli.Tests`, so it runs with that front end's own checks.
- Every test asserts that the CLI succeeded and printed something before asserting what it printed. A CLI that always exits with an error must not make a cross-frontend test pass.
