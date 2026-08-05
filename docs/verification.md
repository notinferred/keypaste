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
| `scripts/verify-docs.sh` | this file and `docs/STEPS.md` agree, and no step grows into several | yes — the original seven watched to fire 2026-07-28, and all three branches of the step-size cap on 2026-08-06 |

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

## V-0007 — merge is entry-level, and loses nothing

**Falsifier, run first.** Build a vault holding one entry and note its value. Build a second file carrying the **same entry UUID** with a different value and an **older** `LastModificationTime`. Run `keypaste merge`. If the local value changed, the step is **FAIL** and you are finished — taking the incoming side because it is the incoming side is file-order precedence, not a merge, and no other result changes that.

**Then:**
1. Run the identical merge a second time. It must report no changes and must not produce two entries sharing one UUID. A merge that is not idempotent cannot be used for a re-sent snapshot, which is the only delivery model the product has.
2. Construct equal timestamps with differing content. The run must exit nonzero, name every conflicting entry, and leave the vault **byte-identical** — check the mtime and the hash, not just the output.
3. Merge a four-entry file into a forty-entry vault. All forty must survive. If any are gone, absence was read as deletion, and a subset was allowed to speak for the whole.
4. Take an entry that lost, and recover its superseded value from that entry's KDBX history. If the old value is unrecoverable, the merge destroyed data it reported having merged.
5. Rename an incoming entry's title but keep its UUID, and merge. It must update the existing entry, not add one. Then do the reverse — same title, different UUID — and confirm it adds rather than overwrites. This pair is what proves the match key is the UUID and not the name.
6. `bash scripts/verify-keepassxc-compat.sh` must still be green: a vault this command wrote is a vault law 4.6 covers.

**PASS** needs all six. **BLOCKED** if no `keepassxc-cli` is available — the compatibility half cannot be taken on trust.

---

## V-0008 — the bench can fail

**Falsifier, run first.** Take any task in `docs/ux.md` and try to write down a result that **fails** it. If you cannot — because the threshold is a word like "fast", a range, an unqualified success ratio, or absent — the step is **FAIL** and you are finished. A bench every possible outcome passes is the checklist item `docs/IDEAS.md` already rejected, wearing a framework's name.

**Then:**
1. `bash scripts/verify-ux.sh` is green, and you have watched at least one of its three assertions fire by breaking a task on purpose and putting it back.
2. Engagement and Retention are **struck on the page** with law 3.5 named as the reason. If they are merely absent, the document claims a completeness it does not have.
3. Every open step in `docs/STEPS.md` that touches a user-visible surface names at least one `T-NN`. An unnamed surface is a surface nobody agreed to measure.
4. Each task names the method that produces its number. "Time to complete" without saying who is timing what is not a method.
5. The five-participant ratio is explained on the page as a discovery heuristic and not as statistics.

**PASS** needs all five. Whether anyone has *run* a session is `[process]` and not checked here — an unrun threshold is D-0043's assertion about the world.

## V-0009 — the app draws, and something other than a person can see it

**Falsifier, run first.** Make the masked value control render its characters in plain text — one property — and run the app suite. If it stays green, the renders are not asserting what this step claims and the step is **FAIL**. Put it back, then do the same to the password field's mask. Both must go red, and red on the *secret-path* assertions specifically, not merely on some unrelated golden.

**Then:**
1. The suite runs with **no display**: no X server, no `DISPLAY`, no virtual framebuffer. If it needs one, O-0020 is not closed, it has moved.
2. Goldens are compared on Linux only; macOS and Windows run the same renders and assert structurally. A golden diffed across font stacks fails for reasons nobody caused, and a test like that gets disabled within a month.
3. A deliberately broken render writes its actual output to the CI artifacts. A pixel diff you cannot look at is a failure you will resolve by deleting the test.
4. A locked window renders no entry titles. Lock, re-render, assert on the bitmap.
5. `docs/desktop.md` has struck the checklist items this covers and says which of the twenty-one still need a human. If it still lists all twenty-one, either nothing was covered or nobody updated it, and both matter.

**PASS** needs all five and the tolerance for anti-aliasing stated as a number in the test, not chosen per-image until things pass.

## V-0010 — the host holds nothing, and says no when it cannot ask

**Falsifier, run first.** Stop every `keypaste agent` on the machine. Drive the extension's credential request. **If a password prompt appears anywhere — browser, host, terminal — the step is FAIL**, and it is the most serious failure in this file: any program on the machine can draw a convincing prompt, and the whole architecture rests on nothing in the agent path being able to make a real one appear. A hang is also FAIL; the extension must say no agent is running and name the command that starts one.

**Then:**
1. `git grep -n 'KeePassLib' src/` returns nothing under the host or the extension. The interop boundary is D-0007's rule, and a second file touching the library ends it.
2. `keypaste browser install` writes the manifest to the documented per-OS path with the extension ID pinned; `keypaste browser uninstall` removes exactly that and leaves any manifest it did not write alone. Plant a foreign manifest beside it and confirm it survives.
3. One extension build loads in both Chrome and Firefox. Two builds is two extensions, and two extensions drift.
4. Malformed framing — a truncated length prefix, a length larger than the payload, a payload that is not JSON — is refused and logged, never parsed optimistically.
5. THREATS.md names the store auto-update channel as a path that reaches users without a git tag, and says what a compromised extension can and cannot reach.

**PASS** needs all five. **BLOCKED** if no browser is installed to load into — a host with nothing on the other end has not been tested.

## V-0011 — one moment, three surfaces, no disagreement

**Falsifier, run first.** On each of the three surfaces in turn — terminal, desktop, extension popup — raise a request and then do nothing at all until the timeout expires. **Any surface resolving to anything other than deny is FAIL.** Then close the popup while a request is live: if that reads as a cancel rather than a denial, that is also **FAIL**. Run this before comparing a single pixel, because a surface that is beautiful and fails open is not a surface with a UX problem.

**Then:**
1. The fields, their order and their wording match `docs/ux.md` on all three. Diff them literally. A surface that adds a helpful line has added a field nobody specified.
2. The agent's reason is labelled as the agent's words on all three, and is defanged on all three: send a reason containing newlines, ANSI escapes and an RTL override, and confirm no surface can be made to draw a second prompt or reverse the entry name.
3. All three meet the same `T-NN` threshold from `docs/ux.md`. If one is twice as slow, that surface fails — the threshold belongs to the moment, not to the renderer.
4. Headless screenshots of the popup exist for both browsers and are compared the way 4.6 compares the app's.
5. The default is deny on all three with no agent running, no policy file, and a malformed policy file.

**PASS** needs all five on all three surfaces. Any behaviour that holds on one surface and not another is **FAIL** — law 4.3 forbids a second security path, and three renderings of one moment is exactly where a second one gets built by accident.

## V-0012 — the CLI's copy does not survive in Win+V

**Falsifier, run first.** On a real Windows machine with Clipboard History enabled (Settings → System → Clipboard), run `keypaste get` on an entry so the password reaches the clipboard. Press **Win+V** before the clear timeout expires, and again after it. **If the value appears in the history panel at either moment, the step is FAIL.** Then check the cloud clipboard on a second machine signed into the same account. This is the whole defect; if it still reproduces, nothing else in this list matters. A virtual machine is fine, a Wine or WSL clipboard is not — WSL does not go through the Windows clipboard the way the shipped binary does, and testing there proves nothing.

**Then:**
1. `GetClipboardFormatName` round-trips every format the code registers, asserted in a test. Read the names back and compare them to the literals. KeePassXC ships `"CanUploadToCloudClipboard "` with a trailing space in every released version, which registers a different and meaningless format that no review would catch — a test that only checks the value was set will pass against that bug.
2. All formats are set in **one** clipboard session. Instrument or read the call sequence: `OpenClipboard` once, `EmptyClipboard`, every `SetClipboardData`, then `CloseClipboard` once. A second session to add the markers means the history service was already notified, so the value leaked and the test still looks green.
3. The clear guard holds a hash, not the secret. `git grep` the clear path for a stored plaintext field that lives for the timeout window. Holding the value to compare against it re-creates in the CLI exactly what D-0046 avoided in the app.
4. The clear still refuses to wipe something the user copied afterwards. Copy the password, copy something else by hand, wait out the timeout, and confirm the hand-copied value survives.
5. macOS and Linux are untouched — the diff does not alter their clipboard path, and O-0019 is still open in `DECISIONS.md` rather than quietly marked done.

**PASS** needs the falsifier clean and all five. **BLOCKED** without a real Windows machine with clipboard history on — this cannot be checked anywhere else, and a green test suite on Linux is not evidence about a Windows clipboard.

## V-0013 — the pages do not claim more than the formats deliver

**Falsifier, run first.** Grep **all six** of `SECURITY.md`, `THREATS.md`, `launch.md`, `README.md`, `docs/demo.md` and `site/public/index.html` for `O-0008`, `clip.exe` and `clipboard history`. **Any sentence still saying the gap is open, unresolved, or CLI-only is FAIL**, as is any sentence calling the clipboard cleared, safe or private without naming what still reads it. Six files, not four: `THREATS.md` T-19 and `launch.md` both carried the claim and an earlier draft of this verifier could not see either, which would have produced a PASS over a page telling readers to turn Clipboard History off.

**Then:**
1. `SECURITY.md` says first-party Clipboard History and Cloud Clipboard are closed on **both** front ends. If it still describes a divergence between the app and the CLI, it is describing a version that no longer ships.
2. Every page naming the residual names the same one, in this order: third-party clipboard managers, which decide independently and mostly ignore the formats, then RDP or Citrix or VDI redirection, which hands the value to a peer machine keypaste cannot reach.
3. Each says the formats are a request to well-behaved consumers rather than an enforcement boundary. A reader who finishes the paragraph believing Windows *prevents* other programs reading the clipboard has been misled by a true sentence.
4. `THREATS.md` T-19 no longer recommends `keypaste get --show` or turning Clipboard History off as a mitigation for this, and its **Proved by** names the tests that actually hold the claim rather than only the two older `VerbTests`.
5. No page contradicts another. A corrected `SECURITY.md` beside a stale `launch.md` is worse than neither, because together they read as one of them being current and the reader cannot tell which.
6. Nothing claims the end-to-end result. The unit tests prove the formats are registered and set in one session; only `V-0012`'s falsifier proves a password is absent from Win+V. Until that has run, a page saying it has been checked on a real machine is asserting something nobody observed — which is the D-0043 failure exactly.

**PASS** needs the falsifier clean and all six. Unlike `V-0012` this is checkable anywhere: it is a reading task, not a Windows task.

---

## What this file does not do

It does not check that a verifier is any *good*. A falsifier that cannot fire passes every reading and proves nothing — `scripts/verify-docs.sh` can confirm a falsifier is present, never that it bites. Watching a new falsifier fire against the current tree, once, before trusting it, is `[process]` and belongs to whoever writes the verifier.
