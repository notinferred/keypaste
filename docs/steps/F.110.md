# F.110 — The app's layout, as the founder picked it

Completed 2026-11-12 in the merge of `f110-finish`; the layout itself merged from `f110-picks` on 2026-11-11, source only.

## Amendments

A first rework merged on 2026-11-10 removed features and was reverted the same day. The founder then picked, screen by screen, what returns, as F.110's row recorded: the list-and-detail layout and its shell without the wordmark, Recommendations, the project's tabs, Agents, Trash, Settings and the one-field unlock as built; Activity under Agents with sortable, resizable columns; two places to add, the sidebar for groups, projects, import and sharing and the item list for an item by kind; and username suggestions.

On 2026-11-11 the founder's design picks made the added parts cohesive: New item opens as a sheet with the item's grouped rows, the generator is one small sheet everywhere, the sidebar's Groups and Projects headings each take a +, Import sits at the sidebar's foot, Share is a 16 px icon in the open item's header, and a username box completes its best match in grey. Menus wait behind the New item and generator sheets, and a form's generator offers only Cancel and Apply. An item's kind is kept as its standard KeePass icon (globe, multiple keys, disk, network server, notepad), set at creation and changed from Item › Change kind with its fields untouched; an item with the default or a custom icon is read from its fields as before.

On 2026-11-12 the founder directed that finishing F.110 include cutting CI to fast, deterministic checks, after the step's last full run failed on a bash gate's own bug and an unexplained Windows hang. The KeePassXC gates became tests in `Keypaste.Core.Tests.KeePassXC`, run on every Linux push; the cross-process gates became `tests/Keypaste.Processes.Tests`; macOS left `main`'s push and each workflow's macOS work became one job; App.Tests runs as three concurrent processes with a hang dump; the culture passes run on Linux only; and the flaky macOS first-run GUI gate was deleted. The six shipped-binary release checks and the four KeePassXC checks of the built CLI stay in bash, in ci's `aot` job and `release.yml`. The working rules now say that records name no run IDs, a flaky check is fixed or deleted, and a step adds tests rather than scripts. `scripts/` went from 108 files and 29,523 lines to 85 files and 20,583.

## Evidence

App.Tests holds the layout, its screen renderer at each reach. On macOS a masked value scored under the match threshold for 75 ms after a click, because the Fluent theme scales a pressed button to 98%; press-to-copy no longer scales. `KeePassXC.FieldsTests` compares the code keypaste gives for every one-time code form with `keepassxc-cli show --totp`.

Moving the gates found a crash on quit: work a window move posted ran after the window closed and threw in `ScreenFromWindow`. `WindowLayoutTests.A_move_whose_posted_work_runs_after_the_window_closed_does_nothing` failed before the fix. `MergeTests` orders a conflict by the stamps KeePassXC and keypaste actually wrote, since keypaste stamps an edit up to a second ahead and not every KeePassXC moves its stamp on a CLI edit. `main`'s first run after the merge failed `SystemNotificationTests.A_notice_raised_before_the_display_is_up_still_reaches_it_when_pressed` on Windows: it waited for a notice off the UI thread by draining the dispatcher, which ran a job another test had queued on the wrong thread; it and its sibling now wait without the dispatcher.

## Limits and follow-ups

`LockedNoticeTests.A_vault_chosen_while_the_app_stays_locked_is_listened_for_in_place_of_the_one_before` failed once on macOS and then hung until the job's limit; it is in BACKLOG's investigation candidates, and App.Tests now writes a hang dump of a process that stalls for 10 minutes. The same day `System.Formats.Cbor` was pinned to 10.0.15 for CVE-2026-118747, which reached the desktop through Yubico's SDK. Hosted macOS runners sometimes give a queued job no runner and cancel it; one macOS job per workflow narrows that, and GitHub's capacity remains outside the project. Generating what `verify-release-matrix.sh` cross-checks is in BACKLOG.
