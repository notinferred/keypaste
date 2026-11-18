# Design

The system every keypaste surface is designed from, the desktop app, the CLI, the site, the README, the store listings and the installers, written down before a screen is designed ([DESIGN-PRINCIPLES](DESIGN-PRINCIPLES.md#craft)). This page owns how a screen is put together, the marks, the palette, the type, the shape, the icons, the terminal rules, the voice, the interaction rules and the usage rules. Where a value also has to exist as code, the file named below holds it, and the app's views hold each screen's exact sizes.

**Brand status:** adopted 2026-09-24 and amended by the founder since, last on 2026-11-17; the amendments are in Git. The desktop app's theme, icon, shell and screens, keypaste.com (through [brand.css](../site/public/brand.css)) and the README's wordmark and screenshots are built from it, and the CLI follows the terminal rules below.

## The desktop's places

The window is laid out as Outlook for Mac lays out mail. A rail of places runs down its left side: Items, Agents, Security and Settings, on Ctrl/⌘+1–4, with Access's slot dimmed for later between Security and Settings, which sits at the rail's foot. Every other screen sits under one of the four, its Back going up to it, except a vault's Environments and Trash, which the sidebar lists. The window has four regions:

| Region | Holds |
|---|---|
| Title bar | The button that hides and shows the sidebar on Items, the one search, centred and naming the vault it searches, and at its end the idle-lock countdown while a lock is near, the save state of the vault in view while its file does not hold what is open, its file and time saved as the tooltip, and Lock |
| Rail | The places as icons, each named by its tooltip and accessible name, the chosen one on the accent's tint with its icon in the accent. Agents carries its mark and Security a badge counting what needs review |
| Sidebar | Items' alone: New at its top, then Favorites, the items tagged `favorite` with their count, then, with one vault open, that vault's places directly under it, with no account, vault heading or All Accounts; only once the local account holds a second vault does the sidebar draw All Accounts and the local account, On this Mac or On this PC, holding each open vault as a section headed by its title. Add vault… sits quietly at the sidebar's foot, opening a vault from its file or creating one, as File › Add vault… opens one. A vault's places are All items, Logins, API keys, Environments, Cards, Notes, Server and Database, each with its count, those of a kind the vault holds none of left out but All items, Logins, Environments and Trash, then Trash, and a Groups row, folded until opened, over the KeePass group tree. While Environments is in view the vault's projects are listed under it, the open one chosen, and the content shows the chosen project: its stages, the selected stage's variables and its run command, with no `.env.keypaste` preview, which Edit › Export .env.keypaste… writes as a file. A vault's heading menu has Lock this vault and Close vault, which takes it out of the account and leaves its file. A vault's heading says merging while another program's save of it is merged, and unsaved while its file lacks a change made here; with one vault, the title bar's save state says so. A locked vault shows its title and a lock, and its places only once it is unlocked; choosing it shows its unlock form in the list's room, the form's one accent its Unlock, while the other vaults stay open. All Accounts and Favorites list every unlocked vault's items in one list, each row's vault muted at its right, and the title bar's search spans them |
| Content | The place in view. Items, Agents and Trash are a list on the left and the chosen thing on the right; Items' list is 280 wide under a header of the place's title with its vault muted after it, a sort menu and "+", its rows in sections of Today, This week and Older while it runs by when items changed, each two lines with the item's tile, its title and its username or kind; the time it changed is the row's tooltip and the pane's Modified line, never drawn on the row |

Every screen acts on the vault it shows: a vault's places, the item pane, New item's vault and Trash on that vault, and Settings' This vault and Agents on the vault in view, each with a vault picker at its top while several are unlocked, since each vault keeps its own settings and agent rules. Security checks every unlocked vault and lists each one's findings under its title, with a filter for one vault. Lock, the idle lock and Ctrl/⌘+L lock every vault together.

The sidebar and the rail are translucent over the system's vibrancy on macOS and solid on Windows, Linux and under high contrast; Settings › General's Reduce transparency makes them solid on macOS too.

New item is a sheet over the window. The sidebar's New and File › New item from template… (⇧⌘N) open its grid of templates, four to a row, each a 26px icon on the accent's tint over its name: Login, API key, Environment, Card, Note, Identity, SSH key, Server, Database, Wi-Fi and Blank item, with Save in and Cancel at its foot; there is no Passkey tile, since websites make passkeys. A tile opens its form at once: Save in at its top, a pop-up of account › vault listing every open vault and starting at the vault in view, the monogram's place holding the template's icon beside the title, which starts as "New login" and the like, the template's fields as grouped rows, each label at its row's left, then Custom fields with Add field…, and Cancel and Save. A template only presets: Add field… adds any field the form lacks and each row's × removes it, and a preset custom field left empty is not written. Card and Identity start with the `Credit Card` and `Identity` tags, which make their protected fields card and identity values in agent settings (`FieldCategory.cs`). An item with either tag is a card or an identity, in its row, the Cards place and its pane, whatever its icon, so a Bitwarden or 1Password import's cards are listed there; every other kind is read from the icon. Any item with a username, a password and a website fills in the browser, whatever its template, and while the form holds those three it says "Fills in your browser on this website." under the rows. The "+" beside the list's title creates in the place in view: on a place of one kind, Logins, API keys, Cards, Notes, Servers or Databases, it opens that kind's form in the vault in view, its tooltip "New login in Personal"; on All items and Favorites it opens a menu headed "New in Personal" of Login, API key, Environment, Card and Note, then More templates…, which opens the grid. ⌘N does what the direct "+" does, and opens a login's form where the place holds every kind.

The window's title names the screen in view, "Items - keypaste", and is "keypaste" alone on the first-launch and unlock screens: only a screen's fixed name, never a vault, group, project or item, since the system's window list, taskbar and switcher show it.

The window goes down to 720 wide, so two sit side by side on a laptop. The rail stays at every width, 52 wide. Under 900 wide the sidebar hides, leaving the rail, the list and the item; the title bar's button opens it over the content, which keeps its width, until Escape, a choice in it or a press outside, and its tooltip names the other ways to it. Where the content cannot hold the list and what it chose side by side, the chosen thing replaces the list and has a Back to it. Wherever the content is under 768 wide, whatever else takes the window, a page narrows its margins. The rail is one list the arrow keys walk, passing Access. Agents' mark says its state by its shape as well as its colour: a filled dot while answering agents, a ring while not, a diamond while refusing them, its word in the tooltip and the accessible name. View › Hide sidebar hides the sidebar at any wide width and the title bar's button brings it back; in a narrow window both open it over the content. `WindowChrome.cs` holds the sizes.

View › Zoom in, Zoom out and Actual size (⌘+, ⌘− and ⌘0) scale every window's interface from 100% to 200% in steps of 25, by drawing it through a layout transform, so the sizes on this page and the breakpoint above read the design's pixels, and at 200% a window 1500 wide lays out as one 750 wide. The minimum window scales with the zoom but never passes the screen's working area; where the screen cannot hold it, the interface keeps its minimum and scrolls inside the window. A prompt takes the zoom as it opens, scaled as a whole so its layout stays fixed, or at the largest scale that shows all of it on its screen, and keeps that scale while it is open.

## Information

1. **Each fact once in a view.** A name, a state or a location appears in one place in a view, the place the person looks for it; another place links to it or leaves it out. The vault's title, the place or group in view and the save state are each such a fact: the list's header names the place or group with the vault after it, and the sidebar heads each vault by its title only while several are open, Items' rows leave out their group, and the pane names a row's subgroup after its kind and vault; an Env cell names its item only where that differs from the item holding most of its column's keys, a tie going to the first path in ordinal order, with the full path always on its tooltip and accessible name.
2. **The main job gets the room.** On Items that is finding an item and using it: the list and the pane take the space, and what serves the whole window stays in the sidebar.
3. **A control sits by what it acts on.** A command that acts on a row is in that row's menu and the menu bar ([§ Interaction](#interaction)), and a field that filters a list is read as part of that list. The pane shows only what the list does, so a search or a group that hides the selected item closes it; the pane says an item's group only where neither the list nor its header does.
4. **Grouped rows, not boxes.** A group of related values is one panel of rows split by hairlines, each row a label and its value, never a box round each field or a card round each group. A word that only names an action on a value, Copy, Reveal or Generate, is not on the row until the row is pointed at or focused.
5. **An explanation sits behind a tooltip or a Learn more.** A warning, a refusal, an error and anything about safety stays on the screen.

## States

Every screen designs each state it can reach: first use, empty, loading, filled, error and locked. An empty state offers the next step, a wait says what it is doing, and an error says what to do next ([§ Interaction](#interaction)). A prompt that releases a value keeps its own rules ([§ Interaction](#interaction), rule 8).

## Colour

The app follows the system's light or dark setting, and both palettes are first-class. Settings offers a theme only on a Linux desktop whose settings portal reports neither, and `app.toml`'s `theme` counts only there; elsewhere it stays in the file, unread. The palette is Native, the grounds and greys of macOS, so the app sits among the system's own apps on every platform. Depth is lightness, not shadow. Lines are quiet hairlines: rows in a group, tables, dividers, chips and read-only values take them, and a group itself takes only a ground a step off the window. The control edge outlines text fields, combo boxes, date pickers, unchecked checkboxes, radio rings and a `ToggleSwitch`'s off track; [Q.8](steps/Q.8.md) names the controls drawn without it.

| Role | Dark | Light |
|---|---|---|
| Window, the content's ground | `#1E1E1F` | `#FFFFFF` |
| Panel: a group of rows, a filled card | `#252527` | `#F5F5F7` |
| Sidebar and rail, solid | `#252528`, `#1C1C1F` | `#EEEEF1`, `#E6E6EA` |
| Card, dialog, menu, a field's surface | `#2A2A2C` | `#FFFFFF` |
| Hover | `#2E2E30` | `#EDEDEF` |
| Active, a segmented control's track | `#3A3A3C` (hover `#444446`) | `#E5E5EA` (hover `#DCDCE0`) |
| A chosen segment | `#636366` | `#FFFFFF` |
| Lines: subtle, default, strong | `#333336`, `#444448`, `#545458` | `#E4E4E7`, `#D1D1D6`, `#C7C7CC` |
| Control edge | `#7C7C82` (hover `#98989D`) | `#86868B` (hover `#6B6B70`) |
| Text: primary, secondary, muted | `#F5F5F7`, `#B4B4BA`, `#949499` | `#1D1D1F`, `#515154`, `#6B6B70` |
| Text: disabled | `#5A5A5F` | `#A6A6AB` |
| Terminal | `#0C0D0E`, header divider `#1D1E22` | unchanged |

On macOS the sidebar and the rail are translucent over the system's vibrancy, their solid colours at 70% and 80% on dark and 72% and 78% on light; on Windows and Linux, and under high contrast, they are solid, and Settings › General's Reduce transparency, kept in `app.toml`'s `[[appearance]]`, makes them solid on macOS too. The title bar is `#262628` on dark and `#F6F6F8` on light, its search a step off it.

The accent is the system's: the accent colour the person chose in macOS, Windows or their Linux desktop, followed as they change it, and system blue, `#007AFF` on light and `#0A84FF` on dark, where the platform reports none. [AccentPalette.cs](../src/Keypaste.App/Theme/AccentPalette.cs) derives each accent colour from it for each palette, moving each only as far as it must to read. A primary button's fill is darkened until white on it keeps 4.5:1, or carries ink `#1D1D1F` where white starts under 3:1 on it, as on yellow, orange and green; its hover and press move 6% and 12% further from its text. The accent as text is lightened on dark and darkened on light until it keeps 4.5:1 on every ground it sits on, and the focus ring the same way to 3:1. A selected row's tint is the accent mixed 24% into the dark window and 14% into the light one, thinned until the row's text keeps 4.5:1. Under high contrast, text on the fill and the accent as text keep 7:1 and the ring 4.5:1.

The filled accent appears once per view, for the primary action or a live signal. On an item that is Copy password, with New a quiet button; on a project it is Run; elsewhere the view's one primary action. That once counts filled buttons alone. What marks a state takes the accent on any view, as macOS draws it: a selected row's tint, a checked box, a chosen radio and a switch that is on, the sidebar's icons and the rail's chosen one, an item's URL and its History, the badge counting what needs attention, the focus ring, a focused field's border and glow and the lock field's caret. While a dialog is up only the dialog is in view, so what its backdrop dims does not count either. Nothing else takes it, a note bar included: a note sits one step up, and a problem on the danger tint with its icon. The marks' dot is the brand's amber `#F2B544`, never the accent.

High-contrast dark and light variants follow macOS's Increase Contrast and Windows' contrast themes, whichever of light or dark is in force. They keep the grounds, draw the sidebar and rail solid, and raise text and lines: on dark, text `#FFFFFF`, `#F5F5F7`, `#D1D1D6`, disabled `#A1A1A6`, lines `#8E8E93`, `#A1A1A6`, `#C7C7CC`, control edge `#C7C7CC` (hover `#F5F5F7`); on light, text `#000000`, `#1D1D1F`, `#3A3A3C`, disabled `#545458`, lines `#6E6E73`, `#545458`, `#3A3A3C`, control edge `#3A3A3C` (hover `#1D1D1F`). The terminal stays dark in both, with lines `#6E7076`, `#74767C`, `#7C7E84` and header divider `#6E7076`.

Status colours, on dark and on light: ok `#3DD06B` and `#1A7A35`, warn `#FFB340` and `#9A5B00`, danger `#FF6961` and `#C4261F`, with a 14% and 10% tint behind danger buttons and problems, and info, indigo `#A09EFF` and `#5856D6`, a hue apart from system blue so that information never reads as the accent. Each keeps 4.5:1 as text on every ground it sits on. The sidebar foot's unsaved or merging dot is idle, `#8E8E93` on dark and `#6B6B70` on light, since its word says so.

An item's tile takes one of seven hues, `#3A7BD5`, `#2F9E6E`, `#C2410C`, `#7C4DCC`, `#B4235A`, `#5B6472` and `#0E7490`, picked by its UUID, with its letter or icon in white. A service's mark sits on its brand's colour in white, or in ink `#1D1D1F` where white is under 3:1 on it, and on the active ground in the primary text where neither keeps 3:1.

The app's values are in [Tokens.axaml](../src/Keypaste.App/Theme/Tokens.axaml) and the accent's derivation in AccentPalette.cs; the CSS copy, for mocks, is [docs/design/tokens](design/tokens/styles.css). keypaste.com draws the same palette from [brand.css](../site/public/brand.css), following the browser's light or dark setting, with system blue as its accent, since a page cannot read the system's, derived as the app derives it; the current page in a list of links takes the active ground rather than the accent's tint, so the filled button stays the only accent in view.

## Type

The desktop draws its text in the system's own faces, fetching none: the interface face for words, and the system's monospace for anything machine-readable, which is keys, secret values, variable names, references, paths, commands, tokens and timestamps. Names, usernames, URLs, project and stage names and labels are words. Avalonia draws only a variable font's default instance, and SF and SF Mono are variable, so until it draws their weights macOS takes Helvetica Neue and Menlo, which have no 600 and no 500: macOS draws 600 as bold and 500 as regular. Windows takes Segoe UI and Cascadia Mono or Consolas, and Linux the desktop's default face and its monospace from fontconfig. keypaste.com names the same faces through CSS stacks and loads no font file; the demo GIF's terminal keeps Fragment Mono, under the SIL Open Font License.

| Style | Size and weight |
|---|---|
| Display | keypaste.com's hero headline, 54/600 up to 76/600, −0.03em |
| Heading | 20/700, −0.01em. A screen's title, an open item's title and the title New item's form starts with |
| List header | 15/700. What a list shows, All items or a group, and New item's title: under the item's title, which outranks it |
| Dialog, card and form title | 15/600. A dialog's, a card's, a list's or an inline form's heading, and a prompt's |
| Body | 13/400, line height 18 |
| Row title | 13/600. An item's, an agent's or a trashed item's title in a list's two-line row, over one secondary line in the helper line's size and colour; a section of the list, Today, is 11/600 muted |
| Button | 13/600 |
| Label | 12/500 |
| Helper line | 12/400, line height 16, muted. Never larger than the label it sits with |
| Table header | 11/600, +0.04em, muted, written in capitals. A project's table's heads, Variable, Value and From item, take its size and weight in sentence case; the caption above an item's custom fields is 12/600 in the secondary text, and the sidebar's account heading 11/600 muted |
| Mono | 12–13. An item's title is words at heading size in its detail pane; a variable's key stays mono |

[Typography.axaml](../src/Keypaste.App/Theme/Typography.axaml) holds these as text classes, and [TextSizes.axaml](../src/Keypaste.App/Theme/TextSizes.axaml) the sizes alone, for text that keeps its own weight and colour. A view sets a size through one of them. A masked value draws at 14 or larger.

The marks are set in Hepta Slab, and only as outlines: no surface loads the face, and the interface never uses it. The wordmark is weight 500 tracked −1.5%, so it sits beside the interface's face without outweighing it; the icon is 580 tracked −1%, so it holds its weight on a tile and at 16px.

## Shape and motion

Spacing is a 4px base on a 16px grid: 2, 4, 6, 8, 12, 16, 20, 24, 32, 40, 56, with 10 and 14 for the sidebar's and a group of rows' insets, as macOS draws them. Radii are 4 for keycaps, 6 for a chosen segment, 7 for buttons and fields, 8 for list rows, tiles and a segmented control's track, 10 for groups, cards, menus and toasts, 12 for a large tile, 14 for dialogs and 15/64 for the app icon; a chip is a pill.

Shadows appear only on overlays and on a chosen segment: dialogs `0 22px 60px rgba(0,0,0,.35)` with a 0.5px edge, toasts and popovers `0 8px 24px rgba(0,0,0,.25)` with the same edge, and a chosen segment `0 1px 1.5px rgba(0,0,0,.06)`. A modal backdrop is `rgba(0,0,0,.4)`. Backgrounds are flat; the 16px hairline grid is kept for brand moments such as the lock screen.

Hover raises one step. A selected row takes the accent's tint edge to edge, with no bar. Focus is a 2px gap in the window's colour and a 2px accent ring; a focused field takes the ring as its border and a 3px glow of it at 28%. A default button is the card's surface with a default line round it, a primary is the filled accent, and a plain button has neither until hovered. A segmented control is the active ground with its chosen segment raised on it, as macOS draws one. Muted text on a hovered or selected row, a toast or a keycap takes the secondary colour. Waiting is an accent dot with a 3px halo; a prompt says its time left in words, since its one accent is its answer. Motion is 120ms for hover and 200ms for overlays, eased `cubic-bezier(.2,.8,.2,1)`, with no bounce. keypaste.com's home page tells one story in square dots of the secondary text colour, drawn on the page itself rather than in boxes: they sit apart in five groups under the hero, gather into one square beside the first chapter, then pass a gate, branch to a team, regroup under an organization and run down one lane to a server as the page scrolls, with the accent for the moment something is allowed or handed over and the amber only on the mark's dot. Words keep their contrast over them on a halo of the page's own colour. One local script, [animations.js](../site/public/animations.js), draws them with no library; under reduced motion each chapter shows a still as it is reached, the dots move only while the story is on screen, a phone draws fewer, and without JavaScript the chapters read as text alone.

## Icons

Lucide at a 1.5px stroke: 16px in lists, sidebar rows, toasts and headings and on an icon-only button, 14px in fields and beside a button's word, 20px on the rail, in a tile and as an empty state's mark, and 26px on a template's tile. Icons are muted by default and take the accent only while their object is live. No emoji and no filled icons. An item is shown by its tile, never by an icon its site serves, which keypaste never fetches. The tile draws, first, the mark of a popular service its website's registrable domain belongs to, a subdomain included and a lookalike never, from Simple Icons' marks bundled in [ServiceIcons.axaml](../src/Keypaste.App/Theme/ServiceIcons.axaml) by [simple-icons-to-axaml.py](../scripts/simple-icons-to-axaml.py) and matched in `Controls/ItemMarks.cs`; otherwise its kind's icon for any kind but a login or a password, `braces` for a project's variable, `laptop` for this computer (`localhost`, `127.0.0.1`, `::1`), `ethernet-port` for another IP address and `house` for a single-label, `.local`, `.lan` or `.home.arpa` host; otherwise its title's first letter on its hue. The app's icons are generated into [Icons.axaml](../src/Keypaste.App/Theme/Icons.axaml) by [lucide-to-axaml.py](../scripts/lucide-to-axaml.py).

Each icon has one meaning, and each meaning one icon:

| Icon | Means |
|---|---|
| `inbox` | Items, all of them |
| `star` | Favorites |
| `layers` | All accounts |
| `users` | An account |
| `globe` | A login, a website |
| `key` | An API key |
| `braces` | Environments, a .env template |
| `credit-card` | A card |
| `file-text` | A note |
| `id-card` | An identity |
| `key-round` | A passkey |
| `key-square` | An SSH key |
| `server` | A server |
| `database` | A database |
| `wifi` | A Wi-Fi network |
| `laptop` | This computer, an item's `localhost` |
| `ethernet-port` | An IP address |
| `house` | A host on the home network |
| `bot` | Agents, an agent |
| `shield` | Security |
| `network` | Access |
| `app-window` | A browser |
| `settings` | Settings |
| `trash` | Trash |
| `square-pen` | New |
| `play` | Run |
| `tag` | Tags |
| `history` | History |
| `terminal` | A command |
| `list-filter` | Filter and sort a list |

## Terminal

The CLI keeps to 80 columns. Amber means it needs you: the `›` prompt, commands in help, a pending box, the cursor. Green `✓` is done, red is refused or missing, grey is everything else. Its box and list characters are `┌ │ └ ›`. A value is never printed without `--reveal`, and `--json` is there for piping. The CLI's own output is the reference, and `scripts/verify-demo.sh` checks the transcripts in the README and the guides against the built binaries.

## Voice

Plain and calm; precise where it protects you. Name the actor and the object: "claude-code wants DATABASE_URL". Say how long plainly: a prompt answers "Deny" or "Allow once", never "Trust" or "for a while", and a rule that releases without asking says what it covers and where to change it. Use sentence case, "you" for the person and the agent's real client name for the agent. No exclamation marks and no emoji. An error says what to do next.

This table is keypaste's glossary: the words keypaste uses on every screen, in the CLI's own sentences and in its documents.

| Say | For | Not |
|---|---|---|
| Trash | Where a deleted item goes; KeePass files name this group Recycle Bin, and a sentence about the file or KeePassXC may say so | recycle bin |
| Group | What holds items; a folder on disk stays a folder | Folder |
| Title | What an item is called; a token, a field or a group keeps its name | Name |
| key file | The file that unlocks a vault beside its password | keyfile |
| Website | What an item opens, in its field, its row and its menus; a sentence about the file, a flag or KeePass's field names it URL | URL, web address |
| One-time code | An item's time-based code, in its field and its menus; KeePass and KeePassXC call it TOTP, and a sentence about the file may say so | TOTP |
| top level | The place outside every group | the root |
| Disconnect | Taking keypaste out of an AI tool | Remove |
| what it can see | The patterns a bridge's `--expose` takes | selectors |
| keypaste.com | What serves share links | the relay |
| item | A record in the vault; KeePass and KeePassXC call it an entry, and a sentence about the file, the format or KeePassXC may say so | entry |
| Environments | The place that holds each project's `.env` templates, and such a template | — |
| stage | One of a project's variants, such as dev, staging or prod, that the tag `env:<project>:<stage>` names; the `--profile` flag keeps its spelling | environment, profile |
| env set | The variables a run receives from a project's stage | profile, Env Sets |
| account | What holds vaults; the one on this computer is On this Mac or On this PC | — |
| vault | A KeePass file keypaste opens, which an account holds | — |
| agent | The program asking for a value, named by its AI tool's client name | — |
| AI tool | The program a person connects to keypaste, such as Claude Code | — |
| bridge | `keypaste mcp`, which an AI tool starts and which never opens the vault | MCP server, agent bridge |
| approver | The app, or `keypaste approve`, while it holds the vault unlocked and answers agents; the code calls it the vault's owner | holder, authority |
| agent settings | What each agent may receive and run, Ask, Allow or Block by field category, kept for each vault on this computer and never in the vault | policy, mode |
| Activity | The desktop's view of the audit log | History, which names an item's earlier versions |

A word a person types or a machine reads keeps its spelling: flags such as `--keyfile`, `--client-label` and `--expose`, the group KDBX files call Recycle Bin, `--json` names and file names. Help names the flag and says what it is for in these words.

## Interaction

The desktop follows Apple's Human Interface Guidelines on every platform, with Ctrl where macOS uses ⌘ ([DESIGN-PRINCIPLES](DESIGN-PRINCIPLES.md#familiarity)). Where they differ from this page, this page wins, and the security behaviour of the prompts and masked fields wins over both. A new screen starts from these rules.

1. **Every command is in the menus**: the macOS menu bar, the menu button on Windows and Linux, and a row's context menu where the command acts on that row. A new command goes into `AppCommands.cs`, which draws the menus and the chords, in File, Edit, Item, Project, Agents, View, Window and Help, with what acts on one kind of row in its own submenu. An open form's own buttons belong to the form, and a prompt's answers are never in a menu. An open item's custom field row keeps Reveal on it, its value copies when pressed (rule 18), and the rest sits behind a ⋯ that opens the row's context menu; the same commands in the menu bar act on the row holding the keyboard. A menu item says what it does in sentence case, ends in an ellipsis when it opens a form, a picker or a window, and is off while its command cannot run; one that only asks to confirm takes none, since the question is part of doing it ("Rotate", "Empty Trash"). A button follows the same words and the same ellipsis, named by its result: a form's action says what it will do ("Connect Claude Code", "Disconnect Cursor"), and the row's button that opens that form ends in the ellipsis. Four things stay out of the menus by name. A masked value's Reveal is on its row alone, since opening a menu hides a shown value (rule 9). A sort is chosen in its list's header menu and has no command, and the menus hold only View › Reset list order (rule 14). A notice's Dismiss belongs to the notice, as a form's buttons belong to the form. A sidebar row's chevron folds it, with Left and Right while it is chosen, since a fold only arranges what is there, as a sort does.
2. **Chords are the platform's own**: ⌘ on macOS and Ctrl elsewhere, matched with exactly their modifiers. None on Windows or Linux holds Ctrl+Alt, which is AltGr, and a standard chord keeps its standard meaning, so ⌘G stays Find next.
3. **A confirmation** puts Cancel first and the action last, names the action by its result ("Delete for good"), draws a destructive action as danger, answers Return with the action unless it is destructive, and Escape with Cancel. An action the person can undo does not ask; one that cannot be undone does.
4. **Work survives**: a draft is kept when the person changes place, or the app asks before discarding it. A lock still discards it.
5. **Every field has a visible label** above it, or, in a row of grouped rows wide enough to hold both (`FieldRow.NarrowBelow`), at the row's left with the field to its right, as System Settings draws them, and before the field on its line in a pop-up row such as Save in; New item's form takes the left, and a field outside grouped rows keeps its label above. A placeholder is only an example; a secret field has none, and its accessible name is its label's fixed words. Items' search names itself by its icon and its placeholder, Search and the vault it searches, its accessible name the one word, a title's project picker is the heading it names, and New item's title field is named by the monogram's place beside it and its accessible name. An error appears under the field it concerns, as the field is typed; an empty field says so once its form's action is pressed.
6. **Feedback is honest**: anything that takes more than a moment shows a moving indicator and says what it is doing, a toast sits centred under the title bar and the header of Items' list, its icon says whether it reports a success, a warning or information, and every error says what to do next.
7. **Accessible to everyone**: every control has an accessible name, never a secret. Enabled text keeps 4.5:1 against its ground, and focus rings, the control edge and status marks 3:1, in both palettes; disabled text and decorative lines are exempt, as in WCAG, and stay quiet. A control's target is at least 20 by 20, a row's action buttons sit 8 apart, status is never shown by colour alone, what a screen announces goes through one live line, and text enlarges to 200%, as far as a prompt's screen holds it whole (rule 8).
8. **Prompts keep their own rules**: every prompt an agent, a run or a terminal raises is a claim card. keypaste's wordmark heads it; the requester line names who is asking and says Verified only when keypaste checked it with the system ([THREATS](../THREATS.md) T-3), otherwise Not verified; then the question, the item by its title and vault, the agent's own words quoted under "<agent> says", one plain line of what allowing does, and Details, closed, with the tool, label, path and process. The footer holds a quiet "Denies in" and at most three buttons, Deny, Always allow where PRODUCT allows it, and the narrowest answer as the primary; a protected item says "Protected · asked every time" and offers no Always allow, and Always allow asks once to confirm; no prompt offers Always block, which is set on the agent's page. Focus starts on Deny, and Return presses the primary, Escape denies or cancels, from any control, but the allows, Return's included, stay disarmed for a second with their labels readable, and Return never presses a destructive action. Nothing an agent sends moves the question or the buttons: its words and a run's command sit in boxes of fixed height that scroll, the quote's rule only as tall as the words, and its name is cut in a sentence; the item picker's list is as tall as its items up to its most, since the vault, not the agent, decides them. The layout shows whole on its screen at any zoom. While another window has focus a prompt is drawn inactive, by colour alone, and its allows stay disarmed however long it waits; each activation starts focus on Deny and disarms them for another second. Deny answers at any time.
9. **A masked value reveals by a toggle**, which says Reveal or Hide in its name, its tooltip, its icon and, where it has room, its word, and is pressed once by pointer, Space, Enter or a screen reader. Pressing the value itself copies it and never reveals it (rule 18). The value is drawn and never in the accessibility tree, one shows at a time, and it hides again after 30 seconds, on leaving its item or place, when a menu opens, and when the window loses focus, is minimized or locks ([THREATS](../THREATS.md) T-25).
10. **One of several is a segmented control**: `Border.segmented` as the log's filter draws it, never the accent, a duration in words ("24 hours", "7 days"). One whose choice waits on a confirmation is a `ChoiceSegment`, which shows only what holds. Any number of several, such as the projects an agent sees, stays a row of buttons. Vault access's choices of a key file and a YubiKey stay radio rings, since an option there opens its own controls under it. What is set is never hidden so that it looks unset: a disclosure stays open while anything in it is set.
11. **A drag says what it would do, and only when it would**: a file or an item over what takes it draws a 2px ink frame round the target and a toast-style caption, "Import bitwarden.json", "Attach scan.png to github", "Move to servers", in ink rather than the accent, so the view keeps its one accent; over anything else nothing lights up. The file's kind decides where it goes, wherever it is dropped: a KeePass file or an export goes to import and a `.env` to the project in view or the first, each to its preview, and any other file attaches to the open item; on the unlock screen, only a vault, or a damaged one with backups beside it. One file at a time, and nothing is taken while an import or a question is up or a prompt waits. A form holding a draft stops only an attachment and an item's drag, since an import's preview opens over the form and a `.env` goes through the discard question; a file over the attachment cap, or named like one the item has, is not lit, and an item is not lit over a group that would refuse it. Importing and moving have their commands in the menus, and attaching is the pane's Add. Holding an item on a folded Groups row for half a second unfolds it, and in a narrow window holding it on the title bar's sidebar button opens the sidebar over the content with its groups unfolded, which the drag's end closes.
12. **A file from outside asks before it costs anything**: a file the system or a second start names is only ever selected on the unlock screen. It leaves a typed password or an open form alone and says so, and with a vault open it asks "Lock … and open …?", with Cancel holding the keyboard and Return not the action, since something other than the person can raise it; a file the unlock screen would refuse is refused first, in a toast, with the vault left open. A file keypaste hasn't opened shows its full path in the monospace under the heading until another is selected, each invisible character written out.
13. **A notification says nothing of what it is about**: the lock screen, other apps and the system's history can read it, so it holds only the fixed words in `SystemNotice.cs`, never an item, field, group, value, vault, agent or reason. It has one action, which brings keypaste forward and answers nothing; a prompt still appears and still decides, its allows disarmed for a second as the action brings it forward. It is posted for a request only while keypaste is in the background, at most once every 30 seconds, and is removed when its request or the locked spell ends. macOS's permission is asked when the person opens Agents or Settings with no prompt waiting, never for a notice, and until it is given a request's prompt still appears and the locked notice is keypaste's own window. On Windows, and where the system cannot take it, the locked notice is keypaste's own window, which may name the vault because it never reaches the lock screen.

14. **A list sorts from a menu in its header**: Items and Trash. A small menu of the orders the list offers, each named by its field and its direction, with the current one marked, and a time starting at the newest. Items runs by when items changed, newest first, in sections of Today, This week and Older, until another order is chosen. Text sorts by the current culture, ignoring case and with "item 2" before "item 10"; on a Linux machine with no ICU, by code point, with numbers as text. What a row leaves blank, or a time it does not have, sorts last either way; `TableSorting.cs` holds the comparison. A sort only arranges what is there: the selection, the pane, what the search or filter kept and the audit chain are untouched. View › Reset list order brings back the list's own order, the latest changed first on Items and the latest deleted first on Trash. `[[window]]` keeps a list's order once it differs. A project's table has no sort, and Activity reads newest first.
15. **The pointing hand means a link**: `Button.link` and nothing else, a switch, a radio button, a row and a value that copies included.
16. **A tooltip says what its control does**, and where a field takes a grammar of its own, what an agent can see or a token's scope, a Learn more beside it explains it in the app.
17. **Scroll bars follow the system's setting**: macOS's scroller style, Windows' Always show scrollbars and GNOME's overlay scrolling, read again as a window comes forward. Another Linux desktop has no standard for it, and they hide until scrolled.
18. **Pressing a value copies it**, a secret included. The value shows a copy glyph while pointed at or focused and a check for about a second after, is reached by Tab, answers Enter and Space, and its accessible name is its label's words, never the value. The clipboard clears as every secret's does. Reveal stays a separate toggle on a secret's row (rule 9, [THREATS](../THREATS.md) T-25).
19. **An item is edited where it is read**: Edit turns the read view into inputs in the same positions, with Cancel and Save in the item's header; a draft is kept as rule 4 says.

## The marks

The icon is "k." and the wordmark "keypaste.": the letters in ink, `#F2F2F0` on dark and `#111214` on light, and the square dot amber, sized from the letters' stem and set close after the last one. They are two marks, never set side by side: the lock and first-run screens and every icon carry the "k." alone, a prompt carries the wordmark alone, and the unlocked window carries neither.

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
3. **The icon's floor is 16px and the wordmark's is 88px wide, or 64px in a prompt's header, where it names the asker rather than the product.** At 32px or smaller, use the favicon.
4. **The filled accent appears once per view**, for the primary action or a live signal, and carries the text its derivation gives it.
5. **Machine-readable text is the system's monospace**, everything else its interface face.
6. **Depth is lightness.** Shadows belong to overlays only.
7. **Every allow says how long it lasts**, in the interface and in the copy about it.

## Checking a screen

`KEYPASTE_SCREENS_OUT=<dir> bash scripts/verify.sh test Keypaste.App.Tests.Rendering.ScreenRenderer` draws every screen with a demo vault in each palette, and in dark and light at 150% and 200%; a screen is checked against this page and the principles' tests, and a frame fails if it holds more than one filled button in the accent its palette derives. Without a folder, the same class still walks every frame at each zoom and fails a control or a line of text cut off with no scrolling that reaches it. `Keypaste.App.Tests.TokenContrastTests` holds every token to its contrast in each palette, and the accent derived from blue, purple, pink, red, orange, yellow, green and graphite.
