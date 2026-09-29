# N.1a1 — Put the app in four places

Completed 2026-09-29 on `main` above `202b24e`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

N.1a's **Build**: the sidebar becomes Items, with the project rows beneath it, and Agents, with Trash and Settings in the footer, on Ctrl/Cmd+1–4. Activity becomes Agents › History, and the full log moves to Settings › Advanced. Env profiles is reached from the project rows and from the Items "+" menu, which also holds New project and Import .env. Sharing becomes an item's Share… action, with its links listed in Settings › Advanced. Import .kdbx moves to "+", the first run and the macOS File menu. The always-visible MCP server card goes, and the Agents row shows a count and a dot. The title-bar search becomes the only search and shows its scope. Traces to PRODUCT §§1 and 5.8.

N.1a's **Verify (V-N.1a)**: a driver works through the app's launch composition (D-0342). From the four places it reaches an item found by search, a project's variables, the agent history, revoking a grant, the full log and its check, creating and revoking a share link, and a KDBX import. The sidebar's automation tree lists exactly the four places and the project rows. The MCP card and the second search box are absent. `EntriesViewLayoutTests` holds at 960 px, and each captured frame has one amber element.

The founder selected N.1a with C.2 and N.7 on 2026-09-29 and approved a plan that split it, as its estimate ran past PRODUCT §6.3's two weeks: this child builds everything but the last clause, and N.1a2 builds the amber check, with N.1a closing when N.1a2 does. The plan settled what the row left open:

- "The first run" for Import .kdbx is the empty Items screen of a new vault: an import copies into the open vault, so the unlock screen has nothing to import into, and N.2 owns the welcome.
- The project rows stay on the legacy `env/` groups until C.4 moves the rows and the project page to tags together; a tag-only project today would open a page that writes the legacy layout.
- The Agents row's dot means this app is answering agents for the vault, which is what the card's "running" meant; a waiting request already has its own prompt window and raises the count.
- Settings › Advanced holds only the activity log and the share links; the rest of N.1b's list stays where it is.

## What changed for users

The sidebar now reads Items, then each env project's row beneath it, then Agents, with Trash and Settings at the foot, and `Ctrl/Cmd+1` to `4` go to those four places in that order. Every other screen sits under one of them and draws a Back button to it: a project's Env profiles under Items, History under Agents, and the activity log and share links under Settings › Advanced.

Items' "New" button opens a menu of New item, New project, Import .env (into the project in view, or the first one, or a new one when there is none) and Import .kdbx. An empty vault's Items screen offers Import .kdbx beside New item, and on macOS it is also under File. The list's own filter box is gone: the titlebar's search is the only search, reads "Search all items" or "Search in this group", and shows the group as a chip with a button that widens it to every item again. "All entries" reads "All items", and the list's empty notes say items.

An item's ⋯ menu has Share…, which opens the link form as a dialog over Items, with the item fixed; Create and copy link closes it with the usual toast, and Cancel or Escape closes it with nothing made. Settings › Advanced lists the links under Share links with their status and Revoke, and says how many may still open.

Agents has a History button, which shows the activity log held to agents' and tokens' records, with no filter to widen it; Settings › Advanced › Activity log is the whole log with its filters and chain check. The MCP server card and the Import .kdbx row are gone from the sidebar: the Agents row carries the grants-and-waiting count, in the same muted style as every count, and a dot that is green while the app answers agents, and its tooltip says how many clients are attached.

Found and fixed on the way: Settings › Recommendations' "Needs review" chip was drawn in mono, because the chip's style outranked the text's own font; it is now sans.

None of this is in a download: the desktop has no public release.

## Evidence

Local, Windows 10 Pro 19045, on `main` above `202b24e` with the step's changes uncommitted.

**The journey.** [FourPlacesJourneyTests](../../tests/Keypaste.App.Tests/Session/FourPlacesJourneyTests.cs) composes the app with `App.Launch` into Avalonia's own lifetime, as [ClosingTheMainWindowTests](../../tests/Keypaste.App.Tests/Session/ClosingTheMainWindowTests.cs) does, with a file picker and a fake share server passed through two new init-only seams on `App`. It presses controls where the window drew them and types into it:

1. It unlocks through the unlock screen.
2. It finds the sidebar's automation tree naming exactly Items, billing and Agents, with Trash and Settings at the foot; no control named `Search` or `McpCard` exists, and the only search box is the titlebar's.
3. `Ctrl+1` and `Ctrl+K`, then typing, find github; selecting the Work group puts "Work" in the search's chip.
4. The billing row opens its variables, and Back returns to Items.
5. "+" › Import .kdbx opens a file another KeePass client wrote, unlocks it with its password and imports it; the vault then holds its entries.
6. The item's ⋯ › Share… makes a link on the server.
7. A bridge attached to the app's endpoint asks for github's password and the app's prompt approves it for an hour; the test writes the bridge's audit line.
8. `Ctrl+2` shows the Agents count as 1 with a live dot; Revoke empties the grants, and History lists the client.
9. `Ctrl+4` › Activity log's Verify chain shows its verdict, Back, then Share links' Revoke withdraws the link from the server.
10. `Ctrl+3` shows Trash, and `Ctrl+L` locks before the window closes.

Its first run failed at step 9: Share links drew nothing, because the Sharing view's code-behind still wired the removed "What" picker and threw while building. That defect was in this step's change and was fixed before the record.

**Unit and view tests.** [ShellViewModelTests](../../tests/Keypaste.App.Tests/ViewModels/ShellViewModelTests.cs) was rewritten for the four places, digits 1–4 and none beyond, Back from each screen under a place, a project row's selection until Back, "+" New project and Import .env, the search's scope and its clearing, the Agents row's dot and tooltip without an authority, Share… as a dialog that toasts, closes and lists its link under Settings, and Cancel. [AppMenuTests](../../tests/Keypaste.App.Tests/AppMenuTests.cs) builds the macOS File menu, finds Import off with no vault, opens the import dialog from it, and finds it off again while that dialog is open; `App.Launch` attaches it only on macOS, so the macOS leg of CI is where launch itself runs it. `EntriesViewLayoutTests` now measures the titlebar search at 1000×680 and 960×520 and finds no second search box. `ImportDialogKeyboardTests` returns focus to Items' "+" after the dialog closes. `ShellStatusTests`, `ShellImportTests` and `SecretsScreenTests` follow the renamed members. `RenderedShell` opens the share passphrase through Share…, so `DrawnMaskTests` covers the dialog's masked field. A first run of the drawn tests found the sidebar sending Settings back to Items: replacing the project rows made the list re-select, and write back, the Items row. The shell now ignores that write-back and replaces the rows only when the projects changed. `ScreenRenderer` names each screen by its place (`02-agents-history`, `04-settings-share-links`) and draws Share… over Items; its renders were compared with the design at 1280 px.

The desktop suite ran 770 tests, 768 passed and 2 skipped, and `dotnet format` reported nothing, before the record was written. `./scripts/verify.ps1` then selected workflows, scripts, backend, integration and desktop and passed in about ten minutes: the backend suites ran 3,045 tests, 3,035 passed and 10 skipped; the desktop suite 770, 768 passed and 2 skipped; Consistency 43 of 43. The `compat` profile was not run: this step changes no core code and no act the workflows gate's app driver presses.

## Decisions

- D-0374: the four places, the screens under them with Back, Share… from an item, imports from "+", and the one search; it supersedes the seven-row sidebar, its MCP card and Import row.

## Limits and follow-ups

- The amber rule of V-N.1a is N.1a2's; until it lands, a screen may draw more than one amber element (the selected sidebar icon is one).
- The journey ran headless under Skia with a fake share server and a picker that answers at once; the native file dialog and the macOS menu bar were not driven.
- The chain verdict is asserted shown, not clean: the test process shares one audit log with other tests.
- Project rows are 34 px like the places, not the 30 px the old section drew.
- `scripts/exercise-desktop-install.sh` now reaches a project by its sidebar row and Agents by `Ctrl+2`, but its "Add variable" and "Check again" labels match nothing in the app: F.25 in STEPS. The exercise was not run here.
- README's screenshots still show the old sidebar; N.7 renders them again.
