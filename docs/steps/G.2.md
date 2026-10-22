# G.2 — Connect every AI tool on this machine

Completed 2026-10-12 at `a02bb6f` on `task/g2`: the dispatched ci 38261386439, run with its `tools` jobs, and app 38261400810 passed every job on all three runners. The integrated commit differs from that tree only in this record.

## Amendments

The founder settled the row's decisions on 2026-10-10 while planning the 0.5.0 rows, and answered further questions during execution on 2026-10-10 and 2026-10-11. D-0435 to D-0438 record what it decided.

- **The tools and how each connects** (2026-10-10). Claude Code, Codex, Gemini CLI and Devin connect through their own `mcp add` and `mcp remove` at user scope. VS Code (its user `mcp.json`), Cursor (`~/.cursor/mcp.json`) and Claude Desktop (macOS and Windows only) connect by an edit of their file. The row had VS Code connect through its own command and Windsurf through a file edit: `code --add-mcp` has no remove, so VS Code moved to a file edit, and Devin, which Windsurf became, replaces it through its own command as a founder amendment. The ids are `gemini-cli`, `devin` and `vscode` (founder answer, 2026-10-10).
- **Gemini CLI** (2026-10-10). Its program goes before `--`, it takes `--scope user`, and keypaste's settings reach the bridge as arguments, because Gemini CLI strips variables whose names contain `KEY`. Setup says that Gemini CLI starts keypaste only in folders the person has trusted.
- **File edits** (2026-10-10). Before the first edit the file is copied to `<name>.keypaste-backup` beside it and that original is kept for good. Only keypaste's entry changes and everything else is kept; the write is atomic and the file is read back to confirm it parses. This reverses the catalog's "never writes their files" (D-0435). The plan review made each edit an `McpSetupPlan` step shown before it runs (D-0333).
- **One confirmation** (2026-10-10). One confirmation, or `--yes`, covers the whole run. Without a terminal and without `--yes`, setup changes nothing and says to pass `--yes` (D-0437).
- **Exit codes** (2026-10-10). The row said only that `setup` exits 0 when any tool is connected. It exits 0 when any tool was connected, removed or found not connected, 3 when none was found and 6 when every tool found refused (D-0437). As built, it also exits 2 when an AppImage's link step fails and 1 when it refuses before asking.
- **A stable path** (2026-10-10). The MSI and the macOS `/Applications` bundle already have fixed paths. From an AppImage, setup and Connect register G.5's link, `~/.local/bin/keypaste`, with `mcp`, creating it first when it is missing and saying so, and `AppRun` no longer dispatches `mcp` itself (D-0334, D-0431). A file keypaste did not write at that path is replaced only behind the run's confirmation and kept as `keypaste.keypaste-backup`, as Settings does (G.5's founder answer, 2026-10-10). When that backup already exists, setup refuses before asking, exits 1, names the backup and says keypaste never overwrites it (founder answer, 2026-10-11).
- **What a registration pins** (founder answer, 2026-10-10). The plan review had setup always pin `--vault`, `--audit-log` and `--approver` (D-0389), because Gemini CLI strips `KEYPASTE_*` and the bridge has no `--home`. The founder narrowed this: they are pinned only under a custom `KEYPASTE_HOME`, and `--approver` alone for a set `KEYPASTE_APPROVER`. A normal install registers no `--vault` and follows `keypaste use`. Under a custom home with no vault named or chosen, setup changes nothing and says to choose one.
- **Finding a tool's command** (2026-10-10). On Windows the npm `.cmd` launchers of `codex`, `gemini` and `code` are resolved to their target without passing arguments through a shell (D-0436). A macOS app started from Finder looks in each tool's documented install folders directly, with no shell (founder answer, 2026-10-10).
- **Claude Desktop on Windows** (founder answer, 2026-10-10). Setup edits the packaged (MSIX) app's file under `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude` when it exists, and `%APPDATA%\Claude`'s otherwise.
- **Removal** (founder answer, 2026-10-10). `setup --remove` first reads each tool's own configuration, read-only. A tool with no keypaste entry is "not connected" and counts as done, and a real failure shows the tool's own words.
- **Moved configuration files** (founder answer, 2026-10-11). When `GEMINI_CLI_HOME` is set, Gemini CLI's file is read at `$GEMINI_CLI_HOME/.gemini/settings.json`, as `CODEX_HOME` is modelled. A set `XDG_CONFIG_HOME` (Devin) or `VSCODE_APPDATA` or `VSCODE_PORTABLE` (VS Code) makes that tool's reading Unknown, so a removal never says "not connected" wrongly. The founder refined this later the same day: an `XDG_CONFIG_HOME` equal to `$HOME/.config` reads Devin's file normally, and any other set value stays Unknown. As built, the equal value counts only outside Windows, since Devin reads `%APPDATA%` on Windows, where any set value stays Unknown; the value is compared exactly once trailing `/` are trimmed (`34f31ab`).
- **Setup's wording** (founder, 2026-10-11). The new lines setup and Connect show stay as written; N.6 rewords them with everything else.
- **What setup registers** (2026-10-10). G.8 sets it: run available and wide reach, with the owner applying each agent's settings. G.2 registers the bridge arguments it did before (`mcp`, `--vault` per D-0389, `--client-label`, `--expose`) and adds only the pins above.
- **Proof by real tools** (2026-10-10). CI installs the real tools on all three runners, and each tool's own list or get command confirms the registration: Cursor's through its Agent CLI where that installs, VS Code's by comparison with the entry `code --add-mcp` writes into a throwaway profile, and Claude Desktop's at the file level only. The job runs on the weekly schedule and on a dispatch whose `tools` input asks for it, which `dev.sh --tools` and a release's dispatch set, and `require-green-gates.sh` requires it green for a tagged commit; ordinary pushes and `dev.sh` runs skip it. This amends D-0423 for this one job (D-0438). The installers are pinned by version, and by digest where the vendor publishes one, as D-0208 does for KeePassXC.
- **F.61 first** (2026-10-10). G.2's CI found the defect F.61 fixed: app 38077611239 at `19a7114` failed `verify-agent-activity.sh` in desktop gates on `macos-15` (job 114287762621), showing a request from "an unnamed client" whose client had named itself. A second dispatch at the same commit, app 38078506725, passed, and the failing observation was kept. The founder made it its own task, integrated before G.5 and G.2. This branch merged it through `task/g5` (`60d2ea6`, carrying `b900b6c`) before part 5's full run, and main at `e8b245d` (`41974da`) before its last.
- **First round of provisional choices** (confirmed 2026-10-11, all as taken). The agent took its recommended option on each and kept building:
  - `keypaste setup` does not look for app bundles in `/Applications`; only the macOS app does, so tests never see a developer's `/Applications`.
  - A set `XDG_CONFIG_HOME` made Devin's reading Unknown on every OS, which the refinement above narrowed.
  - A file tool whose file a variable moved, or that keypaste cannot locate, is found but skipped for connect and remove, and counts toward exit 6.
  - G.5's re-point check is split out of `KeepCurrent` as the init property `CliLinkStatus.Stale`, so G.5's constructors and `FakeCliLink` need no change.
  - `McpServerLocator.TryPin` returns false for a custom home with no vault and the CLI writes the message; `McpBridgeTargetResult` lives in the app and `ExitClientsRefused` in `CliApp`.
  - A removal from a tool with no keypaste entry prints "not connected; nothing to remove" once, in the step listing, and not again in the results.
- **Second round of provisional choices** (confirmed 2026-10-11, all eight as taken):
  - `verify-connect-client.sh` checks the bytes outside keypaste's member after Run it by dropping the inserted member's lines with an `awk` filter, the seed holding one member per line, and comparing the rest with the seed byte for byte.
  - `npm ci` installs Codex and Gemini CLI from a copy of `scripts/real-tools` in `$RUNNER_TEMP`, not in the checkout.
  - Cursor's Linux `.deb`, for which Cursor publishes no digest, is pinned by its versioned URL and the SHA-256 of the file, computed once by streaming it.
  - On macOS, Cursor and Claude Desktop come from the zips their Homebrew casks carry rather than the dmgs the design named; Windows versions come from winget's manifests, so they differ from macOS's.
  - The gate unsets `XDG_CONFIG_HOME`, which the Ubuntu image sets to its default, as on a normal account.
  - `gemini mcp list` runs with `GITHUB_SHA` unset: on a runner Gemini CLI 0.63 passes MCP servers only a few variables, dropping the `DOTNET_ROOT` the test build needs on macOS, and unset it behaves as on a person's machine, still dropping `KEYPASTE_*`.
  - Each command tool's `--version` runs once before the gate takes its byte snapshot, so the files a tool writes on its first run are not counted as changes.
  - Each tool's entry must match exactly the bridge, pinned vault, audit log, approver and the tool's id as its label, as setup's header prints them.
- **The Windows held-saves failure** (founder, 2026-10-11). It became its own task, [F.62](F.62.md), which found and fixed the cause; this record keeps the observation, and G.2 did not wait for it (Evidence).
- **No compatibility with earlier keypaste** (D-0416, confirmed 2026-10-10). Nothing migrates a registration an earlier internal AppImage made as `<image> mcp`, which no longer reaches the bridge; connecting again registers the link.

## What changed

- `Core/Clients/` gains:
  - `McpConfigFile`, which edits only keypaste's member of a tool's JSON file. Its scan refuses a duplicate key and text that does not decode. It splices the member in or out keeping every other byte and comments, checks the result parses and holds exactly that change, keeps the first original owner-only beside the path the tool reads through `AtomicFile.CommitWithoutReplacing` so a backup is never half-written or replaced, refuses a file that changed since it was read, replaces it in one rename that keeps its mode, and reads it back. Inserting and then removing gives back the original bytes.
  - `McpClientConfig` and `McpConfigLocation`, which read each tool's own file read-only and say whether it holds keypaste: Present, Absent, or Unknown when they cannot be sure. They cover Claude Code (`~/.claude.json`), Codex (`$CODEX_HOME/config.toml`, through a TOML scan), Gemini CLI, Devin and the three file tools, and a variable that moves a tool's file is handled as the amendments say.
  - `McpClientMachine`, `McpClientProgram` and `NpmCmdShim`, which find a tool's command only in PATH's absolute entries and, for the macOS app, its documented install folders, and start an npm launcher on Windows as `node.exe` with its script, or as the `.exe` it names, refusing any other batch launcher (D-0436).
  - In `McpClientCatalog`, the seven tools with their platforms, scope arguments, launchers, app bundles and the text each one expands. `McpClientSetup` composes a plan of tool commands, file edits and link steps (Create, Repoint and Replace over G.5's `ICliLink`), refuses a registration a tool would rewrite, and applies the plan. `McpServerLocator.TryForSetup` and `TryForDesktop` choose the bridge's path: on macOS resolved through links and refused when translocated or under `/Volumes`, from an AppImage `~/.local/bin/keypaste`. `TryPin` adds the pins.
- `keypaste setup` gains `--yes`, one question per run, the readings before a removal, the AppImage's link step, a `starts:` line per step in `--dry-run`, and exit codes 2, 3 and 6 beside 0 and 1.
- The app's Connect (`ConnectClientViewModel`, `ClientConnector`) offers the tools for this platform, previews and runs the same plans with the same pins, reads a tool's configuration before a removal, and reports the backup kept, a refused edit in keypaste's words and a failed removal in the tool's.
- `AppRun` dispatches only `cli`, and `verify-linux-appimage.sh` requires `AppRun cli mcp` to answer as the bridge.
- `verify-connect-client.sh` runs with its own home and gains a Cursor phase.
- CI: `scripts/real-tools/` pins Codex 0.162.1 and Gemini CLI 0.63.0 and every package under them by integrity; `install-real-tools.sh` owns every other version and SHA-256; `verify-real-tools.sh` is the gate and `tools` its profile in `verify.sh`; `ci.yml` gains the `tools` input and the `tools (<runner>)` jobs, which `ci ok` needs; `dev.sh --tools` dispatches ci with it; `require-green-gates.sh` counts a ci run only when it holds `tools` jobs and every one succeeded, with new cases and a second negative control in `verify-green-gates.sh`.
- CLAUDE.md, RELEASE, ARCHITECTURE, `docs/mcp-setup.md`, `docs/desktop.md` and FEATURES say what G.2 made false of them; DECISIONS and THREATS hold the rows below.

## Evidence

- Test classes, passing in ci 38191010100 (Core and CLI) and app 38191017390 (app) on all three runners except where a platform rule skips them:
  - New: `McpConfigFileTests`, `McpClientConfigTests`, `McpClientProgramTests`, `NpmCmdShimTests`, `McpServerLocatorTests`, `NpmShimLaunchTests` (Windows only, starting the npm launchers with awkward arguments) and `NodeScriptLaunchTests` (Linux and macOS). The launch tests fail rather than skip under `GITHUB_ACTIONS` when they find no `node`.
  - Rewritten or extended: `McpClientSetupTests`, `SetupVerbTests` (tools planted on a harness-only PATH under the harness's own home), `ConnectClientViewModelTests`, `AgentActivityViewTests`, `AtomicFileTests` (`CommitWithoutReplacing`) and `LinuxCliLinkTests` (`Stale`).
- `verify-real-tools.sh`, in ci 38191010100's `tools` jobs, with Claude Code 2.1.296, Codex 0.162.1, Gemini CLI 0.63.0, Devin 3000.11.3, VS Code 1.141.0, Cursor 3.24.12 (3.19.7 on Windows), Cursor's Agent CLI 2026.10.01-e373342, and Claude Desktop 2.31226.1 on macOS and its 2.19675.1 MSIX on Windows. On each runner, setup without `--yes` and without a terminal changed nothing; a removal before anything was connected called every tool not connected and changed nothing; `setup --yes` connected every tool; Gemini CLI started the bridge it registered, which took its home's settings from its arguments; each file started the bridge with the pinned settings and kept everything else; VS Code's own `--add-mcp` wrote the same entry from the same fields; keypaste's reader found the entry each tool wrote; the backups equalled the seeds; a second connect changed no entry and no backup; removal left no tool listing keypaste and gave back each seeded file; and a second removal called every tool not connected. On Windows the MSIX's package family was `Claude_pzs8sxrjxfjjc`, as the catalog has it.

| Runner | Tools connected, read back and removed | Read back by the tool's own command | Checked at the file level |
|---|---|---|---|
| `ubuntu-24.04` | 6 | Claude Code, Codex, Gemini CLI, Devin, Cursor's Agent CLI | VS Code |
| `macos-15` | 7 | Claude Code, Codex, Gemini CLI, Devin, Cursor's Agent CLI | VS Code, Claude Desktop |
| `windows-2025` | 7 | Claude Code, Codex, Gemini CLI, Devin | VS Code, Cursor, Claude Desktop |

- `verify-connect-client.sh`, in app 38191017390's desktop gates on all three runners: besides G.1's checks, Cursor's file changed only in keypaste's entry, its first original was kept, its registration reached the app's prompt, and removal gave back its bytes and then said it was not connected.
- Defects found in review, each shown failing on a control branch that carried the new tests over the old code (`b875d09`, since deleted):
  - A string in a tool's file that does not decode, such as invalid UTF-8 or half a surrogate pair (`"\ud800"`), threw `InvalidOperationException` out of the scan past every handler, so `setup` and the app crashed instead of leaving the file alone, and since a removal reads every tool first, one such file stopped the whole run. The scan now decodes every name and string, and such a file is reported as holding text that is not valid Unicode at a line and byte; the read-back uses the same scan (`2ca250f`).
  - A relative `XDG_CONFIG_HOME` was taken as unset, so on Linux keypaste edited `~/.config/Code/User/mcp.json` while VS Code, which resolves the value against its working directory, read another file. VS Code's location now reads the variable as `CODEX_HOME` and `GEMINI_CLI_HOME` are read, so a relative value means keypaste cannot tell where VS Code keeps its file (`c213561`).
  - THREATS, D-0438 and `ci.yml` said every tool read the registration back with its own command; they now name VS Code, Claude Desktop and Windows Cursor as checked at the file level (`04b6e12`).

| Test | Control (old code) | Fix |
|---|---|---|
| `McpConfigFileTests.A_file_whose_text_does_not_decode_is_left_alone_and_read_as_unknown` (4 cases) | dev 38189836532: failed on all three runners, 4 of 48 cases, three throwing `InvalidOperationException` and one reading Absent where Unknown was expected | ci 38189854634 and ci 38191010100: passed on all three |
| `McpClientConfigTests.Vs_code_on_linux_reads_its_file_in_xdg_config_home_and_a_relative_one_cannot_be_located`, `A_relative_xdg_config_home_makes_vs_codes_reading_on_linux_unknown` | dev 38189838635: failed on `ubuntu-24.04`, 2 of 96 cases | ci 38189854634 and ci 38191010100: passed on all three |
| `McpClientSetupTests.Vs_code_under_a_relative_xdg_config_home_is_found_but_not_edited` | dev 38189840379: failed on `ubuntu-24.04`, 1 of 44 cases | ci 38189854634 and ci 38191010100: passed |

- A gate defect. `verify-connect-client.sh` compares two paths on Windows through `cygpath`, which G.1 called as `cygpath -u -l`. That printed only a usage error, so both sides were empty and the check that the first unlock chose the vault it unlocked always passed on Windows. It now calls `cygpath -m -l` (`5a0832e`) and has passed on `windows-2025` in every app run since.
- An observation [F.62](F.62.md) closed. App 38170856945 at `5a0832e`: its first attempt failed desktop gates on `windows-2025` (job 114563062577) in `verify-held-saves.sh` with "keypaste lock … did not lock", although the agent's own log says it locked and stopped; the re-run passed. The path is `LockCommand`'s settle, which this branch does not touch. F.62 found the cause, the verb attaching again on the connection the agent was closing after its answer, a write that on `windows-2025` could take seconds to fail, and fixed it: the holder answers only once it has locked, and the verb sends nothing more (D-0434). This branch merged it from main before its final run.

| Run | Commit | What | Result |
|---|---|---|---|
| dev 38077310195 | `77f5e0b` | `McpConfigFileTests` | Failed to build on Linux and macOS, analyzer CA1711 on `AtomicFile.CommitNew`, renamed `CommitWithoutReplacing` in `19a7114`; cancelled on Windows by the next dispatch |
| dev 38077392506 | `19a7114` | `McpConfigFileTests`, all runners | Passed |
| ci 38077603971, app 38077611239 | `19a7114` | Full, part 1 | ci passed; app failed desktop gates on `macos-15`, the defect F.61 fixed |
| app 38078506725 | `19a7114` | app again | Passed, which does not show that defect absent |
| ci 38087274280, app 38087280113 | `677e132` | Full, part 2 | Passed |
| dev 38089100208 | `b391bcd` | `McpClientConfigTests`, all runners | Passed, 73 cases |
| ci 38089277910, app 38089285039 | `b391bcd` | Full, part 3 | Passed |
| dev 38141312891 | `33341ca` | `SetupVerbTests` | Failed to build: `SetupCommand` lacked the `using` for the CLI's option parser, added in `a3cfd6e` |
| dev 38141489520 | `a3cfd6e` | `SetupVerbTests` | Failed on `macos-15`: the tests expected `/var/folders/…` where the bridge's path resolves to `/private/var/folders/…`; they expect the resolved path from `5a6a182` |
| dev 38141842527, 38142010029, 38142183566, 38142334474, 38142658630, 38142836798 | `5a6a182` | `McpClientSetupTests`, `McpServerLocatorTests`, `SetupVerbTests`, `McpClientConfigTests`, `ConnectClientViewModelTests`, `AgentActivityViewTests` | Passed on all three |
| dev 38142495030 | `5a6a182` | `LinuxCliLinkTests` | Passed on Linux and macOS; on Windows every case skips, so the class run reported no tests and failed |
| ci 38143097500, app 38143105000 | `5a6a182` | Full, part 4 | Passed |
| dev 38167872293 | `2be3156` | `ConnectClientViewModelTests` | Passed on all three |
| ci 38168085883, app 38168093713 | `2be3156` | Full, part 5, with F.61 merged | Passed |
| ci 38170253756, app 38170260279 | `b778bd9` | Full | Cancelled by the next push |
| ci 38170848969, app 38170856945 | `5a0832e` | Full with `tools`, part 6 | ci passed, its `tools` jobs on all three; app's first attempt failed in `verify-held-saves.sh` on `windows-2025` ([F.62](F.62.md)), its re-run passed |
| dev 38186493557 | `34f31ab` | `McpClientConfigTests`, the XDG refinement | Passed on all three, 92 cases each |
| ci 38187481379, app 38187487663 | `41974da` | Full with `tools`, G.5 and main at `e8b245d` merged | Passed |
| dev 38189836532, 38189838635, 38189840379 | `b875d09` | The review regressions over the old code | Failed, as expected |
| ci 38189854634, app 38189862128 | `04b6e12` | Full, without `tools` | Passed |
| ci 38191010100, app 38191017390 | `04b6e12` | Full with `tools` | Passed every job on all three runners |
| ci 38261386439, app 38261400810 | `a02bb6f` | Full with `tools`, final, main at `d66ef5b` merged (G.5's squash, v0.5.0-rc.1's listing, F.62) | Passed every job on all three runners |

## Decisions

D-0435, D-0436, D-0437, D-0438; D-0333, D-0334, D-0389 and D-0431 amended; T-40.

## Limits and follow-ups

- Claude Desktop is checked at the file level only. A Windows MSIX install that has not yet run has no `LocalCache` file, so `%APPDATA%`'s is edited, which the packaged app reads while it has no copy of its own. No run shows either file loading in the app.
- VS Code never reads keypaste's entry in CI; its file is compared with the entry its own `code --add-mcp` writes. Cursor's Agent CLI reads the registration only on Linux and macOS, so Windows Cursor is checked at the file level.
- Whether Devin expands `${…}` in `command` and `args` is unverified, so `${` is refused for Devin as a precaution, which may refuse a path Devin would have kept.
- On Windows, pnpm's and yarn's launchers, and npm launchers that carry shebang arguments or variables, are refused naming the launcher; such a tool connects once installed with npm or its vendor's installer.
- The macOS app started from Finder finds a tool installed through a Node version manager only when that tool's folder is on the app's PATH; `keypaste setup` in a terminal whose PATH holds it connects it. `keypaste setup` on a Mac does not look in `/Applications`, so it does not find a file tool that has never run and has no launcher on PATH; the app does.
- A reading keypaste cannot be sure of is Unknown, so the tool's own removal runs and its failure is shown in its words even when the tool held no entry. A file tool whose file a variable moved, or whose file keypaste cannot locate, is found but skipped and counts toward exit 6. A file holding text that does not decode anywhere, another server's value included, is left alone.
- Without a terminal and without `--yes`, `setup --remove` exits 1 before reading anything, even when no tool holds keypaste, as the founder's confirmation rule says.
- A registration made under a custom home names its vault, so it stops following `keypaste use` until setup runs again.
- A bridge Gemini CLI starts under a custom `KEYPASTE_HOME` derives the vault key from the default home, because Gemini CLI strips the variable and the bridge takes no home. Since [G.9](G.9.md) the owner writes the line for every call it answers, keyed by its own home, so only a line the bridge writes itself, for a call it refuses itself or that nothing holding the vault answered, still carries the default home's key (D-0439).
- What remains of editing another program's file is T-40's.
- G.8 sets what setup registers, and N.6 rewords setup's and Connect's new lines.
- README and keypaste.com describe the published `v0.3.0`'s setup; L.1 rewrites them for 0.5.0.
