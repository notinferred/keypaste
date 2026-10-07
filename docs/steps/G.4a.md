# G.4a — Stay in the menu bar or tray and open at login

Completed 2026-09-30 on branch `task/g4a`, source only; dev runs 36728625973, 36733135448, 36733467494, 36735599936, 36739944063 and 36741991767; 36739944063, the last on all three runners, was green on Linux and Windows, and 36741991767 on Linux.

## Amendments

The row was expanded against the code when selected:
- **Build.** A tray icon (`App/AppTray.cs`, Avalonia's `TrayIcon`) offers Open keypaste, Lock (only while a vault is open) and Quit keypaste. With it on, closing the main window locks the session with the new reason `Closed`, stops a hardware key waiting for its touch, and hides the window, and a locked unlock screen is rebuilt so a typed password is dropped; an unlock still deriving its key when the window closes locks again as it finishes, because its screen has been replaced. Only Quit or the platform's quit ends the process, and on macOS a Dock click or a second open shows the window through Avalonia's reopen activation. `stay_in_tray` in `app.toml` is written only once chosen; unset, it is on for Windows and macOS and off for Linux, where a desktop may show no tray. Settings gains a Startup card with the tray switch and Open keypaste at login. The login entry is the platform's own per-user mechanism, starting the app with `--background`: the `keypaste` value under HKCU's Run key, `~/Library/LaunchAgents/com.keypaste.app.plist`, or `$XDG_CONFIG_HOME/autostart/keypaste.desktop`. On Windows the switch also reads the value's entry under `Explorer\StartupApproved\Run`, which Task Manager turns off without removing the Run value, and switching on clears it. Its writers are in `Core/Login/`, because `TheAppSharesTheWriterTests` keeps file writes out of the desktop's own code. A background start with the tray on names no main window until Open, so the lifetime shows none; with the tray off it shows the window.
- **Verify.** Closing the main window with a prompt open refuses the request as `vault-locked` and takes the prompt down with the tray on or off; off, the app is asked to quit; on, it is not, the window is hidden and its unlock screen shown, and the tray's Open and Quit work. Closing into the tray while an unlock waits on a YubiKey or finishes afterwards leaves the session locked. A background start opens no window, leaves the session locked and lets another process take the vault's claim. Each login writer is checked against a fake registry or a temporary home, and the registry and LaunchAgent writers on Windows and macOS by `ci.yml`.
- The task text's "keeps its unlock session" yields to the row: closing still ends the session (D-0313), amending D-0343 only for the tray.

## Evidence

- `ClosingTheMainWindowTests` (6) run `App.Launch` in Avalonia's own lifetime: the F.21 case pinned to the tray off, the same case with it on, an unlock whose software YubiKey answers only after the close, one whose key waits for a touch the close cancels, and a start at login with the tray on and off. `AppTrayTests` (1) holds each menu item to its action and Lock to the session.
- `LoginItemTests` (12), in Core.Tests so every `ci.yml` run covers Windows and macOS: the Run value is `"<program>" --background` and a program path holding a quote is refused; a value Windows turned off reads as off and switching on clears its switch; `RegistryRunKey` and `RegistryStartupApproval` write, read and delete under a throwaway HKCU key on Windows, including Task Manager's disabled bytes; the plist parses to `ProgramArguments` of the program and `--background`, `RunAtLoad` and a mode no other user can write; the autostart `Exec` line follows the Desktop Entry quoting and string-escape rules for a space, `$`, `%`, `"` and `\`, and a path with a line break is refused; an entry naming another copy of the app reads as off; inside an AppImage the entry starts the image, not its mount.
- `StartupSettingsScreenTests` (5): the platform default, the tray choice surviving a restart and reaching `DesktopPreferences.Changed`, Open at login writing and removing the entry, a refused write staying off with a message, and no row without a mechanism.
- `AppSettingsTests.The_tray_choice_round_trips_and_is_absent_until_made` (2).

Dev runs, each `dev.sh`'s dispatch of `dev.yml`:

| Target, runners, commit | Run | Result |
|---|---|---|
| `core,app`, Linux, `087a988` | 36728625973 | Core 2,215 passed and 14 skipped; App failed `TheAppSharesTheWriterTests`, which found the login writers in the desktop, and the tray case's Lock check, which ran before the refresh the lock posts |
| `consistency`, Linux, `9fa4528` | 36733135448 | Build refused an unnecessary `using` in Core |
| `core,app,consistency`, all, `70e9739` | 36733467494 | `AppTrayTests` counted the menu's separator on all three; everything else passed on Linux (857) and Windows (859); macOS also failed F.28's four |
| `core,app,consistency`, all, `dd8347e` | 36735599936 | Linux and Windows green; macOS failed only F.28's four, with 852 App tests passing |
| `core,app,consistency`, all, `7dafb18`, after review | 36739944063 | Linux and Windows green: Core 2,226 and 2,235 passed, App 850 and 851, the registry test running on Windows; macOS failed only F.28's four, with Core 2,226 and App 844 passing |
| `app,consistency`, Linux, `1a09a91`, the records added | 36741991767 | Green: App 850 passed and 4 skipped, consistency 43 |

## Decisions

D-0385, D-0386.

## Limits and follow-ups

- **No native display observed.** Every test runs on Avalonia's headless platform, which makes no tray icon and raises no reopen activation; that the icon shows in the Windows notification area, the macOS menu bar or a Linux StatusNotifier host, that a Dock click shows the window, and that a real login starts the app hidden, is R.1a's manual check.
- **F.38**, found in review: nothing makes the app single-instance, so on Windows and Linux opening keypaste while it sits in the tray starts a second process.
- **macOS keeps its Dock icon,** and the menu bar shows the colour icon, not a template image. A LaunchAgent turned off in System Settings › Login Items keeps its plist, so the switch still reads on.
- **Uninstalling leaves the login entry.** The per-user MSI does not remove the Run value, and removing the app bundle or AppImage leaves the plist or `.desktop` file; each then starts nothing. `docs/desktop.md` says to switch it off first.
- **An AppImage moved after the entry is written** leaves an entry that starts nothing; the switch then reads as off.
- **No process gate covers the window's life.** `verify-desktop-approval.sh` drives the prompt through `Keypaste.AppDriver`, not the main window, and `observe-desktop.yml`, which B.3 may delete, observes only a minimize.
- **F.28**, found here: four `EnvLaunchThroughAppTests` cannot pass on macOS, where the app opens no terminal; `app.yml` runs App.Tests on Linux only.
- **G.4b** tells the person when a bridge finds the locked app.
