# Design principles

Seven principles govern what keypaste builds and how it feels, for the desktop app, the CLI, the site and the brand. They come from the founder's *Principles of Design* (2026-11-03), drawn from Dieter Rams' ten principles, Apple's Human Interface Guidelines and the GOV.UK design principles, and each is stated here with keypaste's own test. The first six hold in priority order; the seventh is where keypaste makes its choices. The security laws of [PRODUCT §3](PRODUCT.md#3-security-laws) stand above all seven, and keypaste also has to work as a business: these principles do not judge that, and business does not override them.

[DESIGN](DESIGN.md) is the system they are applied through.

## 1. Truth

Be what keypaste claims to be.

Test: every claim on a screen, in the CLI, on keypaste.com or in a guide matches what the code does and what recorded evidence shows (D-0036).

- No fake urgency, no hidden cost and no exit harder than the way in: the vault stays a KeePass file the person can open in another app, and leaving keypaste loses nothing.
- When keypaste refuses something it says why, in plain words, and every allow says how long it lasts.
- A screen shows the state as it is: saved, unsaved or changed on disk, locked or unlocked, never a reassurance the code has not checked.
- A goal is named as a goal. keypaste.com may say what keypaste is built to become, such as ready for an enterprise, only on a page that labels what is published, building and planned (founder's direction, 2026-11-10).

## 2. Responsibility

Build it to last, and account for what it costs people.

Test: you can say how long a person's vault outlives keypaste, how a fault gets fixed and what happens to their data at the end.

- The vault is a KDBX file any KeePass app reads, the code is open under AGPL-3.0, and nothing keypaste builds needs a service to keep a local vault working.
- Temporary work counts too: a probe, a fixture or a workflow is deleted once it has served, and CI spends nothing that cannot change a verdict (D-0468).
- Cost includes the person's attention: an interruption, a prompt or a notification has to earn it.

## 3. Purpose

Do a real job for someone.

Test: you can say who loses what if it is removed.

- Start from the moment, not the feature: every task names the moment it improves for a person, such as an agent asking for a key and the person answering with one click, before it names a technology.
- Fewer things, finished. A release carries what makes keypaste keypaste; matching another app is not a reason on its own.
- The main job gets the most space, the fewest steps and the best placement. On Items that is finding an item and using it.

## 4. Clarity

It explains itself.

Test: someone who has never used KeePass can download keypaste, open or create a vault, connect an agent, answer its first request and run a project without asking anyone.

- One journey: download, first run, connecting an agent, the first approval and running a project are designed, built and walked through as one thing, on real machines.
- Decide for the person. Defaults are opinionated and safe ([PRODUCT §5.8](PRODUCT.md#5-product-laws)), and a setting exists only when real people need both answers.
- Safe without effort: the laws of §3 hold without the person having to understand them.
- Controls look like what they do, every action gets feedback, and the words are the person's, not keypaste's internal ones.
- Mistakes are easy to recover from: what can be undone does not ask first, and what cannot be undone, such as deleting past the Trash, says so before it happens ([DESIGN § Interaction](DESIGN.md#interaction)).
- Each fact appears once in a view, where the person looks for it.

## 5. Care

Finish every detail.

Test: every state is designed, including empty, error, loading, locked and first use.

- Care goes where people look: what people touch gets the polish.
- Design for every person: different abilities, languages, screens and text sizes ([DESIGN § Interaction](DESIGN.md#interaction)).
- When the budget runs short, cut scope, never the finish.
- Prioritise by how often people meet a state and how much harm a bad one does; a prompt that releases a value is met often and does the most harm.

## 6. Order

Every choice is deliberate.

Test: the system is written down in [DESIGN](DESIGN.md) before a screen is designed, not explained afterwards.

- Work from the system: the layout, the 4px spacing, the palette, the type scale and the icons.
- At home on every platform: the desktop follows Apple's Human Interface Guidelines on every platform, with Ctrl where macOS uses ⌘ and only where the platform has the concept; DESIGN governs where the two differ, and PRODUCT §3 governs both (D-0682).
- Order is not minimalism. A view can be quiet or loud, but never random.

## 7. Character

Hold a point of view, with one logic across every layer.

Test: you can state each setting below, for each layer, and why.

| Setting | keypaste | Why |
|---|---|---|
| Volume | Quiet: flat ink, one amber per view, a calm voice | A credential tool earns trust by not shouting; the one loud thing is what needs the person ([DESIGN](DESIGN.md)) |
| Mystery | Little: everything a person needs is visible or one menu away | Mystery that fails Clarity is confusion, and an approval cannot be a discovery |
| Tempo | Slow: the look changes only by founder direction recorded in DESIGN, and what people recognize keypaste by, the "k." icon, the wordmark, the amber and the four places' names, stays while what surrounds it changes | A faster tempo still has to pass Responsibility, and people learn where things are |
