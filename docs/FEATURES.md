# Feature coverage and KeePassXC baseline

Reviewed 2026-09-07 against PRODUCT v1.2; milestone mappings updated 2026-09-15 for PRODUCT v1.4 (D-0176); entry editing restated 2026-09-18 when 4.9 closed. This is a first-pass capability inventory.

The baseline is KeePassXC 2.7.12, advertised on its [download page](https://keepassxc.org/download/) at review time. The online [User Guide](https://keepassxc.org/docs/KeePassXC_UserGuide) identifies itself as 2.7.11, so version-specific behavior requires checking against the 2.7.12 release and source. This inventory covers feature families; individual options, integrations, file variants and platform behaviors remain for P.0 in [STEPS](STEPS.md#define-coverage-before-claiming-a-complete-password-manager). P.9 requires evidence for every behavior in that versioned contract on public keypaste releases before any complete-coverage claim.

KDBX compatibility, feature implementation and published availability require separate evidence. Preserving attachments establishes data compatibility; an attachment manager needs its own workflow. Public installation requires checks against released packages on each supported platform.

## How to read the status

The keypaste column describes the source and documentation inspected on the review date: [desktop behavior](desktop.md), [existing steps](STEPS.md), [entry browsing](../src/Keypaste.App/ViewModels/EntriesViewModel.cs), [vault operations](../src/Keypaste.Core/Vault.cs) and [format handling](../src/Keypaste.Core/Internal/KeePassInterop.cs). At that date, CLI/MCP v0.1.0 was public and the desktop was unreleased; `keypaste setup` had been added after that download. [RELEASE](RELEASE.md) owns current release availability.

"Not established" means this review found no implemented user workflow. Delivery rows identify accepted work; completion requires their evidence in STEPS.

[PRODUCT](PRODUCT.md) owns accepted scope. [STEPS](STEPS.md#build-order) owns status, dependencies and milestone gates: Working proposition (R.1), Pilot ready (R.2), Paid release (R.3), Expansion with the full baseline (P.9) and organization pilot (R.4), and Scale. Mappings below refer to that plan and every named child's acceptance criteria.

## Desktop and vault coverage

The upstream families come from the official [feature overview](https://keepassxc.org/docs/KeePassXC_GettingStarted): vaults, groups, search, generation, reports, import/export, TOTP, fields, attachments, history, hardware keys, CLI, auto-open, sharing, SSH and Secret Service. The [User Guide](https://keepassxc.org/docs/KeePassXC_UserGuide) details backups, restoration, merge, quick unlock and integration behavior.

| Family | Keypaste source status | Delivery rows / milestone |
|---|---|---|
| Offline vault creation and opening | CLI and desktop both create and open KDBX4 through one set of core rules. | 0.2, 0.3, 4.1, 4.8 complete, source only; no desktop download yet. Working proposition. |
| Entry editing | GUI adds generated or existing credentials, replaces an existing password or variable value, and edits username, URL and notes. | 4.2 and 4.9 complete, source only. Working proposition. |
| Organization and search | GUI group navigation and case-insensitive title/group search exist. Rich field/tag search and rename/move/clone workflows are not established. | V.5a–b add stable organization operations and GUI search/controls. Expansion. |
| Generator | Character password generation exists. Passphrase workflow not established. | 4.2 covers the existing generator; V.6 adds core/CLI/GUI passphrases, which share passphrases use (Working proposition); 8.3d adds browser generation (Expansion). |
| Entry history | Ordinary updates retain prior values in KeePass history. No history-reading/restoration UI. | V.2a–b add core and desktop inspection/restoration. Working proposition. |
| Recycle bin | Delete permanently removes the entry and its history, recording a deletion tombstone. | V.3a–b add reversible deletion, trash and recovery while retaining explicit permanent deletion. Expansion. |
| Backups and merge | External-write guard refuses stale saves. Automatic backup/restore workflow not established; merge remains unimplemented. | 1.4a–c cover merge semantics, the merge engine and share receive through CLI and GUI (Working proposition); V.4a–b cover encrypted backup/restore/export (Expansion). |
| Attachments and custom fields | Ordinary edits preserve existing attachment/custom-string data. Management UI not established. | V.7 adds protected custom fields; V.8a–b add bounded attachment operations and GUI controls; 9.4 verifies compatibility. Expansion. |
| Tags, expiry, icons, references | Management workflows not established. | 5.4a sets share expiry, and E.1 and 1.4c refuse or warn on expired values (Working proposition); V.5a–b cover everyday tags/expiry, P.3a remaining metadata/settings and P.3b references/placeholders (Expansion). |
| Password health | Weak/reused/expired and compromised-password reports not established. | V.9 adds local findings and P.8 separately reviewed compromised-password checking, both in Expansion. |
| TOTP | No implemented user workflow. | 9.2a–b cover supported algorithm/digit/period combinations, CLI/GUI/MCP use; 8.3e covers browser filling. Expansion. |
| Keyfiles | Current frontend unlock does not expose them. | V.1a–b cover core/CLI/GUI create/open/change-credentials, independently of merge. Expansion. |
| Hardware-key protection | No implemented challenge-response workflow established. | P.1 covers explicitly supported hardware-key devices and interoperable vault operations. Expansion. |
| Quick unlock | Idle/manual locking exists; OS-assisted quick unlock not established. | 4.10a adds Windows and 4.10b macOS quick unlock; optional Expansion work. P.0/P.9 include claimed baseline coverage. |
| Import/export | Env import/export works; password-manager importers remain unimplemented. | 9.1a provides the pipeline; 9.1b/c/d1/d2/e cover Bitwarden JSON, LastPass CSV, 1Password 1PUX/CSV and KeePassXC CSV/KDBX onboarding; 9.1f provides GUI guidance. V.4b covers encrypted whole-vault export. Expansion. |
| Multiple vaults and auto-open | Recent-vault selection exists; simultaneous vaults/auto-open not established. | P.2 covers simultaneous vault identities, linked opening, scopes and lock behavior. Expansion. |
| Format variants/settings | KDBX4/AES-256/Argon2d writing and bidirectional fixtures exist. Broad cipher/KDF/keyfile/metadata coverage is not proved. | 9.4 publishes the compatibility result in Expansion; P.7a covers remaining cipher/KDF controls and P.7b remaining legacy format/import behavior. P.0/P.9 define and verify the complete baseline. |

Upstream password reports include weak, reused and expired credentials; their existence does not prescribe keypaste's scoring algorithm. [KeePassXC health-check description](https://keepassxc.org/blog/2020-08-15-keepassxc-password-healthcheck/). Upstream quick unlock depends on Windows/macOS hardware and setup; challenge-response protection is different from authenticating to an online account. [Getting Started](https://keepassxc.org/docs/KeePassXC_GettingStarted), [hardware-key FAQ](https://keepassxc.org/docs/).

## Integration and distribution coverage

| Family | Keypaste source status | Delivery rows / milestone |
|---|---|---|
| Browser fill, generation and save/update | No implemented/released extension. | 8.1 adds native-host pairing; 8.3a–e cover origin-bound fill, save, update, generation and TOTP/custom fields; 8.4a–b cover actual store distribution; 8.2b covers approval consistency across surfaces. Expansion. |
| Passkeys | No implemented workflow established. | 8.5a covers origin-bound creation; 8.5b authentication, lifecycle, portability and public versions. Expansion. |
| Auto-Type | No implemented workflow established. | P.4a covers Windows/macOS; P.4b the declared Linux display-server path. Expansion. |
| SSH integration | No implemented vault-backed SSH agent workflow. | 9.3a covers signing/protocol behavior; 9.3b selection, lock/unload and published CLI/GUI use. P.0 records differences from the upstream existing-agent design. Expansion. |
| Linux Secret Service | No implemented workflow established. | P.5 covers scoped client access, collection exposure and lock behavior. Expansion. |
| KeeShare-compatible sharing | No established KeeShare protocol workflow. | P.6 covers the reviewed interoperable path. Keypaste shares (5.4a–b), sealed and signed shares (7.1c) and team sharing (7.1b, 7.2) are distinct work, not KeeShare evidence. Expansion. |
| CLI/MCP distribution | CLI/MCP v0.1.0 is public on four targets; setup remains source-only. | R.0a–c and 3.8 cover version/platform contracts, complete immutable publication, provenance and the next verified CLI patch. Working proposition; independent of unfinished desktop work. |
| Desktop installation and updates | App builds from source; CI builds an internal unsigned Windows MSI and Linux AppImage; the publication path exists and refuses a package while it is unsigned; no public desktop installer. | 4.7a–d cover candidates, native installation and upgrades/recovery; 4.7c2 publishes, once there is an identity to sign with and a desktop worth installing (D-0219); 3.5a–b and 3.6b cover macOS/Windows signing, with the Windows identity as 4.7c2's input. R.1 verifies the developer journey. [RELEASE.md](RELEASE.md) owns platform/channel evidence. |
| Additional distribution | No Homebrew/Scoop/winget distribution or public binaries for the additional planned targets. | 3.7a–c cover package managers; 3.9a–d cover macOS Intel, Windows ARM64, Linux musl CLI/MCP and Linux ARM64 desktop. Expansion; each target requires separate evidence. |
| Mobile | No first-party client or custom-relay integration established. | M.1 proves the selected phone approach; M.2a–c deliver account/unlock, credential/autofill and sync/recovery/export workflows; M.3 verifies public phone availability. Expansion; no longer a Paid release precondition. |
| CLI, envs and agent access | CLI/env injection, MCP approval, policy and local audit implemented; setup is newer than v0.1.0. | 4.3a and 4.4 add the native approval dialog, and E.1 completes env use with expiry refusal (Working proposition); 4.3b and 4.4b add activity and approver lifecycle (Expansion). 6.1–2 cover separately proven external delegation views in Expansion. |
| Product guidance | Local guides exist; future GUI, hosted and phone instructions cannot yet be followed against released products. | 5.7 delivers share, receive and relay operator guides before R.1; 3.10a local guides and 3.10b hosted lifecycle guides are Expansion. |

KeePassXC's tagged browser documentation includes fill, generation, adding/updating credentials and extra fields; a read-only filler should therefore name its supported subset rather than claim parity. [Browser integration at 2.7.12](https://github.com/keepassxreboot/keepassxc/blob/2.7.12/docs/topics/BrowserIntegration.adoc). Its passkeys require browser integration. Auto-Type is separate, with an X11 limitation on Linux. Its SSH feature supplies keys to an existing agent; keypaste's planned agent is a different design. [User Guide](https://keepassxc.org/docs/KeePassXC_UserGuide).

KeePassXC distributes platform packages and browser-store extensions. It deliberately delegates cloud file sync to other services and recommends separate mobile clients; there is no official KeePassXC mobile app. KDBX access in those apps does not establish support for keypaste's future account, device or relay protocol. [Downloads](https://keepassxc.org/download/), [FAQ](https://keepassxc.org/docs/).

## Hosted and organization additions

PRODUCT v1.4 orders these additions beside desktop KeePassXC coverage. Hosted and organization workflows remain unimplemented and unreleased; their mappings identify the required delivery.

| User outcome | Keypaste source status | Delivery rows / milestone |
|---|---|---|
| Start without terminal/file expertise | Desktop creates and opens a vault; account/signup and managed storage onboarding are absent. | 4.8 complete in source, so a first vault needs no terminal once the desktop ships (Working proposition); H.1–H.2 settle and implement accounts for the team plan (Paid release); 9.1f import and 5.3b managed onboarding are Expansion. |
| Authorize and revoke devices | No hosted account MFA/session/device workflow. | H.3 adds MFA/session revocation and H.4 device authorization for team-plan accounts (Paid release); 5.3c device/sync controls and M.2a's phone client are Expansion. |
| Sync without exposing vault plaintext | Local KDBX editing exists; no relay/client sync implementation. | 5.2a–c build and publish the drop relay and H.7–H.8 host it (Working proposition); 5.2d adds authorized blob sync, 5.3a–c recoverable client sync and controls, and 5.3d the updated clients (Expansion). |
| Recover account or vault access | No hosted recovery workflow; account and vault recovery need separate authority. | H.5a defines what is recoverable and H.5b implements and rehearses it for team-plan accounts (Paid release); vault recovery stays local, and phone recovery follows in M.2c (Expansion). |
| Survive mistakes and outages | Existing local write guard refuses stale saves; backup, merge and hosted operations are unimplemented. | V.2a–b restore a changed value and 1.4b–c keep replaced revisions in history on receive (Working proposition); V.4a–b add backups (Expansion); H.9 proves restore by redeployment and H.10 establishes support/incident procedures before R.2's observed pilot. |
| Leave or stop paying | Local use is account-free; hosted deletion, export and payment behavior are absent. | H.6 adds account export/deletion and 5.5a–c cover payment enrollment, entitlement, cancellation and failure (Paid release); 3.10b documents the lifecycle (Expansion). Local access remains available. |
| Use a browser vault | No browser vault or proven runtime/core path. | W.1 proves the architecture; W.2a–c cover reviewed onboarding/unlock, editing/sync and public delivery. Expansion after the desktop-led pilot; not an implied R.1/R.2 feature. |
| Share an env set or chosen entries | No share, receive or relay workflow. | 5.4a creates a passphrase-protected KDBX4 share file, 5.4b carries it through a relay drop, 1.4a–c merge it on receive and E.1 refuses expired values; 5.2a–c, H.7–H.8 and 5.7 build, host and document the drop relay. Working proposition, verified by R.1. Hosted drops are free within PRODUCT §5.8's limits; a downloaded copy cannot be recalled, and revocation means rotating the credential. |
| Share work credentials | No organization-owned vault/collection or membership workflow. | 7.1a–b settle authority and build the hosted team directory, and 7.1c seals and signs shares with locally generated keys, free when it ships (Paid release priority, R.3); 7.1d desktop administration is Expansion, verified through R.4. |
| Approve team or service-account use | Existing local single-user approvals do not establish a team broker. | 7.2 adds revocable links and attributed shares and approvals under team audit and policy (Paid release, R.3); 7.4 adds review controls in Expansion, verified through R.4. |
| Administer a workforce | No organization SSO, provisioning or access-review implementation. | 7.3a–b implement OIDC and provisioning/deprovisioning; 7.4 adds access reviews/dashboard; 6.1–2 add external delegation coverage only where provider evidence supports it. R.4 verifies an organization pilot. |
| Trust hosted operation | Security policy and local checks exist; no operated/reviewed hosted service. | 5.7 and THREATS T-26 describe the drop boundary before R.1; H.9–H.10 cover operations and privacy/support before R.2; 10.2 independently reviews the hosted and team boundaries before R.3; R.4 requires review of organization boundaries. |
| Meet later enterprise operating needs | No managed fleet deployment, downstream rotation integration or enterprise assurance evidence. | S.1–S.5 cover measured capacity, fleet deployment, one provider credential lifecycle, support capacity and requested assurance in Scale; credentials already disclosed require provider-side rotation, not merely membership revocation. |

Organization roles and per-collection permissions are distinct controls in established password managers. They are useful models for the Teams design. [Bitwarden organizations](https://bitwarden.com/help/about-organizations/), [collection permissions](https://bitwarden.com/help/collection-permissions/). Recovery also needs an explicit authority model: an organization-controlled recovery key changes who can recover access, even when the provider cannot decrypt the vault. [Bitwarden recovery design](https://bitwarden.com/help/account-recovery/).

Vault permissions cannot erase a recipient's previous copy or make a revealed static password expire. Offboarding must distinguish refusing future downloads/broker requests, rotating vault keys, and rotating the actual downstream credential. Hidden fields are still shared credentials. [Bitwarden offboarding](https://bitwarden.com/help/onboarding-and-succession/), [permission limits](https://bitwarden.com/help/collection-permissions/).

Full PAM/IAM would additionally administer privileges in other systems, including temporary role activation and resource access reviews. That is a separate scope from an enterprise password vault. Integrate with identity providers first; do not promise the broader platform by implication. [Microsoft PIM scope](https://learn.microsoft.com/en-us/entra/id-governance/privileged-identity-management/pim-configure).

## Evidence required to close a gap

For each promoted feature, record its workflow, supported platforms, upstream baseline if applicable, Core/CLI/GUI/browser/mobile coverage, test evidence and first published version. Useful acceptance examples include:

- A new user downloads the app, creates a vault, saves an existing login, finds it, changes it, restores the prior value, then restores an accidentally deleted entry without a terminal.
- A KeePassXC fixture containing protected fields, attachments, history and duplicate titles survives an unrelated keypaste edit; unsupported operations refuse rather than discard data.
- Two offline clients edit different entries and converge; concurrent conflicting edits remain recoverable. Restore a downloaded encrypted backup on a fresh device.
- An offboarded teammate cannot fetch the next rotated snapshot or obtain a new broker release; the test explicitly acknowledges that a retained old snapshot remains decryptable with its old key.
- A developer shares an env set by link with the passphrase sent separately; the receiver previews and merges it, keypaste refuses to inject a value after its expiry, and the prior value is still in history.
- Browser tests cover hostile domain matches, new-account save, password update, locked state, TOTP/passkeys where claimed, and the supported OS/browser combinations.

These are acceptance examples for the mapped delivery rows. P.0 records every baseline behavior, including gaps this inventory missed; P.9 requires their public-version evidence. R.1/R.2/R.3/R.4 separately establish the working share-and-approve product, the small-team pilot, the paid team release and the organization pilot.
