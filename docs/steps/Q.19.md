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

## Stage 2: each fact once, and the main job gets the room

Implemented only, in RELEASE's terms. Answers the founder's "too much to look at"; his decisions of 2026-11-06 settled the choices marked below.

### Findings closed

| ID | Checked against source | What changed |
|---|---|---|
| A01 | Confirmed | Settings says "Recommendations: 11 to review" in one line with Open, gone when nothing was found, and "nothing to review, 3 dismissed" while only dismissed rows remain, since Review again must stay reachable. Their screen sits under Settings with a Back, keeps Move, Open item and Dismiss, says each problem's reason once as its group's heading and labels only a dismissed row. The sidebar's count is unchanged. |
| A02, A37 | Confirmed | A writable custom field keeps Reveal and Copy; replace, generate, rename, protection and remove are its ⋯, which opens the row's context menu, also from a right-click, the menu key or Shift+F10, and each is in the Item menu, acting on the row holding the keyboard. A field KeePassXC owns has no ⋯. |
| A03 | Confirmed | A cell names its item only where it differs from its column's usual item: the one holding most of the column's single-item cells and at least two, a tie going to the first path in ordinal order. A column with no such item names each cell's. The full path is always on the tooltip and accessible name, and a drawn path wraps rather than trims. The other environments that item serves stay visible as "item also in …", since a change reaches them. |
| A04 | Confirmed | The Group column is not drawn while a group is chosen. A sort by it gives way as a dropped column's does, and the pane names a row's subgroup, since a group lists its subgroups' items. |
| A13 | Confirmed | A row's summary hides while its settings are open, the subtitle keeps its first clause, and Mode's help no longer repeats that commands ask. |
| A14, A28 | Confirmed | One line under AI tools says why a listed agent has no Settings, shown only with such a row, and the Connect advice joins it only when a Connect is listed. No row repeats it. |
| A15, A25 | Confirmed | Since you unlocked is gone. Agents' History, in its header, is the one place for the log, which already draws its table with PersonTime; Connect's check points there. |
| A16 | Confirmed | The status line is gone; the error card covers the failing state. |
| A17 | Confirmed | The pane's button says Copy password, hidden with Rotate… on an item with none, since neither applies. Rotate… and the row's Generate… otherwise stay, as the founder decided only the label. |
| A18 | Confirmed | Username, Password and URL rows are not drawn when empty, with no gap left, so Reveal is not offered on an empty password. Edit fields… adds one. A revision with no password offers no Reveal or Copy either. |
| A19 | Confirmed | The icon says the kind and the Kind column is gone: it costs 28 pixels against 88, and reads faster in a list, and it now carries the kind as its tooltip and accessible name. Sorting by kind goes with the column; a kept sort or kept widths for the old columns give way to the table's own. The dot stays only for "in use", beside the time it repeated. |
| A21 | Confirmed | Mode and each row say "Ask, allow for 1 hour", read from `ApprovalLimits.DefaultMaximumTtlSeconds`, and AccessChangePrompt uses the same words, copy only. Mode's help says a timed allow on a whole run of a project, or on a command, lasts 15 minutes, read from `EnvGrantCache.CeilingSeconds`, since those prompts offer that; a project variable asked for alone gets the hour. The confirmation's words follow the approver's `--max-ttl`, the app's being the default. The stored mode `allow-for-a-while` is unchanged. |
| A26 | Part | Process and unlock moved to Settings › Advanced › Diagnostics, as Answering agents. The rest of the finding, 'hash-chained', 'Injected', 'Bundled' and 'the bridge', is words for a later stage and stays in BACKLOG. |
| A46 | Confirmed | Then and Now show only what differs, comparing stored text rather than what drawing cleans and cuts, the rest in one line, "Unchanged: username, URL, notes, custom field names and tags. Custom field values aren't compared here." Comparing them would need Core to read each revision's values, which stays the founder's choice. A revision row shows its title only where it differs, and the sentence restating the time is gone. The password is compared when history is read, leaving a bool on the row. |
| A48 | Confirmed | One sentence pair under the heading holds what the footnote and the hint said; Restore and Delete for good appear with a selection on the title's row, so the note and table stay still. |
| A49 | Confirmed | Patterns sit directly in More, and the inner "Settings for" heading is gone. |
| A52 | Confirmed | The form no longer restates the path. |

A review's repairs on the branch: a row's menu, its Generate, and Add field ask before discarding what another field form holds, and choosing protection hands the keyboard to the rebuilt row's ⋯. A revision with an empty password says "(empty)" in Then and Now. The kind's tooltip sits on a mark the pointer can hit, not the icon's strokes. `verify-agent-activity.sh` checks what `LogViewModel(agentsOnly: true)` draws, and `AuditHistory.Read` lost the filter only the old table used.

### Secret paths

Opening a row's menu, the pane's ⋯, the menu button's flyout or, on macOS, a menu bar menu hides a shown value, recorded under T-25. A row's ⋯ and its menu name the field at most, never a value, and the masked and revealed rules hold.

### Before and after, by frame

| Frame | Before | After |
|---|---|---|
| Items, open item | Seven icons per field, two Copies and a detached third, Kind and Group columns | Three per field, Copy password, no Kind, Group only with no group chosen |
| Items, open `.env` item | Three blank rows above the fields, Group repeating the header | Fields straight under the title, Title and Last used taking the room |
| Settings | Security starts below the fold on a 10-item vault | One line, then Security at once |
| Recommendations | Inside Settings, "Needs review" and the reason on every row | Its own screen, each reason once |
| Env | A path under all 18 filled cells | Two, the ones that differ; rows are shorter |
| Agents, settings open | Process line, summary above its controls, a third disclosure, a raw log | None of them |
| Trash | Paragraph, footnote, hint and two disabled buttons | One paragraph and Empty Trash until a row is chosen |

### Evidence

The review's repairs at `691bf6e6`, all green: ci 42326142913 (fast tier), dev 42326836531 (desktop, Linux), dev 42326825273 (desktop gates, Linux). Before them, at `ba876e6f` on `q19-2-each-fact-once`, all green: ci 42317232101 (fast tier); dev 42317760186 (desktop, Linux, 2365 and 39 tests, none failed in app 42319660431's matching Linux job); dev 42318778468 (desktop gates, Linux, its `verify-agent-activity.sh` reading the log through `AuditHistory` now the screen does not); ci 42319648697; app 42319660431. dev 42323539068 installed that app run's packages on all three runners and every act passed, edit-rerun opening the row's menu and choosing Replace value… by Down and Return; by its own terms it is not evidence. Local, on macOS: `tests/Keypaste.App.Tests`, 2389 passed, 10 skipped, with `AccessChangeTests` and `ClientsVerbTests`, and the acts' selftest, 62 cases.

`desktop tests (macos-15)` in app 42319660431 attempt 1 reached its 24-minute limit: `LockedNoticeTests.A_vault_chosen_while_the_app_stays_locked_is_listened_for_in_place_of_the_one_before` failed after 11 s with "the app never listened for that vault", and the test host printed nothing more until it was cancelled. `gh run rerun --failed` passed it in 10 m 27 s. This stage changes nothing that test reaches; its mechanism is not diagnosed.

### Limits and follow-ups

The row's menu takes the keyboard in the app, and the acts press Down and Return after opening it, because their drivers search only the main window; no person has used it. The macOS menu bar's hide uses `NativeMenu.Opening`, which no headless test can raise. A chosen group hides Group even where it lists subgroups' items, as the founder decided, and only the selected row's pane names the subgroup.

## Stage 3: menus, labels, errors and amber

Implemented only, in RELEASE's terms. Every ID below was checked against the source after stage 2 moved it.

### Findings closed

| ID | Checked against source | What changed |
|---|---|---|
| A06 | Confirmed | `Border.note` sits one ink step up, a problem note on the danger tint with its icon; Env's notice banner takes the same ink. Rotate's question keeps its amber on the button alone. |
| A07, A33 | Confirmed | New item's kind is `ListBox.segmented` under a label, in ink. The control's name stays `NewItemTemplate`. |
| A23, A32 | Confirmed | A label above each master password, each value, each form field and each dropdown, on the unlock, create and restore screens too; "On" beside Env's picker became the label "Item for the new key". `LabelledFields` holds every drawn frame to it, dropdowns included; a title's project picker is its heading, and Settings' Lock the vault after moved its label above. |
| A62 | Confirmed, and wider | `MaskedInput` has no placeholder at all: a secret field's accessible name is a fixed `AutomationProperties.Name`, equal to its label wherever it has one, which a rule test holds. "Optional", "per hour", "until", "end of day" and "Its master password" are gone; "For your list only" and the passphrase's length moved to captions. What remains is an example. |
| A30 | Confirmed | Rotate, Show history, Copy URL, Copy the reference, Copy the TOTP code, Restore as, Add field…, Add attachment…, New project…, Export .env.keypaste…, Run in a terminal, Open terminal, Copy the command, Empty Trash, New token…, Revoke token, End allowance, End all allowances, Agent history, Verify chain, Check status, Clear the clipboard now, Show the prompt, Connect agent…, the settings screens, and This computer's four are in `AppCommands`. File, Edit, Item, Project, Agents, View, Window, Help, with Field, Attachment, Revision, Group, Trash, Share links and Key as submenus. Attachment, key, environment item, token, share link, allowance and crash report rows have context menus, and a project's holds its commands while it is the one open. Each runs the button's own command, so its disarm, confirmation and clipboard rules are the button's. No chord was added, as none of these has the platform's own. |
| A34 | Confirmed | Env's key and environment name, a project's name, a group's name and move, a field's name and rename, New item's title, key name and key value, and a token's name and scope each say what is wrong under their field, as typed, and an empty one once its action is pressed. Each scrolls into view as it appears, and Env's page banner does, for what concerns no field, and a problem line brings the field above it along. The Replace form's refusal and its generator's sit under its value. A problem line inside a data template drew muted, because a resource bound there has a template's priority, below a style's; each is now the `danger` class. |
| A35 | Confirmed | Import's Unlock, Vault access's Changing…, Check this backup and Check the connection spin, the last from its start until it waits on the person to pick an item. |
| A38, A56 | Confirmed | The pane's action reads its `Title`, Connect Cursor or Disconnect Cursor, and the row's buttons that opened it, now ending in an ellipsis, give way while it is open. |
| A41 | Confirmed | DESIGN's Colour says what the once counts and what it does not, and `AmberElements`' exclusions say the same. |
| A42 | Confirmed | The lock field's caret takes `KpAccentText`. |
| A59, A61 | Confirmed, swept | Added the next step to the cited errors and to those a sweep of the view models found stopping at the fault: a wrong import password, an unreadable tool, mismatched passwords, a refused export, a rename or move, an agent setting and a gone Trash item. |
| A63 | Confirmed | DESIGN states the exception: a command that only asks to confirm takes no ellipsis. So Rotate, Remove field, Run in a terminal and Change lost theirs; Add item…, Disconnect agent…, About keypaste…, the row's Connect … and Disconnect… gained theirs. |
| A65 | Confirmed | An import row's box is named Copy and its group, the protection switch Protect this field. |
| A66 | Confirmed | Windows and Linux have Minimize, with no chord, and Maximize or Restore window size; macOS keeps Zoom, ⌘M and Bring all to front. Enter full screen says Exit while it is. The minimize help writes ⌘H. |

### Settled choices

A form opened from a menu takes the keyboard through `ShowInView` once the menu has closed, which replaced a map of headers to control names; a form holding only a dropdown gives it the keyboard, and Return in a closed dropdown confirms a form that can be confirmed and otherwise opens it. An emptied field does not report itself until its action is pressed, since that would flash at anyone retyping a name. Item commands are off behind New item's form, and those on a section an edit hides are off during an edit. Two new top-level menus rather than a longer Item, since a project's and an agent's commands are not an item's. A row's Reveal stays off the menus, since opening a menu hides a shown value (T-25); no menu answers a prompt, and Show the prompt only brings it forward through its own disarm. Disconnect's action stays primary or plain, not danger, since Connect undoes it. Items' search keeps its icon and one word as its label, written into DESIGN. A row's context menu is flat; only the bar has submenus. The pane's ⋯ stays hand-built, since it also shows the reference and the KeePass ID, and now agrees with the Item menu's words. Share's form scrolls under zoom with its actions pinned, since the new label no longer fits at 200%. Patterns' label sits above its field across both columns of Agents' settings. A wrong password stays reported on pressing, not as typed: comparing buffers on each key would say so at the first character.

### Sentences changed in DESIGN

Colour's accent paragraph, Interaction 1 and Interaction 5, as `git log -p` on DESIGN shows, by this task's direction.

### Evidence

The review's repairs at `eb0e46bc`, all green: ci 42342197966 (fast tier), dev 42342588018 (desktop, Linux), dev 42342611944 (desktop gates, Linux); new frames draw the problem note, each wait's spinner and Connect's item picker, and the 150% walk found the picker's row cut off, which now wraps.

Before them, at `e50fc814` on `q19-3-interaction`, all green: ci 42337271759 (fast tier); ci 42337768643 on every runner, `test` at 5776 on Linux and macOS's 4497 with none failed, every `compat`; app 42337775810, with desktop tests at 2422 and 39 on all three runners and none failed, desktop gates on all three, and KeePassXC's first run and workflows; dev 42337766121 (desktop, Linux). Desktop gates on Linux ran at the commit before it, dev 42335132793, whose only later change is one test's lookup. dev 42339241692 installed that app run's packages on all three runners and the install exercise and acts passed, 58 pass and 3 rehearsal; by its own terms it is not evidence. Local, on macOS: `tests/Keypaste.App.Tests`, 2405 passed, 10 skipped; `tests/Keypaste.Consistency.Tests`, 39; the acts' selftest, 62 cases.

dev 42335749470 (desktop, Linux) failed at `d1aa8507` on `The_gui_refuses_every_variable_name_the_cli_refuses`, which read the page's banner where the form now says it; fixed by that one lookup. `test (windows-2025)` in ci 42337768643 attempt 1 failed `LockedAppEndpointTests.BridgesInQuickSuccession_AreEachHeard`, BACKLOG's known missed knock; `gh run rerun --failed` passed it, and this stage changes nothing that test reaches.

### Limits and follow-ups

No person has used the menus, and macOS's native submenus and the Windows and Linux flyout's are drawn only by the headless tests. Commands that stay button-only are in BACKLOG's row. `OneHomeJourneyTests.Every_advanced_feature_is_reached_from_its_one_home` failed once locally at "the settings did not open" during a whole-project run and passed when run with fewer classes; its mechanism is not diagnosed.

## Stage 4: the code matches DESIGN's numbers

The founder decided that the code changes to DESIGN's numbers and DESIGN's numbers do not change; DESIGN is untouched. Implemented only, in RELEASE's terms. Recounted on main at `7f918324` before the change: 278 off-scale numbers in the AXAML's spacing, 10 off-list radii, 109 `mono sm` and 51 `size-12-5` uses, and 31 `section` uses at 13.

### Findings closed

| ID | What changed |
|---|---|
| A08 | Plain words left mono: the save state, 'live', sidebar counts, 'missing', 'expired', 'same as staging', 'value goes to the agent', the generator's strength, agent initials, a chip's words, Agents' time left, the idle-lock sentence, 'also …', a cell's 'item also in …' with its paths staying mono, 'No variables', Items' last used ('never', '7m ago'), an item's agent lines and a YubiKey's label. The Rename form's title is sans, as New item's is. |
| A09 | `MachineRuns`, an attached property, sets machine text in a sentence as mono runs, keeping the whole sentence in `Text`. `InText` finds what has a machine's shape: paths from the root, the home or a drive, spaces included where the path goes on after them (`Application Support/Claude/...`, `C:\Users\Maya Ochoa\...`), flags, `read:`, `env:` and `tag:` words, and `keypaste mcp`, `run` and `log verify`. `Words` names what has no shape: a key, an item's path, a command with arguments. It is on Connect's explanation, a tool's status, the token scope line, the static sentences that name a tag or a command, the replace and rename forms and the notices and errors that can name a path. Recommendations' item paths are mono. |
| A10 | `.section` is 20/600 at −0.03em, and the unused `.h2` is gone. `.title`, the lock screen's title and the open project's title are 24/600 at −0.03em, and Items' list header is `.h1`. The unused `mono.xl` is gone. A project page's 'Items' is a `.section`, the rank of its sibling 'Run with this environment'. The replace and rename forms' heads are sentences holding a key or a path, so they are Body with mono runs, not `.section`. |
| A11 | Mono is 12, 12.5 or 13: `mono.sm`, `mono.lg`, `size-11` and `size-12-5` are gone, keycaps, chips and the prompt's call and countdown rise to 12. Sans is 13 or 13.5 for body, with buttons, fields and captions at 13, and 12 only at weight 500. Helper text and wrapped text at 13 take Body's line height of 1.5. A view sets a size only through a class: all 41 local size setters are gone, by deleting those that changed nothing and naming `size-*` on the element for the rest. |
| A12 | Every icon is 14, 16 or 20: 10 to 13 became 14, 15 became 16, 18 and above became 20, and every sidebar row's icon is 20, its fold chevron staying a 14 glyph in a 20 target. A column head's arrow and the reveal button's icon are 14. The stroke was already 1.5 at every size. |
| A40 | Every Margin, Padding, Spacing and thickness resource is on the scale, by one table of old to new, with the head-to-row, toggle-to-text and 20-target offsets moved together. A tree's indent is 16 a level, so deeper levels are multiples of the 16 grid, 48 at the third. `DesignScaleTests` fails a value off the scale, a radius off the list, a type size off the table or an icon off the three, in the AXAML, its `x:Double` resources and C#'s literals; mono over 13 outside a mask; sans at 12 that is not a label; a size set in a view; and a negative margin its named list of 14 does not give a reason for. `ScrollBarSize`, Fluent's, is the one size exempt. |
| A43 | Radii are 4, 6, 7, 10 and 14. Tiles take 10, segments and in-field buttons 6, Lock and menu items 7, chip removes 4. A dot, a switch's track and the thin progress bar are round at 4, 10 and 4, since a radius over half the side draws the same. |
| A45 | `MaskedInput` draws mono 14 by its theme, and the history's Now mask and a project cell's mask are 14 and mono. |
| A55, A64 | The title bar says 'Lock' in sans, from the menu's label, and the lowercase string is gone. `VaultStatus` became `VaultSaveWords`, and the file name draws in mono beside it in sans, the name still giving way in its middle. A64's other word pairs are in BACKLOG, for stage 5's words. |

### Settled choices

The mapping, where a number had two neighbours: gaps and stacks go down (10 to 8, 14 to 12, 18 to 16, 28 to 24), a button's sides go up (10 to 12), and a card's padding goes up (14 to 16). A list row's `0,1` margin became `0,0,0,2`, which keeps the 2px gap between rows. Sans at 12/400 became 13/400 rather than 12/500, since a heavier helper line is louder. A half size inside DESIGN's range stays: mono 12.5, row titles and sidebar places at 13.5. Time left stays out of mono because it holds a word; a prompt's `0:28` and a time or date stay mono.

### Repairs the frames and the walk asked for

The numbers moved things out of place. KeePass import's Cancel and Import fell off the card at 200% on a small window, so the card scrolls between its head and its answers, its bar in the card's padding, and the keep-in-place choice and any message stay in view with the answers. The project title's picker was cut in the long pseudo-locale at 840, so the title has its own line and the header's actions drop under the subtitle where they do not fit, through `HeaderRow`, at their usual padding. Items' head sat 2 off its rows when row padding became 12, so the list's padding became 4. New outgrew its fixed 68 and cut its label in the long pseudo-locale, so it is a least width at one weight, SemiBold, which keeps the search beside it still as the amber leaves. The title bar's dot sat against 'saved', because Instrument Sans's middle dot advances 1.3 and inks past that; it is drawn.

### For the founder to weigh

DESIGN's numbers are followed as decided, and these are louder or flatter than before. Before frames are `q19-4-base`, rendered at main's `7f918324`, and after frames `q19-4r`, under the session's scratchpad, named in the batch's report.

- Settings and Agents: each card's or list's heading at 20 competes with its contents, where 13 read as a label, and a page of them is a page of headlines (`04-settings`, `95-settings-whole`, `21-agents-full`, `96-recommendations`).
- A form's title inside a pane at 20 sits right under the item's title at 24, two near-equal headings (`19-secrets-organize`, `20-secrets-edit`, `14-secrets-history`, `41-env-add`, `46-env-add-entry`).
- Items: 'All items' and the item's title are both 24, so the list header and the pane no longer rank, and New item's 'New item' stacks under 'All items' at the same size (`01-items`, `13-secrets-new`).
- The sidebar's group and project rows at 20 draw larger than their 13 labels and take a little of a project's name (`01-items`).
- A project page's 'Items' at 20 now ranks with 'Run with this environment' and any open form, adding one more headline to the page (`40-env-staging`).
- Prompts grew. The 13 size and Body's line height take the approval prompt from 574 to 601 tall and the run prompt from 750 to 807, order, Deny and the reason unchanged. A prompt is scaled to fit its screen, so on a 1366 by 768 screen at 200% the run prompt draws at 90% of its design size, where it was 97% (`91-approval`, `91c-run-approval`, `s-run-approval-on-1366x768`).

### Evidence

All green at `cb798d8d` on `q19-4-design-numbers`, before the review's repairs: ci 42347753914 (fast tier); ci 42349447939 on every runner, `test` at 5776 on Linux and macOS and 5778 on Windows, with none failed, and every `compat`; app 42349456134, with desktop tests at 2442 and 39 on all three runners and none failed, desktop gates on all three, and KeePassXC's first run and workflows; dev 42350538138, this app run's packages installed and its acts done on all three; dev 42348145356 (desktop, Linux, 2442 and 39); dev 42348848746 (desktop gates, Linux). `tools` was not run, since nothing here reaches an AI tool. Locally, the renderer's walk at every zoom and pseudo-locale, 70 tests with the classes it touches, and 699 across the views and rendering classes before their last two repairs.

The review's repairs at `9f189044`, which differs from this commit only in this record: ci 42354418065 (fast tier); dev 42354929889 (desktop, Linux, 2459 and 39, none failed, 41 skipped as before); dev 42355658273 (desktop gates, Linux). Windows, macOS and compatibility have not run on the repairs; `main`'s push runs them. Locally, the renderer's walk at every zoom and pseudo-locale, 813 tests across the views, controls, rendering and session classes the repairs touch, and a mutation of each value the check now covers failing it.

### Limits and follow-ups

`MachineRuns` finds paths by their shape, so a path holding a space is mono only up to the space and a relative path such as `Work/linear` inside a sentence stays sans; the sentences come from core and the system, so nothing marks them. A flag can break after its hyphens at 720 (`95a-settings-720`). KeePass import's scroll bar crosses the card's edge when it shows on a small window. No person has seen these sizes on a real screen; the frames are headless.

