# STEPS.md — every step to the finished product, in tiers

> This file evolves. `docs/PRODUCT.md` changes only by dated re-ratification, and the current one is v1.1 (D-0061). Every step carries the **Build** prompt that produced it or will; open steps also carry **Owner** and a **Falsifier** — the specific thing to try that would prove the step is *not* done.

**The admission rule.** A step may be added only if it (a) has an accept criterion that can *fail*, (b) carries that falsifier, and (c) traces to a claim in `docs/PRODUCT.md`. Fails any one and it is a `docs/IDEAS.md` row, not a step. This is the termination condition: without it the plan grows forever.

**Run the falsifier first.** If it fires, stop — the step is not done and nothing else you find changes that. Results are **PASS**, **FAIL** or **BLOCKED**; "looks right" is not a result, and BLOCKED is not a pass. **A verifier gets this file and the repository and not the builder's transcript** — shared context is how a build and its check agree with each other while both are wrong. That is a habit rather than a gate: `scripts/verify-claims.sh` checks that every step a document cites exists, and nothing checks that a falsifier can fire.

**Every Build prompt runs with `docs/PRODUCT.md` in context.** It is law, and a prompt that has not read it will violate it.

**Nothing is gated on revenue or on a benchmark (D-0074).** A tier starts when the tier before it has shipped. Every step below has a falsifier, including the ones that used to sit in a "Gated" section waiting to earn one.

---

## Scope, in tiers

- **Tier 0 — shipped.** A KDBX4 vault the CLI creates, reads and writes, which KeePassXC opens in both directions. Env sets and `keypaste run` injection. The MCP bridge: scoped request, human approval, TTL, policy pre-approvals, and a hash-chained audit log. `v0.1.0` published as four native binaries. A desktop app that unlocks a vault, browses entries and edits env sets, built from source and not released.
- **Tier 1 — the first dollar (D-0065).** The launch of what exists (0.4, 1.5a, 3.1, 3.2, 3.3). The merge the sync needs (1.4). The app becoming the product: Agent Activity with the headline number (4.3), a native approval prompt (4.4), and a signed, notarized, installable release (4.7). The relay, hosted sync and Individual billing (5.2). Nobody is charged until 4.7 and 5.2 have both shipped.
- **Tier 2 — the password manager (D-0068).** The browser: native messaging host (8.1), the approval popup (8.2), autofill (8.3). Importers (9.1), TOTP (9.2), an SSH agent (9.3). The UX bench and the render gates (4.5, 4.6), which un-park now that there is someone to measure (D-0073).
- **Tier 3 — sellable to teams.** Shared env sets (7.1), the broker (7.2), SSO for the hosted service (7.3), the delegation spike and center (6.1, 6.2), the team dashboard (7.4).
- **Out, deliberately.** `docs/PRODUCT.md` §2 — a new vault format, a cloud that can read secrets, browser-only consumers with no vault, enterprise IAM. That list is the ratchet.

**Settled, and not re-opened here.** The stack is C#/.NET on `net10.0` (D-0002) with xUnit v3 on Microsoft.Testing.Platform (D-0003). The KDBX library is vendored KeePassLib 2.61, chosen on maturity rather than licence (D-0007). The licence is AGPL-3.0 — see `LICENSE` — and every release publishes its corresponding source (D-0041). The desktop shell is Avalonia (D-0044). The relay is one .NET binary over S3-compatible storage (D-0064). The tier ladder is three tiers, and the figures are in the working file `docs/ARTIFACTS.md` names, never here (D-0063).

---

## The standing gates

These hold the steps that are already done. Each one is a script and the script is the specification, so none is re-derived here. **They run the software** — they build binaries, drive real `keepassxc-cli`, spawn real children, tamper with the audit chain.

| gate | holds | observed failing |
|---|---|---|
| `scripts/verify-demo.sh` | every transcript on the five pinned pages matches what the shipped binaries print | yes — caught a mode-bit regression |
| `scripts/verify-install.sh` | the README install block, executed verbatim, on a scratch `HOME` | yes — `--negative` decoy |
| `scripts/verify-keepassxc-compat.sh` | the vault opens in a real `keepassxc-cli`. Permanent (law 4.6) | not recorded |
| `scripts/verify-keepassxc-writeback.sh` | a KeePassXC edit is visible to `keypaste env ls` | not recorded |
| `scripts/verify-run-injection.sh` | `run` injects into a real child and writes nothing to disk | not recorded |
| `scripts/verify-run-signals.sh` | SIGTERM reaches the child; exit status is reported. Unix only | not recorded |
| `scripts/verify-mcp-stdio.sh` | nothing but protocol on stdout; every call audited | not recorded |
| `scripts/verify-approval-e2e.sh` | approved returns the secret, refused returns nothing, neither is logged | not recorded |
| `scripts/verify-policy-e2e.sh` | a standing rule grants silently; a rule can never widen | not recorded |
| `scripts/verify-log-chain.sh` | tampering is detected; truncation reads as damage, not attack | not recorded |
| `scripts/verify-aot-trim.sh` | no new trim diagnostic naming `src/` | yes — an empty log is a failure |
| `scripts/verify-claims.sh` | every step a document cites exists; nothing untracked and unignored | yes — a dangling step reference |

**The "observed failing" column is the point.** A check nobody has watched fail is an assertion about the world, not a check on it (D-0043). Eight of these have never been recorded failing, and filling that column in is real work that is not done. It is also the cheapest work on this page: break one on purpose, watch it go red, put it back.

**None of them runs on the founder's machine today.** `global.json` pins .NET SDK 10.0.302 and the machine has 10.0.203, which the `latestPatch` roll-forward cannot cross; every `dotnet` command in this repository fails until **H-0016** is done. Until then a local change is checked by reading, by `scripts/verify-claims.sh` (bash only), and by CI on push.

---

## Owner Queue

What only a human can do. An action needs doing and has a next command; a decision needs choosing and carries its options and a default. A row phrased as "decide whether X" with no options is a prompt to agonise, not to decide.

### Actions — these have a next command, not a question

| id | Do this | Next command | Blocks |
|---|---|---|---|
| **H-0001** | Register `keypaste`: **GitHub org and npm are free** (checked 2026-08-04, 404 on both); crates.io is settled by D-0053 and needs nothing | github.com/organizations/new, then transfer this repo | 0.4 |
| **H-0003** | **Answered 2026-08-04: public once a release actually works, and not before.** §3.8 always required open source; only the date was ever open. The identity rewrite is done (2026-08-05): every commit on all nine branches and both tags reads `ochoadan <hello@danochoa.com>`. **What blocks the flip is `refs/pull/*`** — GitHub keeps a head ref per pull request that no push can delete; `refs/pull/11/head` is `e972225`, authored `Claude <noreply@anthropic.com>`, and its ancestry carries the whole pre-rewrite log. Still served by origin on 2026-09-04. Private they need a token; public they are fetchable | ask GitHub Support to purge the stale `refs/pull/*` and gc the repo, or push the clean history to a fresh repo and delete this one; then Settings → Change visibility. Run **H-0020** first | **3.2** |
| **H-0005** | Record the demo GIF — WSL only, a real Claude session, a human keystroke, three to eight takes budgeted | `scripts/demo/install-recording-tools.sh` (needs sudo), then `record-demo.sh` | 3.1 |
| **H-0006** | Post the launch to the five channels | `launch.md` holds the copy and the preconditions | — |
| **H-0007** | Answer every issue and comment for two weeks after the launch | — | — |
| **H-0010** | Run the twenty-one item checklist in `docs/desktop.md` | run it by hand until 4.6 strikes the items it can | any desktop claim |
| **H-0011** | Run the pre-deploy checklist in `site/README.md` before any keypaste.com deploy | `site/README.md`; D-0037 declined to gate it | every deploy |
| **H-0015** | Enrol in the Apple Developer Program so macOS binaries can be notarized (D-0057) — 99 USD a year, and only a person can accept the agreement | developer.apple.com/programs/enroll, then put the credentials in repository secrets for `release.yml` | 4.7 |
| **H-0016** | Install .NET SDK 10.0.302 on the development machine; nothing in this repository builds without it | `winget install Microsoft.DotNet.SDK.10 --version 10.0.302` or the dotnet-install script, then `dotnet --version` prints `10.0.302` from inside the repo | every local gate |
| **H-0017** | Enrol in Azure Trusted Signing (D-0070) — re-verify price and individual eligibility at enrolment; then put the identity in repository secrets | portal.azure.com → Trusted Signing → create account and certificate profile | 4.7 |
| **H-0018** | Open the Stripe account; create the Individual and Team products with the prices in the working file | dashboard.stripe.com, then the webhook secret into the relay's configuration | 5.2 |
| **H-0019** | Provision the relay host and its S3-compatible bucket; point `sync.keypaste.com` at it behind Cloudflare | a small VM, R2 bucket `keypaste-sync`, the credentials in the host's environment only | 5.2 |
| **H-0020** | Scan the 16 commits that landed after the last history scan (175 → 191) for anything a public repository must not carry | `git log 9d48bd7..main --stat`, then the same grep the first scan used | H-0003 |

### Decisions — none open

Every decision through D-0074 is answered and the ledger at the top of `DECISIONS.md` is the list. `[process]` — a row belongs here only while it is unmade, and **a default is not consent**: an answer must name a trigger, a step or a next command — never a preference.

---

## Tier 0 — shipped

One line each; the reasoning is in `DECISIONS.md`.

- **0.1 — Repo scaffold [x]** — D-0001.
- **0.2 — KDBX round-trip [x]** — vendored KeePassLib; the KeePassXC gate runs on all three OSes and is permanent. D-0007, D-0008.
- **0.3 — Core CLI verbs [x]** — `init add get ls rm`. D-0009 to D-0012.
- **1.1 — Env storage convention [x]** — one entry per variable, gated in CI in both directions. D-0014.
- **1.2 — Import and inject [x]** — `env pull` and `run`, fail-closed parser, signals relayed never escalated. D-0015, D-0016.
- **1.3 — Escape hatches and docs [x]** — `env export`, single-quoted. D-0018.
- **1.5b — The pages say what the clipboard formats close, and what they do not [x]** — six pages, one residual each. D-0056.
- **2.1 — MCP server skeleton [x]** — two tools, official SDK, denies without an approver. D-0019, D-0022.
- **2.2 — Human approval flow [x]** — `keypaste agent` as a separate process; 45 seconds. D-0023, D-0025, D-0026.
- **2.3 — Policy pre-approvals [x]** — keyed on `--client-label`, evaluated after the exposure re-check so a rule can only narrow. D-0028 to D-0030.
- **2.4 — Audit log and threat model [x]** — the chain names what it cannot detect on every pass. D-0031, D-0032.
- **2.5 — The 60-second demo [x]** — `scripts/verify-demo.sh` holds the transcripts; Claude is deliberately not in CI. D-0034, D-0035.
- **3.4 — Release pipeline and the install one-liners [x]** — `v0.1.0` on `dl.keypaste.com`, four native binaries. D-0040, D-0041, D-0043.
- **3.2b — Launch essay [x]** — `docs/keepass-and-agents.md`. D-0038.
- **4.1 — Desktop shell and unlock [x]** — Avalonia; the master password never enters a `TextBox`. D-0044.
- **4.2 — Entry and env UIs [x]** — password generator, shared clipboard rule, lost-write guard. D-0045 to D-0050.

---

## Tier 1 — the first dollar

### 0.4 — The names [ ]

- **Build** — none. Nothing here is agent-runnable.
- **Owner** — **H-0001**. The name is used and not defended (D-0058).
- **Falsifier** — open `https://github.com/keypaste` **logged out**. If it resolves to somebody else's account or organisation, this is FAIL and the name question is bigger than a registration. Same for `npmjs.com/package/keypaste`. Then: all are held by this project, or `DECISIONS.md` records which were unavailable and what the product is called there instead (D-0053: crates.io is somebody else's and keypaste needs nothing there); `launch.md`'s canonical link matches whatever was registered. **BLOCKED** if the registries load but you cannot confirm ownership logged out.
- Traces to `docs/PRODUCT.md` law 5.2, and to §1 — the product has to be findable under the name it claims.

### 1.4 — Entry-level merge [ ]

Two machines syncing one KDBX — by Dropbox today, by 5.2 tomorrow — end with two divergent copies, and keypaste has no answer: the app refuses to save (D-0050) and the CLI does not notice. This is O-0018, and the merge is what 5.2's sync delivers a pulled blob into.

- **Build** — "Implement `Vault.Merge` in `Keypaste.Core` and `keypaste merge <other.kdbx>` as a thin CLI over it, resolving O-0018. Match entries by **KDBX entry UUID only** — never by title or group path, because title matching is how the wrong secret is silently overwritten. For each incoming entry: absent locally, add it under its own group path; present and identical, no-op; present and differing, the newer `LastModificationTime` wins **and the superseded value is pushed onto that entry's KDBX history**, so nothing is destroyed and the loser is still readable in KeePassXC. Equal timestamps with differing content is a **conflict: name every conflicting entry, write nothing, exit nonzero** (law 3.7). **Deletions never propagate** — an entry absent from the incoming file is not a deletion, because a scoped file is a subset by construction, and reading absence as intent would let a four-entry file empty a vault. Support `--key <path>` and `--key-b64` for a keyfile-protected source, which means wiring `KcpKeyFile` into `KeePassInterop` — that file stays the only one in the repository permitted to reference KeePassLib (D-0007), and whether vendored KeePassLib 2.61's keyfile path round-trips against real `keepassxc-cli` is the first thing to establish, before anything is built on it. Print the plan and require an explicit confirmation before writing, with `--yes` for scripts and `--dry-run` to print and stop. Add `scripts/verify-merge.sh` proving: a merged vault opens in real `keepassxc-cli`; merging the same file twice is a no-op with no duplicate UUIDs; an older incoming entry does not overwrite a newer local one; and the superseded value survives in history. Record the conflict policy and the deletion decision as **D-0052** (the number is reserved for it), and close O-0018 or state precisely what remains open."
- **Owner** — none.
- **Falsifier** — build a vault holding one entry and note its value. Build a second file carrying the **same entry UUID** with a different value and an **older** `LastModificationTime`. Run `keypaste merge`. **If the local value changed, FAIL** — taking the incoming side because it is the incoming side is file-order precedence, not a merge. Then: (1) the identical merge a second time reports no changes and produces no duplicate UUIDs; (2) equal timestamps with differing content exits nonzero, names every conflict, and leaves the vault **byte-identical** — check the hash, not the output; (3) a four-entry file merged into a forty-entry vault leaves all forty, because absence is not deletion; (4) an entry that lost has its superseded value recoverable from KDBX history; (5) renaming an incoming entry's title but keeping its UUID updates rather than adds, and the reverse adds rather than overwrites — that pair is what proves the match key is the UUID; (6) `scripts/verify-keepassxc-compat.sh` is still green. **BLOCKED** without a real `keepassxc-cli`.
- Traces to `docs/PRODUCT.md` §1 item 4 (sync), law 4.3 and law 4.6.

### 1.5a — The Windows CLI copies through one Win32 clipboard session [ ]

D-0056 split the clipboard defect from the `argv` exposure and this is the half that gets fixed. The app was fixed first (D-0046) because it owns a window; this restores parity for `keypaste get`.

- **Build** — "Replace the `clip.exe` shell-out in `Keypaste.Cli`'s Windows clipboard path with a Win32 one that can express KeePassXC's three opt-out formats: `ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory` and `CanUploadToCloudClipboard`. Set the text and all three inside **one** `OpenClipboard`/`EmptyClipboard`/`SetClipboardData`×N/`CloseClipboard` session — the history service acts on the notification raised at `CloseClipboard`, so a second session to add the markers has already leaked. P/Invokes go in one `[SupportedOSPlatform(\"windows\")]` class; satisfy the trim and AOT analysers rather than suppressing them. macOS and Linux are untouched and O-0019 stays open. Law 4.5 keeps the tests in this step: assert every registered name with a `GetClipboardFormatName` round-trip rather than trusting the literal, and make the clear guard compare a hash so no plaintext copy lives for the timeout window."
- **Owner** — none.
- **Falsifier** — **the code is written and its unit tests pass; this is all that is left.** On a real Windows machine with Clipboard History enabled (Settings → System → Clipboard), run `keypaste get` on an entry so the password reaches the clipboard. Press **Win+V** before the clear timeout expires, and again after. **If the value appears in the history panel at either moment, FAIL.** Then check cloud clipboard on a second machine signed into the same account. A VM is fine; Wine or WSL is not. Then: (1) `GetClipboardFormatName` round-trips every registered format, asserted in a test — KeePassXC ships `"CanUploadToCloudClipboard "` with a trailing space in every released version; (2) all formats are set in **one** clipboard session; (3) the clear guard holds a hash, not the secret; (4) the clear still refuses to wipe something the user copied afterwards; (5) macOS and Linux are untouched and O-0019 is still open. **BLOCKED** without a real Windows machine with clipboard history on. **Attempted 2026-09-04 on the founder's own machine and BLOCKED there permanently:** `HKLM\SOFTWARE\Policies\Microsoft\Windows\System` sets `AllowClipboardHistory=0` and `AllowCrossDeviceClipboard=0`, a deliberate hardening choice that is not being changed for a test. A per-user Settings toggle cannot override machine policy, so an empty Win+V panel there means the feature is off, **not** that the formats worked — that near-miss is why this falsifier demands a control. **Use a VM with default settings.** `Windows.ApplicationModel.DataTransfer.Clipboard.GetHistoryItemsAsync()` reads the history from PowerShell and returns both a status and the item text, so on a machine where history is enabled this check can be a script with a positive control — copy a known string, assert it lands, then assert the secret does not.
- Traces to `docs/PRODUCT.md` law 3.4 — clipboard history persists the secret and cloud clipboard sends it off the machine, both by keypaste's doing — and to law 4.5.

### 3.1 — README, landing page and the demo GIF [ ]

The README rewrite and the landing page are done. The GIF is the only thing left, and both pages already reserve the slot.

- **Build** — "Trim the recorded cast to under 2 MB, render it to `docs/demo/keypaste-demo.gif`, commit the cast beside it as `docs/demo/keypaste-demo.cast`, and drop the GIF into the slot `README.md` and `site/public/index.html` already reserve. Nothing else on either page moves."
- **Owner** — **H-0005**. The take itself: WSL only, a real Claude session, a human keystroke.
- **Falsifier** — `ls -l docs/demo/keypaste-demo.gif`. Absent is FAIL; 2 MB or larger is FAIL, because the budget is what keeps the README usable on a phone. Then: both `README.md` and `site/public/index.html` reference it and neither still carries the reserving HTML comment; the cast is committed beside it and `grep` finds no master password and the sentinel exactly once; the GIF shows, in order, an agent asking, the approval dialog with a reason, a human answering, and the log afterwards; and `scripts/verify-demo.sh` is still green. **Before rendering, the cast must pass `record-demo.sh`'s own controls** — one take was rejected on 2026-08-26 for never releasing the credential.
- Traces to `docs/PRODUCT.md` law 5.1, the demo is the marketing.

### 3.2 — The launch posts [ ]

- **Build** — none. The copy is written and lives in `launch.md`.
- **Owner** — **H-0006**, blocked by **H-0003** and by step **1.5a**'s falsifier, which needs a person at a Windows machine. `launch.md`'s "Before anything goes out" list is the precondition set, and its unticked items are what remain.
- **Falsifier** — read `launch.md`'s "Before anything goes out" list. **If any box there is unticked, FAIL** regardless of what was posted; each item is something a stranger hits before they hit the product. Then: every one of the five channels has a live URL that loads for a logged-out reader; every link inside every post resolves — if the repository is still private, every repository link is a 404 for the audience the post was written for, and that is FAIL, not a caveat; the install command in each post matches what `README.md` currently documents; and every post says "no released GUI", never "no GUI" (D-0069). **BLOCKED** if the posts exist but you cannot see them logged out.
- Traces to `docs/PRODUCT.md` law 5.3, community before customers.

### 3.3 — Two weeks of answering [ ]

- **Build** — none.
- **Owner** — **H-0007**.
- **Falsifier** `[process]` — find the oldest issue or comment opened after the launch date with no reply from the maintainer. If one exists and is older than 48 hours, FAIL. Not mechanizable: whether a reply was *useful* is a judgement.
- Traces to `docs/PRODUCT.md` law 5.3.

### 4.3 — Agent Activity, and the headline number [ ]

The screen the product is named for, and the one number it sells (D-0066): **what can act as you right now**.

- **Build** — "Build the Agent Activity screen as a client of the running `keypaste agent` (D-0054): add a UI-client message kind to `ApproverProtocol` — subscribe, list-pending, answer — so the app renders the requests the agent is holding and sends approve and deny back over the same pipe. At the top of the screen, the headline number: **live connections + unexpired grants + standing `[[allow]]` rules**, each shown beside the sum, read from the agent for the first two and from `PolicyLoader` for the third. With no agent listening the screen says *nothing is listening* in those words and shows no number at all — never zero, because zero is a claim. Below it: a live feed of incoming requests with Approve and Deny; a history list from the audit log rendered by `AuditText`, verbatim; per-client cards (label, total requests, last seen, standing rules affecting it) with a *pause this client* toggle that writes a deny-all rule to `policy.toml` and shows the line it wrote. Design it like the hero feature, because it is."
- **Owner** — none.
- **Falsifier** — start the app with **no `keypaste agent` running** and open Agent Activity. If the screen renders as though it were live — an empty feed presented as "no requests" or a headline of `0` rather than "nothing is listening" — **FAIL**. A screen that cannot tell "nobody asked" from "nothing is connected" is worse than no screen, because it reads as a safety claim. Then, with a real agent connected: (1) drive a `request_credential` and watch it appear in the feed *before* it is answered; (2) approve from the app: the secret reaches the client and the log names the app as the channel; (3) deny from the app: nothing reaches the client; (4) toggle pause on a client card, request again, and it is refused with `keypaste policy ls` showing the deny-all rule the toggle wrote; (5) kill the app mid-request — it fails closed; (6) lock the vault — the feed stops showing entry names; (7) grant a credential with a 60-second TTL and watch the headline number rise by one and fall by one when it expires, without a refresh. **All seven.** (4) is what makes it a control panel rather than a log viewer; (7) is what makes the number live rather than a count.
- Traces to `docs/PRODUCT.md` §1 items 2 and 5.

### 4.4 — Approval prompts leave the terminal [ ]

- **Build** — "Move the approval prompt from the terminal to a native window or tray notification, keeping `keypaste agent`'s terminal channel working for headless use. Both render the approval moment as 8.2 will specify it: the fields, their order, the wording, the default and the timeout, with the agent's reason as untrusted text. Default deny, timeout deny and every error path deny must hold identically on both channels."
- **Owner** — none. D-0054 applies: the terminal channel stays because the agent never stops owning the pipe.
- **Falsifier** — trigger a request with the native prompt on screen and do nothing until the timeout expires. **If it resolves to anything other than deny, FAIL** — and check the same on the terminal channel, because the defect that matters is the two channels disagreeing. Then, for each channel independently: (1) dismissing the window is a denial, not a cancel; (2) timeout is deny; (3) killing the approver mid-prompt refuses the client; (4) the reason string is the agent's, rendered as untrusted text — send newlines and terminal escapes and confirm neither channel draws a second prompt; (5) headless still works, with no display `keypaste agent` prompts in the terminal. **All five on both channels.** Any behaviour holding on one channel and not the other is FAIL, because two security paths is what law 4.3 forbids.
- Traces to `docs/PRODUCT.md` law 3.2 and law 3.7.

### 4.7 — The app is released [ ]

The app exists and is not on `dl.keypaste.com`. Until it is, there is nothing for a buyer of 5.2 to sync with (D-0065).

- **Build** — "Make `app.yml` publish `keypaste-app` for `win-x64`, `osx-arm64` and `linux-x64` on a `v*` tag, beside the CLI assets and under the same immutability rule. Windows: an installer (WiX or Inno Setup) signed with Azure Trusted Signing (D-0070) in the workflow. macOS: an `.app` bundle in a `.dmg`, codesigned and notarized with the H-0015 credentials (D-0057). Linux: a `.tar.gz` and an AppImage, with the four runtime packages `docs/desktop.md` names asserted on a clean Debian 12 container. Run `keypaste-app --selftest` and `--version` on every published artifact before upload, the way `release.yml` does for the CLI. Publish the measured size on both pages (O-0016: 207 MB self-contained today) and nothing smaller. Rewrite the sentence on `README.md`, `CHANGELOG.md`, `docs/keepass-and-agents.md` and `site/public/index.html` that says the binaries are unsigned, because it stops being true here. Resolve O-0015 and O-0016 in `DECISIONS.md` with what was actually done."
- **Owner** — **H-0015** and **H-0017** before the first tag.
- **Falsifier** — on a fresh Windows VM with default settings, download the installer from `dl.keypaste.com` in a browser and double-click it. **If SmartScreen shows the unknown-publisher wall, FAIL.** On a fresh macOS, open the `.dmg` from a browser download; if Gatekeeper refuses it or asks to strip quarantine, FAIL. Then: `keypaste-app --version` on each artifact equals the tag; the size on both pages equals `ls -l` of the archive within 1 MB; the app opens a vault the CLI on the same machine created; and the checksum line beside each asset verifies. **BLOCKED** without H-0015 and H-0017.
- Traces to `docs/PRODUCT.md` §1 item 1, law 4.4 and law 5.4 (the signed binary is the free one).

### 5.2 — The relay: hosted sync, share links, and the first paid tier [ ]

D-0064 gives it a shape and D-0060 its one invariant: the server cannot read the blob. Replaces the old 5.0 (share bundle) and 5.1 (share relay), which are two endpoints on the same binary.

- **Build** — "Build `src/Keypaste.Relay`, one NativeAOT binary with the same gates and lock-file discipline as the others, over S3-compatible storage (`R2` for the hosted instance, MinIO or a local directory for a self-hoster) and SQLite for accounts and licence keys. Endpoints, all authenticated by a per-device key the client generates: put blob, get blob with an ETag, list versions, and a one-download bundle (D-0064) that deletes after the first fetch or 24 hours, whichever is first. **The relay stores ciphertext and metadata only** — a KDBX file is already encrypted with the master password, so the blob is the file; the relay never sees a master password, a keyfile, or a plaintext field, and a test greps the relay project for every KeePassLib and `Keypaste.Core` vault type and fails if one appears. Client side, in `Keypaste.Core`: `keypaste sync` and the app's Sync screen pull the blob, `Vault.Merge` it (1.4), and push the result with the ETag so a lost write is a refused write, not an overwrite. Share: `keypaste share <entry|env/project>` writes a real KDBX4 bundle holding **only** the named subtree with source UUIDs preserved, protected by a fresh 32-byte keyfile and no password, uploads it, and prints a link whose fragment carries the keyfile — so the relay holds ciphertext it can never read and `keepassxc --keyfile` opens what the recipient downloads. Answer O-0021 before the endpoint ships: an imported bundle lands in a quarantine group, never directly under `env/`, until the user moves it. Billing: Stripe Checkout creates an Individual or Team subscription, the webhook issues a licence key, the relay checks the key on every push, and **nothing in any client is gated** — a self-hosted relay needs no key and the code path for that is the same binary with no Stripe configuration. The relay also sends the double opt-in mail `site/public/thanks/index.html` promises, so the promise is kept before any message goes to the list. Write `docs/sync.md` for a user and `docs/relay.md` for a self-hoster, saying plainly what the operator can see (blob sizes, timestamps, device keys, email addresses) and cannot (anything inside a vault), and extend `THREATS.md` with the relay as a new trusted party. Add `scripts/verify-relay.sh`: start the binary against a temp directory, push, pull, merge, share, burn, and assert the second bundle fetch fails."
- **Owner** — **H-0018** and **H-0019** for the hosted instance; the self-hosted path needs neither.
- **Falsifier** — dump the relay's storage and its SQLite after a push from a vault holding a known sentinel value. **If the sentinel, the master password, or any entry title appears in any byte of either, FAIL.** Then: (1) `strings` over the relay binary finds no KeePassLib type name; (2) remove a licence key server-side and the next push is refused with a message naming the plan, while `keypaste ls`, `get`, `run` and the app's every screen behave exactly as before — a client that changes behaviour on licence state is FAIL; (3) the same binary with no Stripe configuration accepts pushes from any device key, which is the self-hosted path; (4) a bundle's second fetch returns 404; (5) an imported bundle lands in quarantine and `list_entry_names` does not list it until it is moved; (6) `scripts/verify-relay.sh` passes against the published binary in `release.yml`; (7) a signup that never confirmed receives no list message. **BLOCKED** without 1.4.
- Traces to `docs/PRODUCT.md` §1 item 4, §2 (the server cannot read your secrets), law 3.1, law 5.4 and law 5.6.

---

## Tier 2 — the password manager

### 4.5 — The UX bench [ ]

`docs/IDEAS.md` threw out "design language: modern, calm, trustworthy" for the right reason: nothing about it can fail. This is the same ambition rebuilt so that it can. Un-parked by D-0073: from 4.7 there is somebody to measure.

- **Build** — "Write `docs/ux.md`: keypaste's UX bench, adapted from Google's HEART and its Goals–Signals–Metrics discipline. **Take three of the five dimensions and strike the other two on the page, with the reason.** Task success, Happiness and Adoption are observable; Engagement and Retention require behavioural telemetry, which law 3.5 forbids forever — so they are struck out in the document itself rather than quietly omitted. Define every task as `T-NN`, each with: the surface it runs on, the exact words given to the participant, a **numeric threshold that can fail** (time-to-complete and a success ratio out of five), and the method that produces the number. Cover at minimum first unlock, copy a password, inject an env set into a real command, approve an agent request, deny one, find out afterwards why something was denied, and — from Tier 2 — fill a login on a page. Then write `scripts/verify-ux.sh` asserting that (a) every task in `docs/ux.md` carries a numeric threshold and a named method, (b) every open step in `docs/STEPS.md` that touches a user-visible surface names at least one `T-NN`, and (c) no task's threshold is written as a range or a word — 'fast' is not a threshold. Nielsen's five-participant finding is why the ratio is out of five; say so on the page."
- **Owner** — running the sessions is `[process]`. A threshold nobody has measured against a human is an assertion (D-0043).
- **Falsifier** — open `docs/ux.md` and pick any task. If its threshold is a word or a range, FAIL. If `scripts/verify-ux.sh` passes with a task whose method line is blank, FAIL — that is the gate not checking. Then hold one person who has never seen keypaste to `T-01` (first unlock) with a stopwatch and record PASS or FAIL against the number on the page; a bench nobody has run against a human is BLOCKED, not PASS.
- Traces to `docs/PRODUCT.md` law 5.1.

### 4.6 — The app draws in CI [ ]

Closes O-0020. Everything Tier 2 claims about a screen rests on this existing first. Un-parked by D-0073.

- **Build** — "Resolve O-0020. Add `Avalonia.Headless` with the Skia backend to `tests/Keypaste.App.Tests` so views render to a bitmap with **no display of any kind**, and assert on the pixels. Golden images are generated and compared on **Linux only** — font stacks and subpixel rendering differ per platform; on macOS and Windows run the same renders and assert structurally (element bounds, visibility, computed colours). **The assertions that matter are secret-path assertions, and they come first:** the password field renders as dots and never as characters; a masked env value renders as dots until held; releasing a hold returns it to dots within one frame; and a locked window renders no entry titles at all. Then the rest: the unlock empty state, both themes, the clipboard countdown mid-drain, Agent Activity in both its states, and every screen at a narrow and a wide window. Store goldens under `tests/Keypaste.App.Tests/golden/`, fail on any diff above a stated anti-aliasing tolerance, and write the failing render to the CI artifacts. Add the job to `app.yml`. Update `docs/desktop.md` to strike the checklist items this now covers, and state which of the twenty-one still need a human."
- **Owner** — none.
- **Falsifier** — change `MaskedInput` to draw the typed character instead of a dot and run the suite. **If it stays green, FAIL** — the gate is not looking at the pixels. Then: the golden for a locked window contains no glyph run at all; a golden diff above the tolerance fails the job and the failing render is downloadable from the run; and `docs/desktop.md` names each struck item with the test that struck it.
- Traces to `docs/PRODUCT.md` law 4.5 — in a GUI the screen *is* a secret path.

### 8.1 — Native messaging host [ ]

The browser is where agents and logins both live, and `docs/keepass-and-agents.md` already argues that the KDBX ecosystem answered "another program wants a credential" once before, with a local pipe and a Confirm Access dialog.

- **Build** — "Build `keypaste-browser-host`: a native-messaging host speaking Chrome's 32-bit-length-prefixed JSON framing over stdio, and an extension skeleton that loads in both Chrome (MV3 service worker) and Firefox (MV3 with an event page — a single build must load in both or the story is two extensions). **The host holds no vault and decides nothing** — it relays to a running `keypaste agent` over the same local channel `keypaste-mcp` already uses, preserving the D-0023 split exactly: the only process that ever sees a master password is the one the human started. Add `keypaste browser install [--chrome] [--firefox] [--edge]` writing the native-messaging manifest to the correct per-OS location (registry keys on Windows, `NativeMessagingHosts/` on macOS and Linux) with the extension ID pinned, and `keypaste browser uninstall` removing exactly what it wrote. **Fail closed and legibly:** no agent running means the extension says so and offers the command to start one — never a password prompt, never a silent retry. Extend THREATS.md with the new surface: a store's auto-update channel can push code to users without a git tag, which is the first time that has been true of anything keypaste ships; state what is and is not signed, and what a compromised extension can and cannot reach given the host holds no vault."
- **Owner** — registering on the Chrome Web Store and on AMO, and whatever identity each demands. Extension IDs must exist before the manifest can pin them.
- **Falsifier** — with no `keypaste agent` running, click the extension. **If anything resembling a password field appears, FAIL.** Then: the host binary contains no KeePassLib type name (`strings`); `keypaste browser uninstall` leaves the manifest directory and registry exactly as before install, checked by a before/after diff; the same extension zip loads unmodified in Chrome and Firefox; and a request from the extension appears in `keypaste log` with the client label the manifest pinned.
- Traces to `docs/PRODUCT.md` §1 item 2, law 3.1 and law 3.7.

### 8.2 — The approval moment, one spec, three surfaces [ ]

- **Build** — "Write the approval moment once in `docs/ux.md` — the fields, their order, the wording, the defaults, the timeout — and make the terminal prompt, the desktop Agent Activity screen, the native prompt from 4.4 and the browser extension popup all render *that*, with no surface inventing a field or a default of its own. Then build the popup: who is asking, which entry, which field, for how long, and the agent's stated reason **rendered as untrusted text and labelled as the agent's words**, with newlines, terminal escapes and RTL overrides defanged. Default deny; closing the popup is a denial, not a cancel; the 45-second timeout is a denial and the countdown is visible. Hold every surface to the **same** `T-NN` threshold in `docs/ux.md`. Screenshot-test the popup in headless Chrome and headless Firefox the way 4.6 does the app."
- **Owner** — none. D-0054 answers it: the agent owns the pipe and every surface is a client of it.
- **Falsifier** — send a request whose reason contains a newline followed by `Approve? [y/N]` and a U+202E override, to each surface in turn. **If any surface draws a second prompt or reverses the reason's text, FAIL.** Then: close the popup mid-countdown and the client receives a denial with "do not retry" absent; let it time out and the client receives a denial; and the four surfaces show the same fields in the same order, checked against the list in `docs/ux.md`.
- Traces to `docs/PRODUCT.md` law 3.2, law 3.7 and law 4.3.

### 8.3 — Autofill [ ]

The feature that makes a password manager one. Rejected once on effort and incumbents, deferred behind a condition by D-0059, and admitted by D-0061 and D-0068. The effort is why it sits here and not in Tier 1.

- **Build** — "Implement credential autofill in the extension, KeePassXC-parity and no further. **Match on the registrable domain via the Public Suffix List, never on a substring of the URL** — `evil.com/paypal.com` is the entire history of autofill vulnerabilities in one string, and a phishing page that fills is worse than no autofill at all. On a match, show the Confirm Access dialog first — which page, which entry, and Remember offered as an option and never assumed — rendered by 8.2's spec. Fill the form fields directly; **never place a credential on the clipboard as a fallback**. No password capture on submit and no vault writes from the extension in this step: reading is one threat model and writing from inside a page's process is another. Iframes are refused unless same-origin with the top document. Add the phishing-domain cases as tests before the happy path, since the happy path is the one that gets manually checked and the malicious one is not. Then update the comparison table in `README.md`, because a keypaste with autofill is answering a different question than the one that table currently asks."
- **Owner** — none.
- **Falsifier** — store an entry with URL `https://paypal.com`. Open `https://evil.example/paypal.com/login` and `https://paypal.com.evil.example/`. **If the extension offers to fill on either, FAIL.** Then: a page in an iframe from another origin gets no fill; the Confirm Access dialog appears before the first fill on a matching page and Remember is unchecked; with the clipboard holding a sentinel, a fill leaves it holding the sentinel; and the phishing tests exist and were watched failing against a substring matcher before the PSL matcher was written.
- Traces to `docs/PRODUCT.md` §1 item 1 and law 3.7.

### 9.1 — Importers [ ]

- **Build** — "Implement `keypaste import <format> <file>` and the app's Import screen over one `Keypaste.Core` importer per source: 1Password (1PUX and CSV), Bitwarden (JSON), LastPass (CSV) and KeePassXC (CSV). Each importer maps to ordinary KDBX entries — title, username, password, URL, notes, and TOTP seeds into the `otp` attribute 9.2 reads — and puts everything else into notes rather than dropping it silently. Print a plan (N entries, N groups, N fields that could not be mapped, by name) and require confirmation. Never write the source file back; offer to delete it with the same wording `env pull` uses. Add a fixture export from each source under `tests/Keypaste.Core.Tests/fixtures/import/` — synthetic data only — and a script step in the compat gate that imports each and opens the result in real `keepassxc-cli`."
- **Owner** — none.
- **Falsifier** — import each of the four fixtures and run `scripts/verify-keepassxc-compat.sh` against the result. **If any fixture produces a vault `keepassxc-cli` cannot list, FAIL.** Then: a fixture row with a field the importer cannot map appears by name in the plan and in the entry's notes; `keypaste ls` after import matches the fixture's entry count; a TOTP seed from the 1Password fixture lands in `otp` and 9.2 renders a code from it; and no importer test reads a file outside the fixtures directory.
- Traces to `docs/PRODUCT.md` §1 item 1 and law 4.6.

### 9.2 — TOTP [ ]

- **Build** — "Store TOTP seeds the way KeePassXC does — the `otp` string attribute carrying an `otpauth://` URI — and render codes from it: `keypaste get <entry> --otp` prints the current code, the app shows it with its remaining seconds, and the MCP bridge gains `otp` as a fourth requestable field of `request_credential` that returns **a six-digit code and never the seed**, subject to the same approval, exposure and policy path as every other field. The code is computed in `Keypaste.Core` with the BCL's HMAC; no new package. A seed is a secret and is masked, copied and revealed under exactly the rules a password has."
- **Owner** — none.
- **Falsifier** — add a seed in KeePassXC, open the vault in keypaste and compare the code to KeePassXC's at the same second. **If they differ, FAIL.** Then: an agent's `request_credential` for field `otp` is audited with `field: otp` and the reply contains six digits and no `otpauth://` string; the seed appears nowhere in the app's visual tree while a code is shown (4.6's sweep); and the secret-hygiene tests list `otp` alongside `password`.
- Traces to `docs/PRODUCT.md` §1 items 1 and 2, law 3.2 and law 4.6.

### 9.3 — SSH agent [ ]

- **Build** — "Implement `keypaste ssh` as an SSH agent: it serves private keys stored as KDBX attachments (KeePassXC's convention, so a key added in KeePassXC works) over the agent protocol — a Unix domain socket on macOS and Linux, the named pipe OpenSSH for Windows uses. Keys are decrypted into memory on unlock and never written to disk; signing happens in-process. Each key is served only while the vault is unlocked and the user has ticked it; `keypaste ssh ls` lists what is served. The agent never exposes a private key to a client — the protocol signs, it does not export — and a `request_credential` for an SSH key field is refused outright, because there is no field an agent should ever receive."
- **Owner** — none.
- **Falsifier** — with `keypaste ssh` running and one key ticked, run `ssh-add -L`. **If the key is absent, FAIL; if `ssh-add -x` or any client call returns private key bytes, FAIL.** Then: `find` over every temp directory before and after a signing operation shows no new file; locking the vault empties `ssh-add -L` within a second; and a key added in KeePassXC as an attachment is served without any keypaste-side edit.
- Traces to `docs/PRODUCT.md` §1 items 1 and 3, law 3.4.

---

## Tier 3 — sellable to teams

Zero-knowledge (the server stores only ciphertext, law 3.1), KDBX-or-nothing (a shared vault is still a real .kdbx that opens in KeePassXC), self-host stays first-class (§2), and this is **not** enterprise IAM — if it starts looking like Okta, stop and re-read §2. 7.1 and 7.2 are different products for different sensitivities and 7.1 must never silently become 7.2.

### 7.1 — Shared env sets, the copy model [ ]

- **Build** — "Extend the 5.2 zero-knowledge sync into shared env sets for a small team. A shared set is a normal KDBX file whose master key is wrapped per-member: each member has a keypair, and the set's key is sealed to every member's public key, so the relay stores the ciphertext blob plus opaque wrapped-key envelopes and can decrypt neither (law 3.1). Implement invite, list members, and remove-member — where remove MUST rotate the set's key, re-wrap to the remaining members, AND loudly instruct rotation of the actual secret values, because a removed member may have cached plaintext. Be honest in THREATS.md: this bounds FUTURE access, not past copies. Design first (DECISIONS.md: why per-member key-wrapping OUTSIDE the KDBX rather than inventing a format), then code."
- **Owner** — none.
- **Falsifier** — remove a member, then try to open the re-wrapped set with that member's old envelope. **If it decrypts, FAIL.** Then: a non-member's envelope never decrypts; the removal output names the values to rotate; and the shared file still opens in real `keepassxc-cli` with the set's master key (law 4.6).
- Traces to `docs/PRODUCT.md` §1 item 4, law 3.1 and law 4.6.

### 7.2 — Team broker, the access model [ ]

- **Build** — "Build the team broker: the way to share a high-sensitivity credential without giving anyone a copy. A member — or that member's agent — requests it from a shared approver (a self-hostable team relay, or a designated owner's running `keypaste agent`), and the EXISTING scoped-request + approval + TTL + audit machinery from Stage 2 decides and releases exactly one field for one use. This is `request_credential` with the requester on another machine: the secret lives in one place, revocation is instant at the broker with no rotation, and every release is attributed to a named teammate in the audit log. Reuse the Stage 2 approval core verbatim — no second security path (law 4.3). The relay is zero-knowledge about the vault: it brokers a request to the holder, it never holds the vault."
- **Owner** — none.
- **Falsifier** — revoke a member at the broker and have their agent request again within one second. **If anything is released, FAIL.** Then: an unauthorized member's request is denied and audited with their name; no key rotation happened, checked by the vault's byte hash; and the secret lands in exactly one response path, asserted the way `verify-approval-e2e.sh` does today.
- Traces to `docs/PRODUCT.md` §1 item 2, law 3.2 and law 4.3.

### 7.3 — Team identity and SSO, never on the vault path [ ]

- **Build** — "Add team accounts with SSO (OIDC) for the hosted service — and draw the invariant in blood: SSO authenticates who may PULL a wrapped-key envelope, reach the broker, or view the dashboard. It NEVER gates vault decryption and is NEVER on the secret path. Implement OIDC login, map members to their key-wrapping identity from 7.1/7.2, and SCIM (or a manual deprovision) that, on removing a person, AUTOMATICALLY triggers 7.1 re-wrap/rotation and 7.2 broker revocation — deprovisioning that leaves access behind is theatre."
- **Owner** — none.
- **Falsifier** — with a valid SSO session and no vault key, attempt to read any entry. **If a byte of plaintext comes back, FAIL.** Then: a deprovisioned user loses both pull and broker access and the rotation from 7.1 is triggered without a human step; and a broken IdP (wrong signing key) fails every login closed.
- Traces to `docs/PRODUCT.md` §2 (not enterprise IAM), law 3.1 and law 3.7.

### 6.1 — Delegation feasibility spike [ ]

- **Build** — "Run a strictly timeboxed spike (2 days of work max) answering: for a personal GitHub account and a personal Google account, what OAuth grants and authorized apps can be enumerated and revoked via API with user-level scopes only? Produce `docs/feasibility.md` with exact endpoints, scopes, and hard limits, and a recommendation: live aggregation, guided deep-link revocation flows, or hybrid. Do not write product code in this spike."
- **Owner** — none.
- **Falsifier** — open `docs/feasibility.md` and pick one endpoint it names. Call it with the scopes it lists. **If the response differs from what the page says, FAIL.** A page with no endpoints is FAIL.
- Traces to `docs/PRODUCT.md` §1 item 5.

### 6.2 — Delegation center [ ]

- **Build** — "Based on `docs/feasibility.md`, extend Agent Activity into the Delegation Center: unify keypaste agent grants, connected MCP clients, and (as feasible) external OAuth grants into one 'everything that can act as you' view with revoke and deep-link actions and staleness nudges ('unused for 60 days — revoke?'). The headline number from 4.3 grows to include the external grants, each source labelled."
- **Owner** — none.
- **Falsifier** — revoke a GitHub OAuth grant from the screen (or follow its deep link) and refresh. **If the grant is still listed as live, FAIL.** Then: an item unused for 61 days carries the nudge and one unused for 59 does not; and with the network off, the screen still shows the local sources and says the external ones are unreachable rather than empty.
- Traces to `docs/PRODUCT.md` §1 item 5.

### 7.4 — Team delegation dashboard [ ]

- **Build** — "Extend the Delegation Center from 'everything that can act as ME' to 'everything that can act as anyone on the TEAM': unify per-member agent grants, standing policy rules, shared-set membership (7.1) and live broker grants (7.2) into one view an owner reads at a glance and revokes from in one click. Screenshot-worthy or it isn't done (law 5.1)."
- **Owner** — none.
- **Falsifier** — from the dashboard, revoke one member's broker access, then have that member's agent request. **If it is served, FAIL.** Then: the view lists every 7.1 member and every live 7.2 grant, checked against the relay's own tables; and the screenshot for the launch post shows it with no real names in it.
- Traces to `docs/PRODUCT.md` §1 item 5 and law 5.1.

---

## Recurring

Not steps — they have no completion. Run them when the trigger in each one fires.

**M.1 — Security review.** "Act as a hostile security reviewer of the current codebase. Attempt to find: any path where a secret touches disk unencrypted, any place the master key or derived keys outlive their need in memory, any agent-facing response that could leak more than the single requested field, any injection via entry names/reasons, any failure path that fails open, and — from 5.2 — any byte of plaintext the relay could see. Report findings ranked by severity with concrete patches, and add regression tests for every fix. Do not soften findings."

**M.2 — Compatibility audit.** "Verify KDBX compatibility end-to-end against the latest KeePassXC release: round-trip every feature we use (groups, entries, custom fields, our env convention, the `otp` attribute, attachments) in both directions, including a vault created in KeePassXC and modified by keypaste and vice versa. Fix any drift. Update the CI compatibility matrix and note the tested KeePassXC version in the README."

**M.3 — Scope check.** "Read docs/PRODUCT.md sections 2 and 6, then review my last two weeks of commits and open branches. Flag anything that violates the walls (a new format, a server that can read a vault, browser-only consumer features, enterprise IAM creep) or that isn't attached to a step in docs/STEPS.md. Recommend what to cut, park in docs/IDEAS.md, or finish. Be blunt."

**M.4 — Docs and release.** "Prepare release vX.Y: update CHANGELOG.md from commits in human language, bump versions, verify install instructions on all three OSes actually work from scratch, refresh the demo GIF if any user-visible flow changed, tag, and draft the short release announcement."

---

## Definition of focused

You should always be able to answer: which tier am I in, which step am I on, and what would prove it done? If you cannot, open this file, take the first open step in the lowest open tier, and read its Falsifier first.
