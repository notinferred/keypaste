# Brand

Owns the marks, the palette, the type, the shape, the voice and the usage rules. Every surface (desktop app, CLI, site, README, store listing, installer) takes its visual values from here. The design handoff in [docs/design](design/README.md) holds the exact values for every screen and the high-fidelity prototypes they come from; where this page and a prototype disagree, the prototype's markup is the reference for that screen and this page for everything else, including the marks, which the prototypes still draw as the earlier monogram. Where a value also has to exist as code, the file named below holds it.

**Status:** adopted 2026-09-24. Amended 2026-09-28: the theme follows the system, titles use the sans face and the voice is calmer. Amended 2026-09-29 by the founder: the monogram and its lockup give way to an icon and a wordmark in Hepta Slab with an amber dot, never set together, and the status colours gain light-ground values; later that day the founder chose a lighter cut of both marks. The desktop app's theme, icon, shell and screens, keypaste.com (through [brand.css](../site/public/brand.css)) and the README's wordmark and screenshots are built from it as amended ([N.7](steps/N.7.md)), and the CLI follows the terminal rules below.

## Colour

The app follows the system's light or dark setting, and both palettes are first-class. Depth is lightness, not shadow.

| Role | Dark | Light |
|---|---|---|
| App background | `#111214` | `#F7F7F5` |
| Panel, sidebar, title bar | `#18191C` | `#FFFFFF` |
| Card, dialog | `#1D1E22` | `#FFFFFF` |
| Hover | `#24262A` | `#EFEFEC` |
| Active, secondary button | `#2A2C30` (hover `#33363B`) | `#E6E6E2` (hover `#DDDDD8`) |
| Lines: subtle, field, strong | `#26282C`, `#2C2E33`, `#3A3D43` | `#E8E8E4`, `#DCDCD7`, `#C4C4BE` |
| Text: primary, secondary, muted | `#F2F2F0`, `#C9CACD`, `#8E9096` | `#111214`, `#3A3C40`, `#6B6D72` |
| Text: disabled | `#55575C` | `#A3A5A9` |
| Terminal | `#0C0D0E`, header divider `#1D1E22` | unchanged |

The single accent is amber `#F2B544` (hover `#F7C566`, press `#D99D2E`, tint 10–12%). It appears once per view, for the primary action or a live signal, and text on amber is always ink `#111214`. Amber used as text on a light ground is `#8A5A00`.

Status colours: ok `oklch(0.80 0.13 155)`, danger `oklch(0.74 0.14 25)` with a 12% tint behind danger buttons, info `oklch(0.78 0.10 245)`. On the light palette the same hues sit at lightness 0.50, `oklch(0.50 0.13 155)`, `oklch(0.50 0.17 25)` and `oklch(0.50 0.10 245)`, so a status read as text keeps 4.5:1 on every light surface. Warn is amber, and an idle dot is `#55575C`. The app draws them as `#72D699`, `#F7857D` and `#7FBEF3`, and on light as `#007840`, `#B02A2D` and `#296898`, because Avalonia has no oklch.

The app's values are in [Tokens.axaml](../src/Keypaste.App/Theme/Tokens.axaml); the CSS source is [docs/design/tokens](design/tokens/tokens/colors.css).

## Type

Instrument Sans 400/500/600 for the interface and Fragment Mono 400 for anything machine-readable: keys, values, references, paths, commands, tokens and timestamps. Both are under the SIL Open Font License and are vendored in [Assets/Fonts](../src/Keypaste.App/Assets/Fonts), so nothing is fetched at runtime.

| Style | Size and weight |
|---|---|
| Display | 54/600, −0.045em |
| Heading | 24–30/600, −0.03em to −0.035em; section 20/600 |
| Dialog title | 15–16/600, −0.01em |
| Body | 13–14/400, line height 1.5 |
| Label | 12/500 |
| Table header | 11/500, +0.04em, muted, written in capitals |
| Mono | 12–13. An item's title is sans at heading size in its detail pane; a variable's key stays mono |

[Typography.axaml](../src/Keypaste.App/Theme/Typography.axaml) holds these as text classes.

The marks are set in Hepta Slab, and only as outlines: no surface loads the face, and the interface never uses it. The wordmark is weight 500 tracked −1.5%, so it sits beside Instrument Sans without outweighing it; the icon is 580 tracked −1%, so it holds its weight on a tile and at 16px.

## Shape and motion

Spacing is a 4px base on a 16px grid: 2, 4, 6, 8, 12, 16, 20, 24, 32, 40, 56. Radii are 4 for chips and keycaps, 6 for fields, 7 for buttons and rows, 10 for cards and toasts, 14 for dialogs and 15/64 for the app icon.

Shadows appear only on overlays: dialogs `0 24px 64px rgba(0,0,0,.55)`, toasts and popovers `0 8px 24px rgba(0,0,0,.35)`. A modal backdrop is `rgba(8,9,10,.6)` with a 3px blur. Backgrounds are flat ink; the 16px hairline grid is kept for brand moments such as the lock screen.

Hover raises one ink step. A selected row takes a 10% amber tint and a 2px amber inset bar. Focus is a 2px ink gap and a 2px amber ring; a focused field has a 1px amber border and a 3px 15% amber glow. Waiting is an amber dot with a 3px halo. Motion is 120ms for hover and 200ms for overlays, eased `cubic-bezier(.2,.8,.2,1)`, with no bounce.

## Icons

Lucide at a 1.5px stroke: 16px in lists, 14px in fields, 20px in the sidebar. Icons are muted by default and turn amber only while their object is live. No emoji and no filled icons. The app's icons are generated into [Icons.axaml](../src/Keypaste.App/Theme/Icons.axaml) by [lucide-to-axaml.py](../scripts/lucide-to-axaml.py).

## Terminal

The CLI keeps to 80 columns. Amber means it needs you: the `›` prompt, commands in help, a pending box, the cursor. Green `✓` is done, red is refused or missing, grey is everything else. Its box and list characters are `┌ │ └ ›`. A value is never printed without `--reveal`, and `--json` is there for piping. The exact output of each command is in the [CLI design](design/design/keypaste%20CLI.dc.html).

## Voice

Plain and calm; precise where it protects you. Name the actor and the object: "claude-code wants DATABASE_URL". Always give a duration: "Allow for 1 hour", never "Trust". Use sentence case, "you" for the person and the agent's real client name for the agent. No exclamation marks and no emoji. An error says what to do next.

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
7. **Every grant names its duration**, in the interface and in the copy about it.
