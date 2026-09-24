# E.1b — Import and launch a project from the app

Completed 2026-09-24 on `main` above `43157cd`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Build:** the Env Sets screen previews an existing `.env` through the core parser (`DotEnv.cs`) and imports it into `env/<project>` with the existing store, showing each key and whether it is new or replaces one, never a value. A project maps a working directory and a command to one env set, and that mapping is saved without values. Run starts the command, and Open terminal starts the platform's terminal, in that directory. Both go through one core launch that E.1a's resolution feeds, moved out of the CLI's `Execution/` so both front ends share it. Each launch shows the exact command and directory and acts only on an explicit confirmation. Values reach the child's environment only: never its arguments, a file, the app's log or the saved project. A lock or a cancellation before the child starts releases nothing and starts nothing. Traces to PRODUCT §§1.4, 2 and 3.4.

**Verify (V-E.1b):** from the unlocked app, importing a `.env` stores exactly its keys, and the next `keypaste run` sees them. Running a mapped project starts a real child in the chosen directory that reports the set's values from its own environment, while its command line, the saved project and the app's output hold none of them and no file under the directory or the temp path contains one. A set E.1a refuses starts nothing and names the key and why. A lock between confirming and starting, and cancelling the confirmation, each start nothing. A launch observed only through the view model's command, or a child that reads a file instead of its environment, does not pass.

**Founder answers, given while planning:** Run opens the platform's terminal in the project's directory and runs the command there, leaving the shell open afterwards, so the app never captures what the child prints (D-0340). The mapping is kept on this machine, keyed by vault file and project, never in the vault (D-0339).

## What changed for users

- **Import .env** on an open Env Sets card picks a file, reads it through `DotEnv` (at most 1 MiB, every problem reported by line and rule, never the text) and plans it through the new `Core/EnvImport.cs`, which `keypaste env pull` now uses too. The preview lists each variable as `new`, `replaces the stored value, which stays in history` or `unchanged`, with the reader's notes about removed comments and unexpanded `${...}`. Nothing is written until Import; Cancel writes nothing. If the project changed since the preview, Import refuses and asks for the file again. Unchanged variables are not rewritten, so they cost no history slot. The file is left where it was: the app does not repeat `env pull`'s offer to delete it.
- **A project's directory and command** are saved with Save to `~/.keypaste/projects.json`, one per vault file and project. JSON rather than TOML because keypaste's TOML reader has no escapes and a Windows command needs quotes. The directory must be a full path that exists and the command one line of at most 4096 characters. An unreadable `projects.json` is left as it is and Save says so.
- **Run** and **Open terminal** work from the saved mapping. Each first shows a card naming the command (or, for Open terminal, the terminal program), the directory and the variables' names, and starts nothing until Start. The set comes from `AppVaultSession.Environments`, so E.1a's refusals apply: an expired entry or a bad name is refused before the card appears, naming each entry and why, and a lock while the card is up or before the resolver commits starts nothing. The released set goes only into the terminal's environment. Windows starts `%ComSpec% /s /k "<command>"` (Open terminal: `%ComSpec%` alone); Linux starts the first of `x-terminal-emulator`, `gnome-terminal`, `konsole` and `xterm` on `PATH`, running `/bin/sh -c 'eval "$1"; exec "${SHELL:-/bin/sh}"' keypaste <command>` so the command is an argument and never script text; macOS shows that the app opens no terminal there. After the command exits the terminal stays at the person's shell.
- **`keypaste run` is unchanged for users.** Its environment merge, child types and launch now live in `Core/Launch/`, shared with the app; its signal handling and attached launcher stay in the CLI.

## Evidence

**Tests:**

- `EnvLaunchTests`, 12 cases in core: a released set is merged over the parent into the environment and appears in no argument; a set with any other outcome throws before the launcher is called; Windows runs the command through cmd's verbatim command line and opens it bare; each Linux emulator gets the command as the last argument of the fixed script, with its own exec flag; the first emulator found wins and Open terminal passes only the working-directory option; no emulator, a missing or relative directory, a blank command and an unsupported platform each plan nothing.
- `EnvImportTests`, 4 in core: the plan names each key as new, replacing or unchanged; applying writes the new and replaced variables, keeps the old value in history and adds no revision to an unchanged one; case collisions in the file or against the vault refuse the whole import before anything is written; a file with problems is never planned.
- `ProjectMappingsTests`, 8 cases in core: a mapping with quotes and backslashes round-trips and replaces the one before it; the file holds only `vault`, `project`, `directory` and `command`; an unreadable file is reported and left byte for byte; a malformed mapping is skipped and the rest kept; the directory and command rules.
- `EnvLaunchThroughAppTests`, 9 in the app, through the Env Sets view models, `AppVaultSession.Environments` and the real `DetachedProcessLauncher`:
  - Run starts a **real child**, `tests/Keypaste.EnvReporter`, in a terminal. On Windows the terminal is the real `cmd.exe`; on Linux it is a stand-in emulator script that runs its `-e` command as an emulator does. The child reports over a named pipe its working directory, which is the chosen directory, both values from its own environment, and its command line, which holds neither. `projects.json`, the screen's notice, error and confirmation strings, and every file changed since the test began under the project directory, keypaste's home and the temp path hold none of the values.
  - Open terminal starts the terminal in the directory with the set in its environment and none in its arguments.
  - Cancelling the card starts nothing.
  - A lock while the card waits withdraws it and starts nothing.
  - A lock between Start and the resolver's second read starts nothing. The test holds the continuation after Start in a queued synchronization context, locks, and then runs the queue.
  - A set with an expired entry and an invalid name is refused before anyone is asked, naming both, starting nothing and showing no value.
  - An import stores exactly the file's keys and replaces the changed one, and its preview shows no value.
  - A previewed import writes nothing until confirmed.
  - A file with a problem imports nothing and names the line but not the text.
- `ImportedDotEnvIsVisibleToTheCliTests`, 1 in the consistency suite: a `.env` imported on the app's screen is listed by `keypaste env ls` and is exactly what `keypaste run` injects.
- The existing `RunCommandTests` and `EnvPullTests` pass unchanged: 49 tests after the move.

**Mutations**, each restored afterwards:

| Mutation | Result |
|---|---|
| The launch resolves with no confirmation | 5 app tests fail |
| The set is released before the person answers, and the card is asked afterwards | 2 app tests fail (the lock-while-waiting test, which now fails in 30 s rather than hanging, and the lock-between test) |
| `EnvLaunch` also puts `KEY=value` into the child's arguments | 1 core test and the app's Open terminal test fail. The app's Run test does not, because `cmd.exe` takes a raw command line that ignores the argument list. |
| Previewing an import writes it | 2 app tests fail |

The first mutation run left a `cmd.exe` holding the test run's console, because the assertion that failed came before the test ended the process. The test class now ends every process it started when it is disposed.

**Verification:** `./scripts/verify.ps1` on the finished code and documents, before this record was written, first failed in backend and desktop on `dotnet format`: new private constants lacked the `_` prefix and two using blocks were out of order. The formatter also rewrote a vendored KeePassLib file, which was restored. Resumed with `--from backend`, it passed backend, integration and desktop in 510 s, after workflows and scripts had passed in the first run. Backend ran 1,812 tests, 10 of them skipped on Windows, and none failed. Desktop ran 522 app tests and 41 consistency tests, none failing. `compat` was not run: this step changes no KDBX read or write, and its import goes through the existing `EnvStore`. macOS and Linux runs come from CI.

## Decisions

[DECISIONS](../../DECISIONS.md) holds D-0339 and D-0340. Nothing else binds only this step's code.

## Limits and follow-ups

- **Linux emulators.** No real Linux terminal emulator was driven: the runner has no display, so the Linux case of the app test runs a stand-in for `x-terminal-emulator`. Whether a given emulator honours the working directory and passes the environment through is observed only when R.1a runs the installed AppImage on a real desktop. `gnome-terminal` and `konsole` are given the directory explicitly because a terminal server started earlier would not share it.
- **Windows console.** On Windows the terminal is `cmd.exe` in its own console, or in Windows Terminal where that is the default console host. There is no choice of PowerShell or another shell.
- **The real app was not clicked through.** The checks in [desktop.md](../desktop.md#checking-a-build-by-hand) items 49 and 50 were not run in this session.
- **The commit before the start.** A lock after the resolver commits and before `Process.Start` returns does not recall the terminal. E.1a's commit point is where the lock boundary sits, as it is for an agent's release.
- **After the start.** A started terminal keeps the set after a lock (PRODUCT §2). `projects.json` can be edited by any program running as the person, which is why the card names the command it will run.
- **Imported files.** The app leaves an imported `.env` in place. Deleting it, with `env pull`'s warnings, is still the CLI's.
