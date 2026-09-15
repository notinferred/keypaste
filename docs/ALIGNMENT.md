# Adopted product and delivery direction

Adopted 2026-09-15 through PRODUCT v1.4 and D-0176, keeping v1.3's separation of build dependencies from ship gates (D-0132): credential sharing through encrypted KDBX files with controlled agent access on the same vault, a free hosted drop relay, and a paid team plan. [PRODUCT](PRODUCT.md) owns this direction; [STEPS](STEPS.md) owns delivery and status. This page explains their relationship.

## Product direction

A developer keeps credentials and env sets in a local vault, injects a project's env, approves agent requests, and shares an env set or chosen entries as an encrypted KDBX file that the receiver merges into their own vault. The passphrase travels separately from the link, expiry is enforced where keypaste injects and receives, and a downloaded copy cannot be recalled, so revocation means rotating the credential.

Local use remains offline-capable and account-free, and sharing to a file needs no relay. Hosted drops are free within their limits, and sealing to a recipient key and signing are free whenever they ship. Free contains the complete local password manager and self-hosted relay; paid plans sell managed hosting and organization capabilities, and the team plan sells the hosted directory, revocation, attribution, team audit and policy, and support. Pricing plans are separate from delivery milestones.

Full KeePassXC coverage is accepted work, with a dated behavior inventory in [FEATURES.md](FEATURES.md). It is explicit Expansion work that ships after the local product gate. The inventory must distinguish implementation, tested compatibility, platform coverage and first public availability before any completeness claim.

## Delivery structure

Following the sibling `notinferred` project's structure, STEPS starts with Current status and Build order, then gives each task a stable ID, Needs, Build and Verify. Task completion requires retained evidence, and each milestone keeps its full acceptance gate.

| STEPS milestone | User outcome and boundary |
|---|---|
| [Working proposition](STEPS.md#working-proposition) | A signed, published desktop app and CLI in which a developer creates a vault, stores an existing secret, keeps and injects env, shares an env set and receives one, approves an agent in a native dialog, recovers a mistake and reads the audit, with the hosted drop relay live and self-hostable. |
| [Pilot ready](STEPS.md#pilot-ready) | Invited small teams share production env through the hosted relay over an observed period; operators restore the service by redeploying it. |
| [Paid release](STEPS.md#paid-release) | The team plan: accounts as the hosted directory, revocation, attribution, team audit and policy, with billing, support and independent review of the hosted and team boundaries. |
| [Expansion](STEPS.md#expansion) | Whole-vault managed sync, browser filling, importers, phone approval, the remaining KeePassXC baseline and the OIDC/SCIM tier. A full web vault builds on client/core feasibility work, and an organization pilot needs a selected scope. |
| [Scale](STEPS.md#scale) | Increase operational capacity and reliability, verified against declared budgets. |

STEPS owns the exact build dependencies, ship gates and pickup rule; [CLAUDE.md](../CLAUDE.md#records) explains their use. Existing IDs and completed evidence remain traceable. Gates decide what ships, never what may be built, and unfinished behavior stays open.

## What the broader build includes

Publication is part of every release. [RELEASE.md](RELEASE.md) owns the platform matrix and procedures: package and sign, publish immutable downloads or store listings, verify public installation and upgrade/data preservation, and retain the evidence. Expiring workflow artifacts establish packaging only. The browser extension needs store distribution and native-host pairing; unsupported CPU/OS combinations need an explicit status. Marketing follows the release it describes.

The first hosted service is the drop relay: sealed bytes it cannot read, under an unguessable ID, at most 1 MB and 7 days, optionally deleted on first download, with no accounts and rate limits, from the same binary operators self-host. Its pilot restores by redeployment. Accounts arrive with the team plan as its directory, with MFA, device keys, recovery, export/deletion and payment/cancellation. Whole-vault managed sync, with enrollment, conflict recovery, encrypted versions and backups, is Expansion work, as are a phone workflow and a full web vault, which follows feasibility and trust-boundary review. Existing KDBX mobile applications do not automatically support keypaste accounts and sync.

Account recovery is separate from vault recovery. Support cannot reconstruct a lost decryption secret. Recovery material, trusted-device transfer and organization authority require design and review against PRODUCT §3. A compromised origin can deliver malicious code to an unlocked web client despite client-side encryption, so browser work must first prove its trust boundary, core/runtime reuse and mature KDBX handling. D-0064's shared .NET relay and S3-compatible storage remain the service foundation.

The team plan governs shares: a hosted directory publishing locally generated keys, revocable links, attributed shares, team audit and policy. Organization credentials extend beyond shared env sets. The plan covers organization-owned passwords, API credentials and other work secrets; personal/work separation; collections, roles, service accounts, approval policy, provisioning, audit and offboarding. SSO authenticates an account and does not inherently unlock a vault. Revoking membership stops future authorized access; it cannot erase a previously retained credential or snapshot. Downstream credential rotation needs tested provider integrations. A general identity or privileged-access platform remains outside this scope.

## Authoritative documents

| Owner | What changed or remains there |
|---|---|
| [PRODUCT](PRODUCT.md) | Audience, scope, commercial model and milestone intent; the security requirements in §3 remain fixed. |
| [STEPS](STEPS.md) | Actual current state, dependency order, bounded implementation prompts and release gates. |
| [FEATURES](FEATURES.md) | Dated KeePassXC comparison, implemented evidence and gaps linked to accepted work. |
| [RELEASE](RELEASE.md) | Supported distribution channels and the evidence required to call an app released and installable. |
| [DECISIONS](../DECISIONS.md) | D-0176 records why scope and ordering changed to sharing first, following D-0090 and D-0132. Older ledger rows and linked frozen records retain the historical evidence. |
| [CLAUDE](../CLAUDE.md#records) | Ownership, change control and the exact build-plan workflow. |

README, the site and topic guides describe available behavior for named versions. CHANGELOG records significant delivered behavior, and STEPS records completion and remaining work.
