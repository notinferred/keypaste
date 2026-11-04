# Q.8 — Make focus, edges and targets visible

Completed 2026-11-04 on `q8-focus-edges-targets`; ci 41935408391 passed its fast tier at the code integrated, which differs from it only in this record.

## Amendments

The founder directed on 2026-11-04 that the test holds literally every token, so card and divider hairlines reach 3:1 and disabled text 4.5:1 rather than only control edges; that undoing a delete reselects the item, closing BACKLOG's question about `ScreenRenderer`; and that the system's contrast preference holds whichever of light or dark Settings chooses.

Settled in the step: high contrast keeps each palette's grounds and raises text and lines; its values are in DESIGN. Muted and disabled text are held on the app, panel and card grounds, where hover, selection, toasts and keycaps take the secondary colour. The Fluent theme's own copies of each edge and disabled text moved with the tokens.

## Evidence

`TokenContrastTests` computes 420 cases over every text and edge token and the Fluent copies fields and choices draw, in all four palettes, fails on a brush it neither measures nor names as a ground, and holds that high contrast lowers no pair. It replaces `StatusColourContrastTests`. `ThemeFollowsSystemTests` holds the contrast preference under each choice in Settings, `TargetSizeTests` the 20 by 20 targets, and `UndoTests` the pane after an undone delete.

The cause of the renderer's failure was `Reselect`, which set the selection before `Select` compared it, so the row was highlighted over an empty pane; every other caller of `Reselect` now rebuilds the pane too. `ScreenRenderer` draws all 102 screens in `dark`, `light`, `high-contrast-dark` and `high-contrast-light` and passes its one-amber check in each; the Agents and token screens, which it drew once, are now drawn per palette. A sample of frames from the variants was looked at.

On the founder's Mac (macOS 26.6, arm64): `verify.sh test tests/Keypaste.App.Tests` ran 1915 tests, with 1907 passing, 8 skipped and none failing, and `verify.sh compile` passed.

## Limits and follow-ups

No one has seen Increase Contrast or a Windows contrast theme switch the app: the tests raise the preference through Avalonia's settings notification, and `libAvaloniaNative` and `Avalonia.Win32` were only read to hold the code that reports it. A Windows contrast theme with its own colours, such as Aquatic, gets keypaste's high-contrast palette, not those colours. Linux's preference was not checked.

At these values disabled text sits within a few steps of muted text, so a disabled control is told mainly by its missing hover and its context. Hairlines are visibly heavier on every card, table and divider. The 20 by 20 targets were measured on the sidebar folds, the clear-scope button and the chip-remove style, not on every button. `site/public/brand.css` is unchanged.
