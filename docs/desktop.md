# The desktop app

The desktop app opens the same `.kdbx` vaults as the CLI and locks its session when idle.

<a id="what-it-is-not-yet"></a>

## Current limits

In the published version, approvals happen in a separate terminal: an AI request goes through `keypaste-mcp` to a `keypaste agent` you started and unlocked yourself. In source, the unlocked app itself answers `keypaste-mcp` for its vault. It lists entry names, and it asks about each credential request in its own prompt window, which releases only on Approve ([approvals](approvals.md#approving-in-the-desktop-app)). Agent Activity names the app's process and session while it does. When `keypaste agent` already holds the vault, the unlock screen names it as the owner and the app leaves it running.

The focused target is one unlock session for desktop use, MCP requests and env launches, with approval and denial in the app. In source, locking already ends that session for agents in one step: a request still waiting at the app is denied as locked and grants are cleared. Agent Activity lists the request waiting in the prompt, the grants in force with a Revoke for each, and this session's audit history ([approvals](approvals.md#approving-in-the-desktop-app)). Agent Activity also connects an MCP client to the unlocked vault, checks the connection and removes it ([MCP setup](mcp-setup.md#from-the-desktop-app)). In source, Env Sets launches a project through that session after you confirm the command, the directory and the variable names, and `keypaste run --session` in a terminal asks the app for a set in a prompt window of its own ([approvals](approvals.md#runs-that-ask-for-a-projects-variables)); `keypaste run` without `--session` still unlocks on its own. The earlier terminal-owned design is no longer the required architecture.

The desktop app is not published. Build it from source, below. CI produces desktop archives and internal unsigned Windows MSI and Linux AppImage candidates. A desktop publication path exists but remains gated; [RELEASE](RELEASE.md) owns the distribution matrix and outstanding signing, publication and installation evidence.

An existing password or variable value can now be stored and replaced in the app, so `keypaste add` and `keypaste env set` are no longer the only way; an entry's history can be read and restored on its pane, and groups and entries can be renamed and moved there, so neither recovering a replaced password nor tidying a vault needs KeePassXC. Both forms that generate a secret can generate a passphrase as well as a password. A variable's earlier values are reached the same way, through the entry under `env/<project>` that holds it.

## What the screens show

Entries lists titles and groups. The group tree filters the list; search matches titles, group paths, usernames and URLs case-insensitively. A password, a note and a protected custom field are never matched: the search runs in the vault and does not read those fields at all, so no query can confirm a recovery code pasted into a note. What comes back is an entry name and the names of the fields that matched, so a row found by its username says "username" beside it and never the username. Searching does not clear what you had selected, and neither does changing group. Selecting an entry shows its username, URL and notes, which can be edited inline. The password is dots until you hold it: pressing it shows the characters and releasing, dragging off, switching screens, selecting another entry or locking hides them. Copy puts it on the clipboard with the same twenty-second clear as every other secret. A new entry generates its password unless you untick Generate a password, which reveals a masked field to type or paste an existing one into; leaving it empty creates an entry with no password. While generation is on, Characters or Words chooses what to draw: characters give the twenty-character default, and Words gives a passphrase from the vendored EFF long list, with a word count from six to thirty-two and the character that separates them. The list's size and what each word is worth are stated beside the count, and so is what the current count buys, so a longer passphrase is chosen against a number rather than a feeling. An entry's edit form has the same field for a replacement, and leaving it empty keeps the password the entry already has. Deletion requires confirmation, and the confirmation says where the entry goes: to the vault's recycle bin, or nowhere if that vault's bin is switched off. Afterwards a line states what happened, and while it is showing a Restore button beside it puts a recycled entry straight back.

Organizing happens on the same screen. New group beneath the tree makes a group inside whichever one is selected, or at the top level under "All entries"; Rename is not offered there, because the root is not a group anybody named. Organize on a selected entry — also reachable as "Rename or move" on its pane — opens one form holding its name and its group, and one Save changes either or both. That is a single write, so an entry moved and renamed at once keeps its identity, its history, its attachments and the fields keypaste does not model, and the file stays KDBX 4.0. A refusal writes nothing at all and says which refusal it was: a name the destination already answers to, a result that would make two entries answer to one path, a name no vault could address, a variable name nothing could export, and the two names keypaste assigns meaning to. Nothing is applied by clicking away. Renaming a group follows it in the sidebar; creating one leaves you where you were, and an entry you move takes the filter with it. An entry renamed out of its own search result stays listed until you change the query.

Both organize forms say that policy rules and agent exposures match paths, so organizing can stop one applying to these entries and start another. That is how those patterns work: they are matched against a group path and a title, and they follow neither the group nor the entry, so renaming away from a granted path stops a rule matching and renaming onto one starts it. Where the group being renamed sits directly under `env`, the form adds what that particular rename costs: it renames the project, so `keypaste run <old name>` stops finding it. Neither line blocks the change. Removing a group, and moving one to a different parent, are still KeePassXC's, and the CLI has no organize or search verb.

Show history lists what the entry held before, newest first, with the time each value was current; the pane widens and the group tree steps aside while it is open. Selecting a revision shows its username, URL, notes and password beside the current ones. The revision's password is a row of dots until you hold it, as an env value is, and only one can be visible at a time; releasing, switching screens or locking hides it. The current password is still never displayed. Restore this makes the selected revision current and saves, and the value it replaces becomes the newest history item, so a restore can be undone by restoring again. An entry nobody has changed says so rather than showing an empty list, and a vault that changed under the app refuses the restore and says why. KeePass keeps a bounded number of revisions, so restoring one near the end of a long history can drop the oldest.

Trash lists what deleting has put in the vault's recycle bin: each entry's name, the group it came from and when it went. No value is shown there, because recovery is a choice between names; restore one and read it on its pane as usual. Restore puts the selected entry back where it was deleted from, or at the root when that group is gone, and says which. A restore is refused outright when something else has taken the name in the meantime — rename or delete that one in KeePassXC first. Delete for good erases one entry and its history behind its own confirmation; emptying the whole bin is KeePassXC's. A vault whose recycle bin is switched off says so instead of showing an empty list. The screen also states that a vault which has recycled anything is written as KDBX 4.1, which KeePassXC 2.7 and KeePass 2.48 and later read.

Env Sets shows project cards with a copyable `keypaste run <project> -- ` command. The copied CLI command opens the vault separately, asks for its password and closes it before starting the child; it does not reuse the desktop session. In source, adding `--session` and `--vault <this vault>` to it asks this app instead: a prompt window names the project, the variable names, the command and the directory, and the command starts with the set only when you press Approve.

In source, an open card also sets where the project runs on this machine: a directory and the command you would type there, saved with Save. Run opens the platform's terminal in that directory running the command, and Open terminal opens it at a prompt: `cmd.exe` on Windows, and on Linux the first of `x-terminal-emulator`, `gnome-terminal`, `konsole` and `xterm` found on `PATH`. Neither is offered on macOS. Each first shows a card naming exactly what starts, the directory and the variables' names, never their values, and starts nothing until you press Start. The set comes from the vault as saved while the app stays unlocked: a set with an expired entry or a name that cannot be exported is refused before the card appears, naming each entry, and locking while the card is up, or before the set is released, starts nothing. The values go into the terminal's environment only, so everything you run in that terminal can read them, and locking the app does not stop it or take them back. After the command exits the terminal stays open at your shell. Import .env reads a file through the same checks as `keypaste env pull`, lists each variable as new, replacing a stored value or unchanged, and writes only when you press Import. A file with any problem imports nothing, and the file is left where it was.

Opening a card displays masked variables with Copy and Replace buttons. A new variable generates its value unless you untick Generate a value, which reveals a masked field for one you already have; Replace opens the same field for an existing variable. The Characters or Words choice is the same one the entry form offers, over the same generator. Hold a value to reveal it; only one can be visible, and releasing, switching screens or locking hides it.

Copied secrets clear after twenty seconds, with a countdown and Clear now button. Locking or quitting clears them sooner unless another value has replaced the clipboard. If a copy is still in progress, cleanup waits for it to finish; quitting waits, while locking proceeds immediately. Killing the app prevents cleanup. Copied run commands remain on the clipboard because they contain no secret.

## Editing your vault

Everything the app writes goes through the same core as the CLI. A fresh `keypaste ls`, `keypaste get`, `keypaste env ls` or `keypaste run` invocation reads the saved change from the same file. In source, agents are answered from the vault as saved: a change saved in the app is the next value an agent is given, and a grant for what it touched is asked about again. Both front ends use the serialization code exercised by the KeePassXC compatibility gate.

If the vault file changes while open, the app refuses to save its stale copy. Lock and unlock to load the external change, then reapply your edit. No data is written during the refusal. In source, the app also refuses every agent request as `vault-changed` from the moment it sees the change until you lock and unlock.

The app is a vault editor, not just a viewer. Deleting an entry moves it to the vault's KDBX recycle bin, so the entry, its fields and its history survive, and Trash puts it back. A vault whose recycle bin was switched off in KeePassXC still deletes permanently, and the confirmation says so. There is no concurrent-edit merge. Entry history can recover a replaced value while that entry and vault survive; neither it nor the recycle bin can recover a lost file. Saving keeps the file it replaces: the first save after each unlock copies the vault into `<vault>.backups` beside it, no more often than every fifteen minutes, keeping the last five, and a save whose copy cannot be written does not happen. An edit made within fifteen minutes of the newest copy therefore has no copy of its own, and restoring returns the vault as that newest copy found it.

Restoring a backup happens on the unlock screen, while the app is locked. Select the vault and choose Restore a backup: the copies are listed by when they were taken, and one whose file is no longer a KeePass file is marked damaged. Type the master password that copy was made under, which is an earlier one if the password has changed since, choose the keyfile it was made under with That backup's keyfile… or No keyfile, and Check this backup opens it; the panel starts with the keyfile the unlock screen had chosen. The app then shows when it was taken, how many entries, groups and env projects it holds, and which file it will replace; it shows no names and no values. A wrong password and a damaged copy give the same answer, because nothing finer can be said of a file that did not decrypt. Restore this backup replaces the vault with that copy byte for byte and opens it, and a line in the app says what happened. The file it replaced is kept as a backup first, unless a listed copy already holds exactly its bytes, and a restore never drops a copy; the next ordinary save trims back to five. A vault file that is damaged, is no longer a KeePass file or is missing can still be selected when backups sit beside it, for restoring only; a missing vault is reached from the recent list, and one that has been forgotten from it is recovered by opening a backup file directly as a vault. A vault file that briefly cannot be read, as one another program is saving cannot, is retried within the same retry budget a save has, and only one still unreadable after that is left alone with nothing replaced. Close KeePassXC and stop a running `keypaste agent` first: each keeps its own copy of the vault and does not notice a restore. A password typed into this panel, and a checked copy waiting on its confirmation, are cleared after the idle timeout, on minimize when that setting is on, and on every way out. A backup cannot recover a master password nobody remembers, or a keyfile nobody kept.

Settings changes what unlocks the vault under Master password and keyfile. Type the current master password, even though the vault is open, so nobody who finds it unlocked can change it; then set a new password, add or replace a keyfile, stop using the keyfile, or several at once. Change… shows what the vault will open with afterwards and what the change costs: the vault as it was is kept in `<vault>.backups` first, and that copy and every earlier one still open with the old password and keyfile, so delete them if those were exposed. The app stays unlocked on the changed vault, as KeePassXC does, and the next unlock needs the new password and keyfile. A wrong current password, Leave access as it is, a refused keyfile, a vault changed on disk since it was unlocked and a save that fails each leave the vault file untouched. A vault that loses its keyfile keeps or gets a password; a keyfile-only vault can swap its keyfile but drops it only for a password set in the same change. A running `keypaste agent` keeps the vault it opened until you restart it.

Settings says where the copies are kept and how many there are, and Export an encrypted copy writes the vault's exact bytes to a new file, which opens with the same master password. It refuses a file that already exists, the vault itself and anywhere inside its backup directory, and it refuses a vault that changed on disk since it was unlocked. Copies beside the vault do not survive losing the disk or the folder, so keep an exported copy on another disk.

## Building and running it

Install the .NET SDK version selected by the repository's `global.json`. The app lives in its own solution so that ordinary backend work does not pay to build it:

```
dotnet restore keypaste.app.slnx --locked-mode
dotnet build   keypaste.app.slnx -c Release
dotnet run --project src/Keypaste.App -c Release
```

If a build fails with `Access to the path 'artifacts\...' is denied`, an MSBuild worker node or a still-running app may be holding a file. Close the app and pass `-nodeReuse:false` when rebuilding. If the failure persists, also check the path's permissions.

## What it needs on each platform

These are the native GUI prerequisites for the current packaging targets. Building from source also requires the SDK above; CI archives, the internal Windows MSI and the internal Linux AppImage include the .NET runtime but are not published installers.

| Platform | Native prerequisites |
|---|---|
| Windows | No separate browser engine or .NET runtime for a self-contained archive |
| macOS | No separate browser engine or .NET runtime for a self-contained archive |
| Linux | `libx11-6 libice6 libsm6 libfontconfig1`, and an X11 or XWayland session. The AppImage also needs FUSE: `/dev/fuse` and a setuid-root `fusermount3`, which `fuse3` provides; its runtime carries libfuse 3, so `libfuse2` is not needed |

Upgrading is a matter for the package. The Windows MSI is a per-user major upgrade: installing a higher version replaces the lower one in place, the previous version is removed only once the new install commits, so an upgrade that fails partway leaves the one you had installed and runnable, and the MSI refuses a lower version over a higher one. An AppImage has no installer, so an upgrade is a new file put in place of the old one. Neither package holds your data: the vault, `app.toml`, `recent.toml`, `policy.toml` and `audit.jsonl` are untouched by an upgrade, and uninstalling removes the install folder and the Start menu shortcut while leaving `~/.keypaste` and your vault alone. Recorded internal-candidate checks include upgrade and interrupted-install recovery; they do not establish a public desktop installation. See [RELEASE](RELEASE.md) for their evidence.

Avalonia draws with Skia; the app does not embed WebKit or Chromium. Internal installation checks exercised the candidates on fresh `windows-2025` and `ubuntu-24.04` runners (D-0205); supported OS versions and the Linux distribution baseline beyond those runner images remain unverified. A renderer's glibc baseline alone does not establish the app's support range.

## Opening a vault

Open a vault by dragging a `.kdbx` onto the window, choosing Browse or pressing `Ctrl/Cmd+O`, or selecting a recent vault. The shortcut opens the same picker as Browse and does nothing while the create or restore form is showing, while an unlock is running or once a vault is open.

Whichever you use, the file's header is read before you are asked for a password, so a file that was never a vault is refused immediately rather than after you have typed. [PRODUCT](PRODUCT.md) requires both front ends to use shared core rules.

A vault that needs a keyfile takes it from Keyfile… under the password. All four KeePass keyfile forms open it, and a vault a keyfile alone protects opens with the password left empty. A file that is missing, empty, unreadable or itself a vault is refused the moment you choose it, and a wrong pair is answered as "That password and keyfile didn't open this vault." A vault keyed to an ordinary file, rather than to a keyfile, opens with a notice that editing that file loses the vault. The app remembers where the keyfile each vault opened with is, as KeePassXC does, and offers it next time; No keyfile stops using it for this unlock.

## Creating a vault

Create makes a new vault without a terminal. It asks where the file goes through the save picker, then for a master password twice. There is no way to recover that password, and nothing else can open the vault without it.

A keyfile is optional: Add a keyfile… attaches one you already have, such as a KeePassXC keyfile, and the vault then needs both. keypaste never makes a keyfile or a vault a keyfile alone opens, and it refuses an ordinary file that would be keyed by its exact bytes. Keep a copy of the keyfile away from the vault, because losing it locks the vault.

The refusals are the ones `keypaste init` makes, because both front ends ask the same code: a path something already occupies is refused and that file is left exactly as it was, an empty password is refused, and a confirmation that does not match is refused, as is a keyfile keypaste will not attach. Nothing is written until all of them have passed, so a refused attempt leaves the disk as it found it, and cancelling the picker writes nothing at all. The vault is remembered in `recent.toml` only once it exists and the app has opened it.

A new vault opens on an empty Entries list. Add entries there, or with `keypaste add` in a terminal against the same file.

## Locking

The default idle timeout is five minutes without keyboard or mouse input. Settings accepts one minute to eight hours and persists the choice across launches. Idle locking cannot be disabled.

Thirty seconds before it locks, a quiet line appears in the header. Any key or click cancels it.

`Ctrl/Cmd+L` or the sidebar Lock button locks immediately.

Settings can enable lock-on-minimize; it is off by default and persists across launches. Minimizing then closes the unlocked view, clears copied secrets and restores to the unlock screen. Switching windows and macOS `Cmd+H` do not count as minimizing.

Switching windows leaves the vault unlocked and its idle countdown running, and so does restoring it: neither activation nor a pointer that has not moved counts as activity, so the countdown still arrives on time. A machine that sleeps past the timeout wakes locked. The app takes the greater elapsed time from wall and monotonic clocks and rechecks on activation, covering platforms where a monotonic clock pauses during sleep.

Locking disposes the desktop vault session and clears its visible entry state. You type your password again to reopen it. In source, every lock and quitting also deny an agent's request still waiting at the app and clear the grants agents were given, and a request that arrives after the machine slept past the timeout locks the app and is refused. An agent's requests never count as activity. Values already delivered to a client are not recalled. This does not lock a separate terminal approver or erase immutable strings and external copies; see [SECURITY](../SECURITY.md) for memory and clipboard limits.

## Keyboard

The app provides these shortcuts and focus navigation. Verify the full keyboard-only journey on each native platform using the checklist below; logic tests alone do not establish accessibility. When a vault is selected, the unlock field accepts the password followed by Enter.

| | |
|---|---|
| `Ctrl/Cmd+1` … `6` | Entries, Env Sets, Agent Activity, Log, Settings, Trash |
| `Ctrl/Cmd+O` | Open a vault, on the unlock screen |
| `Ctrl/Cmd+L` | Lock now |
| `Tab` / `Shift+Tab` | Move between controls |
| `↑` `↓` | Move within the sidebar or the recent list |
| `Enter` | Unlock |
| `Escape` | Clear the secret field in focus |
| `Ctrl/Cmd+V` | Paste into an entry-password or variable-value field; the master-password fields ignore it |

On macOS the modifier is Cmd; everywhere else, Ctrl.

## Files it keeps, and how to delete them

The app keeps machine-specific settings in `~/.keypaste`, alongside the audit log and policy. They do not travel with the vault. `KEYPASTE_HOME` changes the directory.

| | |
|---|---|
| `recent.toml` | The vaults you have opened here, and where the keyfile each opened with is. Paths only; no entry names, secrets or key material |
| `app.toml` | Idle timeout, theme, lock-on-minimize |
| `projects.json` | For each vault and project you saved on Env Sets, its directory and command. No variable names or values |

`recent.toml` records a vault only after it opens successfully, so a file you were sent and could not open leaves no trace. It holds at most ten, most recent first. Remove one from the list in the app, clear the whole list in Settings, or delete the file. On Linux and macOS it is written owner-only; on Windows it inherits your profile's permissions, which is the same protection `audit.jsonl` already relies on.

Unreadable settings files cause the app to use defaults without overwriting the files. An unreadable `projects.json` is also left as it is, and saving a project then says so instead of replacing it.

The app writes forward slashes in `recent.toml`, including `C:/Users/…` on Windows. Its shared parser rejects backslashes so authorization patterns cannot render differently from their meaning.

## The Log screen

The Log screen reads `~/.keypaste/audit.jsonl` through the same renderer as `keypaste log` ([D-0032](decisions-archive.md)). The reader and `keypaste log` need no unlocked vault; the current desktop exposes this screen only after unlock. Verify chain checks the audit hash chain.

A missing log is normal before the MCP bridge has initialized one. Requests and bridge events populate it; opening the desktop Log screen does not require a prior credential release.

<a id="what-you-should-know-about-the-master-password"></a>

## Secret input

Every field that takes a secret uses one control, which sends characters directly to a buffer wiped on every exit path. It avoids Avalonia `TextBox`, which exposes contents to accessibility and retains immutable strings in undo history. Its accessibility peer exposes no content: tests check that each of the eight fields publishes only its placeholder and a row of dots, and that two secrets of the same length are indistinguishable there.

The entry-password and variable-value fields accept `Ctrl/Cmd+V`. The seven master-password fields — unlock, the two on the create form, a backup's on the restore panel, and the current, new and confirmation fields in Settings — do not, and nothing reads the clipboard on their behalf. A pasted value loses one trailing line break, since copying a token usually brings one. Anything else a keyboard cannot type is refused with a message rather than quietly removed, so what is stored is what was on the clipboard or nothing at all.

Keystrokes still arrive as short-lived immutable strings, and an input method can send several characters at once; a paste arrives as one. `SECURITY.md` describes these memory and input limits.

## Checking a build by hand

CI builds and packages on three operating systems; desktop tests read secret surfaces from frames Skia renders in a headless window ([4.6](steps/4.6.md)) but do not establish native rendering or a complete native workflow. `install-desktop.yml` installs internal candidates on fresh runners and drives the installed app, but a browser download's SmartScreen prompt and a person's use are still observed only by hand. Use a disposable vault with harmless test values for this manual checklist before any release that includes the app:

1. Launch with no `recent.toml`: the empty state offers Create, names dragging and Browse, and does not look broken.
2. Open a vault by drag, and again by the picker. A non-`.kdbx` file is refused before the password field.
3. Create a vault: choose a path, type a master password twice, and land on an empty Entries list. Cancel the save picker instead and check nothing was written. Aim Create at a vault that already exists: it is refused, and that vault still opens with its own password afterwards. Check this on each platform — a save picker that created or truncated the file itself would defeat the refusal, and no headless test can observe that.
4. Wrong password: a calm message, still locked, and nothing added to `recent.toml`.
5. Right password: the shell appears, and the vault is now in `recent.toml`.
6. Complete launch, unlock, all six destinations and `Ctrl/Cmd+L` using only the keyboard.
7. Set a one-minute timeout. Check that the countdown appears, typing cancels it, and inactivity locks. Quit and relaunch without opening Settings; the timeout must remain one minute.
8. Suspend the machine for longer than the timeout. It wakes locked.
9. The theme follows the OS, and both light and dark read as calm. Choose Dark, quit and relaunch: the first frame is dark, with no flash of the light one on the way.
10. Set `idle_timeout_seconds = 137` in `app.toml` and relaunch. Settings must display it, locking must occur at 137 seconds, and the file must remain unchanged.
11. Set a long idle timeout to isolate minimize locking. Enable "Lock when the window is minimized", minimize and restore: expect the unlock screen. Disable it, minimize and restore: expect an unlocked vault and a running idle countdown. Enable it again, quit and relaunch without opening Settings; minimizing must lock. A password copied before locking must no longer paste. Recorded runner results do not replace a person's check on a real macOS or Linux desktop.
12. The Log screen matches `keypaste log` for the same `~/.keypaste/audit.jsonl`.
13. Agent Activity names this app's process and session while the vault is unlocked. With `keypaste agent` holding the vault first, the unlock is refused and the unlock screen names the agent. In source, a credential request from a `keypaste-mcp` configured for the vault opens the prompt window over other windows: Approve does nothing for its first second, Enter and Escape refuse, and locking the app while it is up takes it down. While it is up Agent Activity lists it with its seconds left; after Approve it lists the grant, Revoke removes it, and the same request prompts again.
14. Entries lists titles and groups. Filter by a group and search for part of a title or group path; case changes still match. Selecting an entry shows a username, a URL and notes, and a row of dots where the password is.
15. Copy a password and check the countdown and progress bar. It must paste before the timeout and be absent afterward.
16. Copy, then `Ctrl/Cmd+L`. Paste: nothing.
17. Copy, then quit the app. Paste: nothing.
18. On Windows with Clipboard History enabled and permitted by policy, copy a harmless control string and confirm Win+V contains it. Then copy an app password and check that Win+V excludes it. `keypaste get` has set the same formats since D-0056; native CLI verification is still needed; unit checks alone do not establish absence from Win+V.
19. Hold an Env Sets value to reveal it, then release to hide it. Holding another row must reveal only that row.
20. Copy a project's run command, paste it in a terminal, finish the line: it runs with the project's variables.
21. Add, edit and delete an entry, then check `keypaste ls` and `keypaste get` in a terminal.
21a. Make a group with New group, then Organize an entry into it and give it a new name in the same Save. Check `keypaste get <new path> --show` in a terminal, and that the old path is gone. Try the same Save again with a name the destination already holds: it is refused, says why, and the entry is where it was.
21b. Rename a group holding an env project — `env/billing` to `env/invoicing`. The form says what it costs before you confirm. Afterwards `keypaste run invoicing -- printenv` sees the project and `keypaste run billing` does not.
21c. Search for part of an entry's username, and then part of its URL. The row appears with the matched field named beside it and no value shown. Search for its password: nothing. Search for a word that is only in its notes: nothing.
22. With the app open on a vault, run `keypaste env set` against the same file in a terminal. Come back and make any edit: the app refuses, says why, and the terminal's write is still there.
23. Generate a password in the app, then read it back with `keypaste get --show`.
24. Choose Words, set the count to eight, and check that the line beneath it names eight words and about 103 bits and that the list line names 7,776. Add the entry, then read it back with `keypaste get --show`: eight words separated by full stops. Set the separator to `-` and the form must refuse it, because four of the list's words are spelled with one.
25. Untick Generate a password, type an existing one, and read it back with `keypaste get --show`. Repeat with `Ctrl/Cmd+V` from a value you copied elsewhere, and once with something ending in a newline copied out of a terminal: the stored value must have no trailing newline.
26. Edit that entry and type a replacement password. `keypaste get --show` returns the new one, and both Show history on its pane and KeePassXC's History tab show the old one.
27. Untick Generate a value on a new variable, paste a value, then `keypaste run <project> -- printenv` in a terminal: the child receives exactly what you pasted. Replace it and check the same, with the old value in KeePassXC's History tab.
28. Press `Ctrl/Cmd+V` in the unlock field with something on the clipboard: nothing is entered.
29. Open the vault the app wrote in KeePassXC, both one it created and one it edited.
30. Open Show history on that entry, hold a revision's password to reveal it and release to hide it. Switch screens while holding, and lock while holding: both must take it off the screen. Select a revision and check the layout at the smallest window the app allows — the entry list must still be usable.
31. Restore the oldest revision. `keypaste get --show` returns it, the value it replaced is now the newest history item in both the app and KeePassXC, and the entry keeps its other fields. Restore again to go back.
32. Delete an entry and press Restore on the line that appears. It is back in the list and `keypaste get --show` returns its password.
33. Delete an entry, then a variable on an Env Sets card, and open Trash: both are listed with the groups they came from, and neither is in `keypaste ls`. Restore each and check `keypaste get --show` and `keypaste run <project> -- printenv`.
34. Delete an entry, re-create one with the same name, then try to restore the deleted one from Trash: it is refused, says why, and neither entry changes. Delete the new one and restore again: it works.
35. Delete an entry, open Trash and use Delete for good. It takes a second confirmation, the entry leaves the list, and KeePassXC no longer shows it in the Recycle Bin.
36. Delete something, then lock with `Ctrl/Cmd+L`. Unlock and open Trash: the entry is still recoverable, and nothing from before the lock is still on screen.
37. Change a password, lock, and on the unlock screen choose Restore a backup. Check the newest copy with the master password: it shows a time and counts and no names. Restore it: the app opens on the earlier password, a line says the replaced vault was kept, and `<vault>.backups` holds one more file, which KeePassXC opens on the later password.
38. Repeat with a wrong password, then use Leave the vault as it is after a good check. Neither changes the vault file, and the password field is empty afterwards.
39. With the app locked, overwrite the vault file with a text file, then delete it. Each time it can still be selected, only Restore a backup is offered, and a restore brings the vault back.
40. Check a backup and leave it on the confirmation past the idle timeout, then again with Lock when the window is minimized on and a minimize. Each time the confirmation and the typed password are gone.
41. In Settings, export an encrypted copy to another folder and open it in KeePassXC with the vault's master password. Export to the same name again, to the vault itself and into `<vault>.backups`: each is refused and nothing is written.
42. Make a keyfile in KeePassXC. In Settings, type the current password, add that keyfile and confirm: the app stays on Settings, and an edit still saves. Lock: the unlock screen offers the keyfile, the password alone is refused, and both open the vault. KeePassXC opens it with both.
43. Change the password in Settings, lock, and check the old password is refused and the new one opens. Then choose Restore a backup, check the newest copy with the old password and the keyfile, and restore it: the app opens on the old password.
44. Type a wrong current password, then Leave access as it is after a good one: the vault file's modified time and `<vault>.backups` are unchanged each time.
45. Create a vault with Add a keyfile… and a KeePassXC keyfile, then open it in KeePassXC with the password and keyfile. Try an ordinary text file as the keyfile instead: it is refused and nothing is written.
46. Hold an entry's password to reveal it and release to hide it; hold again and drag off the control, then hold and lock with `Ctrl/Cmd+L`: each must hide it. With a screen reader running, tab through the unlock, create, restore, Settings, entry and env fields: each masked field is announced by its purpose, and the announcement does not change as you type.
47. Open Show history, select a revision and press its Copy. Paste it somewhere harmless, then check the clipboard is empty after twenty seconds, after Clear now on a second copy, and after a lock on a third.
48. In Agent Activity, connect an installed client with a label and one extra exposure. The preview names the vault, the label and the exposure; Cancel leaves `claude mcp list` or `codex mcp list` unchanged, and Run it adds exactly the previewed command. Check the connection raises the prompt window and, after Approve, the history shows the prompted grant under the label. Remove it and the client lists no keypaste. From the installed package, the registered command is the packaged `keypaste-mcp`, or the `.AppImage` file with `mcp`.
49. On an Env Sets card, Import .env a file holding one new and one changed variable. The preview names both and no value; Cancel leaves `keypaste env ls` unchanged, and Import adds the new one and replaces the other, whose old value is in KeePassXC's History tab. The `.env` file is still there.
50. Save a directory and `printenv` (Windows: `set`) as the command, press Run and read the card: the command, the directory and the variable names, no value. Cancel starts nothing. Start opens a terminal in that directory whose output lists the variables, and the terminal stays open. Open terminal does the same at a prompt. Press Run again and lock with `Ctrl/Cmd+L` while the card is up: the card goes and no terminal opens.
51. With the vault unlocked, run `keypaste run --session --vault <vault> <project> -- printenv` (Windows: `set`) in a terminal. A prompt window names the project, the variable names, the command and that terminal's directory, and no value; no password is asked for in the terminal. Approve prints the variables; run it again and Deny, then with a new command lock while the prompt is up: each ends with a reason and prints nothing of the set. Quit the app and run it once more: it says nothing holds the vault and asks for no password.


## Observing minimize-lock on macOS and Linux

Item 11 has been observed on Windows. macOS and Linux require native checks because headless tests cannot establish what their window managers report. `observe-desktop.yml` drives these checks on `macos-15` and on Xvfb with Openbox through [observe-minimize-lock.sh](../scripts/observe-minimize-lock.sh), and both passed. What a runner cannot observe, a person's own minimize click and, on macOS, the `Cmd+H` keystroke, still needs to be recorded on a real macOS machine and Linux desktop.

Download the seven-day `app-<rid>` artifact from `app.yml`, or publish locally:

```
dotnet restore keypaste.app.slnx --locked-mode
dotnet publish src/Keypaste.App -c Release -r osx-arm64 --self-contained --no-restore -o artifacts/app/osx-arm64
```

`linux-x64` for the other. Never pass `-r` to `restore`: it narrows the project's RID set to one and a locked restore then fails (D-0040). Linux also needs `libx11-6 libice6 libsm6 libfontconfig1` and an X11 or XWayland session.

Use a disposable vault and set an idle timeout long enough to exclude it as the cause of locking.

1. Enable "Lock when the window is minimized". Use the macOS yellow button or the Linux titlebar/window-menu minimize action, then restore. Expect the unlock screen and a cleared copied password.
2. Disabled. Untick it, minimize and restore. Expected: still unlocked, and the idle countdown still arrives on time.
3. After a restart. Tick it, quit, relaunch without opening Settings, minimize. Expected: it locks, and `app.toml` is unchanged afterwards.

Switching windows must leave the app unlocked. On macOS, `Cmd+H` hides the app and must also leave it unlocked; `Cmd+M` minimizes it.

Record the OS name, version and build; session type and desktop environment; app build or tag; and each result with the selected platform task. F.2b3 covers the focused Windows/Linux release; macOS observation is needed if that desktop target is selected from BACKLOG. If a window manager reports no minimize event, record that result and update `MinimizeLock.IsSupported` to omit the unsupported checkbox. Untested targets remain unobserved.
