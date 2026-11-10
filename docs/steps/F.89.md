# F.89 — What the Windows and Linux menus draw for a chord

Completed 2026-11-09 on `backlog-2026-11`, source only. No code changed, so no run bears on it.

## Amendments

The founder selected this from BACKLOG's investigation candidates on 2026-11-09: establish what the real menus draw, and add readable key names only if raw names appear. There is no STEPS row. None appear, so nothing was added and the row is closed.

## Evidence

A probe, since deleted, opened `AppMenu.Flyout` from `AppCommands(mac: false)` on `RenderedShell`'s window through the real menu button, under Skia, opened each of the nine groups and read `PART_InputGestureText` from each drawn `MenuItem`. All 23 commands with a chord were read and none shows a raw name: Settings `Ctrl+,`, the places `Ctrl+1` to `Ctrl+4`, Zoom in `Ctrl++`, Zoom out `Ctrl+-`, Actual size `Ctrl+0`, Delete `Delete`, full screen `F11`, and the letters as `Ctrl+N`. The View and keypaste frames were viewed and show the same text. The pixel score of the raw name in each of those cells was at most 0.18, and of the text the block holds 0.79 to 0.86, under `DrawnFrame`'s 0.9, so the scores only separate the two and the proof is the block's text and the viewed frames.

Q.9's `Ctrl+OemPlus`, `Ctrl+OemComma` and `Ctrl+D1` come from a call with no Avalonia platform registered. The same `ToString("p", null)` gave those on a test thread outside the headless session and `Ctrl++`, `Ctrl+,` and `Ctrl+1` on the session's thread. Q.9's own call is not in the tree, so how it was made is inferred.

In Avalonia 12.1.0 (tag commit `a21b9f57`, the commit the package's nuspec names), Fluent's `MenuItem.xaml` binds `PART_InputGestureText` through `PlatformKeyGestureConverter`, which calls `gesture.ToString("p", null)`. `KeyGestureFormatInfo.GetInstance` takes the `KeyGestureFormatInfo` the platform registered, and only without one the invariant, which prints enum names. `Win32Platform`, `X11Platform` and `WaylandPlatform` each register an empty override table, which turns on the common table holding `OemPlus`, `OemComma`, `OemMinus` and `D0` to `D9`; they differ from the headless platform's registration only in the Meta name, which the Control chords here never use. With the Win32 and X11 registrations built by hand, all 23 chords formatted as drawn.

Nothing else displays these gestures: `MenuItem.InputGesture` and `NativeMenuItem.Gesture` in `AppMenu.cs` are the only uses in `src`, and `ShortcutText` and `SearchShortcut` are written by hand.

## Limits and follow-ups

The app was not run on real Windows or Linux; those platforms' registrations were read from source and only their presence in the shipped `Avalonia.Win32` and `Avalonia.X11` assemblies was checked.

The macOS menu bar is out of this question and was not run: AppKit draws it from `setKeyEquivalent`, and `MenuCharFromVirtualKey` in `KeyTransform.mm` maps `OemComma` to `,`, `D1` to `1` and `OemPlus` to `=`, so Zoom in would show ⌘=. A macOS context menu does go through the converter, and Delete, its only chord there, formats as `⌘+⌫` with macOS's table, a plus AppKit's menus would not draw. BACKLOG option if wanted; no row added.
