# Q.19 — Fix every confirmed finding of the whole-app design audit

The founder directed this work on 2026-11-06: fix every confirmed finding of the whole-app design audit, going with what the audit recommends, with the code changing to match DESIGN's numbers. There is no STEPS row. It runs in five stages, and each adds its section here.

## Stage 1: the one terms screen, and behaviour and truth findings

The last repairs ran ci 42308702299 and desktop on Linux dev 42309694316 at `1f5e876f`, which differs from what was integrated only in records; desktop gates ran at the commit before it, whose only later change is one test's lookup (dev 42307267908), and the failure before that (dev 42307270911) was that test's.

In source on `q19-1-terms-and-behaviour` at `c49773d4`; ci 42296184294 (fast tier), ci 42299832656, app 42299845468. Implemented only, in RELEASE's terms.

### Amendments

The founder decided on 2026-11-06, replacing part of [Q.11](Q.11.md): terms are agreed once, on one first-launch screen, with no checkbox and no later banner or card. Continuing agrees to the terms of use, which includes sending crash reports, with a Terms of use link opening the bundled text and one action, Continue. After it there is no card on the unlock screen, no terms step in Share…, and reports send, subject to the crash route being on. Settings keeps its switch, shown plainly. This replaces A47's stack, and PRODUCT §3.5 is re-ratified as v1.22 by that direction.

| `app.toml` | This build |
|---|---|
| `crash_reports = 1` under the carried date | No screen; sends |
| `crash_reports = 0` or another number, under any date | No screen; off; what waits is deleted |
| `crash_reports = 1` under another date or none | The screen, off until Continue; what waits stays for it |
| No answer, absent file included | The screen, off until Continue, which writes `crash_reports = 1` and `terms` |
| Unreadable | No screen, off, no rewrite. Settings' switch asks as below, and its Continue holds for the run without touching the file. Any other Settings change replaces the file, as it always has, and starts nothing |

Settings' switch never agrees to anything. Turned on under terms the person has not agreed to, which reaches a kept `crash_reports = 0` under another date or none and an unreadable file, it opens the same words in a window over Settings, whose Cancel or Escape leaves it off and whose Continue records the agreement. Leaving without Continue writes nothing, so the next start shows the screen. A `--background` start opens no window and sends nothing until the screen is agreed to, because `IsOn` is off under no agreement and the window, once opened, holds the screen. While the screen is up the menus' Open and New are off, and a file handed to the app waits behind it on the unlock screen. `[[terms_linked]]` is no longer read or written; a file holding one keeps it as any section this version does not read.

Judgment calls: a new date shows the one screen once more, as the terms promise, to someone whose reports are on; someone who turned them off is not shown it, since its sentence about sending would be false for them and nothing is sent to them under the new terms. The terms' date moved to 2026-11-06 because their words changed, so a kept Send under 2026-10-28 sees the screen once. The screen keeps Q.11's approved sentence on what a report holds. `Agree` takes an earlier run's marker as a start does, so a kept Send's killed run reports under the terms just agreed, and leaving without Continue leaves the marker for the Continue that comes. Settings' window has a Cancel, which the first-launch screen does not, because a person who flipped a switch must be able to change their mind without quitting.

Sentences of the terms changed:

- "Draft version 2026-10-28" became "Draft version 2026-11-06".
- "You don't have to accept these terms to run, study, change or share it, and nothing here limits the rights the licence gives you." became "The licence lets you run, study, change and share it, and nothing here limits the rights the licence gives you."
- "It does this only if you chose Send crash reports when you first opened it, or turned it on later in Settings." became "It does this from when you continue past the first screen the app shows, which agrees to these terms, until you turn it off in Settings, and again if you turn it back on."
- The `//` note no reader sees now describes this screen.

"Running keypaste needs no agreement: its licence already lets you." went with Share…'s step, the only place it showed. Prompt wording settled by the founder's "go with recommended": the access-change Allow says "Allow until you change it", pairing's explainer ends "It lasts until you remove it in Settings.", and a person's own change gives "You chose this." as its reason, since the line above it says where.

### Findings closed

| ID | Checked against source | What changed |
|---|---|---|
| A05 | Confirmed, for both | A live link's Revoke asks in its row, Cancel first, then Revoke link drawn as danger; Escape cancels, Return does nothing. A waiting crash report's Delete asks the same way. Removing a link that no longer opens stays at once, since it only forgets. |
| A20 | Confirmed | CHANGELOG's Q.14 paragraph says in source, no person has seen one, macOS needs a signed build, and names the Focus limit. |
| A22 | Confirmed | Each fact once: the reason line no longer repeats where the change came from; the length is on the button that saves it (A36). |
| A24 | Confirmed | `docs/desktop.md` corrected at each cited line: End, Reveal, File › New group…, no `--client-label` row, key file, Open a vault…, Delete's Trash and Undo, Disconnect, checklist 1 and checklist 9. |
| A27 | Confirmed | The broken verdict says to treat later rows as unproven and to use `keypaste log verify --expect`; the banner hides while the verdict is open, since each says the file was edited. `--expect` is described as what it shows, whether that record is still in the file. |
| A31 | Confirmed | Trash's two confirmations and the `.env` Delete offer take the keyboard and answer Escape. |
| A36 | Confirmed | See A22, and pairing's explainer. |
| A39 | Confirmed | A directory or command differing from what is saved is a draft, which leaving, the sidebar's projects or the title's project dropdown asks about; Cancel puts the dropdown back. |
| A47 | Replaced | The card is gone; the founder's screen stands in its place, and the frame the finding cited now shows the logo whole. |
| A53 | Confirmed | CHANGELOG and `docs/desktop.md` say in source, with no real drag or Open With run. |
| A60 | Confirmed | Both no-Trash deletes say Delete for good. A group that still holds something, which keypaste refuses to delete there, offers only Cancel under its explanation. |

### Evidence

Local, on the macOS machine, all passing: `tests/Keypaste.App.Tests`, 2376 tests, 10 skipped; `AppSettingsTests`; the selftests of `exercise-desktop-upgrade.sh` (113 cases), `exercise-desktop-acts.sh` (62) and `exercise-desktop-install.sh` (101).

On the branch, at that commit, all green: ci 42296184294's fast tier; ci 42299832656 on every runner, `test` at 5778 and 4499 on Windows and 5776 and 4497 on Linux and macOS with none failed, and every `compat`; app 42299845468, with desktop tests at 2383 and 39 and desktop gates on all three runners, and KeePassXC's first run and workflows; dev 42298479761 (desktop, Linux) and dev 42297294256 (desktop-gates, Linux). dev 42302090548 installed app 42299845468's packages on all three runners, and the install exercise and every act passed, first-run reading "the first-launch screen showed"; by its own terms it is not evidence.

`aot publish (linux-x64)` in ci 42299832656 reached its 10-minute limit inside its Install KeePassXC step in attempt 1, and `gh run rerun --failed` passed it in 3 m 59 s. This stage changes nothing that step runs.

### Limits and follow-ups

`scripts/verify-crash-report.sh`, the exercises and the AppDriver walk the new screen. `exercise-desktop-upgrade.sh`'s real upgrade ran only as its selftest. No person has used the screen, and the terms window's modal behaviour on real desktops stays unobserved. The route stays off until H-0028, so nothing has been sent to keypaste.com. Agreement by continuing needs the lawyer's EU check under H-0027. Whether a program under AGPL-3.0 may stand a terms screen before its first use, as `§10` forbids further restrictions, went to BACKLOG for the same review. The exercises set `KEYPASTE_CRASH_URL=none`, so a run they kill reaches nothing now that Continue turns reports on.
