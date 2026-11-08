# Design

The system every keypaste surface is designed from, the desktop app, the CLI, the site, the README, the store listings and the installers, written down before a screen is designed ([DESIGN-PRINCIPLES](DESIGN-PRINCIPLES.md) §6). This page owns how a screen is put together, the marks, the palette, the type, the shape, the icons, the terminal rules, the voice, the interaction rules and the usage rules. Where a value also has to exist as code, the file named below holds it, and the app's views hold each screen's exact sizes.

**Brand status:** adopted 2026-09-24. Amended 2026-09-28: the theme follows the system, titles use the sans face and the voice is calmer. Amended 2026-09-29 by the founder: the monogram and its lockup give way to an icon and a wordmark in Hepta Slab with an amber dot, never set together, and the status colours gain light-ground values; later that day the founder chose a lighter cut of both marks. The desktop app's theme, icon, shell and screens, keypaste.com (through [brand.css](../site/public/brand.css)) and the README's wordmark and screenshots are built from it as amended ([N.7](steps/N.7.md)), and the CLI follows the terminal rules below. Amended 2026-11-01 by the founder: the interaction rules below, from the audit against Apple's Human Interface Guidelines (D-0682). Amended 2026-11-07 by the founder, because the numbers made screens louder, against "too much to look at": a card's or an inline form's heading takes the dialog title's size, the list header drops under the item's title, a helper line is no larger than its label, and sidebar rows draw 16px icons ([Q.19](steps/Q.19.md)).

## The desktop's places

The app has four places, Items, Agents, Trash and Settings, on Ctrl/⌘+1–4, and every other screen sits under one of them, its Back going up to it (D-0374). The window has three regions:

| Region | Holds |
|---|---|
| Title bar | While the person has hidden the sidebar the button that shows it; the idle-lock countdown, the vault's file name with its save state, its path the tooltip, and Lock |
| Sidebar | The wordmark; Items with the vault's group tree, the projects and Agents; Trash and Settings at the bottom. Under 840 wide, a rail of the four places' icons |
| Content | The place in view: on Items, the list under a header of what it shows, the search and New, and the selected item's pane |

The window's title names the screen in view, "Items - keypaste", and is "keypaste" alone on the first-launch and unlock screens: only a screen's fixed name, never a vault, group, project or item, since the system's window list, taskbar and switcher show it.

The window goes down to 720 wide, so two sit side by side on a laptop. Under 840 wide the sidebar collapses to a rail 56 wide and Items drops its Username and URL columns, and a sort by a column so dropped gives way to the table's own order until it returns. Wherever the content is under 768 wide, whatever else takes the window, the log drops its Where column to the Secrets cell's tooltip and narrows its page's margins, and an environment's note wraps under its mask. The rail keeps the four places, one list the arrow keys walk, each with its name as tooltip and accessible name and the selected one drawn as in the sidebar. Counts drop, and Agents' dot stays, its word in the tooltip and the accessible name. The group tree and the projects open from the rail's first button, or View › Show groups and projects, as the whole sidebar over the content, which keeps its width, until Escape or a press outside. View › Hide sidebar hides the sidebar, or the rail, at any width; the title bar's button then brings it back, and in a narrow window says Show sidebar in words and names the other ways back in its tooltip. `WindowChrome.cs` holds the sizes.

View › Zoom in, Zoom out and Actual size (⌘+, ⌘− and ⌘0) scale every window's interface from 100% to 200% in steps of 25, by drawing it through a layout transform, so the sizes on this page and the breakpoint above read the design's pixels, and at 200% a window 1500 wide lays out as one 750 wide. The minimum window scales with the zoom but never passes the screen's working area; where the screen cannot hold it, the interface keeps its minimum and scrolls inside the window. A prompt takes the zoom as it opens, scaled as a whole so its layout stays fixed, or at the largest scale that shows all of it on its screen, and keeps that scale while it is open.

## Information

1. **Each fact once in a view.** A name, a state or a location appears in one place in a view, the place the person looks for it; another place links to it or leaves it out. The vault's file name, the group in view and the save state are each such a fact: Items' Group column is gone while the list's header names a group, and the pane names a row's subgroup instead; an Env cell names its item only where that differs from the item holding most of its column's keys, a tie going to the first path in ordinal order, with the full path always on its tooltip and accessible name.
2. **The main job gets the room.** On Items that is finding an item and using it: the list and the pane take the space, and what serves the whole window stays in the title bar and the sidebar.
3. **A control sits by what it acts on.** A command that acts on a row is in that row's menu and the menu bar ([§ Interaction](#interaction)), and a field that filters a list is read as part of that list. The pane shows only what the list does, so a search or a group that hides the selected item closes it; the pane says an item's group only where neither the list nor its header does.

## States

Every screen designs each state it can reach: first use, empty, loading, filled, error and locked. An empty state offers the next step, a wait says what it is doing, and an error says what to do next ([§ Interaction](#interaction)). A prompt that releases a value keeps its own rules (D-0326, D-0456, D-0634).

## Colour

The app follows the system's light or dark setting, and both palettes are first-class. Settings offers a theme only on a Linux desktop whose settings portal reports neither, and `app.toml`'s `theme` counts only there; elsewhere it stays in the file, unread. Depth is lightness, not shadow. Lines are quiet: cards, tables, dividers, chips and read-only values take them. The control edge outlines text fields, combo boxes, date pickers, unchecked checkboxes, radio rings and a `ToggleSwitch`'s off track; [Q.8](steps/Q.8.md) names the controls drawn without it.

| Role | Dark | Light |
|---|---|---|
| App background | `#111214` | `#F7F7F5` |
| Panel, sidebar, title bar | `#18191C` | `#FFFFFF` |
| Card, dialog | `#1D1E22` | `#FFFFFF` |
| Hover | `#24262A` | `#EFEFEC` |
| Active, secondary button | `#2A2C30` (hover `#33363B`) | `#E6E6E2` (hover `#DDDDD8`) |
| Lines: subtle, default, strong | `#26282C`, `#2C2E33`, `#3A3D43` | `#E8E8E4`, `#DCDCD7`, `#C4C4BE` |
| Control edge | `#74767C` (hover `#8E9096`) | `#84847F` (hover `#6B6D72`) |
| Text: primary, secondary, muted | `#F2F2F0`, `#C9CACD`, `#8E9096` | `#111214`, `#3A3C40`, `#6B6D72` |
| Text: disabled | `#55575C` | `#A3A5A9` |
| Terminal | `#0C0D0E`, header divider `#1D1E22` | unchanged |

The single accent is amber `#F2B544` (hover `#F7C566`, press `#D99D2E`, tint 10–12%). It appears once per view, for the primary action or a live signal, and text on amber is always ink `#111214`. That once counts what asks for attention; what marks a state or the brand does not count against it and stays amber on any view: the selected row's tint and bar, the focus ring, a focused field's border and glow, the lock field's caret, and the marks' dot. While a dialog is up only the dialog is in view, so what its backdrop dims does not count either. Nothing else takes it, a note bar included: a note sits one ink step up, and a problem on the danger tint with its icon. On a light ground amber as text, a focus ring, a focused field, the lock field's caret, a selection bar or a warn dot is `#8A5A00`; a filled primary button stays `#F2B544`.

High-contrast dark and light variants follow macOS's Increase Contrast and Windows' contrast themes, whichever of light or dark is in force. They keep the grounds and raise text and lines: on dark, text `#FFFFFF`, `#F2F2F0`, `#D9DADD`, disabled and subtle line `#A9ABB0`, lines `#B4B6BB`, `#C9CACD`, control edge `#C9CACD` (hover `#F2F2F0`); on light, text `#000000`, `#1D1E22`, `#3A3C40`, disabled and subtle line `#55575C`, lines `#4A4C50`, `#3A3C40`, control edge `#3A3C40` (hover `#1D1E22`), amber `#6B4500`. The terminal stays dark in both, with lines `#6E7076`, `#74767C`, `#7C7E84` and header divider `#6E7076`.

Status colours: ok `oklch(0.80 0.13 155)`, danger `oklch(0.74 0.14 25)` with a 12% tint behind danger buttons, info `oklch(0.78 0.10 245)`. On the light palette the same hues sit at lightness 0.50, `oklch(0.50 0.13 155)`, `oklch(0.50 0.17 25)` and `oklch(0.50 0.10 245)`, so a status read as text keeps 4.5:1 on every light surface. Warn is amber, though the title bar's unsaved or merging dot is idle, since its word says so and New holds the view's amber, and an idle dot is `#7C7E84` on dark and `#55575C` on light. The app draws them as `#72D699`, `#F7857D` and `#7FBEF3`, and on light as `#007840`, `#B02A2D` and `#296898`, because Avalonia has no oklch.

The app's values are in [Tokens.axaml](../src/Keypaste.App/Theme/Tokens.axaml); the CSS copy, for mocks, is [docs/design/tokens](design/tokens/styles.css).

## Type

Instrument Sans 400/500/600 for the interface and Fragment Mono 400 for anything machine-readable: keys, values, references, paths, commands, tokens and timestamps. Both are under the SIL Open Font License and are vendored in [Assets/Fonts](../src/Keypaste.App/Assets/Fonts), so nothing is fetched at runtime.

| Style | Size and weight |
|---|---|
| Display | 54/600, −0.045em |
| Heading | 24–30/600, −0.03em to −0.035em. A screen's title and an open item's title; the app draws 24 |
| List header | 20/600, −0.01em. What a list shows, All items or a group, and New item's title: under the item's title, which outranks it |
| Dialog, card and form title | 15–16/600, −0.01em. A dialog's, a card's, a list's or an inline form's heading, and a prompt's; the app draws 16 |
| Body | 13–14/400, line height 1.5 |
| Row title | 13.5/500. An item's title in a list |
| Button | 13/500, a primary's 13/600 |
| Label | 12/500 |
| Helper line | 12/400, line height 1.5, muted. Never larger than the label it sits with |
| Table header | 11/500, +0.04em, muted, written in capitals. The sidebar's section labels take it too |
| Mono | 12–13. An item's title is sans at heading size in its detail pane; a variable's key stays mono |

[Typography.axaml](../src/Keypaste.App/Theme/Typography.axaml) holds these as text classes, and [TextSizes.axaml](../src/Keypaste.App/Theme/TextSizes.axaml) the sizes alone, for text that keeps its own weight and colour. A view sets a size through one of them. A masked value draws at 14 or larger.

The marks are set in Hepta Slab, and only as outlines: no surface loads the face, and the interface never uses it. The wordmark is weight 500 tracked −1.5%, so it sits beside Instrument Sans without outweighing it; the icon is 580 tracked −1%, so it holds its weight on a tile and at 16px.

## Shape and motion

Spacing is a 4px base on a 16px grid: 2, 4, 6, 8, 12, 16, 20, 24, 32, 40, 56. Radii are 4 for chips and keycaps, 6 for fields, 7 for buttons and rows, 10 for cards and toasts, 14 for dialogs and 15/64 for the app icon.

Shadows appear only on overlays: dialogs `0 24px 64px rgba(0,0,0,.55)`, toasts and popovers `0 8px 24px rgba(0,0,0,.35)`. A modal backdrop is `rgba(8,9,10,.6)` with a 3px blur. Backgrounds are flat ink; the 16px hairline grid is kept for brand moments such as the lock screen.

Hover raises one ink step. A selected row takes a 10% amber tint and a 2px amber inset bar. Focus is a 2px ink gap and a 2px amber ring; a focused field has a 1px amber border and a 3px 15% amber glow. Muted text on a hovered or selected row, a toast or a keycap takes the secondary colour. Waiting is an amber dot with a 3px halo, except in a prompt, whose dot is muted with no halo, since its one amber is its answer. Motion is 120ms for hover and 200ms for overlays, eased `cubic-bezier(.2,.8,.2,1)`, with no bounce.

## Icons

Lucide at a 1.5px stroke: 16px in lists, sidebar rows, toasts and headings and on an icon-only button, 14px in fields and beside a button's word, 20px on the rail, in a tile and as an empty state's mark. Icons are muted by default and turn amber only while their object is live. No emoji and no filled icons. The app's icons are generated into [Icons.axaml](../src/Keypaste.App/Theme/Icons.axaml) by [lucide-to-axaml.py](../scripts/lucide-to-axaml.py).

## Terminal

The CLI keeps to 80 columns. Amber means it needs you: the `›` prompt, commands in help, a pending box, the cursor. Green `✓` is done, red is refused or missing, grey is everything else. Its box and list characters are `┌ │ └ ›`. A value is never printed without `--reveal`, and `--json` is there for piping. The CLI's own output is the reference, and `scripts/verify-demo.sh` checks the transcripts in the README and the guides against the built binaries.

## Voice

Plain and calm; precise where it protects you. Name the actor and the object: "claude-code wants DATABASE_URL". Say how long plainly: a prompt answers "Deny" or "Allow once", never "Trust" or "for a while", and a rule that releases without asking says what it covers and where to change it. Use sentence case, "you" for the person and the agent's real client name for the agent. No exclamation marks and no emoji. An error says what to do next.

The words keypaste uses, on every screen and in the CLI's own sentences:

| Say | For | Not |
|---|---|---|
| Trash | Where a deleted item goes; KeePass files name this group Recycle Bin, and a sentence about the file or KeePassXC may say so | recycle bin |
| Group | What holds items; a folder on disk stays a folder | Folder |
| Title | What an item is called; a token, a field or a group keeps its name | Name |
| key file | The file that unlocks a vault beside its password | keyfile |
| URL | What an item opens, in its field, its column and its menus | web address |
| top level | The place outside every group | the root |
| Disconnect | Taking keypaste out of an AI tool | Remove |
| what it can see | The patterns a bridge's `--expose` takes | selectors |
| keypaste.com | What serves share links | the relay |

A word a person types or a machine reads keeps its spelling: flags such as `--keyfile`, `--client-label` and `--expose`, the group KDBX files call Recycle Bin, `--json` names and file names. Help names the flag and says what it is for in these words.

## Interaction

The desktop follows Apple's Human Interface Guidelines on every platform, with Ctrl where macOS uses ⌘ ([DESIGN-PRINCIPLES](DESIGN-PRINCIPLES.md) §6, D-0682). Where they differ from this page, this page wins, and the security behaviour of the prompts and masked fields wins over both. A new screen starts from these rules.

1. **Every command is in the menus**: the macOS menu bar, the menu button on Windows and Linux, and a row's context menu where the command acts on that row. A new command goes into `AppCommands.cs`, which draws the menus and the chords, in File, Edit, Item, Project, Agents, View, Window and Help, with what acts on one kind of row in its own submenu. An open form's own buttons belong to the form, and a prompt's answers are never in a menu. An open item's custom field row keeps Reveal and Copy on it and takes the rest behind a ⋯ that opens the row's context menu; the same commands in the menu bar act on the row holding the keyboard. A menu item says what it does in sentence case, ends in an ellipsis when it opens a form, a picker or a window, and is off while its command cannot run; one that only asks to confirm takes none, since the question is part of doing it ("Rotate", "Empty Trash"). A button follows the same words and the same ellipsis, named by its result: a form's action says what it will do ("Connect Claude Code", "Disconnect Cursor"), and the row's button that opens that form ends in the ellipsis. Four things stay out of the menus by name. A masked value's Reveal is on its row alone, since opening a menu hides a shown value (rule 9). A sort is pressed on a table's head and has no command, and the menus hold only View › Reset table order and widths (rule 14). A notice's Dismiss belongs to the notice, as a form's buttons belong to the form. A sidebar row's chevron folds it, with Left and Right while it is chosen, since a fold only arranges what is there, as a sort does.
2. **Chords are the platform's own**: ⌘ on macOS and Ctrl elsewhere, matched with exactly their modifiers. None on Windows or Linux holds Ctrl+Alt, which is AltGr, and a standard chord keeps its standard meaning, so ⌘G stays Find next.
3. **A confirmation** puts Cancel first and the action last, names the action by its result ("Delete for good"), draws a destructive action as danger, answers Return with the action unless it is destructive, and Escape with Cancel. An action the person can undo does not ask; one that cannot be undone does.
4. **Work survives**: a draft is kept when the person changes place, or the app asks before discarding it. A lock still discards it.
5. **Every field has a visible label** above it, and a placeholder is only an example; a secret field has none, and its accessible name is its label's fixed words. Items' search names itself by its icon and its one word, and a title's project picker is the heading it names. An error appears under the field it concerns, as the field is typed; an empty field says so once its form's action is pressed.
6. **Feedback is honest**: anything that takes more than a moment shows a moving indicator and says what it is doing, a toast sits centred under the title bar and the row of Items' search and New, its icon says whether it reports a success, a warning or information, and every error says what to do next.
7. **Accessible to everyone**: every control has an accessible name, never a secret. Enabled text keeps 4.5:1 against its ground, and focus rings, the control edge and status marks 3:1, in both palettes; disabled text and decorative lines are exempt, as in WCAG, and stay quiet. A control's target is at least 20 by 20, a row's action buttons sit 8 apart, status is never shown by colour alone, what a screen announces goes through one live line, and text enlarges to 200%, as far as a prompt's screen holds it whole (rule 8).
8. **Prompts keep their own rules** (D-0326, D-0456, D-0634): no default button, focus on Deny, the allows disarmed for a second with their labels still readable, and a fixed layout whose every part, Deny and the reason included, shows on its screen at any zoom. While another window has focus a prompt is drawn inactive, by colour alone, and its allows stay disarmed however long it waits; each activation starts focus on Deny and disarms them for another second. Deny answers at any time.
9. **A masked value reveals by a toggle**, which says Reveal or Hide in its name, its tooltip, its icon and, where it has room, its word, and is pressed once by pointer, Space, Enter or a screen reader. The value is drawn and never in the accessibility tree, one shows at a time, and it hides again after 30 seconds, on leaving its item or place, when a menu opens, and when the window loses focus, is minimized or locks ([THREATS](../THREATS.md) T-25).
10. **One of several is a segmented control**: `Border.segmented` as the log's filter draws it, never amber, a duration in words ("24 hours", "7 days"). One whose choice waits on a confirmation is a `ChoiceSegment`, which shows only what holds. Any number of several, such as the projects an agent sees, stays a row of buttons. Vault access's choices of a key file and a YubiKey stay radio rings, since an option there opens its own controls under it. What is set is never hidden so that it looks unset: a disclosure stays open while anything in it is set.
11. **A drag says what it would do, and only when it would**: a file or an item over what takes it draws a 2px ink frame round the target and a toast-style caption, "Import bitwarden.json", "Attach scan.png to github", "Move to servers", in ink rather than amber, so the view keeps its one amber; over anything else nothing lights up. The file's kind decides where it goes, wherever it is dropped: a KeePass file or an export goes to import and a `.env` to the project in view or the first, each to its preview, and any other file attaches to the open item; on the unlock screen, only a vault, or a damaged one with backups beside it. One file at a time, and nothing is taken while an import or a question is up or a prompt waits. A form holding a draft stops only an attachment and an item's drag, since an import's preview opens over the form and a `.env` goes through the discard question; a file over the attachment cap, or named like one the item has, is not lit, and an item is not lit over a group that would refuse it. Importing and moving have their commands in the menus, and attaching is the pane's Add. On the rail, holding an item on the groups button for half a second opens the groups over the content, and the drag's end closes them.
12. **A file from outside asks before it costs anything**: a file the system or a second start names is only ever selected on the unlock screen. It leaves a typed password or an open form alone and says so, and with a vault open it asks "Lock … and open …?", with Cancel holding the keyboard and Return not the action, since something other than the person can raise it; a file the unlock screen would refuse is refused first, in a toast, with the vault left open. A file keypaste hasn't opened shows its full path in Fragment Mono under the heading until another is selected, each invisible character written out.
13. **A notification says nothing of what it is about**: the lock screen, other apps and the system's history can read it, so it holds only the fixed words in `SystemNotice.cs`, never an item, field, group, value, vault, agent or reason. It has one action, which brings keypaste forward and answers nothing; a prompt still appears and still decides, its allows disarmed for a second as the action brings it forward. It is posted for a request only while keypaste is in the background, at most once every 30 seconds, and is removed when its request or the locked spell ends. macOS's permission is asked when the person opens Agents or Settings with no prompt waiting, never for a notice, and until it is given a request's prompt still appears and the locked notice is keypaste's own window. On Windows, and where the system cannot take it, the locked notice is keypaste's own window, which may name the vault because it never reaches the lock screen.

14. **A table sorts by its heads and sizes by their edges**: Items, Trash and the activity log. A head stays in capitals and is a control 20 tall: pressing it sorts by its column and again reverses it, a time starting at the newest, and its arrow shows the direction, in the text's own colour, never amber, with its name saying the column and the direction. Text sorts by the current culture, ignoring case and with "item 2" before "item 10"; on a Linux machine with no ICU, by code point, with numbers as text. What a cell leaves blank, or a time it does not have, sorts last either way; `TableSorting.cs` holds the comparison. An edge between two heads is a 20 by 20 target drawn in the control edge that moves with a drag or, in focus, the arrow keys, each column holding its least, and never sorts; a sorted head's arrow stays clear of it, its text trimming first. A sort only arranges what is there: the selection, the pane, what the search or filter kept and the audit chain are untouched. View › Reset table order and widths brings back the table's own order, the vault's on Items, the latest deleted first on Trash and the file's on the log, and its own widths. `[[window]]` keeps a table's order and widths once they differ.
15. **The pointing hand means a link**: `Button.link` and nothing else, a switch, a radio button and a row included.
16. **A tooltip says what its control does**, and where a field takes a grammar of its own, what an agent can see or a token's scope, a Learn more beside it explains it in the app.
17. **Scroll bars follow the system's setting**: macOS's scroller style, Windows' Always show scrollbars and GNOME's overlay scrolling, read again as a window comes forward. Another Linux desktop has no standard for it, and they hide until scrolled.

## The marks

The icon is "k." and the wordmark "keypaste.": the letters in ink, `#F2F2F0` on dark and `#111214` on light, and the square dot amber, sized from the letters' stem and set close after the last one. They are two marks, never set side by side: the app's sidebar carries the wordmark alone, its lock screen and every icon the "k." alone.

| File in [`assets/brand/`](../assets/brand) | Use |
|---|---|
| `keypaste-glyph-dark.svg` / `keypaste-glyph-light.svg` | The icon on a dark or light ground |
| `keypaste-wordmark-dark.svg` / `keypaste-wordmark-light.svg` | The wordmark on a dark or light ground; the README switches between them with `prefers-color-scheme` |
| `keypaste-glyph-mono.svg` / `keypaste-wordmark-mono.svg` | One-colour use, the dot included; takes `currentColor` |
| `keypaste-app-icon.svg` | App icon: the icon on a `#1D1E22` tile, radius 15/64, 46% of its height |
| `keypaste-favicon.svg` | 32px and below: the same tile at radius 12/64 with a larger "k.", whose serifs stay apart at 16px |

[outline-brand-marks.py](../scripts/outline-brand-marks.py) outlines both marks from the recorded Hepta Slab file into these files, the site's favicon and the app's [BrandOutlines.axaml](../src/Keypaste.App/Theme/BrandOutlines.axaml); [render-app-icon.py](../scripts/render-app-icon.py) draws the desktop app's `.ico`, PNG, Linux icon and macOS `.icns` from the two tiles.

## Rules

1. **Always lowercase.** The wordmark and the product name.
2. **The icon and the wordmark are never set side by side, and the dot is never omitted.** It is amber, or the ink's colour in one-colour use.
3. **The icon's floor is 16px and the wordmark's is 88px wide.** At 32px or smaller, use the favicon.
4. **Amber appears once per view**, for the primary action or a live signal, and carries ink text.
5. **Machine-readable text is mono**, everything else is Instrument Sans.
6. **Depth is lightness.** Shadows belong to overlays only.
7. **Every allow says how long it lasts**, in the interface and in the copy about it.

## Checking a screen

`KEYPASTE_SCREENS_OUT=<dir> bash scripts/verify.sh test Keypaste.App.Tests.Rendering.ScreenRenderer` draws every screen with a demo vault in each palette, and in dark and light at 150% and 200%; a screen is checked against this page and the principles' tests. Without a folder, the same class still walks every frame at each zoom and fails a control or a line of text cut off with no scrolling that reaches it.
