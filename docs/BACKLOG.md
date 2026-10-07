# Feature backlog

These are retained feature ideas, not discarded features or promises for the first release. [PRODUCT](PRODUCT.md) owns accepted scope, [ROADMAP](../ROADMAP.md) the milestones of committed work and [STEPS](STEPS.md) its tasks. The features below remain available for future selection. Moving an idea here does not implement it or establish a future release. Select an item only for a concrete unmet user need, then define its scope and verifier in STEPS. Historical IDs locate previous proposals in Git; their old dependencies and commercial promises no longer bind the plan.

## Features for later consideration

| Option | Why it is outside the current product tracks / what would justify it | Previous work IDs |
|---|---|---|
| Browser save, update and passkeys | Filling a login (8.1, 8.3a) is committed to T6. Saving or updating from a page and passkeys each need their own origin-verification and interoperable design. | 8.2a/b, 8.3b–e, 8.4a/b, 8.5a/b |
| Desktop Auto-Type and cross-entry references | Adds window matching, platform input and placeholder semantics; choose a demonstrated app workflow first. Same-entry placeholders are P.3b1 in STEPS; `{REF:}` references to another entry (P.3b2) need their own exposure and audit rules. | P.3b2, P.4a/b |
| Attachment editing | Preserving attachments does not require an editor; select one for a demonstrated workflow, with size limits and no plaintext temporary files. Custom fields (V.7) and TOTP (9.2a/b) are committed in STEPS. | V.8a/b |
| Multiple simultaneous vaults and auto-open | Recent-vault selection already exists. Add concurrent sessions only when needed, with separate locks and exposure scopes. | P.2 |
| Organize a vault with AI | People will ask an AI to tidy their vault: groups, names, tags and duplicates. PRODUCT §2 excludes model-controlled vault administration, every name the AI reads goes to its provider, and a planted entry name can steer the model, for example into moving secrets under a standing rule (T-13). Three designs: the AI proposes a plan that the person reviews and the app applies, flagging any move into or out of an agent's or rule's reach; the AI edits directly, with logging and undo; or a built-in organizer that groups entries by service and finds duplicates with no AI. Select one for a demonstrated need. | — |
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

Complete KeePassXC parity is withdrawn as an objective. Individual capabilities above may be selected for their value; P.0/P.9's exhaustive parity contract and certification gate are removed rather than deferred. The former Working proposition → Pilot ready → Paid release → Expansion → Scale sequence is replaced by the product tracks in [ROADMAP](../ROADMAP.md). No backlog item inherits an obligation to recreate it.

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
| The process gates spend most of their time in fixed waits: `sleep` holding an agent's input open, and the real 45-second approval window ([F.52](steps/F.52.md)) | Measure each gate's waits against its runtime, and establish which can wait on the output it expects (`wait_for`) without changing what the approval, policy and session gates prove. |
| Core.Tests and Cli.Tests take 5 to 7 minutes on Windows and macOS against about 2.5 on Linux ([F.52](steps/F.52.md)) | Record each test's duration on all three runners and name what dominates, keeping the KDF parameters the tests forbid lowering. |

Generic UX-score infrastructure, a document describing document alignment, mandatory marketing per task/milestone, a second maintainer's organization before one exists, and enterprise assurance without a customer requirement are removed. They do not complete the selected user journey. Specific usability observations, release evidence and security tests remain part of each relevant track.
