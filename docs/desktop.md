# The desktop app

> This guide describes the desktop app as built from source for keypaste 1.0.0, its first desktop release, which is not yet published. The published v0.3.0 is the CLI and MCP bridge only and has no desktop app. [FEATURES](FEATURES.md) lists what each version has.

The desktop app opens the same `.kdbx` vaults as the CLI and KeePassXC. While it holds a vault unlocked, it is also what answers your AI agents for that vault: an agent connected through `keypaste mcp` asks, the app shows you the request, and nothing is released unless you allow it or an agent setting you confirmed allows it.

<a id="what-it-is-not-yet"></a>

## Current limits

The app has no public release. CI builds internal, unsigned candidates (a Windows MSI, a Linux AppImage and a macOS DMG holding `keypaste.app`) and installs them on fresh runners; [RELEASE](RELEASE.md) owns what is packaged, signed, published and installation-verified.

One vault has one holder at a time. When `keypaste approve` already holds the vault in a terminal, the unlock screen names it and the app leaves it running; while the app holds the vault, `keypaste approve` is refused the same way.

<a id="what-the-screens-show"></a>

## What the screens show

### Layout

The sidebar holds four places, also on `Ctrl/Cmd+1` to `4`: Items, with the vault's group tree and, under a Projects heading, each project; Agents; and at the foot Trash and Settings. Below them sit the vault's file name with its save state, Lock and, while a lock is near, the idle countdown. Everything else opens under one of the four, with a Back button: a project under Items, Activity under Agents, and Settings › Advanced with share links, scoped tokens and diagnostics. The Agents row counts waiting requests and shows a mark that differs by shape: a filled dot while the app answers agents for this vault, a ring while it does not, a diamond while it refuses them.

Items, Agents and Trash show a list beside the chosen thing. Under 840 wide the sidebar collapses to a rail of icons; its first button, or View › Show groups and projects, opens the whole sidebar over the content. View › Hide sidebar hides it at any width. The window reopens where you left it, at its size and with the sidebar as it was. New item and the password generator open as sheets over the window.

### Items

Each row shows the item's kind as its icon, its title, one line under it and, at the right, when an agent last received it ("in use" while a waiting request names it). The list never shows a password, a note or a custom field's value. A menu in the list's header sorts it; View › Reset list order undoes that.

The search above the list covers what the header names, All items or the group in view; Find reaches it from any place. It matches titles, group paths, usernames, URLs and an item's own tags, case-insensitively, and a row says which field or tag matched. Passwords, notes and protected custom fields are never read for matching.

Selecting an item shows its fields in grouped rows; pressing a row copies it. The password is dots until you press Reveal, and it hides again on Hide, after 30 seconds, on leaving the item, when the window loses focus or is minimized, and on lock. A URL that is a web address (`http`, `https`, or no scheme, opened as `https`) is a link to your browser; any other scheme is plain text. The item's `kp://` reference is in its ⋯ menu. Edit turns the fields into inputs, with Cancel and Save.

The pane also lists custom fields by name, each marked protected, plain or KeePassXC's own; every value is dots until revealed. Add field takes a name, a masked value and a Protected switch that starts on. A field's ⋯ menu replaces, generates, renames, switches protection or removes it, each as one revision. Renaming a field a project reads as a variable first lists what the rename breaks. KeePassXC's own attributes can be copied but not edited as fields; a one-time code is set by pasting a site's setup key or `otpauth://` link, kept in the protected `otp` field as KeePassXC 2.8 reads it, and the item shows the live code. Tags are chips you add and remove; a project tag such as `env:billing:prod` reads as its project and environment, and adding or removing one first names what it reaches.

Each item shows its Created and Rotated dates and, when agents can see it, an Agent access card naming each client that received it. Rotate replaces the password with a generated one, keeping the old one in history and never showing the new one; it changes the vault only, not the credential at its provider. `keypaste rotate <entry>` does the same.

### New item and the generator

New item starts from a kind (Login, API key, Database, Server or Secure note) and a title; Add field… adds username, password, URL, one-time code, notes, group, tags, project, a custom field or a protected field. A login, database or server gets a generated password as the form opens, which typing or pasting replaces. An API key is kept as a protected field named like a variable, such as `OPENAI_API_KEY`. Create writes the item in one change and refuses, writing nothing, an empty title, a title its group already has, a key name that is not a variable's, or a tag KeePass would split. Item › Change kind changes the icon without touching fields. A username box suggests usernames the vault already uses.

The generator is the same sheet everywhere, and File › Generate password… opens it alone. Characters takes a length, by slider or number, and letters, digits, symbols and Leave out lookalikes; Words takes six to thirty-two words from the EFF long list and a separator. The sheet states the bits the choice gives. Apply alone puts the value in the field.

### Organizing

File › New group…, or the + on the Groups heading, makes a group in the one chosen. A group's sidebar menu (right-click, Control-click, or Shift+F10) has New group, Rename group…, Move group… and Delete group. Rename or move in an item's ⋯ menu changes its title and group in one write, so the item keeps its identity, history, attachments and the data keypaste does not model. A refusal writes nothing and says why: a name already taken, two items answering to one path, or a name no vault could address. Dragging an item onto a group moves it.

Patterns in an agent's settings and a bridge's `--expose` match group paths and titles, so moving or renaming can take an item out of an agent's view or bring it in; the forms say so. Renaming a group directly under `env` renames that project, so `keypaste run <old name>` stops finding it. A project row in an agent's settings follows tags and is unaffected.

Edit › Undo (`Cmd/Ctrl+Z`) reverses the last delete, rename or move, duplicate, and group create, rename, move or delete, until the next write.

### History

Show history lists earlier revisions, newest first, beside the item, and shows what each changed. A revision's password reveals and hides like the item's. Restore this revision makes that revision current and keeps the replaced state as the newest revision, so a restore can be undone by restoring again. As in KeePass and KeePassXC, the item becomes that revision whole: attachments, custom fields and custom data added since are dropped, as the caption says. When the revision's project tags differ, the app first says which environments the item leaves and joins, and restores only when you confirm. KeePass keeps a bounded number of revisions.

### Trash

Deleting moves an item, with its fields and history, to the vault's Trash (the KDBX Recycle Bin, which KeePassXC reads as its own), and the line that appears offers Restore. Trash lists each item's title, its group and when it went, never a value. Restore puts it back where it was, under its own title even if another item took the name. Delete for good and Empty Trash each ask first. A vault whose Trash is switched off deletes permanently after asking. A vault that has used Trash is saved as KDBX 4.1, which KeePassXC 2.7 and KeePass 2.48 and later read.

### Projects

A project's screen shows every key against every environment, `dev` first, marking a key that is missing, unusable or holding the same value as another environment, never the value. Under a value is the item it lives on where that is not the column's usual one; hovering gives the full path. Values copy when pressed and reveal only on the row's eye button, which Tab, Space, Enter and screen readers reach; one value shows at a time and hides as an item's password does. Add, Replace and Generate work as for fields. Choosing an environment sends every add, replace, remove, import, Run and Open terminal to it.

Under the matrix, each environment's items are listed. Add item tags another item into the environment and Remove takes the tag off, each after naming what joins or leaves; neither deletes anything. A tag that starts `env:` but makes no project, such as `env:billing:Prod`, is listed at the top.

Import .env reads a file through the same checks as `keypaste env pull`, previews each name as new, replacing or unchanged, and writes only on Import; a file with any problem imports nothing, and the file stays where it is unless you then choose Delete. Export .env.keypaste writes the references the environment would export, never a value.

Run with this environment copies a command such as `keypaste run --session -p dev <project> -- npm start`. Its `--session` asks this app instead of opening the vault, so no password is typed in the terminal and nothing starts while the app is locked. It names no vault, so add `--vault <this vault>` where the terminal would find another. Running it opens a prompt naming the project, the variable names, the item each comes from, the command and the directory; the set is released only on Allow once, which is the only allow a run offers.

A project can also save a directory and command on this machine. Run opens a terminal there running the command, and Open terminal opens one at a prompt: `cmd.exe` on Windows, the first of `x-terminal-emulator`, `gnome-terminal`, `konsole` and `xterm` on Linux, and Terminal on macOS through a launcher inside `keypaste.app` that hands Terminal no value. For another terminal, copy the run command. Each first shows a card naming the command, directory, variable names and their items, and starts nothing until Start. The values go into that terminal's environment, so everything run there can read them, and locking the app does not stop it or take them back.

### Share links

Share… on an item makes a link: pick a field, an expiry (1 hour, 24 hours or 7 days), a number of views (1, 3 or 10) and optionally a passphrase of at least 20 characters. The field is encrypted on this machine and only ciphertext goes to keypaste.com; the key is in the link. Settings › Advanced › Share links lists, checks and revokes the links made from this vault. Whoever holds a link can open it until its views or time run out ([THREATS](../THREATS.md)).

### Import from another app

Import, at the sidebar's foot or on an empty vault's Items screen, takes another KeePass file or another password manager's export and previews where each group or item will land, under a group named after the file. A KeePass file needs its password and key file, and a switch keeps editing that file in place instead. An export's preview lists what will not land and why, and an item whose name exists is kept beside it as `title (2)` or skipped; nothing is replaced. Either import copies everything or nothing. After an export import, delete the export: it holds your passwords unencrypted, and keypaste leaves it where it is. An encrypted export is refused with how to make one keypaste reads.

### Agents

Agents lists the AI tools found on this computer and every agent that asked this vault in the last 30 days, each saying whether it is connected, in use or when it last asked. Add an agent offers a setup prompt to copy, an app to connect, or MCP to add by hand. Connect shows the exact command it will register and runs it only when you press it; a tool's Disconnect removes keypaste again ([MCP setup](mcp-setup.md)).

An agent's page sets what it gets of each kind of value and whether it may run commands, each run still asked; Every other agent sets the rest, and an Allow takes effect once you confirm it. These agent settings are kept on this computer, not in the vault ([agent settings](policy.md)). Agents also lists a request waiting in the prompt with its time left, and Show the prompt brings the prompt forward.

When an agent asks, the app shows a prompt window naming the agent, the item, the field and the agent's stated reason. It offers Deny and Allow once and, for an agent whose bridge has a label, Always allow and Always block, each confirmed in the same window. The allows arm a second after the prompt has focus; Deny works at once. [Approvals](approvals.md) owns the prompt, what each answer keeps and the log.

### Activity

Agents › Activity shows `~/.keypaste/audit.jsonl` as a table, newest first, filtered to All, Agents, You or Denied, with sortable, resizable columns; a row the hash chain does not vouch for is marked at its left edge. Verify chain shows the chain's verdict and copies the latest hash for `keypaste log verify --expect`. The app shows Activity only while unlocked; `keypaste log` reads the log at any time. [Approvals](approvals.md) explains what each line records.

### Recommendations

Items › Recommendations lists, never by value, keys left in notes (`KEY=value` lines, and lines that are exactly a GitHub, GitLab, Stripe, Anthropic, OpenAI, AWS, Slack or npm token), a project tag a later save dropped along with other changes, and a sync tool's conflicted copy to review and merge. Move to a field writes each value to a protected field and removes that line in one revision, refusing, with nothing written, when the notes or the field changed. Restore tag names what it reaches first. Dismiss hides a finding on this machine.

### Copying

Copied secrets clear after twenty seconds, with a countdown and Clear now, and sooner on lock or quit, unless something else has replaced the clipboard. Each copy also asks clipboard managers not to keep it: Windows' Clipboard History and Cloud Clipboard, macOS managers that honour the nspasteboard.org types, and KDE's Klipper. Nothing clears it if the app is killed or the machine loses power, and whether a given clipboard manager honours the markers is outside keypaste's control ([THREATS](../THREATS.md)). A copied run command holds no secret and stays.

<a id="editing-your-vault"></a>

## Editing your vault

Everything the app writes goes through the same core as the CLI, so a fresh `keypaste ls`, `get` or `run` reads the saved change, and agents are answered from the vault as saved. An item's form open in the app shows another device's or a terminal's change once the form closes; saving it writes only the fields you changed.

While the app holds the vault, `keypaste add`, `set`, `rm`, `rotate`, `field rm` and the `env` verbs that edit send their change to the app, which names it, never its value, in its prompt and saves it once you allow it. `keypaste access`, `import`, `token` and `share` are refused instead: make the change in the app or run `keypaste lock` first.

If another program saves the vault while it is open, such as KeePassXC or a sync tool, the app merges that save with its own copy within five seconds, matching items by identity and keeping both sides' changes to different fields of one item. Where both changed the same field the newer version is current and the other is in its history; an item one side deleted after the other changed it is kept, in Trash. A merge that carried your changes saves them, keeps the replaced file in `<vault>.backups` and says what it did; agents are refused as `vault-changed` until it is saved. A file the app will not merge, such as one now holding another vault, is named above the screen with Reload, which discards an edit the app could not save. If the other program changed the password or key file, the app locks and you unlock with the new ones.

Saving keeps the file it replaces: the first save after each unlock, no more often than every fifteen minutes, copies the vault into `<vault>.backups` beside it, keeping the last five, and a save whose copy cannot be written does not happen. Every save opens what it wrote before replacing the vault and keeps the file's permissions.

### Restoring a backup

On the unlock screen, select the vault and choose Restore a backup. Copies are listed by when they were taken; a damaged one is marked. Type the master password that copy was made under and choose its key file, and Check this backup shows when it was taken and how many items, groups and projects it holds, never names or values. Restore this backup replaces the vault with that copy byte for byte, keeping the file it replaces as a backup. A damaged or missing vault can still be selected for restoring when backups sit beside it. Close KeePassXC and stop `keypaste approve` first: each keeps its own copy and does not notice a restore. A backup cannot recover a forgotten master password or a lost key file.

### Changing what unlocks the vault

Settings › This vault › Vault access, behind Change…, changes the master password, adds, makes, replaces or removes a key file, and adds, re-slots or removes a YubiKey. It asks the current master password even though the vault is open. The confirmation says what the change costs: the vault as it was is kept in `<vault>.backups`, and every earlier copy still opens with the old password and key file, so delete them if those were exposed. The app stays unlocked on the changed vault. A vault that loses its key file keeps or gets a password. Adding a YubiKey means a lost or broken key locks the vault, unless a second key holds the same secret in the same slot, which YubiKey Manager makes and keypaste cannot. A running `keypaste approve` keeps the vault it opened until restarted.

Settings also says where the backups are kept, and Export an encrypted copy writes the vault's exact bytes to a new file; keep one on another disk, since copies beside the vault do not survive losing it.

## Opening a vault

Open a vault by dragging a `.kdbx` onto the window, with Open a vault… or `Ctrl/Cmd+O`, from the recent list, or by passing its path to `keypaste-app`. The packages offer keypaste as an Open With choice for `.kdbx` without taking the default from KeePassXC (an AppImage only through an integration tool); a file opened this way is only selected. The file's header is read first, so a file that was never a vault is refused before you type.

The first start shows one screen: continuing agrees to the terms of use for keypaste's online services and to sending crash reports, and the software itself stays under AGPL-3.0. Nothing is sent before Continue. Crash reports are on from then until you turn off Settings' Send crash reports, which also deletes reports waiting to be sent. After it comes the unlock screen. With no vault and no KeePassXC database, its first action is Create a vault…. With KeePassXC databases, it lists the ones KeePassXC last opened on this machine, read from KeePassXC's local settings, which keypaste never writes.

A vault that needs a key file takes it from Unlock with a key file too. All four KeePass key file forms work, and a vault a key file alone protects opens with the password empty. An unusable file is refused when you choose it. A vault keyed to an ordinary file, rather than a KeePass key file, opens with a warning that editing that file loses the vault. The app remembers each vault's key file location.

A vault that also needs a YubiKey opens with Unlock with a YubiKey too, under More options, on slot 2 unless you choose slot 1, programmed for HMAC-SHA1 challenge-response as `ykman otp chalresp` programs it. The screen says Touch your YubiKey while it waits, and every save asks the key again. The CLI cannot ask a YubiKey, so it reaches such a vault only through the app while the app holds it. No physical YubiKey has been tested.

On Windows, after a lock you made, the idle lock, minimize-lock or closing into the tray, Unlock with Windows Hello can reopen the vault once Windows Hello has kept your last unlock. `keypaste lock`, opening another vault, a changed password or key file, and quitting need the master password.

<a id="creating-a-vault"></a>

## Creating a vault

Create a vault asks where the file goes and for a master password twice. Nothing can recover that password. Add a key file attaches one you have, and Create a key file makes a new KeePass XML key file where you choose, never over an existing file; the vault then needs both. keypaste never makes a vault a key file alone opens. Keep a copy of the key file away from the vault. Add a YubiKey makes the vault need a key slot as well, and is refused, writing nothing, when no connected key has the slot programmed.

The refusals are `keypaste init`'s: a path already in use, an empty password, a confirmation that does not match, or a key file keypaste will not attach. Nothing is written until every check passes. The new vault opens on an empty Items list, which offers Import from another app.

## Locking

The vault locks after thirty minutes without keyboard or mouse input by default; Settings accepts one minute to eight hours, and idle locking cannot be turned off. Thirty seconds before, the sidebar's foot shows a countdown that any key or click cancels. An agent's request never counts as activity. A machine that sleeps past the timeout wakes locked.

`Ctrl/Cmd+L` or Lock locks at once. Settings can also lock on minimize, off by default; switching windows and macOS `Cmd+H` do not count as minimizing.

Locking ends the session agents are answered through: a request still waiting is refused as locked, and later requests are refused until you unlock. Values already released to an agent, a terminal or the clipboard's consumers are not recalled, and a separate `keypaste approve` holding another vault is unaffected. [SECURITY](../SECURITY.md) states the memory and clipboard limits.

## Keyboard and menus

Most commands are in the menus: the menu bar on macOS (keypaste, File, Edit, Item, Project, Agents, View, Window, Help) and a Dock menu with Lock and Open vault…; on Windows and Linux the same menus open from the menu button at the window's top left. On macOS the modifier is Cmd; everywhere else, Ctrl.

| | |
|---|---|
| `Ctrl/Cmd+1` … `4` | Items, Agents, Trash, Settings |
| `Ctrl/Cmd++`, `Ctrl/Cmd+-`, `Ctrl/Cmd+0` | Zoom in, out and actual size, up to 200%, kept in `app.toml` |
| `Ctrl/Cmd+,` | Settings |
| `Ctrl+B`, `Ctrl+Cmd+S` on macOS | Hide or show the sidebar |
| `Ctrl/Cmd+N` | New item |
| `Ctrl/Cmd+F`, `Ctrl/Cmd+K` | Search, from any place; also `Option+Cmd+F` on macOS |
| `Ctrl/Cmd+O` | Open a vault; with one open, it locks first |
| `Ctrl/Cmd+L` | Lock |
| `Ctrl/Cmd+W` | Close the window, which locks and leaves the app in the menu bar or tray, or quits where that is off |
| `Ctrl/Cmd+Q` | Quit |
| `Ctrl/Cmd+C` | Outside a text field, copy the chosen item's password |
| `Delete`, `Cmd+Delete` on macOS | Outside a text field, delete the chosen item |
| `Ctrl/Cmd+Z` | Outside a text field, undo the last delete, rename or move |
| `Cmd+M`; `F11` or `Ctrl+Cmd+F` | Minimize on macOS; full screen |
| `Enter` | Unlock, or confirm the form in focus unless its confirm is a danger one |
| `Escape` | Cancel the form in focus, asking first if it holds what you typed |
| `Ctrl/Cmd+V` | Paste into a secret field, the master-password fields included |

<a id="what-you-should-know-about-the-master-password"></a>

## Secret input

Every field that takes a secret uses one control that keeps what you type out of accessibility tools and undo history; a screen reader hears the field's purpose and a row of dots. Pasting works in every secret field; a pasted value loses one trailing line break, and anything else a keyboard cannot type is refused. keypaste clears only what it put on the clipboard, so a master password you copied elsewhere to paste stays there until you copy something else. [SECURITY](../SECURITY.md) describes the memory and input limits.

## What it needs on each platform

The packages include the .NET runtime and embed no browser engine; the app draws with Skia.

| Platform | Native prerequisites |
|---|---|
| Windows | None |
| macOS | None |
| Linux | `libx11-6 libice6 libsm6 libfontconfig1`, and an X11 or XWayland session: the app has no Wayland backend of its own, so a Wayland desktop without XWayland cannot start it. The AppImage also needs FUSE: `/dev/fuse` and a setuid-root `fusermount3`, which `fuse3` provides; its runtime carries libfuse 3, so `libfuse2` is not needed |

A YubiKey on Linux also needs `libpcsclite1` and access to the key's `/dev/hidraw*` device, which Yubico's udev rules grant.

The internal macOS `keypaste.app` is unsigned and unnotarized. A copy downloaded through a browser carries `com.apple.quarantine`, and macOS then says "“keypaste” is damaged and can’t be opened" with no Open Anyway, and refuses to let Terminal run the bundle's launcher. Removing the attribute from the whole bundle lets both run:

```sh
xattr -dr com.apple.quarantine /Applications/keypaste.app
```

The Windows MSI upgrades in place: a higher version replaces the lower one only once the new install commits, and a lower version is refused over a higher one. An AppImage or `keypaste.app` is upgraded by replacing the copy. No package holds your data: the vault, `~/.keypaste` and its files are untouched by an upgrade or an uninstall.

## Files it keeps, and how to delete them

The app keeps this computer's settings in `~/.keypaste` (or `KEYPASTE_HOME`), beside the audit log and `agents.toml`, the agent settings. None of it travels with the vault.

| | |
|---|---|
| `recent.toml` | The vaults opened here (at most ten, recorded only after one opens), where each one's key file is, and its YubiKey slot. Paths and slot numbers only |
| `app.toml` | Idle timeout, theme, lock-on-minimize, whether closing the window keeps the app in the menu bar or tray, the vault agents and the CLI use when nothing names one, the window's place and size, zoom, and whether crash reports are sent with the date of the terms you agreed to |
| `crash-reports/` | Reports waiting to be sent while Send crash reports is on, each as Settings › Advanced › Diagnostics shows it. Turning the switch off deletes the folder |
| `projects.json` | Each project's saved directory and command. No variable names or values |
| `recommendations.json` | Dismissed recommendations, by vault, entry ID and key name. No titles or values |

An unreadable settings file makes the app use defaults without overwriting it. Open keypaste at login is kept only as your login's own entry, which switching it off in Settings removes: a `keypaste` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, `~/Library/LaunchAgents/com.keypaste.app.plist`, or `~/.config/autostart/keypaste.desktop`.

<a id="removing-keypaste"></a>

## Removing keypaste

First run `keypaste setup --remove`, or Disconnect each tool in Agents, so no AI tool keeps starting keypaste; switch off Open at login; and quit the app.

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

A `keypaste` that Link replaced waits beside it as `keypaste.keypaste-backup`, and an AI tool's configuration file that setup or Connect edited keeps its first original as `<name>.keypaste-backup`. None of this touches your vault, `<vault>.backups` or `~/.keypaste`; delete `~/.keypaste` (`rm -rf ~/.keypaste`, or `Remove-Item -Recurse "$HOME\.keypaste"`) only when you want keypaste to forget this computer's settings and log.

## Building and running it

For contributors: install the .NET SDK that `global.json` selects, then

```
dotnet restore keypaste.app.slnx --locked-mode
dotnet build   keypaste.app.slnx -c Release
dotnet run --project src/Keypaste.App -c Release
```

If a build fails with `Access to the path 'artifacts\...' is denied`, close the app and rebuild with `-nodeReuse:false`.

<a id="checking-a-build-by-hand"></a>

The manual checklist a person runs on each platform before a desktop release is in [RELEASE](RELEASE.md#installation-verification).
