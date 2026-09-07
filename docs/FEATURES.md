# Feature coverage and KeePassXC baseline

**Reviewed 2026-09-07. Status: first-pass capability inventory aligned with PRODUCT v1.2 and the accepted build plan; not a parity certification.**

The comparison baseline is **KeePassXC 2.7.12**, the release currently advertised on its
[download page](https://keepassxc.org/download/). The online
[User Guide](https://keepassxc.org/docs/KeePassXC_UserGuide) identifies itself as 2.7.11;
version-specific behavior must therefore be checked against the 2.7.12 release and source.
This inventory covers feature families. It does not claim that every upstream option, integration,
file variant or platform behavior has been inspected. **P.0** in
[STEPS](STEPS.md#define-coverage-before-claiming-a-complete-password-manager) expands this inventory into the complete
versioned behavior contract; **P.9** verifies that contract against public keypaste releases.

KDBX compatibility, feature implementation and published availability are separate claims.
Keeping an attachment when editing another field does not provide an attachment manager.
Building an app in CI does not make it installable by a customer. No broad "KeePassXC parity"
claim is justified until individual behaviors have evidence on each supported platform.

## How to read the status

The keypaste column describes the inspected source and current documentation, principally
[desktop behavior](desktop.md), [existing steps](STEPS.md),
[entry browsing](../src/Keypaste.App/ViewModels/EntriesViewModel.cs),
[vault operations](../src/Keypaste.Core/Vault.cs) and
[format handling](../src/Keypaste.Core/Internal/KeePassInterop.cs).
**The published release remains CLI/MCP v0.1.0; the desktop is not released.** An implemented
source feature is not automatically present in that download: for example, `keypaste setup`
was added afterward. Release-specific availability belongs in [release documentation](RELEASE.md).

"Not established" means this review found no implemented user workflow to claim. A delivery row
records accepted work, not evidence that it shipped. The documentation realignment added tasks;
it did not implement their features.

[PRODUCT v1.2](PRODUCT.md) owns accepted scope. [STEPS](STEPS.md#build-order) owns current status,
dependencies, build order and milestone gates: Working proposition (R.1), Pilot ready (R.2),
Paid release (R.3), Expansion including the full baseline (P.9) and organization pilot (R.4),
and demand-activated Scale. The mappings below point into that plan; this inventory is not a
second work queue. Rows grouped with lettered children refer to every named child's acceptance
criteria in STEPS.

## Desktop and vault coverage

The upstream families come from the official
[feature overview](https://keepassxc.org/docs/KeePassXC_GettingStarted): vaults, groups, search,
generation, reports, import/export, TOTP, fields, attachments, history, hardware keys, CLI,
auto-open, sharing, SSH and Secret Service. The
[User Guide](https://keepassxc.org/docs/KeePassXC_UserGuide) details backups, restoration,
merge, quick unlock and integration behavior.

| Family | Keypaste source status | Delivery rows / milestone |
|---|---|---|
| Offline vault creation and opening | CLI creates/opens KDBX4; desktop opens existing vaults. No GUI creation. | 0.2, 0.3, 4.1 complete; 4.8 adds GUI creation. Working proposition. |
| Entry editing | GUI adds generated credentials and edits username, URL and notes; cannot enter an existing password/API value. | 4.2 covers the existing subset; 4.9 adds secure existing-value input/edit. Working proposition. |
| Organization and search | GUI group navigation and case-insensitive title/group search exist. Rich field/tag search and rename/move/clone workflows are not established. | V.5a–b add stable organization operations and GUI search/controls. Working proposition. |
| Generator | Character password generation exists. Passphrase workflow not established. | 4.2 covers the existing generator; V.6 adds core/CLI/GUI passphrases; 8.3d adds browser generation. Working proposition. |
| Entry history | Ordinary updates retain prior values in KeePass history. No history-reading/restoration UI. | V.2a–b add core and desktop inspection/restoration. Working proposition. |
| Recycle bin | Delete permanently removes the entry and its history, recording a deletion tombstone. | V.3a–b add reversible deletion, trash and recovery while retaining explicit permanent deletion. Working proposition. |
| Backups and merge | External-write guard refuses stale saves. Automatic backup/restore workflow not established; merge remains unimplemented. | V.4a–b cover encrypted backup/restore/export; 1.4a–c cover merge policy, core and CLI/GUI conflict recovery. Working proposition. |
| Attachments and custom fields | Ordinary edits preserve existing attachment/custom-string data. Management UI not established. | V.7 adds protected custom fields; V.8a–b add bounded attachment operations and GUI controls; 9.4 verifies compatibility. Working proposition. |
| Tags, expiry, icons, references | Management workflows not established. | V.5a–b cover everyday tags/expiry; P.3a adds remaining metadata/settings and P.3b references/placeholders. Working proposition, then Expansion. |
| Password health | Weak/reused/expired and compromised-password reports not established. | V.9 adds local findings for the Working proposition; P.8 adds separately reviewed compromised-password checking in Expansion. |
| TOTP | No implemented user workflow. | 9.2a–b cover supported algorithm/digit/period combinations, CLI/GUI/MCP use; 8.3e covers browser filling. Working proposition. |
| Keyfiles | Current frontend unlock does not expose them. | V.1a–b cover core/CLI/GUI create/open/change-credentials, independently of merge. Working proposition. |
| Hardware-key protection | No implemented challenge-response workflow established. | P.1 covers explicitly supported hardware-key devices and interoperable vault operations. Expansion. |
| Quick unlock | Idle/manual locking exists; OS-assisted quick unlock not established. | 4.10a adds Windows and 4.10b macOS quick unlock after R.1; optional alongside Pilot ready. P.0/P.9 include claimed baseline coverage. |
| Import/export | Env import/export works; password-manager importers remain unimplemented. | 9.1a provides the pipeline; 9.1b/c/d1/d2/e cover Bitwarden JSON, LastPass CSV, 1Password 1PUX/CSV and KeePassXC CSV/KDBX onboarding; 9.1f provides GUI guidance. V.4b covers encrypted whole-vault export. Working proposition. |
| Multiple vaults and auto-open | Recent-vault selection exists; simultaneous vaults/auto-open not established. | P.2 covers simultaneous vault identities, linked opening, scopes and lock behavior. Expansion. |
| Format variants/settings | KDBX4/AES-256/Argon2d writing and bidirectional fixtures exist. Broad cipher/KDF/keyfile/metadata coverage is not proved. | 9.4 publishes the Working proposition compatibility result; P.7a covers remaining cipher/KDF controls and P.7b remaining legacy format/import behavior. P.0/P.9 define and verify the complete baseline. |

Upstream password reports include weak, reused and expired credentials; their existence does not
prescribe keypaste's scoring algorithm.
[KeePassXC health-check description](https://keepassxc.org/blog/2020-08-15-keepassxc-password-healthcheck/).
Upstream quick unlock depends on Windows/macOS hardware and setup; challenge-response protection
is different from authenticating to an online account.
[Getting Started](https://keepassxc.org/docs/KeePassXC_GettingStarted),
[hardware-key FAQ](https://keepassxc.org/docs/).

## Integration and distribution coverage

| Family | Keypaste source status | Delivery rows / milestone |
|---|---|---|
| Browser fill, generation and save/update | No implemented/released extension. | 8.1 adds native-host pairing; 8.3a–e cover origin-bound fill, save, update, generation and TOTP/custom fields; 8.4a–b cover actual store distribution. 8.2a–b cover approval consistency. Working proposition. |
| Passkeys | No implemented workflow established. | 8.5a covers origin-bound creation; 8.5b authentication, lifecycle, portability and public versions. Expansion. |
| Auto-Type | No implemented workflow established. | P.4a covers Windows/macOS; P.4b the declared Linux display-server path. Expansion. |
| SSH integration | No implemented vault-backed SSH agent workflow. | 9.3a covers signing/protocol behavior; 9.3b selection, lock/unload and published CLI/GUI use. P.0 records differences from the upstream existing-agent design. Expansion. |
| Linux Secret Service | No implemented workflow established. | P.5 covers scoped client access, collection exposure and lock behavior. Expansion. |
| KeeShare-compatible sharing | No established KeeShare protocol workflow. | P.6 covers the reviewed interoperable path. Keypaste share links (5.4a–b) and organization sharing (7.1a–d) are distinct work, not KeeShare evidence. Expansion. |
| CLI/MCP distribution | CLI/MCP v0.1.0 is public on four targets; setup remains source-only. | R.0a–c and 3.8 cover version/platform contracts, complete immutable publication, provenance and the next verified CLI patch. Working proposition; independent of unfinished desktop work. |
| Desktop installation and updates | App builds from source; no public desktop installer. | 4.7a–d cover candidates, native installation, upgrades/recovery and public downloads; 3.5a–b/3.6a–b cover macOS/Windows signing. R.1 verifies the complete product. [RELEASE.md](RELEASE.md) owns platform/channel evidence. |
| Additional distribution | No Homebrew/Scoop/winget distribution or public binaries for the additional planned targets. | 3.7a–c cover package managers; 3.9a–d cover macOS Intel, Windows ARM64, Linux musl CLI/MCP and Linux ARM64 desktop. Expansion; each target requires separate evidence. |
| Mobile | No first-party client or custom-relay integration established. | M.1 proves the selected phone approach; M.2a–c deliver account/unlock, credential/autofill and sync/recovery/export workflows; M.3 verifies public phone availability. Required for Paid release R.3. |
| CLI, envs and agent access | CLI/env injection, MCP approval, policy and local audit implemented; setup is newer than v0.1.0. | 4.3a–b, 4.4 and 4.4b add native approval/activity/lifecycle; E.1 completes desktop env use. Working proposition. 6.1–2 cover separately proven external delegation views in Expansion. |
| Product guidance | Local guides exist; future GUI, hosted and phone instructions cannot yet be followed against released products. | 3.10a delivers version-correct local guides before R.1; 5.7 and 3.10b deliver sync/operator and hosted lifecycle guides before their respective gates. |

KeePassXC's tagged browser documentation includes fill, generation, adding/updating credentials and
extra fields; a read-only filler should therefore name its supported subset rather than claim parity.
[Browser integration at 2.7.12](https://github.com/keepassxreboot/keepassxc/blob/2.7.12/docs/topics/BrowserIntegration.adoc).
Its passkeys require browser integration. Auto-Type is separate, with an X11 limitation on Linux.
Its SSH feature supplies keys to an existing agent; keypaste's planned agent is a different design.
[User Guide](https://keepassxc.org/docs/KeePassXC_UserGuide).

KeePassXC distributes platform packages and browser-store extensions. It deliberately delegates
cloud file sync to other services and recommends separate mobile clients; there is no official
KeePassXC mobile app. KDBX access in those apps does not establish support for keypaste's future
account, device or relay protocol.
[Downloads](https://keepassxc.org/download/), [FAQ](https://keepassxc.org/docs/).

## Hosted and organization additions

These capabilities are accepted scope under PRODUCT v1.2, beyond desktop KeePassXC parity.
The hosted service and organization workflows remain unimplemented and unreleased. Their task
mappings describe the required delivery, without claiming an available service.

| User outcome | Keypaste source status | Delivery rows / milestone |
|---|---|---|
| Start without terminal/file expertise | Desktop opens an existing file; account/signup and managed storage onboarding are absent. | 4.8/9.1f provide local create/import; H.1–H.2 settle and implement account authentication; 5.3b provides managed desktop onboarding. Working proposition, then Pilot ready. |
| Authorize and revoke devices | No hosted account MFA/session/device workflow. | H.3 adds MFA/session revocation; H.4 device authorization; 5.3c exposes device/sync controls. Pilot ready. M.2a applies the reviewed path to the selected phone client for Paid release. |
| Sync without exposing vault plaintext | Local KDBX editing exists; no relay/client sync implementation. | 5.2a–c implement and publish the shared hosted/self-hosted relay; 5.3a–c implement recoverable client sync and controls, and 5.3d publishes the updated clients. H.7–H.8 provide hosting enrollment/deployment. Pilot ready. |
| Recover account or vault access | No hosted recovery workflow; account and vault recovery need separate authority. | H.5a defines what is recoverable; H.5b implements and rehearses the approved path. 5.7 documents the actual limits. Pilot ready; phone recovery follows in M.2c. |
| Survive mistakes and outages | Existing local write guard refuses stale saves; backup, merge and hosted operations are unimplemented. | V.4a–b and 1.4a–c cover local recovery; H.9 proves service durability, outage handling and alerts; H.10 establishes support/incident procedures. R.2 requires observed pilot journeys. |
| Leave or stop paying | Local use is account-free; hosted deletion, export and payment behavior are absent. | H.6 adds account export/deletion; 5.5a–c cover payment enrollment, entitlement, cancellation and failure; 3.10b documents the lifecycle. Local access remains available. Pilot ready, then Paid release. |
| Use a browser vault | No browser vault or proven runtime/core path. | W.1 proves the architecture; W.2a–c cover reviewed onboarding/unlock, editing/sync and public delivery. Expansion after the desktop-led pilot; not an implied R.1/R.2 feature. |
| Share a scoped bundle | No public share-link workflow. | 5.4a–b review/create scoped bundles and deliver one-download sharing with quarantine import. Expansion; access limits cannot erase copies already received. |
| Share work credentials | No organization-owned vault/collection or membership workflow. | 7.1a–d define ownership/recovery authority and deliver membership, roles, service accounts, sharing, rotation/offboarding and desktop administration for logins, notes, API keys and envs. Expansion, verified through R.4. |
| Approve team or service-account use | Existing local single-user approvals do not establish a team broker. | 7.2 adds attributed scoped approval with current membership/policy checks; 7.4 adds review controls. Expansion, verified through R.4. |
| Administer a workforce | No organization SSO, provisioning or access-review implementation. | 7.3a–b implement OIDC and provisioning/deprovisioning; 7.4 adds access reviews/dashboard; 6.1–2 add external delegation coverage only where provider evidence supports it. R.4 verifies an organization pilot. |
| Trust hosted operation | Security policy and local checks exist; no operated/reviewed hosted service. | H.9–H.10 and 5.7 cover operations, privacy/support and usable procedures before R.2; 10.2 independently reviews new hosted/client boundaries before R.3; R.4 requires review of team boundaries. |
| Meet later enterprise operating needs | No managed fleet deployment, downstream rotation integration or enterprise assurance evidence. | S.1–S.5 cover measured capacity, fleet deployment, one provider credential lifecycle, support capacity and requested assurance. Demand-activated Scale; credentials already disclosed require provider-side rotation, not merely membership revocation. |

Organization roles and per-collection permissions are distinct controls in established password
managers. They are useful models for the Teams design.
[Bitwarden organizations](https://bitwarden.com/help/about-organizations/),
[collection permissions](https://bitwarden.com/help/collection-permissions/).
Recovery also needs an explicit authority model: an organization-controlled recovery key changes
who can recover access, even when the provider cannot decrypt the vault.
[Bitwarden recovery design](https://bitwarden.com/help/account-recovery/).

Vault permissions cannot erase a recipient's previous copy or make a revealed static password
expire. Offboarding must distinguish refusing future downloads/broker requests, rotating vault keys,
and rotating the actual downstream credential. Hidden fields are still shared credentials.
[Bitwarden offboarding](https://bitwarden.com/help/onboarding-and-succession/),
[permission limits](https://bitwarden.com/help/collection-permissions/).

Full PAM/IAM would additionally administer privileges in other systems, including temporary role
activation and resource access reviews. That is a separate scope from an enterprise password vault.
Integrate with identity providers first; do not promise the broader platform by implication.
[Microsoft PIM scope](https://learn.microsoft.com/en-us/entra/id-governance/privileged-identity-management/pim-configure).

## Evidence required to close a gap

For each promoted feature, record its workflow, supported platforms, upstream baseline if applicable,
Core/CLI/GUI/browser/mobile coverage, test evidence and first published version. Useful acceptance
examples include:

- A new user downloads the app, creates a vault, saves an existing login, finds it, changes it,
  restores the prior value, then restores an accidentally deleted entry without a terminal.
- A KeePassXC fixture containing protected fields, attachments, history and duplicate titles survives
  an unrelated keypaste edit; unsupported operations refuse rather than discard data.
- Two offline clients edit different entries and converge; concurrent conflicting edits remain
  recoverable. Restore a downloaded encrypted backup on a fresh device.
- An offboarded teammate cannot fetch the next rotated snapshot or obtain a new broker release;
  the test explicitly acknowledges that a retained old snapshot remains decryptable with its old key.
- Browser tests cover hostile domain matches, new-account save, password update, locked state,
  TOTP/passkeys where claimed, and the supported OS/browser combinations.

These examples explain the evidence expected from the mapped delivery rows. They are not passing
results or a second acceptance queue. P.0 records every baseline behavior, including gaps this
first-pass inventory missed; P.9 cannot close until those behaviors have public-version evidence.
R.1/R.2/R.3/R.4 separately establish the working local product, managed pilot, paid consumer release
and organization pilot.
