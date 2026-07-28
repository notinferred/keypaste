# The desktop app

A window over the same vault the CLI reads. It opens a `.kdbx`, holds it while you are using it, and locks it when you are not.

## What it is not, yet

**It is not the approver.** When an AI agent asks `keypaste-mcp` for a credential, the request still goes to a `keypaste agent` you started in your own terminal, and you still answer it there. This app does not bind the approver pipe and never will while both exist without a hand-off design (DECISIONS.md O-0017). The Agent Activity screen probes for a running agent and tells you which of the two states you are in; that is all it can honestly do today.

**It is not published.** There is nothing on `dl.keypaste.com` to install and `release.yml` does not build it. Build it from source, below.

**It does not type passwords for you.** Adding an entry or a variable generates the value; there is no field to type a specific one into, because a field that accumulates a secret is a different and more dangerous thing than the one the unlock screen uses. `keypaste add` and `keypaste env set` prompt for a value without putting it in a window.

## What the screens show

**Entries** lists titles and groups — what `keypaste ls` prints, and nothing more. Selecting one shows its username, URL and notes. **A password is never shown**: there is a Copy button, and `keypaste get --show` for the times you have to read one. You can edit the username, URL and notes inline, add an entry with a generated password, and delete one behind a confirmation, because there is no undo.

**Env Sets** shows each project as a card with the `keypaste run <project> -- ` line that injects it, and a button to copy that line. Opening a card shows the project's variables as a masked table. **Hold a value to reveal it** — one at a time, for as long as you hold it, and gone the moment you let go, switch screens or lock. There is a Copy button on every row.

**Copying clears itself.** A copied secret leaves the clipboard after twenty seconds, with a countdown in the header and a Clear now button. It is cleared early if you lock, and before the app exits if you quit. It is left alone if you have copied something else since. Nothing clears it if the app is killed. A copied `keypaste run` line is not a secret and is never cleared.

## Editing your vault

Everything the app writes goes through the same code the CLI writes through, so a change made here is visible to `keypaste ls`, `keypaste get`, `keypaste env ls` and `keypaste run` the moment it is saved — there is no sync and nothing to refresh. A vault the app writes is a vault the CLI wrote, and the KeePassXC compatibility gate that covers one covers the other.

**If something else changes the file while the app has it open, the app refuses to save and says so.** The app holds your vault in memory for as long as it is unlocked, so writing it back would revert whatever a terminal or KeePassXC wrote in the meantime — silently, and with no history entry to recover from, because the change was never in the app's copy. Nothing is written. Lock and unlock to pick up the other change, then make yours again.

## Building and running it

The app lives in its own solution so that ordinary backend work does not pay to build it:

```
dotnet restore keypaste.app.slnx --locked-mode
dotnet build   keypaste.app.slnx -c Release
dotnet run --project src/Keypaste.App -c Release
```

If a build fails with `Access to the path 'artifacts\...' is denied`, that is not a permissions problem: an MSBuild worker node or a still-running copy of the app is holding a file. Pass `-nodeReuse:false`, and close the app before rebuilding it.

## What it needs on each platform

| | |
|---|---|
| **Windows** | Nothing. No WebView2, no runtime to install |
| **macOS** | Nothing |
| **Linux** | `libx11-6 libice6 libsm6 libfontconfig1`, and an X11 or XWayland session |

There is no browser engine involved — Avalonia draws with Skia — so there is no WebKit or Chromium to install and none in the process holding your vault. Skia is built against glibc 2.17, which is below the 2.35 floor the published CLI binaries need, so the app does not narrow which Linux this project supports.

## Opening a vault

Three ways in, and all three end at the same place:

- **Drag a `.kdbx` file onto the window.**
- **Browse** (`Ctrl/Cmd+O`) for one.
- **Pick one you have opened before** from the recent list.

Whichever you use, the file's header is read before you are asked for a password, so a file that was never a vault is refused immediately rather than after you have typed. There is no "create a new vault" here: run `keypaste init`. Every feature exists in the CLI before it gets a window (docs/PRODUCT.md §4.2), and the empty state says so.

## Locking

**The vault locks after five minutes of no keyboard and no mouse.** Change it in Settings, between one minute and eight hours. There is deliberately no "never" — a setting that turned the feature off would be the one everybody chose the first time the countdown interrupted them, and an unattended machine is the threat idle locking exists for.

Thirty seconds before it locks, a quiet line appears in the header. Any key or click cancels it.

**Locking now is always one keystroke:** `Ctrl/Cmd+L`, or the button at the bottom of the sidebar. That is the honest counterweight to a five-minute default.

Two behaviours worth knowing:

- **Switching to another window does not lock**, and does not pause the countdown either. Alt-tabbing to a terminal is normal; leaving for ten minutes is not.
- **A machine that slept through the timeout wakes locked.** The countdown reads both the wall clock and the monotonic clock and takes whichever says longer, and it is re-checked when the window is activated — because a timer scheduled on a monotonic clock that slept too would simply never fire.

Locking disposes the vault, so nothing derived from it survives. You type your password again.

## Keyboard

Everything is reachable without the mouse. On launch, focus is on the password field, so the common case is: start the app, type, press Enter.

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

If the log does not exist yet, that is normal: nothing writes to it until an agent has asked `keypaste-mcp` for something.

## What you should know about the master password

The field you type it into is not a text box, and that is deliberate: Avalonia's `TextBox` exposes its contents through the accessibility layer with no exception for password fields, and keeps an undo history of `string`s that cannot be wiped. The control here holds no password at all — it reports one character at a time to a buffer that is wiped on every path out, and its accessibility peer exposes nothing.

**One honest limit.** Each keystroke arrives as a short-lived string the runtime will not let us wipe, and a **paste** arrives as the whole password in one such string. That is narrower than a field holding your password for as long as the window is open, and it is not nothing. `SECURITY.md` carries the full account.

## Checking a build by hand

CI can build the app and run its logic on all three operating systems, but it has no display, so nothing automated has ever seen this app draw. Run these before any release that includes it:

1. Launch with no `recent.toml`: the empty state names `keypaste init` and does not look broken.
2. Open a vault by drag, and again by the picker. A non-`.kdbx` file is refused **before** the password field.
3. Wrong password: a calm message, still locked, and nothing added to `recent.toml`.
4. Right password: the shell appears, and the vault is now in `recent.toml`.
5. **Keyboard only** — launch, type, Enter, reach all five destinations, lock with `Ctrl/Cmd+L`, without touching the mouse.
6. Set the timeout to one minute and wait: the countdown appears, typing cancels it, leaving it alone returns you to the unlock screen.
7. Suspend the machine for longer than the timeout. It wakes locked.
8. The theme follows the OS, and both light and dark read as calm.
9. The Log screen matches `keypaste log` for the same `~/.keypaste/audit.jsonl`.
10. Agent Activity says the right thing both with and without a `keypaste agent` running.
11. Entries lists titles and groups. Selecting one shows a username, a URL and notes, and a row of
    dots where the password is.
12. Copy a password. The countdown appears and the bar drains. Paste into an editor — it is there.
    Wait it out and paste again — it is gone.
13. Copy, then `Ctrl/Cmd+L`. Paste: nothing.
14. Copy, then quit the app. Paste: nothing.
15. **Windows only**: copy a password, then open clipboard history with Win+V. The value is not in
    it. Repeat with `keypaste get` — it is, which is the difference the app's window buys and the CLI
    cannot.
16. Hold a masked value in Env Sets. The characters appear; release and they go. Hold a second row
    while the first is showing — only one is ever revealed.
17. Copy a project's run command, paste it in a terminal, finish the line: it runs with the
    project's variables.
18. Add, edit and delete an entry, then check `keypaste ls` and `keypaste get` in a terminal.
19. With the app open on a vault, run `keypaste env set` against the same file in a terminal. Come
    back and make any edit: the app refuses, says why, and the terminal's write is still there.
20. Generate a password in the app, then read it back with `keypaste get --show`.
21. Open the vault the app wrote in KeePassXC.
