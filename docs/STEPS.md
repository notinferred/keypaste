# STEPS — the build

> **What this is.** Every step between here and the full product, grouped by area. Each open step
> carries a tier tag, a one-paragraph **Build** line an agent can run, and a **Verify** line: an id,
> what must hold, and the concrete observation that fails it. A verifier runs the Verify line cold —
> with this file, `docs/PRODUCT.md` and the repo, never the implementer's transcript. A check that
> cannot fail is not a verifier. Human-needed actions — a registration, an enrolment, a keystroke on
> a real machine — are ordinary steps, sequenced inline, keeping the `H-` id other files cite.
>
> **Tiers — three, and they are the product's plans seen from the build side** (D-0063; figures live
> only in the working file the Not-in-git table in `DECISIONS.md` names).
> - `[MVP]` — **the CLI launch: what a stranger can install and read about this month** (D-0079): the
>   public repository, a security contact that receives mail, the demo GIF, the launch and
>   its two weeks of answering. Nothing new is built
>   here; what exists is finished and shown, and the copy says "no released GUI" (D-0069).
> - `[Launch]` — **the app released, then the first dollar, then the Free plan finished**: signing,
>   notarization and the desktop app's own release and post; then the relay, hosted sync, share links
>   and Individual billing — nobody is charged before 4.7 and 5.2 have both shipped (D-0065) — with the
>   merge that sync needs, Agent Activity with the headline number and the native prompt; then browser
>   autofill, importers, TOTP, an SSH agent, the UX bench and the render gates.
> - `[Scale]` — **the Team plan**: shared env sets, the broker, SSO for the hosted service, the delegation
>   center, the team dashboard.
>
> Within an area, MVP rows come first, then Launch, then Scale. **Pick up the first unticked step of the
> lowest tier in the lowest area that has one, skipping any row marked BLOCKED.** A BLOCKED row is
> neither deferred nor done: it names what would unblock it, and "What exists today" below lists every
> one, so the rule cannot stall on a row no amount of work in this repository can close. Nothing is
> deferred out of existence: every step keeps its row, its verifier and its trace. A Launch or Scale
> step's Build line is a sentence today; it is expanded when its tier opens, and the long prompts this
> file used to carry are in git at `f27cb56`.
>
> **Admission rule.** A step exists only if it (1) has a Verify line that can *fail*, and (2) traces to a
> claim in `docs/PRODUCT.md`. Otherwise it is an Ideas row in `DECISIONS.md`, not a step.
>
> **This file is mutable; `docs/PRODUCT.md` §3 is not, and its other sections change only by a dated
> re-ratification** (v1.1, D-0061). Anything that would bend a law stops and becomes a `DECISIONS.md`
> ledger row for the founder.
>
> **Done means:** `dotnet build` and `dotnet test` green on `keypaste.slnx` and `keypaste.app.slnx` ·
> the step's own `scripts/verify-*.sh` green · the pages it touches still pass `scripts/verify-demo.sh`
> · the Verify line passes cold.
>
> **Invariants (every step, `docs/PRODUCT.md` §3):**
> 1. The master key never leaves the local process; the relay stores ciphertext it cannot read.
> 2. An agent gets one field, one TTL, after one human approval or one rule the human wrote; default deny.
> 3. Every agent access is logged locally, hash-chained, before the agent is answered.
> 4. No secret touches disk unencrypted by keypaste's doing; injection is into process memory.
> 5. No telemetry on secret content or entry names, ever.
> 6. Crypto is KDBX4 through vendored KeePassLib; never a line of our own.
> 7. Every error path on the bridge denies.
> 8. Any KDBX keypaste writes opens in real KeePassXC, tested in CI; `Keypaste.Core` is the only vault
>    logic and every front end is thin over it.
>
> **Standing checks** — each is a script and the script is the specification. A step is not done until
> the ones its change can break are green; every push to `main`, pull request and tag runs all of them.
> `verify-keepassxc-compat.sh` + `verify-keepassxc-writeback.sh` — a vault opens and edits in real
> `keepassxc-cli` on three OSes, *fails if* either direction breaks · `verify-run-injection.sh` — the
> child sees the value and no file was written · `verify-run-signals.sh` — SIGTERM reaches the child ·
> `verify-mcp-stdio.sh` — nothing but protocol on stdout, every call audited · `verify-approval-e2e.sh`
> — approved returns the secret, refused returns nothing, neither is logged · `verify-policy-e2e.sh` —
> a rule grants silently and can never widen · `verify-log-chain.sh` — tampering is detected, truncation
> reads as damage · `verify-aot-trim.sh` — no new trim diagnostic names `src/` · `verify-demo.sh` — the
> five pinned pages match what the binaries print · `verify-install.sh` — the README install block runs
> verbatim on a scratch `HOME`.

**What exists today:** a KDBX4 vault the CLI creates, reads and writes, which KeePassXC opens in both
directions; env sets and `keypaste run` injection; the MCP bridge with a separate `keypaste agent`
approver, a 45-second window, policy pre-approvals and a hash-chained audit log; `v0.1.0` published as
four native binaries on `dl.keypaste.com`; a desktop app that unlocks a vault, browses entries and edits
env sets, built from source and not released; the launch essay, the landing page, the launch copy.
The SDK the plan pins runs on the founder's machine (K.1), so every `dotnet` gate can be run locally before a push; the shell gates that need `clang`, Docker or a Linux runner still cannot.

**BLOCKED rows, and what would unblock each** — the pickup rule skips these, so they are listed here
rather than discovered one at a time: **1.5a** needs a second Windows machine or a VM where
`AllowClipboardHistory` is not forced to `0`; **3.5** needs the Apple Developer Program enrolment;
**3.6** needs Azure Trusted Signing, whose individual eligibility is a review that can refuse; **4.7**
needs 3.5 and 3.6 and is therefore blocked twice over. Only 1.5a is `[MVP]`, and it does not block the
launch — the other three moved to `[Launch]` with D-0079 for exactly this reason.

**The `H-` ids** are human actions — a registration, an enrolment, a keystroke on a real machine. Each
is carried inline by the step that owns it; the Owner Queue that once listed them separately was
deleted at `9d48bd7` and is not coming back. H-0002, H-0004, H-0008, H-0009, H-0012, H-0013 and H-0014
were decisions, answered by D-0054 to D-0060; H-0011 is the site's pre-deploy checklist in
`site/README.md`, run by hand before every deploy. H-0010 was never issued.

---

## A · Vault & format

- [x] **0.1 `[MVP]` — Repo scaffold.** Three projects over one core, warnings as errors, lock files, the
  three-OS CI. D-0001 to D-0006.
- [x] **0.2 `[MVP]` — KDBX round-trip.** Vendored KeePassLib 2.61 behind `KeePassInterop.cs`; the
  KeePassXC gate is permanent on three OSes. D-0007, D-0008.
- [x] **0.3 `[MVP]` — Core CLI verbs.** `init add get ls rm`; exit codes 0–5; one line per prompt on a
  pipe. D-0009 to D-0012.
- [ ] **1.4 `[Launch]` — Entry-level merge.** `Vault.Merge` in `Keypaste.Core` and `keypaste merge
  <other.kdbx>` over it: match on entry UUID only; newer `LastModificationTime` wins and the loser goes
  to KDBX history; equal timestamps with differing content is a conflict that names every entry, writes
  nothing and exits nonzero; absence is never a deletion; `--key` wires `KcpKeyFile` into
  `KeePassInterop` after the keyfile path is proved against real `keepassxc-cli`; `--dry-run`, `--yes`;
  `scripts/verify-merge.sh`; the merge policy recorded as a new ledger row, and O-0018 closed or narrowed.
  **Verify (V-merge):** an older incoming entry with the same UUID does not overwrite the newer local one;
  the same merge twice is a no-op with no duplicate UUIDs; an equal-timestamp conflict leaves the vault
  byte-identical; a four-entry file into a forty-entry vault leaves forty; the loser is in history;
  a renamed title with the same UUID updates and a new UUID with the same title adds; the compat gate
  stays green. *Fails if* the local value changed, or any of the six.
- [ ] **9.1 `[Launch]` — Importers.** `keypaste import <format> <file>` and the app's Import screen over
  one core importer each for 1Password (1PUX, CSV), Bitwarden (JSON), LastPass (CSV) and KeePassXC (CSV);
  unmapped fields land in notes by name; a plan before writing; synthetic fixtures under
  `tests/Keypaste.Core.Tests/fixtures/import/` fed to the compat gate. **Verify (V-import):** each
  fixture imports and the result opens in real `keepassxc-cli`; an unmappable field is named in the plan
  and present in notes; a 1Password TOTP seed lands in `otp`. *Fails if* any fixture's result cannot be
  listed by `keepassxc-cli`, or a field is dropped silently.
- [ ] **9.2 `[Launch]` — TOTP.** KeePassXC's `otp` attribute (`otpauth://`); `keypaste get --otp`; the
  app shows the code with seconds left; `request_credential` gains `otp` as a fourth field returning a
  six-digit code and never the seed, on the same approval path; BCL HMAC, no package. **Verify
  (V-totp):** the code equals KeePassXC's at the same second; an agent reply for `otp` holds six digits
  and no `otpauth://`; the seed is absent from the visual tree while a code shows. *Fails if* the codes
  differ or a seed reaches an agent or a screen.
- [ ] **9.4 `[Launch]` — Compatibility audit against the current KeePassXC.** Re-run both gate directions
  against the newest release, including `otp` and attachments; note the tested version in `README.md`.
  **Verify (V-compat-current):** the version the README names is the current KeePassXC release and both
  scripts pass against it. *Fails if* the README names an older release than the newest tag.

## B · Env variables & injection

- [x] **1.1 `[MVP]` — Env storage convention.** `env/<project>`, one entry per variable, editable in
  KeePassXC, gated both directions. D-0014.
- [x] **1.2 `[MVP]` — Import and inject.** `env pull` fail-closed; `run` merges, relays signals, never
  writes a file. D-0015, D-0016.
- [x] **1.3 `[MVP]` — Escape hatch.** `env export --dotenv`, single-quoted, loud. D-0018.
- [x] **1.5b `[MVP]` — The pages say what the clipboard formats close.** Six pages, one residual each.
  D-0056.
- [ ] **1.5a `[MVP]` — The Windows CLI clipboard, proved on a real machine.** The Win32 path is written
  and unit-tested (D-0056); what is left is the observation. On a Windows VM with default settings, copy
  a known string and confirm Win+V shows it (the control), then `keypaste get` an entry and open Win+V
  before and after the clear; check Cloud Clipboard on a second signed-in machine.
  `Clipboard.GetHistoryItemsAsync()` from PowerShell can script both halves. **Verify (V-winclip):** the
  control appears and the password never does, before or after the clear. *Fails if* the value is in the
  panel at either moment. **BLOCKED** on the founder's machine: policy sets `AllowClipboardHistory=0`,
  so an empty panel there proves nothing. Unblocked by a second Windows machine or a VM where the
  policy is unset. It does not block the launch: `launch.md`'s O-0008 box is ticked with this caveat
  named, and the pages already say what the formats do and do not close (1.5b).
- [ ] **9.3 `[Launch]` — SSH agent.** `keypaste ssh` serves private keys stored as KDBX attachments
  (KeePassXC's convention) over the agent protocol — a Unix socket, or OpenSSH for Windows's named pipe;
  keys decrypt into memory on unlock and never touch disk; only ticked keys are served and only while
  unlocked; a `request_credential` for a key is refused outright. **Verify (V-ssh):** `ssh-add -L` lists
  the ticked key; no client call returns private bytes; no temp file appears across a signing; locking
  empties the list within a second; a key added in KeePassXC is served unchanged. *Fails if* the key is
  absent, or any call returns private bytes.

## C · The agent bridge

- [x] **2.1 `[MVP]` — MCP server skeleton.** Two tools, official SDK, hand-written schemas. D-0019 to
  D-0022.
- [x] **2.2 `[MVP]` — Human approval flow.** `keypaste agent` as a separate process, 45 seconds, grants
  keyed on the connection. D-0023 to D-0027.
- [x] **2.3 `[MVP]` — Policy pre-approvals.** `~/.keypaste/policy.toml`, whole-or-nothing, evaluated
  after the exposure re-check. D-0028 to D-0030.
- [x] **2.4 `[MVP]` — Audit log and threat model.** Hash chain over raw bytes; `THREATS.md` T-1 to T-25.
  D-0031, D-0032.
- [x] **2.5 `[MVP]` — The 60-second demo.** `docs/demo.md`, held to the binaries by `verify-demo.sh`;
  Claude deliberately not in CI. D-0033 to D-0035.
- [ ] **4.4 `[Launch]` — The approval prompt leaves the terminal.** A native window or tray notification on
  the agent, with the terminal channel kept for headless use; both render the same fields in the same
  order with the agent's reason as untrusted text; default deny, timeout deny, every error path deny on
  both. **Verify (V-native-prompt):** with the native prompt on screen, doing nothing until timeout
  denies; dismissing denies; killing the approver mid-prompt refuses the client; a reason with newlines
  and terminal escapes draws no second prompt; with no display the terminal channel still prompts — all
  five on both channels. *Fails if* any behaviour holds on one channel and not the other.
- [ ] **8.2 `[Launch]` — The approval moment, one spec, four surfaces.** Written once in `docs/ux.md`
  (fields, order, wording, defaults, timeout) and rendered by the terminal, the native prompt, Agent
  Activity and the browser popup with no surface inventing a field. **Verify (V-approval-spec):** a
  reason containing a newline, `Approve? [y/N]` and a U+202E override reaches each surface unreversed
  and draws no second prompt; the four show the same fields in the same order as `docs/ux.md`. *Fails
  if* any surface differs from the list or reverses the text.

## D · The desktop app

- [x] **4.1 `[MVP]` — Shell and unlock.** Avalonia; the master password never enters a `TextBox`; idle
  lock on two clocks. D-0044.
- [x] **4.2 `[MVP]` — Entry and env screens.** Generator, shared clipboard rule, lost-write guard.
  D-0045 to D-0050.
- [ ] **4.7 `[Launch]` — The app is released.** `app.yml` already packages `keypaste-app` for `win-x64`,
  `osx-arm64` and `linux-x64` on a `v*` tag, holding every archive to `--version` equalling the tag
  and to a `--selftest` that creates a vault through `Keypaste.Core` and reads an entry back (D-0081);
  what remains is what turns those artifacts into a release: a signed installer on Windows (3.6), a
  notarized `.dmg` on macOS (3.5), an AppImage on Linux with the four runtime packages asserted on
  Debian 12, the R2 publish beside the CLI under the same immutability rule, the measured size on
  both pages (O-0016), the "unsigned" sentences rewritten, and O-0015 and O-0016 closed in
  `DECISIONS.md`. **Verify (V-app-release):** on a fresh Windows VM a browser-downloaded
  installer opens with no SmartScreen wall; on a fresh macOS the `.dmg` opens with no quarantine
  prompt; `--version` equals the tag; the page's size equals the archive's within 1 MB; the app opens a
  vault the CLI created; the checksum verifies. *Fails if* either OS shows a wall, or any of the rest.
  **BLOCKED** on 3.5 and 3.6, which are themselves blocked: neither developer account is held.
- [ ] **4.3 `[Launch]` — Agent Activity and the headline number.** A UI-client message kind on
  `ApproverProtocol` (subscribe, list-pending, answer) so the app is a client of the running agent
  (D-0054). At the top: **what can act as you right now** = live connections + unexpired grants +
  standing `[[allow]]` rules, each shown beside the sum (D-0066); with no agent listening the screen
  says *nothing is listening* and shows no number. Below: the live feed with Approve and Deny, history
  rendered by `AuditText` verbatim, per-client cards with a *pause* toggle that writes a deny-all rule.
  **Verify (V-agent-activity):** with no agent the screen says nothing is listening and renders no
  zero; a request appears before it is answered; approve from the app releases and the log names the
  app; deny releases nothing; pause writes a rule `keypaste policy ls` shows and the next request is
  refused; killing the app mid-request fails closed; locking clears entry names; a 60-second grant moves
  the number up and, at expiry, down without a refresh. *Fails if* an empty feed reads as "no requests",
  or any of the eight.
- [ ] **4.5 `[Launch]` — The UX bench.** `docs/ux.md`: HEART with Engagement and Retention struck on the
  page (law 3.5); every task `T-NN` with the surface, the exact words, a numeric threshold out of five
  and a method — first unlock, copy, inject, approve, deny, find out why, fill a login;
  `scripts/verify-ux.sh` asserting each task has a number and a method and every surface step names a
  `T-NN`. **Verify (V-ux-bench):** no threshold is a word or a range; the script fails on a blank
  method; one stranger held to `T-01` with a stopwatch has a recorded PASS or FAIL. *Fails if* a task
  lacks a number, or no human has been measured (that is BLOCKED, never PASS).
- [ ] **4.6 `[Launch]` — The app draws in CI.** `Avalonia.Headless` with Skia in `Keypaste.App.Tests`,
  goldens on Linux under `tests/Keypaste.App.Tests/golden/`, structural asserts on macOS and Windows;
  secret-path asserts first — the password field draws dots, a masked value draws dots until held and
  returns within a frame, a locked window draws no title; then both themes, the countdown, Agent
  Activity in both states, narrow and wide; failing renders uploaded as artifacts; `docs/desktop.md`
  strikes what this covers (closes O-0020). **Verify (V-render):** making `MaskedInput` draw the typed
  character turns the suite red; the locked golden has no glyph run; a diff over tolerance fails the
  job with the render attached. *Fails if* the character swap stays green.

## E · The browser

- [ ] **8.1 `[Launch]` — Native messaging host.** `keypaste-browser-host`: Chrome's length-prefixed
  JSON over stdio, one extension zip that loads in Chrome and Firefox MV3; holds no vault, relays to the
  running agent (D-0023 preserved); `keypaste browser install|uninstall` writes and removes exactly the
  per-OS manifest; no agent means a sentence and the command to start one, never a password field;
  `THREATS.md` gains the store auto-update surface. Owner: Chrome Web Store and AMO registration, since
  the manifest pins the extension id. **Verify (V-host):** with no agent, nothing resembling a password
  field appears; the host binary has no KeePassLib type name in `strings`; uninstall leaves the
  manifest locations byte-identical to before install; the same zip loads in both browsers; a request
  lands in `keypaste log` under the pinned label. *Fails if* a prompt appears without an agent, or any
  of the rest.
- [ ] **8.3 `[Launch]` — Autofill.** KeePassXC-parity and no further: registrable-domain match through
  the Public Suffix List, never a URL substring; Confirm Access first, Remember unchecked; fill the
  fields directly and never the clipboard; no capture on submit, no vault writes from the extension;
  cross-origin iframes refused; the phishing cases as tests before the happy path; the README comparison
  table updated. **Verify (V-autofill):** with an entry for `https://paypal.com`, neither
  `https://evil.example/paypal.com/login` nor `https://paypal.com.evil.example/` is offered a fill; a
  cross-origin iframe gets none; Confirm Access precedes the first fill; a clipboard sentinel survives a
  fill; the phishing tests were watched failing against a substring matcher. *Fails if* either phishing
  URL fills.

## F · Sync, relay & billing

- [ ] **5.2 `[Launch]` — The relay binary.** `src/Keypaste.Relay`, one NativeAOT binary with the same gates
  and lock files as the others (D-0064), over S3-compatible storage and SQLite for accounts and licence
  keys; per-device keys; endpoints put-blob, get-blob with ETag, list-versions, one-download bundle;
  stores ciphertext and metadata only — a test greps the project for every vault type and fails on one;
  `scripts/verify-relay.sh` against a temp directory; the hosted instance is this binary on H-0019's VM
  and the self-hosted one needs no Stripe configuration. Owner: **H-0019** (VM, bucket, `sync.keypaste.com`).
  **Verify (V-relay):** after a push from a vault holding a sentinel, the storage dump and the SQLite
  contain no sentinel, master password or entry title; `strings` over the binary finds no KeePassLib
  type; the script passes against the published binary in `release.yml`. *Fails if* a plaintext byte is
  in either store.
- [ ] **5.3 `[Launch]` — Sync in the clients.** `keypaste sync` and the app's Sync screen: pull, `Vault.Merge`
  (1.4), push with the ETag so a lost write is a refused write; conflicts surface as 1.4 defines them.
  **Verify (V-sync):** two machines editing different entries converge to one vault with both; the same
  entry edited on both with equal timestamps refuses to push and names it; a push with a stale ETag is
  refused and nothing is overwritten. *Fails if* a write is lost or a stale push lands.
- [ ] **5.4 `[Launch]` — Share links.** `keypaste share <entry|env/project>` writes a real KDBX4 bundle of
  only that subtree with UUIDs preserved, a fresh 32-byte keyfile and no password, uploads it, and
  prints a link whose fragment carries the keyfile; `keepassxc --keyfile` opens the download; an imported
  bundle lands in a quarantine group, never under `env/`, until moved (answers O-0021). **Verify
  (V-share):** the bundle contains no title from outside the subtree; the second fetch is 404; the
  import sits in quarantine and `list_entry_names` does not list it until moved. *Fails if* a
  neighbouring entry is in the bundle or the second fetch succeeds.
- [ ] **5.5 `[Launch]` — Individual billing.** Stripe Checkout creates an Individual subscription, the
  webhook issues a licence key, the relay checks it on push; **nothing in any client is gated** and a
  relay with no Stripe configuration accepts any device key (the self-hosted path). Owner: **H-0018**.
  **Verify (V-billing):** removing a key server-side refuses the next push with a message naming the
  plan while `keypaste ls`, `get`, `run` and every app screen behave exactly as before; the
  unconfigured relay accepts a push. *Fails if* a client changes behaviour on licence state.
- [ ] **5.6 `[Launch]` — Double opt-in.** The relay sends the confirmation mail the thanks page promises;
  no list message goes to an unconfirmed address; `schema.sql` gains the confirmed flag the Worker
  cannot read. **Verify (V-optin):** a signup that never clicked receives no list message; a confirmed
  one does. *Fails if* an unconfirmed address is mailed.
- [ ] **5.7 `[Launch]` — Sync and relay docs.** `docs/sync.md` for a user and `docs/relay.md` for a
  self-hoster, saying what the operator can see (blob sizes, timestamps, device keys, emails) and cannot
  (anything inside a vault); `THREATS.md` gains the relay as a trusted party. **Verify (V-relay-docs):**
  every claim on both pages names the control or test that holds it. *Fails if* a claim has none.
- [ ] **5.8 `[Launch]` — The plans page.** `keypaste.com/plans`: Free, Individual, Team with what each
  gates, from the working file's figures; no cell names a security property or a signature (law 5.4).
  **Verify (V-plans-page):** every gated cell corresponds to a relay-enforced check and the Free column
  lists every local feature. *Fails if* a cell gates something the client enforces or the free binary
  lacks.

## G · Teams & delegation

> Ordered by dependency rather than by number: teams first (7.1-7.3), then the delegation spike and
> centre (6.1, 6.2), then 7.4, which is the team view of what 6.2 builds. Every row is `[Scale]`, so
> the pickup rule is unaffected either way.

- [ ] **7.1 `[Scale]` — Shared env sets, the copy model.** A shared set is a KDBX whose key is wrapped
  per member; the relay holds the blob and opaque envelopes; remove-member rotates, re-wraps and tells
  the owner to rotate values; `THREATS.md` says it bounds future access, not past copies. **Verify
  (V-shared-sets):** a removed member's old envelope no longer decrypts; a non-member's never did; the
  file still opens in `keepassxc-cli`. *Fails if* the old envelope decrypts.
- [ ] **7.2 `[Scale]` — The team broker, the access model.** A member's agent requests from a shared
  approver and the Stage 2 machinery releases one field for one use; instant revocation, no rotation;
  every release attributed to a named teammate; the relay never holds the vault. **Verify (V-broker):**
  a revoked member's request one second later releases nothing; the vault's hash did not change; the
  secret lands in exactly one response path. *Fails if* anything is released after revocation.
- [ ] **7.3 `[Scale]` — Team identity and SSO, never on the vault path.** OIDC for the hosted service
  gates pulling envelopes, reaching the broker and viewing the dashboard — never decryption; SCIM or a
  manual deprovision triggers 7.1 rotation and 7.2 revocation. **Verify (V-sso):** an SSO session with
  no vault key reads no plaintext; a deprovisioned user loses pull and broker access with rotation
  triggered; a broken IdP fails every login closed. *Fails if* a byte of plaintext follows an SSO
  session alone.
- [ ] **6.1 `[Scale]` — Delegation feasibility spike.** Two days: what OAuth grants a personal GitHub and
  Google account can enumerate and revoke with user-level scopes; `docs/feasibility.md` with endpoints,
  scopes, limits and a recommendation. **Verify (V-feasibility):** one named endpoint called with the
  listed scopes answers as the page says. *Fails if* it differs or the page names no endpoint.
- [ ] **6.2 `[Scale]` — Delegation center.** Agent Activity grows to external OAuth grants with revoke
  and deep links and staleness nudges; the headline number gains labelled external sources. **Verify
  (V-delegation):** a grant revoked from the screen is gone on refresh; 61 days unused carries the
  nudge and 59 does not; offline, local sources render and external ones say unreachable. *Fails if* a
  revoked grant still shows live.
- [ ] **7.4 `[Scale]` — Team delegation dashboard.** Everything that can act as anyone on the team, one
  view, one-click revoke. **Verify (V-team-dashboard):** revoking a member's broker access from it
  refuses that member's next request; every 7.1 member and 7.2 grant matches the relay's tables. *Fails
  if* a revoked member is served.

## H · Release, signing & distribution

- [x] **3.4 `[MVP]` — Release pipeline and install one-liners.** Four NativeAOT binaries on
  `dl.keypaste.com`, gates run against the published artifact, immutable versions. D-0040 to D-0043.
- [ ] **3.5 `[Launch]` — macOS notarization (H-0015).** Enrol in the Apple Developer Program and add the
  notarization step to `release.yml` and `app.yml`; the free binary is the notarized one (D-0057).
  **Verify (V-notarize):** `spctl --assess` accepts a browser-downloaded binary on a fresh macOS with no
  quarantine prompt. *Fails if* Gatekeeper refuses it.
  **BLOCKED**: the Apple Developer Program enrolment is not held. Unblocked by paying for it.
- [ ] **3.6 `[Launch]` — Windows signing (H-0017).** Enrol in Azure Trusted Signing, re-verifying price and
  eligibility, and sign the CLI and the app installer in the workflows (D-0070). **Verify (V-sign-win):**
  `Get-AuthenticodeSignature` reports Valid on a downloaded binary; the installer opens on a fresh VM
  with no SmartScreen wall. *Fails if* either.
  **BLOCKED**: Azure Trusted Signing is not held, and individual eligibility is a review that can refuse.
- [ ] **3.7 `[Launch]` — Package managers.** A Homebrew tap, a Scoop bucket and a winget manifest,
  published from `release.yml` (closes O-0011). **Verify (V-packages):** `brew install`, `scoop
  install` and `winget install` each put the tagged version on `PATH` in a fresh shell. *Fails if* one
  installs an older version or none.
- [ ] **3.8 `[Scale]` — Provenance.** `actions/attest-build-provenance` on every asset and a documented
  `gh attestation verify` line (narrows O-0012; reproducibility stays unclaimed). **Verify
  (V-provenance):** the documented command verifies a downloaded asset. *Fails if* it does not.
- [ ] **3.9 `[Scale]` — More targets on evidence.** `linux-musl-x64`, `win-arm64` and `osx-x64` added
  only when the R2 download log shows a request for them (O-0013). **Verify (V-targets):** each added
  RID passes the whole gate suite on its own native runner. *Fails if* a RID ships that no gate executed.

## I · Site, docs & launch

- [x] **3.2b `[MVP]` — Launch essay.** `docs/keepass-and-agents.md`, held to the binaries. D-0038.
- [ ] **3.0 `[MVP]` — The repository goes public (H-0003).** **10.1 first** — that row is the hostile
  review of the very tree this step publishes, and publishing is the one action here that cannot be
  undone. Then scan `9d48bd7..HEAD` — a revision range, never a commit count, which was already wrong by
  thirty-five when it was last written down — for anything `.gitignore`'s "Never commit these" block
  names: `*.kdbx`, `*.key`, `*.keyx`, `.env`, `.env.*`, `*.pem`, `*.pfx`, `secrets.json`, `.keypaste/`,
  `*businessnotes*.md`; and for the credentials the Infrastructure table in `DECISIONS.md` names — the
  `keypaste_signup_writer` role password, Stripe, Cloudflare, Apple and Azure material. The patterns are
  derived from those two committed lists rather than recalled, because the first scan's grep was never
  recorded anywhere and cannot be recovered; deriving them means the next scan reconstructs itself.
  Then have GitHub Support purge `refs/pull/*` and gc — the route that keeps the repository, its URL and
  its runner verification — and only if they refuse, push the clean history to a fresh repository and
  delete this one, which pays again what D-0082 measured. Then Settings → Change visibility. Measured
  2026-09-05: `refs/pull/11/head` (`e972225`) is still served and `git ls-remote` returns twenty pull
  refs in all, so V-public fails today. **Verify
  (V-public):** `git ls-remote origin 'refs/pull/*'` returns nothing and `refs/pull/11/head`
  (`e972225`) is unreachable; a logged-out browser opens the repository and `docs/demo.md`. *Fails if* a
  pull ref with the pre-rewrite identity is still served.
- [ ] **3.1 `[MVP]` — The demo GIF (H-0005).** Record the flow on `docs/demo.md` with any screen
  recorder, crop it to under 2 MB, save it as `docs/demo/keypaste-demo.gif`, and fill the slot both
  pages reserve. **Verify (V-gif):** the file exists under 2 MB; both pages reference it and neither
  carries the reserving comment; the GIF shows an agent asking, the dialog with a reason, a human
  answering, and the log. *Fails if* absent, 2 MB or over, or missing a beat.
- [ ] **3.2 `[MVP]` — The launch posts (H-0006).** Post `launch.md`'s copy to r/KeePass and the MCP
  community, wait 48 hours, then r/selfhosted, Show HN, X. **Verify (V-launch):** every box in
  `launch.md`'s "Before anything goes out" is ticked first; each post has a live URL logged out; every
  link in every post resolves; each post says "no released GUI". *Fails if* a box is unticked or a link
  404s.
- [ ] **3.3 `[MVP]` — Two weeks of answering (H-0007).** Every issue and comment answered for fourteen
  days; security reports moved to `security@keypaste.com` at once. **Verify (V-answering):** the oldest
  unanswered issue opened after launch day is under 48 hours old. *Fails if* one is older.
- [ ] **3.10 `[Launch]` — Product docs for the password manager.** Install, import, autofill, TOTP, SSH,
  sync, what the agent can and cannot do, deletion of an account. **Verify (V-docs):** every app screen
  links to its page and every page's commands run as written. *Fails if* a screen has no page or a
  command fails.
- [ ] **3.11 `[Launch]` — The app's launch post.** One post per channel when 4.7 and 5.x are shipped,
  written the way `launch.md` is: preconditions first, copy that concedes the incumbents by name.
  **Verify (V-app-launch):** every claim in the post names the step or gate that holds it. *Fails if*
  a claim has none.

## J · Security

- [x] **2.4a `[MVP]` — `SECURITY.md` and `THREATS.md`.** A private contact, the scope, T-1 to T-25 with
  a named residual each. D-0031, D-0032.
- [ ] **2.4b `[MVP]` — The security contact works (H-0021).** `SECURITY.md` names
  `security@keypaste.com` as the only channel an outside reporter can reach while the repository is
  private, and nothing has ever tested that the address delivers. Send to it from an address outside
  the Cloudflare Email Routing rule and read what arrives; once 3.0 lands, switch on GitHub's private
  vulnerability reporting as the second channel and strike the sentence in `SECURITY.md` that says it
  is unreachable. **Verify (V-security-contact):** a message sent from an outside address arrives in
  the destination mailbox, headers intact, within an hour; after 3.0, the repository's Security tab
  offers private vulnerability reporting to a logged-out-then-logged-in stranger. *Fails if* the mail
  bounces, silently disappears, or `SECURITY.md` still calls the second channel unreachable after 3.0.
- [x] **10.1 `[MVP]` — Hostile review before the repository goes public.** Done 2026-09-05, D-0084.
  Three findings, each two commits — the test alone with its failing output in the body, then the fix.
  **Untrusted names reached every renderer but four unsanitized**: `keypaste ls`, `env ls`, the
  `env pull` rejection message and the whole app display layer now draw through `EntryNameSanitizer`,
  and a listing says when what it drew is not what the vault holds; what addresses an entry, seeds an
  edit or reaches the clipboard stays exact, because sanitizing is lossy. The payload is a bidi
  override rather than an ANSI escape: a KDBX title is stored in XML and U+001B is not legal there, so
  a control character cannot survive the round trip — measured, and it corrected the finding.
  **An exception other than cancellation escaped both MCP tools before the audit append**, so an
  access could happen with no record (law 3.3); nothing was released, so it failed silently rather
  than open. Both catches are total now, as are the two approver-side filters that let the same
  exceptions past, and a peer can no longer end the approver by failing its accept. The lone-surrogate
  route an earlier pass proposed was spiked and falsified — `Utf8JsonWriter` does not throw on one and
  `JsonDocument.Parse` refuses it at the wire — so it is written up as I/O, not as remote input.
  **The approval prompt discarded `WasAltered`** and now says when the name or reason drawn is not the
  stored one, conditionally, so `verify-demo.sh`'s pinned dialog is byte-identical.
  Eight of the eleven gates were run locally and pass, including both KeePassXC directions against
  2.7.10 and `verify-demo.sh`; `verify-aot-trim`, `verify-run-signals` and `verify-install` need CI.
  `THREATS.md` T-1, T-6 and T-14 are rewritten in place — T-14 now says that the approver writes no
  audit line of its own (D-0020), so a release to a pipe peer that is not `keypaste-mcp` is recorded
  nowhere. **Verify
  (V-review):** every finding has a test that was red before its patch; the review is dated in
  `DECISIONS.md`. *Fails if* a finding has no red-then-green test.
- [ ] **10.2 `[Scale]` — External pen test.** A paid test of the relay and the bridge, the report
  summarised on the trust page. **Verify (V-pentest):** a report exists and every finding is closed or
  accepted in writing. *Fails if* one is neither.

## K · Development environment & CI

- [x] **K.1 `[MVP]` — The SDK on the founder's machine (H-0016).** .NET SDK 10.0.302, the exact version
  `global.json` pins (D-0076), installed machine-wide on 2026-09-04; `dotnet --version` in the repo prints
  `10.0.302` and `dotnet test keypaste.slnx` passes 1,010 tests with 2 skipped.
- [ ] **K.4 `[Launch]` — Branch protection.** Once 3.0 lands, require the `ci` checks and the three
  `keepassxc compat` checks on `main` (D-0008 names this as the part outside the repository). **Verify
  (V-protection):** a pull request with a red `compat` check cannot be merged in the UI. *Fails if* it
  can.
- [ ] **K.5 `[Launch]` — Fork pull requests on Blacksmith runners.** An answer for pull requests from
  forks, which have no access to the runner labels. **Verify (V-fork-ci):** a pull request from a fork
  runs `ci.yml` to completion. *Fails if* it stays queued.

