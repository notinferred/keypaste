# ideas.md — The Parking Lot
> Ideas wait here. Most wait forever, and that's the point (CORE.md law 5.5).
> Nothing here enters a sprint unless it's promoted into PLAN.md with a checkbox.
> Business, pricing, and positioning notes are kept privately, outside this repo.

---

## UI direction (the "KeePass reskin to modern")
- **Design keywords:** calm, precise, bank-lobby-not-hacker-movie. Lots of whitespace, muted palette with ONE accent color, system fonts or Inter, subtle depth, dark mode first-class.
- **Anti-patterns to avoid:** green-on-black terminal cosplay, padlock icons everywhere, red scary warnings for normal actions, dense 2003-era tree-table UI.
- **Signature moments:** the approval dialog (this is the brand — make it beautiful and instantly readable: WHO wants WHAT and WHY); the Agent Activity feed; reveal-on-hold masked values; satisfying auto-clearing clipboard toast with countdown.
- **Screenshot strategy:** always show keypaste next to classic KeePass with the same vault open — "same file, different decade."
- Command palette (Cmd+K) for everything.
- Onboarding: "import your existing .kdbx" as the FIRST option, new vault second — meet users where they are.

## Feature parking lot (unsorted, promote sparingly)
- Browser extension (autofill) — HUGE effort, incumbents strong; probably never.
- Mobile app (read-only companion first if ever).
- TOTP/2FA code storage + agent-safe TOTP handling (agents get a code, never the seed) — strong candidate for post-Stage 4.
- SSH key management + `keypaste ssh` agent integration.
- Git hooks that block committing plaintext secrets (nice free marketing tool, could be standalone).
- "Secret leases" for long-running agents with auto-rotation reminders.
- Passkey storage once KDBX ecosystem support matures (watch KeePassXC's passkey work) — ties back to research report's idea #1 without owning recovery.
- `keypaste env set --no-history`: overwrite a value without keeping the previous one as KDBX
  history, for rotating a credential that leaked. Diverges from KeePassXC's own editor, so it
  has to be opt-in and loud (D-0014).
- Design direction for the GUI, the approval dialog and keypaste.com lives in [DESIGN.md](DESIGN.md)
  and design.html - proposed only, and it needs the local-first vs hosted-sync question in CORE.md
  section 2 answered before any of it is built.
- `keypaste run` on Unix could `execve` the child instead of wrapping it: exit status, job control
  and signals would all be exactly right for free, and the address space holding the secrets would
  be discarded by the kernel. Not done because Windows has no equivalent, so the wrapper has to
  exist anyway - two implementations of one feature (D-0016).
- `keypaste run --no-inherit`: start from an empty environment rather than the current one, the
  shape `env -i` has. Useful for reproducing exactly what CI sees.
- `keypaste run --skip-unusable`: inject what can be injected and warn about the rest, instead of
  refusing. Only if somebody hits it - a partially injected environment fails somewhere else.
- Vault health report: reused passwords, stale entries, weak values, secrets-in-env hygiene score.
- Team approval quorum ("2 of 3 must approve production credentials") — great enterprise-ish story, Stage 6+.
- Windows Hello / Touch ID unlock.
- Import wizards: 1Password, Bitwarden, LastPass exports → KDBX.
- Anomaly nudges: "an agent asked for prod DB creds at 3am — review?"
- Public "trust page": reproducible builds, third-party audit fund, bug bounty.

## Marketing/content ideas
- Series: "Secrets hygiene for the agent era" (each post = one THREATS.md topic, humanized).
- Comparison pages: keypaste vs .env, vs Infisical (honest: "they're for teams/cloud; we're local-first"), vs plain KeePass.
- A public demo vault anyone can point Claude at to feel the approval flow safely.
- Conference/lightning talk: "I let Claude into my password manager — safely."
- Partner with KeePassXC community respectfully: contribute compat fixes upstream, never fork-and-fight.

## Open questions to revisit
- Does the daemon architecture (background keypaste process for approvals) beat per-invocation unlock? (UX vs attack surface.)
- Hardware key (YubiKey) unlock support timing.
- Should policy.toml live inside the vault (synced, encrypted) instead of beside it?
- Naming inside product: "agents" vs "clients" vs "apps" — user testing needed.
