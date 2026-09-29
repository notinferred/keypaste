# N.15 — Cut the marks lighter to fit the interface

Completed 2026-09-29 on `main` above `283628a`, source only. [BRAND](../BRAND.md) owns the marks; this record is what the step did and is not revised afterwards.

## Scope as selected

**Founder direction, 2026-09-29.** After N.7 applied the Hepta Slab marks, the founder asked for a new logo and wordmark in that face that fit the new styling better. A concept canvas, a private artifact, showed the N.7 marks beside three directions, each in the sidebar at 104px, on the lock screen, as the app icon from 128 down to 16px, in a browser tab, in keypaste.com's header and in the README, on dark and light:

- A · Quiet: the wordmark at weight 500 and the icon at 580, tracked tighter, with a square dot sized from the stem and set closer;
- B · Keycap: A's wordmark, with the app icon drawn as a keycap;
- C · Caret: an amber text caret in place of the dot, with a block-cursor variant.

The founder chose A. Traces to PRODUCT §5.8 through BRAND.

**Build:** the outline script draws both marks to A's settings and writes every file drawn from them, the site's inline wordmarks included; the raster icons, BRAND and the README's screenshots follow.

**Verify:** two runs of the script leave every file it writes byte-identical; `BrandMarksTests` holds each mark's placement, ink and dot in both palettes; `ScreenRenderer` draws every screen in both palettes with at most one amber element; and the regenerated icons decode at each size.

## What changed for users

- **A lighter wordmark.** "keypaste." is Hepta Slab at weight 500, where it was 600, tracked −1.5%. It sits beside the sidebar's Instrument Sans instead of outweighing it.
- **A tighter icon.** "k." is weight 580, tracked −1%, so it keeps its weight on the app tile and at 16px.
- **The dot belongs to the letters.** Each mark's square dot is sized from its weight's stem, 1.45 stems in the wordmark and 1.3 in the icon, and set close after the last letter. Before, it was the font's own period.
- **Everywhere the marks appear:** the desktop's sidebar and lock screen, the app icon on Windows, macOS and Linux, the favicon, keypaste.com's header and footer, the share viewer and the thanks page, and the README with its screenshots.

None of this is in a download: the desktop has no public release, and keypaste.com changes when `main` is next pushed.

## Evidence

Local, Windows 10 Pro 19045, on `main` above `283628a` with the step's changes uncommitted.

**The marks.** [outline-brand-marks.py](../../scripts/outline-brand-marks.py) read the recorded `HeptaSlab[wght].ttf` (SHA-256 `737badc7…1f29`, checked by the script) and wrote:

- the eight SVGs in `assets/brand/` and `docs/design/assets/`;
- the site favicon;
- `BrandOutlines.axaml`;
- the inline wordmark in `site/public/index.html` (header and footer), `site/public/s/index.html`, `site/public/thanks/index.html` and `site/src/worker.js`.

A second run left all of them byte-identical. The wordmark is now 494.08 × 100 units, where it was 510 × 100, so the site's 112px wordmark is 22.67px tall.

**The icons.** [render-app-icon.py](../../scripts/render-app-icon.py) redrew:

- `keypaste.ico` at 16, 20, 24, 32, 40, 48, 64, 128 and 256 px;
- `keypaste-256.png`;
- the Linux `com.keypaste.app.svg`;
- `keypaste.icns`.

Pillow decoded the ico at those nine sizes, each of the icns's PNG payloads at 128, 256, 512 and 1024 px, and the PNG at 256 px. The PNG, the two tiles and the Linux SVG were viewed in headless Edge.

**The app.** One run of `ScreenRenderer`, `BrandMarksTests` and `AmberElementsTests` passed 12 of 12, with the renderer drawing its frames in both palettes into an output folder:

- `BrandMarksTests` holds, in both palettes, one wordmark and no icon on the unlocked shell, one icon and no wordmark on the lock screen, each mark's ink colour and amber only in the mark's last fifth;
- the renderer found no frame with more than one amber element.

**The screenshots.** Four README screenshots were matched to the rendered frames they came from, by counting changed pixels:

| Screenshot | Frame | Where it differed |
|---|---|---|
| `secrets.png` | `dark/10-secrets-login` | the sidebar, and N.5's link-style URL in the pane, which the old screenshot predated |
| `env-profiles.png` | `dark/40-env-staging` | the sidebar only |
| `agents.png` | `20-agents` | mostly the sidebar |
| `lock-screen.png` | `dark/80-lock-idle` | the icon only |

Each was replaced by its new frame. `approval-prompt.png` carries no mark and is unchanged.

**The command.** `bash scripts/verify.sh` selected workflows, scripts, backend, integration and desktop, since brand assets are paths no narrower profile claims, and passed in 609 seconds.

- The desktop suite ran 838 tests: 835 passed and 3 were skipped, the renderer's draws, which need an output folder.
- Consistency passed 43 of 43.
- The backend passed 3,091 of 3,101, with 10 skipped.
- Integration's demo check passed over the changed `site/public/index.html`.

`compat` needs an installed KeePassXC and is run by name; this step changed no vault path.

## Decisions

No ledger row: the marks are BRAND's. These bind only this step's code.

- **The script owns every copy of the outlines.** The site's four inline wordmarks had been pasted by hand. The script now rewrites them, and it stops if a file no longer carries one, so a re-run cannot leave the site on an older cut.
- **The wordmark's letters are one outline.** Tracked letters may touch, and the app's geometry and the icon rasterizer fill even-odd, so the letters are united before they are written.
- **The dot is geometry, not a glyph.** Its side is measured from the "l"'s stem halfway up the x-height at the mark's weight, so a change of weight resizes it with the letters.

## Limits and follow-ups

- **Headless rendering only.** The icons were not viewed in a real taskbar, Dock or launcher, and the app's marks only in frames Skia drew headless.
- **The prototypes in `docs/design/design/` still draw the monogram,** as N.7 left them. BRAND and `assets/brand/` are the reference.
- **Regenerating the marks** still needs the recorded font file, fontTools, uharfbuzz and skia-pathops. None of them is a build prerequisite.
