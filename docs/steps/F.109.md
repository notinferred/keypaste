# F.109 — Rehearse the desktop upgrade and uninstall for real after Q.11 and Q.13

Completed 2026-11-10 at `43ab6807`, no code changed; upgrade-desktop 42810056543, a rehearsal and never release evidence.

## Amendments

The founder selected this on 2026-11-09 from BACKLOG's row on `exercise-desktop-upgrade.sh`, which had run only as its selftest since [Q.11](Q.11.md) and [Q.13](Q.13.md) changed it. There is no STEPS row.

## Evidence

`upgrade-desktop (win-x64)`, `(linux-x64)` and `(osx-arm64)` all succeeded on unpublished, unsigned candidates: install, fixture, upgrade, the data after the upgrade, an interrupted upgrade and the uninstall passed on each, and a downgrade was refused on Windows, the check being not applicable on the other two. The baseline holds the lower build's answer to its first launch, Q.11's change, and on Windows the upgrade kept `.kdbx`'s Open With entry and the uninstall removed it, as Q.13 asks.

## Limits and follow-ups

A rehearsal establishes nothing for a release. The `.kdbx` check is the exercise's on Windows only. The macOS leg runs again on the first signed bundle, under 3.5b in [STEPS](../STEPS.md).
