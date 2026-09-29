# N.1a2 — Allow one amber element per frame

Completed 2026-09-29 on `main` above `19dcf99`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards. With it, N.1a closes: [N.1a1](N.1a1.md) built the four places and every other clause of V-N.1a.

## Scope as selected

**Build:** a test-side detector counts the amber elements in a frame the app drew: connected regions of the accent's hue and saturation, on the topmost surface only, outside the marks, a selected row's inset bar and the focus ring. The screens N.1a1's journey passes through are brought to at most one, the view's primary action or live signal (BRAND rule 4): selected sidebar and list icons, thin progress bars and link buttons leave amber, a row's in-use dot turns blue, and Agents' Connect client is primary only while nothing is waiting. Traces to PRODUCT §§1 and 5.8.

**Verify (V-N.1a2):** differential tests show the detector counting two primaries as two, amber mono text as one in both themes, a tint or a selected row as none, and a dialog's primary over a backdrop as one. Every frame of N.1a1's journey then holds at most one amber element, and where the view has a primary action or live signal it is that element. A rule asserted over styles or tokens rather than drawn frames does not pass.

This is the last clause of N.1a's Verify, "each captured frame has one amber element", as the plan the founder approved on 2026-09-29 read it: at most one, and the view's primary action or live signal where it has one. Read as exactly one, it would put amber on Trash, Settings and the logs, which have no primary action, against BRAND rule 4.

## What changed for users

Amber now marks one thing on a screen: its main action, such as Items' "+ New", Import in the import dialog, Create and copy link in Share…, or Connect client on Agents, or something live, such as a waiting request's countdown.

- A selected row keeps its faint amber tint and bar, but its icon, like the selected sidebar row's and group's, is drawn in the text colour: an icon is amber only while its object is live, as BRAND says.
- Link-style buttons such as Clear now, Choose… and Restore are drawn in the text colour.
- The thin bars that count a grant's or the clipboard's time down are grey; the time in words beside them says the same.
- An entry's "in use" dot is blue rather than amber, since several can be in use at once, and a search result's matched-field note, such as "username", is secondary text.
- On Agents, the section icons are grey, a grant's time left is secondary text, and Connect client is the screen's primary button while nothing is waiting.
- A project's selected profile is underlined in the text colour, and the import dialog's file icon is grey.

The rest of the app's amber, on the lock screen, the prompts, notices and the other screens N.1a's journey does not pass through, is N.7's to settle.

None of this is in a download: the desktop has no public release.

## Evidence

Local, Windows 10 Pro 19045, on `main` above `19dcf99` with the step's changes uncommitted.

**The detector.** [AmberElements](../../tests/Keypaste.App.Tests/Rendering/AmberElements.cs) reads the frame Skia drew, after the render timer has run long enough for every 120–200 ms transition to finish. A pixel is amber when its red is highest, its chroma is at least 48 of 255 and its hue lies from 30° to 48°: amber `#F2B544`, its shades and the light theme's `#8A5A00` sit near 39° with chroma of 138 and more, while the 10–15% tints stay under 26 and danger, ok and info lie at 4°, 143° and 207°. Pixels join across ten pixels along a line and three between lines, so a line of amber text is one element, and a region under eight pixels is not one. Only a visible dialog's bounds count when a backdrop is up. The marks, the leading three pixels of a selected row and the focus ring are cleared, a focused text field whole, since its border and glow are its focus.

Its first runs found every glyph on the real window amber: Skia's subpixel text puts orange fringes on white letters. The detector now switches the window to grayscale text and redraws before it reads.

[AmberElementsTests](../../tests/Keypaste.App.Tests/Rendering/AmberElementsTests.cs), each drawn by Skia in the app's theme, checks seven cases:

- one primary button is one element, and two are two;
- "42m left" in amber mono is one element, dark and light;
- a 10% tint, a selected list row and a focused field are none;
- a waiting dot is one;
- the mark beside a primary leaves one;
- a dialog's primary over a backdrop, with two primaries behind it, is one.

**The journey.** [FourPlacesJourneyTests](../../tests/Keypaste.App.Tests/Session/FourPlacesJourneyTests.cs) now asserts on ten frames. `AddEntry` is the one amber element on Items with an item open and the search scoped, `ImportConfirm` in the import dialog, `CreateLink` in Share…, and `ConnectClient` on Agents with a grant in force. There is none on a project's variables, History, Settings, the activity log with its verdict shown, share links and Trash. Before the changes above, its first failing frame, Items, held four elements: "+ New", and the selected sidebar row's, list row's and group's icons. Agents held six: the section icon, the grant's time left, two progress bars and two link buttons.

**The command.** `./scripts/verify.ps1` selected workflows, scripts and desktop, the only profiles the step's paths map to, and passed in about six minutes: the desktop suite ran 777 tests, 775 passed and 2 skipped, and Consistency 43 of 43.

## Decisions

- D-0375: how an amber element is counted, and at most one per frame, being the view's primary action or live signal.

## Limits and follow-ups

- Only the screens and states the journey reaches are held to the rule; N.7's ScreenRenderer check covers every screen in both palettes.
- A waiting request on Agents makes its countdown the live signal and Connect client an ordinary button; the journey did not draw that state.
- The detector reads hue and chroma, so a design that used amber at under 48 chroma, or another hue for a signal, would pass unseen.
- Frames are drawn headless at a scaling of 1; the join distances are in pixels.
