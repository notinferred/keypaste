# verification.md — one cold-run verifier per open step

> **Read this before you read anything else, and do not read the build prompt.** A verifier gets this file, the repository, and nothing else. Not the Build lane of the step, not the transcript of whoever built it. Shared context is how a build and its check agree with each other while both are wrong.
>
> **Every verifier carries a falsifier: the specific thing to try that would prove the step is *not* done. Run the falsifier first.** If the falsifier fires, stop — the step is not done, and nothing else you find changes that.
>
> Results are **PASS**, **FAIL** or **BLOCKED**. "Looks right" is not a result. BLOCKED means you could not run the check, and it is not a pass.

---

## 1. The standing gates

These hold the steps that are already done. They are not re-derived here — each one is a script, and the script is the specification.

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
| `scripts/verify-docs.sh` | this file and `docs/STEPS.md` agree | yes — all seven assertions watched to fire, 2026-07-28 |

**The "observed failing" column is the point.** A check that has never been watched to fail is an assertion about the world, not a check on it (D-0043). Nine of these have never been recorded failing. Filling that column in is real work and it is not done.

---

## V-0001 — The demo GIF exists and both pages show it

**Falsifier, run first.** `ls -l docs/demo/keypaste-demo.gif`. If the file is absent, the step is **FAIL** and you are finished. If it is present but 2 MB or larger, the step is **FAIL** — the budget is not decoration, it is what keeps the README usable on a phone.

**Then:**
1. `git grep -n 'keypaste-demo.gif' README.md site/public/index.html` — both must reference it, and neither may still carry the reserving HTML comment in place of the image.
2. Open the GIF. It must show, in order: an agent asking, the approval dialog with a reason, a human answering, and the log afterwards. A GIF of a terminal scrolling is not the demo.
3. `bash scripts/verify-demo.sh` must still pass — the transcripts around the slot must not have moved when the image landed.

**PASS** only if the file exists, is under 2 MB, is referenced by both pages, shows the four beats, and `verify-demo.sh` is green.

---

## V-0006 — The name is actually held

**Falsifier, run first.** Open `https://github.com/keypaste` logged out. If it resolves to somebody else's account or organisation, the step is **FAIL** and the name question is bigger than a registration. Do the same for `npmjs.com/package/keypaste` and `crates.io/crates/keypaste`.

**Then:**
1. All three are held by this project, or there is a written decision in `DECISIONS.md` recording which were unavailable and what the product will be called on that registry instead.
2. `launch.md`'s canonical link matches whatever was registered. A launch post pointing at a personal account when an org exists is a link that ages badly.
3. The trademark check has an answer, even if the answer is "not worth filing".

**BLOCKED** if the registries are reachable but you cannot confirm ownership while logged out.

---

## V-0002 — The launch actually went out

**Falsifier, run first.** Open `launch.md` and read the "Before anything goes out" list. If any box there is unticked, the step is **FAIL** regardless of what was posted — the list exists because each item is something a stranger hits before they hit the product.

**Then:**
1. Every one of the five channels named in `launch.md` has a live URL, and each URL loads for a logged-out reader.
2. Every link inside each post resolves. If this repository is still private, every repository link is a 404 for the audience the post was written for, and that is **FAIL**, not a caveat.
3. The install command in each post matches what `README.md` currently documents.

**BLOCKED** is the right result if the posts exist but you cannot see them logged out.

---

## V-0003 — Two weeks of answering `[process]`

**Falsifier, run first.** Find the oldest issue or comment opened after the launch date with no reply from the maintainer. If one exists and is older than 48 hours, **FAIL**.

Not mechanizable: whether a reply was *useful* is a judgement. This verifier is a person's reading of the issue tracker, and it is second-class until something better exists.

---

## V-0004 — Agent Activity answers "what can act as me right now?"

**Falsifier, run first.** Start the app with no `keypaste agent` running and open Agent Activity. If the screen renders as though it were live — an empty feed presented as "no requests" rather than as "nothing is listening" — the step is **FAIL**. A screen that cannot tell "nobody asked" from "nothing is connected" is worse than no screen, because it reads as a safety claim.

**Then, with a real agent connected:**
1. Drive a `request_credential` from `scripts/` and watch the request appear in the feed *before* it is answered. A screen that only shows history is not this step.
2. Approve one from the app. The secret must reach the client, and the audit log must record the decision with the app named as the channel.
3. Deny one from the app. Nothing must reach the client.
4. Toggle revoke/pause on a client card, then request again from that client. It must be refused, and `keypaste policy ls` must show the deny-all rule the toggle wrote.
5. Kill the app mid-request. The request must fail closed.
6. Lock the vault. The feed must stop showing entry names.

**PASS** needs all six. Step 4 is the one that makes it a control panel rather than a log viewer.

---

## V-0005 — The approval prompt left the terminal without weakening

**Falsifier, run first.** Trigger a request with the native prompt on screen and do nothing until the timeout expires. If it resolves to anything other than **deny**, the step is **FAIL** — and check the same on the terminal channel, because the defect that matters is the two channels disagreeing.

**Then, for each of the two channels independently:**
1. Default is deny — dismissing the window is a denial, not a cancel.
2. Timeout is deny.
3. Every error path is deny. Kill the approver process mid-prompt and confirm the client is refused.
4. The reason string shown is the agent's, rendered as untrusted text. Send a reason containing newlines and terminal escapes and confirm neither channel is fooled into drawing a second prompt.
5. Headless still works: with no display, `keypaste agent` must still prompt in the terminal.

**PASS** needs all five on both channels. Any behaviour that holds on one channel but not the other is **FAIL**, because two security paths is exactly what law 4.3 forbids.

---

## What this file does not do

It does not check that a verifier is any *good*. A falsifier that cannot fire passes every reading and proves nothing — `scripts/verify-docs.sh` can confirm a falsifier is present, never that it bites. Watching a new falsifier fire against the current tree, once, before trusting it, is `[process]` and belongs to whoever writes the verifier.
