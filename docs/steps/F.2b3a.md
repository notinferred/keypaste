# F.2b3a — Observe desktop lock behavior on Windows

Completed 2026-09-24 on `main` above `15b2c47`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

F.2b3 was selected as the next row in track order, since no code task was ready. Its Build: a person runs the app from an `app.yml` artifact at a named commit, with a disposable vault and a `keypaste-mcp` configured for it, on a real Windows 10 or 11 desktop and a real Linux X11 or XWayland desktop, and records each act. The acts are minimizing from the title bar with minimize-lock on, with it off and after a restart; closing the window while a request waits in the prompt window; sleeping past the idle timeout and waking; and a request arriving while another application has focus. Its Verify requires every act on both desktops, `vault-locked` for a request waiting when the window closes, the unlock screen and a refused request after waking past the timeout, and the prompt over the other window with focus on Deny.

Amendments, each on the founder's direction on 2026-09-24:

- The observation was made on Windows only. F.2b3 was split: this record holds the Windows observations, and [STEPS](../STEPS.md) keeps F.2b3b for what they did not settle. That is the close act after F.21's repair, the sleep act, and every act on Linux.
- The founder directed the remaining acts to be checked without them. The focus act and a sleep attempt were driven through UI Automation on the same desktop.

The artifact had to be built at a named commit first. The founder had the four unpushed commits E.1a to F.17 redated into 18:00–06:00 and pushed. That push found F.19 and F.20, and F.19 was repaired first.

## What changed for users

Nothing. This step observed and changed no code.

## Evidence

**Environment.** Windows 10 Pro 22H2, build 19045.6332, with its own window manager (DWM). The artifact was `keypaste-app-0.3.1-dryrun-win-x64.zip` from `app.yml` run 36044104913 at `15b2c47`, sha256 `e922cf09f50c95a46549bd77a552d70b763fe6f5e43c7f296cb0505a6d14ef6f`, checked against its `.sha256`, unzipped and run with `KEYPASTE_HOME` set to a scratch directory. Requests came from the packaged `keypaste-mcp` with `--client-label f2b3`, driven over stdio by a script sending `initialize` and one `request_credential` for `env/probe/TOKEN`. The person created the vault in the app. The minimize acts leave no audit line, because only `keypaste-mcp` writes the log.

| Act | Result |
|---|---|
| 1. Minimize-lock on | Title-bar minimize and restore showed the unlock screen. A `TOKEN` value copied before the minimize did not paste into Notepad afterwards. Passed. |
| 2. Minimize-lock off, 1-minute timeout | Minimize and restore left the vault unlocked. Untouched, the header warning appeared and the app locked on time. The warning read "Locking in 29 seconds." and did not change. Passed. |
| 3. After a restart | With minimize-lock on and an 8-hour timeout, the person quit, and the app was relaunched with the same home. They unlocked without opening Settings, minimized and restored: unlock screen. `app.toml`'s sha256 was the same before the relaunch and after the act. Passed. |
| 4. Close the main window with a request waiting | **Failed; F.21.** The request was sent at 15:37:41 and the main window closed with its close button. The prompt stayed up, and process 25900 kept running with it as its only visible top-level window. At 15:38:26 the request ended `decision: denied`, `method: timed-out`, "nobody answered inside the window" (audit seq 2), not `vault-locked`. The process exited only after the prompt, its last window, closed. In a repeat at 15:39:30, the person closed the main window and then pressed Approve on the remaining prompt. The client received the value, and the audit logged `granted`, `prompt`, "a person approved this request" (seq 3). |
| 5. Sleep past the idle timeout | With a 1-minute timeout, the app was unlocked through UI Automation at 16:28:01 and the observer suspended the PC at 16:28:08 (Kernel-Power 42, "Application API"). The person woke it at 16:28:24 (Power-Troubleshooter 1: 23 s asleep), before the timeout had passed. Five seconds later the shell was still shown, which is correct for 28 s elapsed. The idle lock then overtook a request sent at 16:28:58, which ended `vault-locked`, "the vault was locked before anybody answered". The app had not locked on waking, as expected after 23 s. |
| 6. A request while another application has focus | Notepad was brought to the foreground and a request was sent to the app, which had been unlocked through UI Automation on a second vault the CLI made. The prompt existed within 0.25 s and its Deny button had keyboard focus. The foreground window stayed Notepad's, because Windows did not hand the prompt the foreground, so keystrokes went on reaching Notepad. `ApprovalWindow.axaml` sets `Topmost`, so the prompt draws over Notepad; the z-order itself was not measured. Deny was invoked, and the audit logged `denied`, `prompt`, "a person refused this request". |

While the app was locked, a request was refused at once, `denied`, `no-approver`, "no keypaste process holds the vault unlocked", with no prompt.

**Mechanism of act 4.** `App.axaml.cs` ends the authority, and with it the session, only in `OnShutdownRequested`. The app sets no `ShutdownMode`, so Avalonia's default `OnLastWindowClose` applies. While `WindowApprovalChannel`'s prompt window is open, closing `MainWindow` is not the last close: no shutdown is requested, and the owner keeps answering until the prompt closes.

## Decisions

None.

## Limits and follow-ups

- **Open rows.** F.21 carries act 4's defect and its regression. F.2b3b carries act 4 again after the repair, the sleep act on Windows, and every act on Linux.
- **Setup detour.** Setting up the vault, the person read the Env Sets card's directory and command fields as where a new project is saved. A new project writes nothing to the vault until its first variable, so the first attempt left the vault unchanged.
- **Idle warning.** The idle warning's number is set once, when the warning is raised, so it stays at 29 seconds until the lock.
- **Where these went.** Both setup and warning findings are in [BACKLOG](../BACKLOG.md). Neither contradicts [desktop](../desktop.md), which promises only that a line appears.
- **Screen lock.** Windows' own screen lock did not engage on waking; that is the machine's setting, outside keypaste.
