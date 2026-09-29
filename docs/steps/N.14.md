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

**Verify, set for this step:**

- The sidebar's rows read Items, the vault's groups as a tree folded under it, a Projects heading over the project rows, then Agents. Folding a group hides its children; choosing one shows its items and selects its row. Folding away the group Items shows moves Items to the folded group. Right and Left fold the chosen row from the keyboard, and the heading cannot be chosen.
- At 960×520 and 1000×680, with an item open, the toolbar, a list at least 100 px tall, the divider and the preview are whole and apart. A comparison of two revisions hides the list.
- Every screen still renders in both palettes with at most one amber element, and the four-places journey reaches every act from the new sidebar.

## What changed for users

**The sidebar holds the vault's groups, as KeePassXC's tree does.**

- Under Items, each group is a row, and a group with groups inside it folds with its chevron, or with Right and Left while it is chosen.
- Items itself folds the whole tree.
- Choosing a group shows its items; folding away the group you are looking at shows the folded group instead.
- The env projects follow under a PROJECTS heading, then Agents; Trash and Settings stay at the foot, and `Ctrl/Cmd+1`–`4` are unchanged.

**Items is a table above a preview.**

- Each row is one line: title, kind, group (a variable's project and profile) and when an agent last received it.
- As before, no value from inside an item is ever in the list.
- The chosen item's preview sits below the table, with username, password, URL and notes one line each, the label to the left, as KeePassXC's preview lists them.
- The line between table and preview can be dragged.

**Writing takes the whole view.** A new item, an edit and a comparison of two revisions fill the area below the header, as KeePassXC's editor does, and the table comes back when they are saved or cancelled.

**Groups are made and renamed in Items.** New group is in Items' "+" menu, and Rename sits beside the group's name while one is chosen.

The README's screenshots show the new layout.

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

These bind only this step's code:

- **The sidebar's rows are rebuilt whole when the tree folds.** The keyboard is handed back to the chosen row after a fold.
- **The groups are listed in ordinal order of their paths, as `GroupNode` already sorted them.** KeePassXC shows the vault's own order.
- **A fold keeps its state only while the vault is unlocked.** Items starts unfolded and every group folded.

## Limits and follow-ups

- **Username and URL are not table columns.** KeePassXC shows a username column by default. keypaste's list has never carried a field value (EntryRow, the hygiene gate), and this step kept to that. They are in the preview.
- **Group order follows ordinal path order, not the vault's own order, which KeePassXC shows.**
- **No context menu on a group row.** New group and Rename are in Items' header.
- **Plain tags are not listed in the sidebar.** A tag is a chip on the item and a search term. Projects are read from the legacy `env/` layout until C.4 reads them from tags.
