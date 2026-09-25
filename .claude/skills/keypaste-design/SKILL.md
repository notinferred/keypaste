---
name: keypaste-design
description: Keep keypaste's desktop screens, CLI output, site, README and brand assets on brand. Use when designing, restyling or reviewing any keypaste interface, prototype, screenshot, page or copy, and when adding a colour, font, icon, spacing value or user-facing wording.
user-invocable: true
---

# keypaste design

Read these before changing anything a person sees:

- [docs/BRAND.md](../../../docs/BRAND.md) owns the marks, palette, type, shape, icons, terminal rules, voice and usage rules.
- [docs/design/README.md](../../../docs/design/README.md) holds the exact values for every desktop screen, overlay and CLI view, and the prototype state and interactions.
- The prototypes in `docs/design/design/` are the high-fidelity reference: `keypaste Desktop.dc.html` for the app, `keypaste CLI.dc.html` for terminal output and `keypaste Brand System.dc.html` for components and voice. Styles are inline, so read px, colours and copy from the markup; demo data and handlers are in the desktop file's `<script data-dc-script>`. Open one in a browser with `support.js` beside it.
- `docs/design/tokens/` is the CSS token source. The marks are in `assets/brand/`.

## Production code

The desktop app (Avalonia, `src/Keypaste.App`) already carries the brand; use it rather than new values:

- `Theme/Tokens.axaml`: brushes such as `KpBgApp`, `KpBgPanel`, `KpBgCard`, `KpBorderSubtle`, `KpTextPrimary`, `KpTextMuted`, `KpAccent`, `KpOk`, `KpDanger`, `KpInfo`. A view never writes a hex colour.
- `Theme/Typography.axaml`, `Buttons.axaml`, `Inputs.axaml`, `Lists.axaml`, `Surfaces.axaml`: classes for text, buttons, fields, rows and cards.
- Icons: `<ctl:KpIcon Icon="lock" Size="16"/>`; add a Lucide icon with `python scripts/lucide-to-axaml.py <name>`. Marks: `<ctl:BrandMark/>` and `<ctl:BrandLockup/>`.
- `KEYPASTE_SCREENS_OUT=<dir> dotnet test tests/Keypaste.App.Tests -- --filter-class Keypaste.App.Tests.Rendering.ScreenRenderer` renders every screen with a demo vault; compare the PNGs with the prototype.

keypaste.com takes its tokens from `site/public/brand.css`. CLI colour and help layout follow the terminal rules in BRAND.

Brand never overrides security behaviour: a secret stays masked until revealed, masked fields stay at 14px or larger, and secret-bearing elements carry no automation name. Keep every control name the tests use.

## Checks before finishing

- Colours, type, spacing and radii come from the tokens, and amber appears once per view, for the primary action or a live signal, with ink text on it.
- Anything machine-readable (keys, values, `kp://` references, paths, commands, tokens, timestamps) is Fragment Mono; everything else is Instrument Sans.
- Depth is lightness; shadows only on dialogs, toasts and popovers.
- Icons are Lucide at 1.5px, muted unless their object is live. No emoji, no filled icons.
- Copy names the actor and the object, gives every grant a duration, uses sentence case and "you", has no exclamation marks and says what to do next after an error. "keypaste" is always lowercase.

## Throwaway visuals

For a mock or a slide rather than production code, copy the SVGs from `assets/brand/` and the tokens from `docs/design/tokens/styles.css` into a static HTML file, load Instrument Sans and Fragment Mono, and build from the prototype's components. If the request gives no direction, ask what is being made and for whom before designing.
