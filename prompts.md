# prompts.md — The keypaste Prompt Library
> Copy-paste these into Claude Code (or any coding agent), one at a time, in order.
> Rule: paste CORE.md into context (or reference it) with EVERY prompt. Finish and verify each prompt's goal before moving to the next.
> Each prompt = one goal = roughly one working session.
> Checkboxes mirror PLAN.md — check one here only when its PLAN.md box is checked. Maintenance prompts recur and are never checked.

---

## STAGE 0 — Foundation

- [x] **0.1 — Repo scaffold**
"You are building keypaste, a local-first KDBX-compatible secrets tool. Read CORE.md and treat it as law. Create a monorepo with three packages: keypaste-core (all vault logic), keypaste-cli (thin CLI over core), keypaste-mcp (placeholder for now). Set up [CHOSEN LANGUAGE] tooling, formatter, linter, test runner, a CI workflow that runs tests on push for macOS/Linux/Windows, an AGPL LICENSE for core, a SECURITY.md with a private disclosure contact, and a README stub with the one-sentence pitch from CORE.md section 1. No vault logic yet — just a clean skeleton where `core` exposes a hello function that cli calls, with one passing test proving the wiring."

- [x] **0.2 — KDBX round-trip**
"In keypaste-core, integrate the most mature KDBX4 library for our stack (evaluate options briefly, pick the audited/most-maintained one, justify in a DECISIONS.md entry). Implement: create a new KDBX4 vault with a master password (Argon2 defaults per spec), add an entry (title, username, password, url, notes), save to disk, reopen, and read back. Write a round-trip test proving byte-level reopenability and field integrity. NEVER implement any cryptography yourself — library only (CORE.md law 3.6). Then add a CI step that installs keepassxc-cli and verifies our generated vault opens and lists entries correctly in KeePassXC — this compatibility test is permanent and must never be removed."

- [x] **0.3 — Core CLI verbs**
"Build the keypaste CLI with these commands, all calling keypaste-core only: `keypaste init <vault.kdbx>` (prompts for master password twice, creates vault), `keypaste add <entry>` (interactive prompts for fields, password input hidden), `keypaste get <entry>` (prints password to stdout ONLY with --show, otherwise copies to clipboard with auto-clear after 20s), `keypaste ls` (tree view of groups/entries, names only), `keypaste rm <entry>`. Master password prompt must never echo. Add --vault flag and KEYPASTE_VAULT env var for vault path. Write integration tests for every command. Keep output clean and script-friendly; errors go to stderr with nonzero exit codes."

## STAGE 1 — Env variables

- [x] **1.1 — Env storage convention**
"Design and implement keypaste's env-set convention inside standard KDBX: a KDBX group named `env/<project>` where each entry is one variable (title=KEY, password=value), OR one entry per project using custom string fields — evaluate both for KeePassXC readability and pick one, documenting why in DECISIONS.md. Implement `keypaste env ls [project]` and `keypaste env set <project> KEY=value` and `keypaste env rm <project> KEY`. Everything must remain fully viewable and editable in vanilla KeePassXC — add a compatibility test proving a KeePassXC-edited value is picked up by keypaste."

- [x] **1.2 — Import and inject**
"Implement `keypaste env pull <project> [path/to/.env]` which parses a dotenv file (handle quotes, comments, multiline values, export prefixes) and stores every variable into the project's env set, printing a summary and offering to shred the original file. Then implement the flagship command `keypaste run <project> -- <command...>`: unlock vault, load the project's variables, spawn the child process with them merged into its environment, and stream stdio transparently, forwarding exit codes and signals. Secrets must exist only in process memory — never written to any temp file (CORE.md law 3.4). Test with a script that echoes an injected variable, and test signal forwarding with a long-running child."

- [x] **1.3 — Escape hatches & docs**
"Implement `keypaste env export <project> --dotenv` that writes a .env file only after an explicit interactive confirmation and prints a red warning that plaintext secrets are now on disk. Add `--stdout` variant for piping. Then write docs/replace-dotenv.md: a 5-minute guide taking a developer from an existing .env to `keypaste run`, including CI notes (KEYPASTE_VAULT + a dedicated CI vault, never the personal one) and a FAQ covering 'what if I lose my master password' (honest answer: it's gone — that's the point) and 'how do I sync' (your file, your sync tool)."

## STAGE 2 — MCP bridge (the headline)

- [x] **2.1 — MCP server skeleton**
"Build keypaste-mcp: an MCP server (official SDK, stdio transport) exposing exactly two tools. Tool `list_entry_names` returns entry titles and group paths ONLY — never usernames, never secrets, and it must sanitize titles defensively when returning them since entry names could contain prompt-injection text; document this risk in a THREATS.md you create now. Tool `request_credential` takes {entry, field, reason, ttl_seconds} and for now always returns DENIED — the approval flow comes next. Wire it so Claude Desktop and Claude Code can connect (write the config snippet in docs/mcp-setup.md). Every call, allowed or denied, appends a JSONL line to the audit log: timestamp, client info, tool, args, decision."

- [x] **2.2 — Human approval flow**
"Implement the approval gate for request_credential per CORE.md law 3.2: when an agent requests a credential, show the human a confirmation displaying agent/client name, entry requested, the agent's stated reason, and TTL — via a native OS dialog if available, else a terminal prompt on the keypaste daemon side. Default deny; 60-second timeout is deny; any error path is deny (fail closed, law 3.7). On approval, return ONLY the requested field value, and hold the grant in memory so repeat requests for the same entry within the TTL don't re-prompt. Write tests simulating approve, deny, timeout, and error paths, asserting the secret appears in exactly one path."

- [x] **2.3 — Policy pre-approvals**
"Add a policy file (~/.keypaste/policy.toml) letting the user pre-authorize narrow patterns, e.g. allow client 'claude-code' to read field 'password' of entries matching group 'env/dev*' with ttl<=3600 and no prompt. Parse and validate strictly — any malformed policy means the policy is ignored entirely and everything prompts (fail closed). `keypaste policy ls` shows active rules in plain English. Log policy-based grants distinctly in the audit log. Add tests: matching rule grants silently, non-matching prompts, malformed file prompts everything, and a rule can never widen to secrets outside its pattern."

- [x] **2.4 — Audit log & threat model**
"Implement `keypaste log` rendering the JSONL audit trail as a readable table (time, client, entry, decision, method: prompt/policy) with filters --denied, --client, --since. Make the log append-only from the app's perspective and add a per-line hash chain so tampering is detectable; `keypaste log verify` checks the chain. Then complete THREATS.md covering at minimum: confused-deputy via a malicious MCP client, prompt injection through entry names/reasons, audit log tampering, clipboard scraping, memory dumping, and a stolen vault file — with our mitigation or honest 'out of scope' for each."

- [x] **2.5 — The 60-second demo**
"Prepare the flagship demo end to end: a script (docs/demo.md) where the user asks Claude Code to run a deploy that needs an API key, Claude calls keypaste's request_credential, the approval dialog appears with the reason, the user approves, the deploy proceeds, and `keypaste log` shows the whole exchange. Fix every rough edge that breaks the flow: startup time, unclear dialog wording, error messages. Record the happy path as a terminal cast/GIF. This demo is the marketing (CORE.md law 5.1) — polish it like a product surface."

## STAGE 3 — Launch

- [ ] **3.1 — README & landing**
"Rewrite the README as a launch-grade front page: demo GIF first, then the one-sentence pitch, then three trust bullets (local-first & offline, standard KDBX — your vault opens in KeePassXC, open source AGPL), then install one-liners per OS, then the MCP setup snippet, then a comparison table vs plain KeePass / 1Password / Infisical focused only on our wedge. Also produce a single-file static landing page for keypaste.com with the same content plus an email signup, no trackers, no cookies."
> Done except the GIF (D-0036, D-0037). The install one-liners were **not** written — there is nothing to install yet, so both pages say so and 3.4 below owns the fix. Both pages reserve the GIF slot and both are now held to the binaries by `scripts/verify-demo.sh`; recording the take is a WSL session with a human in it, and it is the only thing between here and checking this box.

- [x] **3.2 — Launch essay**
"Write the launch essay 'Your password manager can't talk to AI — and everyone is pasting secrets into chats instead': open with the real behavior (people paste API keys into LLM chats and .env files into agents), explain why vaults stayed offline while agents arrived, introduce the scoped-request + approval + audit model, show the demo, and end with the local-first/KDBX/open-source stance. Honest, technical, no marketing fluff, ~1200 words, ready for a blog post and adaptable to a Show HN text."
> [`docs/keepass-and-agents.md`](docs/keepass-and-agents.md), retitled (D-0038). "Your password manager can't talk to AI" is false — 1Password Environments, Bitwarden's Agent Access SDK and Keeper all approve requests, and the July-2026 research behind D-0036 had already established it. Narrowing to *your KeePass vault* keeps the claim true and aims at the audience Stage 3 is going to. No Show HN text: 3.3 owns the launch posts and should not go out before 3.4 ships something to install. The essay's transcripts are gated by `scripts/verify-demo.sh`.

- [ ] **3.3 — Launch checklist runner**
"Create launch.md with a checklist and tailored copy for each channel: Show HN title+text, r/selfhosted post, r/KeePass post (respectful, KDBX-compat-focused), MCP community/Discord message, X thread, lobste.rs. Each post is written for its audience's culture, links the demo, and asks one genuine question to invite feedback. Include a 14-day follow-up plan: respond to every issue, label good-first-issues, weekly changelog."

- [ ] **3.4 — Release pipeline and the install one-liners** *(land this before 3.3 goes out — strangers arriving from a launch post need something to install)*
"Resolve O-0006 first: publish `Keypaste.Cli` and `Keypaste.Mcp` with `PublishAot=true` for each target RID and actually run the binaries, because vendored KeePassLib's AOT compatibility has never been tested and `third_party/` has the AOT analyzers disarmed. Then add a tag-triggered release workflow that publishes self-contained single-file binaries for linux-x64, linux-arm64, osx-x64, osx-arm64 and win-x64 to a GitHub Release with checksums. Declaring RuntimeIdentifiers collides with `RestorePackagesWithLockFile` — regenerate the lock files with `--force-evaluate` and keep `--locked-mode` working in CI. Finish by replacing the build-from-source block in README.md and site/public/index.html with real per-OS install one-liners, and verify each one from scratch on that OS before committing it."

## STAGE 4 — Modern GUI (the KeePass reskin)

- [ ] **4.1 — Tauri shell & unlock**
"Scaffold the keypaste desktop app with Tauri using keypaste-core via commands/FFI — zero vault logic in the frontend. Implement: vault open/unlock screen (drag a .kdbx or recent list, master password field), auto-lock after idle, and a main window shell with sidebar navigation (Entries, Env Sets, Agent Activity, Log, Settings). Follow the design direction in ideas.md → 'UI direction': calm, modern, generous spacing, system fonts, light+dark, no security-theater aesthetics. Ship with keyboard-first navigation."

- [ ] **4.2 — Entry & env UIs**
"Build the Entries view: searchable list with group tree, entry detail pane with copy buttons (auto-clearing clipboard), inline edit, add/delete, password generator. Build the Env Sets view: projects as cards, variables as a masked table with reveal-on-hold, per-project 'copy as run command' helper showing `keypaste run <project> -- `. Everything reads/writes through core so CLI and GUI stay perfectly consistent; add a test that a GUI edit is visible to the CLI immediately."

- [ ] **4.3 — Agent Activity screen (the differentiator)**
"Build the Agent Activity screen — the seed of the delegation dashboard: a live feed of incoming agent requests with Approve/Deny buttons replacing the OS dialog when the app is open; a history list from the audit log; per-client summary cards (client name, total requests, last seen, standing policy rules affecting it) with a 'revoke/pause this client' toggle that flips a deny-all policy rule. This screen must make a screenshot-worthy answer to 'what can act as me right now?' — design it like the product's hero feature, because it is."

## STAGE 5 — Sharing & first revenue

- [ ] **5.1 — One-time encrypted share**
"Implement `keypaste share <entry|env-set>`: encrypt the payload client-side with a random key, upload ciphertext to a relay (build a tiny self-hostable relay server in the repo: store blob, one download, then delete, TTL max 24h), and output a link where the decryption key lives only in the URL fragment so the relay can never read it. Add `keypaste share --burn` verification test proving second fetch fails. Document self-hosting the relay in docs/relay.md and be explicit in THREATS.md about what the relay operator can and cannot see."

- [ ] **5.2 — Hosted tier groundwork**
"Design (docs first, then minimal code) the first paid tier per CORE.md law 5.4 — convenience, never security: hosted relay for shares + optional encrypted vault sync where the server only ever stores the encrypted .kdbx blob (zero knowledge, client-side keys). Write pricing.md comparing free/self-host (everything) vs Pro (hosted relay, sync, priority support, $X/mo) vs Team (shared vaults workflow, $Y/user). Then implement only the hosted relay billing gate (license key check on the relay, nothing in the client is ever gated)."

## STAGE 6 — Delegation dashboard v2 (gated on traction)

- [ ] **6.1 — Feasibility spike**
"Run a strictly timeboxed spike (2 days of work max) answering: for a personal GitHub account and a personal Google account, what OAuth grants/authorized apps can we enumerate and revoke via API with user-level scopes only? Produce feasibility.md with exact endpoints, scopes, and hard limits, and a recommendation: live aggregation, guided deep-link revocation flows, or hybrid. Do not write product code in this spike."

- [ ] **6.2 — Delegation center**
"Based on feasibility.md, extend Agent Activity into the Delegation Center: unify keypaste agent grants, connected MCP clients, and (as feasible) external OAuth grants into one 'everything that can act as you' view with revoke/deep-link actions and staleness nudges ('unused for 60 days — revoke?'). Update positioning across README/landing to 'the control panel for everything that can act as you' only if Stage 3 and 5 benchmarks in PLAN.md were actually met — otherwise stop and re-read PLAN.md's pivot conditions."

## MAINTENANCE PROMPTS (recurring, any stage)

**M.1 — Security review**
"Act as a hostile security reviewer of the current codebase. Attempt to find: any path where a secret touches disk unencrypted, any place the master key or derived keys outlive their need in memory, any agent-facing response that could leak more than the single requested field, any injection via entry names/reasons, any failure path that fails open. Report findings ranked by severity with concrete patches, and add regression tests for every fix. Do not soften findings."

**M.2 — Compatibility audit**
"Verify KDBX compatibility end-to-end against the latest KeePassXC release: round-trip every feature we use (groups, entries, custom fields, our env convention) in both directions, including a vault created in KeePassXC and modified by keypaste and vice versa. Fix any drift. Update the CI compatibility matrix and note the tested KeePassXC version in the README."

**M.3 — Scope check**
"Read CORE.md sections 2 and 6, then review my last two weeks of commits and open branches. Flag anything that violates the scope walls (new formats, cloud-held secrets, consumer features, enterprise IAM creep) or that isn't attached to a PLAN.md checkbox. Recommend what to cut, park in ideas.md, or finish. Be blunt."

**M.4 — Docs & release**
"Prepare release vX.Y: update CHANGELOG.md from commits in human language, bump versions, verify install instructions on all three OSes actually work from scratch, refresh the demo GIF if any user-visible flow changed, tag, and draft the short release announcement for GitHub Releases and X."
