# C.2 — Flag keys left in notes for review

Completed 2026-09-29 on `main` above `b5d8eac`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Verify (V-C.2):** on a vault KeePassXC made, one entry's notes hold `STRIPE_SECRET_KEY=sk_test_1`, `export OPENAI_API_KEY=sk-proj-2`, a GitHub token on a line of its own, a PEM block and a sentence, and another's a sentence only. After unlocking, Settings › Recommendations lists the first entry's three findings as needing review and not the second entry, the Settings row shows the count, no first-level screen shows a banner, and no value appears in the automation tree or a drawn frame. Moving them leaves protected fields holding exactly those values in one revision, notes holding only the PEM block and the sentence, and a revision holding the old notes, which real KeePassXC reads. A dismissal survives a lock and unlock, editing the notes between the check and the move refuses the move, and a search for `sk_test_1` still finds nothing. A parser tested only on strings does not pass.

The founder selected C.2 together with N.1a and N.7 on 2026-09-29 and approved a plan that settled what the row left open:

- A value is either unquoted with no whitespace, or exactly one `'…'`, `"…"` or `` `…` `` string taken literally. Anything else, such as an unbalanced quote or text after the value that is not a `#` comment, is not reported, because the move must never write a guessed value.
- A token counts only as a whole line, since only whole lines leave the notes; a `KEY=value` line whose value is also a token is one finding, named by its key. A token written to a field takes the variable its service documents, such as `GITHUB_TOKEN`.
- A selection is moved all or nothing: one refused finding refuses every entry in it. A field already holding the same value keeps its flag and its line still leaves.
- A dismissed finding stays listed as Dismissed, uncounted, with Review again, so a mistaken dismissal can be undone.
- Dismissals are kept by vault identity, entry UUID and key, so no title reaches `~/.keypaste`.

## What changed

Found and fixed on the way: after a change of master password, keyfile or YubiKey in Settings, the app replaces its open vault, and the title bar's "saved" status stopped following later saves because the shell still listened to the vault it had replaced. The session now forwards saves from whichever vault is open, and the title bar and Recommendations both follow them.

## Evidence

Local, Windows 10 Pro 19045, KeePassXC 2.7.10, on `main` above `b5d8eac` with the step's changes uncommitted.

**Core.** [NoteKeyCheckTests](../../tests/Keypaste.Core.Tests/NoteKeyCheckTests.cs), always on a vault that was saved and reopened: the V-C.2 notes give exactly `STRIPE_SECRET_KEY`, `OPENAI_API_KEY` and a `GITHUB_TOKEN` token, by line, and the sentence-only entry gives nothing; a rule table of 24 entries in one vault covers indentation and `export\t`, spaces around `=`, the three quotes, unbalanced and trailing quotes, empty values, prose, lowercase, standard and KeePassXC names, a 129-character key, a glued `exportKEY`, a token in a sentence, a short token, AWS in and out of case, Anthropic, Slack, a key holding a token and an unterminated PEM block; a repeated key is marked on both lines; entries in the recycle bin and in `.keypaste/tokens` are not read; a reflection sweep finds no value in any public member or `ToString` of a finding; and `Search("sk_test_1")` is empty. [VaultNoteMoveTests](../../tests/Keypaste.Core.Tests/VaultNoteMoveTests.cs): the fixture's move writes three protected fields with exact values, leaves the PEM block and sentence, adds exactly one revision holding the old notes and raises one edit, across a save and reopen; two entries move as one revision each and one edit naming both; CRLF notes, which exist only before a save because KDBX stores LF, lose exactly their lines; an equal existing field keeps its plain flag; changed notes, a field with another value, a repeated key and a renamed entry each refuse with nothing pending, no edit and no new revision, and changed notes on one entry refuse the other entry too; a finding from another check is rejected. [RecommendationDismissalsTests](../../tests/Keypaste.Core.Tests/RecommendationDismissalsTests.cs) cover the round trip, a missing, malformed and oversized file, and incomplete rows. Two mutations were each caught and reverted: dropping the stamp comparison failed the changed-notes test, and dropping the reserved-group filter failed the recycle-bin and `.keypaste` test.

**App.** [RecommendationsTests](../../tests/Keypaste.App.Tests/ViewModels/RecommendationsTests.cs), through the shell a real unlock builds: three rows needing review on `services/Stripe`, a Settings count of 3 that is not live, and no notice or toast; Select all and Move selected write protected fields, trim the notes, add one revision and save; a dismissal is written with no value or title, survives a lock and a new shell as 2 and Dismissed, and Review again restores 3; notes saved after the check, with the recheck held back, refuse the move with the file's bytes and history unchanged; a field with another value refuses with identical bytes; an unreadable `recommendations.json` still lists every key and is never replaced; a search for `sk_test_1` on Secrets lists nothing; and after an access change a later save rechecks the notes, the regression for the defect above. [DrawnRecommendationsTests](../../tests/Keypaste.App.Tests/Rendering/DrawnRecommendationsTests.cs), in frames Skia drew: Settings draws `STRIPE_SECRET_KEY` in its cell, neither value is drawn anywhere in the key's mono style or the location's sans style, the window's automation surface exposes neither, and the Settings count carries no amber class; no main destination draws "Needs review" or the key. `SecretHygieneTests` gained a key and a token in the unselected sentinel entry's notes, both on the never-anywhere list, and a two-sided sweep of the Recommendations list before and after a lock. `ScreenRenderer`'s demo vault gained a key in `linear`'s notes, and the Settings render was checked against BRAND.

**KeePassXC.** [verify-keepassxc-workflows.sh](../../scripts/verify-keepassxc-workflows.sh)'s seed gained a `review` group with the V-C.2 entries, the GitHub token assembled at run time so no literal token sits in the repository. A new app step on each of the three vaults runs [Keypaste.AppDriver](../../tests/Keypaste.AppDriver/Program.cs)'s new `notes-review`, `notes-dismiss`, `notes-restore` and `notes-move` acts: the review lists exactly the three findings, counts 3 and prints no value; a dismissal is still Dismissed with a count of 2 in the next process, and Review again restores 3; with `GITHUB_TOKEN` set to another value by `keypaste set --field`, the move is refused with exit 1, identical bytes and no backup; after `field rm`, the move succeeds and KeePassXC reads the three values, marks all three protected, reads the notes as exactly the PEM block and the sentence and the other entry's as they were, counts exactly one more revision, finds the old notes in history and still finds every unmodelled marker. The whole gate passed on 2.7.10, run directly on this step's code.

**The command.** `KPXC_CLI=… bash scripts/verify.sh compat` passed every KeePassXC gate once on the final code: compat, write-back, history, recycle bin, run, backup, organize, import, fields, projects, keyfile, XML attach and workflows, on 2.7.10. `./scripts/verify.ps1` then selected workflows, scripts, backend, integration and desktop and passed in about ten minutes: the backend suites ran 3,045 tests, 3,035 passed and 10 skipped; the desktop suite 762, 760 passed and 2 skipped; Consistency 43 of 43.

Hosted CI has not run; `app.yml` runs the workflows gate on Ubuntu and Windows when `main` is pushed.

## Decisions

- D-0372: what the notes check reads, where its results may appear, and how dismissals are kept, narrowing D-0278 for this check only.
- D-0373: `Vault.MoveNoteKeys` joins D-0369's two custom-field writers, one revision per entry and all or nothing.

## Limits and follow-ups

- Not found: YAML-style `KEY: value`, values spanning lines, quoted values with escapes, lowercase keys, and tokens inside a sentence or behind a label. Each would need a guess the move cannot take back.
- The token list is eight services; another service's token is found only as a `KEY=value` line.
- No CLI surface lists or moves findings.
- The check reads every entry's notes on the UI thread after each save, as the sidebar count does; a vault of many thousands of entries with long notes has not been measured.
- A dismissal is keyed by entry UUID, so a finding comes back if KeePassXC's merge gives the entry a new UUID.
- Password health, V.9, adds its findings to the same card.
- Source only; the app step has run only locally on Windows.
