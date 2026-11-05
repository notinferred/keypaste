# Q.10 — Pick up where the person left off

Completed 2026-11-04 on `q10-window-state`; ci 41951124089 passed its fast tier at the code integrated, which differs from it only in this record.

## Amendments

This row reverses the 960 minimum width D-0344 set, whose ledger is gone: the window's minimum is 720×520, held with the default size and the narrow breakpoint in `WindowChrome.cs`, where Q.9 scales it.

Settled in the step, all reversible:

- Under 840 wide the sidebar collapses and Username and URL go together, one breakpoint so nothing returns as the window narrows; at 840 beside the sidebar each column still has the 72 the layout test asks.
- A collapsed sidebar still opens, over the content, because the group tree is only there; the content keeps its width, so no other screen meets a width under 720. Escape or a press outside closes it, and it is not the kept choice to hide it.
- The kept choice to hide the sidebar persists too, beyond the row's list, and the item reads Hide sidebar or Show sidebar. The chord is ⌃⌘S on macOS, its platform's own, and Ctrl+B elsewhere, which keypaste's plain fields do not use.
- The split now also holds across a trip away from Items, because the view is rebuilt there.
- `app.toml` carries a `[[window]]` section rather than new keys in `[[settings]]`, and `bounds` is one string. The strict reader holds a section to eight keys, of which `[[settings]]` already takes seven and keeps a later version's besides, and refuses a negative number, which a screen left of the main one needs; either would cost every reader, the CLI's included, the whole file. A reader older than this keeps the section as it found it (D-0520).
- A window nobody has touched writes nothing, and an unchanged one rewrites nothing, so the file changes only when the person changed something. The write takes only `[[window]]` from the running copy, so a hand edit made while the app runs is not overwritten, and it leaves a file that does not read whole alone (D-0028).

## Evidence

`WindowLayoutTests` restores saved bounds, brings off-screen bounds back, holds normal bounds under a maximize and a minimize, keeps the layout on close and lock through the app as launch composes it, and reopens with it; `EntriesViewLayoutTests` lays out Items at 720 and 839; `AppSettingsTests` holds the section's round trip, a negative position, bounds that make no window, and `keypaste use`'s rewrite. On the founder's Mac (macOS 26.6, arm64), `verify.sh test tests/Keypaste.App.Tests` ran 1988 tests with none failing, including `ScreenRenderer`, and `verify.sh test` over `AppSettingsTests`, `ChosenVaultTests`, `HealthCheckTests`, `ChosenVaultVerbTests`, `DoctorVerbTests` and `StatusTests` ran 143 with none failing; `verify.sh compile` passed.

The built app, opened for four seconds in a throwaway home with `bounds` of `120,90,820,560`, stood at exactly that in `CGWindowListCopyWindowInfo`; with `9000,9000,820,560` it stood at `822,557,820,560`, on the 1728×1117 display, which holds that macOS positions and working areas share points with window sizes.

`ScreenRenderer` adds `16b-secrets-720`, `16c-secrets-720-sidebar-open`, `16d-secrets-sidebar-hidden`, `29b-agents-720` and `95a-settings-720`, looked at in dark and `16d` in light. The first draw showed the search taking focus as the sidebar collapsed, a second amber; focus now moves only from inside the sidebar, held by a test.

Checked by reading, for what a window size, the old minimum, `app.toml`'s keys or the sidebar could break: `Keypaste.AppDriver`, which reaches the sidebar through the view model; `verify-crash-report.sh`, `exercise-desktop-acts.sh` and `verify-session-lifecycle.sh`, which read `crash_reports`, `terms` and `vault` by line and find no such key in `[[window]]`; `exercise-desktop-upgrade.sh`, which compares `app.toml`'s hash after a first start and is why an untouched window writes nothing; `LockedNotice`, whose watch on `app.toml` re-reads the chosen vault; `verify-demo.sh`, whose transcripts name none of this.

## Limits and follow-ups

No person has dragged, maximized, entered full screen or quit a real window: the real run above covers the restore's position and size on macOS alone, stopped by a kill, and the rest are headless. Windows and Linux were not opened, so `screen.Scaling` as their unit, two monitors at different scales, a frame's invisible border on Windows and a window manager moving a window after it maps are untested; such a move would count as a change and write `[[window]]`. A platform that resizes a window through its animation into full screen before reporting the state could keep the wrong bounds under it. The desktop gates, `compat` and the upgrade gate were not run locally.

A long vault name in the title bar's status can squeeze the search in a narrow window, because the status is not trimmed: trimming it would hide the save state at its end. Q.18 moves the search. A window spanning two screens comes back wholly on the one it most overlaps. A collapsed sidebar has no control of its own beyond the menu and the chord; a title bar toggle is a question for the founder.
