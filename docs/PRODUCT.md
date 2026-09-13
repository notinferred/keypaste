# Product rules

Product rules change only through dated re-ratification under explicit founder direction, recorded with a reason in DECISIONS and matching owner-document updates. The security laws in §3 remain fixed. Last ratified: 2026-09-13 (v1.3, D-0132); earlier versions remain in git. Editorial changes preserve these rules.

## 1. Product

keypaste is a KeePass-compatible password manager for personal and work credentials, with environments and controlled agent access built in.

People should be able to create or import a vault, save and fill logins, update credentials, recover mistakes and move between devices without learning a terminal or managing file paths. Existing KeePass users retain direct access to ordinary KDBX files. Developers can map entries into environments and approve bounded agent requests within the same product. Organizations can own and govern shared passwords, API credentials and other work secrets.

The complete KeePassXC feature baseline is a tracked product objective. Daily-use workflows come first; advanced capabilities follow as explicit, verified work. [FEATURES.md](FEATURES.md) owns the dated baseline and gaps. KDBX compatibility, source implementation and complete feature coverage are different claims.

The product is freemium. Free is the whole local password manager and the self-hosted relay; paid plans sell managed hosting and organization capabilities. Commercial plans do not determine engineering milestones. [STEPS.md](STEPS.md) owns the build order:

1. Working proposition: a publicly released daily-use desktop password manager and browser extension, with environment and agent workflows integrated.
2. Pilot ready: managed encrypted sync and account/device operations that invited nontechnical users can complete and operators can restore.
3. Paid release: a validated consumer hosting experience, a supported phone workflow, reviewed trust boundaries and complete commercial/support operations.
4. Expansion: complete the remaining accepted KeePassXC baseline and build organization credential management. These tracks progress alongside the release work as their build dependencies hold; their publication follows the Working proposition and Pilot ready gates, and an organization pilot also needs a selected pilot scope.
5. Scale: operational capacity and reliability work, verified against declared budgets.

## 2. Scope boundaries

KDBX is the only vault format. Hosted and self-hosted services store encrypted vault data and cannot unlock it. Account authentication and account recovery are separate from vault unlocking and vault recovery. Support cannot reconstruct a lost vault secret; any previously configured recovery or organization authority requires an explicit reviewed design consistent with §3.

Local vault creation, opening and use work offline without an account. Managed onboarding handles storage and sync for people who do not want to manage files; KDBX export preserves portability. Hosted and self-hosted relay deployments use the same binary, with self-hosting supported for individuals and organizations.

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
3. Publish and support the local product, then validate managed hosting with invited users before charging. Paid consumer release requires the pilot gates, a supported phone workflow and independent review of the new hosted/client trust boundaries. Marketing announcements follow the release they describe.
4. Free and self-hosted tiers are fully secure and fully functional. Paid tiers sell hosting, sync convenience, team features and support. Encryption and signatures are never paid upgrades.
5. STEPS is the founder's executable plan and owns active work and dependencies. Accepted product changes update PRODUCT, STEPS and the decision record together. Unaccepted ideas stay in DECISIONS.
6. Free includes the complete local password manager: CLI, app, agent bridge, browser extension, TOTP, SSH, importers and the self-hosted relay binary. Paid plans sell managed hosting and organization capabilities such as shared ownership, administrative policy and lifecycle management. Every plan must satisfy its full workflow and release gates.
7. Document what account recovery can restore and what vault recovery requires. Test backups, supported recovery paths, device revocation, export and deletion. Cancellation must preserve local vault access. Offboarding stops future authorized access; it cannot erase credentials or snapshots a person already retained.

## 6. Decision tiebreakers

Apply these in order:

1. Reject work that risks user trust.
2. Prefer required workflows and gates in the current STEPS milestone; later work follows its recorded build dependencies, and gates decide only what may ship.
3. Split work that one person cannot build and verify within two weeks into bounded children, preserving the parent's acceptance requirements.
4. Prefer demonstrable, tested outcomes with recovery from ordinary mistakes.
5. Prefer established approaches and focused, shippable work over novelty, perfection or breadth.
