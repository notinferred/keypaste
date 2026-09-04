# PRODUCT.md — The keypaste Constitution
> **This file changes only by a dated re-ratification.** §3 does not change at all. Every other section may be rewritten by the founder, once, with a date, and a `D-` row in `DECISIONS.md` saying why. If a decision conflicts with the current text, the decision is wrong. Last ratified: 2026-09-04 (v1.1, D-0061). v1.0 was July 2026 and is in git.

---

## 1. What keypaste IS

**keypaste is a KeePass-compatible password manager for people who work with AI agents.**

One sentence pitch: *"A cleaner KeePassXC: your passwords and env variables in an ordinary KDBX file you own, synced through a service you can pay for or run yourself, and the only vault that lets an AI agent ask for exactly one credential — with your approval, a lifetime you were shown, and a log line — without ever seeing the vault."*

The product is **freemium**. Free is the whole password manager. Paid is hosting and teams. In the order they are built and sold:

1. The KDBX-compatible vault and the desktop app that opens it (ride existing KeePass trust, never invent a new format)
2. The agent bridge — scoped, approved, audited access over MCP — which is the selling point no other vault has
3. Env variable / API key storage and injection (`keypaste run -- npm start`), the developer's reason to arrive
4. Hosted zero-knowledge sync, which is the business, with self-hosting of the same binary first-class
5. "What can act as you right now" — the headline number, read from the running agent, which grows into the delegation dashboard

## 2. What keypaste is NOT (the walls that remain)

- **NOT** a new proprietary vault format. KDBX or nothing.
- **NOT** a cloud service that can read your secrets. The hosted tier stores an encrypted blob and client-held keys and can decrypt nothing (D-0060). A forgotten master password is gone. Self-host is the same binary and stays first-class.
- **NOT** chasing browser-only consumers who have no vault and want none. Apple, Google and the browser serve them. keypaste is for the person who already keeps, or is ready to keep, a file of their own.
- **NOT** an enterprise IAM/NHI platform. Do not compete with Descope, Token Security, Okta. If an enterprise wants it, they self-host.
- **NOT** everything at once. `docs/STEPS.md` orders the work in tiers, and a tier is not started until the one before it is sold or shipped.

## 3. Security laws (violating any of these kills the project's only asset: trust)

1. **The vault master key never leaves the local process.** No exceptions, no telemetry of it, no "encrypted backup to our servers."
2. **Agents NEVER get the vault.** Agents get: one credential, one scope, one TTL, after one explicit human approval (or a pre-approved policy the human wrote). Default is deny.
3. **Every agent access is logged** — immutably, locally, human-readable: who/what, which entry, when, granted/denied.
4. **No secret ever touches disk unencrypted** by keypaste's doing. Injection is into process environment memory, not into files.
5. **No analytics/telemetry on secret content or entry names. Ever.** Opt-in anonymous usage counts only.
6. **All crypto is boring.** Use the KDBX4 spec (Argon2, AES-256/ChaCha20) via mature audited libraries. NEVER write custom crypto. NEVER "improve" the format.
7. **Fail closed.** Any error path in the agent bridge results in denial, not exposure.
8. **The code is open source (permissive or copyleft — decided once, in docs/STEPS.md) and stays open.** Auditable code is the trust strategy for an unknown founder.
9. **Dependencies are minimized and pinned.** Every new dependency on the secret path requires written justification in the PR.
10. **Vulnerability reports get a security policy, a private contact, and honesty.** If breached or a serious bug ships, disclose fast and fully.

## 4. Engineering laws

1. **Local-first, offline-capable.** The core works with no network at all. Sync is an addition to a file that already works, never a precondition.
2. **Core-first.** Every feature lives in `Keypaste.Core`; the CLI and the desktop app are both thin over it, and neither waits for the other. A feature that exists in one front end and not the other says so on the page.
3. **One core library** that CLI, GUI, MCP server and relay client all share. No logic duplicated in frontends.
4. **Cross-platform from day one** (macOS, Linux, Windows) — pick a stack that makes this cheap.
5. **Tests on the secret path are mandatory.** No untested code touches encryption, injection, sync, or the agent bridge.
6. **Compatibility is sacred:** any KDBX file keypaste writes must open correctly in KeePassXC. This is tested in CI against real KeePassXC.
7. **Small releases, real changelogs, semantic versioning.**
8. **Documentation ships with the feature**, not after.

## 5. Product laws

1. **The demo is the marketing.** Every stage must end in something demoable in under 60 seconds.
2. **Solve your own pain first.** If you (the founder) don't use keypaste daily, don't ship it to others.
3. **Community before customers.** KeePass forums, MCP ecosystem, HN, r/selfhosted — earn credibility there before any paid tier exists.
4. **Monetize the convenience, never the security.** Free/self-host tier is fully secure and fully functional. Paid tiers sell hosting, sync convenience, team features, support — never "more encryption", and never a signature the free binary lacks.
5. **One founder, one focus.** New ideas go to docs/IDEAS.md, not into the sprint. docs/IDEAS.md is where ideas wait their turn — most wait forever.
6. **Free is the whole password manager.** CLI, app, agent bridge, browser extension, TOTP, SSH, importers, and the relay binary to run yourself. Paid is the relay somebody else runs, and what teams need on top of it.

## 6. Decision tiebreakers (when stuck, in order)

1. Does it protect user trust? → if it risks trust, no.
2. Does it serve the order in §1? → if not, docs/IDEAS.md.
3. Can one person ship it in ≤2 weeks? → if not, cut scope until yes.
4. Would it make the 60-second demo better? → prefer the option that demos.
5. Boring beats clever. Shipped beats perfect. Focused beats big.

---
*If you are reading this months from now, tired, tempted to add a format, to let the server read a vault, or to build for people who will never keep a file: the answer is still no. Re-read section 2. Everything else in here can be re-ratified, with a date, and that is the only way it changes.*
