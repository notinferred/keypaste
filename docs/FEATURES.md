# Current capabilities

Reviewed against source and existing tests on 2026-09-21. This inventory records implemented behavior and remaining user journeys; checks were not rerun during the documentation refocus. [PRODUCT](PRODUCT.md) owns scope, [STEPS](STEPS.md) owns delivery, and [RELEASE](RELEASE.md) owns package and installation evidence.

The current public product is CLI/MCP `v0.3.0`. The desktop works from source and has internal package evidence, but no public release. A source feature is not automatically available in a downloaded binary.

## Vault and desktop

| Capability | Implemented behavior | Remaining gap |
|---|---|---|
| Create and open | CLI and desktop create and open password-protected KDBX vaults through the same core. Desktop offers a picker, from Browse or `Ctrl/Cmd+O` on the unlock screen, drag-and-drop and recent vaults. In source, every CLI verb that opens a vault also takes `--keyfile` or `KEYPASTE_KEYFILE`, opening all four KeePass keyfile forms and a vault protected by a keyfile alone; an arbitrary hashed file is named on stderr as the form one edit destroys, and a missing, empty or unreadable file, or the vault itself, is refused before the password is asked for. In source, `keypaste access` changes the master password and adds, replaces or removes an existing XML, 32-byte or hex keyfile, keeping the replaced file as a backup; it never removes a password or attaches an arbitrary hashed file. In source, the desktop's unlock screen takes a keyfile, opens a vault a keyfile alone protects, refuses an unusable file when it is chosen and remembers the keyfile's location for that vault; its create form attaches an existing keyfile to a new password vault; and Settings changes the password and adds, replaces or removes the keyfile behind the current password and a confirmation stating what the change costs the backups, staying unlocked afterwards. | keypaste never creates a keyfile or a keyfile-only vault; `keypaste init` takes no keyfile, so the CLI makes a password vault and `keypaste access` attaches one afterwards. The CLI records no keyfile. A keyfile-only vault's access change is guarded by the keyfile alone, having no password to ask for. Hardware-key unlock is committed later as P.1. |
| Save safely | Both front ends use the same writer. A stale copy refuses to overwrite an external change. A save that replaces an existing vault first copies it into `<vault>.backups` beside it, keeping the last five: once per unlock and no more often than every fifteen minutes. A save whose copy cannot be written does not happen, and nothing turns that off. | No concurrent-edit merge. The CLI says where the copies are kept once, when it creates the directory; the desktop says it in Settings. |
| Restore and export a vault | From the desktop's locked unlock screen, a vault's backups are listed, one is opened with the master password it was made under, and after a confirmation it replaces the vault byte for byte. The file it replaces is kept as a backup and nothing is pruned. It works when the vault is healthy, damaged or missing. Settings exports an exact encrypted copy to a new file, refusing an existing file, the vault and its backup directory. | No CLI restore or export verb. A missing vault is reachable only from the recent list. A restored vault opens with the password and keyfile the backup was made under, which the restore panel takes, and nothing recovers a forgotten password or a lost keyfile. Another program holding the vault, a terminal `keypaste agent` included, keeps its own copy and does not notice a restore. |
| Store and edit | Desktop accepts existing or generated passwords and edits username, URL, notes and password. CLI adds, reads and removes entries. The desktop creates and renames groups, and renames and moves an entry in one write behind one confirm, preserving identity, history and the data keypaste does not model; a refusal writes nothing and says which it was. | Removing a group and moving one to another parent are unimplemented in core and so unreachable anywhere; the CLI has no organize verb. Clone is unimplemented. |
| Find | Desktop group navigation and case-insensitive search over titles, group paths, usernames and URLs, matched in core so no value reaches the screen; a result names the fields it matched. Passwords, notes and protected custom fields are not read for matching. CLI lists a names-only tree. | The CLI has no search verb. Tag search is unimplemented. |
| Copy and reveal | Copy clears unchanged clipboard content after twenty seconds; desktop also clears on lock and normal quit. On the desktop an entry's current password, an env value and a history password each reveal while held and copy through that same clear. Every masked input is announced by its purpose, not its content. | Reveal needs a pointer: no keyboard gesture holds a value. What each secret surface draws is checked in frames Skia renders in a headless window ([4.6](steps/4.6.md)), not on a native display, scaling or screen reader. |
| Generate | Core, CLI and desktop generate character passwords and EFF word-list passphrases. | No browser generation or credential rotation workflow. |
| History | Updates retain prior revisions; desktop lists, reveals and restores them. A restore makes the entry that revision whole, as KeePass and KeePassXC do: its attachments, custom fields, custom data, tags and auto-type become the revision's, and the replaced state is kept as the newest revision, within the history limit. A recycled entry keeps its history and gets it back on a restore. | History lives in the same vault and is not a separate backup; the whole-file copies under Save safely are, and Restore and export a vault puts one back. |
| Delete | Deletion asks for confirmation and moves the entry, its fields and its history to the vault's KDBX recycle bin, which KeePassXC reads as its own. A vault whose recycle bin is switched off deletes permanently, and both front ends say which happened. The desktop's Trash screen restores one or erases one behind a second confirmation, and Delete offers an immediate Restore. | No trash verb in the CLI, so recovering from a terminal still means KeePassXC. Emptying the whole bin is KeePassXC's. |
| Lock | Desktop manual, idle and optional minimize locking dispose its vault session and clear its visible state. Idle defaults to five minutes. In source, locking also gives up the vault's claim and stops serving it to agents. Every lock and quitting deny a request still waiting at the app as locked and zero the grants agents were given; an agent's request never counts as activity, and one arriving after the machine slept past the timeout locks the app and is refused ([U.2](steps/U.2.md)). | This does not lock a terminal approver holding another vault or control a launched process. |
| Existing data | Every workflow under [KeePassXC compatibility](#keepassxc-compatibility) keeps the attachments, custom fields, custom data, tags and auto-type KeePassXC wrote, except a history restore, which returns them to the revision's. | No attachment, custom-field, tag or auto-type editor, and no claim beyond the subset below. |

Implementation: [Vault](../src/Keypaste.Core/Vault.cs), [organize outcomes](../src/Keypaste.Core/VaultOrganization.cs), [name rules](../src/Keypaste.Core/VaultNameRules.cs), [format boundary](../src/Keypaste.Core/Internal/KeePassInterop.cs), [desktop session](../src/Keypaste.App/Session/AppVaultSession.cs), [entries](../src/Keypaste.App/ViewModels/EntriesViewModel.cs), [editing](../src/Keypaste.App/ViewModels/EntryDetailViewModel.cs), [history](../src/Keypaste.App/ViewModels/EntryHistoryViewModel.cs), [trash](../src/Keypaste.App/ViewModels/TrashViewModel.cs), [backups, restore](../src/Keypaste.Core/VaultBackups.cs), [restore panel](../src/Keypaste.App/ViewModels/RestoreBackupViewModel.cs), [export](../src/Keypaste.App/ViewModels/SettingsViewModel.cs). Existing coverage includes vault round-trip/save/history suites, desktop session and secret-input suites, [CLI/desktop consistency tests](../tests/Keypaste.Consistency.Tests/README.md), and KeePassXC compatibility scripts. [The desktop guide](desktop.md) records native verification limits.

## KeePassXC compatibility

Checked on 2026-09-23 in source ([9.4](steps/9.4.md)) by [verify-keepassxc-workflows.sh](../scripts/verify-keepassxc-workflows.sh): locally against KeePassXC 2.7.10 on Windows, and in `app.yml` run 35892763909 against Ubuntu 24.04's packaged 2.7.6 and the pinned Windows 2.7.12. None of it is in a public download.

KeePassXC makes three vaults by importing one KeePass XML document: one behind a password, one behind a password and an XML keyfile, and one behind the keyfile alone. Each is KDBX 4.0 with AES-256 and AES-KDF. Each carries meta, group and entry custom data, a plain and a protected custom field, a tag, an auto-type association, a revision KeePassXC wrote and two attachments. The CLI runs its verbs on them, and the desktop's screens are driven through the commands they bind to. After every write, KeePassXC opens the vault with its current factors and reads what was written.

| Workflow | CLI | Desktop | What KeePassXC reads afterwards |
|---|---|---|---|
| Open | `ls` | Unlock, with a password, a keyfile or both | — |
| Edit | `env set` on a variable KeePassXC made | The entry pane changes the password, notes and URL of the entry carrying the unmodelled data | The new values. The first save's backup is byte for byte the file KeePassXC wrote |
| History | — | Restores the revision KeePassXC wrote, then the state that restore replaced | The first restore makes the entry the revision whole, dropping what KeePassXC added after it; the second brings all of it back |
| Organize | — | Renames the group carrying custom data, then renames the entry and moves it out of that group | Still KDBX 4.0, and the entry keeps its data |
| Delete and recover | `rm` | Trash restores the CLI's deletion, and its own | Each deletion is in KeePassXC's own Recycle Bin until it is restored |
| Backup and restore | Saves keep the backups | The restore panel, and export from Settings | The restored vault is KeePassXC's own bytes, and the export opens under the same factors |
| Change access | Changes the password, attaches, replaces and removes an XML keyfile, and swaps a keyfile-only vault's keyfile | The same, and sets a password while removing a keyfile-only vault's keyfile | Opens only under the new factors, with the cipher and KDF KeePassXC chose |
| Create | `init` | Create with a password and a keyfile | Opens both; the desktop's needs both factors |

After every write in the table, KeePassXC checks four things. It still finds the custom data, custom fields, tag and auto-type on the entries themselves, not only in history. It exports both attachments byte for byte. Its cipher and KDF are unchanged. The file is still KDBX 4.

These are refused, and each leaves the vault byte-identical with no backup taken:

- Opening with a wrong password, or with a missing or wrong keyfile, from either front end.
- Attaching an arbitrary file keyed by its hash, from either front end.
- Removing the only factor of a keyfile-only vault, from either front end.
- `add` over an existing entry.
- A desktop move onto an occupied name.
- A Trash restore onto a name KeePassXC has since taken.
- Restoring a backup with a wrong password.
- Creating over an existing vault.

Not exercised:

- KDBX 3.1.
- ChaCha20 or Twofish.
- An Argon2 vault made by KeePassXC, because `keepassxc-cli` cannot choose a KDF.
- Custom icons and KeePassXC's browser-integration data.

The desktop is driven through its screens' commands, not through rendered windows. This is a supported subset, not KeePassXC parity (D-0292).

## MCP and approvals

| Capability | Implemented behavior | Remaining gap |
|---|---|---|
| Agent tools | `list_entry_names` returns exposed names; `request_credential` requests one field. Exposure defaults to `env/**`. In source, the process holding the vault applies the tool's argument limits and the exposure itself, so a request that bypasses the bridge is refused the same way ([4.3a](steps/4.3a.md)). | No vault editing through MCP. |
| Approval | A separately started `keypaste agent` unlocks a vault and asks in its terminal. Requests can reuse a live connection-scoped grant or match a user-written policy. In source, the desktop asks about each credential request for the vault it has unlocked in a prompt window showing the client, its label, the entry, the field, the lifetime and the agent's reason. Only Approve, which works a second after the prompt appears, releases. Deny, closing it, the timeout, a lock and the client giving up each refuse and take it down, and a bridge that hangs up withdraws its request at `keypaste agent` too ([4.4](steps/4.4.md)). | The desktop ignores `policy.toml`, so every release from the app needs a person. Pending requests, grants and audit history are not shown in the app. |
| Unlock lifecycle | Terminal approver holds its vault until stopped. MCP bridge holds no vault and cannot trigger a master-password prompt. In source, one vault has one owner: the desktop and `keypaste agent` each claim it before the password is read, and the second is refused naming the first. The bridge reaches whichever holds the vault named by its `--vault`, and its audit line names the unlocked session that answered ([U.1](steps/U.1.md)). The app starts and ends that session as it unlocks, locks and quits; Agent Activity names the process and session answering agents, read from the session rather than the pipe, and the unlock screen names `keypaste agent` holding the vault. A crashed app answers nothing, and relaunched it starts locked ([4.4b](steps/4.4b.md)). | Terminal approver has no idle lock; in source, stopping it withdraws a request waiting at its prompt and zeroes its grants, as a desktop lock does. The owner serves the vault as it was when unlocked. In source, a bridge with no `--vault` is refused. |
| Updated values | A fresh CLI invocation reads the saved file. In source, the process holding a vault answers agents only while it matches the file: a desktop edit is the next value released and its grants are asked about again, and a save by another program is refused as `vault-changed` until the vault is reopened. | The app shows no notice of an external save until one of its own saves is refused. Launches through the session do not exist yet. |
| Local records | Calls, including refusals, enter a local hash-chained audit log. Missing audit writes deny release. | No protection against a fully recomputed log or missing tail without an independent anchor. |
| Client setup | CLI `setup` configures supported clients or prints manual configuration. | No desktop connection flow. Printed formats do not establish every client/platform installation. |

Implementation: [MCP tools](../src/Keypaste.Mcp/Program.cs), [terminal approver](../src/Keypaste.Cli/Commands/AgentCommand.cs), [approval decision](../src/Keypaste.Core/Approval/ApproverHandler.cs), [vault claim](../src/Keypaste.Core/Ownership/VaultClaim.cs), [session check](../src/Keypaste.Core/Ownership/SessionAuthority.cs), [desktop endpoint](../src/Keypaste.App/Session/SessionHost.cs), [desktop prompt](../src/Keypaste.App/Session/WindowApprovalChannel.cs), [desktop session authority](../src/Keypaste.App/Session/AppAuthority.cs), [desktop activity status](../src/Keypaste.App/ViewModels/AgentActivityViewModel.cs), [setup](../src/Keypaste.Cli/Commands/SetupCommand.cs). Existing suites exercise exposure, consent, refusal, grant expiry, policy and audit behavior. [Approvals](approvals.md) and [MCP setup](mcp-setup.md) describe the terminal workflow and, in source, the desktop's prompt.

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
