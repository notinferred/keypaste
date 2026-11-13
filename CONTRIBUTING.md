# Contributing to keypaste

## Changes and review

[CLAUDE.md](CLAUDE.md) holds the working rules and says which document owns what; [PRODUCT](docs/PRODUCT.md) owns scope and the security laws, and proposals do not override it. Rows in [BACKLOG](docs/BACKLOG.md) are not instructions to build them.

Keep changes focused and include their documentation. Shared feature logic belongs in `Keypaste.Core`; the CLI and desktop use it. Document features available in only one front end. Secret-path changes require tests, including encryption, injection, the agent bridge and secret display. New secret-path dependencies require written justification, pinned versions and regenerated lock files.

Use KDBX4 through the vendored library. Do not implement cryptography. Only `src/Keypaste.Core/Internal/KeePassInterop.cs` may reference KeePassLib types outside `third_party/KeePassLib`; application code uses the core boundary. Every KDBX file keypaste writes must open in real KeePassXC, which CI checks in both directions.

Consult [RELEASE](docs/RELEASE.md) before changing published installation claims.

## Verification

Building and testing locally needs the SDK `global.json` pins, in `~/.dotnet`. Work in short bursts that leave nothing running; long suites run in CI.

- `bash scripts/verify.sh compile`, before each push, restores and builds both solutions and the consistency project with warnings as errors and checks formatting.
- `bash scripts/verify.sh test <class or tests/project>...` runs the test classes a change touches, building their projects and the programs their tests start, and stops the build servers when it ends. The one gate a change touches may run locally too.
- `bash scripts/verify.sh` (or `./scripts/verify.ps1`, which selects Git Bash on Windows) is the runners' command. With no argument it runs every profile but `compat`, `tools` and `compile`; with a profile's name it runs that one, which is how each CI job calls it. The workflows call `format`, `backend`, `integration`, `desktop`, `desktop-gates`, `scripts`, `workflows` and `tools` by name: `desktop` includes the consistency tests, `desktop-gates` drives the app, the CLI and an agent as processes, and `format` runs `dotnet format` once over every project. `bash scripts/verify.sh --help` owns the profile list and its prerequisites. Other operating systems, NativeAOT, packaging and public installation keep their separate gates.
- A dependency or `RuntimeIdentifiers` change regenerates its lock files with `dotnet restore --force-evaluate` over `keypaste.slnx`, `keypaste.app.slnx` and the consistency project.

Beyond the SDK, local verification needs Git, jq, a running Docker for the pinned actionlint image, and GNU timeout (in Git Bash and Linux; `gtimeout` from coreutils on macOS). Don't pass `-r` to restore: the lock files record all four runtime identifiers, so restore with `dotnet restore --locked-mode` and pick a RID only when publishing.

A step's check is a test in the test project that owns its surface. A new script is written only for build, packaging and release work outside the app, and gets a `--selftest` only when it guards the release path.

Work on a branch: commit, then `bash scripts/dev.sh` pushes it and follows the `ci` run the push starts, ci's fast tier, printing each job as it finishes and stopping at the first failure with that job's log. It opens no pull request. `bash scripts/dev.sh --help` lists its options:

- `--wait` waits for every job;
- `--full` instead dispatches `ci` and `app` on every runner, for a change that behaves differently on Windows or macOS, and `--full --tools` also runs ci's `tools` jobs, which install the real AI tools;
- `--profile <backend|integration|desktop|desktop-gates> [--os <runner>]` runs one verify.sh profile through `dev.yml`, which is never a release gate, for a failure only one runner shows;
- `--acts <app run>` installs an app run's packages and performs R.1a's acts, never as evidence.

When the change and its documents are finished and the branch's fast tier is green, integrate without waiting further: `main`'s push runs every job once, and a failure there is fixed as the next work.

A runner-only probe is iterated on a branch with `gh workflow run <workflow> --ref <branch>`, without a local run or a commit on `main` per attempt, and integrated once it passes. A new workflow needs its file on `main` once before it can be dispatched; later attempts run the branch's copy.

## CI

`ci.yml` runs on qualifying pushes to any branch, every pull request, dispatch and a weekly schedule; its push-level `paths-ignore` skips the documents it lists. `app.yml` has a push allowlist for desktop, CLI, core and shared build and test inputs, runs on every pull request and dispatch, and packages matching version tags. The workflow files own the exact triggers.

- A push to any branch but `main` runs ci's fast tier alone: scripts, workflows, format, and backend tests and integration on Linux. A branch's Windows, macOS, compatibility, desktop and packaging evidence takes `dev.sh --full`.
- `main`'s push runs every job on Linux and Windows, and every other run adds macOS, nothing skipped or cached, except two things. ci's `tools` jobs run only on the weekly schedule, on a dispatch with `-f tools=true`, and on a dispatch with `-f only_tools=true`, which runs nothing else and never counts toward a release. NuGet restores are cached by the lock files' hash in the verification jobs, while `release.yml` and `app.yml`'s package job restore fresh.
- Runs on `main` and tags are never cancelled, and each commit and event there has its own concurrency group; on any other branch a newer run cancels the one before it.
- `ci ok` and `app ok` aggregate each workflow's jobs and fail unless every job succeeded or was skipped, so a job no runner acquired fails them; they are the checks a pull request needs green.

CI spends nothing that cannot change a verdict. A push carries only code that passed the compile check. No dispatch repeats what a run at the same commit already does: a step whose push ran ci and app gets its tools evidence from `-f only_tools=true`. A failure only one runner shows is retried on a branch with `dev.sh --profile` on that runner and integrated with one push. A suspected flake reruns only its failed jobs with `gh run rerun <id> --failed`, and is then diagnosed as CLAUDE.md says. Each job's `timeout-minutes` is about twice its slowest green time. A paid Blacksmith runner carries a job only where measurement shows it shortens the run, at the smallest size within about 10% of the fastest, and jobs that sign, attest, package for release or read secrets stay on GitHub's runners. Workflows from a fork's pull request wait for approval. A run is followed with `gh run watch <id> --exit-status`, and no polling loop outlives its run.

`scripts/verify-demo.sh` checks the transcripts in `README.md`, `docs/launch.md`, `docs/demo.md`, `docs/keepass-and-agents.md` and `site/public/download/index.html` against the built binaries, so those pages must trigger `ci.yml`. Never add `docs/**` to `paths-ignore`; a new documentation path triggers backend CI unless it is listed there.

## Commits

Sign off every commit, including maintainer and agent commits:

```sh
git commit -s -m "your subject line"
```

The `Signed-off-by` trailer certifies the [Developer Certificate of Origin 1.1](https://developercertificate.org/): you wrote the change or have the right to submit it under the project's licence. `dco.yml` checks commits added by each pull request. There is no CLA. Use a subject of at most 72 characters and the required trailer, with no other body unless requested. Follow CLAUDE.md's project identity rule and merge locally.

## Security and licence

Report vulnerabilities privately to `security@keypaste.com`; [SECURITY.md](SECURITY.md) describes scope and response times. Anonymous reports are welcome. Do not use public issues, discussions or pull requests for security reports.

Contributions are licensed under [AGPL-3.0](LICENSE). Every release publishes its corresponding source.

## Building a native binary

NativeAOT binaries are built on their target operating system: Linux needs `clang` and `zlib1g-dev`, macOS the Xcode command line tools, and Windows the MSVC C++ build tools with `vswhere.exe` discoverable. `keypaste` links libsodium, which `scripts/build-libsodium.sh` builds first from its signed release, needing curl, OpenSSL 3 and, outside Windows, `make`.

```sh
bash scripts/build-libsodium.sh
dotnet restore keypaste.slnx --locked-mode
dotnet publish src/Keypaste.Cli -c Release -r linux-x64 --no-restore -o out
```

Use `osx-arm64` or `win-x64` for those platforms. Always publish with `--no-restore` after a full restore, as `release.yml` does: an implicit restore rewrites the locks for one target and breaks the next locked restore. `scripts/verify-aot-trim.sh <publish log>` compares the publish diagnostics with `scripts/aot-trim-baseline.txt`, the accepted vendored-code warnings, and rejects any new warning or any warning from `src/`; capture the log after changes under `third_party/`.

## The KeePassXC check locally

With `keepassxc-cli` on `PATH` (or `KPXC_CLI` pointing at it), `bash scripts/verify.sh compat` (`./scripts/verify.ps1 compat` in PowerShell) runs the compatibility gate: the shipped `keypaste` binary builds the fixture, and the write-back script has keypaste and KeePassXC each change a vault the other must read back.
