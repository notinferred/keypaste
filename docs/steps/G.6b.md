# G.6b — A Claude Desktop extension that starts the installed keypaste

Completed 2026-10-21 on `main` at `c208ad6`, source only: ci 39633992142 and app 39633992293, every job green, and ci 39634014306 ran the `tools` jobs. The bundle is packed and checked in CI and published nowhere: the row ships after 4.7c2.

## Amendments

The founder delegated the row's open choices on 2026-10-21: where the row leaves a choice open, the recommended option is taken and listed here. None touches a PRODUCT §3 law, what leaves the vault or who may approve it, and none publishes anything. D-0474 and D-0475 record them.

- **The format, as of 2026-10-21.** The MCPB specification ([MANIFEST.md](https://github.com/modelcontextprotocol/mcpb/blob/main/MANIFEST.md), read 2026-10-21) gives manifest version 0.3 as current, with server types `node`, `python` and `binary`, `mcp_config` with `platform_overrides`, `user_config`, and `${__dirname}` substitution. The CLI's latest release on npm that day is `@anthropic-ai/mcpb` 2.1.2: its schemas accept 0.1 to 0.4, where 0.4 is marked experimental and adds `uv`, and `init` still writes 0.2. Its `validate` checks the schema and that a local icon is a PNG; `pack` validates, then zips the directory without checking that `entry_point` exists. The bundle uses 0.3.
- **Server type `node`** (founder-delegated). Claude Desktop runs a `node` extension with its own Node, so one dependency-free script serves macOS and Windows with no runtime and no keypaste in the bundle. A `binary` launcher would need a script per platform, which Windows cannot start without `cmd.exe`, or a compiled program per platform.
- **Where the launcher looks** (founder-delegated). The launcher looks in `keypaste.app` under `/Applications` and `~/Applications`, then in `/usr/local/bin` (G.5's link), `/opt/homebrew/bin` (3.7a's place, which G.5 already reads) and `~/.local/bin` (README's CLI). On Windows it looks in the MSI's `%LOCALAPPDATA%\Programs\keypaste`. Outside Windows it passes over an installed place whose file, followed through links, or any directory above it belongs to an account other than root and the person's, because another admin can write `/Applications` and a Homebrew prefix (T-39). Last, it tries PATH's fully qualified entries, skipping relative and, on Windows, drive-relative ones as T-40's rule does. The app's own copy comes first because the desktop prefers the bridge beside it.
- **Setup's arguments** (founder-delegated). The launcher starts `keypaste mcp --client-label claude-desktop --allow-run --expose **`, which is exactly what setup registers for Claude Desktop under the default home. An agent's settings are therefore the same whether Claude Desktop was connected by setup or by the extension, and the extension releases nothing setup's registration would not (D-0446). It passes no `--vault`, so it follows the vault chosen with `keypaste use` (D-0389).
- **No user configuration and no tool list** (founder-delegated). There is no vault field: a person whose vault is not the chosen one runs `keypaste use`. The manifest lists no tools and sets `tools_generated`, so the bridge alone owns its tools, and `run` appears only because of `--allow-run`.
- **macOS and Windows only** (founder-delegated). Claude Desktop runs only on those two systems, which are where D-0435 connects it.
- **Version and icon at pack time** (founder-delegated). The manifest holds `{version}`, as `Info.plist` does, and the script writes the version it is given; CI gives it `VersionPrefix`. The icon is the app's `keypaste-256.png`, copied at pack time as the AppImage does, so the repository keeps one copy of it.
- **The CLI pin and the CI step** (founder-delegated). `scripts/mcpb-cli` locks the CLI and its 54 dependencies by integrity, as `scripts/real-tools` does, and `npm ci --ignore-scripts` installs it. The step runs in ci's checks job on `ubuntu-24.04`, whose image already has Node, npm and jq, so no Action is added (D-0468). Its limit is 2 minutes, and the job's limit rises from 4 to 5 minutes.
- **No CHANGELOG entry** (founder-delegated). Users can install nothing new until the bundle is published.

## Evidence

- `packaging/claude-desktop/manifest.json` and `server/index.js` are the bundle's sources.
- `scripts/build-claude-desktop-extension.sh` runs in ci's checks job and fails the job when any of these is untrue:
  - The pinned CLI validates the staged manifest and packs it.
  - The unpacked bundle holds exactly `icon.png`, `manifest.json` and `server/index.js`.
  - Started as Claude Desktop starts it, from the manifest's `entry_point`, the launcher runs a stand-in `keypaste` from `~/.local/bin` with setup's arguments. The stand-in echoes `ping` back through the launcher's stdio, and its exit code 7 comes back.
  - With the stand-in in `~/.local/bin` and another `keypaste` on an absolute PATH entry, the launcher starts the installed one.
  - The launcher finds the stand-in through an absolute PATH entry.
  - When the stand-in sits only in a relative PATH entry, the launcher skips it, exits 1, writes nothing to stdout, and says on stderr that keypaste is not installed.
- `ClaudeDesktopExtensionTests.TheLauncherStartsTheBridgeAsSetupRegistersClaudeDesktop` compares the launcher's arguments with `McpServerRegistration.CommandLine` for Claude Desktop under the default home. If setup's registration changes, the test fails until the launcher matches it.
- `verify.sh workflows` shellchecks the script.
- Nothing was run on the founder's machine except `bash -n`, `node --check`, and `npm install --package-lock-only --ignore-scripts`, which resolved the lock and installed nothing.
- The lock's integrity for `@anthropic-ai/mcpb` 2.1.2 matches the registry's `dist.integrity`.
- Runs: ci 39633992142 and app 39633992293.

## Decisions

D-0474 (the bundle and its launcher) and D-0475 (packing and checking it in CI). THREATS T-39 names the launcher's places and its check.

## Limits and follow-ups

- **Held for the founder: publication has no row.** Removing G.6b's row leaves the bundle's release asset, any `mcpb sign` signature and its installation verification in Claude Desktop with no STEPS row. G.6c covers only the Registry listing. Adding a row for them, or folding them into G.6c, is a publication choice, so it was not taken here.
- **Only the Linux path is checked.** The check runs only the launcher's Linux branch, `~/.local/bin` and PATH. Its macOS and Windows places, its passing over an installed copy another account owns, its Windows rule that skips a drive-relative PATH entry such as `\tools`, and Claude Desktop installing and starting the bundle, run in no check until the bundle's installation is verified.
- **Claude Desktop can hold keypaste twice.** If setup or Connect has already written keypaste's entry into `claude_desktop_config.json`, installing the extension gives Claude Desktop two keypaste servers under one label. Setup does not yet detect the extension.
- **No custom home.** A `KEYPASTE_HOME` other than the default, which setup pins with flags (D-0389), is not supported by the extension.
- **Time limits are estimates.** The step's 2-minute limit and the job's 5 minutes are estimates until ci 39633992142 and app 39633992293 show the step's time.
- **The CLI calls version 0.3 unfinished.** The CLI's own 0.3 schema file is marked work in progress, while the specification calls 0.3 current.
