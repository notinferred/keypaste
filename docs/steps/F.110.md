# F.110 — The app's layout, as the founder picked it

Completed 2026-11-11 in the merge of `f110-picks`, source only; branch runs app 43005335003 and 43008791236 (red, macOS), dev 43014881861 (macOS green with the fix); main's runs follow the merge.

## Amendments

A first rework merged on 2026-11-10 removed features and was reverted the same day. The founder then picked, screen by screen, what returns, as F.110's row recorded: the list-and-detail layout and its shell without the wordmark, Recommendations, the project's tabs, Agents, Trash, Settings and the one-field unlock as built; Activity under Agents with sortable, resizable columns; two places to add, the sidebar for groups, projects, import and sharing and the item list for an item by kind; and username suggestions. Changing an item's kind stays open in F.110's row, to be chosen from visuals.

## Evidence

Built in stages with a review after each; the review findings were fixed, including a generator that could change a field without Apply. On macOS CI a masked value scored under the match threshold for 75 ms after a click, because the Fluent theme scales a pressed button to 98%; press-to-copy no longer scales, shown by dev run 43014881861.

## Limits and follow-ups

The design of New item, the generator, the add buttons and the suggestions is not yet cohesive with the rest, by the founder's review of 2026-11-11; the options sketched that day are for a later choice. Changing an item's kind is open.

Main's runs: ci 43022862058 green at `d499355b`; app 43025440381 at `540aa47f` green on its second attempt. Its first attempt failed `LockedNoticeTests.A_vault_chosen_while_the_app_stays_locked_is_listened_for_in_place_of_the_one_before` on macOS, which then hung until the job's time limit; that test covers the locked-app notice, which this work did not touch, and the rerun of the failed job alone passed. First occurrence, recorded here. The same push pinned `System.Formats.Cbor` to 10.0.15 for CVE-2026-118747, published overnight against 10.0.12, which reached the desktop through Yubico's SDK and failed every desktop restore.

On 2026-11-11 the founder's design picks made the added parts cohesive: New item opens as a sheet with the item's grouped rows, the generator is one small sheet everywhere, the sidebar's Groups and Projects headings each take a +, Import sits at the sidebar's foot, Share is an icon in the open item's header (the coordinator's choice, which the founder asked for), and a username box completes its best match in grey. Branch runs: ci 43062159910 green; app 43062177790 failed one rule, a heading + whose negative margin the design list did not name, fixed by reusing the sidebar fold's margin; app 43065414064 green on its second attempt, its first having lost a Linux runner's package servers and three macOS jobs that got no runner within 15 minutes.
Main's runs for the design picks: ci 43071522691 green; app 43071522969 green on its second attempt, after two macOS jobs were cancelled at 15 minutes without a runner (now in BACKLOG's investigation candidates).
