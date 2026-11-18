# J.2 — Ask a separate yes for crash reports at first launch

Completed 2026-11-18, source only.

The first-launch screen asks for crash reports with its own Send crash reports checkbox, off until the person checks it, beside Continue, which now agrees to the terms of use alone ([law 5](../../SECURITY.md#security-laws)). Settings' switch still turns reports on or off; turned on under terms not agreed to, its window says that Continue agrees to the terms and turns reports on, the switch being the yes. `app.toml` keeps the answer under `send_crash_reports`. An earlier build wrote `crash_reports = 1` on Continue alone, so that key reads as no answer when it is 1 and as a no when it is 0, a save drops it, and such a home is shown the first-launch screen once more.

## Evidence
`CrashReportingTests` (terms accepted alone keeps and sends nothing to a fake endpoint; an earlier install's `crash_reports = 1` sends nothing and is asked again; a yes sends after thirty seconds; the switch off stops it), `FirstLaunchTermsTests`, `CrashReportScreensTests`, `CrashJourneyTests`, `PlainWordsTests`, `AppSettingsTests` and `Keypaste.Processes.Tests.CrashReportTests`, whose driver's `continue send` checks the box; `exercise-desktop-acts.sh --selftest`, which now expects Continue alone to write `send_crash_reports = 0`.

## Limits and follow-ups
The draft terms of use (`src/Keypaste.App/Terms/terms-of-use.txt`) still say continuing past the first screen starts crash reports; they are legal text awaiting the founder's or a lawyer's approval and were left unchanged. The installed-app upgrade exercise starts a lower release whose Continue wrote `crash_reports = 1`; the upgraded app now shows the first-launch screen again over it, which only CI's installed-app gates exercise.
