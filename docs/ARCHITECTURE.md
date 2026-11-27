# Code map

Where each part of keypaste lives, so a change starts from the right files. Paths under `Core/`, `Cli/`, `Mcp/`, `Browser/` and `App/` are in `src/Keypaste.<Project>/`. [SECURITY](../SECURITY.md#security-laws) owns the security laws.

## Projects

| Project | Owns | Start at |
|---|---|---|
| `src/Keypaste.Core` | Every rule about vaults, env sets, generation, agent access, approval, audit and release | [Where each concern lives](#where-each-concern-lives) |
| `src/Keypaste.Infrastructure` | What knows nothing about vaults: strict TOML and CSV, newline-delimited frames, the shared command-line syntax over System.CommandLine, atomic file replacement, version ordering. References no project. Submodule of [keypaste-infrastructure](https://github.com/notinferred/keypaste-infrastructure). | `Toml.cs`, `Syntax.cs` |
| `src/Keypaste.Cli` | The `keypaste` command. The binary also carries the bridge and the browser relay, which `Program.cs` dispatches to before any vault access. | `Program.cs`, `CliApp.cs` |
| `src/Keypaste.Mcp` | The bridge, `keypaste mcp`: a vault-free MCP server over stdio, a library whose one public type is `BridgeEntry` | `BridgeEntry.cs` |
| `src/Keypaste.Browser` | The vault-free relay a browser starts as native messaging host `com.keypaste.browser`, speaking KeePassXC-Browser's protocol. One public type, `BrowserEntry`. | `BrowserEntry.cs`, `Relay.cs` |
| `src/Keypaste.App` | The Avalonia desktop app | `Program.cs`, `App.axaml.cs` |
| `extension` | The browser extension, a GPL-3.0 fork of KeePassXC-Browser 1.10.4.1 ([UPSTREAM.md](../extension/UPSTREAM.md)). Submodule of [keypaste-extension](https://github.com/notinferred/keypaste-extension). | `src/background/keepass.js`, `build.js` |
| `third_party/KeePassLib` | The vendored KDBX engine, with BouncyCastle's Twofish compiled in. [KeePassLib's](../third_party/KeePassLib/UPSTREAM.md) and [BouncyCastle's](../third_party/KeePassLib/bouncycastle-twofish/UPSTREAM.md) UPSTREAM.md own provenance. Submodule of [keypaste-keepasslib](https://github.com/notinferred/keypaste-keepasslib). | — |

Each submodule is pinned to a commit, and a pin moves only in a commit of its own, which Dependabot proposes monthly. KeePassLib's and Infrastructure's lock files are in `locks/`.

| Solution | Builds |
|---|---|
| `keypaste.slnx` | Core, Infrastructure, CLI, MCP, the relay, their tests, the fake browser and KeePassLib |
| `keypaste.app.slnx` | Core, Infrastructure, the desktop and its tests |
| `tests/Keypaste.Consistency.Tests` | References both front ends, in neither solution ([README](../tests/Keypaste.Consistency.Tests/README.md)) |

Both solutions write into `artifacts/`.

## Rules the build holds

- **The one-core rule.** Every rule lives once, in Core. The CLI, the desktop, the bridge and the relay adapt it and add none of their own.
- **Only `Core/Internal/` touches KeePassLib.** Core references it under the `KeePass` extern alias, which only `Internal/` files declare ([law 6](../SECURITY.md#security-laws)).
- **Banned symbols.** Each `src` project's `BannedSymbols.txt`, plus the shared `src/BannedSymbols.*.txt`, bans the network outside the share and crash clients, file writes in the front ends, and a vault in the bridge and the relay ([law 11](../SECURITY.md#security-laws)). An exception needs a `#pragma` that `BanExemptionsTests` lists.
- **Project references.** `Directory.Build.targets` holds which project may reference which, and that only the CLI links libsodium.
- **Culture.** In Core, Infrastructure and the desktop, `.editorconfig` and Meziantou.Analyzer require every culture-sensitive call to say ordinal, invariant or current. `verify.sh backend` runs Core's tests under Turkish, German and Arabic.
- **Source rule tests** (`*SourceRulesTests`, `DispatchOrderTests`) hold what the compiler cannot see, each saying why.

## Processes

| Process | Started by | Vault | Entry |
|---|---|---|---|
| Desktop app | The person, or the login entry with `--background` | Holds several unlocked, as their approver | `App/Program.cs` |
| `keypaste approve` | The person, in a terminal | Holds one unlocked, as its approver | `Cli/Commands/ApproveCommand.cs` |
| `keypaste mcp`, the bridge | The AI tool | Never opens one ([law 11](../SECURITY.md#security-laws)) | `Mcp/BridgeEntry.cs` |
| Browser relay | The browser, for keypaste's extension | Never opens one | `Browser/BrowserEntry.cs` |
| `keypaste run` | The person, or Terminal for the app's Run | Opens, reads and closes it, or asks the approver | `Cli/Commands/RunCommand.cs`, `Core/Launch/EnvLaunch.cs` |
| Other verbs | The person | Saving verbs take the vault's claim | `Cli/CliApp.cs` |

### Ownership

- One vault has one approver, which the code calls its owner. It takes the vault's claim (`Core/Ownership/VaultClaim.cs`) before reading the password, then serves the vault on a per-user, per-vault pipe (`Core/Ipc/ApproverEndpoint.cs`).
- `Core/Ownership/SessionAuthority.cs` records every request in the audit log before replying, and refuses a release it cannot record ([law 3](../SECURITY.md#security-laws)).
- Each unlock is a `SessionLifetime.cs`; a lock ends it, withdrawing what agents have waiting.
- Only a claim's holder writes. `Vault.OpenReadOnly` never saves.

### Desktop app

- `App/Session/OpenVaults.cs` holds the open vaults, each an `AppVaultSession.cs` with its own claim, lifetime and pipe.
- One copy runs per user and `KEYPASTE_HOME` (`Core/Ownership/AppClaim.cs`). A second start or a handed file (`HandedFile.cs`) goes to the running copy.
- `App/Session/SessionHost.cs` serves each vault to agents. `WindowApprovalChannel.cs` asks the person, one prompt at a time across all vaults, in a window under `Views/`.
- While locked, `App/Session/LockedNotice.cs` tells the person when a bridge finds the vault locked.
- All vaults lock together on idle, `Ctrl/Cmd+L`, the tray's Lock (`App/AppTray.cs`) and quitting. Closing the window into the tray hides it and locks none, so agents are still answered. One locks alone for `keypaste lock`, an access change or a refused reload.
- A project's Run and Open terminal (`ViewModels/ProjectLaunchViewModel.cs`) start at their press, logged as `pressed`; a protected stage's ask on its card. Run again in the Dock and tray menus starts the last `Session/ProjectLaunching.cs` `ProjectRun` once more (`App.RunAgainAsync`), and is off after a protected one.
- Quick unlock: `Session/QuickUnlockCopies.cs`, with `Session/WindowsHello.cs` and `Session/TouchId.cs` as `MachineQuickUnlock.cs` picks them.
- Agents › Connect registers an AI tool through `Core/Clients/`. `App/CrashReporting.cs` sends crash reports only under the terms the person accepted (`App/Terms/`).

The [desktop guide](desktop.md) describes what the person sees.

### keypaste approve

It refuses to start without its audit log. It then holds the unlocked vault and asks the person through `Cli/Approval/TerminalApprovalChannel.cs`, under the vault's `agents/<vault>.toml`. Ctrl+C, SIGTERM and SIGHUP end its lifetime as a lock does.

### The bridge

- `Mcp/BridgeVaults.cs` holds a route to each `--vault`'s owner, each an `ApproverConnection.cs`. A search asks every route at once; a request goes to the item's vault, and an item more than one vault could hold is refused.
- A locked vault answers as locked without holding up another (`Mcp/LockedApp.cs`).
- It writes an audit line only for a call no owner recorded (`McpAudit.cs`).
- `Program.StartsBridge` decides from the arguments alone.

| MCP tool | Class in `Mcp/Tools/` | Does |
|---|---|---|
| `find_access` | `FindAccessTool` | Searches what the bridge exposes |
| `list_entry_names` | `ListEntryNamesTool` | Lists exposed items' titles, groups, askable fields and project tags, never a value |
| `request_credential` | `RequestCredentialTool` | Asks for one field |
| `request_attachment` | `RequestCredentialTool` | Asks for one attachment by name |
| `request_linked_set` | `RequestLinkedSetTool` | Asks for up to four linked fields in one prompt |
| `request_save` | `SaveValueTool` | Asks the person to save a value |
| `request_item_change` | `RequestItemChangeTool` | Asks the person to allow a change to an item |
| `run` | `RunTool` | Starts an approved command with the set in its environment; only under `--allow-run` |

### The browser relay

- `Relay.cs` speaks the extension's protocol; `NativeMessages.cs` frames messages and `NonceLedger.cs` keeps nonces. It sends the owner only typed requests over `Core/Ipc/BrowserLink.cs` and writes no audit line.
- `RelayOptions.cs` picks the owners it may reach from `app.toml`'s local account.
- The NaCl box is `Sodium.cs` alone, over libsodium, which [build-libsodium.sh](../scripts/build-libsodium.sh) builds from its signed release. `KnownAnswers.cs` holds the vectors `keypaste --selftest` checks.
- On the owner's side, `Core/Ownership/SessionBrowsers.cs` handles pairing, list, release and show. `Core/Browser/BrowserPairings.cs` keeps pairings in `browsers.json`, sealed under each unlocked vault's key.

### keypaste run

It opens the vault, reads an env set, closes the vault and starts the child through `Core/Launch/EnvLaunch.cs`. It takes no claim. While the vault's claim names the app or `keypaste approve` and that owner attaches with the vault unlocked, it opens nothing and sends an env request to the owner, which asks the person and records it, as `--session` does.

| Flag | What it does instead |
|---|---|
| `--session` | Only asks the owner, and opens nothing even when none answers |
| `--no-session` | Always opens the vault with the master password, asking no owner |
| `--token` | Sends a token env request the owner verifies against `Core/Tokens/` |
| `--bundle` | Opens a token bundle with no vault or pipe |

The app's Run on macOS carries a launch pass in `KEYPASTE_LAUNCH`, which the owner takes in place of asking and no child inherits.

### Other verbs

- `keypaste lock` and `keypaste doctor` attach to the owner as the bridge does. `doctor` (`Core/Health/`) opens no vault and writes nothing.
- `keypaste` alone reports status and starts nothing (`Commands/StatusCommand.cs`).
- `keypaste share` seals one field and uploads the ciphertext to api.keypaste.com's `/v1/share`.

## Request paths

A credential request:

1. `Mcp/Tools/RequestCredentialTool.cs` → `Mcp/BridgeVaults.cs` picks the vault.
2. `Mcp/ApproverConnection.cs` → `Core/Ipc/ApproverClient.cs` → pipe → `Core/Ipc/ApproverListener.cs`.
3. `SessionAuthority` refuses a request not from its current session.
4. `Core/Approval/ApproverHandler.cs` checks exposure and rules, asks the person if needed, then reads one field.
5. `SessionAuthority` records the line, or refuses the release.

A save takes the same path to `Core/Ownership/SessionSaver.cs` (`Core/Editing/ValueSaves.cs`). An item change goes to `Core/Ownership/SessionItemChanger.cs` (`Core/Editing/ItemChanges.cs`).

A browser request:

1. Extension → browser → `keypaste` → `Browser/Relay.cs` opens the box and checks the nonce.
2. `Core/Ipc/BrowserLink.cs` → pipe → `SessionAuthority`, which admits only a browser attachment to its current session.
3. `Core/Ownership/SessionBrowsers.cs` decides, bound to the proven pairing.
4. The relay seals the reply.

A Run from the app on macOS: `App/ViewModels/ProjectLaunchViewModel.cs` → `SessionAuthority.IssueLaunch` (`Core/Approval/LaunchPasses.cs`) → Terminal → `keypaste run --session` → `Core/Ownership/LaunchEndpoint.cs`, which takes the pass and releases the set without asking, then records it as `pressed`, or `prompt` for one confirmed on its card. On Windows and Linux `SessionAuthority.RecordAppRun` writes the same line before the terminal starts.

## Where each concern lives

### Core

Paths are in `src/Keypaste.Core/`.

| Concern | Files |
|---|---|
| Open, save, items, fields, history, Trash | `Vault.cs`, `VaultEntry.cs`, `EntryField.cs`, `FieldNameRules.cs`, `EntryName.cs`, `EntryHandle.cs`, `EntryRevision.cs`, `RecycledEntry.cs` |
| Organize, search, an agent's search, linked items | `VaultOrganization.cs`, `VaultNameRules.cs`, `VaultSearch.cs`, `AccessIndex.cs`, `IndexedEntry.cs`, `FieldReferences.cs` |
| Attachments (5 MiB cap) | `EntryAttachment.cs`, `Internal/KeePassInterop.Attachments.cs` |
| A terminal's edit to items | `Editing/TerminalEdit.cs`, `Editing/TerminalEdits.cs`, `Ownership/SessionEditor.cs`, `VaultCheckpoint.cs` |
| An agent's save or item change | `Editing/`, `Approval/SaveRequestRules.cs`, `Approval/ItemChangeRules.cs`, `Ownership/SessionSaver.cs`, `Ownership/SessionItemChanger.cs` |
| Recommendations | `Recommendations/`, `Internal/KeePassInterop.Recommendations.cs` |
| Creation, unlock factors, access changes | `VaultCreation.cs`, `VaultKeyfile.cs`, `VaultAccess.cs`, `VaultLocation.cs`, `HardwareKeys/` |
| Backups, restore, export, every vault file write | `VaultBackups.cs`, `Internal/VaultFiles.cs` |
| Merging another program's save; sync conflicted copies | `VaultMerge.cs`, `Internal/KeePassInterop.Merge.cs`, `Ownership/VaultFileWatch.cs`, `ConflictedCopies.cs` |
| The KDBX boundary | `Internal/KeePassInterop.cs` and `KeePassInterop.<Concern>.cs` |
| Key derivation | `Internal/NativeArgon2.cs`, `KdbxFormat.cs`, `VaultKeyDerivation.cs` |
| Env sets and dotenv | `EnvStore.cs`, `EnvResolution.cs`, `EnvImport.cs`, `DotEnv.cs`, `DotEnvWriter.cs` |
| Project tags, `env:<project>[:<stage>]` | `TagRules.cs`, `ProjectTag.cs`, `ProjectCatalog.cs` |
| Profiles, matrix, diffs | `EnvProfileNames.cs`, `EnvMatrix.cs`, `EnvDiff.cs` |
| `kp://` references | `KpReference.cs`, `EnvReferenceFile.cs`, `EnvReferenceResolution.cs` |
| KeePass placeholders such as `{PASSWORD}` | `KeePassPlaceholders.cs`, `PlaceholderFill.cs` |
| Auto-Type planning (nothing here types) | `AutoType/` |
| keypaste's hidden groups | `ReservedGroups.cs` |
| One-time codes | `Totp/` |
| Scoped tokens and bundles | `Tokens/` |
| Share links | `Sharing/` |
| Browser fill: manifests, pairings, login matching | `Browser/` |
| Crash reports: scrubber, outbox, sender | `CrashReports/` |
| Import | `Import/`, `Internal/KeePassInterop.Import.cs` |
| Starting a program with an env set | `Launch/` |
| Where a project runs (`projects.json`) | `Projects/` |
| Password and passphrase generation | `PasswordGenerator.cs`, `PassphraseRecipe.cs`, `WordList.cs` |
| Secret input and display | `SecretBuffer.cs`, `SecretInput.cs`, `DisplayTextSanitizer.cs`, `EntryNameSanitizer.cs`, `Clipboard/` |
| What an agent may name | `EntryExposure.cs` |
| Approval and release | `Approval/` |
| The pipe protocol and caller identity | `Ipc/`, `Approval/RequesterIdentity.cs` |
| Who holds a vault, sessions, owner endpoints | `Ownership/` |
| Agent settings (`agents/<vault>.toml`) | `Agents/` |
| Audit log and every write under `~/.keypaste` | `Audit/`, `Ownership/AuditCheckpoints.cs`, `HomeFiles.cs` |
| `recent.toml`, `app.toml`, the local account | `Recent/`, `Settings/` |
| Opening at login | `Login/` |
| `keypaste` on a terminal's PATH | `Linking/` |
| Connecting AI tools | `Clients/` |
| `keypaste doctor` checks | `Health/` |
| Last used and release ledger | `Activity/` |
| Running a program and capturing output | `Processes/` |

### Front ends

| Concern | Files |
|---|---|
| CLI verbs, one class each | `Cli/Commands/` |
| Verb tree, dispatch, exit codes | `Cli/CliApp.cs` |
| Resolving the vault | `Cli/VaultLocator.cs` |
| Prompts, opening a vault, edits through the owner | `Cli/VaultSession.cs`, `Cli/OwnerEdit.cs` |
| Styling and `--json` | `Cli/Styling/`, `Cli/Output/CliJson.cs` |
| Desktop screens and view models | `App/ViewModels/`, `App/Views/` |
| Design tokens, styles, icons | `App/Theme/` |
| Labels (`Strings.resx`, generated by `scripts/generate-strings.py`) | `App/Resources/` |
| Culture and ICU | `App/Globalization/` |
| System notifications | `App/Notifications/` |
| YubiKeys | `App/HardwareKeys/YubiKeyHardware.cs` |
| Desktop targets (Windows, and Linux and macOS) | `App/Keypaste.App.csproj` |
| Extension: native messaging, NaCl, pairing | `extension/src/background/` |
| Extension: field icon and fill | `extension/src/content/` |
| Extension builds and their keys | `extension/build.js`, `extension/keys/` |

## Tests and gates

| What | Where |
|---|---|
| Tests per project | `tests/Keypaste.<Project>.Tests` |
| Local verification | `scripts/verify.sh` or `verify.ps1` ([CONTRIBUTING](../CONTRIBUTING.md#verification)) |
| KeePassXC compatibility | `Keypaste.Core.Tests.KeePassXC`, `scripts/verify-keepassxc-*.sh` |
| Across process boundaries | `Keypaste.Processes.Tests` |
| Shipped processes end to end | `verify-mcp-stdio.sh`, `verify-approval-e2e.sh`, `verify-policy-e2e.sh`, `verify-log-chain.sh`, `verify-run-injection.sh`, `verify-run-signals.sh` |
| Real AI tools | `verify-real-tools.sh`, ci's `tools` jobs only |
| Published transcripts | `verify-demo.sh` |
| Extension and browser fill | `verify-extension-load.sh`, `verify-browser-fill.sh` |
| Gate helpers | `scripts/lib/common.sh`, `scripts/lib/kpxc.sh` |
| Test vectors shared with keypaste.com | `tests/vectors/` |

Unshipped helpers the tests and gates drive:

| Helper | Stands in for |
|---|---|
| `AppDriver` | The desktop: one act, a served vault, a clicked prompt, the crash route on loopback |
| `FakeMcpClient` | Claude Code, so the connect gate never writes a real client's configuration |
| `FakeBrowser` | keypaste's extension talking to a host |
| `EnvReporter` | The child of the app's Run, reporting its environment |
| `TxfContender`, `PoolStarver`, `VaultSaver`, `MinimizeObserver` | Processes particular probes drive |

## Packaging, workflows and services

- `release-targets.json` defines the release targets; the `build-*.sh`, `publish-*.sh`, `sign-windows.sh`, `release-completion.sh` and `require-*.sh` scripts implement [RELEASE](RELEASE.md).
- `packaging/claude-desktop/` is the Claude Desktop extension, a launcher for the installed `keypaste mcp`. Nothing publishes it yet.
- `.github/workflows/` holds `ci.yml` (backend), `app.yml` (desktop), `release.yml`, `install*.yml`, `upgrade-desktop.yml`, `dco.yml`, `dependency-notices.yml` and `dev.yml`. [CONTRIBUTING](../CONTRIBUTING.md#ci) says what each covers.
- keypaste.com and the services at `api.keypaste.com` and `share.keypaste.com` live in private repositories. The product builds and releases without them; the shared vectors are the product's, in `tests/vectors/`.
