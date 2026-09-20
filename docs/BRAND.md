# Brand

Owns the marks, the palette, the type and the usage rules. Every surface — desktop app, CLI, site, store listing, installer — takes its visual values from here. Where a value also has to exist as code, this page names the file that holds it and that file is generated from or checked against these values; it does not carry a second opinion.

**Status:** adopted 2026-09-08 and not yet applied. The desktop app and the site still draw their earlier palettes. Apply these assets when relevant interface work calls for them; a complete brand rollout is not a separate gate for the focused local release.

## Colour

| Name | Hex | Role |
|---|---|---|
| Coral | `#FF6B57` | The one accent. Primary action, focus, selection, the dot in the mark. |
| Ink | `#2A2D31` | Text on Paper, the dark ground, and the label on any coral fill. |
| Paper | `#FAFAF9` | The light ground, and text on Ink. |

Three values, and no fourth is introduced without a row here. Once applied, surface and text steps derive from Ink and Paper in [Tokens.axaml](../src/Keypaste.App/Theme/Tokens.axaml), which today still holds the earlier accent.

**Ink is the contrast colour on coral, in both themes.** White on `#FF6B57` measures 2.6:1 and fails the 4.5:1 floor. The app icon carries white shapes on coral because a mark is not text; a button label, a link or a badge may not.

**Coral is the accent, so danger is not a colour.** The destructive confirmation is the one place the desktop app previously spent a red, and coral now occupies that hue. Two warm reds on one screen and neither reads, so the destructive path is drawn with weight and border instead. The design direction in [DECISIONS](../DECISIONS.md) is amended when this is applied.

## Type

**Nunito ExtraBold (800), always lowercase**, for the logo and every headline. Body text is Inter. Monospace stays as it is: file paths, the log pane and any column of digits, per the existing `KpMono` stack.

Nunito and Inter are both under the SIL Open Font License and are to be vendored, the way the KeePassLib port already is, so nothing is fetched at runtime and rendering is identical on every desktop target.

## The marks

Ten files in [`assets/brand/`](../assets/brand). Each has one job; a file with no rule and a rule with no file are both defects.

| File | Use |
|---|---|
| `keypaste-wordmark.svg` | Default lockup, on Paper or any light ground |
| `keypaste-wordmark-dark.svg` | The same lockup on Ink or any dark ground |
| `keypaste-wordmark-mono-black.svg` / `-mono-white.svg` | One-colour contexts |
| `keypaste-glyph.svg` | The "k" and dot alone, wherever the wordmark cannot meet its floor |
| `keypaste-glyph-mono-black.svg` / `-mono-white.svg` | One-colour contexts |
| `keypaste-icon.svg` | App icon, coral tile |
| `keypaste-icon-dark.svg` / `keypaste-icon-light.svg` | App icon on a dark or light shelf |

## Rules

1. **Always lowercase.** The logo and every headline. No sentence case, no capitals.
2. **The dot is coral on colour marks, and it is never omitted.**
3. **Clear space equals the height of the "k", on all four sides.** The "k" measures 69 units in the 435×130 wordmark, so the margin is `0.53×` the mark's height.
4. **The wordmark's floor is 96px wide.** Below that it is retired and the glyph takes over.
5. **A dark UI takes the dark cuts** — `wordmark-dark` and `icon-dark`, never a colour file dimmed.
6. **One-colour contexts take the mono cuts.**

## Open: rule 2 and two of the files disagree

Rule 2 holds in the wordmark, where the word is Ink and the dot is Coral. It does not hold in `keypaste-glyph.svg` or `keypaste-icon-light.svg`, where the coral went to the "k" and the dot came out Ink. Either those two files take the swap, or rule 2 gains a written exception. Nothing references them until this is settled.
