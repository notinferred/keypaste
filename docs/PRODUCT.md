# PRODUCT.md — The keypaste Constitution
> **DO NOT MODIFY THIS FILE.** Everything else in this repo can change. This cannot.
> If a decision conflicts with this file, the decision is wrong.
> Last ratified: July 2026 (v1.0)

---

## 1. What keypaste IS

**keypaste is the safe bridge between your credentials and the AI era.**

One sentence pitch: *"Stop pasting secrets into chats. keypaste is a local-first, KDBX-compatible vault that stores your passwords AND env variables, injects them into your projects, and lets AI agents like Claude request exactly one credential — with your approval, scoped access, and a full audit trail — without ever seeing your vault."*

The product is a **wedge**, not a platform. The wedge is:
1. KDBX-compatible vault (ride existing KeePass trust, never invent a new format)
2. Env variable / API key storage + injection (`keypaste run -- npm start`)
3. A safe MCP server for AI agents (scoped, approved, audited access)
4. The audit log grows into the delegation dashboard (v2, earned — not built first)

## 2. What keypaste is NOT (permanent scope walls)

- **NOT** a new proprietary vault format. KDBX or nothing.
- **NOT** a cloud service that holds user secrets. Local-first forever. Sync is the user's problem (their file, their Dropbox/Syncthing/whatever) until/unless a zero-knowledge hosted tier is added — and even then, self-host must remain first-class.
- **NOT** "for everyone." The user is a developer, indie hacker, power user, or tiny team. Consumers with only browser passwords are served by Apple/Google/Bitwarden — do not chase them.
- **NOT** an enterprise IAM/NHI platform. Do not compete with Descope, Token Security, Okta. If an enterprise wants it, they self-host the open-source version.
- **NOT** feature-complete-first. Ship the wedge. Say no to everything else until the wedge has users.

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

1. **Local-first, offline-capable.** The core works with no network at all.
2. **CLI-first, UI-second.** Every feature exists in the CLI before it gets a GUI. The GUI calls the same core library the CLI does.
3. **One core library** (`keypaste-core`) that CLI, GUI, and MCP server all share. No logic duplicated in frontends.
4. **Cross-platform from day one** (macOS, Linux, Windows) — pick a stack that makes this cheap.
5. **Tests on the secret path are mandatory.** No untested code touches encryption, injection, or the agent bridge.
6. **Compatibility is sacred:** any KDBX file keypaste writes must open correctly in KeePassXC. This is tested in CI against real KeePassXC.
7. **Small releases, real changelogs, semantic versioning.**
8. **Documentation ships with the feature**, not after.

## 5. Product laws

1. **The demo is the marketing.** Every stage must end in something demoable in under 60 seconds.
2. **Solve your own pain first.** If you (the founder) don't use keypaste daily, don't ship it to others.
3. **Community before customers.** KeePass forums, MCP ecosystem, HN, r/selfhosted — earn credibility there before any paid tier exists.
4. **Monetize the convenience, never the security.** Free/self-host tier is fully secure and fully functional. Paid tiers sell hosting, sync convenience, team features, support — never "more encryption."
5. **One founder, one focus.** New ideas go to docs/IDEAS.md, not into the sprint. docs/IDEAS.md is where ideas wait their turn — most wait forever.

## 6. Decision tiebreakers (when stuck, in order)

1. Does it protect user trust? → if it risks trust, no.
2. Does it serve the wedge (KDBX + env + agent bridge)? → if not, docs/IDEAS.md.
3. Can one person ship it in ≤2 weeks? → if not, cut scope until yes.
4. Would it make the 60-second demo better? → prefer the option that demos.
5. Boring beats clever. Shipped beats perfect. Focused beats big.

---
*If you are reading this months from now, tired, tempted to pivot to "for everyone" or to add a cloud vault or a new format: the answer is still no. Re-read section 2.*
