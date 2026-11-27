# The desktop app

The desktop app opens the same `.kdbx` vaults as the CLI and KeePassXC. While a vault is unlocked, the app also answers your AI agents for it: an agent asks through `keypaste mcp`, the app shows you the request, and nothing is released unless you allow it or an agent setting you confirmed allows it.

The app arrives with keypaste 1.0.0, which is not published yet; until then you can [build it from source](#build-it-from-source). [FEATURES](FEATURES.md) lists what each version has.

## Install

The packages are a Windows MSI, a Linux AppImage and a macOS DMG holding `keypaste.app`. They include the .NET runtime. [RELEASE](RELEASE.md) owns what is packaged, signed, published and installation-verified.

| Platform | What it needs |
|---|---|
| Windows | Nothing more |
| macOS | Nothing more |
| Linux | `libx11-6 libice6 libsm6 libfontconfig1`, and an X11 or XWayland session: the app has no Wayland backend of its own, so a Wayland desktop without XWayland cannot start it. The AppImage also needs FUSE: `/dev/fuse` and a setuid-root `fusermount3`, which `fuse3` provides; its runtime carries libfuse 3, so `libfuse2` is not needed |
| YubiKey on Linux | `libpcsclite1`, and access to the key's `/dev/hidraw*` device, which Yubico's udev rules grant |

The macOS `keypaste.app` is signed with keypaste's Developer ID and notarized, and the DMG carries its stapled ticket, so macOS opens a downloaded copy once you confirm it. The MSI and the programs in it are signed with keypaste's Authenticode identity. The AppImage is unsigned; check it against its checksum and attestation ([SECURITY](../SECURITY.md#verifying-a-release)).

To upgrade, install a newer MSI, or replace the AppImage or `keypaste.app`. Upgrading or uninstalling never touches your vault or `~/.keypaste`.

## Get started

### First start

The first start shows Terms of use, with Send crash reports as a separate choice that stays off unless you check it. The unlock screen then offers Create a vault… and Open a vault…, and lists the databases KeePassXC last opened.

<a id="creating-a-vault"></a>

### Create a vault

Choose Create a vault…, pick where the file goes and type a master password twice. Nothing can recover that password.

- **Key file.** Key file… attaches one you have or makes a new one. The vault then needs both the password and the key file. Keep a copy of the key file away from the vault.
- **YubiKey.** YubiKey… makes the vault also need a programmed key slot.

Nothing is written until every check passes.

### Open and unlock a vault

Drag a `.kdbx` onto the window, choose Open a vault…, or pick it from the recent list.

- **Key file.** Choose it with Key file… under Unlock.
- **YubiKey.** Choose YubiKey… (slot 2 by default), programmed for HMAC-SHA1 challenge-response, and touch the key when asked. Every save asks again. The CLI reaches such a vault only through the app while the app holds it. No physical YubiKey has been tested.
- **Windows Hello and Touch ID.** After an idle lock or a lock you made, Unlock with Windows Hello, or Unlock with Touch ID on a Mac, reopens the vault. Quitting, `keypaste lock`, a changed password or key file, and on a Mac an added or removed fingerprint need the master password. Bringing keypaste forward yourself, from the Dock, the menu bar or tray icon, your browser's Unlock… or the notification that an agent asked for a locked vault, asks Windows Hello or Touch ID at once where they can reopen it, unless another vault is unlocked. If you cancel it, the button stays. An agent's request alone never asks.

One vault has one approver at a time: while `keypaste approve` holds it in a terminal, the app cannot, and the other way round.

### Use several vaults

Add vault…, at the foot of the sidebar or in the File menu, opens or creates another vault. Each vault has its own connection and settings for agents.

Search covers every unlocked vault. Settings, Agents and Security act on the vault you pick at their top, and so do Settings' Share links and Scoped tokens. An item's Share… shares from the item's own vault, and closes if that vault locks. A vault's heading menu locks or closes just that vault; Lock locks them all.

When an agent asks for a locked vault, keypaste shows one notice. You see another after a vault opens, or after every vault locks again once that notice is gone. With another vault unlocked, Open keypaste on the notice only brings the window forward; unlock the asked vault from its own unlock form.

### Use keypaste in a terminal

A project's copied run command and the other `keypaste` commands in this guide need `keypaste` on the terminal's `PATH`. The MSI puts it there on Windows. On macOS and Linux, Settings › keypaste in a terminal links it: on macOS as `/usr/local/bin/keypaste`, after an administrator password, and on Linux as `~/.local/bin/keypaste`. Until it is linked, copying a run command offers Link the keypaste command… there.

On a Mac, move keypaste.app to Applications and open it from there before you link the command or turn on Open at login. A copy run from the mounted disk image, or from the temporary copy macOS makes of a downloaded app, can't be linked or opened at login, because it is gone once you eject or quit it.

<a id="what-the-screens-show"></a>
<a id="layout"></a>

## Find and use items

The rail at the left holds Items, Agents, Security and Settings. On Items, the sidebar lists New, Favorites, the vault's places and Trash. Other lists the identities, SSH keys and Wi-Fi networks no other place holds, and the list shows how many items it holds beside its title, with the search under it.

A vault is one flat list of items, organized by tags. The app shows no groups, and leaves the groups a file already holds untouched for KeePassXC.

### Search

Find searches titles, group paths, usernames, websites and tags. Passwords, notes and protected fields are never searched. A row never shows a secret value.

While you type, `Down` and `Up` choose through the matches and `Return` chooses the first, with the keyboard still in the search. A search that hides the chosen item chooses its first match instead. In the search, `Ctrl/Cmd+C` copies the chosen item's password unless you have selected some of the search's text.

### Copy and reveal

Select an item, then press a field's row to copy it; `Ctrl/Cmd+C` copies the password, `Shift+Ctrl/Cmd+C` the username and `Ctrl/Cmd+T` the one-time code. A password stays hidden until you press Reveal, and hides again when you press Hide, leave the item or the window, and on lock.

A copied secret clears after twenty seconds, and sooner on lock or quit. Each copy asks clipboard managers not to keep it. Nothing clears it if the app is killed, and keypaste cannot make a clipboard manager honour the request ([THREATS](../THREATS.md) T-19).

<a id="new-item-and-the-generator"></a>

### Add an item

New in the sidebar offers Login, API key, Environment, Card and Note, and More templates… (`Shift+Ctrl/Cmd+N`) opens the full grid. Save in, at the top of the form, picks the vault.

- A template only presets fields; Add field… adds any field.
- An API key is kept as a protected field named like a variable, such as `OPENAI_API_KEY`.
- A card or identity is tagged so agent settings treat its protected fields as card or identity values.
- Any item with a username, a password and a website fills in your browser.

The password generator, opened from a password field, makes random characters or an EFF word-list passphrase.

### Edit an item

Edit, change the fields, then Save.

- **Custom fields.** Add field takes a name, a value and a Protected switch that starts on. Renaming a field a project reads as a variable first lists what the rename breaks.
- **One-time code.** Paste a site's setup key or `otpauth://` link, and the item shows the live code.
- **Tags.** A project tag such as `env:billing:prod` shows as its project and stage.
- **Rotate.** Replaces the password with a generated one and keeps the old one in history. It changes the vault only, not the credential at its provider.

### Rename, delete and undo

Rename… in an item's ⋯ menu changes its title. Agent settings and a bridge's `--expose` match titles, so a rename can take an item out of an agent's view or bring it in; the form says so.

Delete moves an item to the vault's Trash, which KeePassXC reads as its Recycle Bin. Restore puts it back. A vault that has used Trash is saved as KDBX 4.1, which KeePassXC 2.7 and KeePass 2.48 and later read.

Edit › Undo (`Ctrl/Cmd+Z`) reverses the last delete, rename or duplicate, until the next write.

### Restore an earlier version

Show history lists an item's earlier revisions, and Restore this revision makes one current. As in KeePass, the item becomes that revision whole, so attachments and fields added since are dropped.

### Share a field

Share… makes a link to one field, with an expiry, a number of views and an optional passphrase. The field is encrypted on this machine and only ciphertext goes to api.keypaste.com; the key is in the link. Whoever holds the link can open it until its views or time run out ([THREATS](../THREATS.md)).

### Import from another app

File › Import… takes another KeePass file or another password manager's export and previews where each item will land. Nothing is replaced, and the import copies everything or nothing. An encrypted export is refused with how to make one keypaste reads.

After importing an export, delete it: it holds your passwords unencrypted.

<a id="environments"></a>

## Projects and environments

Environments lists the vault's projects. Pick a stage, such as `dev` or `prod`, and everything you do goes to it.

- **Variables.** The stage's table shows each name, its masked value and the item it comes from, and flags "Missing in prod" or "Same as staging".
- **Items in each stage.** Edit › Items in each stage adds or removes another item's stage tag, after naming what changes.
- **`.env` files.** Edit › Import .env… reads a file through the same checks as `keypaste env pull` and previews each name before writing. Edit › Export .env.keypaste… writes references, never a value.

### Run a command with a project's variables

Run with keypaste shows the stage's command, such as `keypaste run -p dev <project> -- npm start`, to copy. While the app holds the vault, `keypaste run` asks the app instead of opening the vault, so you type no password in the terminal, and Activity lists the run. The command adds `--vault '<this vault's path>'` when this vault isn't the one chosen for the terminal, or when another open vault has a project of the same name, so the run reaches this vault. When the vault's file path or the project's name holds characters a terminal could run or change, the app shows no command and says why. The app asks before releasing the variables to a command run in a terminal, every time.

Run… asks for a directory and a command and saves them; from then on the button reads Run and opens a terminal running the command there. Pressing Run, or Edit › Open terminal, is your answer, so it starts at once, and Activity lists it as You pressed Run. A protected stage such as `prod` still asks first, on a card.

Run again, in the Dock menu on a Mac and in the menu bar or tray icon's menu, runs the last command Run started once more, in the same project, stage and directory. It is off until something has run, while that vault is locked, and after a Run of a protected stage, which asks on its card in the project.

The values go into that terminal's environment, so everything run there can read them. Locking the app does not take them back.

## Answer an agent's request

When an agent asks, the app shows who is asking, the item and the agent's own words.

| Answer | What it does |
|---|---|
| Deny | Refuses; works at once, as does `Escape` |
| Allow once | Releases this one value |
| Always allow | For an agent whose bridge has a label: keeps allowing that field to that agent |

A click on an allow works once the prompt has focus; Return and Space wait a second. [Approvals](approvals.md) owns the prompt, what each answer keeps and the log.

## Set up agents

Agents lists the AI tools on this computer and the agents that asked this vault recently.

- **Connect an AI tool.** In Agents, the + (Connect an AI tool…) opens the Connect sheet: a setup prompt to paste into an AI tool, the server entry to add by hand, and the tools found on this computer. Connect… shows what Add it for me changes, and nothing is written until you press it ([MCP setup](mcp-setup.md)).
- **Choose what an agent gets.** An agent's page sets what it gets of each kind of value and whether it may run commands. Settings › Agents sets the defaults. Agent settings are kept on this computer, not in the vault ([agent settings](policy.md)).
- **See what happened.** Agents › Activity shows the audit log, newest first, each line under who acted: an agent by its name, and your browser's pairings and fills as you, in that browser. Agents, Your browser and You narrow it to one of them. Your browser's lookups, which list a page's logins by name and release no password, stay hidden until you choose Show browser lookups, and the line above the table counts what is hidden. After 30 days, a day of lookups reads as one line, such as you, in Chrome on studio, 14 lookups, as an agent's do; fills keep their own lines. A record the hash chain does not vouch for says Edited and is never hidden. `keypaste log` reads the whole log at any time.

## Review security findings

Security, on the rail, lists what needs attention, never by value:

- keys left in notes
- weak, reused, expired and soon-expiring passwords
- a vault whose password protection is weaker than keypaste's
- a conflicted copy a sync tool left beside the vault

Each finding offers its fix, and keypaste changes nothing unasked. Move to a field writes a note's key to a protected field. Strengthen upgrades the vault's protection once you confirm that KeePass before 2.35 can no longer open it, and keeps a copy of the file as it was. Dismiss hides a finding on this machine.

<a id="editing-your-vault"></a>

## Working beside the CLI and KeePassXC

The app and the CLI write through the same core, so a fresh `keypaste` command reads the app's saved change. A form open in the app saves only the fields you changed, so the last save of a field wins and the other stays in history.

While the app holds the vault, CLI commands that edit it send their change to the app, which saves it once you allow it. `keypaste access`, `import`, `token` and `share` are refused instead: make the change in the app, or run `keypaste lock` first.

When another program such as KeePassXC or a sync tool saves the vault, the app merges that save within a minute. Where both changed the same field, the newer version wins and the other goes to history. If the other program changed the password or key file, the app locks and you unlock with the new ones.

### Backups

The first save after each unlock copies the vault into `<vault>.backups` beside it, keeping recent, daily and weekly copies. Every save checks what it wrote before replacing the vault.

Copies beside the vault do not survive losing the disk. Settings › Export an encrypted copy writes the vault to a new file; keep one on another disk.

### Restore a backup

1. Select the vault on the unlock screen and choose File › Restore a backup….
2. Choose a copy by when it was taken.
3. Type the master password that copy was made under, and choose its key file.
4. Restore replaces the vault with that copy, keeping the replaced file as a backup.

Close KeePassXC first: it does not notice a restore. A backup cannot recover a forgotten master password or a lost key file.

### Change the master password, key file or YubiKey

Settings › This vault › Vault access › Change… changes the master password, key file or YubiKey. It asks the current master password first.

- Earlier copies in `<vault>.backups` still open with the old password and key file. Delete them if those were exposed.
- A lost or broken YubiKey locks the vault, unless a second key holds the same secret in the same slot, which YubiKey Manager makes and keypaste cannot.

## Lock and settings

The vault locks after thirty minutes without keyboard or mouse input; Settings accepts one minute to eight hours, and idle locking cannot be turned off. An agent's request never counts as activity. `Ctrl/Cmd+L` and Lock in the menu bar or tray icon's menu lock at once, so does quitting, and Settings can also lock on minimize.

Closing the window with Keep in menu bar, or Keep in tray, on leaves keypaste running with your vaults unlocked: agents are still answered and Run again still works, until a lock. On a Mac the Dock icon stays while a vault is unlocked, so the Dock and ⌘Tab still reach keypaste. With the setting off, closing the window quits.

Locking refuses waiting and later agent requests until you unlock. Values already released to an agent, a terminal or the clipboard are not recalled. [SECURITY](../SECURITY.md) states the memory and clipboard limits.

Settings also holds Open keypaste at login, crash reports and where backups are kept.

### Files it keeps

The app keeps this computer's settings in `~/.keypaste` (or `KEYPASTE_HOME`), beside the audit log and each vault's agent settings in `agents/`. None of it travels with the vault.

| File | Holds |
|---|---|
| `recent.toml` | Recent vaults, their key file paths and YubiKey slots |
| `app.toml` | App settings and your answer on crash reports |
| `crash-reports/` | Reports waiting to be sent while Send crash reports is on |
| `projects.json` | Each project's saved directory and command |
| `recommendations.json` | Dismissed findings |
| `save-counts/` | Each vault's last seen save count, so an older copy is noticed |

## Keyboard shortcuts

Every command is also in the menus.

| Keys | Action |
|---|---|
| `Ctrl/Cmd+1` … `4` | Items, Agents, Security, Settings |
| `Ctrl/Cmd+F`, `Ctrl/Cmd+K` | Search |
| `Ctrl/Cmd+N` | New item |
| `Ctrl/Cmd+C` | Outside a text field, or in the search with none of its text selected, copy the chosen item's password |
| `Shift+Ctrl/Cmd+C` | Copy the chosen item's username |
| `Ctrl/Cmd+T` | Copy the chosen item's one-time code |
| `Down`, `Up`, `Return` in the search | Choose the next or previous match, or the first |
| `Ctrl/Cmd+O` | Open a vault |
| `Ctrl/Cmd+L` | Lock |
| `Ctrl/Cmd+W` | Close the window; vaults stay unlocked until they lock |
| `Ctrl/Cmd+Q` | Quit |

<a id="removing-keypaste"></a>

## Removing keypaste

First run `keypaste setup --remove`, or Disconnect each tool in Agents, so no AI tool keeps starting keypaste. Then switch off Open at login and quit the app.

On macOS, delete the app and the LaunchAgent, and the link at `/usr/local/bin/keypaste` only while it names an app bundle's `keypaste`, so one you put there yourself stays:

```sh
rm -rf /Applications/keypaste.app
rm -f ~/Library/LaunchAgents/com.keypaste.app.plist
case "$(readlink /usr/local/bin/keypaste)" in *.app/Contents/MacOS/keypaste) sudo rm /usr/local/bin/keypaste ;; esac
```

On Windows, uninstall keypaste from Settings › Apps, which also removes its Start menu shortcut, its PATH entry and Open at login's value. The desktop ZIP has no uninstaller: delete its folder, and Open at login's value with

```powershell
Remove-ItemProperty -Path HKCU:\Software\Microsoft\Windows\CurrentVersion\Run -Name keypaste
```

On Linux, delete the AppImage or the archive's folder, the autostart entry, and `~/.local/bin/keypaste` only while it is the script Settings wrote:

```sh
rm /path/to/keypaste-app-*.AppImage
rm -f "${XDG_CONFIG_HOME:-$HOME/.config}/autostart/keypaste.desktop"
grep -q 'wrote this; a newer keypaste re-points it' ~/.local/bin/keypaste && rm ~/.local/bin/keypaste
```

An AI tool's configuration file that setup or Connect edited keeps its original as `<name>.keypaste-backup`. None of this touches your vault, `<vault>.backups` or `~/.keypaste`. Delete `~/.keypaste` (`rm -rf ~/.keypaste`, or `Remove-Item -Recurse "$HOME\.keypaste"`) only when you want keypaste to forget this computer's settings and log.

## Build it from source

Install the .NET SDK that `global.json` selects, then:

```
dotnet restore keypaste.app.slnx --locked-mode
dotnet build   keypaste.app.slnx -c Release
dotnet run --project src/Keypaste.App -c Release
```

If a build fails with `Access to the path 'artifacts\...' is denied`, close the app and rebuild with `-nodeReuse:false`.

<a id="checking-a-build-by-hand"></a>

The manual checklist a person runs on each platform before a desktop release is in [desktop-checklist](desktop-checklist.md).
