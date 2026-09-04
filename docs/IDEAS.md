# IDEAS.md — the ledger

> Ideas wait here. Most wait forever, and that is the point (`docs/PRODUCT.md` law 5.5). Nothing here is a step. It becomes one only when it can name an accept criterion that can *fail* and carry a falsifier — the admission rule at the top of `docs/STEPS.md`.
>
> **Append-only. Never delete a row; flip its status.** A deleted row destroys the only evidence the idea was ever considered, and a ledger that only contains live ideas cannot tell "never proposed" from "quietly dropped".
>
> Business, pricing and positioning figures are kept out of git — in a gitignored working file and in private storage, both named by location in `docs/ARTIFACTS.md` (D-0072).

Status vocabulary: **open** (no decision) · **parked** (decided: not now, with a reason) · **rejected** (decided: no) · **promoted** (now a step in `docs/STEPS.md`) · **shipped**.

---

## UI direction

Adopted in 4.1 and 4.2, and standing: calm, precise, bank-lobby-not-hacker-movie. Whitespace, a muted palette with one accent, Inter, subtle depth, dark mode first-class. Avoided, deliberately: green-on-black terminal cosplay, padlock icons everywhere, red warnings for normal actions, and dense 2003-era tree-tables. The signature moment is the approval dialog — who wants what, and why — and it is the brand.

---

## The ledger

| idea | who | status | why |
|---|---|---|---|
| One-time encrypted share links, self-hostable relay, key in the URL fragment | founder | promoted | Folded into step 5.2 (D-0064): the relay carries a one-download bundle endpoint, the bundle is a real KDBX holding one subtree with source UUIDs preserved, and the keyfile lives in the URL fragment so the relay moves bytes it can never read. O-0021 (a bundle is untrusted input) is answered inside 5.2 before the endpoint ships. |
| `keypaste merge` — entry-level reconciliation of two KDBX files by UUID | founder | promoted | 1.4. Not a sharing feature: §2 makes sync the user's problem and then leaves the user holding two divergent copies, which is O-0018 and the reason the app can only refuse to save (D-0050). Sharing turns out to be a free application of it — the bundle is delivered *into* a merge — so the engine is justified even if 5.0 is never built. |
| Multi-vault and vault-per-project ergonomics | founder | open | No prompt exists for it anywhere. Wanted, but no accept criterion that can fail has been written; the app's recent-vault list is as far as it goes. |
| Git-friendly vault workflows and conflict guidance | founder | open | No prompt exists for it. Blocked on step 1.4 — two writers, one KDBX, and no merge until then. |
| Hosted relay and sync as the first paid tier | founder | promoted | Step 5.2, Tier 1 (D-0064, D-0065). Convenience never security (law 5.4); the local-first vs hosted tension below was settled by D-0060. |
| Delegation dashboard: aggregate agent grants, MCP connections, external OAuth | founder | promoted | Steps 6.1 and 6.2, Tier 3 (D-0074). The feasibility spike is the first step and it has not run. |
| Teams: shared env sets by per-member key wrapping (the copy model) | founder | promoted | Step 7.1, Tier 3. Bounds future access, not past copies — that honesty has to survive into the build. |
| Teams: a broker that releases without copying (the access model) | founder | promoted | Step 7.2, Tier 3. The differentiated one — instant revocation, no rotation. Reuses the Stage 2 approval core verbatim or it is not this idea. |
| Team SSO for the hosted service, never on the vault path | founder | promoted | Step 7.3, Tier 3. If the IdP being compromised can read a vault, it is the wrong design. |
| Team delegation dashboard | founder | promoted | Step 7.4, Tier 3. |
| "Design language: modern, calm, trustworthy" as a checklist item | founder | rejected | Nothing about it can fail, so it was never a step. The direction above is the durable form. |
| Sign-in-first landing flow from the design exploration | founder | open | Inverts local-first, which `docs/PRODUCT.md` §2 makes permanent. Must be answered before any hosted-sync work; D-0060 answered it for 5.2: the server cannot read the blob, and nothing signs in before a vault exists. |
| Local-first vs a hosted sync tier | founder | promoted | §2 permits a zero-knowledge hosted tier if self-host stays first-class. **D-0060 settled it: the server cannot read the blob**, so a forgotten master password stays gone; D-0061 made hosted sync the business and D-0064 gave it a shape. It is step 5.2 in Tier 1. |
| TOTP/2FA storage with agent-safe handling — a code, never the seed | founder | promoted | Step 9.2, Tier 2 (D-0068). |
| Command palette (Ctrl/Cmd+K) | founder | open | Fits the keyboard-first shell 4.1 shipped. |
| Onboarding that offers "import your existing .kdbx" first | founder | open | Meets users where they are; new vault second. |
| SSH key management and a `keypaste ssh` agent integration | founder | promoted | Step 9.3, Tier 2 (D-0068). |
| Git hooks that block committing plaintext secrets | founder | open | Could stand alone; free marketing. |
| Secret leases for long-running agents, with rotation reminders | founder | open | |
| Passkey storage once KDBX support matures | founder | open | Watch KeePassXC's work; do not own recovery. |
| `keypaste env set --no-history` for rotating a leaked value | founder | open | Diverges from KeePassXC's editor, so it must be opt-in and loud (D-0014). |
| `execve` the child on Unix instead of wrapping it | founder | parked | Exit status, job control and signals would be right for free, but Windows has no equivalent, so the wrapper exists anyway — two implementations of one feature (D-0016). |
| `keypaste run --no-inherit`, the shape `env -i` has | founder | open | Useful for reproducing what CI sees. |
| `keypaste run --skip-unusable` | founder | parked | Only if somebody hits it. A partially injected environment fails somewhere else, later, worse. |
| CI gate reading an exported `.env` with real `dotenv`, `python-dotenv` and `godotenv` | founder | parked | The only way to keep D-0018's portability claim honest, but it puts npm and pip on the three-OS job. Checked by hand against dotenv 17.4.2 for now. |
| `keypaste env export --format json` | founder | parked | Only once something asks. `--dotenv` is required today precisely so a second format can be added without changing what the first means. |
| `keypaste env diff <project> [file]` | founder | open | The natural companion to `pull` and `export`, and it never has to print a value. |
| A `direnv` shim | founder | parked | `keypaste run` scopes exposure to one command; anything direnv-shaped puts values in the interactive shell and everything it launches. Resolve that first. |
| Vault health report: reuse, staleness, weak values | founder | open | |
| Team approval quorum, "2 of 3 for production" | founder | parked | Good story; waits for Tier 3 with the rest of teams. |
| Windows Hello / Touch ID unlock | founder | open | |
| Import wizards for 1Password, Bitwarden, LastPass | founder | promoted | Step 9.1, Tier 2 (D-0068); KeePassXC CSV added to the list, because a switcher from the tool this one is cleaner than is the likeliest arrival. |
| Anomaly nudges — "an agent asked for prod credentials at 3am" | founder | open | |
| Public trust page: reproducible builds, audit fund, bounty | founder | open | Ties to O-0012; reproducibility is not claimed today. |
| Break-glass emergency access with mandatory after-the-fact review | founder | parked | Waits for Tier 3 with the rest of teams. Convenience never softens the audit. |
| Browser extension with autofill | founder | promoted | Step 8.3, Tier 2. Rejected once on effort and incumbents, deferred behind a condition by D-0059, and then overtaken: `docs/PRODUCT.md` v1.1 (D-0061) makes keypaste a password manager, and one without autofill is not one (D-0068). The effort and the incumbents are still real and are why it sits behind the first dollar rather than in Tier 1. |
| Browser extension as an approval surface for agents that live in the browser | founder | promoted | Step 8.1, Tier 2, and the native messaging host 8.3 fills through. Native messaging to a running `keypaste agent`, which is what keepassxc-browser replaced its localhost HTTP server with in 2018 — the precedent `docs/keepass-and-agents.md` already cites. |
| A UX bench: HEART minus the two dimensions law 3.5 forbids measuring | founder | promoted | Step 4.5, Tier 2 (D-0073). The durable form of the design-language row this table already rejected: same ambition, rebuilt so a result can fail. Engagement and Retention need behavioural telemetry and are struck on the page rather than quietly dropped. It waits for the app's first release because a threshold no human has been held to is an assertion rather than a measurement (D-0043). |
| Headless render gates so a screenshot is evidence rather than a memory | founder | promoted | Step 4.6, Tier 2 (D-0073), closing O-0020. `Avalonia.Headless` renders to a bitmap with no display, which makes the masked-value and locked-window claims testable for the first time — in a GUI the screen is a secret path, so law 4.5 applies to it. |
| Mobile app | founder | parked | Read-only companion first, if ever. |
| Series: "Secrets hygiene for the agent era", one post per THREATS.md entry | founder | open | Marketing. |
| Comparison pages vs .env, vs Infisical, vs plain KeePass | founder | open | Honest framing: they are for teams and cloud; keypaste is local-first. |
| A public demo vault anyone can point Claude at | founder | open | Lets people feel the approval flow safely. |
| Conference talk: "I let Claude into my password manager — safely" | founder | open | |
| Contribute KDBX compat fixes upstream to KeePassXC | founder | open | Respectfully. Never fork-and-fight. |
| Daemon architecture for approvals vs per-invocation unlock | founder | open | UX against attack surface. |
| Hardware key (YubiKey) unlock | founder | open | Timing unclear. |
| `policy.toml` inside the vault, synced and encrypted | founder | open | |
| Product vocabulary: "agents" vs "clients" vs "apps" | founder | open | Needs user testing. |
