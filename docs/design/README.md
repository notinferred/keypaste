# Handoff: keypaste — desktop app, CLI, brand system

## Overview
keypaste is an open-source, local password manager on a KDBX file, for people who keep their passwords in KeePass and write software ([PRODUCT](../PRODUCT.md)). This handoff describes the prototypes the current screens were built from; [ROADMAP](../../ROADMAP.md) has the simpler app that replaces parts of them. Core ideas:
- **MCP server**: AI clients (claude-code, cursor, …) request secrets over MCP stdio. A human approves each request (deny / allow once / allow for 1h), or a time-boxed **grant** covers it.
- **Inject first**: agents list key *names*; a value reaches an agent only through a request the person approves, and `keypaste run -p dev -- cmd` injects values into a child process instead.
- **Env profiles**: one key set per project, with a value per profile (dev / staging / prod). Prod always needs a live approval.
- **.env import/export**: export writes `.env.keypaste` containing **references only** (`KEY=kp://project/profile/KEY`), which is safe to commit.
- **Sharing**: end-to-end encrypted, expiring, view-limited links; the key lives in the URL fragment.
- **Scoped tokens**: for CI and scripts, e.g. `read:acme/api/staging/*`, inject-only, with expiry.
- **KDBX**: import a .kdbx and keep editing it in place, so it stays compatible with KeePassXC and other KDBX apps.

Surfaces: **desktop app** (Electron/Tauri/native; your choice) and **CLI**.

## About the design files
The files in `design/` are **design references built in HTML**: prototypes of the intended look and behavior, not production code. Recreate them in the target codebase's environment and patterns. If there is no codebase yet, a good fit is **Tauri + React + TypeScript** for desktop and **Rust or Go** for the CLI/MCP server, but pick what suits the project.

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
  - Window controls: three 12px circles `#3A3D43`, gap 8.
  - Search, centered, `min(440px,100%)` × 28, radius 6, bg `#111214`, border `#26282C`, 12.5px muted. The app's only search (D-0374). Placeholder "Search all items", or "Search in this group" with the group as a chip (clearable) inside the field. ⌘K keycap is mono 10.5 on `#24262A`, radius 4.
  - Right side: status "● acme.kdbx · saved" (mono 11, green dot 6px) and a "lock" button (26h, `#24262A`, radius 6).
- **Sidebar**, 232px (`#18191C`, right border), padding 16/10, gap 20:
  - The wordmark alone, 104px wide (BRAND: never beside the icon).
  - Four places (D-0374), one list read top to bottom: Items, with a chevron that folds the vault's group tree beneath it (D-0376: rows 28h, 13/400, folder icon 14px, 14px indent a level, a 16px chevron on a group with groups inside it); a PROJECTS heading (table-heading style, not selectable) over each env project's row (indented, mono 12, with count); then Agents; Trash and Settings quieter at the foot. `Ctrl/Cmd+1`–`4` in that order; Left and Right fold the chosen row.
  - Nav rows: 34h, radius 7, 13.5/500. Icon 16px is muted, or the text colour when active; active bg `#2A2C30`. Count on the right is mono 11, muted. Agents also carries a 6px dot, green while the app is answering agents.
  - No Import row and no MCP server card: import is in Items' "+" menu, and what the card said is the Agents row's tooltip.
- **Back**: a screen under a place (a project's Env profiles, Agents › History, and Settings › Advanced's activity log, share links, scoped tokens and diagnostics) has a ghost "‹ <place>" button above its title.

**1. Items**, as KeePassXC's main window (D-0376): the list header, then the table over the chosen item's preview, split 2:3 by a draggable 1px line (the table at least 100px tall, the preview 140px). A new item, an edit or a comparison of two revisions takes the whole area below the header.
- List header: the group or project path (mono 13), profile badge "dev" (20h, `#24262A`, radius 4, mono 11), Rename group (a 14px folder-pen icon) while a group is chosen, and a "New" button (28h), amber unless a form in the view shows its own primary, that opens a menu: New item, New group, New project, Import .env, Import .kdbx. No filter field: the titlebar search is the only one.
- Table: column heads TITLE, KIND, GROUP, LAST USED (table-heading style), then rows 36h, radius 7, grid `28px | 1fr | 112px | 1fr | 112px`: kind icon, title (sans 13.5/500, a variable's key mono 13) with the matched field ("username", "URL") after it in a search, kind (12.5 muted), group or "project · profile" (mono 12 muted), and "last used" (mono 11) with a 6px dot. The dot is blue for in use, green for recently granted, `#55575C` for idle. No field value is ever a column.
  - Hover bg `#1D1E22`. Selected: bg `rgba(242,181,68,.10)`, `box-shadow: inset 2px 0 0 #F2B544`, icon in the text colour.
- New item (N.4): the templates Login, API key, Database, Server and Secure note as a row of selectable chips with their icons (globe, key-round, database, server, sticky-note), then Title and Folder side by side (Folder a dropdown of the vault's groups, "Top level" first, 14px indent a level), the template's own fields, Notes, Tags as chips with an add box, and Create, the form's one amber element, beside a ghost Cancel.
- Preview (padding 28/32, gap 24, max-width 820); username, password or value, URL and notes are one row each, the label in a 96px column to the left of its value:
  - Title (sans 22/600; a variable's key in mono 22) and path "Kind · acme.kdbx › acme › api › dev". Actions: Copy, Rotate, ⋯ (30h, `#2A2C30`).
  - **Value** field (40h, bg `#111214`, border `#2C2E33`): shows 24 masked • characters, with a Reveal/Hide toggle.
  - **URL**: a web address is an underlined mono link with a 13px external-link icon, in primary text, never amber, with its own Copy; any other value is plain text with a caption saying keypaste opens only web addresses (N.5).
  - Two cards: Agent access, shown only when agents can see the item, and Profiles (dev set / staging set / prod "approval required" in secondary text).
  - Metadata rows (140px label column): Created, Rotated.
  - The ⋯ menu ends, above Delete, with the reference (`kp://acme-api/dev/KEY`, mono 12, its helper as a tooltip), the KDBX entry identifier and Copy the reference (N.5).

**2. Agents**: padding 28/32, max-width 1080.
- **Active grants** table: agent (13.5/600), secrets · scope (mono 12), time left (mono 11 secondary) over a 3px muted progress bar, and "Revoke" (danger-tint button). Revoke removes the row and shows a toast.
- **MCP clients** cards (auto-fill, min 240; `#18191C`; radius 10):
  - 32px initials tile, name and client, status dot, and a 30px ⋯ toggle.
  - POLICY, the policy held, as text. The ⋯ menu offers "Session grants up to 1h", "Ask every time" and "Inject only", the held one checked in the text colour; it is disabled for a client with no label.

**3. Activity log** (Settings › Advanced; Agents › History is the same table held to Agents, with no filter and no Verify chain)
- Segmented filter: All / Agents / You / Denied.
- Table columns `64 | 110 | 100 | 1.6fr | 1fr | 100`, min-width 680, scrolls horizontally: TIME (mono muted), ACTOR, ACTION, SECRETS (mono), WHERE (mono muted), RESULT (colored dot + label: Granted / Approved 1h = ok, Denied = danger, Token = info, Expired / Link = muted).
- Subtitle: "Every agent request, grant, token run and share. Kept on this machine, hash-chained, append-only."

**4. Env profiles**
- Header actions: "Import .env", "Export .env.keypaste".
- Matrix with columns KEY | DEV | STAGING | PROD (shield icon on PROD). Cells: "••••••" (set), "differs" (secondary), "missing" (danger).
- Below it, two panels:
  - `.env.keypaste` preview (terminal style) with a dev/staging/prod segmented toggle that rewrites the references.
  - "Run with this profile" card showing the command `keypaste run -p <profile> -- npm start` plus explanatory copy.

**Scoped tokens** (Settings › Advanced): a table with columns NAME, TOKEN, SCOPE, MODE (outlined chip), EXPIRES. "New token" is the screen's primary, and the form's Create token, then the minted token's Copy, take its place while shown.

**Diagnostics** (Settings › Advanced): the vault, keypaste's home, `KEYPASTE_HOME` and the version, labels muted 12, values mono 12.

**5. Sharing**: Settings › Advanced › Share links is the share list (what, recipient · rule, status dot). The "New share link" form is the Share… dialog an item's ⋯ menu opens:
- What (the item, fixed) and Recipient.
- Expires segmented control: 1h / **24h** / 7d. Views: **1** / 3 / 10.
- "Require a passphrase, sent separately" checkbox.
- Primary button "Create and copy link".

**Overlays**
- **MCP approval dialog** (the key moment), 460 max, radius 14, `#1D1E22`, border `#3A3D43`:
  - Bot tile, "claude-code wants 2 secrets" (16/600), "via MCP · ~/acme/api · profile dev", and a countdown "0:28" (mono 11 secondary) with a muted dot: the primary answer is the prompt's one amber element.
  - Tool call box (mono 11.5): "tool: keypaste.run / npm run migrate".
  - Secrets list with "inject only" tags.
  - Explainer text, then the buttons Deny (ghost) · Allow once (secondary) · **Allow for 1 hour ⏎** (primary, right-aligned).
- **KDBX import dialog**:
  - File card: "acme.kdbx · KDBX 4.1 · Argon2id · 142 entries · key file + password · Decrypted".
  - Group → project mapping rows.
  - Toggle: "Keep editing this file in place, so KeePassXC and other KDBX apps stay in sync".
  - Buttons: Cancel / "Import 142 entries".
- **Lock screen**: full-window with a 16px hairline grid background.
  - 56px mark, "acme.kdbx is locked", "Enter its master password to open it." It says nothing of agents (N.2).
  - Focused password field with an amber caret, amber "Unlock" button, the key-file line, then "More options", which reveals "Unlock with a YubiKey too"; a vault that last opened with a YubiKey shows it directly. While the key waits for a touch, its waiting dot is the amber element and Unlock is secondary.
  - With no vault selected, the welcome: "Open a vault", then, when KeePassXC has opened databases here, a KEEPASSXC LAST OPENED list (table-heading style, rows as the recent list's, file name in mono with the path in a tooltip) above "Open another file…" and "Create a new vault…", neither amber; with nothing from KeePassXC, the amber "Open a vault…" and "Create a new vault…".
- **Toast**, bottom-right 20px: `#24262A`, border `#3A3D43`, radius 10, green check icon, auto-dismisses after 2.8s.

## CLI (`keypaste CLI.dc.html`)
Rules: 80 columns; amber = needs you (prompt `›`, commands in help, pending boxes, cursor); green ✓ = done; red = refused or missing; grey = everything else. `--json` for piping. Values are never printed without `--reveal`. See the file for exact output of:
- `run`
- `mcp serve` (boxed approval with the key hints [d] deny · [o] once · [h] 1 hour, plus a countdown)
- `grants`, `grants revoke`
- `env export` / `env diff`
- `share`, `import`, `token create`
- `--help`, with commands grouped as SECRETS: get, set, run, env · AGENTS: mcp, grants, token, log · VAULT: import, share, lock

## Interactions & state (prototype)
State: `view`, `sel` (selected secret), `reveal`, `approval`, `importOpen`, `locked`, `profile`, `filter`, `grants[]`, `toast`.
- The approval dialog auto-opens 1.6s after load; clicking the MCP server card re-opens it.
  - Deny → toast "Denied claude-code".
  - Allow once → toast "Allowed once. Injected into pid 48213".
  - Allow 1h → adds or refreshes a 60-minute grant, then shows a toast.
  - Real app: Enter = primary; the dialog times out as a Deny after 30s.
- Selecting a secret resets `reveal` to false. Copy shows a toast and **clears the clipboard after the product's clipboard window (20 s)**.
- Lock hides everything and pauses agents; unlocking (password or hardware key) restores the app.
- The profile toggle rewrites the `.env.keypaste` preview and the run command.

## Assets
- The marks, the icon "k." and the wordmark "keypaste.", are in [`assets/brand/`](../../assets/brand), and [BRAND](../BRAND.md) says which file serves which use. The prototypes still draw the earlier monogram.
- Icons: **Lucide**, 1.5px stroke (the prototype uses the `lucide-static` icon font). Use `lucide-react` or equivalent. Sizes: 16px lists, 14px fields, 20px sidebar.
- Fonts: Instrument Sans and Fragment Mono (Google Fonts, OFL). Self-host them in the app.

## Files
- `design/keypaste Desktop.dc.html`: interactive desktop prototype
- `design/keypaste CLI.dc.html`: terminal designs
- `design/keypaste Brand System.dc.html`: logo, color, type, components, voice
- `design/support.js`: runtime needed to open the .dc.html files
- `tokens/styles.css` (+ `tokens/tokens/*.css`): CSS custom properties, ready to drop in
