# The desktop app

The desktop app opens the same `.kdbx` vaults as the CLI and locks its session when idle.

<a id="what-it-is-not-yet"></a>

## Current limits

Approvals still happen in the terminal. When an AI agent asks `keypaste-mcp` for a credential, the request goes to a `keypaste agent` you started in your own terminal. The Agent Activity screen currently reports whether that agent is running. The design is settled: the agent owns the approver pipe and the app will connect as a UI client (D-0054 in [DECISIONS.md](../DECISIONS.md#d-0054--the-agent-owns-the-approver-pipe-the-app-is-a-client-of-it)). The client channel and approval controls remain step 4.3 in [STEPS](STEPS.md).

The desktop app is not published. Build it from source, below. `app.yml` can package desktop archives, an internal unsigned Windows MSI and an internal unsigned Linux AppImage on version tags, but keeps them as workflow artifacts; `release.yml` publishes the CLI/MCP downloads. See [RELEASE](RELEASE.md) for the distribution matrix and remaining desktop publication requirements.

An existing password or variable value can now be stored and replaced in the app, so `keypaste add` and `keypaste env set` are no longer the only way, and an entry's history can be read and restored on its pane, so recovering a replaced password no longer needs KeePassXC. A variable's earlier values are reached the same way, through the entry under `env/<project>` that holds it.

## What the screens show

Entries lists titles and groups. The group tree filters the list; search matches titles and group paths case-insensitively, excluding secret values. Selecting an entry shows its username, URL and notes, which can be edited inline. Passwords have a Copy button but are never displayed here; use `keypaste get --show` to read one. A new entry generates its password unless you untick Generate a password, which reveals a masked field to type or paste an existing one into; leaving it empty creates an entry with no password. An entry's edit form has the same field for a replacement, and leaving it empty keeps the password the entry already has. Deletion requires confirmation and has no undo.

Show history lists what the entry held before, newest first, with the time each value was current; the pane widens and the group tree steps aside while it is open. Selecting a revision shows its username, URL, notes and password beside the current ones. The revision's password is a row of dots until you hold it, as an env value is, and only one can be visible at a time; releasing, switching screens or locking hides it. The current password is still never displayed. Restore this makes the selected revision current and saves, and the value it replaces becomes the newest history item, so a restore can be undone by restoring again. An entry nobody has changed says so rather than showing an empty list, and a vault that changed under the app refuses the restore and says why. KeePass keeps a bounded number of revisions, so restoring one near the end of a long history can drop the oldest.

Env Sets shows project cards with a copyable `keypaste run <project> -- ` command. Opening a card displays masked variables with Copy and Replace buttons. A new variable generates its value unless you untick Generate a value, which reveals a masked field for one you already have; Replace opens the same field for an existing variable. Hold a value to reveal it; only one can be visible, and releasing, switching screens or locking hides it.

Copied secrets clear after twenty seconds, with a countdown and Clear now button. Locking or quitting clears them sooner unless another value has replaced the clipboard. If a copy is still in progress, cleanup waits for it to finish; quitting waits, while locking proceeds immediately. Killing the app prevents cleanup. Copied run commands remain on the clipboard because they contain no secret.

## Editing your vault

Everything the app writes goes through the same core as the CLI. A fresh `keypaste ls`, `keypaste get`, `keypaste env ls` or `keypaste run` invocation reads the saved change from the same file. An already unlocked process, including a terminal approver, retains its in-memory copy until reopened. Both front ends use the serialization code exercised by the KeePassXC compatibility gate.

If the vault file changes while open, the app refuses to save its stale copy. Lock and unlock to load the external change, then reapply your edit. No data is written during the refusal.

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

Upgrading is a matter for the package. The Windows MSI is a per-user major upgrade: installing a higher version replaces the lower one in place, the previous version is removed only once the new install commits, so an upgrade that fails partway leaves the one you had installed and runnable, and the MSI refuses a lower version over a higher one. An AppImage has no installer, so an upgrade is a new file put in place of the old one. Neither package holds your data: the vault, `app.toml`, `recent.toml`, `policy.toml` and `audit.jsonl` are untouched by an upgrade, and uninstalling removes the install folder and the Start menu shortcut while leaving `~/.keypaste` and your vault alone. Step 4.7d in [STEPS](STEPS.md) checks all of that, including an install that fails partway, on the internal candidates; no desktop package is published yet.

Avalonia draws with Skia; the app does not embed WebKit or Chromium. 4.7b installed and exercised the internal candidates on fresh `windows-2025` and `ubuntu-24.04` runners (D-0205); supported OS versions and the Linux distribution baseline beyond those runner images remain unverified. A renderer's glibc baseline alone does not establish the app's support range.

## Opening a vault

Open a vault by dragging a `.kdbx` onto the window, choosing Browse (`Ctrl/Cmd+O`), or selecting a recent vault.

Whichever you use, the file's header is read before you are asked for a password, so a file that was never a vault is refused immediately rather than after you have typed. [PRODUCT](PRODUCT.md#4-engineering-laws) §4.2 requires shared core logic, with neither front end waiting for the other.

## Creating a vault

Create makes a new vault without a terminal. It asks where the file goes through the save picker, then for a master password twice. There is no way to recover that password, and nothing else can open the vault without it.

The refusals are the ones `keypaste init` makes, because both front ends ask the same code: a path something already occupies is refused and that file is left exactly as it was, an empty password is refused, and a confirmation that does not match is refused. Nothing is written until all three have passed, so a refused attempt leaves the disk as it found it, and cancelling the picker writes nothing at all. The vault is remembered in `recent.toml` only once it exists and the app has opened it.

A new vault opens on an empty Entries list. Add entries there, or with `keypaste add` in a terminal against the same file.

## Locking

The default idle timeout is five minutes without keyboard or mouse input. Settings accepts one minute to eight hours and persists the choice across launches. Idle locking cannot be disabled.

Thirty seconds before it locks, a quiet line appears in the header. Any key or click cancels it.

`Ctrl/Cmd+L` or the sidebar Lock button locks immediately.

Settings can enable lock-on-minimize; it is off by default and persists across launches. Minimizing then closes the unlocked view, clears copied secrets and restores to the unlock screen. Switching windows and macOS `Cmd+H` do not count as minimizing.

Switching windows leaves the vault unlocked and its idle countdown running, and so does restoring it: neither activation nor a pointer that has not moved counts as activity, so the countdown still arrives on time. A machine that sleeps past the timeout wakes locked. The app takes the greater elapsed time from wall and monotonic clocks and rechecks on activation, covering platforms where a monotonic clock pauses during sleep.

Locking disposes the desktop vault session and clears its visible entry state. You type your password again to reopen it. This does not lock a separate terminal approver or erase immutable strings and external copies; see [SECURITY](../SECURITY.md) for memory and clipboard limits.

## Keyboard

The app provides these shortcuts and focus navigation. Verify the full keyboard-only journey on each native platform using the checklist below; logic tests alone do not establish accessibility. When a vault is selected, the unlock field accepts the password followed by Enter.

| | |
|---|---|
| `Ctrl/Cmd+1` … `5` | Entries, Env Sets, Agent Activity, Log, Settings |
| `Ctrl/Cmd+L` | Lock now |
| `Ctrl/Cmd+O` | Open a vault |
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
| `recent.toml` | The vaults you have opened here. Paths only; no entry names or secrets |
| `app.toml` | Idle timeout, theme, lock-on-minimize |

`recent.toml` records a vault only after it opens successfully, so a file you were sent and could not open leaves no trace. It holds at most ten, most recent first. Remove one from the list in the app, clear the whole list in Settings, or delete the file. On Linux and macOS it is written owner-only; on Windows it inherits your profile's permissions, which is the same protection `audit.jsonl` already relies on.

Unreadable settings files cause the app to use defaults without overwriting the files.

The app writes forward slashes in `recent.toml`, including `C:/Users/…` on Windows. Its shared parser rejects backslashes so authorization patterns cannot render differently from their meaning.

## The Log screen

The Log screen reads `~/.keypaste/audit.jsonl` through the same renderer as `keypaste log` (DECISIONS.md D-0032). It needs no unlocked vault. Verify chain checks the audit hash chain.

A missing log is normal before the MCP bridge has initialized one. Requests and bridge events populate it; opening the desktop Log screen does not require a prior credential release.

<a id="what-you-should-know-about-the-master-password"></a>

## Secret input

Every field that takes a secret uses one control, which sends characters directly to a buffer wiped on every exit path. It avoids Avalonia `TextBox`, which exposes contents to accessibility and retains immutable strings in undo history. Its accessibility peer exposes no content: tests check that each of the seven fields publishes only its placeholder and a row of dots, and that two secrets of the same length are indistinguishable there.

The entry-password and variable-value fields accept `Ctrl/Cmd+V`. The three master-password fields — unlock, and the two on the create form — do not, and nothing reads the clipboard on their behalf. A pasted value loses one trailing line break, since copying a token usually brings one. Anything else a keyboard cannot type is refused with a message rather than quietly removed, so what is stored is what was on the clipboard or nothing at all.

Keystrokes still arrive as short-lived immutable strings, and an input method can send several characters at once; a paste arrives as one. `SECURITY.md` describes these memory and input limits.

## Checking a build by hand

CI builds and packages on three operating systems; the current desktop logic tests do not verify rendered pixels. Rendering coverage remains step 4.6. `install-desktop.yml` (4.7b) installs internal candidates on fresh runners and drives the installed app, but a browser download's SmartScreen prompt and a person's use are still observed only by hand. Use a disposable vault with harmless test values for this manual checklist before any release that includes the app:

1. Launch with no `recent.toml`: the empty state offers Create, names dragging and Browse, and does not look broken.
2. Open a vault by drag, and again by the picker. A non-`.kdbx` file is refused before the password field.
3. Create a vault: choose a path, type a master password twice, and land on an empty Entries list. Cancel the save picker instead and check nothing was written. Aim Create at a vault that already exists: it is refused, and that vault still opens with its own password afterwards. Check this on each platform — a save picker that created or truncated the file itself would defeat the refusal, and no headless test can observe that.
4. Wrong password: a calm message, still locked, and nothing added to `recent.toml`.
5. Right password: the shell appears, and the vault is now in `recent.toml`.
6. Complete launch, unlock, all five destinations and `Ctrl/Cmd+L` using only the keyboard.
7. Set a one-minute timeout. Check that the countdown appears, typing cancels it, and inactivity locks. Quit and relaunch without opening Settings; the timeout must remain one minute.
8. Suspend the machine for longer than the timeout. It wakes locked.
9. The theme follows the OS, and both light and dark read as calm. Choose Dark, quit and relaunch: the first frame is dark, with no flash of the light one on the way.
10. Set `idle_timeout_seconds = 137` in `app.toml` and relaunch. Settings must display it, locking must occur at 137 seconds, and the file must remain unchanged.
11. Set a long idle timeout to isolate minimize locking. Enable "Lock when the window is minimized", minimize and restore: expect the unlock screen. Disable it, minimize and restore: expect an unlocked vault and a running idle countdown. Enable it again, quit and relaunch without opening Settings; minimizing must lock. A password copied before locking must no longer paste. `docs/STEPS.md` F.2b2 owns the runner results for macOS and Linux; the real-desktop record is deferred to Expansion as F.2b3.
12. The Log screen matches `keypaste log` for the same `~/.keypaste/audit.jsonl`.
13. Agent Activity says the right thing both with and without a `keypaste agent` running.
14. Entries lists titles and groups. Filter by a group and search for part of a title or group path; case changes still match. Selecting an entry shows a username, a URL and notes, and a row of dots where the password is.
15. Copy a password and check the countdown and progress bar. It must paste before the timeout and be absent afterward.
16. Copy, then `Ctrl/Cmd+L`. Paste: nothing.
17. Copy, then quit the app. Paste: nothing.
18. On Windows with Clipboard History enabled and permitted by policy, copy a harmless control string and confirm Win+V contains it. Then copy an app password and check that Win+V excludes it. `keypaste get` has set the same formats since D-0056; native CLI verification belongs to step 1.5a in `docs/STEPS.md`.
19. Hold an Env Sets value to reveal it, then release to hide it. Holding another row must reveal only that row.
20. Copy a project's run command, paste it in a terminal, finish the line: it runs with the project's variables.
21. Add, edit and delete an entry, then check `keypaste ls` and `keypaste get` in a terminal.
22. With the app open on a vault, run `keypaste env set` against the same file in a terminal. Come back and make any edit: the app refuses, says why, and the terminal's write is still there.
23. Generate a password in the app, then read it back with `keypaste get --show`.
24. Untick Generate a password, type an existing one, and read it back with `keypaste get --show`. Repeat with `Ctrl/Cmd+V` from a value you copied elsewhere, and once with something ending in a newline copied out of a terminal: the stored value must have no trailing newline.
25. Edit that entry and type a replacement password. `keypaste get --show` returns the new one, and both Show history on its pane and KeePassXC's History tab show the old one.
26. Untick Generate a value on a new variable, paste a value, then `keypaste run <project> -- printenv` in a terminal: the child receives exactly what you pasted. Replace it and check the same, with the old value in KeePassXC's History tab.
27. Press `Ctrl/Cmd+V` in the unlock field with something on the clipboard: nothing is entered.
28. Open the vault the app wrote in KeePassXC, both one it created and one it edited.
29. Open Show history on that entry, hold a revision's password to reveal it and release to hide it. Switch screens while holding, and lock while holding: both must take it off the screen. Select a revision and check the layout at the smallest window the app allows — the entry list must still be usable.
30. Restore the oldest revision. `keypaste get --show` returns it, the value it replaced is now the newest history item in both the app and KeePassXC, and the entry keeps its other fields. Restore again to go back.


## Observing minimize-lock on macOS and Linux

Item 11 has been observed on Windows. macOS and Linux require native checks because headless tests cannot establish what their window managers report. `docs/STEPS.md` F.2b2 owns the results: `observe-desktop.yml` drives these checks on `macos-15` and on Xvfb with Openbox through [observe-minimize-lock.sh](../scripts/observe-minimize-lock.sh), and both passed. What a runner cannot observe, a person's own minimize click and, on macOS, the `Cmd+H` keystroke, is deferred to Expansion as F.2b3, recorded on a real macOS machine and Linux desktop.

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

Record the OS name, version and build; session type and desktop environment; app build or tag; and each result in F.2b3, following F.2b1. If a window manager reports no minimize event, record that result and update `MinimizeLock.IsSupported` to omit the unsupported checkbox. Untested targets remain unobserved.
