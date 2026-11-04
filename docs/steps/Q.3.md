# Q.3 — Context menus on every row

Completed 2026-11-04 at `d2db7c12` on `q3-context-menus`, source only; ci 41903552193 passed its fast tier at that commit. The integrated commit differs from `d2db7c12` only in this record.

## Amendments

The founder settled three gaps in the row on 2026-11-04: the row's commands go into a new Item menu between Edit and View, acting on what is chosen; a revision row's menu holds its existing Copy and Restore; and, since agent rows cannot be chosen, the menu bar's agent commands act on the agent row the keyboard is in, as macOS sends a menu command to the focused view.

## What changed

- **One table** (`AppCommands.cs`): the Item menu holds Copy username, Copy password, Open URL, Edit fields…, Rename or move…, Duplicate, Share…, Copy this revision's password, Restore this revision, Restore, Delete for good, Rename group…, Move group…, Delete group, Open project, Agent settings… and Disconnect agent, each routed to the screen's existing command and off while it cannot run. Edit's Delete and File's New group stay where they were and join the item's and group's context menus. `ContextMenu(row)` chooses an item, Trash row, revision or sidebar group before returning its entries; a project, which choosing would open, and an agent, which cannot be chosen, are made the target of their entries while they run.
- **One path** (`ShellView`): the context-request event, which Avalonia raises for a right-click and the menu key, opens a `MenuFlyout` of the row's entries (`AppMenu.Context`), at the pointer or at the row. Shift+F10 raises the same request, as Avalonia does not on every platform, and so does a Control-click on macOS. A choice that opens a group form or Rename or move hands the keyboard to the form once the menu has closed.
- The sidebar group menu's own panel, its state in `ShellViewModel` and its key and pointer handlers are gone; its labels are now Rename group…, Move group… and Delete group, as the Item menu needs them unambiguous.
- An agent row's card has a transparent background, so a right-click on its empty part reaches the row.

Choices settled in the step, each reversible:
- Context menus draw no icons and no danger colour, as the menu bar and the menu button's flyout do not.
- The new menu items end in an ellipsis where they open a form (DESIGN § Interaction); the ⋯ menu's Edit fields and Rename or move keep theirs without one until Q.17.
- A row's menu attaches to the row through `FlyoutBase.SetAttachedFlyout`, not `ContextFlyout`, which would open the last menu again by itself.

## Evidence

- `ContextMenuTests` (7): an item's, a Trash row's, a revision's, a sidebar group's, a project's and an agent's menu each open from a right-click and from Shift+F10 or the menu key, list their commands and run one (Duplicate, Restore, Copy this revision's password, Rename group… and New group with the keyboard in the form, Open project, Agent settings…); Open project and Agent settings… are off once done; the menu bar's Disconnect agent is on only while the keyboard is in that agent's row; a Control-click opens an item's menu with macOS's commands.
- On the founder's Mac (macOS 26.6, arm64): `verify.sh test` passed `ContextMenuTests`, `AppCommandsTests`, `AppMenuTests`, `ShortcutsTests`, `SidebarTreeKeyboardTests`, `EntryMenuKeyboardTests`, `ShellViewModelTests`, `AgentsFirstLevelTests` and `Keypaste.Consistency.Tests`, and `verify.sh compile` passed.

## Limits and follow-ups

- A Control-click on macOS is tested headlessly only: whether AppKit already delivers it to Avalonia as a right-click, which this step's handler would leave alone, is unchecked on the Mac.
- Shift+F10 is taken throughout the shell and raised as a context request on the focused control, so in a text field it opens that field's own menu.
