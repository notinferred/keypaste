# DESIGN.md — proposed design system

> **Status: proposed, not accepted.** Nothing here has been built or decided. It is a visual
> exploration parked in the repo so it does not get lost between now and Stage 3–4, where it
> becomes relevant. `PLAN.md` says which stage is actually in progress.

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

CORE.md §2 permits this, but conditionally, and the condition is the whole sentence:

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
