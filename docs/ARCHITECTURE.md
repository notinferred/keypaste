# Code map

Where the code is, so a task starts from the right files instead of a search of the whole tree. [PRODUCT](PRODUCT.md) owns intent and laws; this map changes only when a project, entry point or process boundary changes.

## Projects

| Project | Role | Start at |
|---|---|---|
| `src/Keypaste.Core` | Every vault, env, generation, policy, approval, audit and release rule. Front ends adapt it and add none of their own (PRODUCT §4.2). Only `Internal/KeePassInterop.cs` touches KeePassLib types. | the concern table below |
| `src/Keypaste.Cli` | The `keypaste` command. One class per verb in `Commands/`; `CliApp` owns streams and exit codes, `VaultLocator` resolves `--vault` and `KEYPASTE_VAULT`, and `VaultSession` owns prompts, `--keyfile` and opening a vault. `keypaste agent` is the terminal approver. | `Program.cs`, `CliApp.cs` |
| `src/Keypaste.Mcp` | `keypaste-mcp`, the vault-free MCP bridge over stdio. Its two tools are in `Tools/`. It is the only process that writes the audit log (`McpAudit.cs`). | `Program.cs` |
| `src/Keypaste.App` | The Avalonia desktop. `Session/AppVaultSession.cs` owns the unlocked vault; `ViewModels/` has one view model per screen and `Views/` its XAML; `ShellViewModel` disposes every screen on lock. | `Program.cs`, `App.axaml.cs` |
| `third_party/KeePassLib` | The vendored KDBX engine. [UPSTREAM.md](../third_party/KeePassLib/UPSTREAM.md) owns provenance and local changes. | — |

`keypaste.slnx` builds Core, CLI, MCP, their tests and KeePassLib; `keypaste.app.slnx` builds Core, the desktop and its tests. Both write into `artifacts/`. `tests/Keypaste.Consistency.Tests` references both front ends and belongs to neither solution; [its README](../tests/Keypaste.Consistency.Tests/README.md) says why.

## Processes today

Four processes can hold vault data, each unlocking on its own; T2 in [STEPS](STEPS.md) replaces this with one session. One vault has one owner: the desktop and `keypaste agent` take its claim (`Core/Ownership/VaultClaim.cs`) before reading the password, and serve it on a per-user, per-vault named pipe (`Core/Ipc/ApproverEndpoint.cs`) while unlocked (D-0309).

- The desktop app unlocks its own session (`App/Session/AppVaultSession.cs`) and locks it on idle, on `Ctrl/Cmd+L`, on quitting and, when that setting is on, on minimize. Each unlock is a `Core/Ownership/SessionLifetime.cs`, and every lock ends it before the vault is disposed, withdrawing what agents have waiting and zeroing their grants (D-0313). `App/Session/SessionHost.cs` serves the unlocked vault to agents: listings are answered, and each credential request is put to the person in the prompt window `App/Session/WindowApprovalChannel.cs` shows (`Views/ApprovalWindow.axaml`), with no policy file consulted. `App/Session/AppAuthority.cs` is the two together as launch composes them: disposing it is quitting, and its status is read from the authority answering agents (D-0321). Agent Activity's Connect section runs an MCP client's own registration command and starts the registered `keypaste-mcp` once to check it, both through `Core/Clients/`; the desktop packages carry that bridge (D-0334).
- `keypaste agent` is started by a person in a terminal. It holds the unlocked vault, listens on the vault's pipe and asks that person through `Cli/Approval/TerminalApprovalChannel.cs`. Ctrl+C, SIGTERM and SIGHUP end its lifetime the same way.
- `keypaste-mcp` is started by the MCP client, never opens a vault, forwards each request to the vault's owner over the pipe and writes the audit record.
- `keypaste run` opens the vault, reads an env set, closes the vault and starts the child with the values in its environment. It takes no claim.

A credential request runs: MCP client → `Mcp/Tools/RequestCredentialTool.cs` → `Mcp/ApproverConnection.cs`, which attaches to the owner's session naming its vault → `Core/Ipc/ApproverClient.cs` → pipe → `Core/Ipc/ApproverListener.cs` in the owner → `Core/Ownership/SessionAuthority.cs`, which refuses a request not from its current session (D-0310) → `Core/Approval/ApproverHandler.cs`, which resolves the entry, re-checks exposure, consults the grant cache, a recent refusal and the policy, asks the person and only then reads one field → the authority commits the release only while the session's lifetime is live (D-0313) → reply naming the session → audit line.

## Where each concern lives in Core

| Concern | Files |
|---|---|
| Open, save, entries, history, recycle bin | `Vault.cs`, `VaultEntry.cs`, `EntryName.cs`, `EntryHandle.cs`, `EntryRevision.cs`, `RecycledEntry.cs` |
| Organize and search | `VaultOrganization.cs`, `VaultNameRules.cs`, `VaultSearch.cs` |
| Creation, unlock factors and access changes | `VaultCreation.cs`, `VaultKeyfile.cs`, `VaultAccess.cs`, `VaultLocation.cs` |
| Backups, restore and export | `VaultBackups.cs` |
| KDBX boundary, save retries and timing | `Internal/KeePassInterop.cs`, `Internal/SaveClock.cs`, `KdbxFormat.cs`, `ProcessTemporaryDirectory.cs`, `PathIdentity.cs` |
| Env sets and dotenv | `EnvStore.cs`, `EnvConvention.cs`, `EnvNameRules.cs`, `DotEnv.cs`, `DotEnvWriter.cs`, `SourceSnapshot.cs` |
| Password and passphrase generation | `PasswordGenerator.cs`, `PassphraseRecipe.cs`, `WordList.cs` |
| Secret input and display | `SecretBuffer.cs`, `SecretInput.cs`, `DisplayTextSanitizer.cs`, `EntryNameSanitizer.cs`, `Clipboard/` |
| What an agent may name | `EntryExposure.cs` |
| Approval, grants and credential release | `Approval/` |
| Approver pipe protocol | `Ipc/` |
| Who holds a vault, and which session a request belongs to | `Ownership/` |
| Standing rules in `policy.toml` | `Policy/` |
| Audit log and `~/.keypaste` paths | `Audit/`, `Audit/KeypasteHome.cs` |
| `recent.toml` and `app.toml` | `Recent/`, `Settings/` |
| Connecting MCP clients: the catalogue, setup plans, finding the bridge and the connection check | `Clients/` |
| Running another program and capturing what it printed | `Processes/` |

## Tests, gates and delivery

- `tests/Keypaste.<Project>.Tests` mirror the projects. The helper projects `TxfContender`, `PoolStarver`, `VaultSaver`, `VaultRestorer`, `MinimizeObserver`, `AppDriver` and `FakeMcpClient` are unshipped processes that particular probes and gates drive; `AppDriver` performs one desktop act through the screen's view model, or holds a vault unlocked and served as the app does, with the app's prompt window drawn on a headless display and clicked. `FakeMcpClient` builds as `claude` and answers Claude Code's `mcp add`, `remove` and `list`, so the connect gate never writes a real client's configuration.
- `scripts/verify.sh`, or `verify.ps1` in PowerShell, is the local verification entry point ([CLAUDE.md](../CLAUDE.md)). `scripts/verify-keepassxc-*.sh` are the permanent KeePassXC compatibility gates, one per vault feature, and `verify-keepassxc-workflows.sh` runs every workflow through the CLI and the desktop on vaults KeePassXC made; `verify-mcp-stdio.sh`, `verify-approval-e2e.sh`, `verify-policy-e2e.sh`, `verify-log-chain.sh`, `verify-session-authority.sh`, `verify-desktop-approval.sh`, `verify-connect-client.sh` and `verify-run-*.sh` drive the shipped processes; `verify-demo.sh` checks published transcripts against the binaries.
- Packaging and release: `release-targets.json` defines targets; `build-*.sh`, `publish-desktop-bridge.sh`, `sign-windows.sh`, `publish-release.sh`, `release-completion.sh` and `require-*.sh` implement [RELEASE](RELEASE.md).
- `.github/workflows/`: `ci.yml` (backend), `app.yml` (desktop), `release.yml`, `install*.yml`, `upgrade-desktop.yml`, `observe-desktop.yml`, `dco.yml` and single-question probes (`*-probe.yml`).
- `site/` is keypaste.com; [site/README.md](../site/README.md) owns its deployment.
