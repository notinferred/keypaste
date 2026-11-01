# Q.1 — Shortcuts match their modifier exactly

Completed 2026-10-31 at `8e05a94a` on `q1-exact-shortcuts`, source only; runs ci 41268051883 and app 41268060657 at that commit. The integrated commit differs from `8e05a94a` only in this record.

## What changed

`Shortcuts` acts on a chord only when its modifiers are exactly the command modifier, ⌘ on macOS and Ctrl elsewhere. It took any chord that held that modifier, so on Windows AltGr, which arrives as Ctrl+Alt, fired them: AltGr+L ("ł" on a Polish layout) locked the vault and dropped an edit, AltGr+O ("ó") opened the file picker while the master password was typed, and AltGr+2 and AltGr+3 ("²", "³" on a German layout) changed place. A chord with Shift no longer fires either.

## Evidence

- `ShortcutsTests`, two tests: Ctrl+Alt, or ⌘⌥ on macOS, with L, K, 2 and 3 on the unlocked window and with O on the unlock screen reaches the window unhandled, and nothing locks, searches, moves or opens the picker; the plain chords still move and open it. Both failed against the old check, Ctrl+Alt+O opening the picker.
- On the founder's Mac (macOS 26.6, arm64): `verify.sh test` passed `ShortcutsTests`, `ShellImportTests` and `FourPlacesJourneyTests`, 16 tests, and `verify.sh compile` passed.
- CI at `8e05a94a`: ci 41268051883 passed every job, and app 41268060657 passed every job but `keepassxc first run (macos-15)`, which failed as F.74 does: KeePassXC exited with `Trace/BPT trap: 5` after Qt's font message, before opening the vaults.

## Limits and follow-ups

- Q.2's ⌥⌘F and ⌥⌘H stay macOS's: on Windows and Linux Ctrl+Alt is AltGr, so their counterparts there take no Alt.
