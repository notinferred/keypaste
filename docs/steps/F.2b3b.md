# F.2b3b — Observe the remaining real-desktop lock acts

Completed 2026-09-24 on `main` above F.21, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

Build: as [F.2b3a](F.2b3a.md) did on Windows, run the app from an `app.yml` artifact at a named commit, with a disposable vault and a `keypaste-mcp` configured for it, and record each act. On a real Windows 10 or 11 desktop: closing the main window through the window manager while a request waits in the prompt window. On a real Linux X11 or XWayland desktop: that act, minimizing from the title bar with minimize-lock on, with it off and after a restart, and a request arriving while another application has focus. Each result names the OS, version and build, the session type and window manager, the commit, and the audit lines the act produced. A window manager that reports no minimize is recorded, and `MinimizeLock.IsSupported` then omits the checkbox there, with a test. Verify: the record holds every act with the source SHA. Closing the main window with a request waiting leaves `vault-locked` for it in the audit log. On Linux the prompt appears over the other window with focus on Deny. An act that could not be made is recorded as unobserved, which leaves the row open.

Amendments, each on the founder's direction on 2026-09-24:

- Sleeping past the idle timeout was removed from the acts on both desktops; U.2's tests carry it.
- Acts may be made by the observer's own automation (D-0342). Every act here was.
- The Linux desktop was an Xfce session in the WSL Ubuntu on the Windows machine, chosen over a virtual machine.

## What changed for users

Nothing. This step observed and changed no code.

## Evidence

Every act ran the packages `app.yml` run 36080707226 built at `8fde202` on branch `f21`, the F.21 repair that run's desktop tests passed. Each package's sha256 matched its published `.sha256`. Requests came from the package's own bridge with `--client-label f2b3b --expose env/**`, sent `initialize` and one `request_credential` for `env/probe/TOKEN` over stdio. The vault was made by a script over `Keypaste.Core`, with the recent list pointing at it, in a scratch `KEYPASTE_HOME`.

**Windows.** Windows 10 Pro 22H2, build 19045.6332, DWM. `keypaste-app-0.3.1-dryrun-win-x64.zip`, sha256 `4b52f2ba6148446d28267b0160659fefa02bb3b9a913f7d3bc72b7ada1409500`, unzipped and run with its `keypaste-mcp.exe`. The app was unlocked through UI Automation.

| Act | Result |
|---|---|
| Close the main window with a request waiting | Passed. The request was sent at 21:30:13 and the prompt window came up beside the main one. At 21:30:37.012 the main window was closed with UI Automation's `WindowPattern.Close`, since the title-bar button was not found in the automation tree on that pass. The client's reply at 21:30:37 was "the vault is locked, so there was nothing to read. Nothing was read or released." Audit seq 1: `denied`, `vault-locked`, "the vault was locked before anybody answered". The process had exited within 3 seconds. |

**Linux.** Ubuntu 24.04.3 LTS in WSL2, kernel 6.18.33.2-microsoft-standard-WSL2. X11 session on TigerVNC 1.13.1's `Xvnc` at 1600×1000, window manager xfwm4 4.18.0 with the Xfce panel and desktop; nothing from WSLg's Wayland display was used. `keypaste-app-0.3.1-dryrun-linux-x64-internal-unsigned.AppImage`, sha256 `50317a5506b2df20714fa34d75a3f327e749955c4566151b02bb989ead66e181`, run through FUSE, with its bridge started as `<AppImage> mcp` as a client the app registers runs it. The app's automation tree was not reachable over AT-SPI, so acts were driven with `xdotool` clicks and keys at positions read from screenshots of the display, and window state was read with `xprop` and `wmctrl`. The minimize acts leave no audit line, because only the bridge writes the log.

| Act | Result |
|---|---|
| 1. Minimize-lock on | Passed. Ticked in Settings (`lock_when_minimized = 1`). The `TOKEN` password was copied and read back from the clipboard, and at 21:37:11 the title bar's minimize button was clicked: `WM_STATE` Iconic, `_NET_WM_STATE_HIDDEN`. Restored from the panel's task button: the unlock screen, and the clipboard held nothing. |
| 2. Minimize-lock off, 1-minute timeout | Passed. The timeout was chosen at 21:37:41.0 and the box unticked (`idle_timeout_seconds = 60`, `lock_when_minimized = 0`). Minimized from the title bar at 21:37:42 (Iconic) and restored at 21:37:44: still unlocked. Untouched, the app locked between 21:38:41.4 and 21:38:43.5, polled every 2 seconds, about 60 seconds after the last input. |
| 3. After a restart | Passed. With an 8-hour timeout, the box was ticked and the app quit with its close button at 21:39:00. `app.toml`'s sha256 was `a205947ce0ab3938d097760a1596a2b338c8c2c816b322a8fd6d683cc930cfb5`. Relaunched with the same home and unlocked without opening Settings; minimized from the title bar at 21:39:14 and restored: the unlock screen. `app.toml`'s sha256 was unchanged. |
| 4. Close the main window with a request waiting | Passed. The request was sent at 21:39:40 and the prompt window was mapped over the main one. At 21:39:41.484 the main window's title-bar close button was clicked. The client's reply was "the vault is locked, so there was nothing to read. Nothing was read or released." Audit seq 1: `denied`, `vault-locked`, "the vault was locked before anybody answered". The process had exited within 3 seconds and no window of it remained. |
| 5. A request while another application has focus | Passed. Mousepad was activated and typed into, and the request was sent at 21:40:11. The prompt was mapped 0.26 seconds later, xfwm4 made it the active window, and it was the top of `_NET_CLIENT_LIST_STACKING`, drawn over Mousepad. At 21:40:20.438 Return was pressed: the client's reply was "DENIED. A person read this request and said no.", audit seq 2 `denied`, `prompt`, "a person refused this request", and Mousepad's text was unchanged. Focus then returned to Mousepad. |

xfwm4 reported minimize through `WM_STATE`, so `MinimizeLock.IsSupported` did not change.

## Decisions

None.

## Limits and follow-ups

- **The Linux display.** `Xvnc` is a real X server and xfwm4 a real window manager, but the display is a framebuffer read through screenshots rather than a monitor, and WSL2 is not a clean Linux install. GNOME, KDE and XWayland under a Wayland compositor were not run.
- **The Windows close.** It was asked through UI Automation, not clicked on the title bar; [F.2b3a](F.2b3a.md)'s act 4 clicked the button, which is where the defect was seen.
- **Focus on Windows.** Windows kept the foreground on the other application in F.2b3a's act 6, while xfwm4 gave it to the prompt here. No row changes that behavior.
- **Found here.** In the default 1000×680 window, with an entry open, the Entries toolbar overlaps: [F.22](F.22.md).
- **The artifacts' commit.** `8fde202` is the F.21 repair as pushed on `f21` for its `app.yml` run; F.21 was integrated on `main` with the same source.
