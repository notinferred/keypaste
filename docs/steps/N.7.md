# N.7 — Apply the amended brand

Completed 2026-09-29 on `main` above `856b407`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Verify (V-N.7):** with no `app.toml`, a frame captured under a light platform setting has the light app background and one under a dark setting the dark background, and a choice in Settings overrides either. `ScreenRenderer` renders every screen in both palettes, and each frame has one amber element. The rendered item title and headings use Instrument Sans, and a key and a value Fragment Mono. `CliAppTests` pins the new help line, which fits 80 columns. A palette shown only in a token file does not pass.

"One amber element" is read as D-0375 reads it: at most one, the view's primary action or live signal where it has one.

**Amendments by the founder, 2026-09-29.**

- **New marks.** The monogram and its lockup give way to an icon ("k." with an amber dot) and a wordmark ("keypaste." with an amber dot), never set together. A concept sheet showed four directions as a private artifact:
  - Hepta Slab, at SemiBold and at Bold;
  - Nunito ExtraBold;
  - Instrument Sans;
  - the brand of 2026-09-08 as it was.

  The founder picked direction A. It is built at SemiBold, the weight the sheet recommended.
- **No new rows.** Work this step found goes into this step, not onto new rows. A split of N.7 into N.7a and N.7b, and a row for the light palette's status contrast, were drafted and withdrawn. The contrast fix is below.
- **A lighter cut of the marks, later the same day.** The founder asked for a logo and wordmark in the new font that fit the new styling better. A second concept canvas, also a private artifact, set the marks above beside three directions, each in the sidebar, on the lock screen, as the app icon from 128 down to 16px, in a browser tab, in keypaste.com's header and in the README:
  - A · Quiet, lighter and tighter;
  - B · Keycap, A's wordmark with the app icon drawn as a keycap;
  - C · Caret, an amber text caret in place of the dot.

  The founder picked A. It was first recorded as a step of its own, N.15, in `1968915`. Under the founder's rule above, it belongs here: that record was withdrawn in its favour, and the ID N.15 is retired. Its account follows.

  - **What changed.** The wordmark is Hepta Slab at weight 500, where it was 600, tracked −1.5%. The icon is weight 580 tracked −1%. Each mark's square dot is sized from its weight's stem, 1.45 stems in the wordmark and 1.3 in the icon, and set close after the last letter, where it had been the font's own period. They are applied to the desktop's sidebar and lock screen, the app icon on three platforms, the favicon, keypaste.com's header, footer, share viewer and thanks page, and the README and its screenshots.
  - **How.** [outline-brand-marks.py](../../scripts/outline-brand-marks.py) now outlines each mark at its own weight, unites the tracked letters into one outline, since the app and the icon rasterizer fill even-odd, and draws the dot as geometry. It also rewrites the wordmark inline in `site/public/index.html`, `s/index.html`, `thanks/index.html` and `site/src/worker.js`, and stops if one no longer carries it. [render-app-icon.py](../../scripts/render-app-icon.py) redrew the `.ico`, the PNG, the Linux SVG and the `.icns`.
  - **Evidence.** Two runs of the outline script left every file byte-identical. Pillow decoded the `.ico` at nine sizes from 16 to 256 px and the `.icns` at 128 to 1024 px. `ScreenRenderer`, `BrandMarksTests` and `AmberElementsTests` passed 12 of 12 in both palettes. Four README screenshots were matched to the frames they came from and replaced; `approval-prompt.png` carries no mark. `bash scripts/verify.sh` passed workflows, scripts, backend, integration and desktop, the desktop suite 835 of 838 with the renderer's 3 skipped.
  - **Limits.** Headless rendering only: the icons were not seen in a real taskbar, Dock or launcher. The prototypes in `docs/design/design/` still draw the monogram.

## Evidence

Local, Windows 10 Pro 19045, on `main` above `856b407` with the step's changes uncommitted.

**The system theme.** [ThemeFollowsSystemTests](../../tests/Keypaste.App.Tests/ThemeFollowsSystemTests.cs) composes a main window with no `app.toml` and reads the centre pixel of each drawn frame:

- `#111214` under a dark platform setting;
- `#F7F7F5` after the platform turns light;
- `#111214` again when it turns back;
- Light in Settings stays light under a dark platform, and Dark stays dark under a light one;
- choosing System follows the platform again.

The headless platform always reports light, so [PlatformTheme](../../tests/Keypaste.App.Tests/PlatformTheme.cs) raises its colour-change event, and each headless dispatch starts dark. `StartupSettingsTests` and `AppSettingsTests` hold System as the default.

**Both palettes, one amber element.** [ScreenRenderer](../../tests/Keypaste.App.Tests/Rendering/ScreenRenderer.cs) renders its 70 screen frames twice, into `dark` and `light`, with the platform set to each and no theme chosen. It fails any frame with more than one amber element by N.1a2's detector; the component sheet, which shows every control at once, is exempt.

Its first run found fourteen frames over the rule, seven per palette:

- the env add form and a new item's form: a checked box and a chosen option beside the primary;
- the three prompts and the allow-once prompt: the waiting dot, and in the last the removed-text note, beside the primary;
- the YubiKey settings: three chosen options.

The checked colours had been overridden in both theme dictionaries, but an older amber definition at the dictionary's top level was found first. Removing it, and drawing the prompts' dot and note grey, brought every frame to at most one. Both palettes then rendered clean, with the new marks in place. The frames nothing renders were checked by reading every remaining amber use in the views, which moved Sharing's partly used status to blue and Trash's note and the restore notice off amber.

**Status contrast.** [StatusColourContrastTests](../../tests/Keypaste.App.Tests/StatusColourContrastTests.cs) resolves ok, danger and info against the app, panel, card and hover grounds of each palette. All 24 pairs are at 4.5:1 or more, the lowest being ok on light hover at 4.85:1. With the old light values, ok on the light app background measured 1.66:1.

**The marks.** [outline-brand-marks.py](../../scripts/outline-brand-marks.py) outlines "k." and "keypaste." from Google Fonts' `HeptaSlab[wght].ttf`, whose SHA-256 it records and checks. It instantiates weight 600, removes overlaps, and shapes with HarfBuzz at −1% tracking. From one run it writes:

- the eight SVGs in `assets/brand/` and `docs/design/assets/`;
- the site favicon;
- the app's [BrandOutlines.axaml](../../src/Keypaste.App/Theme/BrandOutlines.axaml).

A second run left every file byte-identical. [render-app-icon.py](../../scripts/render-app-icon.py) now rasterizes the two tiles it reads from `assets/brand/`, flattening curves and filling even-odd. The regenerated `keypaste.ico` decodes at 16, 20, 24, 32, 40, 48, 64, 128 and 256 px, `keypaste-256.png` at 256 px, and `keypaste.icns` at 128, 256, 512 and 1024 px.

[BrandMarksTests](../../tests/Keypaste.App.Tests/Rendering/BrandMarksTests.cs) holds three things in both palettes:

- the unlocked shell shows one wordmark and no icon, and the lock screen one icon and no wordmark;
- each mark's drawn bounds hold its ink colour;
- amber appears only in the last fifth of each mark, where the dot is.

keypaste.com's home page was served locally and captured in headless Edge with the wordmark in its header.

**Type.** [TitleTypographyTests](../../tests/Keypaste.App.Tests/Rendering/TitleTypographyTests.cs) reads the drawn frame, and each match is paired with the other face failing in the same place:

- "github" in its pane matches Instrument Sans 22/600 with the heading's tracking, and not Fragment Mono;
- the list row's title matches the sans at 13.5/500, and not the mono;
- the field key `API_TOKEN` matches the mono, and not the sans;
- the variable key `STRIPE_KEY` in its pane matches the mono at 22, and not the sans heading.

[DrawnFrame](../../tests/Keypaste.App.Tests/Rendering/DrawnFrame.cs) now lays out a tracked reference with a TextBlock's letter spacing, since FormattedText has none. Untracked references draw as before, and `DrawnFrameTests` and `DrawnRevealTests` pass unchanged.

**The CLI.** `CliAppTests` (13 tests) pins the new first line, and `Help_FitsEightyColumns` holds it within 80 columns.

**The command.** `bash scripts/verify.sh` selected workflows, scripts, backend, integration and desktop, since brand assets and the CLI are paths no narrower profile claims, and passed in about ten minutes. The desktop suite ran 807 tests: 804 passed and 3 skipped, the renderer's three draws, which need an output folder. Consistency passed 43 of 43, and the backend 3,035 of 3,045 with 10 skipped. Integration's demo check passed over the changed `site/public/index.html`. `compat` needs an installed KeePassXC and is run by name; this step changed no vault format path.

## Decisions

No ledger row. The theme default and the marks are BRAND's, and each colour change applies D-0375.

## Limits and follow-ups

- **Only drawn states are checked for amber.** Two requests waiting on Agents draw two amber countdowns, and a view the renderer does not reach could hold two amber elements.
- **Headless rendering only.** The amber, mark and type checks read frames drawn headless by Skia at a scaling of 1, not a platform's own text rendering.
- **Following the system is proven through the headless platform's colour-change event.** A real system switch on Windows, macOS and Linux is item 9 of desktop.md's checklist.
- **The prototypes in `docs/design/design/` still draw the monogram.** BRAND and `assets/brand/` are the reference for the marks, as `docs/design/BRAND.md` now says.
- **Regenerating the marks needs the recorded font file, fontTools, uharfbuzz and skia-pathops.** None of them is a build prerequisite.
