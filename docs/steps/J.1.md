# J.1 — Show each vault as a flat list in the desktop app

Completed 2026-11-18, source only.

The desktop app lists each vault's items as one flat list, organized by vaults and tags, as [PRODUCT §2](../PRODUCT.md#2-scope-boundaries) sets. The sidebar's Groups row and group tree, the group forms (new, rename, move, delete), the item's group picker in New item and in Rename, dragging an item onto a group, the Group sort, the pane's and the row's group text and Search all items are gone, with the code that only served them (`GroupNode`, `GroupRow`, `GroupsRow`, `DepthToIndent`, the item drag). A new item is saved at the vault's top level, and Rename keeps an item in the group the file holds it in, so the groups a file already holds are saved untouched. Core, the CLI, search over group paths, what agents see, keypaste's reserved groups and Trash are unchanged; the driver's `relocate` act went with the move it drove, and `item-new` lost its folder.

## Evidence
`EntriesOrganizeTests.An_item_in_a_KeePassXC_group_is_listed_flat_and_keeps_its_group_through_an_edit_and_a_save`, `NewItemTests.A_new_item_goes_to_the_vaults_top_level_and_offers_no_group`, `EntriesViewModelTests.The_list_holds_every_item_whatever_its_group`, `ShellViewModelTests.The_sidebar_lists_no_group_and_All_items_lists_every_groups_items`, `CurrentStateTests` (a renamed or deleted entry is refused to an agent), `GuiOrganizeIsVisibleToTheCliTests` (the CLI reads a renamed entry in its own group), and the other App test classes the change touched.

## Limits and follow-ups
That KeePassXC opens a file the app saved with every group and item where it was is held by the read-back test above, not by KeePassXC itself; the `compat` gate and CI's installed-app exercises were not run here, and `verify-no-icu.sh`'s two `item-new` calls changed with the driver. Security's findings and Trash still name an item's group, which this task did not cover.
