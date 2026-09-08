# The desktop app

A window over the same vault the CLI reads. It opens a `.kdbx`, holds it while you are using it, and locks it when you are not.

## What it is not, yet

**Approvals still happen in the terminal.** When an AI agent asks `keypaste-mcp` for a credential, the request goes to a `keypaste agent` you started in your own terminal. The Agent Activity screen currently reports whether that agent is running. The design is settled: the agent owns the approver pipe and the app will connect as a UI client (D-0054 in [DECISIONS.md](../DECISIONS.md#d-0054--the-agent-owns-the-approver-pipe-the-app-is-a-client-of-it)). The client channel and approval controls remain step 4.3 in [STEPS](STEPS.md).

**The desktop app is not published.** Build it from source, below. `app.yml` can package desktop archives on version tags, but keeps them as workflow artifacts; `release.yml` publishes the CLI/MCP downloads. See [RELEASE](RELEASE.md) for the distribution matrix and remaining desktop publication requirements.

**Entering an existing password or variable value still requires the CLI.** Adding one in the app generates its value. `keypaste add` and `keypaste env set` prompt for an existing value; secure desktop input is scheduled in step 4.9 of [STEPS](STEPS.md).

## What the screens show

**Entries** lists titles and groups. The group tree filters the list, and the search box matches titles and group paths case-insensitively; it does not search secret values. Selecting an entry shows its username, URL and notes. **An entry's password is never shown on this screen**: there is a Copy button, and `keypaste get --show` for the times you have to read one. You can edit the username, URL and notes inline, add an entry with a generated password, and delete one behind a confirmation, because there is no undo.

**Env Sets** shows each project as a card with the `keypaste run <project> -- ` line that injects it, and a button to copy that line. Opening a card shows the project's variables as a masked table. **Hold a value to reveal it** — one at a time, for as long as you hold it, and gone the moment you let go, switch screens or lock. There is a Copy button on every row.

**Copying clears itself.** A copied secret leaves the clipboard after twenty seconds, with a countdown in the header and a Clear now button. It is cleared early if you lock, and before the app exits if you quit. It is left alone if you have copied something else since. Nothing clears it if the app is killed. A copied `keypaste run` line is not a secret and is never cleared.

## Editing your vault

Everything the app writes goes through the same core as the CLI. A fresh `keypaste ls`, `keypaste get`, `keypaste env ls` or `keypaste run` invocation reads the saved change from the same file. An already unlocked process, including a terminal approver, retains its in-memory copy until reopened. Both front ends use the serialization code exercised by the KeePassXC compatibility gate.

**If something else changes the file while the app has it open, the app refuses to save and says so.** The app holds your vault in memory for as long as it is unlocked, so writing it back would revert whatever a terminal or KeePassXC wrote in the meantime — silently, and with no history entry to recover from, because the change was never in the app's copy. Nothing is written. Lock and unlock to pick up the other change, then make yours again.

## Building and running it

Install the .NET SDK version selected by the repository's `global.json`. The app lives in its own solution so that ordinary backend work does not pay to build it:

```
dotnet restore keypaste.app.slnx --locked-mode
dotnet build   keypaste.app.slnx -c Release
dotnet run --project src/Keypaste.App -c Release
```

If a build fails with `Access to the path 'artifacts\...' is denied`, an MSBuild worker node or a still-running app may be holding a file. Close the app and pass `-nodeReuse:false` when rebuilding. If the failure persists, also check the path's permissions.

## What it needs on each platform

These are the native GUI prerequisites for the current packaging targets. Building from source also requires the SDK above; CI archives include the .NET runtime but are not published installers.

| Platform | Native prerequisites |
|---|---|
| **Windows** | No separate browser engine or .NET runtime for a self-contained archive |
| **macOS** | No separate browser engine or .NET runtime for a self-contained archive |
| **Linux** | `libx11-6 libice6 libsm6 libfontconfig1`, and an X11 or XWayland session |

Avalonia draws with Skia; the app does not embed WebKit or Chromium. The supported OS versions and Linux distribution baseline still need whole-package native verification in step 4.7b. A renderer's glibc baseline alone does not establish the app's support range.

## Opening a vault

Three ways in, and all three end at the same place:

- **Drag a `.kdbx` file onto the window.**
- **Browse** (`Ctrl/Cmd+O`) for one.
- **Pick one you have opened before** from the recent list.

Whichever you use, the file's header is read before you are asked for a password, so a file that was never a vault is refused immediately rather than after you have typed. Vault creation currently uses `keypaste init`; the desktop app has no creation screen yet. [PRODUCT](PRODUCT.md#4-engineering-laws) §4.2 requires shared core logic, with neither front end waiting for the other.

## Locking

**The vault locks after five minutes of no keyboard and no mouse.** Change it in Settings, between one minute and eight hours; the choice is read back at every launch, so it is the timeout in force rather than the one on the screen. There is deliberately no "never" — a setting that turned the feature off would be the one everybody chose the first time the countdown interrupted them, and an unattended machine is the threat idle locking exists for.

Thirty seconds before it locks, a quiet line appears in the header. Any key or click cancels it.

**Locking now is always one keystroke:** `Ctrl/Cmd+L`, or the button at the bottom of the sidebar. That is the honest counterweight to a five-minute default.

Two behaviours worth knowing:

- **Switching to another window does not lock**, and does not pause the countdown either. Alt-tabbing to a terminal is normal; leaving for ten minutes is not.
- **A machine that slept through the timeout wakes locked.** The countdown reads both the wall clock and the monotonic clock and takes whichever says longer, and it is re-checked when the window is activated — because a timer scheduled on a monotonic clock that slept too would simply never fire.

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
| `Escape` | Clear the password field |

On macOS the modifier is Cmd; everywhere else, Ctrl.

## Files it keeps, and how to delete them

Both live in `~/.keypaste`, beside the audit log and the policy file, and neither travels with your vault — they describe **this machine**. `KEYPASTE_HOME` moves them.

| | |
|---|---|
| `recent.toml` | The vaults you have opened here. Paths only — no entry names, no secrets |
| `app.toml` | Idle timeout, theme, lock-on-minimize |

`recent.toml` records a vault **only after it opens successfully**, so a file you were sent and could not open leaves no trace. It holds at most ten, most recent first. Remove one from the list in the app, clear the whole list in Settings, or delete the file. On Linux and macOS it is written owner-only; on Windows it inherits your profile's permissions, which is the same protection `audit.jsonl` already relies on.

Both files fail closed: if either is unreadable, the app uses its defaults and **does not overwrite what it could not read**, so a file you are part-way through editing by hand survives.

Paths in `recent.toml` are written with forward slashes, including on Windows. That is not cosmetic — the reader keypaste uses refuses a backslash inside a value, deliberately, so that a pattern in `policy.toml` cannot be written one way and mean another. The app writes `C:/Users/…` rather than weakening that rule for every file.

## The Log screen

It shows the same table `keypaste log` prints, from the same `~/.keypaste/audit.jsonl`, rendered by the same code — not a second implementation that could drift (DECISIONS.md D-0032). It needs no unlocked vault, because the audit log is a record of this machine rather than of your vault. "Verify chain" shows what the hash chain says about the file.

A missing log is normal before the MCP bridge has initialized one. Requests and bridge events populate it; opening the desktop Log screen does not require a prior credential release.

## What you should know about the master password

The field you type it into is not a text box, and that is deliberate: Avalonia's `TextBox` exposes its contents through the accessibility layer with no exception for password fields, and keeps an undo history of `string`s that cannot be wiped. The control here holds no password at all — it reports one character at a time to a buffer that is wiped on every path out, and its accessibility peer exposes nothing.

**One honest limit.** Each keystroke arrives as a short-lived string the runtime will not let us wipe, and a **paste** arrives as the whole password in one such string. That is narrower than a field holding your password for as long as the window is open, and it is not nothing. `SECURITY.md` carries the full account.

## Checking a build by hand

CI builds and packages on three operating systems; the current desktop logic tests do not verify rendered pixels. Rendering coverage remains step 4.6, and native installation checks remain step 4.7b. Use a disposable vault with harmless test values for this manual checklist before any release that includes the app:

1. Launch with no `recent.toml`: the empty state names `keypaste init` and does not look broken.
2. Open a vault by drag, and again by the picker. A non-`.kdbx` file is refused **before** the password field.
3. Wrong password: a calm message, still locked, and nothing added to `recent.toml`.
4. Right password: the shell appears, and the vault is now in `recent.toml`.
5. **Keyboard only** — launch, type, Enter, reach all five destinations, lock with `Ctrl/Cmd+L`, without touching the mouse.
6. Set the timeout to one minute and wait: the countdown appears, typing cancels it, leaving it alone returns you to the unlock screen. Quit, relaunch and wait again without opening Settings — still one minute.
7. Suspend the machine for longer than the timeout. It wakes locked.
8. The theme follows the OS, and both light and dark read as calm. Choose Dark, quit and relaunch: the first frame is dark, with no flash of the light one on the way.
9. Put a number the list does not offer into `app.toml` by hand — `idle_timeout_seconds = 137` — and relaunch. Settings names it, the countdown arrives at 137 seconds, and the file is unchanged afterwards.
10. The Log screen matches `keypaste log` for the same `~/.keypaste/audit.jsonl`.
11. Agent Activity says the right thing both with and without a `keypaste agent` running.
12. Entries lists titles and groups. Filter by a group and search for part of a title or group path; case changes still match. Selecting an entry shows a username, a URL and notes, and a row of dots where the password is.
13. Copy a password. The countdown appears and the bar drains. Paste into an editor — it is there.
    Wait it out and paste again — it is gone.
14. Copy, then `Ctrl/Cmd+L`. Paste: nothing.
15. Copy, then quit the app. Paste: nothing.
16. **Windows only, on a machine where Clipboard History is enabled and not disabled by policy**:
    copy a known harmless string and confirm Win+V shows it — that is the control. Then copy a
    password from the app and open Win+V: the value is not in it. `keypaste get` sets the same
    formats since D-0056; whether that holds on a real machine is step 1.5a's Verify line in
    `docs/STEPS.md`, not this list.
17. Hold a masked value in Env Sets. The characters appear; release and they go. Hold a second row
    while the first is showing — only one is ever revealed.
18. Copy a project's run command, paste it in a terminal, finish the line: it runs with the
    project's variables.
19. Add, edit and delete an entry, then check `keypaste ls` and `keypaste get` in a terminal.
20. With the app open on a vault, run `keypaste env set` against the same file in a terminal. Come
    back and make any edit: the app refuses, says why, and the terminal's write is still there.
21. Generate a password in the app, then read it back with `keypaste get --show`.
22. Open the vault the app wrote in KeePassXC.
