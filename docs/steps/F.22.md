# F.22 — Keep the Entries screen whole at the default window size

Completed 2026-09-24 on `main` above F.2b3b, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

Observed in [F.2b3b](F.2b3b.md) on Linux under xfwm4 at `8fde202`: in the main window's default 1000×680, with an entry open, the search box shrank to about 60 px under the Add button, the list column narrowed to about 230 px and the entry's title met the Delete button. Build: at the main window's default and minimum sizes, with an entry open, the Entries toolbar, list and entry pane keep every control whole and apart. A regression renders the Entries screen with an entry selected at both sizes in a headless window and asserts that no two toolbar controls' bounds intersect and the search box keeps its minimum width. Verify: the regression fails at `8fde202` and passes after the repair in `app.yml`, and a frame of the default size shows the search box, Add, Organize, Delete and the entry's title apart.

The founder selected it on 2026-09-24, directing it fixed at once. Amendment: at the 720 px minimum width the group tree (180 px), the entry's pane (320 px) and their dividers leave no room for the list, so no arrangement of the toolbar makes that size whole while the tree stays reachable, and an open entry cannot be closed to bring a hidden tree back. The minimum became 960 px (D-0344), and "minimum size" in the Build and Verify reads as that.

## What changed for users

With an entry open, the Entries screen no longer overlaps. The search box and the Add, Organize and Delete buttons run across the list and the entry's pane rather than the list alone, the entry's pane starts below them, and the two dividers sit between the panes rather than over the list and the pane's fields. The main window can no longer be made narrower than 960 px.

## Evidence

**Mechanism.** The toolbar sat in the list's star column. With the tree at 180 px and the pane at 320 px, that column was about 180 px wide in the default window, narrower than Add, Organize and Delete, so the search box's star column got nothing and the buttons overflowed into the pane. Separately, both divider columns were fixed at 1 px while each divider asked for 12 px of margin on either side, so the panes met and each line was drawn inside the next pane.

**Repair.** In `EntriesView.axaml` the toolbar spans the list, the divider and the pane; the pane and its divider start in the row below; the divider columns are `Auto`; and the list's column has a 100 px minimum. `MainWindow.axaml`'s `MinWidth` is 960.

**Regression.** [EntriesViewLayoutTests](../../tests/Keypaste.App.Tests/Views/EntriesViewLayoutTests.cs) opens the `github` entry in `RenderedShell` at 1000×680 and 960×520, each browsing and comparing a revision, draws a frame and reads bounds from the window's layout. It asserts that the window is the requested width, the search box is at least 160 px and the list at least 100 px, that the search box, Add, Organize, Delete and the entry's title lie inside the window and apart, and that the tree, the list, the pane and both dividers are apart.

| Run | Source | Result |
|---|---|---|
| Local, Windows 10 Pro 22H2 19045.6332 | `8fde202` with only the controls named | failed at 1000×680: "the search box is 64 px wide" |
| Local, Windows 10 | the toolbar spanning, the dividers still 1 px | failed at both sizes: "the list … overlaps the tree's divider" |
| Local, Windows 10 | a 140 px list minimum, the 560 px comparison pane | failed at 960×520 comparing: Delete left the window |
| Local, Windows 10 | a 140 px list, the comparison pane narrowed to 520 px | passed, 4 of 4; the desktop project 537 of 537 |
| `app.yml` run 36084643134, `ubuntu-24.04`, `bash scripts/verify.sh desktop` | `019f3bf` on `f21`, that state | failed 2 of 537: `DrawnRevealTests` revision, "the cell's dots are not drawn at rest", because the revision's dots no longer fit the narrower pane in Linux's monospace font; and `AgentActivityViewModelTests.The_request_in_front_of_a_person_is_listed_and_counts_down` timed out, see below |
| Local, Windows 10 | the repair: a 100 px list, the 560 px comparison pane | passed, the layout and reveal classes 10 of 10 |
| `app.yml` run 36085114438, `ubuntu-24.04` | `b578a66` on `f21`, the repair | passed: 536 of 537 desktop tests, 1 skipped as before; 41 of 41 consistency tests |

Frames drawn at 1000×680 and 960×520, browsing and comparing, were looked at: the toolbar and panes are apart, and at 960 px a long title in the list is trimmed (`STRIP…`). At 960 px while comparing a revision the list is 107 px wide.

## Decisions

D-0344: the main window is at least 960 px wide, and the Entries screen fits it.

## Limits and follow-ups

- **Where it was checked.** Only headless frames and the Linux observation that found it. The repaired layout was not seen on a native Windows or Linux desktop, at display scaling other than 100 %, or with long titles and deep groups.
- **Narrow lists.** At 960 px the list is 142 px wide with an entry open and 107 px while comparing, so titles longer than a few characters are trimmed.
- **An intermittent failure.** Run 36084643134 also failed `AgentActivityViewModelTests.The_request_in_front_of_a_person_is_listed_and_counts_down`: after its manual clock passed the 45-second window, the request had not ended within 10 seconds (line 43). No view is involved, and it has not failed in the 30 `app.yml` runs before it. [F.23](../STEPS.md) diagnoses it.
- **Other screens.** Only Entries was measured. Raising the minimum only removes widths below 960 px, so no other screen has less room than before.
