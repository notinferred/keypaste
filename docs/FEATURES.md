# Current capabilities

Reviewed against source and existing tests on 2026-09-20. This inventory records implemented behavior and remaining user journeys; checks were not rerun during the documentation refocus. [PRODUCT](PRODUCT.md) owns scope, [STEPS](STEPS.md) owns delivery, and [RELEASE](RELEASE.md) owns package and installation evidence.

The current public product is CLI/MCP `v0.3.0`. The desktop works from source and has internal package evidence, but no public release. A source feature is not automatically available in a downloaded binary.

## Vault and desktop

| Capability | Implemented behavior | Remaining gap |
|---|---|---|
| Create and open | CLI and desktop create and open password-protected KDBX vaults through the same core. Desktop offers a picker, drag-and-drop and recent vaults. | Keyfile unlock remains T1 work; hardware-key unlock is unimplemented, committed later as P.1 after the first integrated desktop release. |
| Save safely | Both front ends use the same writer. A stale copy refuses to overwrite an external change. A save that replaces an existing vault first copies it into `<vault>.backups` beside it, keeping the last five: once per unlock and no more often than every fifteen minutes. A save whose copy cannot be written does not happen, and nothing turns that off. | No concurrent-edit merge. The CLI says where the copies are kept once, when it creates the directory; the desktop says it in Settings. |
| Restore and export a vault | From the desktop's locked unlock screen, a vault's backups are listed, one is opened with the master password it was made under, and after a confirmation it replaces the vault byte for byte. The file it replaces is kept as a backup and nothing is pruned. It works when the vault is healthy, damaged or missing. Settings exports an exact encrypted copy to a new file, refusing an existing file, the vault and its backup directory. | No CLI restore or export verb. A missing vault is reachable only from the recent list. A restored vault opens with the backup's password, no keyfile can be supplied until V.1b, and nothing recovers a forgotten password. Another program holding the vault, a terminal `keypaste agent` included, keeps its own copy and does not notice a restore. |
| Store and edit | Desktop accepts existing or generated passwords and edits username, URL, notes and password. CLI adds, reads and removes entries. Core creates and renames groups and renames and moves entries, preserving identity, history and the data keypaste does not model, and refuses a name that would be taken, ambiguous or unusable as an environment variable. | No surface reaches the organize operations: the desktop controls are V.5b and the CLI has no verb for them. Clone is unimplemented. |
| Find | Desktop group navigation and case-insensitive title/group search; CLI lists a names-only tree. | Search over usernames and URLs, and the organization controls over V.5a's operations, are V.5b. Tag search is unimplemented. |
| Copy and reveal | Copy clears unchanged clipboard content after twenty seconds; desktop also clears on lock and normal quit. Env values and history passwords reveal while held. | Entries cannot reveal the current password; CLI `get --show` can. |
| Generate | Core, CLI and desktop generate character passwords and EFF word-list passphrases. | No browser generation or credential rotation workflow. |
| History | Updates retain prior revisions; desktop lists, reveals and restores them. Restoring retains the replaced value, within the history limit. A recycled entry keeps its history and gets it back on a restore. | History lives in the same vault and is not a separate backup; the whole-file copies under Save safely are, and Restore and export a vault puts one back. |
| Delete | Deletion asks for confirmation and moves the entry, its fields and its history to the vault's KDBX recycle bin, which KeePassXC reads as its own. A vault whose recycle bin is switched off deletes permanently, and both front ends say which happened. The desktop's Trash screen restores one or erases one behind a second confirmation, and Delete offers an immediate Restore. | No trash verb in the CLI, so recovering from a terminal still means KeePassXC. Emptying the whole bin is KeePassXC's. |
| Lock | Desktop manual, idle and optional minimize locking dispose its vault session and clear its visible state. Idle defaults to five minutes. | This does not lock the separate terminal approver or control a launched process. |
| Existing data | Ordinary edits preserve attachments and custom strings; fixtures exercise real KeePassXC reads and writes. | No attachment/custom-field management UI, broad format-options contract or full KeePassXC coverage claim. |

Implementation: [Vault](../src/Keypaste.Core/Vault.cs), [organize outcomes](../src/Keypaste.Core/VaultOrganization.cs), [name rules](../src/Keypaste.Core/VaultNameRules.cs), [format boundary](../src/Keypaste.Core/Internal/KeePassInterop.cs), [desktop session](../src/Keypaste.App/Session/AppVaultSession.cs), [entries](../src/Keypaste.App/ViewModels/EntriesViewModel.cs), [editing](../src/Keypaste.App/ViewModels/EntryDetailViewModel.cs), [history](../src/Keypaste.App/ViewModels/EntryHistoryViewModel.cs), [trash](../src/Keypaste.App/ViewModels/TrashViewModel.cs), [backups, restore](../src/Keypaste.Core/VaultBackups.cs), [restore panel](../src/Keypaste.App/ViewModels/RestoreBackupViewModel.cs), [export](../src/Keypaste.App/ViewModels/SettingsViewModel.cs). Existing coverage includes vault round-trip/save/history suites, desktop session and secret-input suites, [CLI/desktop consistency tests](../tests/Keypaste.Consistency.Tests/README.md), and KeePassXC compatibility scripts. [The desktop guide](desktop.md) records native verification limits.

## MCP and approvals

| Capability | Implemented behavior | Remaining gap |
|---|---|---|
| Agent tools | `list_entry_names` returns exposed names; `request_credential` requests one field. Exposure defaults to `env/**`. | No vault editing through MCP. |
| Approval | A separately started `keypaste agent` unlocks a vault and asks in its terminal. Requests can reuse a live connection-scoped grant or match a user-written policy. | No native desktop approval/deny dialog. |
| Unlock lifecycle | Terminal approver holds its vault until stopped. MCP bridge holds no vault and cannot trigger a master-password prompt. | Desktop and approver have independent sessions; terminal approver has no idle lock. |
| Updated values | A fresh CLI invocation reads the saved file. | An already-open terminal approver retains its snapshot until reopened; desktop edits are not live updates to it. |
| Local records | Calls, including refusals, enter a local hash-chained audit log. Missing audit writes deny release. | No protection against a fully recomputed log or missing tail without an independent anchor. |
| Client setup | CLI `setup` configures supported clients or prints manual configuration. | No desktop connection flow. Printed formats do not establish every client/platform installation. |

Implementation: [MCP tools](../src/Keypaste.Mcp/Program.cs), [terminal approver](../src/Keypaste.Cli/Commands/AgentCommand.cs), [approval decision](../src/Keypaste.Core/Approval/ApproverHandler.cs), [desktop activity status](../src/Keypaste.App/ViewModels/AgentActivityViewModel.cs), [setup](../src/Keypaste.Cli/Commands/SetupCommand.cs). Existing suites exercise exposure, consent, refusal, grant expiry, policy and audit behavior. [Approvals](approvals.md) and [MCP setup](mcp-setup.md) describe the usable terminal workflow.

## Environment projects

| Capability | Implemented behavior | Remaining gap |
|---|---|---|
| Store | Each variable is an ordinary KDBX entry under `env/<project>`, with its value in the password field. Desktop and CLI edit these entries. | No live global environment. |
| Import/export | CLI imports a validated `.env` and explicitly exports plaintext when requested. | No desktop import/export flow. Exported plaintext persists independently of locking. |
| Run | `keypaste run <project> -- <command>` prompts for the vault password, reads variables, closes the vault and starts the child with those variables. | It does not reuse desktop unlock state or require the desktop to stay unlocked. |
| Desktop use | Env Sets offers editing, copy/reveal and Copy run command. | No launch-app or launch-terminal action and no project activation tied to a shared session. |
| Lock after release | Desktop lock clears its own state. | It cannot remove values already delivered to a child, descendant, clipboard consumer or MCP client. |

Implementation: [env convention](../src/Keypaste.Core/EnvConvention.cs), [env store](../src/Keypaste.Core/EnvStore.cs), [run](../src/Keypaste.Cli/Commands/RunCommand.cs), [process launcher](../src/Keypaste.Cli/Execution/SystemProcessLauncher.cs), [Env Sets](../src/Keypaste.App/ViewModels/EnvSetsViewModel.cs). Existing injection checks assert that variables reach the child without an env file and that the vault closes before startup. [Replace your `.env`](replace-dotenv.md) describes this workflow.

## The focused product still to complete

The committed outcome is a familiar local KDBX password manager with one unlock session governing desktop use, MCP access and env launches. An AI request can be approved or denied in the app. A selected env project can launch an app or terminal while the vault is unlocked. Locking stops new credential releases and launches and cancels pending approvals; it does not recall disclosed values or revoke credentials at their issuers.

That integrated journey is not implemented today. [STEPS](STEPS.md) covers daily vault use and recovery, the shared session, MCP approval in the app, env projects and desktop delivery. Existing components are reusable; their separate checks do not establish this combined experience.

Hosted accounts, sync services, relay/share links, teams, billing, mobile, a browser vault, browser extensions and complete KeePassXC parity have no committed delivery requirement. Broader capabilities, including TOTP, passkeys, SSH-agent and Secret Service integrations, are options in [BACKLOG](BACKLOG.md). KDBX compatibility means preserving and exchanging supported data; it does not mean reproducing every KeePassXC workflow.
