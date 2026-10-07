# N.2 — Offer the person's KeePassXC database on first run

Completed 2026-09-29 on `main` above `2e8a5c5`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Verify (V-N.2):** real KeePassXC opens two databases and closes; with an empty `~/.keypaste`, the welcome lists exactly those two, the last active first, and unlocking one opens it. A listed path that has since been deleted is not offered, and an ini with a malformed line lists the rest. KeePassXC's files are byte-identical afterwards. The lock screen's automation tree names no agent, and its YubiKey control is under More options for a vault without a slot.

A reader shown only on a hand-written ini does not pass.

The founder selected N.2 with N.5 and N.4 on 2026-09-29, to be built whole and in that order, and approved a plan that settled what the row left open:

- "With no recent vault" means no vault in `recent.toml` still exists, which is exactly when the welcome shows; a recent vault that exists is selected and KeePassXC's file is not read.
- Where KeePassXC 2.7 keeps its local file is taken from its `Config.cpp`: Qt's `AppLocalDataLocation` on Windows, `CacheLocation` on macOS, and `GenericCacheLocation` plus `keepassxc` on Linux.
- The lock screen keeps naming a process that holds the vault when an unlock is refused, since that is the next step, but no longer says agents reach it there. Agents keeps the whole sentence.

## Evidence

Local, Windows 10 Pro 19045, KeePassXC 2.7.10, on `main` above `2e8a5c5`.

- **The reader.** [KeePassXcDatabasesTests](../../tests/Keypaste.Core.Tests/KeePassXcDatabasesTests.cs), 16 of 16:
  - the last active first, then the open ones, then the recent ones, each once across `/` and `\` forms;
  - a vault deleted since, and a relative path, left out;
  - a line that is not a setting, a broken section header and a line of invalid UTF-8 each cost only that line;
  - another section's keys ignored;
  - QSettings' quoted lists, `\x` escapes, `@Invalid()`, `@Variant`, `@ByteArray` and `@@`;
  - at most ten; a file past 1 MiB offers nothing;
  - the file is left byte for byte and untouched in time;
  - the locator on each platform, with `XDG_CACHE_HOME` taken only when absolute.
- **The screens.** [FirstRunTests](../../tests/Keypaste.App.Tests/ViewModels/FirstRunTests.cs), 6 of 6: the welcome offers KeePassXC's two databases, the last active first; choosing one selects it, unlocks it and records it in `recent.toml`, with KeePassXC's file unchanged; no file leaves the welcome as it was; a selected recent vault means KeePassXC is not read; a listed file that is not a vault is refused where it was chosen; neither subtitle speaks of agents; and More options hides the YubiKey for a vault without a slot but not for one with a slot. [LockScreenTreeTests](../../tests/Keypaste.App.Tests/Views/LockScreenTreeTests.cs), 3 of 3, walk the visible automation tree: the welcome names `work.kdbx`, Open another file… and Create a new vault… and no agent; the lock screen names no agent and no YubiKey control until More options is pressed through the window's hit-testing, then Unlock with a YubiKey too; a remembered slot shows it at once. `MaskedInputAutomationTests` admits the two new literal button names, `AppAuthorityTests` checks each screen's sentence, and `ScreenRenderer` drew the welcome with KeePassXC's list and a lock screen with More options, in both palettes, each with at most one amber element.
- **Real KeePassXC.** [verify-keepassxc-first-run.sh](../../scripts/verify-keepassxc-first-run.sh) makes three vaults with `keepassxc-cli`, has KeePassXC itself open them with `--pw-stdin` and close as a person closes it (SIGTERM, or WM_CLOSE on Windows), and checks that KeePassXC wrote the three keys. It deletes one vault. Then, through [Keypaste.AppDriver](../../tests/Keypaste.AppDriver/Program.cs)'s new `welcome` and `open-offered` acts on the unlock screen's view model with an empty `KEYPASTE_HOME`, the welcome lists exactly the two left, the last active first; choosing the first unlocks it; a copy of KeePassXC's own file with a line that is not a setting, a broken section header and invalid UTF-8 inserted lists the same two; and KeePassXC's files and both vaults hash as before. Its negative control is the same check failing with KeePassXC's file absent. KeePassXC runs as its own single instance with its own settings, so a KeePassXC already open is left alone: it passed locally with KeePassXC 2.7.10 running in the founder's session, which was untouched.
- **Where KeePassXC keeps its file.** `app.yml`'s new `first-run` job ran the gate on hosted runners from branch `n2-first-run`, run 36614156422 at `d9dc5a5`, with KeePassXC at its default locations and the driver finding the file by keypaste's own locator: on `windows-2025`, KeePassXC 2.7.12 wrote `%LOCALAPPDATA%\KeePassXC\keepassxc.ini`; on `macos-15`, KeePassXC 2.7.12 from Homebrew wrote `~/Library/Caches/KeePassXC/keepassxc.ini`; on `ubuntu-24.04`, KeePassXC 2.7.6 from apt wrote `$XDG_CACHE_HOME/keepassxc/keepassxc.ini`, with `XDG_CACHE_HOME` in the work directory. All three passed. The first run of the job, 36612520326 at `6c81f09`, passed on Windows and Ubuntu and failed on macOS before KeePassXC started, because bash 3.2 treats an empty array under `set -u` as unbound; the gate now expands its option arrays as that bash allows. In both runs the Ubuntu leg of the existing workflows gate failed at C.2's notes step with `tr: write error: Broken pipe`, as it did on `main` at `2e8a5c5` before this step; that failure is outside this step and was reported to the founder.
- **The command.** `bash scripts/verify.sh`, given every path the step changes, selected workflows, scripts, backend, integration and desktop and passed in 745 seconds: the backend suites ran 3,061 tests, 3,051 passed and 10 skipped; the desktop suite 819, 816 passed and 3 skipped (the renderer's draws, which need an output folder); Consistency 43 of 43. The renderer was run by hand with an output folder and passed in both palettes. `bash scripts/verify.sh compat` then passed every KeePassXC gate against KeePassXC 2.7.10 on Windows, the workflows gate and the new first-run gate among them.

## Decisions

- D-0377: with no recent vault that exists, the welcome offers the databases KeePassXC last opened, read only from three keys of KeePassXC 2.7's local `keepassxc.ini` where Qt puts it on each OS; keypaste never writes KeePassXC's files.

## Limits and follow-ups

- A portable KeePassXC and KeePassXC 2.6, which kept these keys in its roaming file, are not found.
- A KeePassXC database that needs a keyfile or a YubiKey opens once the person chooses them on the unlock screen: KeePassXC's `LastKeyFiles` and `LastChallengeResponse` are binary QSettings values the reader does not read.
- There is no test through `App.Launch`: the desktop tests share one isolated home across parallel tests, so whether the welcome shows at all would depend on what another test left in `recent.toml`. The one line that hands the located file to the screen is exercised by R.1a's first act on an installed app.
- Source only.
