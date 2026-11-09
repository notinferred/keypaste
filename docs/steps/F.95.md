# F.95 — The first-launch screen says what agreeing covers

Completed 2026-11-09 on `backlog-2026-11`, source only; CI has not run on it, so there are no run IDs. Legal wording, and a draft: it awaits the approval of the founder or a lawyer under H-0027, and nothing here shows it is sufficient.

## Amendments

The founder selected BACKLOG's row on the first-launch screen and the licence, deciding that the screen is worded so agreement covers keypaste's online services and crash reports, not use of the software. The flow is unchanged: Continue is still the only way past the screen, and it still turns crash reports on.

The sentence "Continuing agrees to the terms of use, which include sending crash reports. You can turn crash reports off in Settings." became "Continuing agrees to the terms of use for keypaste's online services and to sending crash reports. You can turn crash reports off in Settings. The software itself is under AGPL-3.0, and nothing here limits the rights it gives you." Settings' window shows the same string. `terms-of-use.txt` already says it covers the services and limits no licence right, so it does not change, its date stays 2026-11-06, and nobody who agreed is asked again.

## Evidence

`FirstLaunchTermsTests` holds the whole sentence on the screen, in Settings' window and in what a screen reader hears: 3 of 14 failed before the change and 14 of 14 passed after, with `PlainWordsTests`, 20 in all. `verify.sh compile` passed. `ScreenRenderer.Every_screen_is_drawn_to_a_file` passed on its four palettes; `00-first-launch`, `00-first-launch-720` and `00h-settings-terms-asked` were looked at in dark and hold the sentence whole, on five lines at the screen's 360 width.

## Limits and follow-ups

H-0027's row already asks whether requiring this agreement before the app opens sits with AGPL §10, so BACKLOG's row is gone. Whether this wording answers that, and whether "nothing here limits the rights it gives you" may stand on the screen, is for H-0027.
