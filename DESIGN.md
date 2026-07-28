# DESIGN.md — proposed design system

> **Status: proposed, not accepted.** Nothing here has been built or decided. It is a visual
> exploration parked in the repo so it does not get lost between now and Stage 3–4, where it
> becomes relevant. `docs/STEPS.md` says which stage is actually in progress.

[`design.html`](design.html) holds the exploration: a landing page, the agent approval dialog, an
app shell, and six identity marks with one applied example.

It is an export from a design tool, not a working page — it loads a `support.js` that is not in
this repository, and it uses a non-standard `style-hover` attribute for hover states. Open it to
look at, not to build from.

## Where it lands in the plan

The approval dialog is the closest to being real: Stage 2 ships the MCP bridge and its approval
flow, and that flow needs a shape. The landing page belongs to Stage 3, the app shell to Stage 4.

## One tension to resolve before any of it is built

The exploration is themed *"hosted underneath, invisible on top"*, and the landing page's first
proof point is *"sign in and your keys are there — on your laptop, your other laptop, your
terminal."*

docs/PRODUCT.md §2 permits this, but conditionally, and the condition is the whole sentence:

> **NOT** a cloud service that holds user secrets. Local-first forever. Sync is the user's problem
> (their file, their Dropbox/Syncthing/whatever) until/unless a **zero-knowledge** hosted tier is
> added — and even then, **self-host must remain first-class**.

The design already answers the second half — *"export or self-host the same product, any day, no
conversation required"* is on the page. Two things are still open:

1. **Sign-in as the opening move inverts §4.1**, *"the core works with no network at all."* A
   networked default is a different product from an offline-first one with optional sync. The
   reconcilable version is sign-in as opt-in sync, not as how you get your keys at all.
2. **"the format… never appears in the product"** is fine as UI — nobody should see a file path.
   But KDBX *is* the trust argument (§1: "ride existing KeePass trust"), so invisible-in-the-UI
   must not drift into unmentioned-anywhere. Routing it to `/security`, as the mock does, is
   probably the right answer; it should be a deliberate one.

Neither is a blocker for anything currently being built. Both need a decision record before the
hosted tier exists, because the answer constrains the MCP bridge in Stage 2.

## Stage 4.1 did not adopt this

The desktop shell built in 4.1 took the palette values and the type scale from here, re-derived by
hand into XAML resources, and nothing else. Two reasons, and the second is the real one.

The mechanical one: `design.html` is a design-tool export that loads a `support.js` this repository
does not have and uses a non-standard `style-hover` attribute, so there was no markup to carry over
even before the shell turned out not to be a webview at all (DECISIONS.md D-0044).

The one that matters: the tension above is still open. The exploration's opening move is sign-in,
and 4.1 had no standing to ship a premise that inverts docs/PRODUCT.md §4.1 while the question of whether
keypaste is offline-first with optional sync — or something you log into — has never been answered.
Adopting the visual language would have been the thin end of adopting the framing. The landing page
and the approval dialog are untouched by this note; the approval dialog is Stage 4.3's, and it is
the surface this exploration got closest to right.
