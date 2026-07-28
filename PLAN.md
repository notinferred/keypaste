# PLAN.md — keypaste Roadmap
> This file evolves. CORE.md does not. Check items off as you go; move finished stages to the bottom.
> Companion files: `prompts.md` (ready-to-use build prompts per stage) and `ideas.md` (the parking lot).

---

## Decisions — LOCKED (July 2026, founder profile: .NET + Next.js, full-time, portfolio + business)
- [x] **Language/stack: C#/.NET 8+ for everything server-side** — `keypaste-core` (class library), `keypaste-cli` (console app, published as self-contained/AOT single binaries for macOS/Linux/Windows), `keypaste-mcp` (official ModelContextProtocol C# SDK, stdio transport). Massive tailwind: **KeePass itself is a .NET app** — the KDBX ecosystem is native to this stack.
- [x] **KDBX library sub-decision (resolved in 0.2):** `KeePassLib`, vendored from the KeePass 2.61 netstandard port. No maintained, adopted KDBX4 NuGet package for .NET exists; the two clean-shaped ones had days of commit history. KeePass is GPL-2.0-**or-later**, not GPL-2.0-only, so it combines with AGPL-3.0 and the licence never forced the choice — maturity did. Full survey in DECISIONS.md D-0007.
- [x] **License:** copyleft core (GPL/AGPL family, finalized alongside the KDBX lib choice for compatibility), MIT for any client SDK snippets. Protects against closed cloud clones.
- [x] **GUI framework (Stage 4): Avalonia over `Keypaste.Core`, in one .NET process** *(amended in 4.1 — DECISIONS.md D-0044)*. The original decision here read "Next.js frontend + .NET local backend, desktop shell via Photino.NET … with Electron as fallback if Photino friction appears", and that fallback clause is what fired: Photino's last code commit was January 2025, it targets `net8.0;net9.0` only, and its Linux natives need glibc 2.38 against this project's published floor of 2.35 — the GUI would not have run on the oldest Linux the CLI supports. Avalonia keeps the one property that mattered (a plain `ProjectReference` to the core, no bridge, law 4.3 by construction), targets `net10.0`, needs no GTK or WebKit on Linux, and removes the webview from the process holding the unlocked vault. The cost is that the UI is XAML rather than web; the Next.js-covers-keypaste.com rationale was already stale, since the site is one hand-written HTML file behind a Cloudflare Worker.
- [x] **Timeline compression:** founder is full-time → target weeks in this plan are calendar-realistic, not aspirational. Stages 0–3 (launch) in ~6–8 weeks.
- [ ] **Name check:** trademark + NuGet + npm + GitHub org "keypaste" availability — grab all handles in Stage 0, week 1.

---

## Stage 0 — Foundation & proof of life (Week 1–2)
**Goal: a repo that opens, reads, and writes a real KDBX4 file, verified against KeePassXC.**
- [ ] Register GitHub org `keypaste`, npm/crates names, point keypaste.com to a one-line landing page with email capture — **the page and the email capture landed in 3.1** (Cloudflare Worker → PlanetScale Postgres, D-0037); the org and the package names are what is still open here
- [x] Repo scaffold: `keypaste-core`, `keypaste-cli`, `keypaste-mcp` (monorepo), CI, license, SECURITY.md, this file trio
- [x] Pick KDBX library; write round-trip test: create vault → add entry → save → open in KeePassXC → verify — vendored KeePassLib 2.61, see DECISIONS.md D-0007
- [x] CI job that runs KeePassXC-cli against generated files (compatibility law #6) — all three OSes, permanent, see DECISIONS.md D-0008
- [x] `keypaste init`, `keypaste add`, `keypaste get`, `keypaste ls` (plus `rm`) working in CLI — see DECISIONS.md D-0009..D-0012
- **Exit demo:** terminal clip — create vault, add a secret, open same file in KeePassXC GUI.

## Stage 1 — Env variables & injection (Week 3–4)
**Goal: keypaste replaces .env files.**
- [x] Entry convention for env sets (KDBX group `env/<project>`, one entry per variable: title=KEY, password=value) — stays 100% KeePassXC-*editable*, gated in CI in both directions (D-0014)
- [x] `keypaste env pull <project> [file]` (import existing .env, then offer to delete it) — fail-closed parser in the core, no "shred" claim (D-0015); `env ls` / `env set` / `env rm` shipped in 1.1
- [x] `keypaste run <project> -- <cmd>`: inject into child process env, nothing written to disk — gated in CI on all three OSes, signals relayed rather than escalated (D-0016)
- [x] `keypaste env export <project> --dotenv` (red warning, confirmation, `--stdout` for pipes) — spelled `env export`, not `export`; single-quoted output so other .env readers agree (D-0018)
- [x] Docs: "Replace your .env in 5 minutes" guide — `docs/replace-dotenv.md`, with CI notes and the FAQ
- **Exit demo:** delete a project's .env, `keypaste run dev -- npm start`, app boots.

## Stage 2 — The MCP bridge (Week 5–7) ← the headline feature
**Goal: Claude can request ONE credential, you approve, it's scoped and logged.**
- [x] MCP server (`keypaste-mcp`) with tools: `list_entry_names` (names only, never secrets), `request_credential(entry, reason, ttl)` — stdio, `ModelContextProtocol.Core` pinned at 1.4.1 (D-0019). It shipped in 2.1 denying every request, because the approval flow did not exist yet and a server started with no terminal cannot ask for a master password (D-0022); 2.2 gave it somewhere to forward to, and the bridge still holds no vault and decides nothing (D-0023)
- [x] Human approval flow: `keypaste agent` — a process you start in your own terminal — shows client, entry, field, reason and lifetime; default deny, 45-second timeout deny (under every MCP client's own 60s request timeout, D-0025). A native OS dialog is still to come; the terminal prompt is the channel today
- [x] Scoping: the response carries one field value and nothing else, proved against four sentinels in one entry (`SecretHygieneTests`); grants expire on a TTL capped by `--max-ttl` and are scoped to one connection (D-0026)
- [x] Append-only local audit log (JSONL + `keypaste log` pretty view) — a table with `--denied`, `--client` and `--since`, plus a per-record hash chain and `keypaste log verify`, which names what it cannot detect on every pass rather than only on a failure (D-0031, D-0032); a log keypaste cannot link a record onto stops the bridge at startup
- [x] Policy file for pre-approvals — `~/.keypaste/policy.toml`, read once by `keypaste agent`, evaluated after the exposure re-check so a rule can only ever narrow (D-0029); keyed on the operator's `--client-label` rather than the name an agent asserts (D-0030); anything wrong with the file means the whole of it is ignored and every request prompts (D-0028). `keypaste policy ls` renders what each pattern parsed to rather than the line that was typed
- [x] Threat-model doc: confused deputy, prompt injection via entry names, log tampering — and mitigations — `THREATS.md` grew T-10 to T-12 in 2.2 and closed T-7; 2.3 resolved T-3, closed T-6's named gap and added T-13 to T-17, including T-14, the one place the policy file makes keypaste weaker than 2.2; **2.4 closed T-5**, gave T-12's divergence a reader, and promoted memory dumping, clipboard scraping and a stolen vault file into T-18 to T-20. One deferral outlives the stage and says so: T-13 still cannot show which entries a rule matches today, because that needs an open vault, so it waits for the GUI
- [x] Setup guide for Claude Desktop + Claude Code — `docs/mcp-setup.md`, with the config snippets for both clients, what `--expose` governs, the audit log format and its `method` vocabulary, and how to read it back
- [x] The flagship demo, end to end — `docs/demo.md`: a committed offline fixture (`scripts/demo/deploy.sh`) that refuses without `STRIPE_KEY`, a real `keypaste agent`, a real approval, and `keypaste log` as the payoff. `scripts/verify-demo.sh` holds every transcript on the page to what the shipped binaries actually print, on all three OSes, and says plainly that it does not run Claude and cannot (D-0034). Startup and latency were measured rather than guessed, and exactly one number justified touching code (D-0035)
- **Exit demo (THE demo):** ask Claude "deploy this, get the API key from my vault" → approval popup → Claude proceeds → `keypaste log` shows the access. 60 seconds.

## Stage 3 — Launch (Week 8–9)
**Goal: strangers using it.**
- [ ] Polish README with the demo GIF at top — the rewrite is done (D-0036): hero, pitch, trust bullets, honest install, MCP snippet, sourced comparison table, and both pages' transcripts now held to the binaries by `scripts/verify-demo.sh`. **What is left is only the GIF** — the take, the trim to under 2 MB, and dropping it into the slot both pages already reserve. The pipeline is `scripts/demo/` (D-0033) and is WSL-only, needs a real Claude session and a human keystroke, and budgets three to eight takes
- [x] Landing page: the 60-second demo video, install one-liner, "local-first, KDBX, open source" trust bullets — `site/public/index.html`, rewritten in place with the same GIF slot, plus the comparison table and the signup form. The install block became a real per-OS one-liner in 3.4 once there was something to install, held to the published assets by `scripts/verify-install.sh`; build-from-source stays on the page permanently because it is strictly stronger (THREATS.md T-21)
- [x] Release pipeline: **`v0.1.0` is published** at `https://dl.keypaste.com/v0.1.0/` — four native binaries plus the corresponding source, each with a checksum, every gate run against the exact bytes uploaded. Distribution is Cloudflare R2 rather than a GitHub Release because this repository is private and private release assets 404 for everyone (D-0041); `osx-x64` was dropped with reasons and O-0006 was answered yes (D-0040). The documented install commands are now executed verbatim on Ubuntu 2404, macOS 15 and Windows 2025 by `.github/workflows/install.yml`, weekly and on demand, with a corrupted-download negative control on the two Unix legs. Its first real run failed and was right to — see D-0043 for the two reasons it had never run at all

- [ ] Launch posts: Hacker News (Show HN), r/selfhosted, r/KeePass, MCP community/Discord, X
- [x] Write the launch essay — [`docs/keepass-and-agents.md`](docs/keepass-and-agents.md), retitled "Your **KeePass vault** can't talk to AI — and everyone is pasting secrets into chats instead" because the original title is false: 1Password, Bitwarden and Keeper all ship request-and-approve flows, and all three are named in the essay's second paragraph (D-0038). Its transcripts are in `verify-demo.sh`'s `TRANSCRIPT_PAGES`, so the essay cannot drift from the binaries either
- [ ] Respond to every issue/comment for 2 weeks straight
- **Benchmarks:** tracked privately, outside this repo.

## Stage 4 — Modern GUI (Week 10–14)
**Goal: the KeePass reskin — a vault UI that doesn't look like 2003.**
- [x] Desktop app (Avalonia) over keypaste-core — **shell and unlock done in 4.1** (D-0044): open a vault by drag, picker or recent list; master password in a control that holds no password and whose automation peer exposes nothing; idle auto-lock on two clocks, defaulting to 5 minutes, that also locks a machine which slept through the timeout; a five-destination sidebar with `Ctrl/Cmd+1..5` and `Ctrl/Cmd+L`; light and dark following the OS. The Log view is real and renders `AuditText` verbatim, so it and `keypaste log` cannot drift (D-0032)
- [ ] Entry browse/search and the env-set editor — **4.2**. Entries and Env Sets ship in 4.1 as empty states naming the CLI command that already does the job
- [ ] The differentiating screen: **Agent Activity** — live feed of agent requests, approve/deny buttons, history (this is the delegation dashboard seed)
- [ ] Approval prompts move from CLI to native windows/tray
- [ ] Design language: modern, calm, trustworthy (see ideas.md → UI section for direction)
- **Exit demo:** side-by-side screenshot — KeePass classic vs keypaste — same file, different decade.

**Where 4.1 left it.** The app opens a real KDBX written by the shipped CLI and holds it in a session
built to the `Func<Vault?>` shape `VaultCredentialSource` already takes, so 4.3 wires the approver in
without changing it. Three things it deliberately does not do, each stated in the app itself rather
than left to be discovered: it is **not the approver** — a running `keypaste agent` still answers
agent requests, and the Agent Activity screen probes for one and says so; it **ships in no release**,
so `release.yml` is untouched and there is nothing on `dl.keypaste.com` to install; and it sets no
`PublishAot`, so the CLI's single-file property is not yet claimed for it (O-0016). `keypaste agent`
still has no idle lock of its own — 4.1's auto-lock is the app's, and `docs/approvals.md` says so.
CHANGELOG.md is untouched on purpose: it is written for somebody deciding whether to upgrade, and
there is nothing yet to upgrade to.

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
