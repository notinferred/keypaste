# Design principles

Eight principles govern what keypaste builds and how it feels, for the desktop app, the CLI, the site and the brand. They are the eight of Apple's [Human Interface Guidelines](https://developer.apple.com/design/human-interface-guidelines/design-principles), Purpose, Agency, Responsibility, Familiarity, Flexibility, Simplicity, Craft and Delight, which PRODUCT §7 adopts (v1.25, 2026-11-10); each is stated here in keypaste's own words with keypaste's own test. Apple ranks none of them and neither does keypaste: they are tools for weighing a choice, and when two pull apart the one that protects the person's credentials and attention wins. The security laws of [PRODUCT §3](PRODUCT.md#3-security-laws) stand above all eight, and keypaste also has to work as a business: these principles do not judge that, and business does not override them.

[DESIGN](DESIGN.md) is the system they are applied through.

## Purpose

Do a real job for someone.

Test: finding an item and using it, and answering an agent, get the most room and the fewest steps.

- Start from the moment, not the feature: every task names the moment it improves for a person, such as an agent asking for a key and the person answering with one click, before it names a technology.
- Fewer things, finished. A release carries what makes keypaste keypaste; matching another app is not a reason on its own.
- The main job gets the most space, the fewest steps and the best placement. On Items that is finding an item and using it.

## Agency

Let people do things their own way, and never trap them.

Test: nothing interrupts except what needs the person, and a mistake can be undone.

- Stay out of the way. A prompt, a notification or a dialog has to earn the person's attention, and a guided flow can be skipped or left.
- What can be undone does not ask first. What cannot be undone, such as deleting past the Trash, says so before it happens ([DESIGN § Interaction](DESIGN.md#interaction)).
- Work survives a change of place, and leaving keypaste loses nothing: the vault stays a KeePass file the person can open in another app.

## Responsibility

Act in the person's interest, and be what keypaste claims to be.

Test: every claim on a screen, in the CLI, on keypaste.com or in a guide matches what the code does and what recorded evidence shows; you can say how long a person's vault outlives keypaste, how a fault gets fixed and what happens to their data at the end; and every allow says how long it lasts.

- No fake urgency, no hidden cost and no exit harder than the way in.
- When keypaste refuses something it says why, in plain words, and every allow says how long it lasts.
- A screen shows the state as it is: saved, unsaved or changed on disk, locked or unlocked, never a reassurance the code has not checked.
- A goal is named as a goal. keypaste.com may say what keypaste is built to become, such as ready for an enterprise, only on a page that labels what is published, building and planned (founder's direction, 2026-11-10).
- The vault is a KDBX file any KeePass app reads, the code is open under AGPL-3.0, and nothing keypaste builds needs a service to keep a local vault working.
- Temporary work counts too: a probe, a fixture or a workflow is deleted once it has served, and CI spends nothing that cannot change a verdict.

## Familiarity

Build on what people already know.

Test: someone who has used another password manager finds search, New, copy and lock without looking.

- Use the platform's own patterns and the words of the person's work, not keypaste's internal ones. The desktop follows Apple's Human Interface Guidelines on every platform, with Ctrl where macOS uses ⌘ and only where the platform has the concept; DESIGN governs where the two differ, and PRODUCT §3 governs both.
- Once a control behaves one way it behaves that way everywhere: a value pressed copies, a list is a list, a sort is a menu.
- Every action gets feedback, and controls look like what they do.

## Flexibility

Work for every person, device and way of using it.

Test: it works on all three platforms, by keyboard and screen reader, from 720 wide up to 200% zoom.

- Design for different abilities, languages, screens and text sizes ([DESIGN § Interaction](DESIGN.md#interaction)); accessibility is built in from the start, never added after.
- Content and controls keep predictable places as the window narrows: the list and the detail give way to one another, and nothing needs a pointer.
- At home on every platform, with the same care on each.

## Simplicity

Include only what earns its place.

Test: every element on a daily screen can say why it is there, and what is rare is one menu away.

- Decide for the person. Defaults are opinionated and safe ([PRODUCT §5.8](PRODUCT.md#5-product-laws)), and a setting exists only when real people need both answers.
- Each fact appears once in a view, where the person looks for it; an explanation lives in a tooltip or a Learn more, while a warning, a refusal or anything about safety stays visible.
- Safe without effort: the laws of §3 hold without the person having to understand them.
- One journey: download, first run, connecting an agent, the first approval and running a project are designed, built and walked through as one thing, on real machines, so that someone who has never used KeePass needs to ask no one.
- Simplicity is not minimalism. A view can be quiet or loud, but never random.

## Craft

Finish every detail.

Test: every state is designed, including empty, error, loading, locked and first use.

- Work from the system, written down in [DESIGN](DESIGN.md) before a screen is designed, not explained afterwards: the layout, the 4px spacing, the palette, the type scale and the icons.
- Care goes where people look, and where a bad state does the most harm; a prompt that releases a value is met often and does the most harm.
- When the budget runs short, cut scope, never the finish.

## Delight

Make it feel calm and trustworthy.

Test: the feeling is calm and trustworthy, and amber and the marks carry the character.

- Volume is quiet: flat ink, one amber per view, a calm voice. A credential tool earns trust by not shouting; the one loud thing is what needs the person ([DESIGN](DESIGN.md)).
- Mystery is little: everything a person needs is visible or one menu away, and an approval is never a discovery.
- Tempo is slow: the look changes only by founder direction recorded in DESIGN, and what people recognize keypaste by, the "k." icon, the wordmark, the amber and the four places' names, stays while what surrounds it changes.
- Delight is never decoration that gets in the way of the job.
