---
name: keypaste-design
description: Keep keypaste's desktop screens, CLI output, site, README and brand assets on brand. Use when designing, restyling or reviewing any keypaste interface, prototype, screenshot, page or copy, and when adding a colour, font, icon, spacing value or user-facing wording.
user-invocable: true
---

# keypaste design

Read these before changing anything a person sees:

- [docs/DESIGN-PRINCIPLES.md](../../../docs/DESIGN-PRINCIPLES.md) holds the seven principles in priority order, each with its test.
- [docs/DESIGN.md](../../../docs/DESIGN.md) is the design system: how a screen is put together, the marks, palette, type, shape, icons, terminal rules, voice, interaction and usage rules.
- The app's views hold each screen's exact sizes, and the screen renderer below draws the real screens. `docs/design/tokens/` is the CSS copy of the tokens, for mocks. The marks are in `assets/brand/`.

## Production code

The desktop app (Avalonia, `src/Keypaste.App`) already carries the brand; use it rather than new values:

- `Theme/Tokens.axaml`: brushes such as `KpBgApp`, `KpBgPanel`, `KpBgCard`, `KpBorderSubtle`, `KpTextPrimary`, `KpTextMuted`, `KpAccent`, `KpOk`, `KpDanger`, `KpInfo`. A view never writes a hex colour.
- `Theme/Typography.axaml`, `Buttons.axaml`, `Inputs.axaml`, `Lists.axaml`, `Surfaces.axaml`: classes for text, buttons, fields, rows and cards.
- Icons: `<ctl:KpIcon Icon="lock" Size="16"/>`; add a Lucide icon with `python scripts/lucide-to-axaml.py <name>`. Marks: `<ctl:BrandMark/>` (the icon) and `<ctl:BrandWordmark/>`, never side by side.
- `KEYPASTE_SCREENS_OUT=<dir> bash scripts/verify.sh test Keypaste.App.Tests.Rendering.ScreenRenderer` renders every screen with a demo vault into `<dir>`; check them against DESIGN and the principles' tests.

keypaste.com takes its tokens from `site/public/brand.css`. CLI colour and help layout follow the terminal rules in DESIGN.

Brand never overrides security behaviour: a secret stays masked until revealed, masked fields stay at 14px or larger, and secret-bearing elements carry no automation name. Keep every control name the tests use.

## Checks before finishing

- Colours, type, spacing and radii come from the tokens, and amber appears once per view, for the primary action or a live signal, with ink text on it.
- Anything machine-readable (keys, values, `kp://` references, paths, commands, tokens, timestamps) is Fragment Mono; everything else is Instrument Sans.
- Depth is lightness; shadows only on dialogs, toasts and popovers.
- Icons are Lucide at 1.5px, muted unless their object is live. No emoji, no filled icons.
- Copy names the actor and the object, says how long every allow lasts, uses sentence case and "you", has no exclamation marks and says what to do next after an error. "keypaste" is always lowercase.
- Each fact appears once in a view, and the main job gets the room (DESIGN § Information).
- DESIGN § Interaction holds: the command is in `AppCommands.cs` and so in the menus, with the platform's chord and an ellipsis when it opens more; a confirmation puts Cancel first, answers Return and Escape, and asks only when the action cannot be undone; a draft survives a change of place; every field has a visible label and its error under it; a wait shows a moving indicator; every control has an accessible name that holds no secret, contrast is 4.5:1 for text and 3:1 for focus, edges and status, and targets are at least 20 by 20.

## Throwaway visuals

For a mock or a slide rather than production code, copy the SVGs from `assets/brand/` and the tokens from `docs/design/tokens/styles.css` into a static HTML file, load Instrument Sans and Fragment Mono, and build from the rendered screens. If the request gives no direction, ask what is being made and for whom before designing.
