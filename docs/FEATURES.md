# Features

What keypaste can do, on which surface, and in which version, reviewed against source and tests on 2026-11-12. Versions use [RELEASE](RELEASE.md)'s states:

- **`v0.3.0`**: Published, and installation-verified on runner images for all four targets ([RELEASE](RELEASE.md#published-releases)).
- **1.0.0**: Implemented, in no download yet. Desktop capabilities are also in the internal, unsigned desktop packages.

[SECURITY](../SECURITY.md#guarantees-and-limits) and [THREATS](../THREATS.md) own the guarantees and limits. [CHANGELOG](../CHANGELOG.md) lists each change.

## Older command names in v0.3.0

The guides use the 1.0.0 names.

| `v0.3.0` | 1.0.0 |
|---|---|
| `keypaste agent` | `keypaste approve` |
| `keypaste-mcp`, a second binary | `keypaste mcp` |
| `keypaste get <entry> --show` | `keypaste get <entry> --reveal` |
| `~/.keypaste/policy.toml` and `keypaste policy ls` | Agent settings for each vault in `~/.keypaste/agents/`, `keypaste clients` and the desktop's Agents |
| A variable is an item `env/<project>/<NAME>` | A variable is a field of an item tagged `env:<project>`, by default `env/<project>/.env` |
| `rm` deletes for good | `rm` moves to Trash |

## CLI

| Capability | Version | Note |
|---|---|---|
| Vaults: `init`, `--vault` or `KEYPASTE_VAULT` | `v0.3.0` | 1.0.0 adds a default vault, chosen in the app or with `keypaste use` |
| Key files: `--keyfile` or `KEYPASTE_KEYFILE`, all four KeePass forms | 1.0.0 | `init` takes none; a YubiKey vault is refused, pointing to the desktop |
| `keypaste access`: change the master password, add, replace or remove a key file | 1.0.0 | Keeps the replaced file as a backup; never removes the password ([T-28]) |
| Items: `add`, `ls`, `get` to a clipboard cleared after twenty seconds, `rm` | `v0.3.0` | |
| Items: `search`, `show`, `rotate`, custom fields, `attachment` up to 5 MiB, `get <entry> totp` | 1.0.0 | Trash, groups and backups are the desktop's |
| Passwords: `--generate`, `--length`, `--no-symbols`, `--no-lookalikes` | `v0.3.0` | |
| Passphrases: `--words`, `--separator`, and `keypaste generate`, which stores nothing | 1.0.0 | EFF word list |
| Projects: `env pull`, `set`, `ls`, `rm`, `export --dotenv`, and `run <project> -- <command>` | `v0.3.0` | `--dotenv` writes plaintext ([plaintext limits](../SECURITY.md#plaintext-and-file-limits)) |
| Projects by tag: `env:<project>` or `env:<project>:<stage>`, `env tag`, `env untag`, `-p`, `env diff`, protected `prod` and `production` | 1.0.0 | A `v0.3.0`-layout item is no longer a variable |
| References: `kp://` in `env export`, `run --env-file`, `.env.keypaste` | 1.0.0 | [T-31] |
| `run --session`: the app or `keypaste approve` asks, with no password typed | 1.0.0 | [T-30] |
| Scoped tokens: `token create` and `run --token` for CI | 1.0.0 | Asks nobody except for a protected stage ([T-32]) |
| Edits while the vault is held go to its holder, which asks you | 1.0.0 | At most 64 KiB per request ([T-43]) |
| `keypaste import` of a KDBX file or another manager's export | 1.0.0 | Not a YubiKey vault; the desktop's import can |
| Share links: `keypaste share` uploads only ciphertext; links expire, count opens and can be revoked | 1.0.0 | [T-33] |
| `keypaste lock`, `status`, `doctor` | 1.0.0 | |
| Audit log: `log`, `log verify` | `v0.3.0` | 1.0.0 adds `--json`, `--expect <hash>` and `--checkpoints` ([T-5]) |

## MCP bridge and approvals

| Capability | Version | Note |
|---|---|---|
| Bridge, holding no vault and never asking for a master password | `v0.3.0` | `keypaste mcp` in 1.0.0; `doctor` flags a client still on `keypaste-mcp` |
| Tools: `list_entry_names` and `request_credential`, one field of one item | `v0.3.0` | |
| Tools: `request_attachment`, `request_save`, and `run` with `--allow-run` | 1.0.0 | [T-35], [T-54] |
| Terminal approver `keypaste agent`, with time-limited grants and `policy.toml` rules | `v0.3.0` | |
| Approvals: Deny, Allow once, and Always allow for a labelled agent's field, in `keypaste approve` or the desktop | 1.0.0 | Up to three waiting per agent, 45 seconds each ([T-36]) |
| Runs and terminal edits asked each time | 1.0.0 | No standing answer |
| Agent settings: each kind of value is Ask, Allow or Block, with per-agent rules; passkeys always blocked | 1.0.0 | Allow releases with nobody asked ([T-13], [T-14]) |
| Exposure: `--expose` globs | `v0.3.0` | 1.0.0: `setup` and Connect register `--expose '**'` ([T-4]) |
| Client setup: `keypaste setup` for Claude Code and Codex, printed configuration for Cursor and Claude Desktop | `v0.3.0` | |
| Client setup for Gemini CLI, Devin, VS Code, Cursor and Claude Desktop; the desktop's Add an agent | 1.0.0 | On Windows, pnpm or yarn installs are refused; the macOS app opened from Finder misses Node version manager installs ([T-40]) |
| One vault, one holder: a second desktop or `keypaste approve` is refused | 1.0.0 | `keypaste approve` has no idle lock; a KeePassXC save behind the holder stops agents until it merges ([T-29]) |
| Audit log: every call in `~/.keypaste/audit.jsonl`, hash-chained, without the released value | `v0.3.0` | 1.0.0 summarizes lookups older than 30 days ([T-5]) |

## Desktop app

Every row is 1.0.0. [The desktop guide](desktop.md) describes the app. Screens are checked headless, and installed packages were driven on runners, not by a person ([RELEASE](RELEASE.md#installation-verification)).

| Capability | Note |
|---|---|
| Open and create with a password, key file, both or a YubiKey; Windows Hello after a lock | Not run against a physical YubiKey ([T-37], [T-44]) |
| Change access in Settings: password, key file and YubiKey | [T-28] |
| Items: templates, custom fields, tags, URLs, one-time codes, attachments, duplicate, rename, undo | No auto-type editor |
| Find over titles, groups, usernames, URLs and tags | Never reads a password, note or protected field |
| Copy and reveal, cleared or hidden on a timer, on lock and on quit, with clipboard markers | The CLI's macOS and Linux copies carry no marker ([T-19]) |
| Generator and rotate | Rotation changes the vault, not the provider ([T-12]) |
| History: list, reveal and restore a revision | History is in the same file, not a backup |
| Trash: delete, restore, erase, empty | |
| Safe saves: backups in `<vault>.backups`, merges with another program's save, refusal of a stale or lossy save | Data KeePassLib doesn't know, such as a newer KDBX version's, is left out of the save and kept only in its backup |
| Restore a backup from the unlock screen; export an encrypted copy | Nothing recovers a forgotten password or lost key file ([T-26], [T-37]) |
| Lock: manual, idle (30 minutes by default), optionally on minimize | Delivered values stay delivered ([T-23]) |
| Security: secrets in notes, dropped project tags, weak, reused or expiring passwords, weak key derivation, sync conflicts | YAML-style or multi-line values in notes are not found |
| Environments: stages, masked variables, import, export, Run and Open terminal | Locking does not stop a started terminal ([T-23]) |
| Agents: Add an agent, settings, kept answers, the waiting request, Activity | |
| Prompt window, saying Verified when keypaste checked who asks | Verified never shown for a real AI tool ([T-3]) |
| Share links and scoped tokens | |
| Import from KeePass and other managers, with an editable plan | Not observed in an installed app |
| System notifications while in the background | No person has seen one ([T-51]) |
| Drag and drop, and Open With for `.kdbx` | Never run for real |
| Crash reports, off unless chosen | The terms are a draft |

## Browser extension

1.0.0, with the desktop app.

| Capability | Note |
|---|---|
| Browser fill: pair keypaste's extension once per browser, then fill a site's logins from every open vault | Tested in Chrome, Edge, Brave and Firefox under test IDs; not in any store (8.1g) ([T-48], [T-61]) |

## KeePassXC compatibility

These tests run against source; `v0.3.0` passed its own release's gate. This is a supported subset, not KeePassXC parity. `Keypaste.Core.Tests.KeePassXC.WorkflowsTests` runs the workflows below on vaults KeePassXC made behind a password, a password and key file, and a key file alone, and KeePassXC reads the whole vault back after every write.

| Run | Against |
|---|---|
| Every `ci` run | Ubuntu 24.04's packaged KeePassXC 2.7.6 |
| A push to `main` | Also the pinned Windows KeePassXC |
| Dispatch and release runs | The pinned Windows and macOS KeePassXC 2.8.0 |

| Workflow | CLI | Desktop |
|---|---|---|
| Open | `ls` | Unlock with a password, a key file or both |
| Edit and fields | `env set`, `set --field`, `field rm` | Change the password, notes and URL; add, replace, protect and remove fields |
| Tags | `env tag`, `env untag` | Add and remove a tag |
| Notes review and dropped tags | | Security |
| History, organize, delete and recover | `rm` | Restore a revision; rename and move; Trash |
| Backup, restore, change access, create | `init`, `access` | Restore panel, export, Settings, Create |

**Kept after each write:** custom data, custom fields, tags, auto-type, attachments byte for byte, cipher and key derivation, and KeePassXC-Browser's data. A KDBX 3.1 vault is saved as KDBX 4.0, its original kept as the backup.

**Refused, with the vault left byte-identical:** a wrong password or key file; a key file keyed by its hash; a `Password` or `otp` field written from the CLI; a malformed project tag; removing a key-file-only vault's only factor; `add` over an existing item; a move or restore onto a taken name; restoring a backup with a wrong password; creating over an existing vault.

**Not exercised:** a real YubiKey, ChaCha20, an Argon2 vault made by KeePassXC, custom icons.

[T-3]: ../THREATS.md#t-3--confused-deputy-a-malicious-or-impersonating-client
[T-4]: ../THREATS.md#t-4--over-exposure-of-the-listing-surface
[T-5]: ../THREATS.md#t-5--audit-log-tampering
[T-12]: ../THREATS.md#t-12--a-standing-approval-used-under-a-reason-nobody-read
[T-13]: ../THREATS.md#t-13--an-allow-covers-a-kind-of-value-not-the-values-you-pictured
[T-14]: ../THREATS.md#t-14--an-allow-is-a-standing-grant-to-anything-that-can-reach-the-owner
[T-19]: ../THREATS.md#t-19--clipboard-scraping
[T-23]: ../THREATS.md#t-23--what-auto-lock-is-for-and-what-it-is-not
[T-26]: ../THREATS.md#t-26--restoring-a-backup-while-the-app-is-locked
[T-28]: ../THREATS.md#t-28--a-keyfile-that-is-any-file-at-all-is-one-edit-from-losing-the-vault
[T-29]: ../THREATS.md#t-29--who-holds-a-vault
[T-30]: ../THREATS.md#t-30--a-run-asks-the-session-for-a-whole-env-set
[T-31]: ../THREATS.md#t-31--a-committed-reference-file-chooses-what-a-run-injects
[T-32]: ../THREATS.md#t-32--a-scoped-token-is-a-bearer-credential
[T-33]: ../THREATS.md#t-33--whoever-holds-a-share-link-can-open-it
[T-35]: ../THREATS.md#t-35--a-run-keeps-values-out-of-the-result-not-out-of-the-agents-reach
[T-36]: ../THREATS.md#t-36--an-agents-settings-bind-only-the-bridge-that-names-them
[T-37]: ../THREATS.md#t-37--a-yubikey-protects-a-copied-vault-not-an-unlocked-one-and-a-lost-key-locks-it
[T-40]: ../THREATS.md#t-40--setup-edits-another-programs-configuration-file-and-starts-its-command
[T-43]: ../THREATS.md#t-43--a-terminal-asks-the-owner-to-change-the-vault
[T-44]: ../THREATS.md#t-44--windows-hello-stands-in-for-the-master-password-after-a-lock
[T-48]: ../THREATS.md#t-48--the-browser-relay-holds-released-passwords-in-plaintext-and-anything-can-start-it-or-ask-to-pair
[T-51]: ../THREATS.md#t-51--a-notification-is-read-by-more-than-the-person
[T-54]: ../THREATS.md#t-54--an-agent-asks-the-person-to-save-a-value
[T-61]: ../THREATS.md#t-61--a-login-filled-on-a-site-before-fills-there-at-once
