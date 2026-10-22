# N.14 — Lay out Items as KeePassXC does

Completed 2026-09-29 on `main` above `a2321ac`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

Selected by the founder on 2026-09-29 and built in the same session, so it had no STEPS row. The founder found Items crowded: four columns (sidebar, a Groups column, the list and the item's pane), each item's details beside the list. They asked for KeePassXC's layout, which was researched from its user guide and source (`DatabaseWidget`, `EntryView`, `EntryPreviewWidget`, `EditEntryWidget`):

- a group tree on the left, with tags and saved searches under it;
- an entry table on the right;
- a preview of the chosen entry below the table;
- an editor that takes over the view.

The founder agreed a mapping onto keypaste, and this is it:

- **Group tree.** The vault's group tree folds under Items in the sidebar, and the Groups column goes.
- **Projects.** They take the place of KeePassXC's tags, under a heading. Plain tags get no list, since a project is a tag and a group is where an item lives.
- **Table.** The list becomes a table.
- **Preview.** The chosen item's preview sits below the table, not beside it.
- **Takeover.** A new item, an edit and a comparison of two revisions take the whole view.

Traces to PRODUCT §§1 and 5.8: a KeePass user understands the main screens without learning keypaste's terms.

## Evidence

Local, Windows 10 Pro 19045, on `main` above `a2321ac`.

- **The tree.** `ShellViewModelTests`:
  - lists the sidebar as Items, Work, env, Projects, billing, Agents;
  - unfolds env to add billing beneath it;
  - chooses billing, which shows its two variables and selects its row;
  - folds env, which moves Items to env;
  - folds Items, which leaves Items, Projects, billing and Agents and shows every item.

  [SidebarTreeKeyboardTests](../../tests/Keypaste.App.Tests/Views/SidebarTreeKeyboardTests.cs) presses Right and Left on the focused env row, and checks that the Projects heading's container is disabled and not focusable. Its first run found that a fold replaced the rows and dropped keyboard focus, so the next Left went nowhere. The sidebar now hands focus back to the chosen row after a fold.
- **The layout.** `EntriesViewLayoutTests` holds D-0344 at 1000×680 and 960×520:
  - the search box is at least 160 px wide;
  - the list is at least 100 px tall and above the preview;
  - the toolbar's controls stay inside the window and apart;
  - while two revisions are compared, the list is not shown.
- **The journey.** `FourPlacesJourneyTests` walks the sidebar's automation tree and chooses Work from the tree rather than a Groups column. Every frame still passes the amber check.
- **Both palettes.** `ScreenRenderer` drew every screen, dark and light, each with at most one amber element. Its component sheet had stopped drawing after N.7 moved the status colours into the per-theme dictionaries: its toast icon looked `KpOk` up with no theme and got an unset value. It now asks for the current palette's. CI skips the renderer without an output folder, which is why N.7's run did not show it.
- **The row.** `SecretsScreenTests` reads each row's kind and group columns, and no value in them.

**The command.** `bash scripts/verify.sh` selected workflows, scripts, integration and desktop, and passed in about nine minutes: the desktop suite ran 810 tests, 807 passed and 3 skipped (the renderer's draws, which need an output folder), and Consistency 43 of 43. The renderer was run by hand with an output folder and passed in both palettes.

## Decisions

- D-0376: Items is laid out as KeePassXC's main window. It supersedes the Groups column and D-0344's list kept beside a comparison.

## Limits and follow-ups

- **Username and URL are table columns**, as KeePassXC shows them, by the founder's decision D-0488; a password, a note or a custom field's value never is (`1b16a9ca`; ci 39785584625, app 39825609666 at `d57aa9b7`).
- **Group order follows the vault's own order**, as KeePassXC shows it (`1b16a9ca`; ci 39785584625, app 39825609666 at `d57aa9b7`).
- **A group row has a menu**, on a right-click or the menu key or Shift+F10: New group, Rename, Move and Delete, each opening Items' own form for that group (`1b16a9ca`; ci 39785584625, app 39825609666 at `d57aa9b7`).
- **Plain tags are not listed in the sidebar.** A tag is a chip on the item and a search term. Projects were read from the legacy `env/` layout until [C.4](C.4.md) read them from tags; since [C.6](C.6.md) only tags make a project (D-0416).
