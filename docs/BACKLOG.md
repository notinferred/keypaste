# Feature backlog

These are retained feature ideas, not discarded features or promises for the first release. [PRODUCT](PRODUCT.md) owns accepted scope, [ROADMAP](../ROADMAP.md) the milestones of committed work and [STEPS](STEPS.md) its tasks. The features below remain available for future selection. Moving an idea here does not implement it or establish a future release. Select an item only for a concrete unmet user need, then define its scope and verifier in STEPS. Historical IDs locate previous proposals in Git; their old dependencies and commercial promises no longer bind the plan.

## Features for later consideration

| Option | Why it is outside the current product tracks / what would justify it | Previous work IDs |
|---|---|---|
| Browser save, update and passkeys | Filling a login (8.1, 8.3a) is committed to T6. Saving or updating from a page and passkeys each need their own origin-verification and interoperable design. | 8.2a/b, 8.3b–e, 8.4a/b, 8.5a/b |
| Desktop Auto-Type and cross-entry references | Adds window matching, platform input and placeholder semantics; choose a demonstrated app workflow first. Same-entry placeholders are P.3b1 in STEPS; `{REF:}` references to another entry (P.3b2) need their own exposure and audit rules. | P.3b2, P.4a/b |
| Attachment editing | Preserving attachments does not require an editor; select one for a demonstrated workflow, with size limits and no plaintext temporary files. Custom fields (V.7) and TOTP (9.2a/b) are committed in STEPS. | V.8a/b |
| Multiple simultaneous vaults and auto-open | Recent-vault selection already exists. Add concurrent sessions only when needed, with separate locks and exposure scopes. | P.2 |
| Privacy-preserving breach checks | Local password health (V.9) is committed to T6. A remote check is a separate capability with its own privacy design. | P.8 |
| Keyfile generation | PRODUCT §3.4 forbids writing a secret to disk unencrypted, and a keyfile is key material; `keypaste access` attaches only an existing one (D-0287). Reconsider with a decision reconciling §3.4, an overwrite and location rule, and a loss warning. | V.1a2 |
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
| An idle warning that counts down | The header's warning is set once, 30 seconds before the lock, so its number stays at 29 while the lock approaches ([F.2b3a](steps/F.2b3a.md)). Update it each second or drop the number; any key or click must still cancel it. |
| Command palette and additional generator recipes | Add when they improve an observed frequent task; existing CLI recipe controls are not proof of desktop controls. |
| Deleting a group, and moving one to another parent | Core has neither, so neither front end can offer them and a group made by mistake is removed in KeePassXC. A deletion has to decide what happens to what is inside it — recycle each entry, or refuse a group that is not empty — and that is its own design with its own refusals. Select it when somebody actually reorganizes deeply enough to need it. |
| Pasting the master password | A local input choice needing explicit clipboard handling and tests; entry-field paste support does not establish it for unlock fields. |
| Policy editing/storage in the vault and unusual-request notices | Policy is authorization. Avoid automatic broadening, circular unlock rules or inferred trust from a client label. |
| Git secret hooks and conflict guidance | Potential project tooling; a warning or file-sync guide does not implement merge or guarantee secret removal from history. |
| Extra vulnerability channel, reproducible builds, independent review and bounty | Useful trust work when operationally supported. Keep the working private contact and current provenance checks; do not claim review or reproducibility from a draft page. |
| Repository rulesets and fork-CI observation | Administrative improvements may be selected separately. A fixture or policy document is not an observation of a real fork run. Former K.4/K.5. |
| Windows clipboard-history observation | The documented residual remains. A real-machine observation, former 1.5a, can establish only the tested platform behavior. |
| Consent-based launch notifications and educational material | The existing signup promise requires confirmation before sending. Double opt-in/unsubscribe, a public demo vault, articles and talks require a selected audience and separate sending authorization. Former 5.6, 3.11. |
| Upstream interoperability fixes | Contribute a reproducible format defect when one is found; keep required attribution and interoperability evidence. |

## Investigation candidates

These observations were previously in DECISIONS. They are not completed fixes or established causes. Reproduce a defect and give it a bounded task in STEPS before treating it as solved.

| Observation | What must be established |
|---|---|
| A policy allowance is consumed before an oversized approved response fails delivery | Decide and test whether the allowance counts authorization or delivery; retain audit truth and refusal behavior. |
| Policy rate-limit windows use wall time | Reproduce a clock-jump effect on allowance, then bound it without weakening approval expiry. |
| `projects.json` is written only by the desktop but read by `keypaste run`, `env export` and `env diff` | Establish whether a CLI-only user can map a directory to a project at all, and whether a command given no project then fails without saying why. |
| Listing source failures are recorded as `no-approver` | Reproduce the wrong audit classification while a listener exists; distinguish unavailable transport from failed vault access. |
| Exposure parse errors lose detail when a reply is too large | Bound diagnostics at their source if a reproducible operator problem needs it; preserve the transport limit. |
| Approver disconnects, write failures and handler exceptions lack separate diagnostics | Establish which missing observation prevents diagnosis; never log secret content. |
| Post-upload listing is printed rather than checked | Prove the storage consistency assumption before changing publication completion; immutable versions must not be burned by a false failure. |
| Windows saving depends on Transactional NTFS | Existing F.6/F.7/F.10/F.12 repairs retain their measured evidence. Replacing the dependency requires a new preservation experiment, not reopening repaired defects on speculation. |
| Core.Tests and Cli.Tests take 7 to 10 minutes on Windows and macOS against about 4 on Linux ([F.52](steps/F.52.md), measured in [F.58](steps/F.58.md)) | Each assembly alone takes about half its concurrent time, so the runner is saturated by real Argon2 derivations rather than held by a lock. Splitting `test` into a Core and Mcp job and a Cli job per runner should bring Windows from about 620 s to 430 to 470 s; one dispatch of that split settles it, and it amends D-0423's one job per profile. |
| An access change that adds a hardware key asked it twice once on `windows-2025` ([F.53](steps/F.53.md)) | Whether a transient save retry explains it, which asks the key again per attempt; if so the test should count attempts rather than assume one. |
| A credential reply lost on a live connection is re-sent once by the bridge (`ApproverConnection.cs` retry), so a person could be asked twice for a request they answered ([F.58](steps/F.58.md)) | Reproduce a lost reply after an approval, then decide whether credential requests are at most once, as runs are, or carry an id the owner remembers. |
| The bridge waits for the owner's reply only on the MCP request's token (`ApproverClient.cs`), so a suspended `keypaste agent` holds an agent's call for as long as its client waits ([F.58](steps/F.58.md)) | Reproduce with a stopped owner, then bound the wait by the approval window as the CLI does, without a retry after it. |
| `app.toml` keeps only the keys this version knows, and a `projects.json` mapping with one bad field is dropped on the next save ([F.58](steps/F.58.md)) | Reproduce with a newer and an older front end on one home, then refuse or preserve what is not understood rather than drop it. |
| A KDBX 3.1 vault is converted to KDBX 4 on its first save, and that path is not exercised ([F.58](steps/F.58.md), FEATURES' "Not exercised") | Check in a 3.1 fixture made by another writer and test open, save, reopen and the backup kept before the conversion. |
| Negative assertions in the desktop approval tests wait a fixed 200 ms, so they pass on a slow runner without proving the click was ignored ([F.58](steps/F.58.md)) | Give each a positive synchronisation point before asserting that nothing completed. |
| keypaste.com's `/subscribe` has no rate limiter, unlike the share routes ([F.58](steps/F.58.md)) | Add a limiter binding and the share routes' check, and verify it on a preview deploy before `main`, since a push deploys the site. |
| An owner that crashes leaves nothing on disk saying why ([F.58](steps/F.58.md)) | Decide what a local crash record may hold, since exception messages can carry entry names, before writing one. |
| RELEASE's last-known-good promotion and recovery procedure is still open ([F.58](steps/F.58.md)) | Give it a STEPS row in the release track and rehearse it once before 0.5.0 is promoted. |
| `AtomicReplaceTests.A_commit_replaces_the_vault_with_what_was_written` failed once on `windows-2025` with "name reserved for use by another transaction" ([F.57](steps/F.57.md)) | Whether Transactional NTFS leaves a name reserved after the previous test's transaction ends, and whether a save retry would absorb it. |
| `verify-keepassxc-fields.sh` on `ubuntu-24.04` waited 30 seconds for the desktop's approval prompt and none appeared, once ([F.55](steps/F.55.md)) | Whether the prompt was raised and not seen, or never raised, under a fully loaded runner. |
