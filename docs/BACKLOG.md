# Feature backlog

These are retained feature ideas, not discarded features or promises for the first release. [PRODUCT](PRODUCT.md) owns accepted scope, [ROADMAP](../ROADMAP.md) the milestones of committed work and [STEPS](STEPS.md) its tasks. The features below remain available for future selection. Moving an idea here does not implement it or establish a future release. Select an item only for a concrete unmet user need, then define its scope and verifier in STEPS. Historical IDs locate previous proposals in Git; their old dependencies and commercial promises no longer bind the plan.

## Features for later consideration

| Option | Why it is outside the current product tracks / what would justify it | Previous work IDs |
|---|---|---|
| Browser save, update and passkeys | Filling a login (8.1, 8.3a) is committed to T6. Saving or updating from a page and passkeys each need their own origin-verification and interoperable design. | 8.2a/b, 8.3b–e, 8.4a/b, 8.5a/b |
| Desktop Auto-Type and cross-entry references | Adds window matching, platform input and placeholder semantics; choose a demonstrated app workflow first. Same-entry placeholders fill in since P.3b1; `{REF:}` references to another entry (P.3b2) need their own exposure and audit rules. | P.3b2, P.4a/b |
| Attachment editing | Preserving attachments does not require an editor; select one for a demonstrated workflow, with size limits and no plaintext temporary files. Custom fields (V.7) and TOTP (9.2a/b) are committed in STEPS. | V.8a/b |
| Multiple simultaneous vaults and auto-open | Recent-vault selection already exists. Add concurrent sessions only when needed, with separate locks and exposure scopes. | P.2 |
| Privacy-preserving breach checks | Local password health is in Recommendations (V.9). A remote check is a separate capability with its own privacy design. | P.8 |
| Deleting old backups after an access change | Copies taken before a password or keyfile change still open with the old credentials; `access` names them and routine pruning removes them over time. A delete verb is destructive and needs its own refusals and tests; reconsider when a person needs to close that exposure at once. | V.1a2 |
| Broader cipher/KDF controls and legacy formats | Preserve supported data now; expand advertised compatibility only with fixtures, migration safety and an identified unsupported vault. | P.3a, P.7a/b |
| Encrypted file sharing | Merging a synced file's changes (1.4a/b) is committed to T6, share links for one field or login exist (D-0357), and T8 shares a project's secrets through an end-to-end relay (D-0400). Sharing a whole file or KeeShare-style exchange needs identity and key-transfer rules that survive review against PRODUCT §3. | 5.4a, 1.4c, P.6 |
| Drop relay for files | Share links for one field or login exist (D-0357); a relay for files adds hosting and untrusted downloads. Reconsider only after file sharing demonstrates demand; old size, retention and free-service promises are withdrawn. | 5.2a–c, 5.4b, 5.7, H.7–H.10, R.2 |
| External delegation dashboard and provider rotation | Cannot infer downstream rights from a vault entry or log reader. Prove one provider's observable/revocable actions before adding a dashboard. | 6.1, 6.2, S.3 |
| Rotation from the server | A team project with server access (T11) could have the server rotate its values at their issuers. Each provider's rotation is its own integration; reconsider once T11 works and a provider is named. | — |
| SSH and Linux Secret Service | Distinct local protocols and lock-lifecycle obligations; select a named consumer and prove it before exposing vault access. | 9.3a/b, P.5 |
| Phone and web vault clients | A keypaste phone app is the later option for reaching a cloud vault (D-0408); KeePass phone apps reach a local vault through file sync. Separate interaction, delivery and security boundaries. Third-party KDBX compatibility does not establish a keypaste client; no mobile framework or browser runtime is selected by this backlog. | M.1–M.3, W.1–W.2c |
| More architectures | The macOS arm64 desktop joins the first desktop release (STEPS T5). Intel macOS, Windows ARM64, a Linux arm64 desktop and musl each need a selected target and native installation evidence. | 3.9a–d |
| GitHub Release mirrors | Homebrew, Scoop and winget are committed after 0.5.0 (3.7a–c). A mirror is a separate channel, though the MCP Registry's hosting rule may require a copy of one file (G.6c). The current download origin remains authoritative. | — |
| Fleet deployment and service scale | No operated service or fleet requirement establishes these needs today. Measure a real bottleneck or deployment request first. | S.1, S.2, S.4, S.5 |

## Smaller possibilities

| Idea | Boundary for reconsideration |
|---|---|
| A clean inherited environment | `run --no-inherit` may solve specific tool needs; `env diff` and `--json` output already exist. Avoid silently skipping unusable values. |
| Shell/direnv integration or stopping launched programs on lock | Ordinary environments are copied into processes. Any convenience must state descendant exposure and cannot promise to revoke delivered credentials. |
| Secret rotation reminders and deliberate history removal | Reminders do not rotate a provider credential; purging a leaked value is explicit and cannot erase existing copies or backups. |
| Command palette and additional generator recipes | Add when they improve an observed frequent task; existing CLI recipe controls are not proof of desktop controls. |
| Unusual-request notices | Agents' settings are kept and edited in the vault since G.8; a notice that an agent's requests look unusual is not authorization. Avoid automatic broadening, circular unlock rules or inferred trust from a client label. |
| Git secret hooks and conflict guidance | Potential project tooling; a warning or file-sync guide does not implement merge or guarantee secret removal from history. |
| Extra vulnerability channel, reproducible builds, independent review and bounty | Useful trust work when operationally supported. Keep the working private contact and current provenance checks; do not claim review or reproducibility from a draft page. |
| Repository rulesets and fork-CI observation | Administrative improvements may be selected separately. A fixture or policy document is not an observation of a real fork run. Former K.4/K.5. |
| Windows clipboard-history observation | The documented residual remains. A real-machine observation, former 1.5a, can establish only the tested platform behavior. |
| Consent-based launch notifications and educational material | The existing signup promise requires confirmation before sending. Double opt-in/unsubscribe, a public demo vault, articles and talks require a selected audience and separate sending authorization. Former 5.6, 3.11. |
| Upstream interoperability fixes | Contribute a reproducible format defect when one is found; keep required attribution and interoperability evidence. |
| A native Wayland backend for the Linux app | The app runs under X11 or XWayland, so a Wayland desktop without XWayland cannot start it; it stays so by founder decision 2026-10-22. Reconsider when Avalonia ships a stable Wayland backend. |

## Investigation candidates

These observations were previously in DECISIONS. They are not completed fixes or established causes. Reproduce a defect and give it a bounded task in STEPS before treating it as solved.

| Observation | What must be established |
|---|---|
| Listing source failures are recorded as `no-approver` | Reproduce the wrong audit classification while a listener exists; distinguish unavailable transport from failed vault access. |
| Exposure parse errors lose detail when a reply is too large | Bound diagnostics at their source if a reproducible operator problem needs it; preserve the transport limit. |
| Approver disconnects, write failures and handler exceptions lack separate diagnostics | Establish which missing observation prevents diagnosis; never log secret content. |
| Post-upload listing is printed rather than checked | Prove the storage consistency assumption before changing publication completion; immutable versions must not be burned by a false failure. |
| Windows saving depends on Transactional NTFS | Existing F.6/F.7/F.10/F.12 repairs retain their measured evidence. Replacing the dependency requires a new preservation experiment, not reopening repaired defects on speculation. |
| An access change that adds a hardware key asked it twice once on `windows-2025` ([F.53](steps/F.53.md)) | Whether a transient save retry explains it, which asks the key again per attempt; if so the test should count attempts rather than assume one. |
| Negative assertions in the desktop approval tests wait a fixed 200 ms, so they pass on a slow runner without proving the click was ignored ([F.58](steps/F.58.md)) | Give each a positive synchronisation point before asserting that nothing completed. |
| keypaste.com's `/subscribe` has no rate limiter, unlike the share routes ([F.58](steps/F.58.md)) | Add a limiter binding and the share routes' check, and verify it on a preview deploy before `main`, since a push deploys the site. |
| RELEASE's last-known-good promotion and recovery procedure, deferred until after the first desktop release by founder decision 2026-10-22 ([F.58](steps/F.58.md)) | After that release, give it a STEPS row in the release track and rehearse it once before a later release is promoted. |
| `verify-keepassxc-fields.sh` on `ubuntu-24.04` waited 30 seconds for the desktop's approval prompt and none appeared, once ([F.55](steps/F.55.md)) | Whether the prompt was raised and not seen, or never raised, under a fully loaded runner. |
| Two compat gates stopped on `windows-2025` with no log kept: `verify-keepassxc-projects.sh` for 28 minutes ([F.63](steps/F.63.md)) and `verify-keepassxc-fields.sh` for 32 ([F.65](steps/F.65.md)) | Which phase stopped and which processes were alive, from either gate's watchdog the next time it fires; then whether keypaste or the runner hung. |
| `verify-run-session.sh` on `windows-2025` printed its last `ok` and exited 1 with no message, once (app 38834646300, attempt 1, job 116760936787; [F.63](steps/F.63.md)) | Whether its exit cleanup's `rm -rf` failed on a file a process ended by `taskkill` still held, silently because the cleanup's first `exec` sends the gate's standard error to `/dev/null`; let that error through and reproduce it before repairing. |
