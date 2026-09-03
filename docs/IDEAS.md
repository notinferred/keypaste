# IDEAS.md — the ledger

> Ideas wait here. Most wait forever, and that is the point (`docs/PRODUCT.md` law 5.5). Nothing here is a step. It becomes one only when it can name an accept criterion that can *fail* and carry a falsifier — the admission rule at the top of `docs/STEPS.md`.
>
> **Append-only. Never delete a row; flip its status.** A deleted row destroys the only evidence the idea was ever considered, and a ledger that only contains live ideas cannot tell "never proposed" from "quietly dropped".
>
> Business, pricing and positioning notes are kept privately, outside this repository — see `docs/ARTIFACTS.md`.

Status vocabulary: **open** (no decision) · **parked** (decided: not now, with a reason) · **rejected** (decided: no) · **promoted** (now a step in `docs/STEPS.md`) · **shipped**.

---

## UI direction

Adopted in 4.1 and 4.2, and standing: calm, precise, bank-lobby-not-hacker-movie. Whitespace, a muted palette with one accent, Inter, subtle depth, dark mode first-class. Avoided, deliberately: green-on-black terminal cosplay, padlock icons everywhere, red warnings for normal actions, and dense 2003-era tree-tables. The signature moment is the approval dialog — who wants what, and why — and it is the brand.

---

## The ledger

| idea | who | status | why |
|---|---|---|---|
| One-time encrypted share links, self-hostable relay, key in the URL fragment | founder | parked | Split in two, both in `docs/STEPS.md` under Gated. 5.0 is the bundle — a real KDBX holding one subtree, source UUIDs preserved, opened by a 32-byte keyfile and no password — and needs no relay, no account and no network. 5.1 is the relay alone, and since the keyfile is what lives in the URL fragment it is purely additive: it moves bytes it can never read. 5.0 is gated on the trace rather than on the build (O-0021, and sharing serves no §1 claim), 5.1 on 3.2 shipping. |
| `keypaste merge` — entry-level reconciliation of two KDBX files by UUID | founder | promoted | 1.4. Not a sharing feature: §2 makes sync the user's problem and then leaves the user holding two divergent copies, which is O-0018 and the reason the app can only refuse to save (D-0050). Sharing turns out to be a free application of it — the bundle is delivered *into* a merge — so the engine is justified even if 5.0 is never built. |
| Multi-vault and vault-per-project ergonomics | founder | parked | The prompt is in `docs/STEPS.md` under Gated. Wanted, but no accept criterion that can fail has been written. |
| Git-friendly vault workflows and conflict guidance | founder | parked | The prompt is in `docs/STEPS.md` under Gated. Blocked on O-0018 — two writers, one KDBX, and no merge. |
| Hosted relay and sync as the first paid tier | founder | parked | The prompt is in `docs/STEPS.md` under Gated.2. Convenience never security (law 5.4). Needs the local-first vs hosted-sync tension below resolved first. |
| Delegation dashboard: aggregate agent grants, MCP connections, external OAuth | founder | parked | Gated on benchmarks being hit; the prompt is in `docs/STEPS.md` under Gated. A feasibility spike is the first real step and it has not run. |
| Teams: shared env sets by per-member key wrapping (the copy model) | founder | parked | Gated on revenue; the prompt is in `docs/STEPS.md` under Gated. Gated on revenue. Bounds future access, not past copies — that honesty has to survive into any build. |
| Teams: a broker that releases without copying (the access model) | founder | parked | Gated on revenue; the prompt is in `docs/STEPS.md` under Gated. The differentiated one — instant revocation, no rotation. Reuses the Stage 2 approval core verbatim or it is not this idea. |
| Team SSO for the hosted service, never on the vault path | founder | parked | Gated on revenue; the prompt is in `docs/STEPS.md` under Gated. If the IdP being compromised can read a vault, it is the wrong design. |
| Team delegation dashboard | founder | parked | Gated on revenue; the prompt is in `docs/STEPS.md` under Gated. Gated exactly like Stage 6. |
| "Design language: modern, calm, trustworthy" as a checklist item | founder | rejected | Nothing about it can fail, so it was never a step. The direction above is the durable form. |
| Sign-in-first landing flow from the design exploration | founder | open | Inverts local-first, which `docs/PRODUCT.md` §2 makes permanent. Must be answered before any hosted-sync work; it is the fork the parked Stage 5 rows sit behind. |
| Local-first vs a hosted sync tier | founder | settled | §2 permits a zero-knowledge hosted tier if self-host stays first-class. The founder stated the intent — free now, freemium if server costs bite — which made this **H-0014** and O-0022 rather than a musing. **D-0060 settled it: 5.2 as written, and the server cannot read the blob.** A hosted tier that can read a vault is a different product and law 3.1 forbids it, so a forgotten master password stays gone. Still gated on 5.1; settled is not the same as started. |
| TOTP/2FA storage with agent-safe handling — a code, never the seed | founder | open | Strong candidate once the desktop app settles. |
| Command palette (Ctrl/Cmd+K) | founder | open | Fits the keyboard-first shell 4.1 shipped. |
| Onboarding that offers "import your existing .kdbx" first | founder | open | Meets users where they are; new vault second. |
| SSH key management and a `keypaste ssh` agent integration | founder | open | |
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
| Team approval quorum, "2 of 3 for production" | founder | parked | Good story, gated with the rest of teams. |
| Windows Hello / Touch ID unlock | founder | open | |
| Import wizards for 1Password, Bitwarden, LastPass | founder | open | |
| Anomaly nudges — "an agent asked for prod credentials at 3am" | founder | open | |
| Public trust page: reproducible builds, audit fund, bounty | founder | open | Ties to O-0012; reproducibility is not claimed today. |
| Break-glass emergency access with mandatory after-the-fact review | founder | parked | Gated with teams. Convenience never softens the audit. |
| Browser extension with autofill | founder | deferred | Re-opened by the founder, who wants a working password manager. The prompt is in `docs/STEPS.md` as gated 8.3 and cannot become a step: autofill traces to no claim in `docs/PRODUCT.md` §1, and §2 makes "for everyone" a permanent wall — so it needs a deliberate re-ratification, not a drift. **The original rejection has not been refuted.** Huge effort and strong incumbents were true when this row was written and are true now; wanting it more is not an argument against either. **D-0059 answered H-0013 by giving the deferral a condition that can fire:** 8.1 ships a native messaging host, and users ask for autofill unprompted anyway. Both observable. If 8.1 ships and nobody asks, this row flips to rejected on evidence rather than on preference. |
| Browser extension as an approval surface for agents that live in the browser | founder | parked | 8.1 and 8.2. A different product from the row above in the same package format, and it serves §1 wedge item 3 rather than fighting Apple and Google. Native messaging to a running `keypaste agent`, which is what keepassxc-browser replaced its localhost HTTP server with in 2018 — the precedent `docs/keepass-and-agents.md` already cites. **Parked 2026-09-04, not rejected.** It measures or extends a product nobody is using yet, and a threshold no human has been held to is an assertion rather than a measurement (D-0043). It returns when there is somebody to measure. The full prompt is in git at `db9c93c`. |
| A UX bench: HEART minus the two dimensions law 3.5 forbids measuring | founder | parked | 4.5. The durable form of the design-language row this table already rejected: same ambition, rebuilt so a result can fail. Engagement and Retention need behavioural telemetry and are struck on the page rather than quietly dropped. **Parked 2026-09-04, not rejected.** It measures or extends a product nobody is using yet, and a threshold no human has been held to is an assertion rather than a measurement (D-0043). It returns when there is somebody to measure. The full prompt is in git at `db9c93c`. |
| Headless render gates so a screenshot is evidence rather than a memory | founder | parked | 4.6, closing O-0020. `Avalonia.Headless` renders to a bitmap with no display, which makes the masked-value and locked-window claims testable for the first time — in a GUI the screen is a secret path, so law 4.5 applies to it. **Parked 2026-09-04, not rejected.** It measures or extends a product nobody is using yet, and a threshold no human has been held to is an assertion rather than a measurement (D-0043). It returns when there is somebody to measure. The full prompt is in git at `db9c93c`. |
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
