# keypaste — brand + design system

keypaste is an open-source, local password manager on a KDBX file, for people who keep their passwords in KeePass and write software. It serves secrets over MCP with human approval, time-boxed grants and inject-only access; runs commands with env profiles (`keypaste run`); imports and exports `.env` files; and shares secrets through encrypted, expiring links. Surfaces: the desktop app and the CLI.

The interface is built on a Swiss-precision grid. The marks, chosen on 2026-09-29, are an icon "k." and a wordmark "keypaste." in Hepta Slab with an amber dot ([BRAND](../BRAND.md)).

## Index
- `keypaste Brand System.dc.html`: logo, color, type, space, components, icons, voice
- `keypaste Desktop.dc.html`: interactive desktop prototype (secrets, agents and grants, activity, env profiles, sharing, MCP approval, KDBX import, unlock)
- `keypaste CLI.dc.html`: terminal output design
- `styles.css` imports `tokens/typography.css`, `tokens/colors.css` and `tokens/spacing.css`
- `assets/`: `keypaste-glyph-{dark,light,mono}.svg`, `keypaste-wordmark-{dark,light,mono}.svg` (mono takes currentColor), `keypaste-app-icon.svg`, `keypaste-favicon.svg`

## Content fundamentals
Plain and calm; precise where it protects you. Name the actor and the object ("claude-code wants DATABASE_URL"). Always give a duration ("Allow for 1 hour", never "Trust"). Use sentence case, "you" for the person, and the agent's real client name for the agent. No exclamation marks or emoji. Errors say what to do next. The wordmark is always lowercase.

## Visual foundations
- **Color:** follows the system's light or dark setting. Inks #111214 to #2A2C30 are layered by lightness; borders are #26282C, #2C2E33 and #3A3D43; text is #F2F2F0, #C9CACD and #8E9096. The single accent is amber #F2B544 (hover #F7C566, press #D99D2E), used once per view for the primary action or a live signal. Text on amber is always ink. Status colors share one oklch lightness and chroma: ok 155°, danger 25°, info 245°, and warn is amber. A light theme is available via `[data-theme="light"]`.
- **Type:** Instrument Sans for UI (display 54/600 at −4.5% tracking; h1 30; h2 20; body 14; label 12/500). Fragment Mono for anything machine-readable: keys, values, refs, commands, tokens.
- **Space:** 4px base on a 16px grid. Radii are 4 for chips, 6 for fields, 7 for buttons, 10 for cards and 14 for dialogs.
- **Depth:** lightness, not shadow. Shadows appear only on overlays (dialogs, toasts, popovers). Modal backdrops are rgba(8,9,10,.6) with a 3px blur.
- **Backgrounds:** flat ink. The 16px hairline grid is reserved for brand moments (cover, lock screen).
- **States:** hover raises one ink step; selected rows get a 10% amber tint and a 2px amber inset bar; focus is a 2px ink gap plus a 2px amber ring. The waiting state is an amber dot with a 3px halo.
- **Motion:** 120–200ms, ease cubic-bezier(.2,.8,.2,1). No bounces.

## Iconography
Lucide (CDN icon font, `lucide-static`) at a 1.5px stroke: 16px in lists, 14px in fields, 20px in the sidebar. Icons are muted grey by default and turn amber only when their object is live. Unicode is used in the CLI (✓ › ┌ │ └). No emoji, no filled icons.

## Logo
Two marks, never set side by side: the icon "k." and the wordmark "keypaste." in Hepta Slab, the wordmark at weight 500 and the icon at 580, drawn only as outlines. The letters are ink and the square dot amber, or the ink's color in one-color use. Minimum sizes: 16px for the icon, 88px wide for the wordmark. At 32px or smaller, use the favicon: the dark tile with a larger "k.". The prototypes still draw the earlier monogram; BRAND and `assets/` are the reference for the marks.
