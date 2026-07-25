# PLAN.md — keypaste Roadmap
> This file evolves. CORE.md does not. Check items off as you go; move finished stages to the bottom.
> Companion files: `prompts.md` (ready-to-use build prompts per stage) and `ideas.md` (the parking lot).

---

## Decisions — LOCKED (July 2026, founder profile: .NET + Next.js, full-time, portfolio + business)
- [x] **Language/stack: C#/.NET 8+ for everything server-side** — `keypaste-core` (class library), `keypaste-cli` (console app, published as self-contained/AOT single binaries for macOS/Linux/Windows), `keypaste-mcp` (official ModelContextProtocol C# SDK, stdio transport). Massive tailwind: **KeePass itself is a .NET app** — the KDBX ecosystem is native to this stack.
- [x] **KDBX library sub-decision (resolved in 0.2):** `KeePassLib`, vendored from the KeePass 2.61 netstandard port. No maintained, adopted KDBX4 NuGet package for .NET exists; the two clean-shaped ones had days of commit history. KeePass is GPL-2.0-**or-later**, not GPL-2.0-only, so it combines with AGPL-3.0 and the licence never forced the choice — maturity did. Full survey in DECISIONS.md D-0007.
- [x] **License:** copyleft core (GPL/AGPL family, finalized alongside the KDBX lib choice for compatibility), MIT for any client SDK snippets. Protects against closed cloud clones.
- [x] **GUI framework (Stage 4): Next.js frontend + .NET local backend.** Desktop shell via **Photino.NET** (lightweight webview hosting your Next.js static export over the .NET core — one language for logic, your strongest UI stack for the interface) with Electron as fallback if Photino friction appears. The Next.js skills also cover the keypaste.com landing/marketing site directly.
- [x] **Timeline compression:** founder is full-time → target weeks in this plan are calendar-realistic, not aspirational. Stages 0–3 (launch) in ~6–8 weeks.
- [ ] **Name check:** trademark + NuGet + npm + GitHub org "keypaste" availability — grab all handles in Stage 0, week 1.

---

## Stage 0 — Foundation & proof of life (Week 1–2)
**Goal: a repo that opens, reads, and writes a real KDBX4 file, verified against KeePassXC.**
- [ ] Register GitHub org `keypaste`, npm/crates names, point keypaste.com to a one-line landing page with email capture
- [x] Repo scaffold: `keypaste-core`, `keypaste-cli`, `keypaste-mcp` (monorepo), CI, license, SECURITY.md, this file trio
- [x] Pick KDBX library; write round-trip test: create vault → add entry → save → open in KeePassXC → verify — vendored KeePassLib 2.61, see DECISIONS.md D-0007
- [x] CI job that runs KeePassXC-cli against generated files (compatibility law #6) — all three OSes, permanent, see DECISIONS.md D-0008
- [x] `keypaste init`, `keypaste add`, `keypaste get`, `keypaste ls` (plus `rm`) working in CLI — see DECISIONS.md D-0009..D-0012
- **Exit demo:** terminal clip — create vault, add a secret, open same file in KeePassXC GUI.

## Stage 1 — Env variables & injection (Week 3–4)
**Goal: keypaste replaces .env files.**
- [ ] Entry convention for env sets (KDBX group = project, custom fields = KEY→value) — stays 100% KeePassXC-readable
- [ ] `keypaste env pull <project>` (import existing .env), `keypaste env ls`
- [ ] `keypaste run <project> -- <cmd>`: inject into child process env, nothing written to disk
- [ ] `keypaste export --dotenv` (with loud warning) for escape-hatch compatibility
- [ ] Docs: "Replace your .env in 5 minutes" guide
- **Exit demo:** delete a project's .env, `keypaste run dev -- npm start`, app boots.

## Stage 2 — The MCP bridge (Week 5–7) ← the headline feature
**Goal: Claude can request ONE credential, you approve, it's scoped and logged.**
- [ ] MCP server (`keypaste-mcp`) with tools: `list_entry_names` (names only, never secrets), `request_credential(entry, reason, ttl)`
- [ ] Human approval flow: OS-native prompt (or CLI confirm) showing agent, entry, reason — default deny, timeout deny
- [ ] Scoping: response contains only the requested field; TTL after which cached grant expires
- [ ] Append-only local audit log (JSONL + `keypaste log` pretty view)
- [ ] Policy file for pre-approvals (e.g. "Claude Code may read entries in group 'dev/*' without prompting")
- [ ] Threat-model doc: confused deputy, prompt injection via entry names, log tampering — and mitigations
- [ ] Setup guide for Claude Desktop + Claude Code
- **Exit demo (THE demo):** ask Claude "deploy this, get the API key from my vault" → approval popup → Claude proceeds → `keypaste log` shows the access. 60 seconds.

## Stage 3 — Launch (Week 8–9)
**Goal: strangers using it.**
- [ ] Polish README with the demo GIF at top
- [ ] Landing page: the 60-second demo video, install one-liner, "local-first, KDBX, open source" trust bullets
- [ ] Launch posts: Hacker News (Show HN), r/selfhosted, r/KeePass, MCP community/Discord, lobste.rs, X
- [ ] Write the launch essay: "Your password manager can't talk to AI. Here's why that's a problem." 
- [ ] Respond to every issue/comment for 2 weeks straight
- **Benchmarks:** tracked privately, outside this repo.

## Stage 4 — Modern GUI (Week 10–14)
**Goal: the KeePass reskin — a vault UI that doesn't look like 2003.**
- [ ] Tauri app wrapping keypaste-core: vault unlock, entry browse/search, env-set editor
- [ ] The differentiating screen: **Agent Activity** — live feed of agent requests, approve/deny buttons, history (this is the delegation dashboard seed)
- [ ] Approval prompts move from CLI to native windows/tray
- [ ] Design language: modern, calm, trustworthy (see ideas.md → UI section for direction)
- **Exit demo:** side-by-side screenshot — KeePass classic vs keypaste — same file, different decade.

## Stage 5 — Sharing & tiny teams (Month 4–5)
**Goal: first monetizable surface, without violating local-first.**
- [ ] `keypaste share` — one-time encrypted secret links (client-side encrypted, self-hostable relay; the relay never sees plaintext)
- [ ] Multi-vault + vault-per-project ergonomics
- [ ] Git-friendly vault workflows / conflict guidance (teams sync the .kdbx via their own git/drive)
- [ ] Optional hosted relay + sync = first paid tier (convenience, not security — CORE law)
- **Benchmark:** tracked privately, outside this repo.

## Stage 6 — Delegation dashboard (v2, only after traction)
**Goal: expand Agent Activity into "everything that can act as you."**
- [ ] Aggregate: keypaste agent grants + MCP server connections + (feasibility-gated) OAuth grant views for GitHub/Google
- [ ] Revocation center, stale-grant nudges ("this agent hasn't used this in 60 days")
- [ ] Position: "the control panel for everything that can act as you" — now earned, anchored to a real product
- **Gate:** only start once the Stage 3 and Stage 5 benchmarks have actually been hit.

---

## Definition of "focused"
At any moment you should be able to answer: *which stage am I in, which checkbox is next?* If you can't, stop, open this file, pick the next unchecked box, ignore everything else.
