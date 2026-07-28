# STEPS.md — every step to the finished product

> This file evolves. `docs/PRODUCT.md` does not.
> Done steps are one line. Open steps carry **Build**, **Owner** and **Verify**.

**The admission rule.** A step may be added only if it (a) has an accept criterion that can *fail*,
(b) names its verifier in `docs/verification.md`, and (c) traces to a claim in `docs/PRODUCT.md`.
Fails any one of those and it is a `docs/IDEAS.md` row, not a step. This is the termination
condition: without it the plan grows forever.

---

## Scope

- **Built.** A KDBX4 vault the CLI creates, reads and writes, which KeePassXC opens in both
  directions. Env sets and `keypaste run` injection. The MCP bridge: scoped request, human approval,
  TTL, policy pre-approvals, and a hash-chained audit log. `v0.1.0` published as four native
  binaries. A desktop app that unlocks a vault, browses entries and edits env sets.
- **Building.** Stage 3's launch, and Stage 4's Agent Activity screen — the one screen that answers
  "what can act as me right now?"
- **Later.** Sharing, a hosted tier, the delegation dashboard and teams. All of it is in
  `docs/IDEAS.md` with a status, because none of it yet has an accept criterion that can fail.
- **Out, deliberately.** `docs/PRODUCT.md` §2 — a new vault format, a cloud service holding
  secrets, "for everyone", enterprise IAM. That list is locked and is the ratchet.

**Settled, and not re-opened here.** The stack is C#/.NET on `net10.0` (D-0002) with xUnit v3 on
Microsoft.Testing.Platform (D-0003). The KDBX library is vendored KeePassLib 2.61, chosen on
maturity rather than licence (D-0007). The licence is AGPL-3.0 — see `LICENSE` — and every release
publishes its corresponding source (D-0041). The desktop shell is Avalonia, after Photino and Tauri
were both named in this file and neither survived being checked (D-0044).

---

## Owner Queue

What only a human can do: decide, register, sign, pay, post, or press a key. Nothing below is
agent-runnable, and several of them block steps that are otherwise finished.

| id | What only you can do | Blocks | Where it came from |
|---|---|---|---|
| **H-0001** | Register the `keypaste` GitHub org, and the npm and crates names | Stage 0 | Stage 0, still open since week 1 |
| **H-0002** | Trademark check on the name "keypaste" | H-0001 | the LOCKED decisions block |
| **H-0003** | Decide whether this repository goes public, knowing the decision is irreversible | **the launch** | O-0014 |
| **H-0004** | Choose DCO or CLA and write `CONTRIBUTING.md` | first outside PR | O-0002 |
| **H-0005** | Record the demo GIF — WSL only, a real Claude session, a human keystroke, three to eight takes budgeted | 3.1 | `scripts/demo/README.md`, D-0033 |
| **H-0006** | Post the launch to the five channels | — | `launch.md` holds the copy and the preconditions |
| **H-0007** | Answer every issue and comment for two weeks after the launch | — | Stage 3 |
| **H-0008** | Decide whether the binaries get signed and notarized, and pay for it if so | trust on first run | O-0010, O-0015, THREATS T-21 |
| **H-0009** | Settle Windows clipboard history and the `argv` exposure before the audience stops being one person | **the launch** | O-0008, O-0009 |
| **H-0010** | Run the twenty-one item manual checklist in `docs/desktop.md` — nothing automated has ever seen this app draw | any desktop claim | O-0020 |
| **H-0011** | Run the pre-deploy checklist in `site/README.md` before any keypaste.com deploy | every deploy | `site/README.md`; D-0037 declined to build a CI job for it |
| **H-0012** | Answer who owns the approver pipe once the app can approve | **4.3** | O-0017 |

`[process]` — this queue is a ledger, not a gate. A ticked row is a person's word.

---

## Stage 0 — Foundation (done)

- [x] Repo scaffold: core, CLI and MCP packages, CI, AGPL, `SECURITY.md` — D-0001
- [x] KDBX library chosen and round-tripped: vendored KeePassLib 2.61 — D-0007
- [x] KeePassXC compatibility gated in CI on all three OSes, permanently — D-0008
- [x] `keypaste init`, `add`, `get`, `ls`, `rm` — D-0009..D-0012
- [ ] **Register the org and the package names** — Owner-only, no build lane. See **H-0001**.

## Stage 1 — Env variables and injection (done)

- [x] Env-set convention: KDBX group `env/<project>`, one entry per variable, KeePassXC-editable — D-0014
- [x] `keypaste env pull` with a fail-closed parser and no "shred" claim — D-0015
- [x] `keypaste run <project> -- <cmd>` injects into a real child, writes nothing to disk — D-0016
- [x] `keypaste env export --dotenv`, single-quoted so other readers agree — D-0018
- [x] `docs/replace-dotenv.md` — the five-minute guide

## Stage 2 — The MCP bridge (done)

- [x] `keypaste-mcp` with `list_entry_names` and `request_credential` — D-0019, D-0022, D-0023
- [x] Human approval via `keypaste agent`: default deny, 45-second timeout deny — D-0025
- [x] One field and nothing else, TTL capped by `--max-ttl`, scoped to one connection — D-0026
- [x] Hash-chained JSONL audit log and `keypaste log verify` — D-0031, D-0032
- [x] `policy.toml` pre-approvals, fail-closed, keyed on the operator's label — D-0028..D-0030
- [x] `THREATS.md` T-1..T-20
- [x] `docs/mcp-setup.md` for Claude Desktop and Claude Code
- [x] The 60-second demo, held to the binaries by `scripts/verify-demo.sh` — D-0034, D-0035

## Stage 3 — Launch (building)

- [x] Landing page at keypaste.com with the signup form — D-0037
- [x] Release pipeline: `v0.1.0` on `dl.keypaste.com`, four binaries, checksums, every gate run
      against the exact bytes uploaded — D-0040, D-0041, D-0043
- [x] The launch essay, `docs/keepass-and-agents.md`, retitled because the original title was
      false — D-0038

### 3.1 — The demo GIF [ ]

The README rewrite and the landing page are done. The GIF is the only thing left, and both pages
already reserve the slot.

- **Build** — "Trim the recorded cast to under 2 MB, render it to `docs/demo/keypaste-demo.gif`,
  and drop it into the slot `README.md` and `site/public/index.html` already reserve. Nothing else
  on either page moves."
- **Owner** — **H-0005**. The take itself: WSL only, a real Claude session, a human keystroke.
- **Verify** — `V-0001`
- Traces to `docs/PRODUCT.md` law 5.1, the demo is the marketing.

### 3.2 — The launch posts [ ]

- **Build** — none. The copy is written and lives in `launch.md`.
- **Owner** — **H-0006**, and it is blocked by **H-0003** and **H-0009**. `launch.md`'s own
  "Before anything goes out" list is the precondition set; every item on it is false today.
- **Verify** — `V-0002`
- Traces to `docs/PRODUCT.md` law 5.3, community before customers.

### 3.3 — Two weeks of answering [ ]

- **Build** — none.
- **Owner** — **H-0007**.
- **Verify** — `V-0003` `[process]`
- Traces to `docs/PRODUCT.md` law 5.3.

## Stage 4 — The desktop app (building)

- [x] Shell and unlock: drag/picker/recent, a master-password control that holds no password,
      idle auto-lock on two clocks, five-destination sidebar, light and dark — D-0044
- [x] Entries and env sets: searchable list, detail pane with self-clearing clipboard, inline edit,
      generated passwords, masked variables with reveal-on-hold, and a test proving the shipped CLI
      sees a GUI edit at once — D-0045..D-0050

### 4.3 — Agent Activity [ ]

The seed of the delegation dashboard, and the screen the product is named for.

- **Build** — "Build the Agent Activity screen: a live feed of incoming agent requests with
  Approve/Deny replacing the terminal prompt while the app is open; a history list read from the
  audit log; per-client summary cards showing name, total requests, last seen and the standing
  policy rules affecting it, with a revoke/pause toggle that flips a deny-all rule. Everything
  reads and writes through core. Screenshot-worthy or it is not done."
- **Owner** — **H-0012** first. The app and `keypaste agent` cannot both own the approver pipe, and
  nothing decides which does.
- **Verify** — `V-0004`
- Traces to `docs/PRODUCT.md` §1, wedge item 4.

### 4.4 — Approval prompts leave the terminal [ ]

- **Build** — "Move the approval prompt from the terminal to a native window or tray notification,
  keeping `keypaste agent`'s terminal channel working for headless use. Default deny, timeout deny
  and every error path deny must hold identically on both channels."
- **Owner** — none beyond **H-0012**.
- **Verify** — `V-0005`
- Traces to `docs/PRODUCT.md` law 3.2 and law 3.7.

---

## What is no longer a step

Sharing, the hosted tier, the delegation dashboard and teams were Stages 5, 6 and 7. None of them
has an accept criterion that can fail today, and all of them are gated on benchmarks tracked outside
this repository — so under the admission rule they are `docs/IDEAS.md` rows, not steps. They come
back as steps when one of them earns a verifier.

"Design language: modern, calm, trustworthy" left for the same reason: nothing about it can fail.

---

## Definition of focused

You should always be able to answer: which step am I on, and what would prove it done? If you
cannot, open this file, take the first open step, and read its Verify lane first.
