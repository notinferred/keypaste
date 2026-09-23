# Build plan

This plan owns committed work, recommended order, dependencies and acceptance evidence for [PRODUCT](PRODUCT.md) v1.7 (2026-09-19, D-0243–D-0245). [FEATURES](FEATURES.md) owns the capability inventory, [RELEASE](RELEASE.md) distribution evidence, and [BACKLOG](BACKLOG.md) optional work and the [step records](steps/README.md) what each completed task did. The license remains AGPL-3.0.

This file holds open work only. Finishing a task removes it from here, adds its record and evidence row under [steps](steps/README.md), and details the next task. No row is authorized by appearing here: the founder selects what is built, and on an instruction to build the next task the first ready code row in track order is taken. No backlog item is automatically eligible.

## Current status

The public release is CLI/MCP `0.3.0`; [RELEASE](RELEASE.md) owns what can be installed and [FEATURES](FEATURES.md) owns what works in source. The desktop runs from source with internal Windows and Linux packages and no public release. The desktop, `keypaste agent` and `keypaste run` unlock separately: locking the app leaves the terminal approver running with its own snapshot, the Agent Activity screen only probes for a listener, and env cards copy a command rather than launch through the app. Closing those gaps is T2–T4; sharing, a relay and hosted services are outside the committed tracks.

## Product tracks

Track order follows the finished product: a usable password manager, a consistent lock boundary, the two integrations that depend on it, then verified distribution. Existing components are reused; completed evidence retains its original scope. Readiness can allow independent work across tracks when selected, but does not waive release checks.

| Order | Track | Finish condition | Existing foundation / remaining work |
|---|---|---|---|
| T1 | Everyday vault use and recovery | A person opens or creates a supported vault, organizes and uses credentials, restores an edit or deletion, recovers a backup and changes vault access settings from the app. | Creation, editing, history, generators, deletion recovery, whole-file backups, their restore, an encrypted export, the organize controls with search and keyfile and access settings exist; the current-password reveal remains. |
| T2 | One shared unlock session | One user-initiated unlock supplies current vault state; lock, timeout and shutdown stop new releases across connected surfaces. | Desktop session and core gates exist separately; ownership, lifecycle, freshness and terminal-mode conflicts are unimplemented. |
| T3 | AI requests in the app | A configured MCP client produces a real native request, approval/denial and a matching audit record; a locked app refuses access. | CLI setup, bridge, terminal approvals, policies and audit exist; desktop connection, approval, lifecycle and actual request status remain. |
| T4 | Project environments | Import/edit an env set and explicitly run an app or terminal with it through the shared session, without plaintext files. | Env storage/import and standalone injection exist; app import/launch, project mapping and session-connected execution remain. |
| T5 | Desktop delivery | A new user installs the public Windows/Linux desktop, completes the combined journey, upgrades without losing data and can report a problem. | Internal packages and publication plumbing exist; product readiness, signing identity, public installation and updated guidance remain. |

## Selection and evidence

Only the next five tasks are detailed. Later rows name a bounded outcome and the dependencies their own implementation or verifier needs; expand a selected later task before building it. Tasks are not marked implemented from a document, reader, mock response or consuming screen. Name the producer, transport, consumer and user action exercised, and retain the source/version and limitations of the observation.

Needs are build dependencies. Ships after names publication gates. External signing identity is an input, not a queue of enrollment code. A ready row does not authorize publication, account changes or messages. Preserve the secret-path tests, real KeePassXC compatibility, stale-write refusals and release integrity checks while changing product scope.

## T1 — Everyday vault use and recovery

Recovery comes before broader feature depth. Completed T1 steps and their evidence are in [steps](steps/README.md). These tasks are the next detailed implementation candidates, and none is selected. Each uses the existing shared core and traces to PRODUCT §§1, 2 and 5.7.

- [ ] **V.10 — Complete current-password use and accessible input.** Needs: 4.9.
  **Build:** the entry pane reveals the selected entry's current password while its control is held, as history and env values already do, with copy and clear behaving the same on every secret surface and every secret input carrying an accessible name that does not vary with its content. More generator recipes remain optional.
  **Verify (V-V.10):** hold and release on the rendered control shows and removes the value, and lock, navigation and losing the pointer end it. The window's automation surface while the value is shown equals the surface at rest and carries none of its characters. Copy and clear are exercised on each secret surface against the real clipboard seam. A view-model flag without the drawn control does not pass.
- [ ] **F.15 — Make Open work from the locked screen.** Needs: 4.8.
  **Build:** `Ctrl/Cmd+O` on the unlock screen invokes the vault picker. `App.OnShortcut` returns when the shell is null, so the locked screen has no binding at all today. A path the picker returns goes through the same offer the Browse button uses, including the restore-only path a missing or damaged vault takes, and the guide describes whatever is true afterwards rather than the current limitation.
  **Verify (V-F.15):** a key event delivered to the locked window reaches the picker and lands in the vault it returns; cancelling leaves the screen, the recent list and every file untouched; the desktop guide's description of the binding matches the code. A view model holding a command nothing invokes does not pass.
- [ ] **4.6 — Verify secret drawing through the app.** Needs: V.10, V.2b, V.3b, V.4b.
  **Build:** drive the secret-entry, reveal and recovery controls in a real visual tree and read what was drawn, rather than what a view model holds: the masked field, the held reveal on an env value, a history revision and the entry pane, each through its own interaction and each through a lock. Extend the existing automation-surface check to every control that draws a secret, so the window's accessibility surface while a value is shown equals the surface at rest.
  **Verify (V-4.6):** a rendered frame holding a revealed value contains its characters and the same frame after the hold ends does not; a masked field's frame never does, at any length. The automation surface carries none of them in either state. Locking mid-reveal removes the value from the next frame. A view-model flag, a screenshot reader that never rendered, or an assertion about a property rather than a frame does not pass.
- [ ] **9.4 — Record supported vault workflows and compatibility.** Needs: V.1b, V.3b, V.4b, V.5b, V.10.
  **Build:** one gate that drives create, open, edit, organize, delete and recover, backup and restore, and change-access through the shipped CLI and the app on vaults KeePassXC made, including a keyfile vault, a keyfile-only vault, an AES-KDF vault and one carrying attachments, custom fields and custom data keypaste does not model. FEATURES then states the supported subset, what each workflow preserves and each refused variant, from that run alone. No full KeePassXC parity claim (D-0292).
  **Verify (V-9.4):** each workflow's result opens in KeePassXC with its unmodelled data intact and each refused variant leaves the file byte-identical; a FEATURES claim with no step in the gate, or a fixture keypaste made, does not pass.

## T2 — One shared unlock session

These rows replace the future terminal-owned approval architecture in D-0054. They do not assert that today's app locks the separate terminal agent. The process choice is made by U.1; the observable lock and ownership contract is already required by PRODUCT §2.

- [ ] **U.1 — Establish one vault session authority.** Needs: 4.1, 2.2.
  **Build:** choose the process that owns the unlocked vault in the desktop journey and record the choice as a decision, then implement its authenticated local attachment. The vault-free `keypaste-mcp` bridge reaches the owner only over a per-user endpoint that names the vault and session on every operation, and no master password or key crosses it. A second owner of the same vault, and a terminal `keypaste agent` and the desktop both claiming it, are refused by name rather than resolved silently. Standalone CLI verbs keep an explicit unlock of their own outside the desktop journey rather than a hidden second session. Traces to PRODUCT §2.
  **Verify (V-U.1):** a real `keypaste-mcp` request against the unlocked desktop reaches the owning process, is answered from its vault and names the session it reached. The same request with the app locked, with a stale session identifier and from a client presenting no valid attachment is refused. Starting `keypaste agent` on the vault the app owns, and a second app instance on it, are each refused with a message naming the owner, and neither opens the vault. A capture of the attachment traffic carries no password or key. A mocked endpoint, or a listener that only accepts connections, does not pass.
- [ ] **U.2 — Enforce the common lock boundary.** Needs: U.1. — Manual/idle lock, expired sleep/resume and shutdown cancel pending approvals, clear grants and deny new reads/releases/launches, including policy grants and in-flight races. Agent traffic cannot extend the human idle deadline. Re-unlock cannot revive old requests or capabilities; already delivered copies remain outside control.
- [ ] **U.3 — Serve current saved vault state.** Needs: U.1, V.1a, V.3a, V.5a. — Later requests and launches see app edits; deletion, moves, changed access credentials and changed exposure invalidate affected grants before the next release. Detect external file changes before releasing stale values, invalidate affected grants and require a safe reload/re-unlock; never silently overwrite, merge or continue serving the old snapshot.
- [ ] **4.4b — Own session startup and shutdown from the app.** Needs: U.1, U.2. — A user starts, unlocks, locks and closes the complete session without managing a terminal. Exercise crash/restart and contention with the standalone agent; status must reflect actual authority, not merely a listening pipe.

## T3 — AI requests in the app

The bridge, approval gate, policy and audit writer already exist. The work is to join them to the shared session and native user interaction, with exposure and authorization still independent of unlocking.

- [ ] **4.3a — Attach MCP to the shared session.** Needs: U.1, U.2. — Authenticate the local request route and preserve one-field scope, TTL, exposure and protocol limits; locked, absent, wrong-session and failed attachment paths deny. Prove the real request reaches the authority, not just that an endpoint can be connected to.
- [ ] **4.4 — Approve and deny in the desktop.** Needs: 4.3a. — Render the existing core prompt with requester, entry/field, scope, lifetime and untrusted agent reason; explicit Yes grants, while timeout, dismissal, disconnect and lock deny. Exercise a real MCP request through response and audit.
- [ ] **4.3b — Show actual requests, grants and audit outcomes.** Needs: 4.4. — Pending requests and effective grants come from the authority; audit history comes from the writer's records. Provide an explicit stop/revoke action for future access, test its effect on the next request, and distinguish unavailable state from no activity.
- [ ] **2.6a — Connect an MCP client from the app.** Needs: 4.4, 4.4b. — Surface the existing setup behavior with explicit configuration consent, exposure selection and a connection check that produces a real approval and audit record; removal revokes future session access. Client configuration contains no unlock secret.

## T4 — Project environments

An env set remains ordinary KDBX entries under `env/<project>`. Reuse the existing parser, storage and child-process injection. The app's current command-copy feature is not implementation evidence for any of the launch rows.

- [ ] **E.1a — Resolve usable project environments through the session.** Needs: U.2, U.3. — Read a selected set from the live session, validate names and refuse expired, recycled or unusable entries before disclosing any values; apply the same refusal to standalone `keypaste run`. Never silently inject a partial set.
- [ ] **E.1b — Import and launch a project from the app.** Needs: E.1a, 4.4b. — Preview/import an existing `.env` through the shared parser, map a working directory and env set, and explicitly Run or Open terminal. Confirm the selected command without exposing values in arguments, logs or saved project configuration; cancellation/lock before release starts nothing. Values reach the actual child environment and no plaintext file is produced.
- [ ] **E.1c — Use the unlocked session from the CLI runner.** Needs: E.1a. — Let `keypaste run` explicitly attach to the desktop session with project authorization and no second password prompt; refusal does not silently fall back to a separately unlocked vault. Preserve documented standalone operation, exit codes and signal behavior with unambiguous mode/ownership rules.

## T5 — Desktop delivery

The Windows MSI and Linux AppImage are internal candidates. Existing install and upgrade observations remain useful but must be repeated for the final shared-session product. The macOS desktop, package managers and GitHub Release mirrors are optional BACKLOG items; public CLI/MCP support is retained.

- [ ] **F.2b3 — Observe remaining real-desktop lock behavior.** Needs: F.2b2, U.2. — Record the Windows/Linux desktop interactions and sleep/restore behavior the shared-session release claims, including actual window-manager clicks rather than an observer's event reader alone. Existing Windows observations remain evidence for their tested version; macOS-specific observation follows a selected macOS release.
- [ ] **R.1a — Exercise the integrated desktop candidate.** Needs: 9.4, 4.6, F.15, U.2, U.3, 4.4b, 4.3b, 2.6a, E.1b, E.1c, F.2b3. — Install the candidate and complete T1–T4 with a real child process and MCP client. Create/open, edit, restore history/deletion/backup, change access, approve/deny, run env, edit again and observe fresh values, then lock and prove later releases fail. Retain outcomes and unreached checks; fixtures alone cannot close this row.
- [ ] **4.7c2 — Publish and verify the focused desktop release.** Needs: 4.7c1. Ships after: R.1a. — **Input:** a verified Microsoft Artifact Signing identity as keypaste (H-0017), or the recorded cloud-HSM fallback if ineligible, with the repository variables `sign-windows.sh` requires. Prove signing and changed-byte refusal before enabling the real policy. Publish immutable Windows/Linux desktop packages and matching CLI/MCP components, verify public hashes/provenance/signatures and upgrade/data preservation. Native install evidence must come from these public bytes; neither rehearsal certificates nor unpublished artifacts qualify.
- [ ] **L.1 — Make the released app understandable and reachable.** Needs: 4.7c2. — Version-correct download, create/open, recovery, MCP and project guides, plus issue/security reporting routes on README and the site. A new user follows them against the public package. This replaces L.1's mandatory mirrors, package managers and posting campaign; no messages or signup mail are authorized by this row.
- [ ] **R.1 — Accept the installed local product.** Needs: 4.7c2, L.1. — Repeat the R.1a journey from anonymous public downloads on each promised desktop target and retain its actual results, version and limits. Verify fresh CLI/MCP installs on their four supported targets. The local product works offline without account, relay, sharing or subscription; no package-manager entry, announcement or team pilot is needed to pass.

## Later vault features

These extend T1 after the first integrated desktop release. They are committed product features, kept outside the immediate task order and R.1's requirements; expand a row only when the founder selects it. The next five detailed tasks are unchanged.

- [ ] **P.1 — Hardware-key vault unlocking.** Needs: V.1b, U.2. Ships after: R.1. — Add supported hardware keys to vault setup/unlock and the shared session through KeePassXC-compatible [challenge-response](https://keepassxc.org/docs/#faq-yubikey-2fa). Require real-device unlock/save/reopen and KeePassXC interoperability, missing/wrong-key refusal, and documented spare-key and lost-key recovery limits. Device/platform support is explicit; OS quick unlock and browser passkeys are separate features.

## Completion

A finished task has its bounded behavior, an executed verifier and evidence naming the tested source/version. Implemented, Packaged, Published and Installation-verified stay separate. Reader-only fixtures prove a reader; they cannot prove a writer, running producer, native interaction or public release. Completed rows in [steps](steps/README.md) are historical evidence at their original scope, not blanket acceptance of the five tracks.

The source of prior plans is Git. No completed row is extended to cover new requirements. Publication, service operation, customer contact and optional work require explicit task selection and applicable authorization.

Open IDs that changed meaning: 4.3a/b, 4.4 and 4.4b are rescoped around U.1–U.3; E.1 is E.1a–c; R.1a is a candidate check and R.1 its public-download counterpart; 4.7c is completed plumbing (4.7c1) plus the open release (4.7c2); F.15 remains an unimplemented shortcut. The [rescope record](steps/rescope-2026-09-19.md) has the full continuity notes.

The [current release matrix](RELEASE.md#current-distribution--2026-09-07) owns public availability: a completed source step does not mean the behavior is in the current download.
