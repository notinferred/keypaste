# Product rules

Product rules change only through dated re-ratification under explicit founder direction, recorded with a reason in DECISIONS and matching owner-document updates. The security laws in §3 remain fixed. Last ratified: 2026-09-15 (v1.4, D-0176); earlier versions remain in git. Editorial changes preserve these rules.

## 1. Product

keypaste is credential sharing through encrypted files, with environments and controlled agent access on the same KeePass-compatible vault for personal and work credentials.

A share is a KDBX4 file holding only the chosen entries or env set, protected by a generated six-word passphrase. It travels as a link through the relay while the passphrase goes by a second channel, and the receiver merges it into their vault by UUID under the merge semantics D-0157 records, keeping history. Sharing to a local file works without the relay (§4.1). A temporary share sets entry expiry: keypaste refuses to inject an expired value and warns when receiving one. A downloaded copy cannot be recalled, and revocation means rotating the credential; every description of expiry says so.

People should be able to create or import a vault, save and fill logins, update credentials, recover mistakes and move between devices without learning a terminal or managing file paths. Existing KeePass users retain direct access to ordinary KDBX files. Developers can map entries into environments and approve bounded agent requests within the same product. Organizations can own and govern shared passwords, API credentials and other work secrets.

The complete KeePassXC feature baseline is a tracked product objective. Daily-use workflows come first; advanced capabilities follow as explicit, verified work. [FEATURES.md](FEATURES.md) owns the dated baseline and gaps. KDBX compatibility, source implementation and complete feature coverage are different claims.

The product is freemium. Free is the whole local password manager and the self-hosted relay; paid plans sell managed hosting and organization capabilities. Sealing a share to a recipient's key and signing it are free (§5.4); the paid team plan sells the hosted directory, revocation, attribution, team audit and policy, and support. Commercial plans do not determine engineering milestones. [STEPS.md](STEPS.md) owns the build order:

1. Working proposition: a signed, published desktop app and CLI in which a developer creates a vault, stores an existing secret, keeps env sets, injects a project's env, shares an env set and receives one, approves an agent request in a native dialog, recovers an ordinary mistake and reads the audit, with the hosted drop relay live and self-hostable.
2. Pilot ready: invited small teams share production env through the hosted relay over an observed period, and operator restore is redeployment.
3. Paid release: the team plan, with accounts as its directory, billing, support and independent review of the hosted and team boundaries.
4. Expansion: whole-vault managed sync, browser filling, importers, phone approval, the remaining KeePassXC baseline and the OIDC/SCIM tier. These tracks progress alongside the release work as their build dependencies hold; their publication follows the Working proposition and Pilot ready gates, and an organization pilot also needs a selected pilot scope.
5. Scale: operational capacity and reliability work, verified against declared budgets.

## 2. Scope boundaries

KDBX is the only vault format. Hosted and self-hosted services store encrypted vault data and cannot unlock it. Account authentication and account recovery are separate from vault unlocking and vault recovery. Support cannot reconstruct a lost vault secret; any previously configured recovery or organization authority requires an explicit reviewed design consistent with §3.

Local vault creation, opening and use work offline without an account. Managed onboarding handles storage and sync for people who do not want to manage files; KDBX export preserves portability. Hosted and self-hosted relay deployments use the same binary, with self-hosting supported for individuals and organizations.

The relay's first job is drops: sealed bytes it cannot read under an unguessable ID, at most 1 MB and kept at most 7 days, optionally deleted on first download, with no accounts and rate-limited. It is one binary (D-0064), self-hostable, and free when hosted within those limits (§5.8). Version 1 shares are unsigned; the passphrase's separate channel is the sender check. A keypair is generated locally and a directory only publishes it: sealing a share to a recipient's key and signing it work with keys exchanged by any means, including a self-hosted relay, with no subscription. KDBX being the only vault format is why a share is a KDBX file.

Organization-owned credentials, access policy, provisioning integrations, audit and offboarding are in scope. Replacing an identity provider or promising universal downstream privilege control is outside this build plan. Credential rotation and temporary provider credentials need explicit integrations and separate verifiers. Work follows STEPS dependencies and milestone gates; preparing a later task cannot waive intervening user-experience, security or publication gates.

## 3. Security laws

1. The vault master key never leaves the local process, including through telemetry or an encrypted server backup. There are no exceptions.
2. Agents never get the vault. Each release contains one credential with one scope and TTL, following explicit human approval or a pre-approved policy the human wrote. Default is deny.
3. Every agent access has an immutable, local, human-readable log recording who or what requested which entry, when, and whether access was granted or denied.
4. Keypaste never writes secrets to disk unencrypted. Injection uses process environment memory only.
5. Analytics and telemetry never include secret content or entry names. Only opt-in anonymous usage counts are permitted.
6. Use the KDBX4 specification (Argon2, AES-256/ChaCha20) through mature audited libraries. Never write custom cryptography or modify the format.
7. Fail closed: every error path in the agent bridge denies access.
8. The code is open source and stays open, under the permissive or copyleft license decided once in docs/STEPS.md. Auditable code establishes trust in an unknown founder.
9. Dependencies are minimized and pinned. Every new dependency on the secret path requires written justification in the PR.
10. Provide a security policy, a private vulnerability-reporting contact and honest responses. Disclose breaches and serious shipped bugs promptly and fully.

## 4. Engineering laws

1. Local vault workflows work without a network or account. Managed sync adds convenience and device continuity while local access and export remain independent of it.
2. Vault, environment, policy and credential-release behavior lives in `Keypaste.Core`; CLI, desktop and integration surfaces are thin adapters. Guides and the feature inventory name differences in surface coverage.
3. CLI, GUI, MCP server and relay client share one core implementation. A web or mobile client must first prove a supported core/runtime path and mature KDBX handling under §3. Promising a browser port or introducing a second secret-handling implementation requires a recorded architecture and security review. Browser-delivered code is a separate trust boundary even when encryption runs in the client.
4. Cross-platform distribution for macOS, Linux and Windows, with supported architectures, browser channels and phone workflows explicitly recorded. Build success does not establish installation support. Every advertised platform must satisfy [RELEASE.md](RELEASE.md).
5. Tests are mandatory for code touching encryption, injection, sync or the agent bridge.
6. Every KDBX file keypaste writes must open correctly in KeePassXC, verified in CI against real KeePassXC.
7. Ship small releases with changelogs and semantic versions, preserving published artifacts. Release work includes packaging, public distribution, native installation and upgrade/data-preservation proof. Temporary CI artifacts establish packaging only.
8. Documentation ships with the feature, with one owner per fact and version-correct instructions. Distinguish implemented, packaged, published and installation-verified behavior.

## 5. Product laws

1. Each delivery slice produces a short user demo for marketing and retains the milestone's required acceptance evidence.
2. The founder uses the supported workflow daily before asking others to rely on it. New users must be able to create or migrate, use credentials and recover ordinary mistakes without developer assistance.
3. Publish and support the local product, then validate managed hosting with invited users before charging. Paid release requires the pilot gates and independent review of the hosted and team trust boundaries; a phone workflow is not a precondition. Marketing announcements follow the release they describe.
4. Free and self-hosted tiers are fully secure and fully functional. Paid tiers sell hosting, sync convenience, team features and support. Encryption and signatures are never paid upgrades.
5. STEPS is the founder's executable plan and owns active work and dependencies. Accepted product changes update PRODUCT, STEPS and the decision record together. Unaccepted ideas stay in DECISIONS.
6. Free includes the complete local password manager: CLI, app, agent bridge, browser extension, TOTP, SSH, importers and the self-hosted relay binary. Paid plans sell managed hosting and organization capabilities such as shared ownership, administrative policy and lifecycle management. Every plan must satisfy its full workflow and release gates.
7. Document what account recovery can restore and what vault recovery requires. Test backups, supported recovery paths, device revocation, export and deletion. Cancellation must preserve local vault access. Offboarding stops future authorized access; it cannot erase credentials or snapshots a person already retained.
8. Hosted drops are free within their limits: at most 1 MB per drop, kept at most 7 days, rate-limited and without an account. A subscription never gates sending or receiving a share within those limits.

## 6. Decision tiebreakers

Apply these in order:

1. Reject work that risks user trust.
2. Prefer required workflows and gates in the current STEPS milestone; later work follows its recorded build dependencies, and gates decide only what may ship.
3. Split work that one person cannot build and verify within two weeks into bounded children, preserving the parent's acceptance requirements.
4. Prefer demonstrable, tested outcomes with recovery from ordinary mistakes.
5. Prefer established approaches and focused, shippable work over novelty, perfection or breadth.
