# Contributing to keypaste

[SECURITY](SECURITY.md#security-laws) holds the security laws. No proposal overrides them. The [code map](docs/ARCHITECTURE.md) shows where each concern lives.

## Rules for a change

- Keep a change focused and include its documentation. Document a feature only one front end has.
- Put shared feature logic in `Keypaste.Core`. The CLI and the desktop use it.
- Changes on the secret path require tests: encryption, injection, the agent bridge and secret display.
- Use KDBX4 through the vendored library, and implement no cryptography. Only files under `src/Keypaste.Core/Internal/` may reference KeePassLib types outside `third_party/KeePassLib`.
- Every KDBX file keypaste writes must open in real KeePassXC. CI checks both directions.
- A new dependency needs a pinned version, regenerated lock files and its one-line reason beside it in `Directory.Packages.props`.
- A change's check is a test in the test project that owns its surface. A new script is only for build, packaging and release work outside the app, and gets a `--selftest` only when it guards the release path.
- Consult [RELEASE](docs/RELEASE.md) before changing a published installation claim.

## Setup

Clone with the submodules. KeePassLib with Twofish, `Keypaste.Infrastructure` and the extension are each pinned to a commit ([projects](docs/ARCHITECTURE.md#projects)).

```sh
git clone --recurse-submodules https://github.com/notinferred/keypaste
```

In a clone made without them, and after a pull that moves a pin, run `git submodule update --init`. A change to a submodule is made and merged in its own repository, then its new commit is pinned here.

You need:

- the .NET SDK that `global.json` pins, in `~/.dotnet`
- Git and jq
- a running Docker, for the pinned actionlint image
- GNU timeout: built into Git Bash and Linux, `gtimeout` from coreutils on macOS

## Verification

Work in short bursts that leave nothing running. Long suites run in CI.

| Command | When | Does |
|---|---|---|
| `bash scripts/verify.sh compile` | Before each push | Restores and builds both solutions and the consistency project with warnings as errors, and checks formatting |
| `bash scripts/verify.sh test <class or tests/project>...` | For the classes a change touches | Builds their projects and the programs their tests start, runs them, and stops the build servers |
| `bash scripts/verify.sh compat` | With `keepassxc-cli` on `PATH`, or `KPXC_CLI` pointing at it | The shipped `keypaste` builds the fixture, then keypaste and KeePassXC each change a vault the other must read back |
| `bash scripts/verify.sh <profile>` | The runners' command; each CI job calls one profile by name | Runs that profile |

On Windows, `./scripts/verify.ps1` takes the same arguments and selects Git Bash. The one gate a change touches may run locally too.

With no argument, `verify.sh` runs every profile but `compat`, `tools` and `compile`. The workflows call `format`, `backend`, `integration`, `desktop`, `desktop-gates`, `scripts`, `workflows` and `tools`. `desktop` includes the consistency tests, `desktop-gates` drives the app, the CLI and an agent as processes, and `format` runs `dotnet format` once over every project. `bash scripts/verify.sh --help` owns the profile list and its prerequisites. Other operating systems, NativeAOT, packaging and public installation have their own gates.

### Lock files

Restore with `dotnet restore --locked-mode`. Don't pass `-r`: the lock files record all four runtime identifiers, so pick a RID only when publishing.

A dependency or `RuntimeIdentifiers` change regenerates the lock files with `dotnet restore --force-evaluate` over `keypaste.slnx`, `keypaste.app.slnx` and the consistency project.

## Pushing a branch

Work on a branch and commit. `bash scripts/dev.sh` pushes it and follows the `ci` run the push starts, ci's fast tier. It prints each job as it finishes and stops at the first failure with that job's log. It opens no pull request.

| Option | Use |
|---|---|
| `--wait` | Wait for every job |
| `--full` | Dispatch `ci` and `app` on every runner, for a change that behaves differently on Windows or macOS |
| `--full --tools` | Also run ci's `tools` jobs, which install the real AI tools |
| `--profile <backend\|integration\|desktop\|desktop-gates> [--os <runner>]` | Run one verify.sh profile through `dev.yml`, for a failure only one runner shows; never a release gate |
| `--acts <app run>` | Install an app run's packages and perform the desktop acts; never evidence |

`bash scripts/dev.sh --help` lists every option.

When the change and its documents are finished and the branch's fast tier is green, integrate without waiting further. `main`'s push runs every job once, and a failure there is the next work.

Iterate a runner-only probe on a branch with `gh workflow run <workflow> --ref <branch>`, with no local run or commit on `main` per attempt, and integrate once it passes. A new workflow needs its file on `main` once before it can be dispatched; later attempts run the branch's copy.

## CI

| Workflow | Runs on |
|---|---|
| `ci.yml` | Every push to any branch, every pull request, dispatch and a weekly schedule, so `ci ok` reports on every commit |
| `app.yml` | Pushes that touch desktop, CLI, core or shared build and test inputs, every pull request and dispatch; packages matching version tags |
| `install-desktop-main.yml` | Each successful `app` run on `main`: installs that run's Linux package and performs the install exercise and the desktop acts as a rehearsal. Windows and macOS are dispatched by hand. |

The workflow files own the exact triggers.

- **A branch push** runs ci's fast tier alone: scripts, workflows, format, and backend tests and integration on Linux. Windows, macOS, compatibility, desktop and packaging evidence for a branch takes `dev.sh --full`.
- **`main`'s push** runs every job on Linux and Windows, and every other run adds macOS, nothing skipped or cached, with two exceptions. ci's `tools` jobs run only on the weekly schedule and on a dispatch with `-f tools=true` or `-f only_tools=true`; the last runs nothing else and never counts toward a release. NuGet restores are cached by the lock files' hash in the verification jobs, while `release.yml` and `app.yml`'s package job restore fresh.
- **Order.** ci's `checks` runs first. The jobs that build and test start once it passes, beside `format`. Each builds once per runner and runs everything that build serves, as `app.yml`'s desktop job does.
- **Cancellation.** Runs on `main` and tags are never cancelled, and each commit and event there has its own concurrency group. On any other branch a newer run cancels the one before it.
- **Required checks.** `ci ok` and `app ok` aggregate each workflow's jobs and fail unless every job succeeded or was skipped, so a job no runner acquired fails them. They are the checks a pull request needs green.
- **Fork pull requests** wait for approval before their workflows run.
- **Desktop exercises** drive the app's controls by automation ID, a control's `x:Name` or a generated row's AutomationId, through `scripts/lib/desktop-drive.sh`.

### The changes filter

On a push that changes only the documents ci's `changes` job lists, or the extension, every job but `checks` is skipped. `scripts/verify-demo.sh` checks the transcripts in `docs/demo.md` and `docs/keepass-and-agents.md` against the built binaries, so those pages stay out of the list. Never add `docs/**` to it. A new documentation path runs the backend jobs unless it is listed there.

### Spending

CI spends nothing that cannot change a verdict.

- A push carries only code that passed the compile check.
- No dispatch repeats what a run at the same commit already did. A change whose push ran ci and app gets its tools evidence from `-f only_tools=true`.
- A failure only one runner shows is retried on a branch with `dev.sh --profile` on that runner, then integrated with one push.
- A suspected flake reruns only its failed jobs with `gh run rerun <id> --failed`, and is then diagnosed, since a passing retry does not close an intermittent defect.
- Each job's `timeout-minutes` is about twice its slowest green time.
- A paid Blacksmith runner carries a job only where measurement shows it shortens the run, at the smallest size within about 10% of the fastest. Jobs that sign, attest, package for release or read secrets stay on GitHub's runners.
- Follow a run with `gh run watch <id> --exit-status`. No polling loop outlives its run.

## Commits

Sign off every commit, maintainer and agent commits included:

```sh
git commit -s -m "your subject line"
```

The `Signed-off-by` trailer certifies the [Developer Certificate of Origin 1.1](https://developercertificate.org/): you wrote the change or have the right to submit it under the project's licence. `dco.yml` checks the commits each pull request adds. There is no CLA.

Use a subject of at most 72 characters and the trailer, with no other body unless requested. Project commits are authored as `keypaste <contact@keypaste.com>` and merged locally.

## Building a native binary

NativeAOT binaries are built on their target operating system:

| OS | Needs |
|---|---|
| Linux | `clang` and `zlib1g-dev` |
| macOS | The Xcode command line tools |
| Windows | The MSVC C++ build tools, with `vswhere.exe` discoverable |

`keypaste` links libsodium, which `scripts/build-libsodium.sh` builds first from its signed release. It needs curl, OpenSSL 3 and, outside Windows, `make`.

```sh
bash scripts/build-libsodium.sh
dotnet restore keypaste.slnx --locked-mode
dotnet publish src/Keypaste.Cli -c Release -r linux-x64 --no-restore -o out
```

Use `osx-arm64` or `win-x64` for those platforms. Always publish with `--no-restore` after a full restore, as `release.yml` does: an implicit restore rewrites the locks for one target and breaks the next locked restore.

`scripts/verify-aot-trim.sh <publish log>` compares the publish diagnostics with `scripts/aot-trim-baseline.txt`, the accepted vendored-code warnings, and rejects any new warning or any warning from `src/`. Capture the log after changes under `third_party/`.

## Security and licence

Report vulnerabilities privately to `security@keypaste.com`, never in a public issue, discussion or pull request. Anonymous reports are welcome. [SECURITY.md](SECURITY.md) describes scope and response times.

Contributions are licensed under [AGPL-3.0](LICENSE). Every release publishes its corresponding source.
