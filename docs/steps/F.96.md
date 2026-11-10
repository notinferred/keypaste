# F.96 — The desktop app waits for a display on a Mac whose displays sleep

Completed 2026-11-09 on `backlog-2026-11`, source only; CI has not run on it, so there are no run IDs.

## Amendments

The founder selected BACKLOG's two rows on the sleeping display, deciding that if a start at login can hit the crash the app waits for a display and then starts normally. Two choices follow from it: a start from a person's own action waits the same way, since it reaches the same call, and the wait has no bound, since a Mac's display can sleep for days and a start that gave up would leave a login with no app. A start handed to the running copy never waits.

## Evidence

Cause: -6661 is `kCVReturnInvalidArgument`. Avalonia 12.1.0's `PlatformRenderTimer::RegisterTick` returns what `CVDisplayLinkCreateWithActiveCGDisplays` returns, and `DoInitialize` reaches it through `new Compositor` whether or not a window will open. A read-only probe on the founder's Mac, whose display had gone to sleep by itself at 18:55:24 on 2026-11-09, got -6661 from that call, 0 active displays and 1 online; `CVDisplayLinkCreateWithCGDisplays` with an empty list also returns -6661.

Reach: `--background` differs from a normal start only in `ShowInDock`. The LaunchAgent is `RunAtLoad` under `LimitLoadToSessionType` Aqua, with no display condition, and launchd starts agents as part of login, so an automatic login with no awake display reaches the call. No login start was observed failing. Windows and Linux use `SleepLoopRenderTimer` or `UiThreadRenderTimer`, which need no display.

`DisplayWaitTests`, 8 passed through `Program.Run` with a stand-in for the session: a display awake starts at once, every display asleep starts once one wakes, with and without `--background`, a session ending during the wait exits 0 and releases the home without starting, a render timer that still fails exits 1 with a message, another failure stays a failure, and a start the running copy takes never looks at the display. `verify.sh compile` passed.

## Limits and follow-ups

The tests do not run `MacSession`'s CoreVideo call or its signals, nor Avalonia on a sleeping display; the built app has not run in that state, which BACKLOG's row keeps open. The failure that still gets past the check is recognized by Avalonia's message, which an upgrade could reword. A second start during the wait is told the app did not answer, as the first has no endpoint yet.
