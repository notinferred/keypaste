# F.88 — A test for each way a reveal of a code, a generated password or a minted token ends

Completed 2026-11-09 on `backlog-2026-11`, source only; ci and app have not run on it, so no run IDs are recorded.

## Amendments

None. The founder selected the BACKLOG row on 2026-11-09: a test for each way out on each of the three surfaces, and a drawn frame for the generator.

## Evidence

`DrawnRevealWaysOutTests` (33) reveals through the real toggle on `RenderedShell` and reads each result from the frame Skia drew: a TOTP code through hide, the 30 seconds, window focus, minimize, the window hidden, lock, another item, a search, a group, another place, the edit form, a saved change, another value and a menu; a generated password through hide, the 30 seconds, window focus, minimize, the window hidden, lock, Cancel and Escape; a minted token through hide, the 30 seconds, window focus, minimize, the window hidden, lock, Done, another place and a menu. Each row asserts the value is off the frame, out of the cell's `Rendered` and styled properties and out of everything `AutomationSurface` sweeps; the generator's and token's dismissals also assert the view model forgot it (`Reveal` null, `IsZeroed`). Two facts cover a value changing while shown: a TOTP period ending and Regenerate each draw the new value and take the old off the frame. `RevealMergeTests` gained a poll-driven merge for all three.

Not applicable: another value on the generator, a modal dialog with one cell whose backdrop leaves nothing else to reveal; another value on a token, alone on its screen, and a value changing on one, since `MintedToken` never changes; another place on the generator, whose modal backdrop keeps the sidebar and the in-window menus out of reach.

`ScreenRenderer` draws `13b-secrets-generate-revealed` next to the masked `13b`, walked by the reach and amber checks, and viewed in dark.

Each failed when broken, restored after: no timeout in `RevealedValue` (time on all three), `EntryTotpRow` not raising `Changed` (the next code not drawn), `GeneratePasswordViewModel` not raising `Changed` (the regenerated value not drawn), `SetMinted` not forgetting (token done, another place and lock), `ShellView` not subscribing `VaultMoved` (the generated merge row and four existing merge tests). The merge rows for a code and a token still pass with that subscription cut, because the merge rebuilds those screens and a replaced row or screen ends the reveal too.

On the founder's Mac, `verify.sh test` over `DrawnRevealWaysOutTests`, `RevealMergeTests` and `ScreenRenderer` ran 75: 52 passed, 23 skipped (the renderer's frames without a folder), none failed; `verify.sh compile` passed.

## Limits and follow-ups

The macOS native menu's hide on the generator is not tested: Avalonia keeps `INativeMenuExporterEventsImplBridge.RaiseOpening` internal, so a headless test cannot open it. At the window's default width a 56-character token is cut with an ellipsis, so the drawn check reads its first 40 characters. By reading `ShowMerged`, a merge with Tokens open and no draft rebuilds the screen, which disposes the view model and forgets a minted token before it was copied; no test holds that behaviour and it is unrelated to a value left on screen.
