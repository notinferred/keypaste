# Capabilities

Each capability by surface, with its state in [RELEASE](RELEASE.md)'s terms and the version it belongs to, reviewed against source and tests on 2026-11-12. [PRODUCT](PRODUCT.md) owns scope, [ROADMAP](../ROADMAP.md) what comes next, [CHANGELOG](../CHANGELOG.md) each change, and [SECURITY](../SECURITY.md) and [THREATS](../THREATS.md) the guarantees and limits.

Two states appear below:

- **Published `v0.3.0`**: in the CLI and MCP bridge download, which is also installation-verified on runner images for all four targets ([RELEASE](RELEASE.md#published-releases)).
- **Implemented (unreleased)**: in source, headed for 1.0.0, and in no download. Every desktop capability is also in the internal, unsigned desktop packages, which are Packaged, not Published.

A source feature is not in a downloaded binary until a release publishes it.

## CLI

| Capability | State | Gap |
|---|---|---|
| Create and open a vault: `init`, `--vault` or `KEYPASTE_VAULT` | Published `v0.3.0` | |
| Key files: `--keyfile` or `KEYPASTE_KEYFILE` on every verb that opens a vault, all four KeePass key file forms, a vault a key file alone protects | Implemented (unreleased) | `init` takes no key file; `access` attaches one afterwards. A vault that needs a YubiKey is refused with a pointer to the desktop app |
| Default vault: the one chosen in the app or with `keypaste use` | Implemented (unreleased) | |
| Change access: `keypaste access` changes the master password and adds, replaces or removes a key file, keeping the replaced file as a backup | Implemented (unreleased) | Never removes a password or attaches an arbitrary file keyed by its hash |
| Items: `add`, `ls`, `get` (clipboard, cleared after twenty seconds, or `--show`), `rm` (permanent) | Published `v0.3.0` | |
| Items: `get --reveal` in place of `--show`; `rm` moves to Trash; `search`, `show`, `rotate`; custom fields with `set --field`, `get --field`, `field ls`, `field rm`; `attachment` (up to 5 MiB); `get <entry> totp` | Implemented (unreleased) | No CLI verb to restore from Trash, empty it, organize groups, or restore and export backups: the desktop does these |
| Generate a character password: `--generate`, `--length`, `--no-symbols`, `--no-lookalikes` | Published `v0.3.0` | |
| Passphrases from the EFF word list: `--words`, `--separator`, and `keypaste generate`, which stores nothing | Implemented (unreleased) | |
| Projects: `env pull`, `env set`, `env ls`, `env rm`, `env export --dotenv`, and `run <project> -- <command>`, each variable an item under `env/<project>` | Published `v0.3.0` | |
| Projects by tag: a variable is an env-named field of an item tagged `env:<project>` or `env:<project>:<environment>`; `env tag`, `env untag`, `-p` for an environment, `env diff`, protected `prod` and `production` | Implemented (unreleased) | An item of the `v0.3.0` layout stays an ordinary item and is no longer a variable. No live global environment |
| References: `env export` writes `kp://` references by default and plaintext only with `--dotenv`; `run --env-file` or a mapped directory's `.env.keypaste` | Implemented (unreleased) | Exported plaintext outlives a lock |
| `run --session`: the app or `keypaste approve` holding the vault asks, with no password typed in the terminal | Implemented (unreleased) | |
| Scoped tokens: `token create` for CI, `run --token`, runs without a prompt except for a protected environment | Implemented (unreleased) | A token's run asks nobody, by design ([THREATS](../THREATS.md) T-32) |
| Edits while the vault is held: `add`, `set`, `rm`, `rotate`, `field rm` and the `env` writes go to the app or `keypaste approve`, which asks you and saves | Implemented (unreleased) | One request of at most 64 KiB, so a larger `.env` is refused while the vault is held |
| Import: `keypaste import` of another KDBX file or another password manager's export | Implemented (unreleased) | Cannot open a file that needs a YubiKey; the desktop's import can |
| Share links: `keypaste share` encrypts one field or a login on this machine and uploads only ciphertext to keypaste.com; links expire, open a set number of times and can be revoked | Implemented (unreleased) | |
| `keypaste lock`, `status`, `doctor` | Implemented (unreleased) | |
| Audit log: `log` with `--since`, `--denied`, `--client`; `log verify` | Published `v0.3.0` | |
| `log --json`, `log verify --expect <hash>` | Implemented (unreleased) | A fully recomputed log or a removed tail is caught only against a hash you kept |

## MCP bridge and approvals

| Capability | State | Gap |
|---|---|---|
| Bridge: the `keypaste-mcp` binary, holding no vault and unable to ask for a master password | Published `v0.3.0` | |
| Bridge as `keypaste mcp` inside the one `keypaste` binary; `keypaste doctor` flags a client still registered with `keypaste-mcp` | Implemented (unreleased) | |
| Tools: `list_entry_names` (names, never a value) and `request_credential` (one field of one item) | Published `v0.3.0` | |
| Tools: `request_attachment` (one attachment by name, up to 5 MiB), `request_save` (you save a value the agent never sees), and `run` on a bridge started with `--allow-run` | Implemented (unreleased) | A run's command can reveal a value in a form the output scrubber does not know (T-35) |
| Terminal approver `keypaste agent`: asks `Approve? [y/N]`; a grant lasts for a time-to-live; `policy.toml` rules release without asking | Published `v0.3.0` | |
| Terminal approver `keypaste approve` and the desktop's prompt window: Deny or Allow once, and for an agent whose bridge has a label, Always allow or Always block for that agent and that item's field, each confirmed; a request waits its turn, up to three per agent, each with 45 seconds; when several items answer, you pick one | Implemented (unreleased) | An agent's label is whatever its configuration says, so answers kept for a label follow it (T-36) |
| Runs and terminal edits are asked each time, with no standing answer: a run offers Deny and Allow once | Implemented (unreleased) | |
| Agent settings: each kind of value (passwords, custom fields, project variables, card values, identity values, TOTP codes, notes, attachments) is Ask, Allow or Block for every agent, with per-agent rules and kept answers; Allow takes effect only once confirmed; passkeys are always blocked; kept in `~/.keypaste/agents.toml` on this computer, sealed with a key the vault holds; `keypaste clients` | Implemented (unreleased) | A value whose kind is Allow is released with nobody asked, and recorded |
| No timed grants: no TTL, `--max-ttl` or `keypaste grants` | Implemented (unreleased) | |
| Exposure: `--expose` globs; by default the `env/` subtree | Published `v0.3.0` | |
| Exposure: a bridge started by hand defaults to `tag:env:*`; `setup` and the desktop's Connect register `--expose '**'`, so an agent sees every item's title, group, tags and URL (and username unless hidden) and its agent settings decide the values | Implemented (unreleased) | |
| Client setup: `keypaste setup` registers Claude Code and Codex through their own commands and prints configuration for Cursor and Claude Desktop | Published `v0.3.0` | |
| Client setup for Gemini CLI, Devin, VS Code, Cursor and Claude Desktop (macOS and Windows), keeping the tool's original file beside it; the desktop's Add an agent | Implemented (unreleased) | CI checks VS Code's and Claude Desktop's registrations at the file level only, and Cursor's through its own CLI only on Linux and macOS. On Windows a tool installed with pnpm or yarn is refused; the macOS app started from Finder misses tools installed through a Node version manager |
| One vault, one holder: the desktop or `keypaste approve` claims the vault before its password is read, and the second is refused naming the first | Implemented (unreleased) | `keypaste approve` has no idle lock. KeePassXC can still save behind the holder, which then refuses agents until it has merged or been restarted |
| Audit log: every call, allowed or refused, is a line in `~/.keypaste/audit.jsonl`, hash-chained, never holding the released value; a failed write refuses the release | Published `v0.3.0` | Agent-written arguments can hold sensitive text |

## Desktop app

Every row is Implemented (unreleased) and in the internal packages; none is Published. [The desktop guide](desktop.md) describes the app.

| Capability | Gap |
|---|---|
| Open and create: password, key file, both, or a YubiKey slot programmed for HMAC-SHA1; the databases KeePassXC last opened are listed first; on Windows, Windows Hello unlocks again after a lock | Not run against a physical YubiKey, so real-device unlock, save and KeePassXC interoperability are unverified. One key per vault |
| Change access in Settings: password, key file and YubiKey, with what the change costs the backups stated first | A key-file-only vault's change is guarded by the key file alone |
| Items: kinds (Login, API key, Database, Server, Secure note) kept as KeePass icons, custom fields, tags, URL links, one-time codes (TOTP) with the live code, attachments, duplicate, groups, rename and move, undo of the last delete, rename or move | No auto-type editor |
| Find: search over titles, group paths, usernames, URLs and tags, never reading a password, note or protected field | |
| Copy and reveal: the clipboard clears after twenty seconds, on lock and on quit; a reveal hides after 30 seconds, on leaving the item, on losing focus and on lock; copies carry the Windows, macOS and KDE password-manager clipboard markers | The CLI's copies on macOS and Linux carry no marker; whether a clipboard manager honours one is a manual check |
| Generator: character passwords with length, character kinds and lookalikes, and passphrases; rotate replaces a password and keeps the old one in history | Rotation changes the vault, not the credential at its provider |
| History: list, reveal and restore a revision, as KeePass and KeePassXC restore it | History is in the same file, not a backup |
| Trash: delete into the KDBX recycle bin, restore, erase, empty | |
| Save safely: a stale copy refuses to overwrite; each save first keeps a copy in `<vault>.backups` (the last five, at most every fifteen minutes); another program's save is merged with the open copy, field by field; a vault file that vanishes can be saved back | A file the app will not merge, such as another vault, waits for a reload |
| Restore and export: from the unlock screen, restore a backup over the vault; from Settings, export an exact encrypted copy | Nothing recovers a forgotten password or a lost key file |
| Lock: manual, idle (30 minutes by default) and optional on minimize; a lock stops answering agents | Cannot remove values already delivered to a child process, clipboard consumer or agent |
| Recommendations: secrets found in notes, a project tag a merge dropped, a sync tool's conflicted copy | YAML-style or multi-line values in notes are not found |
| Projects screen: every key against every environment, import and export, and Run or Open terminal with the set after a confirmation naming the command, directory and variables | Opens only Terminal on macOS, `cmd.exe` on Windows and the first Linux terminal emulator found; locking does not stop a terminal already started |
| Agents: Add an agent, connections, each agent's settings and kept answers, the waiting request, and Activity, the log of what agents and tokens asked for | |
| Prompt window for credentials, attachments, saves, runs and terminal edits | |
| Share links, scoped tokens and browser fill: pair KeePassXC-Browser with the vault from Settings | That KeePassXC's own browser integration still answers its pairing afterwards is not shown |
| Import from another app: KeePass files and other password managers' exports, with a plan you edit | Checked in headless tests, not observed in an installed app |
| System notifications for a request while keypaste is in the background | No person has seen one; macOS posts them only from a signed build, which does not exist yet |
| Drag and drop, and Open With for `.kdbx` | No real drag or Open With has been run |
| Crash reports: on once you press Continue on the first-launch screen; Settings' Send crash reports turns them off | The terms are a draft until approved |

Each screen is checked in headless tests and frames rendered without a display; the installed packages were driven on Windows, macOS and Linux runners, not on a person's computer ([RELEASE](RELEASE.md#installation-verification)).

## KeePassXC compatibility

`Keypaste.Core.Tests.KeePassXC.WorkflowsTests` runs the workflows below on vaults KeePassXC makes behind a password, a password and a key file, and a key file alone, and after every write KeePassXC reads the whole vault back. Every `ci` run checks it against Ubuntu 24.04's packaged KeePassXC 2.7.6, a push to `main` also against the pinned Windows KeePassXC, and dispatch and release runs against the pinned Windows and macOS KeePassXC 2.8.0. `FieldsTests`, `MergeTests` and `ProjectsTests` in the same namespace check field protection, `otp` and attachments, a KeePassXC save merged with the app's, and projects KeePassXC tagged. These tests run against source; `v0.3.0` passed the compatibility gate of its own release.

| Workflow | CLI | Desktop |
|---|---|---|
| Open | `ls` | Unlock with a password, a key file or both |
| Edit and fields | `env set`, `set --field`, `field rm` | Change the password, notes and URL; add, replace, protect and remove fields |
| Tags | `env tag`, `env untag` | Add and remove a tag |
| Notes review and dropped tags | | Recommendations |
| History, organize, delete and recover | `rm` | Restore a revision; rename and move; Trash |
| Backup, restore, change access, create | `init`, `access` | Restore panel, export, Settings, Create |

After each write, KeePassXC still finds the custom data, custom fields, tags and auto-type on the entries, exports both attachments byte for byte, keeps its cipher and key derivation, and reads KDBX 4. A KDBX 3.1 vault is saved as KDBX 4.0 with its original kept as the backup, a Twofish vault stays Twofish, and KeePassXC-Browser's data survives every write.

Refused with the vault left byte-identical: a wrong password or key file; attaching an arbitrary file keyed by its hash; writing a field named `Password` or one of KeePassXC's own attributes such as `otp` from the CLI; a malformed project tag; removing the only factor of a key-file-only vault; `add` over an existing item; a move or Trash restore onto a taken name; restoring a backup with a wrong password; creating over an existing vault.

Not exercised: a real YubiKey, ChaCha20, an Argon2 vault made by KeePassXC (`keepassxc-cli` cannot choose the key derivation), custom icons. This is a supported subset, not KeePassXC parity.

## Older command names in v0.3.0

The guides use the 1.0.0 names. `v0.3.0` has these instead:

| `v0.3.0` | 1.0.0 |
|---|---|
| `keypaste agent` | `keypaste approve` |
| `keypaste-mcp`, a second binary | `keypaste mcp` |
| `keypaste get <entry> --show` | `keypaste get <entry> --reveal` |
| `~/.keypaste/policy.toml` and `keypaste policy ls` | Agent settings in `~/.keypaste/agents.toml`, `keypaste clients` and the desktop's Agents |
| A variable is an item `env/<project>/<NAME>` | A variable is a field of an item tagged `env:<project>`, by default `env/<project>/.env` |
| `rm` deletes for good | `rm` moves to Trash |
