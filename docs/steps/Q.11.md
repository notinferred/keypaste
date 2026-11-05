# Q.11 — Open on a first run anyone understands

Completed 2026-11-05 on `q11-first-run`; ci 42054926381 passed its fast tier at the code integrated, which differs from it only in this record and in H-0027's row in STEPS.

## Amendments

The founder set boundaries on selecting the row: H-0027 stays open, no legal wording changes, and every sentence about the terms is listed for review before this merges; this record lists them under Evidence. The notice and the terms are word for word what the first run showed. The edits to `terms-of-use.txt` are its `//` note, which no reader sees and the parser drops.

Settings is not in the row. Its switch could have turned reports on for someone who passed the choice unanswered, under terms they might not have looked at, so until the person has answered it shows the same choice instead. Share… records its own date, which the row does not name, so that Share… asks once per version of the terms rather than on every link.

2026-11-05, by the founder, after seeing the frames, because the card was the largest thing on the first screen and its buttons fell below the fold:

1. The card is short: a heading, a statement of what a report holds and never holds, the two equal non-amber buttons with no default, in view without scrolling at the default 1000x680 window at 100%, and a Terms of use link. The row's "appear where their two services start" became this link at the founder's direction: it opens the bundled terms in a small window inside the app, offline, which Escape closes, returning focus to the link. The scrolling box left the card, nothing is agreed at install, and keypaste.com publishes no terms page.
2. Share…'s first step shows the same link in place of the box, with Cancel and Continue, and nothing can upload before Continue. Settings' unanswered choice is the same short form.
3. Asked once: the card appears only at the first start with no answer, and stays gone from the next start, answered or not, with reports off and Settings holding the choice.
4. `keypaste share` stays as it is, with a BACKLOG row to settle with H-0027 before 1.0.0.
5. DESIGN's clause "no screen asks a new person to know a flag" is cut.

A new date is not a new first ask. The founder allowed one only if it stays truthful under the terms' "when you first opened it, or turned it on later in Settings" and PRODUCT §3.5's "at first run or in Settings", and it cannot: someone who has used keypaste for months and answers a card on the unlock screen did neither. So under a new date reports stop, the choice waits in Settings, and the unlock screen does not ask. The cost is that someone who chose Send stops sending without being told. Re-asking would need those words changed when the terms next change, which is the founder's with H-0027.

## What `app.toml` means now

`terms` is the date of the terms in force when the person last made the crash-report choice, on the unlock screen or in Settings, and `crash_reports` is that answer; reports go only while it says 1 under the date this build carries, as before. Both stay in `[[settings]]`, where every older file and reader has them.

Remembering the asking takes a new key, and `[[settings]]` had reached the reader's eight with `share_terms`, leaving a later version no room: a ninth key makes the whole file unreadable to the reader that meets it. So a new section `[[terms_linked]]` holds where the app has linked the terms, which says nothing of whether anyone opened them: `crash_choice`, the date in force when the unlock screen asked, and `share`, the date Share… last linked, which moved there from `share_terms` before any build carrying it was released. `AppSettings.TermsShown`, whose name claimed the same, became `CrashChoiceTerms`. `[[settings]]` is back to seven keys with one to spare. `[[window]]` holds the window's layout and is rewritten whenever that changes, so it is the wrong home for something about consent, and a section of its own survives an older reader as `[[window]]` does. A `share_terms` left in a file by a build of this branch is kept as an unknown key, and Share… asks once more.

| An existing `app.toml` | This build |
|---|---|
| `crash_reports` = 1 or 0, `terms` = the carried date | Not asked, no write; sends or does not, as chosen |
| `crash_reports` = 1 or 0, an older `terms` | Off, not asked, no write; waiting reports kept after a 1, deleted after a 0, as before; Settings shows the choice |
| No `terms`, no `crash_choice` (absent file included) | Off; asked once, and `crash_choice` is written when the window first opens |
| `crash_choice` and no answer | Off, not asked; Settings shows the choice |
| Unreadable | Off, not asked. What the app records on its own, the asking and Share…'s Continue, leaves the file as it is and lasts for the run. Settings shows the choice, and an answer there or any other Settings change replaces the file with a fresh one, as Settings always has, so an answer persists; a change other than an answer leaves a file with no answer and no asking, so the next start asks |

The asking is recorded when a window opens on the card, so a start at login that opens none has not asked. `scripts/exercise-desktop-upgrade.sh` compares `app.toml` byte for byte, so its lower build's start answers Don't send on every platform, Windows newly so, before the baseline, and an answered file is not written again. If `app.toml` cannot be written, the next start asks again. A lock in the same run does not ask again once someone has unlocked past the card, as before.

The founder approved the placement and wording for merge on 2026-11-05, with the terms still a draft, and directed that the sentences this makes questionable go to H-0027.

## Evidence

The founder's 2026-11-05 changes, with the review's repairs: `tests/Keypaste.App.Tests` passed locally, 2133 tests, as did `AppSettingsTests`, 66; the desktop profile passed on Linux in dev run 42055868294, 2135 and 39 tests with none failed, and desktop-gates, whose `verify-crash-report.sh` leaves the card unanswered and starts again, in dev run 42057425715. `exercise-desktop-upgrade.sh --selftest` passed, 110 cases; the exercise itself, including its new Windows answer before the baseline, was not run.

Before those changes, classes run locally, all passing at that code: `TermsWhereServicesStartTests`, `CrashJourneyTests`, `FirstRunTests`, `LockScreenTests`, `LockScreenTreeTests`, `CrashReportingTests`, `CrashReportScreensTests`, `PlainWordsTests`, which now retires the replaced words on every screen, `ShellViewModelTests`, `KeepUnsavedWorkTests`, `FourPlacesJourneyTests`, `DrawnMaskTests`, `ScreenRenderer`, `AppSettingsTests` and the CLI and Core classes whose sentences changed. The desktop profile passed on Linux in dev run 42042184103 at `efac1951`, 2122 and 39 tests with none failed, and the one commit after it changes only a gate's expected word. desktop-gates passed on Linux in dev run 42043481354 at `bfa25579`. Run 42042193115 failed twice before that: GitHub answered 504 for libsodium's download, not this change, and on its rerun `verify-connect-client.sh` still expected the old "Removed.", which `bfa25579` corrects.

`verify-crash-report.sh`, `exercise-desktop-acts.sh`, `exercise-desktop-install.sh`, `exercise-desktop-upgrade.sh` and `AppDriver` walk the new first run. `verify-keepassxc-keyfile.sh` and `verify-keepassxc-recyclebin.sh` read the CLI's new words.

Sentences about the terms for the founder's review, new or moved:

- `Views/CrashChoiceView.axaml`, the card and Settings: "A report holds the version, system, screen and where the code failed, never your vault, a path or who you are." and the link "Terms of use".
- `Views/ShareDialogView.axaml`, unchanged, moved from the card and the box: "keypaste keeps your vault on this computer and never sends it anywhere. keypaste.com runs two services, share links and crash reports, which these terms cover. Running keypaste needs no agreement: its licence already lets you." and the link "Terms of use".
- `Views/TermsWindow.axaml`: `terms-of-use.txt` whole and unchanged, with the window's title "Terms of use" and "Close".
- `SECURITY.md`: "In source, the app asks once, on its unlock screen at the first start with no answer, with a link to the terms of use below the two answers, Settings holds the same choice from then on, and it sends nothing until you choose to; a report holds the version, ..."
- `THREATS.md`: "under the date of the terms of use in force when they answered, on the unlock screen, which asks once, or in Settings, whose choice is two equal buttons with no default and a link that opens those terms"
- `docs/desktop.md`: "a Terms of use link, which opens the terms the app carries in a small window that Escape closes", "It asks once, at the first start with no answer, whether or not you answer. Leaving it unanswered sends nothing and no later start asks; Settings then shows the same choice in place of its switch until you answer. Terms with a new date stop reports until you choose Send again in Settings, and the unlock screen does not ask.", "The first time you use an item's Share…, the dialog shows a note on what the terms cover and the same link, with Cancel and Continue, before its form." and, for `app.toml`, "the date of the terms of use in force when you chose (`terms`), and under `[[terms_linked]]` the date in force when the unlock screen asked (`crash_choice`) and the date Share… last linked (`share`)".
- `CHANGELOG.md`, an earlier entry corrected: "The desktop app now asks at its first start whether to send crash reports, with Send crash reports and Don't send and a link to keypaste's terms of use, a draft until they are approved."
- `ROADMAP.md`: "The app links its terms of use where it asks about crash reports, at first run and in Settings, and, once the person chooses Send crash reports in either, reports its crashes to keypaste.com with nothing from the vault in them"
- `CHANGELOG.md`: "The crash-report question moved there as a short card, with Send crash reports and Don't send as two equal buttons and no default and a Terms of use link that opens the terms in a small window. It is asked once, and Settings holds it after that; nothing is sent until you choose Send, and a choice you already made stands. An item's Share… shows the same link the first time, before its form."
- `docs/ARCHITECTURE.md`: "the unlock screen asks the crash-report choice once, at the first start whose `app.toml` records no answer and no asking, which the window's opening records, Settings shows the same choice until it is answered, both and Share…'s first step link the terms of use (`App/Terms/`) in `Views/TermsWindow.axaml`"
- `docs/BACKLOG.md`: "`keypaste share` uploads with no terms of use shown or linked, as it did before Q.11, whose link sits on the desktop's Share… and crash-report choice. Settle with H-0027 before 1.0.0 whether the CLI shows or points to them."
- `app.toml`'s own comments: "the terms of use in force when that was chosen", "the terms of use in force when the unlock screen asked about crash reports", "the terms of use Share… last linked".
- The `//` note in `terms-of-use.txt`, which no reader sees: "a new date stops crash reports until someone chooses Send under it, and shows Share…'s step again."

## Limits and follow-ups

H-0027 stays open: the terms keep their draft wording and bracketed items. Three of their sentences need settling with it, none reworded here. "If a change affects crash reports, the app shows it to you before sending anything under the new terms" sleeps until a new date, and then nothing is sent until Send, but a link beside Send does not by itself show the change. "We post changes at keypaste.com/terms" names a page that does not exist. "when you first opened it" is true of a new install's card, and not literally of someone upgrading from a build that never asked, such as v0.3.0, who sees the card once at the first start of this one.

`keypaste share` uploads with no terms shown, as it did before; BACKLOG holds it. The two answers are in view at 1000x680 at 100% on the welcome, with any number of KeePassXC databases, whose list holds three rows while the card is up, and on a locked vault with any number of recent vaults, which now sit below the card, and under Windows Hello, More options and a lock note, a state only an upgrade reaches, drawn with the recents as `00f` and `00g`; zoomed, the card is reached by scrolling, and nothing sends meanwhile. The card's statement summarizes: the terms also list the .NET version, the uptime and the date, and what a report never holds at more length. In `00g`, with recent vaults below the card, the frame opens scrolled and the top of the logo is clipped, while the password and the answers stay in view; the likely cause is the list keeping its selected row in view, which is not diagnosed and is left as it is. `docs/policy.md` and `docs/desktop.md` keep "ceilings" in their prose, and README and FEATURES keep "keyfile" and "recycle bin", because guides change only to correct what a step made false; L.1 rewrites them. No person or real screen reader has used this, the terms window has run only on the headless platform, so its modal behaviour on Windows, macOS and Linux desktops is unobserved, and `compat`, the KeePassXC gates and the packaged install and upgrade exercises were not run.
