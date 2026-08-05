# STEPS.md — every step to the finished product

> This file evolves. `docs/PRODUCT.md` does not. Every step carries the **Build** prompt that produced it or will; open steps also carry **Owner** and **Verify**.

**The admission rule.** A step may be added only if it (a) has an accept criterion that can *fail*, (b) names its verifier in `docs/verification.md`, and (c) traces to a claim in `docs/PRODUCT.md`. Fails any one of those and it is a `docs/IDEAS.md` row, not a step. This is the termination condition: without it the plan grows forever.

**Every Build prompt runs with `docs/PRODUCT.md` in context.** It is law, and a prompt that has not read it will violate it.

---

## Scope

- **Built.** A KDBX4 vault the CLI creates, reads and writes, which KeePassXC opens in both directions. Env sets and `keypaste run` injection. The MCP bridge: scoped request, human approval, TTL, policy pre-approvals, and a hash-chained audit log. `v0.1.0` published as four native binaries. A desktop app that unlocks a vault, browses entries and edits env sets.
- **Building.** Stage 3's launch, Stage 4's Agent Activity screen — the one screen that answers "what can act as me right now?" — the entry-level merge two machines and one Dropbox folder already need, and Stage 8's browser surface.
- **Measured, not asserted.** Stage 4.5 and 4.6 are the reason the rest of this list can claim anything about a screen: a task bench with thresholds that can fail, and a renderer in CI so a pixel is evidence rather than a memory of having looked.
- **Later, and gated.** Stages 5 to 7: sharing, a hosted tier, the delegation dashboard, teams. Their prompts are below with the condition each is gated on. None is a step until it can name a verifier.
- **Out, deliberately.** `docs/PRODUCT.md` §2 — a new vault format, a cloud service holding secrets, "for everyone", enterprise IAM. That list is locked and is the ratchet.

**Settled, and not re-opened here.** The stack is C#/.NET on `net10.0` (D-0002) with xUnit v3 on Microsoft.Testing.Platform (D-0003). The KDBX library is vendored KeePassLib 2.61, chosen on maturity rather than licence (D-0007). The licence is AGPL-3.0 — see `LICENSE` — and every release publishes its corresponding source (D-0041). The desktop shell is Avalonia, after Photino and Tauri were both named in this file and neither survived being checked (D-0044).

---

## Owner Queue

What only a human can do. **Split in two, because listing them together is why none of them cleared:** an action needs doing and has a next command; a decision needs choosing and now carries its options and a default. A row phrased as "decide whether X" with no options is a prompt to agonise, not to decide, and this queue spent weeks proving it.

### Actions — these have a next command, not a question

| id | Do this | Next command | Blocks |
|---|---|---|---|
| **H-0001** | Register `keypaste`: **GitHub org and npm are free** (checked 2026-08-04, 404 on both); crates.io is settled by D-0053 and needs nothing | github.com/organizations/new, then transfer this repo | 0.4 |
| **H-0003** | **Answered 2026-08-04: public once a release actually works, and not before.** §3.8 always required open source; only the date was ever open, and the date is now "after a release you would defend", not "before the posts". History is already scanned clean — 175 commits, no vault, no key, no `.env`, nothing over 500 KB. **The identity rewrite is done (2026-08-05):** every commit on all nine branches and both tags reads `ochoadan <hello@danochoa.com>`, no third-party attribution, no message bodies, and each branch kept its tree hash and commit count, so the scan above still holds. **What blocks the flip now is `refs/pull/*`** — GitHub keeps a head ref per pull request that no push can delete, `refs/pull/11/head` is `e972225` authored by `Claude <noreply@anthropic.com>`, and its ancestry carries the whole pre-rewrite log. Private they need a token; public they are fetchable and the pull request pages render them, which is the leak this row exists to close | ask GitHub Support to purge the stale `refs/pull/*` and gc the repo, or push the clean history to a fresh repo and delete this one; then Settings → Change visibility | **3.2** |
| **H-0005** | Record the demo GIF — WSL only, a real Claude session, a human keystroke, three to eight takes budgeted | `scripts/demo/install-recording-tools.sh` (needs sudo), then `record-demo.sh` | 3.1 |
| **H-0006** | Post the launch to the five channels | `launch.md` holds the copy and the preconditions | — |
| **H-0007** | Answer every issue and comment for two weeks after the launch | — | — |
| **H-0010** | Run the twenty-one item checklist in `docs/desktop.md` | 4.6 will strike most of it; run what remains | any desktop claim |
| **H-0011** | Run the pre-deploy checklist in `site/README.md` before any keypaste.com deploy | `site/README.md`; D-0037 declined to gate it | every deploy |
| **H-0015** | Enrol in the Apple Developer Program so macOS binaries can be notarized (**D-0057**) — 99 USD a year, and only a person can accept the agreement | developer.apple.com/programs/enroll, then put the credentials in repository secrets for `release.yml` | the next `v*` tag |

### Decisions — none open

All seven were answered on 2026-08-06 and the reasoning is in `DECISIONS.md`. The name is used and not defended (**D-0058**). Contribution terms are DCO (**D-0055**). macOS binaries are notarized and Windows binaries are not (**D-0057**). The CLI opts out of Windows clipboard history, and the `argv` exposure stays documented (**D-0056**). The agent owns the approver pipe and the app is a client of it (**D-0054**). Autofill is deferred behind a condition that can fire (**D-0059**). The hosted tier is 5.2 as written, and the server cannot read the blob (**D-0060**).

Three of them produced work rather than closing it: **H-0015** below, step **1.5**, and the `CONTRIBUTING.md` that D-0055 requires.

`[process]` — a row belongs here only while it is unmade, and **a default is not consent**: it is what the absence of a decision has already chosen on your behalf, written down so it stops being invisible. Nothing mechanical ages a row out of this table, which is how seven of them accumulated. The guard is that an answer must name a trigger, a step or a next command — never a preference.

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
- **Owner** — **H-0001**. H-0002 is answered by D-0058: the name is used and not defended.
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

### 1.4 — Entry-level merge [ ]

`docs/PRODUCT.md` §2 makes sync the user's problem — their file, their Dropbox. Two machines syncing one KDBX is how that instruction ends, and keypaste has no answer for the result: the app refuses to save (D-0050) and the CLI does not notice. This is O-0018, and the merge it names is also what any future share would be delivered into.

- **Build** — "Implement `Vault.Merge` in `Keypaste.Core` and `keypaste merge <other.kdbx>` as a thin CLI over it, resolving O-0018. Match entries by **KDBX entry UUID only** — never by title or group path, because title matching is how the wrong secret is silently overwritten. For each incoming entry: absent locally, add it under its own group path; present and identical, no-op; present and differing, the newer `LastModificationTime` wins **and the superseded value is pushed onto that entry's KDBX history**, so nothing is destroyed and the loser is still readable in KeePassXC. Equal timestamps with differing content is a **conflict: name every conflicting entry, write nothing, exit nonzero** (law 3.7). **Deletions never propagate** — an entry absent from the incoming file is not a deletion, because a scoped file is a subset by construction, and reading absence as intent would let a four-entry file empty a vault. Support `--key <path>` and `--key-b64` for a keyfile-protected source, which means wiring `KcpKeyFile` into `KeePassInterop` — that file stays the only one in the repository permitted to reference KeePassLib (D-0007), and whether vendored KeePassLib 2.61's keyfile path round-trips against real `keepassxc-cli` is the first thing to establish, before anything is built on it. Print the plan and require an explicit confirmation before writing, with `--yes` for scripts and `--dry-run` to print and stop. Add `scripts/verify-merge.sh` proving: a merged vault opens in real `keepassxc-cli`; merging the same file twice is a no-op with no duplicate UUIDs; an older incoming entry does not overwrite a newer local one; and the superseded value survives in history. Record the conflict policy and the deletion decision as **D-0052**, and either close O-0018 or state precisely what remains open."
- **Owner** — none.
- **Verify** — `V-0007`
- Traces to `docs/PRODUCT.md` §2 (sync is the user's problem), law 4.3 and law 4.6.

### 1.5a — The Windows CLI copies through one Win32 clipboard session [ ]

D-0056 split H-0009 and this is the half that gets fixed. D-0046 closed it for the app, which owns a window and can hand Avalonia a data object; `clip.exe` cannot express the formats, so `keypaste get` still leaves the secret in Win+V and in cloud clipboard after the twenty seconds are up. The app getting a secret-path fix before the CLI inverts law 4.2, and this restores the ordering.

- **Build** — "Replace the `clip.exe` shell-out in `Keypaste.Cli`'s Windows clipboard path with a Win32 one that can express KeePassXC's three opt-out formats: `ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory` and `CanUploadToCloudClipboard`. Set the text and all three inside **one** `OpenClipboard`/`EmptyClipboard`/`SetClipboardData`×N/`CloseClipboard` session — the history service acts on the notification raised at `CloseClipboard`, so a second session to add the markers has already leaked. P/Invokes go in one `[SupportedOSPlatform(\"windows\")]` class; satisfy the trim and AOT analysers rather than suppressing them. macOS and Linux are untouched and O-0019 stays open. Law 4.5 keeps the tests in this step: assert every registered name with a `GetClipboardFormatName` round-trip rather than trusting the literal, and make the clear guard compare a hash so no plaintext copy lives for the timeout window."
- **Owner** — none.
- **Verify** — `V-0012`
- Traces to `docs/PRODUCT.md` law 3.4 — clipboard history persists the secret and cloud clipboard sends it off the machine, both by keypaste's doing — and to law 4.2 and law 4.5.

### 1.5b — The pages say what the formats close, and what they do not [ ]

Split from 1.5a because it fails differently. 1.5a fails by leaving a secret in Win+V; this one fails by claiming a safety the formats cannot deliver, which is the more dangerous of the two because nothing red appears.

- **Build** — "Correct `SECURITY.md`, which states the Windows clipboard-history gap as open on both front ends. It is closed on both — D-0046 for the app, 1.5a for the CLI. Say precisely what that buys: first-party Clipboard History and Cloud Clipboard are closed, and third-party clipboard managers and RDP or Citrix redirection are covered by nothing, because the formats are a request to well-behaved consumers and not an enforcement boundary. Do not write that the clipboard is safe. Then check whether `README.md`, `docs/demo.md` or `site/public/index.html` repeat the old claim, and fix every copy in the same change."
- **Owner** — none.
- **Verify** — `V-0013`
- Traces to `docs/PRODUCT.md` law 3.4, and to D-0036 — a claim on a published page may only say what a gate or a citation can hold.

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
- **Owner** — **H-0006**, blocked by **H-0003** and by step **1.5**, which is the work D-0056 left behind when it answered H-0009. `launch.md`'s "Before anything goes out" list is the precondition set, and every item on it is false today.
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
- **Owner** — none. D-0054 settled the pipe: the agent keeps it and this screen is a client of the agent, not a second binder. Building that UI-client channel is part of this step.
- **Verify** — `V-0004`
- Traces to `docs/PRODUCT.md` §1, wedge item 4.

### 4.4 — Approval prompts leave the terminal [ ]

- **Build** — "Move the approval prompt from the terminal to a native window or tray notification, keeping `keypaste agent`'s terminal channel working for headless use. Default deny, timeout deny and every error path deny must hold identically on both channels."
- **Owner** — none. D-0054 applies here too: the terminal channel stays because the agent never stops owning the pipe.
- **Verify** — `V-0005`
- Traces to `docs/PRODUCT.md` law 3.2 and law 3.7.

### 4.5 — The UX bench [ ]

`docs/IDEAS.md` already threw out "design language: modern, calm, trustworthy" for the right reason: nothing about it can fail. This is the same ambition rebuilt so that it can.

- **Build** — "Write `docs/ux.md`: keypaste's UX bench, adapted from Google's HEART and its Goals–Signals–Metrics discipline. **Take three of the five dimensions and strike the other two on the page, with the reason.** Task success, Happiness and Adoption are observable; Engagement and Retention require behavioural telemetry, which law 3.5 forbids forever — so they are struck out in the document itself rather than quietly omitted, because a framework silently missing two fifths reads as a framework that was followed. Define every task as `T-NN`, each with: the surface it runs on, the exact words given to the participant, a **numeric threshold that can fail** (time-to-complete and a success ratio out of five), and the method that produces the number. Cover at minimum first unlock, copy a password, inject an env set into a real command, approve an agent request, deny one, and find out afterwards why something was denied. Then write `scripts/verify-ux.sh` asserting that (a) every task in `docs/ux.md` carries a numeric threshold and a named method, (b) every open step in `docs/STEPS.md` that touches a user-visible surface names at least one `T-NN`, and (c) no task's threshold is written as a range or a word — 'fast' is not a threshold. Nielsen's five-participant finding is why the ratio is out of five; say so on the page so the number is not mistaken for statistics."
- **Owner** — running the sessions is `[process]`. A threshold nobody has measured against a human is an assertion, exactly as D-0043 says.
- **Verify** — `V-0008`
- Traces to `docs/PRODUCT.md` law 5.1 — the demo is the marketing, and a demo nobody can complete in sixty seconds is a claim rather than a demo.

### 4.6 — The app draws in CI [ ]

Closes O-0020. Everything Stage 4 and Stage 8 claim about a screen rests on this existing first.

- **Build** — "Resolve O-0020. Add `Avalonia.Headless` with the Skia backend to `tests/Keypaste.App.Tests` so views render to a bitmap with **no display of any kind**, and assert on the pixels. Golden images are generated and compared on **Linux only** — font stacks and subpixel rendering differ per platform, so cross-platform pixel equality is a test that fails for reasons nobody caused; on macOS and Windows run the same renders and assert structurally (element bounds, visibility, computed colours) instead. **The assertions that matter are secret-path assertions, and they come first:** the password field renders as dots and never as characters; a masked env value renders as dots until held; releasing a hold returns it to dots within one frame; and a locked window renders no entry titles at all. Then the rest: the unlock empty state, both themes, the clipboard countdown mid-drain, and every screen at a narrow and a wide window. Store goldens under `tests/Keypaste.App.Tests/golden/`, fail on any diff above a stated anti-aliasing tolerance, and write the failing render to the CI artifacts so a red build can be looked at rather than guessed at. Add the job to `app.yml`, not `ci.yml` — the app already has its own workflow and its own solution so that backend work does not pay to build it. Update `docs/desktop.md` to strike the checklist items this now covers, and state plainly which of the twenty-one still need a human."
- **Owner** — none.
- **Verify** — `V-0009`
- Traces to `docs/PRODUCT.md` law 4.5 — tests on the secret path are mandatory, and in a GUI the screen *is* a secret path: nothing else can prove a value the user did not ask to reveal was never drawn.

## Stage 8 — The browser

Numbered clear of 5 through 7 deliberately: those are gated on launch and revenue, and this is not. The browser is where agents increasingly live, and `docs/keepass-and-agents.md` already argues that the KDBX ecosystem answered "another program wants a credential" once before, with a local pipe and a Confirm Access dialog.

### 8.1 — Native messaging host [ ]

- **Build** — "Build `keypaste-browser-host`: a native-messaging host speaking Chrome's 32-bit-length-prefixed JSON framing over stdio, and an extension skeleton that loads in both Chrome (MV3 service worker) and Firefox (MV3 with an event page, since Firefox's MV3 differs and a single build must load in both or the story is two extensions). **The host holds no vault and decides nothing** — it relays to a running `keypaste agent` over the same local channel `keypaste-mcp` already uses, preserving the D-0023 split exactly: the only process that ever sees a master password is the one the human started. Add `keypaste browser install [--chrome] [--firefox] [--edge]` writing the native-messaging manifest to the correct per-OS location (registry keys on Windows, `NativeMessagingHosts/` on macOS and Linux) with the extension ID pinned, and `keypaste browser uninstall` removing exactly what it wrote and nothing else. **Fail closed and legibly:** no agent running means the extension says so and offers the command to start one — never a password prompt, never a silent retry, because a convincing prompt is what any program on the machine can already draw. Extend THREATS.md with the new surface: a store's auto-update channel can push code to users without a git tag, which is the first time that has been true of anything keypaste ships; state what is and is not signed, and what a compromised extension can and cannot reach given the host holds no vault."
- **Owner** — registering on the Chrome Web Store and on AMO, and whatever identity each demands. Extension IDs must exist before the manifest can pin them.
- **Verify** — `V-0010`
- Traces to `docs/PRODUCT.md` §1 wedge item 3, law 3.1 and law 3.7.

### 8.2 — The approval moment, one spec, three surfaces [ ]

The signature moment, rendered the same everywhere it appears.

- **Build** — "Write the approval moment once in `docs/ux.md` — the fields, their order, the wording, the defaults, the timeout — and make the terminal prompt, the desktop Agent Activity screen and the browser extension popup all render *that*, with no surface inventing a field or a default of its own. Then build the popup: who is asking, which entry, which field, for how long, and the agent's stated reason **rendered as untrusted text and labelled as the agent's words**, with newlines and terminal escapes and RTL overrides all defanged. Default deny; closing the popup is a denial, not a cancel; the 45-second timeout is a denial and the countdown is visible. Add a task to `docs/ux.md` and hold all three surfaces to the **same** `T-NN` threshold — if approving takes twice as long in one of them, that is a failure of that surface and not a property of it. Screenshot-test the popup in headless Chrome and headless Firefox the way 4.6 does the app."
- **Owner** — none. D-0054 answers it for all three: the agent owns the pipe and every surface is a client of it, so the popup is a third renderer rather than a third binder.
- **Verify** — `V-0011`
- Traces to `docs/PRODUCT.md` law 3.2, law 3.7 and law 4.3 — one core, no second security path.

---

## Gated — the prompts are written, the steps are not open

None of these is a step. Each is gated on something that has not happened, and none can name an accept criterion that would fail today, so under the admission rule they are `docs/IDEAS.md` rows carrying their prompt here. They become steps by earning a verifier, not by being wanted.

### 5.0 — The share bundle (gated on 1.4, and on this tracing to anything)

**Gated on the trace, not on the build.** The prompt below could be written tomorrow and its accept criterion can fail, but sharing serves no claim in `docs/PRODUCT.md` §1 — it is not the vault, not env injection, not the agent bridge — so §6.2 sends it here and it stays a `docs/IDEAS.md` row until that is answered rather than assumed. **O-0021 must be answered before any of it is built.**

"Implement `keypaste share <entry|env/project>` writing a bundle: a real KDBX4 file containing **only** the named subtree, with every entry's UUID copied from the source vault unchanged, protected by a freshly generated 32-byte keyfile and **no password**. UUID preservation is the whole mechanism — it is what makes a re-sent bundle an update through `keypaste merge` rather than a second copy of everything. Print the keyfile as base64 to stdout once, say plainly that it must travel by a different channel, and never write it beside the bundle. Add a test that looks in the bundle for the title of an entry outside the named subtree and **fails if it is present**; a bundle that carries a neighbouring entry is the entire failure mode of this feature. Write docs/sharing.md saying, without hedging, that a delivered bundle cannot be recalled, that rotating the secret is the only lever, and that the recipient needs neither an account nor keypaste, because `keepassxc --keyfile` opens it."

### 5.1 — One-time encrypted share relay (gated on 3.2 shipping, and on 5.0)

"Build a tiny self-hostable relay in the repo — store blob, one download, then delete, TTL max 24h — and teach `keypaste share` to upload a 5.0 bundle to it, emitting a link whose fragment carries the bundle's keyfile so the relay holds ciphertext it can never read. The relay learns nothing 5.0 did not already decide, because the key never leaves the fragment. Add a `--burn` test proving the second fetch fails. Document self-hosting in docs/relay.md and be explicit in THREATS.md about what the relay operator can and cannot see."

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

### 8.3 — Autofill (gated on D-0059's condition, and on nothing else)

**This is the one the locked core does not permit today.** `docs/PRODUCT.md` §2 makes "for everyone" a permanent wall, and autofill is the feature that defines the consumer category — it traces to no claim in §1, so under the admission rule it cannot be a step no matter how much it is wanted. `docs/IDEAS.md` rejected it once already, on effort and on incumbents, and neither of those reasons has been refuted by anything since. It sits here, fully written, so that the day §2 is deliberately re-ratified the work is ready and nobody has to re-derive it — and so that until that day, nobody can start it by accident.

**The gate is a condition that can fire, not a mood.** D-0059 replaced "defer until the wedge has users" — which has no test and so could never close — with two observable things: **8.1 has shipped a native messaging host, and users ask for autofill unprompted anyway.** If both happen, §2 gets a dated re-ratification and this becomes admissible. If 8.1 ships and nobody asks, the row closes as rejected on evidence, which is what the original rejection predicted and never got to test.

"Implement credential autofill in the Stage 8 extension, KeePassXC-parity and no further. **Match on the registrable domain via the Public Suffix List, never on a substring of the URL** — `evil.com/paypal.com` is the entire history of autofill vulnerabilities in one string, and a phishing page that fills is worse than no autofill at all. On a match, show the Confirm Access dialog first: which page, which entry, and Remember offered as an option and never assumed — that dialog is the ecosystem's existing answer and 8.2's spec already describes it. Fill the form fields directly; **never place a credential on the clipboard as a fallback**, because the clipboard is a global read for every process on the machine and D-0046 exists precisely because Windows would otherwise record it. No password capture on submit and no vault writes from the extension in this step: reading is one threat model and writing from inside a page's process is another. Iframes are refused unless same-origin with the top document. Add the phishing-domain cases as tests before the happy path, since the happy path is the one that gets manually checked and the malicious one is not. Then update the comparison table in README.md, because a keypaste with autofill is answering a different question than the one that table currently asks."

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
