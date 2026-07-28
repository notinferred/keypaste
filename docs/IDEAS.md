# IDEAS.md — the ledger

> Ideas wait here. Most wait forever, and that is the point (`docs/PRODUCT.md` law 5.5).
> Nothing here is a step. It becomes one only when it can name an accept criterion that can *fail*
> and a verifier in `docs/verification.md` — the admission rule at the top of `docs/STEPS.md`.
>
> **Append-only. Never delete a row; flip its status.** A deleted row destroys the only evidence
> the idea was ever considered, and a ledger that only contains live ideas cannot tell "never
> proposed" from "quietly dropped".
>
> Business, pricing and positioning notes are kept privately, outside this repository — see
> `docs/ARTIFACTS.md`.

Status vocabulary: **open** (no decision) · **parked** (decided: not now, with a reason) ·
**rejected** (decided: no) · **promoted** (now a step in `docs/STEPS.md`) · **shipped**.

---

## UI direction

Adopted in 4.1 and 4.2, and standing: calm, precise, bank-lobby-not-hacker-movie. Whitespace, a
muted palette with one accent, Inter, subtle depth, dark mode first-class. Avoided, deliberately:
green-on-black terminal cosplay, padlock icons everywhere, red warnings for normal actions, and
dense 2003-era tree-tables. The signature moment is the approval dialog — who wants what, and why —
and it is the brand.

---

## The ledger

| idea | who | status | why |
|---|---|---|---|
| One-time encrypted share links, self-hostable relay, key in the URL fragment | founder | parked | Was Stage 5.1. Real, designed, and gated on launch traction that is tracked outside this repo. No verifier today, so not a step. |
| Multi-vault and vault-per-project ergonomics | founder | parked | Was Stage 5. Wanted, but no accept criterion that can fail has been written. |
| Git-friendly vault workflows and conflict guidance | founder | parked | Was Stage 5. Blocked on O-0018 — two writers, one KDBX, and no merge. |
| Hosted relay and sync as the first paid tier | founder | parked | Was Stage 5.2. Convenience never security (law 5.4). Needs the local-first vs hosted-sync tension below resolved first. |
| Delegation dashboard: aggregate agent grants, MCP connections, external OAuth | founder | parked | Was Stage 6, explicitly gated on benchmarks being hit. A feasibility spike is the first real step and it has not run. |
| Teams: shared env sets by per-member key wrapping (the copy model) | founder | parked | Was Stage 7.1. Gated on revenue. Bounds future access, not past copies — that honesty has to survive into any build. |
| Teams: a broker that releases without copying (the access model) | founder | parked | Was Stage 7.2. The differentiated one — instant revocation, no rotation. Reuses the Stage 2 approval core verbatim or it is not this idea. |
| Team SSO for the hosted service, never on the vault path | founder | parked | Was Stage 7.3. If the IdP being compromised can read a vault, it is the wrong design. |
| Team delegation dashboard | founder | parked | Was Stage 7.4. Gated exactly like Stage 6. |
| "Design language: modern, calm, trustworthy" as a checklist item | founder | rejected | Nothing about it can fail, so it was never a step. The direction above is the durable form. |
| Sign-in-first landing flow from the design exploration | founder | open | Inverts local-first, which `docs/PRODUCT.md` §2 makes permanent. Must be answered before any hosted-sync work; it is the fork the parked Stage 5 rows sit behind. |
| Local-first vs a hosted sync tier | founder | open | §2 permits a zero-knowledge hosted tier if self-host stays first-class. Unresolved, and it changes what gets built above. |
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
| Browser extension with autofill | founder | rejected | Huge effort, incumbents strong. Probably never. |
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
