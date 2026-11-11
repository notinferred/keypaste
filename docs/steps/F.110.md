# F.110 — The app's layout, as the founder picked it

Completed 2026-11-11 in the merge of `f110-picks`, source only; branch runs app 43005335003 and 43008791236 (red, macOS), dev 43014881861 (macOS green with the fix); main's runs follow the merge.

## Amendments

A first rework merged on 2026-11-10 removed features and was reverted the same day. The founder then picked, screen by screen, what returns, as F.110's row recorded: the list-and-detail layout and its shell without the wordmark, Recommendations, the project's tabs, Agents, Trash, Settings and the one-field unlock as built; Activity under Agents with sortable, resizable columns; two places to add, the sidebar for groups, projects, import and sharing and the item list for an item by kind; and username suggestions. Changing an item's kind stays open in F.110's row, to be chosen from visuals.

## Evidence

Built in stages with a review after each; the review findings were fixed, including a generator that could change a field without Apply. On macOS CI a masked value scored under the match threshold for 75 ms after a click, because the Fluent theme scales a pressed button to 98%; press-to-copy no longer scales, shown by dev run 43014881861.

## Limits and follow-ups

The design of New item, the generator, the add buttons and the suggestions is not yet cohesive with the rest, by the founder's review of 2026-11-11; the options sketched that day are for a later choice. Changing an item's kind is open.
