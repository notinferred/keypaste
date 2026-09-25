# Brand

Owns the marks, the palette, the type and the usage rules. Every surface (desktop app, CLI, site, store listing, installer) takes its visual values from here. The full design handoff, with exact values for every screen, is in [docs/design](design/README.md); its [brand guide](design/BRAND.md) owns voice and component rules. Where a value also has to exist as code, the file named below holds it.

**Status:** adopted 2026-09-24 (direction 1b, the monogram). The desktop app's theme, icon and shell are built from it; the screens are being restyled onto it. The site still draws its earlier palette.

## Colour

Dark first. Inks `#111214`, `#18191C`, `#1D1E22`, `#24262A`, `#2A2C30` are layered by lightness; lines are `#26282C`, `#2C2E33`, `#3A3D43`; text is `#F2F2F0`, `#C9CACD`, `#8E9096`. The single accent is amber `#F2B544` (hover `#F7C566`, press `#D99D2E`), used once per view for the primary action or a live signal, and text on amber is always ink. Status colours share one oklch lightness and chroma: ok 155°, danger 25°, info 245°; warn is amber. A light theme overrides the surfaces and text.

The app's values are in [Tokens.axaml](../src/Keypaste.App/Theme/Tokens.axaml); the CSS source is [docs/design/tokens](design/tokens/tokens/colors.css).

## Type

Instrument Sans 400/500/600 for the interface and Fragment Mono 400 for anything machine-readable: keys, values, references, paths, commands, tokens and timestamps. Both are under the SIL Open Font License and are vendored in [Assets/Fonts](../src/Keypaste.App/Assets/Fonts), so nothing is fetched at runtime.

The wordmark is "keypaste", always lowercase, Instrument Sans 600 at −0.045em. It is live text, not outlines; outline it before production print use.

## The marks

The lowercase k is a cursor stem (`rect 10,8,10,48`) and an insert-bracket arm (`polygon 40,24 54,24 38,40 54,56 40,56 24,40`) on a 64-unit grid, with a 4-unit gap that is never closed. The arm is amber, or the stem's colour in one-colour use.

| File in [`assets/brand/`](../assets/brand) | Use |
|---|---|
| `keypaste-mark.svg` | The mark in colour, on a dark ground |
| `keypaste-mark-mono.svg` | One-colour use; takes `currentColor` |
| `keypaste-lockup-dark.svg` / `keypaste-lockup-light.svg` | Mark and wordmark on a dark or light ground |
| `keypaste-app-icon.svg` | App icon: `#1D1E22` tile, radius 15/64 |
| `keypaste-favicon.svg` | 16px and below: amber tile, ink glyph |

[render-app-icon.py](../scripts/render-app-icon.py) draws the desktop app's `.ico`, PNG and Linux icon from this geometry.

## Rules

1. **Always lowercase.** The wordmark and the product name.
2. **The gap between stem and arm is never closed.**
3. **The mark's floor is 16px and the lockup's is 88px wide.** At 16px or smaller, use the favicon.
4. **Amber appears once per view**, for the primary action or a live signal, and carries ink text.
5. **Icons are Lucide at a 1.5px stroke**: 16px in lists, 14px in fields, 20px in the sidebar. No emoji and no filled icons.
