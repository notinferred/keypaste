# Handoff: keypaste — desktop app, CLI, brand system

## Overview
keypaste is an open-source, local password manager on a KeePass database, for people who keep their passwords in KeePass and write software ([PRODUCT](../PRODUCT.md)). This handoff describes the prototypes: `keypaste Desktop.dc.html` draws the app as the decisions in force on 2026-10-21 have it, and the CLI and brand-system prototypes are older. Core ideas:
- **Agents**: an agent in an AI tool (Claude Code, Cursor, Codex, …) asks keypaste for an item over MCP. You answer each request in its own prompt window (Deny, Allow once, Allow for 1 hour), an **allowance** with a duration covers it, or that agent's settings make it Automatic.
- **Inject first**: agents list item *names*; a value reaches an agent only through a request you allow, and `keypaste run -p dev acme-api -- cmd` injects values into a child process instead.
- **Projects**: an item's `env:` tags put it in a project, with a value for each environment (dev / staging / prod); no group makes a project (D-0416). A protected environment such as prod is asked about every time.
- **.env import/export**: export writes `.env.keypaste` containing **references only** (`KEY=kp://project/environment/KEY`), which is safe to commit.
- **Sharing**: end-to-end encrypted, expiring, view-limited links; the key lives in the URL fragment.
- **Scoped tokens**: for CI and scripts, e.g. `read:acme-api/staging/*`, inject-only, with expiry.
- **KeePass database**: open a .kdbx and keep editing it in place, so it stays compatible with KeePassXC and other KeePass apps, or import its groups into this vault's groups.

Surfaces: the **desktop app** (Avalonia, `src/Keypaste.App`) and the **CLI**.

## About the design files
The files in `design/` are **design references built in HTML**: prototypes of the intended look and behavior, not production code. The app rebuilds them in Avalonia from the brushes and classes in `src/Keypaste.App/Theme/`.

To view them, open any `design/*.dc.html` in a browser (`support.js` must sit next to it; fonts and icons load from CDNs). All styling is inline, so read exact values straight from the markup. Demo data and handlers are in the `<script data-dc-script>` class at the bottom of `keypaste Desktop.dc.html`.

## Fidelity
**High fidelity.** Colors, type, spacing, radii and copy are final. Build pixel-accurate using the tokens in `tokens/`.

## Design tokens (see `tokens/*.css`)
**Color (the dark palette; the app follows the system, and BRAND gives the light one)**
- Inks: `#111214` app bg · `#18191C` panel/sidebar/titlebar · `#1D1E22` card/dialog · `#24262A` hover · `#2A2C30` active/secondary button · `#33363B` secondary hover
- Lines: `#26282C` subtle · `#2C2E33` field border · `#3A3D43` strong/dialog border
- Text: `#F2F2F0` primary · `#C9CACD` secondary · `#8E9096` muted · `#55575C` disabled
- Accent amber: `#F2B544` · hover `#F7C566` · press `#D99D2E` · tint `rgba(242,181,68,.10–.12)`. Text on amber is always `#111214`.
- Status (same oklch L/C): ok `oklch(0.80 0.13 155)` · danger `oklch(0.74 0.14 25)` (bg tint `rgba(240,120,110,.12)`) · info `oklch(0.78 0.10 245)` · warn = amber
- Terminal bg: `#0C0D0E`, header divider `#1D1E22`
- Light theme overrides live under `[data-theme="light"]` in colors.css.

**Type**
- UI: Instrument Sans 400/500/600. Mono: Fragment Mono 400 for anything machine-readable (keys, values, refs, paths, commands, tokens, timestamps).
- Scale: display 54/600, −0.045em · h1 24–30/600, −0.03em to −0.035em · dialog title 15–16/600, −0.01em · body 13–14/400, line-height 1.5 · label 12/500 · table header 11/500, +0.04em, uppercase, muted · mono 12–13.
- Marks: the icon "k." and the wordmark "keypaste.", always lowercase, outlined from Hepta Slab; [BRAND](../BRAND.md) owns them.

**Space / shape**
- 4px base: 2, 4, 6, 8, 12, 16, 20, 24, 32, 40, 56.
- Radii: chip/badge 4 · field 6 · button 7 · card 10 · dialog 14 · app icon 15/64.
- Shadows appear only on overlays: dialog `0 24px 64px rgba(0,0,0,.55)`; toast/popover `0 8px 24px rgba(0,0,0,.35)`.
- Modal backdrop: `rgba(8,9,10,.6)` + `backdrop-filter: blur(3px)`.
- Focus: `box-shadow: 0 0 0 2px #111214, 0 0 0 4px #F2B544`. Focused field: 1px amber border + `0 0 0 3px rgba(242,181,68,.15)`.
- Motion: 120ms (hover) / 200ms (overlays), `cubic-bezier(.2,.8,.2,1)`, no bounce.

## Screens / views (desktop, `keypaste Desktop.dc.html`)

**App shell**: full window; grid rows `40px / 1fr`.
- **Titlebar** (`#18191C`, bottom border `#26282C`): columns `232px | 1fr | auto`.
  - Window buttons are the platform's own (D-0508): macOS draws its traffic lights over the empty first column, above the sidebar, and Windows and Linux draw their caption buttons at the right, past a 140px inset the titlebar leaves them. The prototype draws neither and leaves the first column empty.
  - Search, centered, `min(440px,100%)` × 28, radius 6, bg `#111214`, border `#26282C`, 12.5px muted. The app's only search (D-0374). Placeholder "Search all items", or "Search in this group" with the group as a chip (clearable) inside the field. ⌘K keycap is mono 10.5 on `#24262A`, radius 4.
  - Right side: status "● acme.kdbx · saved" (mono 11, green dot 6px) and a "lock" button (26h, `#24262A`, radius 6).
- **Sidebar**, 232px (`#18191C`, right border), padding 16/10:
  - The wordmark alone, 104px wide and 22 above the list (BRAND: never beside the icon).
  - Four places (D-0374), one list read top to bottom: Items, with a chevron that folds the vault's group tree beneath it (D-0376: rows 28h, 13/400, folder icon 14px, 14px indent a level, a 16px chevron on a group with groups inside it); a PROJECTS heading (table-heading style, not selectable) over each env project's row (indented, mono 12, with count); then Agents; Trash and Settings quieter at the foot. `Ctrl/Cmd+1`–`4` in that order; Left and Right fold the chosen row.
  - Nav rows: 34h, radius 7, 13.5/500. Icon 16px is muted, or the text colour when active; active bg `#2A2C30`. Count on the right is mono 11, muted. Agents also carries a 6px dot, green while the app is answering agents and red while it refuses every request because its audit log won't open (D-0494). Trash and Settings are quieter: 32h, 13/500, icon 14px.
  - No Import row and no MCP server card: import is in Items' "+" menu, and what the card said is the Agents row's tooltip.
- **Back**: a screen under a place (a project's screen, Agents › History, Settings › Advanced, and Advanced's activity log, share links, scoped tokens and diagnostics) has a ghost "‹ <screen>" button above its title, naming the screen it returns to: from the activity log it returns to Advanced, and from Advanced to Settings (D-0507). The sidebar keeps the place selected.

**1. Items**, as KeePassXC's main window (D-0376): the list header, then the table over the chosen item's preview, split 2:3 by a draggable 1px line (the table at least 100px tall, the preview 140px). A new item, an edit or a comparison of two revisions takes the whole area below the header.
- List header: the chosen group's path (mono 13), or the vault's file name while every item shows; Rename group (a 14px folder-pen icon) while a group is chosen; and a "New" button (28h), amber unless a form in the view shows its own primary, that opens a menu: New item, New group, New project, Import .env, Import from another app, which opens the KeePass database dialog or the export dialog by the file picked. No filter field: the titlebar search is the only one.
- Table: column heads TITLE, USERNAME, URL, KIND, GROUP, LAST USED (table-heading style), then rows 36h, radius 7, grid `28px | 1.4fr | 1fr | 1fr | 88px | 1fr | 104px`, sharing the list's width with no sideways scroll: kind icon, title (sans 13.5/500, a variable's key mono 13) with the matched field ("username", "URL") or tag ("tag env:billing:prod") after it in a search, username and URL (mono 12, secondary; "—" in the disabled colour when empty), kind (12.5 muted), group (mono 12 muted, "—" at the top level), and "last used" (mono 11) with a 6px dot. The dot is blue for in use, green for recently used, `#55575C` for idle. The username and URL are the only field values that are columns (D-0488): a password, a custom field or the notes never is.
  - Hover bg `#1D1E22`. Selected: bg `rgba(242,181,68,.10)`, `box-shadow: inset 2px 0 0 #F2B544`, icon in the text colour.
- New item (N.4): the templates Login, API key, Database, Server and Secure note as a row of selectable chips with their icons (globe, key-round, database, server, sticky-note), then Title and Folder side by side (Folder a dropdown of the vault's groups, "Top level" first, 14px indent a level), the template's own fields, Notes, Tags as chips with an add box that suggests the vault's project tags as one is typed, and Create, the form's one amber element, beside a ghost Cancel.
- Preview (padding 28/32, gap 24, max-width 820); username, password or value, URL and notes are one row each, the label in a 96px column to the left of its value:
  - Title (sans 22/600; a variable's key in mono 22) and path "Kind · acme.kdbx › Work › acme". Actions: Copy, Rotate, ⋯ (30h, `#2A2C30`).
  - **Username** in mono, with its own Copy.
  - **Password** field, or a variable's protected field under its key in mono (40h, bg `#111214`, border `#2C2E33`): shows 24 masked • characters at 14px, with a Reveal/Hide toggle.
  - **URL**: a web address is an underlined mono link with a 13px external-link icon, in primary text, never amber, with its own Copy; any other value is plain text with a caption saying keypaste opens only web addresses (N.5).
  - **Notes**, then **Tags** as outlined chips (mono 11), with an add box that suggests the vault's project tags the item lacks as one is typed.
  - Two cards: Agent access, shown only when agents can see the item, and Environments, shown only when the item's tags put it in a project: each project and environment, "set" in ok text, or "approval required" in secondary text where an agent's settings ask.
  - Metadata rows (140px label column): Created; Expires with its local date and time to the minute, only for an item with an expiry, reading Expired in danger text once it has passed; Rotated.
  - The ⋯ menu: Edit fields, Rename or move, Share…, then, above Delete, the reference of an item in a project (`kp://acme-api/dev/KEY`, mono 12, its helper as a tooltip), the KeePass item ID and Copy the reference (N.5).

**2. Agents** (D-0455, D-0456): one page read top to bottom, padding 28/32, max-width 1080, sections 28 apart. Nothing on it answers a request.
- Header: h1 "Agents" over the subtitle "Connect your AI tools and choose what each may see. A request waits for your answer in its own window, unless you set that agent to Automatic, which never covers commands."; beneath, a 6px green dot and the caption "Answering agents from this app · process 4120 · unlock 1c08…", its process and unlock in mono, while the app answers agents, else a card with a 15px amber alert-triangle saying why agents cannot reach the vault. A ghost "History" button (30h, 13px history icon) sits at the right.
- **Waiting for you**, only while a request waits: a 15px muted clock and the section heading, with "Answer in the prompt window" (caption) at the right. A table row (padding 16/14) holds the agent (13.5/600) over what it asked for (mono 12, secondary: item · field, a command, or whose settings change and where the request claims it came from), the countdown (mono 11), which is the frame's one amber, and "Show the prompt" (sm). The agent's reason is never shown here; Show the prompt brings the prompt window forward with focus on Deny and its allows disarmed for a second.
- **Allowances**: a 15px muted hourglass and the heading, with "End all" (danger, sm) at the right while any is in force; otherwise the caption "No allowance is in force, so the next request asks you." Each row: the agent (13.5/600) over item · field (mono 12, secondary), a 150px column with the time left (mono, secondary) over a thin progress bar, and "End" (danger, sm).
- **AI tools**: a 15px muted plug and the heading, the caption "Found on this computer, and any agent that asked in the last 30 days. Settings follow each one's name.", "Looking for AI tools on this computer…" while the background look runs, and, when none is found, a caption naming the tools keypaste looks for. A `clients.toml` keypaste cannot use shows a card with its problem in danger text and "Reset". One table, a row each (padding 16/14):
  - A 32px initials tile (mono 12), the name (14/600), and a status line with a 6px dot, green with ok text when the tool is Connected or the agent is in use now, otherwise grey with muted text: "Connected · in use now", "Not connected", "keypaste can't tell whether Codex has it: <reason>", "last asked 2 h ago". A caption under it gives the agent's mode and reach; an agent with no name has a hint to connect it from the list or start its bridge with `--client-label` (mono), and no Settings. A status that wraps keeps its dot on the first line.
  - "Connect" (an ordinary button, top right) on a found tool without keypaste, or one whose configuration keypaste cannot read. It opens a filled card under the row, one at a time: "Connect Claude Code" (dialog title) with a ghost close (14px x), the plan in a terminal block (mono 12), a caption, "Run it" and a ghost "Cancel"; afterwards the message, "Check the connection", the item picker and "Ask for it". Run it is primary, the frame's amber, only while nothing waits.
  - "Settings": a disclosure on every named row and on Every other agent, a 28h ghost header with a 13px chevron (right when closed, down when open). Open, it holds "Connect again" and "Disconnect" (danger) for a tool shown Connected, Disconnect alone for one whose configuration keypaste cannot read, then the agent's settings panel. Any number can be open, and a refresh keeps them.
  - Order, as Agents opens: agents in use now, then the most recently asked, then found tools not yet used (connected first, in catalogue order), then **Every other agent**, `*` in its tile, whose settings every unlabeled agent and every name without its own gets. While it stays open a row keeps its place, so nothing moves under the pointer or takes the focus from an open panel, and a row that appears joins after the one it follows in that order.
- **Agent settings** panel (inside a row's Settings; D-0443): the title "Settings for <agent>" and a caption saying the settings follow that name, then rows with a 140px label column. Mode (Ask every time, Ask, allow for a while, Automatic, as chips), Give values, May run commands, Can see (All projects or Only these, the picks applied with Apply as one change, and "Advanced: patterns"), Project rows (each with its mode, its limit and Remove, then "Add a row") and Limits (per hour, until a day and time, Apply), each row of controls wrapping so that at the narrowest window nothing passes the table's edge; Every other agent's panel offers no Automatic and no commands. The panel holds no primary: each choice is one change, applied at once when it gives the agent nothing more, otherwise confirmed in the prompt window. "Use the vault's settings" appears beside a line naming what this computer's ceilings hold back.
- **Since you unlocked**: a 15px muted history icon and the heading, a caption counting the records, and the records as `keypaste log` prints them in a terminal block (mono 12, at most 360 tall, scrolling).
- The first level, with every disclosure and pane closed, holds at most twelve kinds of control, a control repeated on every row counted once.

**3. Activity log** (Settings › Advanced; Agents › History is the same table held to Agents, with no filter and no Verify chain)
- A ghost "Verify chain" and the segmented filter: All / Agents / You / Denied.
- Table columns `64 | 110 | 100 | 1.6fr | 1fr | 100`, min-width 680, scrolls horizontally: TIME (mono muted), ACTOR, ACTION, SECRETS (mono), WHERE (mono muted), RESULT (colored dot + label: Allowed / Approved 1h = ok, Denied = danger, Token = info, Expired / Link = muted).
- Subtitle: "Every agent request, allowance, token run and share. Kept on this machine, hash-chained, append-only." History's: "What agents and tokens asked for, and what came of it. Kept on this machine, hash-chained, append-only."

**4. Projects** (D-0417), opened from a project's row in the sidebar
- Title: the open project's name, sans 24/600, which is also its picker; "Projects" when none is open. Header actions: "Import .env", "Export .env.keypaste".
- A card listing each tag that starts `env:` and puts its item in no project, muted icon, no amber.
- Matrix with columns KEY | DEV | STAGING | PROD (a muted shield icon on PROD), and "+ ENVIRONMENT". Cells: "••••••" (set), "differs" (secondary), "missing" (danger), and under a value its item and the other environments that item serves (mono 11, muted).
- Items: a table per environment (the prototype shows the one its environment toggle picks), its name as the table heading with "Add item", each row the item's path, its keys, "also staging" (mono 11, muted) and a Remove action shown on hover. Add item opens a form with a filter and a list of items; choosing one, or Remove, shows what the tag change reaches, with "Add to staging" as the primary or "Remove from dev" in danger, and "Keep it".
- Below it, two panels:
  - `.env.keypaste` preview (terminal style) with a dev/staging/prod segmented toggle that rewrites the references.
  - "Run with this environment" card showing the command `keypaste run -p <environment> <project> -- npm start` plus explanatory copy.

**Scoped tokens** (Settings › Advanced): a table with columns NAME, TOKEN, SCOPE, MODE (outlined chip), EXPIRES. "New token" is the screen's primary, and the form's Create token, then the minted token's Copy, take its place while shown.

**Diagnostics** (Settings › Advanced): the vault, keypaste's home, `KEYPASTE_HOME` and the version, labels muted 12, values mono 12. Below, a Crash reports card (16px muted bug, 13/600 heading): a caption counting the reports waiting, or saying the switch is off, and each report as its exact JSON (mono 12, secondary, selectable, wrapping) with a ghost sm Delete at its right, rows split by hairlines (D-0594).

**5. Sharing**: Settings › Advanced › Share links is the share list (what, recipient · rule, status dot, Revoke or Remove) with "Check status". The "New share link" form is the Share… dialog an item's ⋯ menu opens, titled "Share <item>":
- What (one of the item's fields) and Recipient.
- Expires segmented control: 1h / **24h** / 7d. Views: **1** / 3 / 10.
- "Require a passphrase, sent separately" checkbox.
- Primary button "Create and copy link".

**Settings**: h1 "Settings" over "Saved on this machine, and in force as soon as you change them.", max-width 760, cards 20 apart (radius 10, panel fill, a 16px muted icon and a 13/600 heading, rows 20/14 split by hairlines): Recommendations, Security, Startup, Appearance, Vault access, Backups, Export, Agents and the CLI, Browser fill (a globe icon: the row "Pair a browser" with its caption and a secondary Pair a browser, which while armed gives way to a ghost Stop and a line counting down "Pairing is on for 1:58. In KeePassXC-Browser, press Connect, then answer keypaste's window."; then "Paired browsers", each pairing's name 13.5/500 over the build in mono 12 secondary and "Paired <date> · last used <time>" in caption, with a ghost sm Remove, or "No browser is paired with this vault."; D-0634), Recent vaults, Crash reports (a bug icon, the Send crash reports switch and its caption), then one row, "Advanced" with its caption and Open, which opens Settings › Advanced (D-0507). No amber.

**Settings › Advanced**: h1 "Advanced" over "What the everyday screens leave out, each on a screen of its own.", then one card with a row each for Activity log, Share links, Scoped tokens and Diagnostics: the name (13.5/500), a caption, and Open, which opens that screen under Advanced.

**Trash**: h1 "Trash" with Restore and Delete for good (danger) at the right, and "Select an item to restore or delete it" until one is chosen; a table NAME, DELETED FROM, DELETED, its chosen row marked as Items marks one.

**Overlays**
- **Credential prompt** (D-0326), a fixed-layout window of its own, 508 wide, radius 14, `#1D1E22`, border `#3A3D43`; every block has a fixed height, so nothing the agent writes moves the question, the item or the buttons (T-2):
  - Bot tile, "claude-code wants 1 secret" (16/600), "via MCP · label claude-code" (12.5 muted), and a countdown "0:28" (mono 11 secondary) with a muted dot.
  - Tool call box (mono 11.5): "tool: keypaste.request_credential" over the item, `Work/github`.
  - The field, `password`, tagged "value goes to the agent", then "You can allow it once, or for 1 hour".
  - "The agent says it needs this because" over the reason in a box that scrolls, then "That sentence was written by the agent, not by keypaste. Treat it as a claim." and the warning that allowing returns the value itself to the agent.
  - Deny (ghost) · Allow once (secondary) · **Allow for 1 hour** (primary, right-aligned, the prompt's one amber element). No button is the default, so Enter allows nothing: focus starts on Deny, drawn with the focus ring, Escape denies, and both allows stay disarmed for their first second on screen and for a second after Agents brings the prompt forward (D-0456).
- **Pairing confirmation** (D-0634), the credential prompt's fixed-layout window, shown only while the person armed pairing: a globe tile, "A browser asks to pair" over "You turned on pairing in Settings" and the countdown; the call box holds the build the browser named, "KeePassXC-Browser from the Chrome Web Store (oboonakemofpalcgghocfoadofidjkkk)"; "Name this pairing, as the browser will list it" over a field holding "Chrome on <computer>" and, in danger 12, why a name cannot be used; the note that any program can name the build, so allow only a pairing you started; Deny (ghost) · **Pair** (primary, the window's one amber). Focus starts on Deny, Enter does nothing, and Pair is armed a second after the window appears and only for a usable name.
- **Not-paired notice**, after a request to pair while pairing was off: a globe tile, "Not paired" over the build, "A browser asked to pair. To pair it, press Pair a browser.", and Open Settings beside Close, the default. One at a time.
- **KeePass database import dialog** (D-0356, D-0416), from Items' New menu:
  - File card: "old-work.kdbx" over "KeePass database 4.1 · Argon2id · 142 items · key file + password", and "Decrypted" in ok text.
  - Before it is decrypted: the password field, the key-file line and, where the app reaches YubiKeys, a line like it, "No YubiKey" with "Use a YubiKey", or "YubiKey, slot 2" (mono 12) with "Use slot 1" and "Remove". While the key waits, the amber waiting dot, "Touch your YubiKey" and Cancel, with Unlock secondary.
  - "Map groups into this vault": a row for each source group, with a checkbox that includes it (strong border, the check in the text colour), its path and item count (mono 12 and 11), an arrow, and the group of this vault it is copied into, an editable field (mono 12) that is disabled while the row is left out. Under the rows, what no import copies: "Not copied: Recycle Bin (12 items)". Groups map only to groups: a project comes from the items' own tags.
  - The switch "Keep editing this file in place, so KeePassXC and other KeePass apps stay in sync" (the track in the text colour when on). On, the rows give way to a note that the open vault locks and this file opens as the vault, with nothing copied.
  - Buttons: Cancel / "Import 68 items", counting the included rows, or "Open old-work.kdbx" when the file is kept in place.
- **Export import dialog** (D-0591, D-0592), the same 520-wide dialog for another app's export, titled "Import another app's export":
  - File card: "bitwarden_export.json" over "Bitwarden JSON · 3 items", and "Not encrypted" (muted, lock-open icon); a refused file says why in danger text with "Can't read", and offers Choose another file….
  - "Copy into this group", a mono field holding the file's name; for names the vault already has, "1 name your vault already has" over a segmented Keep both / Skip them and a caption that nothing is replaced; "Where each item lands", rows of the export's path (mono 12 secondary), an arrow and where it lands or "skipped" (mono 12), with a muted note under a renamed one; "Not imported (3)", each item's path over its part and why.
  - Buttons: Cancel / "Import 3 items". Once imported: a green check and "Imported 3 items into bitwarden_export", the Not imported list, a card "Delete bitwarden_export.json now" saying it holds your passwords unencrypted and keypaste does not delete it, with the file's path in mono, and Done, the dialog's one amber element.
- **Lock screen**: full-window with a 16px hairline grid background.
  - The "k." icon 56px tall, "acme.kdbx is locked", "Enter its master password to open it." It says nothing of agents (N.2).
  - Focused password field with an amber caret, amber "Unlock" button, the key-file line, then "More options", which reveals "Unlock with a YubiKey too"; a vault that last opened with a YubiKey shows it directly. While the key waits for a touch, its waiting dot is the amber element and Unlock is secondary.
  - On Windows, a vault Windows Hello can open again: "Unlock it with Windows Hello or its master password." under the heading, and an amber "Unlock with Windows Hello" (15px fingerprint icon) above the password field, with Unlock secondary (D-0553).
  - With no vault selected, the welcome: "Open a vault", then, when KeePassXC has opened databases here, a KEEPASSXC LAST OPENED list (table-heading style, rows as the recent list's, file name in mono with the path in a tooltip) above "Open another file…" and "Create a new vault…", neither amber; with nothing from KeePassXC, the amber "Open a vault…" and "Create a new vault…".
- **First run** (D-0594), full-window before the welcome, a 520px column under the 56px mark: "Before you start" (22/600) over a 13px secondary line, the terms of use in a 300px filled card that scrolls (title 14/600, its dated line muted 12, headings 13/600, paragraphs 12.5 secondary), and the amber Continue, wide, which has the focus; then "Crash reports", what a report holds and never holds, a muted line that Settings changes it, and Send crash reports and Don't send side by side, equal and secondary, neither the default.
- **Toast**, bottom-right 20px: `#24262A`, border `#3A3D43`, radius 10, green check icon, auto-dismisses after 2.8s. A copy's toast says "<field> copied." with "Clears in 20s" (muted) and a Clear now link, behind a clipboard-check icon.

## CLI (`keypaste CLI.dc.html`)
Rules: 80 columns; amber = needs you (prompt `›`, commands in help, pending boxes, cursor); green ✓ = done; red = refused or missing; grey = everything else. `--json` for piping. Values are never printed without `--reveal`. See the file for exact output of:
- `run`
- `approve` (boxed approval with the key hints [d] deny · [o] once · [h] 1 hour, plus a countdown)
- `grants`, `grants revoke`
- `env export` / `env diff`
- `share`, `import`, `token create`
- `--help`, with commands grouped as SECRETS: get, set, run, env · AGENTS: mcp, grants, token, log · VAULT: import, share, lock

## Interactions & state (prototype)
State: `view` (the screen; one under a place names its parent, where Back goes), `group` (the group Items shows), `sel` (the chosen item), `reveal`, `newOpen`, `menuOpen`, `approval` and `armed`, `waiting`, `allowances[]`, `open` and `modes` (each agent's Settings), `importOpen`, `inPlace`, `include` (the import's rows), `shareOpen`, `locked`, `moreOptions`, `project`, `env`, `filter`, `theme`, `trashSel`, `toast`.
- The credential prompt opens 1.6s after load, and Show the prompt on Agents opens it again; its allows do nothing for its first second.
  - Deny → toast "Denied claude-code".
  - Allow once → toast "Allowed claude-code once".
  - Allow for 1 hour → adds or refreshes a 60-minute allowance, then shows a toast.
  - Real app: no button is the default, so Enter allows nothing; Escape denies, and an unanswered prompt is denied at its deadline, 45 seconds by default.
- The sidebar: Items' chevron folds the group tree and Work's folds its child; a group shows its items, a project's row opens Projects, and Trash and Settings sit at the foot.
- Choosing an item resets `reveal` to false. Copy shows the clipboard toast; the real app **clears the clipboard after the product's clipboard window (20 s)**. Connect and Rotate show a toast where the app opens a pane.
- Settings' Advanced row opens Settings › Advanced, whose rows open the activity log, share links, scoped tokens and diagnostics; Back goes up one screen at a time.
- Lock hides everything and pauses agents; unlocking (password or hardware key) restores the app.
- The environment toggle on Projects rewrites the `.env.keypaste` preview, the environment's items and the run command.
- The import's checkboxes recount its button, and its switch turns the button into "Open old-work.kdbx".

## Assets
- The marks, the icon "k." and the wordmark "keypaste.", are in [`assets/brand/`](../../assets/brand), and [BRAND](../BRAND.md) says which file serves which use. The desktop prototype draws them from those outlines; the CLI and brand-system prototypes still draw the earlier monogram.
- Icons: **Lucide**, 1.5px stroke. The prototype uses the `lucide-static` icon font; the app draws the same icons with `KpIcon` from `src/Keypaste.App/Theme/Icons.axaml`, which `scripts/lucide-to-axaml.py` generates. Sizes: 16px for the sidebar's places and 14px at its foot, 15px in the item list.
- Fonts: Instrument Sans and Fragment Mono (Google Fonts, OFL). The prototype loads them from Google Fonts; the app carries them in `src/Keypaste.App/Assets/Fonts`.

## Files
- `design/keypaste Desktop.dc.html`: interactive desktop prototype
- `design/keypaste CLI.dc.html`: terminal designs
- `design/keypaste Brand System.dc.html`: logo, color, type, components, voice
- `design/support.js`: runtime needed to open the .dc.html files
- `tokens/styles.css` (+ `tokens/tokens/*.css`): CSS custom properties, ready to drop in
