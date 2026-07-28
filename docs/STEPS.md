# STEPS.md — every step to the finished product

> This file evolves. `docs/PRODUCT.md` does not. Every step carries the **Build** prompt that produced it or will; open steps also carry **Owner** and **Verify**.

**The admission rule.** A step may be added only if it (a) has an accept criterion that can *fail*, (b) names its verifier in `docs/verification.md`, and (c) traces to a claim in `docs/PRODUCT.md`. Fails any one of those and it is a `docs/IDEAS.md` row, not a step. This is the termination condition: without it the plan grows forever.

**Every Build prompt runs with `docs/PRODUCT.md` in context.** It is law, and a prompt that has not read it will violate it.

---

## Scope

- **Built.** A KDBX4 vault the CLI creates, reads and writes, which KeePassXC opens in both directions. Env sets and `keypaste run` injection. The MCP bridge: scoped request, human approval, TTL, policy pre-approvals, and a hash-chained audit log. `v0.1.0` published as four native binaries. A desktop app that unlocks a vault, browses entries and edits env sets.
- **Building.** Stage 3's launch, and Stage 4's Agent Activity screen — the one screen that answers "what can act as me right now?"
- **Later, and gated.** Stages 5 to 7: sharing, a hosted tier, the delegation dashboard, teams. Their prompts are below with the condition each is gated on. None is a step until it can name a verifier.
- **Out, deliberately.** `docs/PRODUCT.md` §2 — a new vault format, a cloud service holding secrets, "for everyone", enterprise IAM. That list is locked and is the ratchet.

**Settled, and not re-opened here.** The stack is C#/.NET on `net10.0` (D-0002) with xUnit v3 on Microsoft.Testing.Platform (D-0003). The KDBX library is vendored KeePassLib 2.61, chosen on maturity rather than licence (D-0007). The licence is AGPL-3.0 — see `LICENSE` — and every release publishes its corresponding source (D-0041). The desktop shell is Avalonia, after Photino and Tauri were both named in this file and neither survived being checked (D-0044).

---

## Owner Queue

What only a human can do: decide, register, sign, pay, post, or press a key. Nothing below is agent-runnable, and several of them block steps that are otherwise finished.

| id | What only you can do | Blocks | Where it came from |
|---|---|---|---|
| **H-0001** | Register the `keypaste` GitHub org, and the npm and crates names | 0.4 | open since week 1 |
| **H-0002** | Trademark check on the name "keypaste" | H-0001 | the LOCKED decisions block |
| **H-0003** | Decide whether this repository goes public, knowing the decision is irreversible | **3.2** | O-0014 |
| **H-0004** | Choose DCO or CLA and write `CONTRIBUTING.md` | first outside PR | O-0002 |
| **H-0005** | Record the demo GIF — WSL only, a real Claude session, a human keystroke, three to eight takes budgeted | 3.1 | `scripts/demo/README.md`, D-0033 |
| **H-0006** | Post the launch to the five channels | — | `launch.md` holds the copy and the preconditions |
| **H-0007** | Answer every issue and comment for two weeks after the launch | — | 3.3 |
| **H-0008** | Decide whether the binaries get signed and notarized, and pay for it if so | trust on first run | O-0010, O-0015, THREATS T-21 |
| **H-0009** | Settle Windows clipboard history and the `argv` exposure before the audience stops being one person | **3.2** | O-0008, O-0009 |
| **H-0010** | Run the twenty-one item manual checklist in `docs/desktop.md` — nothing automated has ever seen this app draw | any desktop claim | O-0020 |
| **H-0011** | Run the pre-deploy checklist in `site/README.md` before any keypaste.com deploy | every deploy | `site/README.md`; D-0037 declined to build a CI job for it |
| **H-0012** | Answer who owns the approver pipe once the app can approve | **4.3** | O-0017 |

`[process]` — this queue is a ledger, not a gate. A ticked row is a person's word.

---

## Stage 0 — Foundation

### 0.1 — Repo scaffold [x]

- **Build** — "You are building keypaste, a local-first KDBX-compatible secrets tool. Read docs/PRODUCT.md and treat it as law. Create a monorepo with three packages: keypaste-core (all vault logic), keypaste-cli (thin CLI over core), keypaste-mcp (placeholder for now). Set up tooling, formatter, linter, test runner, a CI workflow that runs tests on push for macOS/Linux/Windows, an AGPL LICENSE for core, a SECURITY.md with a private disclosure contact, and a README stub with the one-sentence pitch from docs/PRODUCT.md section 1. No vault logic yet — just a clean skeleton where core exposes a hello function that cli calls, with one passing test proving the wiring."
- **Outcome** — shipped. D-0001.

### 0.2 — KDBX round-trip [x]

- **Build** — "In keypaste-core, integrate the most mature KDBX4 library for our stack (evaluate options briefly, pick the audited/most-maintained one, justify in a DECISIONS.md entry). Implement: create a new KDBX4 vault with a master password (Argon2 defaults per spec), add an entry (title, username, password, url, notes), save to disk, reopen, and read back. Write a round-trip test proving byte-level reopenability and field integrity. NEVER implement any cryptography yourself — library only (docs/PRODUCT.md law 3.6). Then add a CI step that installs keepassxc-cli and verifies our generated vault opens and lists entries correctly in KeePassXC — this compatibility test is permanent and must never be removed."
- **Outcome** — vendored KeePassLib 2.61; the KeePassXC gate runs on all three OSes and is permanent. D-0007, D-0008.

### 0.3 — Core CLI verbs [x]

- **Build** — "Build the keypaste CLI with these commands, all calling keypaste-core only: `keypaste init <vault.kdbx>` (prompts for master password twice, creates vault), `keypaste add <entry>` (interactive prompts for fields, password input hidden), `keypaste get <entry>` (prints password to stdout ONLY with --show, otherwise copies to clipboard with auto-clear after 20s), `keypaste ls` (tree view of groups/entries, names only), `keypaste rm <entry>`. Master password prompt must never echo. Add --vault flag and KEYPASTE_VAULT env var for vault path. Write integration tests for every command. Keep output clean and script-friendly; errors go to stderr with nonzero exit codes."
- **Outcome** — shipped. D-0009 to D-0012.

### 0.4 — The names [ ]

- **Build** — none. Nothing here is agent-runnable.
- **Owner** — **H-0001** and **H-0002**.
- **Verify** — `V-0006`
- Traces to `docs/PRODUCT.md` law 5.2, and to §1 — the product has to be findable under the name it claims.

## Stage 1 — Env variables and injection

### 1.1 — Env storage convention [x]

- **Build** — "Design and implement keypaste's env-set convention inside standard KDBX: a KDBX group named `env/<project>` where each entry is one variable (title=KEY, password=value), OR one entry per project using custom string fields — evaluate both for KeePassXC readability and pick one, documenting why in DECISIONS.md. Implement `keypaste env ls [project]` and `keypaste env set <project> KEY=value` and `keypaste env rm <project> KEY`. Everything must remain fully viewable and editable in vanilla KeePassXC — add a compatibility test proving a KeePassXC-edited value is picked up by keypaste."
- **Outcome** — one entry per variable, gated in CI in both directions. D-0014.

### 1.2 — Import and inject [x]

- **Build** — "Implement `keypaste env pull <project> [path/to/.env]` which parses a dotenv file (handle quotes, comments, multiline values, export prefixes) and stores every variable into the project's env set, printing a summary and offering to shred the original file. Then implement the flagship command `keypaste run <project> -- <command...>`: unlock vault, load the project's variables, spawn the child process with them merged into its environment, and stream stdio transparently, forwarding exit codes and signals. Secrets must exist only in process memory — never written to any temp file (docs/PRODUCT.md law 3.4). Test with a script that echoes an injected variable, and test signal forwarding with a long-running child."
- **Outcome** — shipped with a fail-closed parser and no "shred" claim; signals are relayed rather than escalated. D-0015, D-0016.

### 1.3 — Escape hatches and docs [x]

- **Build** — "Implement `keypaste env export <project> --dotenv` that writes a .env file only after an explicit interactive confirmation and prints a red warning that plaintext secrets are now on disk. Add `--stdout` variant for piping. Then write docs/replace-dotenv.md: a 5-minute guide taking a developer from an existing .env to `keypaste run`, including CI notes (KEYPASTE_VAULT + a dedicated CI vault, never the personal one) and a FAQ covering 'what if I lose my master password' (honest answer: it's gone — that's the point) and 'how do I sync' (your file, your sync tool)."
- **Outcome** — spelled `env export`, single-quoted so other .env readers agree. D-0018.

## Stage 2 — The MCP bridge

### 2.1 — MCP server skeleton [x]

- **Build** — "Build keypaste-mcp: an MCP server (official SDK, stdio transport) exposing exactly two tools. Tool `list_entry_names` returns entry titles and group paths ONLY — never usernames, never secrets, and it must sanitize titles defensively when returning them since entry names could contain prompt-injection text; document this risk in a THREATS.md you create now. Tool `request_credential` takes {entry, field, reason, ttl_seconds} and for now always returns DENIED — the approval flow comes next. Wire it so Claude Desktop and Claude Code can connect (write the config snippet in docs/mcp-setup.md). Every call, allowed or denied, appends a JSONL line to the audit log: timestamp, client info, tool, args, decision."
- **Outcome** — shipped denying everything, because a server with no terminal cannot ask for a master password. D-0019, D-0022.

### 2.2 — Human approval flow [x]

- **Build** — "Implement the approval gate for request_credential per docs/PRODUCT.md law 3.2: when an agent requests a credential, show the human a confirmation displaying agent/client name, entry requested, the agent's stated reason, and TTL — via a native OS dialog if available, else a terminal prompt on the keypaste daemon side. Default deny; 60-second timeout is deny; any error path is deny (fail closed, law 3.7). On approval, return ONLY the requested field value, and hold the grant in memory so repeat requests for the same entry within the TTL don't re-prompt. Write tests simulating approve, deny, timeout, and error paths, asserting the secret appears in exactly one path."
- **Outcome** — `keypaste agent` as a separate process; the timeout is 45 seconds, not 60, so it lands under every MCP client's own 60-second limit. D-0023, D-0025, D-0026.

### 2.3 — Policy pre-approvals [x]

- **Build** — "Add a policy file (~/.keypaste/policy.toml) letting the user pre-authorize narrow patterns, e.g. allow client 'claude-code' to read field 'password' of entries matching group 'env/dev*' with ttl<=3600 and no prompt. Parse and validate strictly — any malformed policy means the policy is ignored entirely and everything prompts (fail closed). `keypaste policy ls` shows active rules in plain English. Log policy-based grants distinctly in the audit log. Add tests: matching rule grants silently, non-matching prompts, malformed file prompts everything, and a rule can never widen to secrets outside its pattern."
- **Outcome** — keyed on the operator's `--client-label`, evaluated after the exposure re-check so a rule can only narrow. D-0028 to D-0030.

### 2.4 — Audit log and threat model [x]

- **Build** — "Implement `keypaste log` rendering the JSONL audit trail as a readable table (time, client, entry, decision, method: prompt/policy) with filters --denied, --client, --since. Make the log append-only from the app's perspective and add a per-line hash chain so tampering is detectable; `keypaste log verify` checks the chain. Then complete THREATS.md covering at minimum: confused-deputy via a malicious MCP client, prompt injection through entry names/reasons, audit log tampering, clipboard scraping, memory dumping, and a stolen vault file — with our mitigation or honest 'out of scope' for each."
- **Outcome** — the chain names what it cannot detect on every pass, not only on failure. D-0031, D-0032.

### 2.5 — The 60-second demo [x]

- **Build** — "Prepare the flagship demo end to end: a script (docs/demo.md) where the user asks Claude Code to run a deploy that needs an API key, Claude calls keypaste's request_credential, the approval dialog appears with the reason, the user approves, the deploy proceeds, and `keypaste log` shows the whole exchange. Fix every rough edge that breaks the flow: startup time, unclear dialog wording, error messages. Record the happy path as a terminal cast/GIF. This demo is the marketing (docs/PRODUCT.md law 5.1) — polish it like a product surface."
- **Outcome** — `scripts/verify-demo.sh` holds every transcript to what the shipped binaries print, and says plainly it does not run Claude and cannot. D-0034, D-0035.

## Stage 3 — Launch

### 3.4 — Release pipeline and the install one-liners [x]

- **Build** — "Resolve O-0006 first: publish `Keypaste.Cli` and `Keypaste.Mcp` with `PublishAot=true` for each target RID and actually run the binaries, because vendored KeePassLib's AOT compatibility has never been tested and `third_party/` has the AOT analyzers disarmed. Then add a tag-triggered release workflow that publishes self-contained single-file binaries for each supported RID with checksums. Declaring RuntimeIdentifiers collides with `RestorePackagesWithLockFile` — regenerate the lock files with `--force-evaluate` and keep `--locked-mode` working in CI. Finish by replacing the build-from-source block in README.md and site/public/index.html with real per-OS install one-liners, and verify each one from scratch on that OS before committing it."
- **Outcome** — `v0.1.0` on `dl.keypaste.com`; distribution is R2 because the repository is private and private release assets 404. `osx-x64` was dropped with reasons. D-0040, D-0041, D-0043.

### 3.2b — Launch essay [x]

- **Build** — "Write the launch essay: open with the real behavior (people paste API keys into LLM chats and .env files into agents), explain why vaults stayed offline while agents arrived, introduce the scoped-request + approval + audit model, show the demo, and end with the local-first/KDBX/open-source stance. Honest, technical, no marketing fluff, ~1200 words, ready for a blog post and adaptable to a Show HN text."
- **Outcome** — `docs/keepass-and-agents.md`, retitled because the original title was false: 1Password, Bitwarden and Keeper all ship request-and-approve flows. D-0038.

### 3.1 — README, landing page and the demo GIF [ ]

The README rewrite and the landing page are done. The GIF is the only thing left, and both pages already reserve the slot.

- **Build** — "Trim the recorded cast to under 2 MB, render it to `docs/demo/keypaste-demo.gif`, and drop it into the slot `README.md` and `site/public/index.html` already reserve. Nothing else on either page moves."
- **Owner** — **H-0005**. The take itself: WSL only, a real Claude session, a human keystroke.
- **Verify** — `V-0001`
- Traces to `docs/PRODUCT.md` law 5.1, the demo is the marketing.

### 3.2 — The launch posts [ ]

- **Build** — none. The copy is written and lives in `launch.md`.
- **Owner** — **H-0006**, blocked by **H-0003** and **H-0009**. `launch.md`'s "Before anything goes out" list is the precondition set, and every item on it is false today.
- **Verify** — `V-0002`
- Traces to `docs/PRODUCT.md` law 5.3, community before customers.

### 3.3 — Two weeks of answering [ ]

- **Build** — none.
- **Owner** — **H-0007**.
- **Verify** — `V-0003` `[process]`
- Traces to `docs/PRODUCT.md` law 5.3.

## Stage 4 — The desktop app

### 4.1 — Desktop shell and unlock [x]

- **Build** — "Scaffold the keypaste desktop app over keypaste-core with a plain project reference and zero vault logic outside the core. Implement: vault open/unlock screen (drag a .kdbx or recent list, master password field), auto-lock after idle, and a main window shell with sidebar navigation (Entries, Env Sets, Agent Activity, Log, Settings). Follow the UI direction in docs/IDEAS.md: calm, modern, generous spacing, system fonts, light+dark, no security-theater aesthetics. Ship with keyboard-first navigation."
- **Outcome** — Avalonia. The master password never enters a `TextBox`, idle lock runs on two clocks and survives sleep, and the Log view renders `AuditText` verbatim so it cannot drift from `keypaste log`. D-0044.

### 4.2 — Entry and env UIs [x]

- **Build** — "Build the Entries view: searchable list with group tree, entry detail pane with copy buttons (auto-clearing clipboard), inline edit, add/delete, password generator. Build the Env Sets view: projects as cards, variables as a masked table with reveal-on-hold, per-project 'copy as run command' helper showing `keypaste run <project> -- `. Everything reads/writes through core so CLI and GUI stay perfectly consistent; add a test that a GUI edit is visible to the CLI immediately."
- **Outcome** — shipped, and it cost three things the core had to grow first: a password generator, a shared clipboard-clear rule, and a guard against lost writes. D-0045 to D-0050.

### 4.3 — Agent Activity [ ]

The seed of the delegation dashboard, and the screen the product is named for.

- **Build** — "Build the Agent Activity screen — the seed of the delegation dashboard: a live feed of incoming agent requests with Approve/Deny buttons replacing the OS dialog when the app is open; a history list from the audit log; per-client summary cards (client name, total requests, last seen, standing policy rules affecting it) with a 'revoke/pause this client' toggle that flips a deny-all policy rule. This screen must make a screenshot-worthy answer to 'what can act as me right now?' — design it like the product's hero feature, because it is."
- **Owner** — **H-0012** first. The app and `keypaste agent` cannot both own the approver pipe, and nothing decides which does.
- **Verify** — `V-0004`
- Traces to `docs/PRODUCT.md` §1, wedge item 4.

### 4.4 — Approval prompts leave the terminal [ ]

- **Build** — "Move the approval prompt from the terminal to a native window or tray notification, keeping `keypaste agent`'s terminal channel working for headless use. Default deny, timeout deny and every error path deny must hold identically on both channels."
- **Owner** — none beyond **H-0012**.
- **Verify** — `V-0005`
- Traces to `docs/PRODUCT.md` law 3.2 and law 3.7.

---

## Gated — the prompts are written, the steps are not open

None of these is a step. Each is gated on something that has not happened, and none can name an accept criterion that would fail today, so under the admission rule they are `docs/IDEAS.md` rows carrying their prompt here. They become steps by earning a verifier, not by being wanted.

### 5.1 — One-time encrypted share (gated on 3.2 shipping)

"Implement `keypaste share <entry|env-set>`: encrypt the payload client-side with a random key, upload ciphertext to a relay (build a tiny self-hostable relay server in the repo: store blob, one download, then delete, TTL max 24h), and output a link where the decryption key lives only in the URL fragment so the relay can never read it. Add `keypaste share --burn` verification test proving second fetch fails. Document self-hosting the relay in docs/relay.md and be explicit in THREATS.md about what the relay operator can and cannot see."

### 5.2 — Hosted tier groundwork (gated on 5.1, and on the local-first fork being answered)

"Design (docs first, then minimal code) the first paid tier per docs/PRODUCT.md law 5.4 — convenience, never security: hosted relay for shares + optional encrypted vault sync where the server only ever stores the encrypted .kdbx blob (zero knowledge, client-side keys). Compare free/self-host (everything) against paid tiers, keeping every number in the private business notes rather than this repository. Then implement only the hosted relay billing gate (license key check on the relay, nothing in the client is ever gated)."

### 6.1 — Feasibility spike (gated on Stage 3 and Stage 5 benchmarks actually being hit)

"Run a strictly timeboxed spike (2 days of work max) answering: for a personal GitHub account and a personal Google account, what OAuth grants/authorized apps can we enumerate and revoke via API with user-level scopes only? Produce feasibility.md with exact endpoints, scopes, and hard limits, and a recommendation: live aggregation, guided deep-link revocation flows, or hybrid. Do not write product code in this spike."

### 6.2 — Delegation center (gated on 6.1)

"Based on feasibility.md, extend Agent Activity into the Delegation Center: unify keypaste agent grants, connected MCP clients, and (as feasible) external OAuth grants into one 'everything that can act as you' view with revoke/deep-link actions and staleness nudges ('unused for 60 days — revoke?'). Update positioning across README/landing to 'the control panel for everything that can act as you' only if the Stage 3 and Stage 5 benchmarks were actually met — otherwise stop."

### 7.x — Teams (gated on Stage 5 revenue; the whole stage lives behind the scope walls)

Do not start any of these until Stage 3 has shipped and Stage 5 has paying users. Zero-knowledge (the server stores only ciphertext, law 3.1), KDBX-or-nothing (a shared vault is still a real .kdbx that opens in KeePassXC), self-host stays first-class (§2), and this is **not** enterprise IAM — if it starts looking like Okta, stop and re-read §2. 7.1 and 7.2 are different products for different sensitivities and 7.1 must never silently become 7.2.

**7.1 — Shared env sets, the COPY model.** "Extend the 5.2 zero-knowledge sync into shared env sets for a small team. A shared set is a normal KDBX file whose master key is wrapped per-member: each member has a keypair, and the set's key is sealed to every member's public key, so the server only ever stores the ciphertext blob plus opaque wrapped-key envelopes and can decrypt neither (law 3.1). Implement invite, list members, and remove-member — where remove MUST rotate the set's key, re-wrap to the remaining members, AND loudly instruct rotation of the actual secret values, because a removed member may have cached plaintext. Be honest in THREATS.md: this bounds FUTURE access, not past copies. Design first (DECISIONS.md: why per-member key-wrapping OUTSIDE the KDBX rather than inventing a format), then code. Tests: a non-member's envelope never decrypts; a removed member's old envelope fails after rotation; the shared file still round-trips through real KeePassXC (law 4.6)."

**7.2 — Team broker, the ACCESS model, the differentiated one.** "Build the team broker: the way to share a HIGH-sensitivity credential without giving anyone a copy. Instead of syncing the secret, a member — or that member's agent — requests it from a shared approver (a self-hostable team relay, or a designated owner's running `keypaste agent`), and the EXISTING scoped-request + approval + TTL + audit machinery from Stage 2 decides and releases exactly one field for one use. This is `request_credential` with the requester on another machine: the secret lives in one place, revocation is INSTANT at the broker with no rotation, and every release is attributed to a named teammate in the audit log. Reuse the Stage 2 approval core verbatim — no second security path (law 4.3). The relay is zero-knowledge about the vault: it brokers a request to the holder, it never holds the vault. Tests: an unauthorized member's request is denied and audited; instant revocation stops the next request with NO key rotation; the secret still lands in exactly one response path."

**7.3 — Team identity and SSO, service-account auth only, never on the vault path.** "Add team accounts with SSO (OIDC) for the HOSTED SERVICE — and draw the invariant in blood: SSO authenticates who may PULL a wrapped-key envelope, reach the broker, or view the dashboard. It NEVER gates vault decryption and is NEVER on the secret path. Your IdP being compromised must not equal any vault being readable (law 3.1). Implement OIDC login, map members to their key-wrapping identity from 7.1/7.2, and SCIM (or a manual deprovision) that, on removing a person, AUTOMATICALLY triggers 7.1 re-wrap/rotation and 7.2 broker revocation — deprovisioning that leaves access behind is theatre. Tests: SSO cannot decrypt anything; a deprovisioned user loses both pull and broker access and rotation is triggered; a broken IdP fails closed (law 3.7)."

**7.4 — Team delegation dashboard.** "Extend the Stage 6 Delegation Center from 'everything that can act as ME' to 'everything that can act as anyone on the TEAM': unify per-member agent grants, standing policy rules, shared-set membership (7.1) and live broker grants (7.2) into one view an owner reads at a glance and revokes from in one click. Gated exactly like Stage 6: build it only if Stage 5 revenue and the benchmarks were actually met; otherwise it goes back to docs/IDEAS.md. Screenshot-worthy or it isn't done (law 5.1)."

---

## Recurring

Not steps — they have no completion. Run them when the trigger in each one fires.

**M.1 — Security review.** "Act as a hostile security reviewer of the current codebase. Attempt to find: any path where a secret touches disk unencrypted, any place the master key or derived keys outlive their need in memory, any agent-facing response that could leak more than the single requested field, any injection via entry names/reasons, any failure path that fails open. Report findings ranked by severity with concrete patches, and add regression tests for every fix. Do not soften findings."

**M.2 — Compatibility audit.** "Verify KDBX compatibility end-to-end against the latest KeePassXC release: round-trip every feature we use (groups, entries, custom fields, our env convention) in both directions, including a vault created in KeePassXC and modified by keypaste and vice versa. Fix any drift. Update the CI compatibility matrix and note the tested KeePassXC version in the README."

**M.3 — Scope check.** "Read docs/PRODUCT.md sections 2 and 6, then review my last two weeks of commits and open branches. Flag anything that violates the scope walls (new formats, cloud-held secrets, consumer features, enterprise IAM creep) or that isn't attached to a step in docs/STEPS.md. Recommend what to cut, park in docs/IDEAS.md, or finish. Be blunt."

**M.4 — Docs and release.** "Prepare release vX.Y: update CHANGELOG.md from commits in human language, bump versions, verify install instructions on all three OSes actually work from scratch, refresh the demo GIF if any user-visible flow changed, tag, and draft the short release announcement."

---

## Definition of focused

You should always be able to answer: which step am I on, and what would prove it done? If you cannot, open this file, take the first open step, and read its Verify lane first.
