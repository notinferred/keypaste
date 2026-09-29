# N.7 — Apply the amended brand

Completed 2026-09-29 on `main` above `856b407`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Build:** the app follows [BRAND](../BRAND.md) as amended on 2026-09-28. With no theme chosen, `AppSettings.Default` and `App.axaml` follow the system's light or dark setting, as it changes; Light or Dark chosen in Settings still wins. Titles and headings, the item title in its pane among them, use Instrument Sans at BRAND's sizes, and keys, values, references, paths, commands and timestamps stay Fragment Mono. The copy on the main screens and in the prompts follows BRAND's voice without dropping what T-2's evidence relies on. The first line of `keypaste --help` carries PRODUCT §1's positioning within 80 columns. The keypaste-design skill applies. Traces to PRODUCT §§1 and 5.8.

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

## What changed for users

- **New marks everywhere.** The desktop's sidebar carries the wordmark "keypaste." alone and its lock screen the icon "k." alone. The same marks, in Hepta Slab with an amber dot, are used for:
  - the app icon on Windows, macOS and Linux;
  - keypaste.com's header, footer, share viewer and favicon;
  - the README.
- **The app follows the system.** With no theme chosen, it starts in the light or dark palette the operating system uses and switches when the system does. Light or Dark in Settings still wins, and choosing System again follows the system again.
- **Status colours read on light.** Green, red and blue text is darker on the light palette (`#007840`, `#B02A2D`, `#296898`), at 4.85:1 or more on every light surface. Before, it was 1.7–2.4:1, now that light is what a light system gets. Examples are "Not opened yet", "missing" and a refusal. keypaste.com's light palette uses the same values.
- **Titles are words.** An item's title is Instrument Sans: 22/600 in its pane and 13.5/500 in the list. An env variable's key stays Fragment Mono in both places. Keys, values, `kp://` references, paths, commands and times are still mono.
- **Amber marks one thing on every screen,** in both palettes, where N.1a2 held only the screens its journey passed through:
  - a checked box, a chosen option and a switch that is on are drawn in the text colour;
  - Items' "+ New" is ordinary while a form in the view shows its own primary: a new item, an edit, a rotate or a field;
  - on Agents, the connect, token and run forms' buttons are ordinary while a request waits;
  - the unlock screen's Unlock is ordinary while a YubiKey waits for a touch, whose waiting dot is then the signal; the recent vault's icon and the key-file and USB icons are grey;
  - in the prompt windows, the countdown, its dot and the note that part of the reason was removed are grey, since the answer to allow is the prompt's one amber element;
  - Env profiles' notices, protected-profile shield, cell notes and problems are secondary text, and the launch confirmation card is neutral;
  - an unverified row in the activity log has a red bar, and an unread reason a grey triangle;
  - a share link opened but still opening is blue;
  - the notice after a restore is a neutral card, and Trash's "comes back at the root" is secondary text;
  - the import dialog's switch and the share dialog's passphrase check are drawn in the text colour.
- **Calmer copy.**
  - Agents' subtitle now reads "Apps and tokens that can ask for a secret. Each request waits for your answer, or uses one you gave for a set time.", replacing "MCP clients and tokens that can ask for secrets. Every request goes through you or a grant."
  - The empty pane now reads "Choose an item to see it here.", replacing "Choose a secret to see it here."
  - The prompts' copy already met BRAND's voice and is unchanged, so every element T-2's evidence relies on is where it was.
- **The CLI says what keypaste is.** The first line of `keypaste --help` reads `keypaste <version> · a simple, local password manager on your KeePass file`. That is 70 columns with a release version and 76 with an `-rc.NN` suffix; PRODUCT §1's fuller sentence would be 85.

None of this is in a download: the desktop has no public release, and keypaste.com changes when `main` is next pushed.

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

No ledger row. The theme default and the marks are BRAND's, and each colour change applies D-0375. These bind only this step's code:

- **System maps to an explicit Light or Dark variant,** taken from the platform's colour values and updated on their change event.
  - Avalonia's own Default variant, which `ApplyTheme` used before, had a defect: after Light or Dark had been requested and System was chosen again, it left the app with no variant at all, so the window fell back to white in either palette.
  - The theme test above caught it.
- **A prompt window's waiting dot is grey,** since the prompt is itself the waiting request and its allow answer is its amber element. The waiting dot on the unlock screen and in the title bar stays amber.
- **The marks are geometry, not text.** The app draws the generated outlines, so no mark face is vendored. `BrandMark` and `BrandWordmark` keep their proportions: a set Width or Height gives the other.
- **Light status values sit at oklch lightness 0.50.** The site's 0.55 measured 4.25:1 for ok on the light background.

## Limits and follow-ups

- **Only drawn states are checked for amber.** Two requests waiting on Agents draw two amber countdowns, and a view the renderer does not reach could hold two amber elements.
- **Headless rendering only.** The amber, mark and type checks read frames drawn headless by Skia at a scaling of 1, not a platform's own text rendering.
- **Following the system is proven through the headless platform's colour-change event.** A real system switch on Windows, macOS and Linux is item 9 of desktop.md's checklist.
- **The prototypes in `docs/design/design/` still draw the monogram.** BRAND and `assets/brand/` are the reference for the marks, as `docs/design/BRAND.md` now says.
- **Regenerating the marks needs the recorded font file, fontTools, uharfbuzz and skia-pathops.** None of them is a build prerequisite.
