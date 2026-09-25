# Handoff: keypaste — desktop app, CLI, brand system

## Overview
keypaste is an open-source, KDBX-compatible secrets manager for developers and AI agents. Core ideas:
- **MCP server**: AI clients (claude-code, cursor, …) request secrets over MCP stdio. A human approves each request (deny / allow once / allow for 1h), or a time-boxed **grant** covers it.
- **Inject-only**: agents see key *names*, never values. Values are injected into a child process env (`keypaste run -p dev -- cmd`).
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
**Color, dark by default**
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
- Wordmark: "keypaste", always lowercase, Instrument Sans 600, −0.045em.

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
  - Search, centered, `min(440px,100%)` × 28, radius 6, bg `#111214`, border `#26282C`, 12.5px muted. Placeholder "Search secrets". ⌘K keycap is mono 10.5 on `#24262A`, radius 4.
  - Right side: status "● acme.kdbx · saved" (mono 11, green dot 6px) and a "lock" button (26h, `#24262A`, radius 6).
- **Sidebar**, 232px (`#18191C`, right border), padding 16/10, gap 20:
  - Lockup: 22px mark + 19px wordmark.
  - Nav rows: 34h, radius 7, 13.5/500. Icon 16px is muted, or amber when active; active bg `#2A2C30`. Count on the right is mono 11 (amber for an Agents count > 0). Items: Secrets, Agents, Activity, Env profiles, Sharing.
  - "PROJECTS" label (11/500, +0.04em) and project rows: 30h, mono 12, with count.
  - Bottom: "Import .kdbx" (32h, 1px dashed `#3A3D43`), then the MCP server card (bg `#111214`, border, radius 8, padding 10) reading "MCP server ● running" and "stdio · 3 clients".

**1. Secrets**: columns `minmax(240px,34%) | 1fr`.
- List header: project path (mono 13), profile badge "dev" (20h, `#24262A`, radius 4, mono 11), and an amber "New" button (28h). Below it a filter field (30h).
- Rows: 52h, radius 7, grid `18px | 1fr | auto` over two lines.
  - Line 1: icon, key (mono 13, ellipsis), and "last used" (mono 11) with a 6px dot. The dot is amber for in use, green for recently granted, `#55575C` for idle.
  - Line 2: kind (11.5 muted).
  - Hover bg `#1D1E22`. Selected: bg `rgba(242,181,68,.10)`, `box-shadow: inset 2px 0 0 #F2B544`, amber icon.
- Detail (padding 28/32, gap 24, max-width 820):
  - Key (mono 22) and path "Kind · acme.kdbx › acme › api › dev". Actions: Copy, Rotate, ⋯ (30h, `#2A2C30`).
  - **Value** field (40h, bg `#111214`, border `#2C2E33`): shows 24 masked • characters, with a Reveal/Hide toggle.
  - **Reference** (36h, `#18191C`): `kp://acme/api/dev/KEY`, plus the helper line "Use this in .env.keypaste with keypaste run, or in an agent's run tool when the bridge allows it…".
  - Two cards: Agent access, and Profiles (dev set / staging set / prod "approval required" in amber).
  - Metadata rows (140px label column): Created, Rotated, KDBX entry.

**2. Agents**: padding 28/32, max-width 1080.
- **Active grants** table: agent (13.5/600), secrets · scope (mono 12), time left (mono 11 amber) over a 3px progress bar, and "Revoke" (danger-tint button). Revoke removes the row and shows a toast.
- **MCP clients** cards (auto-fill, min 240; `#18191C`; radius 10):
  - 32px initials tile, name and client, status dot.
  - POLICY select with three options: "Ask every time", "Session grants up to 1h", "Inject only".
- **Scoped tokens** table with columns NAME, TOKEN, SCOPE, MODE (outlined chip), EXPIRES. "New token" link.

**3. Activity**
- Segmented filter: All / Agents / You / Denied.
- Table columns `64 | 110 | 100 | 1.6fr | 1fr | 100`, min-width 680, scrolls horizontally: TIME (mono muted), ACTOR, ACTION, SECRETS (mono), WHERE (mono muted), RESULT (colored dot + label: Granted / Approved 1h = ok, Denied = danger, Token = info, Expired / Link = muted).
- Subtitle: "Stored inside the vault, signed, append-only."

**4. Env profiles**
- Header actions: "Import .env", "Export .env.keypaste".
- Matrix with columns KEY | DEV | STAGING | PROD (shield icon on PROD). Cells: "••••••" (set), "differs" (amber), "missing" (danger).
- Below it, two panels:
  - `.env.keypaste` preview (terminal style) with a dev/staging/prod segmented toggle that rewrites the references.
  - "Run with this profile" card showing the command `keypaste run -p <profile> -- npm start` plus explanatory copy.

**5. Sharing**: left column is the share list (what, recipient · rule, status dot). Right column is the "New share link" form:
- What (select) and Recipient.
- Expires segmented control: 1h / **24h** / 7d. Views: **1** / 3 / 10.
- "Require a passphrase, sent separately" checkbox.
- Primary button "Create and copy link".

**Overlays**
- **MCP approval dialog** (the key moment), 460 max, radius 14, `#1D1E22`, border `#3A3D43`:
  - Bot tile, "claude-code wants 2 secrets" (16/600), "via MCP · ~/acme/api · profile dev", and an amber countdown "0:28" with a haloed dot.
  - Tool call box (mono 11.5): "tool: keypaste.run / npm run migrate".
  - Secrets list with "inject only" tags.
  - Explainer text, then the buttons Deny (ghost) · Allow once (secondary) · **Allow for 1 hour ⏎** (primary, right-aligned).
- **KDBX import dialog**:
  - File card: "acme.kdbx · KDBX 4.1 · Argon2id · 142 entries · key file + password · Decrypted".
  - Group → project mapping rows.
  - Toggle: "Keep editing this file in place, so KeePassXC and other KDBX apps stay in sync".
  - Buttons: Cancel / "Import 142 entries".
- **Lock screen**: full-window with a 16px hairline grid background.
  - 56px mark, "acme.kdbx is locked", "Agents are paused until you unlock."
  - Focused password field with an amber caret, amber "Unlock" button, and "Touch YubiKey 5C to unlock".
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
- `assets/keypaste-mark.svg`: primary mark (paper stem `#F2F2F0` + amber arm).
- `assets/keypaste-mark-mono.svg`: one-color, `currentColor`.
- `assets/keypaste-app-icon.svg`: app icon; `#1D1E22` tile, rx 15/64.
- `assets/keypaste-favicon.svg`: for 16px and below; amber tile with ink glyph.
- Mark geometry on a 64u grid: stem `rect(10,8,10,48)`; arm `polygon(40,24 54,24 38,40 54,56 40,56 24,40)`; the 4u gap is never closed.
- Icons: **Lucide**, 1.5px stroke (the prototype uses the `lucide-static` icon font). Use `lucide-react` or equivalent. Sizes: 16px lists, 14px fields, 20px sidebar.
- Fonts: Instrument Sans and Fragment Mono (Google Fonts, OFL). Self-host them in the app.

## Files
- `design/keypaste Desktop.dc.html`: interactive desktop prototype
- `design/keypaste CLI.dc.html`: terminal designs
- `design/keypaste Brand System.dc.html`: logo, color, type, components, voice
- `design/support.js`: runtime needed to open the .dc.html files
- `tokens/styles.css` (+ `tokens/tokens/*.css`): CSS custom properties, ready to drop in
- `BRAND.md`: brand and voice guide · `SKILL.md`: Claude Code skill for keeping future work on-brand
