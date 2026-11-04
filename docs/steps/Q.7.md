# Q.7 — Work with assistive technology

Completed 2026-11-04 on `q7-assistive-technology`; ci 41931887073 passed its fast tier at the code integrated, which differs from it only in this record.

## Amendments

The founder directed on 2026-11-04 that the row closes on the headless tree and CI, with the screen-reader check on each platform moved to BACKLOG, so that part of the acceptance is open. Causes inside Avalonia go to BACKLOG as to-dos, and a small workaround in keypaste's own code is allowed; none was built.

Settled in the step: `Controls/Announcer.cs` gives a control a line to say through its window's one hidden live text, the main window's or a prompt's, which replaces the shell's own. The sidebar's dot says `live` or `refusing`. A row's name comes from its item's `ToString`, as lists already did. A list's name was added wherever the tree had none.

## Evidence

`AssistiveTechnologyTests` sweeps nine places, the New item form, an open item, an open project and the unlock screen for an unnamed field, list or row, hidden forms included, and for each of `RenderedShell`'s secrets. It also holds Items' row columns, the sidebar's levels and folds, the dot's and a tag's words, heading levels, a toast said once, a refused unlock said at each refusal and a prompt's opening said once while its countdown ticks. `ShellAnnouncementsTests` still holds the idle warning.

On the founder's Mac (macOS 26.6, arm64): `verify.sh test tests/Keypaste.App.Tests` ran 1510 tests, with 1507 passing, 3 skipped and none failing, and `verify.sh compile` passed.

**macOS.** Q.2's record said System Events reached no button by name. On 2026-11-04 `entire contents` of the window on the first screen, with a throwaway home, listed `AXButton`s named Open another file… and Create a new vault…, an `AXList` named KeePassXC's databases and an `AXRow` named by its row, each beneath several unnamed `AXGroup`s. So Avalonia's peers do reach AppKit, and the likeliest reason for Q.2's reading is the nesting, which was not re-tested against its method. `libAvaloniaNative` holds `AXLiveRegionChanged` and `AXHeading`, and `Avalonia.Win32.Automation` holds `RaiseLiveRegionChanged` and `ToUiaHeadingLevel`. No notification was observed on any platform.

## Limits and follow-ups

No person or screen reader has heard an announcement, on any platform; BACKLOG holds that check, Linux's bridge, where `Avalonia.FreeDesktop.AtSpi` shows no live-region code, and macOS's unnamed groups. Peers are created lazily, so a screen reader may first hear a window's live text only after it has walked the tree. The stack restarts an identical toast (Q.6), so a repeat is not said again, and messages arriving together can replace each other in the one live text. Visible labels were checked only where `ScreenRenderer` still draws, because it fails earlier (BACKLOG); the sidebar's word was seen there, and the new form labels were not seen drawn.
