# Feature backlog

These are retained feature ideas, not discarded features or promises for the first release. [PRODUCT](PRODUCT.md) owns accepted scope and [STEPS](STEPS.md) owns committed delivery, including hardware-key vault unlocking as later T1 work. The features below remain available for future selection. Moving an idea here does not implement it or establish a future release. Select an item only for a concrete unmet user need, then define its scope and verifier in STEPS. Historical IDs locate previous proposals in Git; their old dependencies and commercial promises no longer bind the plan.

## Features for later consideration

| Option | Why it is outside the current product tracks / what would justify it | Previous work IDs |
|---|---|---|
| Browser fill, save, update and passkeys | A separate origin-verification and store-distribution surface; consider after evidence that copy/reveal is the daily-use obstacle. Passkeys need their own interoperable design. | 8.1, 8.2a/b, 8.3a–e, 8.4a/b, 8.5a/b |
| Desktop Auto-Type and field references | Adds window matching, platform input and placeholder semantics; choose a demonstrated app workflow first. | P.3b, P.4a/b |
| TOTP, custom fields and attachment editing | Useful vault depth, but preserving existing data does not require implementing every editor. Select the needed family and test its complete workflow. | 9.2a/b, V.7, V.8a/b |
| Third-party password-manager importers | Existing supported KDBX opening is the initial migration path. Choose an actual source format; require preview, loss reporting and source preservation. | 9.1a–f |
| Multiple simultaneous vaults and auto-open | Recent-vault selection already exists. Add concurrent sessions only when needed, with separate locks and exposure scopes. | P.2 |
| OS-assisted quick unlock | Windows Hello or Touch ID can make repeated unlocking more convenient; require supported hardware and real-device evidence. Hardware-key vault unlocking has its own committed P.1 row in STEPS. | 4.10a/b |
| Vault health and privacy-preserving breach checks | Local reports and remote checks are separate capabilities; an identified user need must justify each. | V.9, P.8 |
| Broader cipher/KDF controls and legacy formats | Preserve supported data now; expand advertised compatibility only with fixtures, migration safety and an identified unsupported vault. | P.3a, P.7a/b |
| Encrypted file sharing and entry-level merge | Not needed for one person's vault/AI/project loop. A real sharing or divergent-edit need must justify merge, tombstones, identity and key-transfer rules. No share/receive implementation exists. | 5.4a, 1.4a–c, P.6 |
| Drop relay and link sharing | Adds hosting and untrusted downloads before the local product is complete. Reconsider only after file sharing demonstrates demand; old size, retention and free-service promises are withdrawn. | 5.2a–c, 5.4b, 5.7, H.7–H.10, R.2 |
| Managed sync and account onboarding | Adds device identity, conflict resolution, recovery and service operation. External file sync can carry a KDBX today, but is not a keypaste merge service. | 5.2d, 5.3a–d, H.1–H.6 |
| Paid hosting and billing | No paid product is selected. Pricing, entitlements, payment infrastructure and a paid-release gate require a new commercial decision. | 5.5a–c, 5.8, 10.2, R.3 |
| Organizations, team broker, SSO and provisioning | A distinct ownership and administration product. Reconsider only for a named team need, with retained-copy and offboarding limits explicit. | 7.1a–d, 7.2, 7.3a/b, 7.4, R.4 |
| External delegation dashboard and provider rotation | Cannot infer downstream rights from a vault entry or log reader. Prove one provider's observable/revocable actions before adding a dashboard. | 6.1, 6.2, S.3 |
| SSH and Linux Secret Service | Distinct local protocols and lock-lifecycle obligations; select a named consumer and prove it before exposing vault access. | 9.3a/b, P.5 |
| Phone and web vault clients | Separate interaction, delivery and security boundaries. Third-party KDBX compatibility does not establish a keypaste client; no mobile framework or browser runtime is selected by this backlog. | M.1–M.3, W.1–W.2c |
| macOS desktop and more architectures | Preserve existing CLI support; expand desktop support when a target is selected, funded and natively installation-tested. Existing macOS artifacts are internal. | 3.5a/b, 4.7a2, 4.7e, 3.9a–d |
| Package managers and GitHub Release mirrors | Useful discovery and update channels after verified downloads exist; choose channels people actually request. The current download origin remains authoritative. | 3.7a–c, L.1 |
| Fleet deployment and service scale | No operated service or fleet requirement establishes these needs today. Measure a real bottleneck or deployment request first. | S.1, S.2, S.4, S.5 |

Complete KeePassXC parity is withdrawn as an objective. Individual capabilities above may be selected for their value; P.0/P.9's exhaustive parity contract and certification gate are removed rather than deferred. The former Working proposition → Pilot ready → Paid release → Expansion → Scale sequence is replaced by the five product tracks. No backlog item inherits an obligation to recreate it.

## Smaller possibilities

| Idea | Boundary for reconsideration |
|---|---|
| Project env diff, JSON export and a clean inherited environment | Names-only comparison, explicit output formats and `run --no-inherit` may solve specific tool needs. Avoid silently skipping unusable values. |
| Shell/direnv integration or stopping launched programs on lock | Ordinary environments are copied into processes. Any convenience must state descendant exposure and cannot promise to revoke delivered credentials. |
| Secret rotation reminders and deliberate history removal | Reminders do not rotate a provider credential; purging a leaked value is explicit and cannot erase existing copies or backups. |
| Applying the adopted brand | [BRAND](BRAND.md) records unapplied marks and palette; apply them when relevant interface work is selected, without a separate release gate. |
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
| Listing source failures are recorded as `no-approver` | Reproduce the wrong audit classification while a listener exists; distinguish unavailable transport from failed vault access. |
| Exposure parse errors lose detail when a reply is too large | Bound diagnostics at their source if a reproducible operator problem needs it; preserve the transport limit. |
| Approver disconnects, write failures and handler exceptions lack separate diagnostics | Establish which missing observation prevents diagnosis; never log secret content. |
| Post-upload listing is printed rather than checked | Prove the storage consistency assumption before changing publication completion; immutable versions must not be burned by a false failure. |
| Windows saving depends on Transactional NTFS | Existing F.6/F.7/F.10/F.12 repairs retain their measured evidence. Replacing the dependency requires a new preservation experiment, not reopening repaired defects on speculation. |

Generic UX-score infrastructure, a document describing document alignment, mandatory marketing per task/milestone, a second maintainer's organization before one exists, and enterprise assurance without a customer requirement are removed. They do not complete the selected user journey. Specific usability observations, release evidence and security tests remain part of each relevant track.
