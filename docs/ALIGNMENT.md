# Adopted product and delivery direction

Adopted 2026-09-07 through PRODUCT v1.2 and D-0090, with build dependencies separated from ship gates by v1.3 (D-0132): a complete password manager with reliable public releases, managed hosting for nontechnical users, environments and organization credentials. [PRODUCT](PRODUCT.md) owns this direction; [STEPS](STEPS.md) owns delivery and status. This page explains their relationship.

## Product direction

Daily use covers creating or importing a vault, saving and filling a login, updating it and recovering a mistake. Environment mapping and approved agent requests use the same vault through their own workflows.

Local use remains offline-capable and account-free. Managed onboarding lets a nontechnical user create an account and vault without choosing KDBX paths or operating a terminal. Storage can be managed for the user while ordinary KDBX export preserves ownership. Free contains the complete local password manager and self-hosted relay; paid plans sell managed hosting and organization capabilities. Pricing plans are separate from delivery milestones.

Full KeePassXC coverage is accepted work, with a dated behavior inventory in [FEATURES.md](FEATURES.md). Daily-use coverage comes first; advanced features remain explicit Expansion work that ships after the local product gate. The inventory must distinguish implementation, tested compatibility, platform coverage and first public availability before any completeness claim.

## Delivery structure

Following the sibling `notinferred` project's structure, STEPS starts with Current status and Build order, then gives each task a stable ID, Needs, Build and Verify. Task completion requires retained evidence, and each milestone keeps its full acceptance gate.

| STEPS milestone | User outcome and boundary |
|---|---|
| [Working proposition](STEPS.md#working-proposition) | A daily-use desktop password manager and browser extension, publicly distributed on the supported platforms, with integrated env and agent workflows and recovery from ordinary mistakes. |
| [Pilot ready](STEPS.md#pilot-ready) | Invited nontechnical users can create an account, enroll devices and use encrypted sync; operators can restore the service and users can recover through documented supported paths. |
| [Paid release](STEPS.md#paid-release) | Consumer hosting has passed the pilot, phone workflow and independent security-review gates; commercial, support and exit behavior are tested before charging. |
| [Expansion](STEPS.md#expansion) | Complete advanced KeePassXC coverage, shipping after the Working proposition gate. Organization credentials ship after the Pilot ready gate, and their pilot needs a selected scope. A full web vault builds on client/core feasibility work. |
| [Scale](STEPS.md#scale) | Increase operational capacity and reliability, verified against declared budgets. |

STEPS owns the exact build dependencies, ship gates and pickup rule; [CLAUDE.md](../CLAUDE.md#records) explains their use. Existing IDs and completed evidence remain traceable. Gates decide what ships, never what may be built, and unfinished behavior stays open.

## What the broader build includes

Publication is part of every release. [RELEASE.md](RELEASE.md) owns the platform matrix and procedures: package and sign, publish immutable downloads or store listings, verify public installation and upgrade/data preservation, and retain the evidence. Expiring workflow artifacts establish packaging only. The browser extension needs store distribution and native-host pairing; unsupported CPU/OS combinations need an explicit status. Marketing follows the release it describes.

Managed hosting is an account and device experience backed by encrypted sync. The plan covers enrollment/revocation, concurrency and conflict recovery, encrypted versions, backups and restore drills, offline use, account MFA, content-free operations, support, export/deletion and payment/cancellation. The initial hosted route is desktop plus extension. A supported phone workflow is required before broad paid consumer release; a full web vault follows feasibility and trust-boundary review. Existing KDBX mobile applications do not automatically support keypaste accounts and sync.

Account recovery is separate from vault recovery. Support cannot reconstruct a lost decryption secret. Recovery material, trusted-device transfer and organization authority require design and review against PRODUCT §3. A compromised origin can deliver malicious code to an unlocked web client despite client-side encryption, so browser work must first prove its trust boundary, core/runtime reuse and mature KDBX handling. D-0064's shared .NET relay and S3-compatible storage remain the service foundation.

Organization credentials extend beyond shared env sets. The plan covers organization-owned passwords, API credentials and other work secrets; personal/work separation; collections, roles, service accounts, approval policy, provisioning, audit and offboarding. SSO authenticates an account and does not inherently unlock a vault. Revoking membership stops future authorized access; it cannot erase a previously retained credential or snapshot. Downstream credential rotation needs tested provider integrations. A general identity or privileged-access platform remains outside this scope.

## Authoritative documents

| Owner | What changed or remains there |
|---|---|
| [PRODUCT](PRODUCT.md) | Audience, scope, commercial model and milestone intent; the security requirements in §3 remain fixed. |
| [STEPS](STEPS.md) | Actual current state, dependency order, bounded implementation prompts and release gates. |
| [FEATURES](FEATURES.md) | Dated KeePassXC comparison, implemented evidence and gaps linked to accepted work. |
| [RELEASE](RELEASE.md) | Supported distribution channels and the evidence required to call an app released and installable. |
| [DECISIONS](../DECISIONS.md) | D-0090 records why scope and ordering changed. Older ledger rows and linked frozen records retain the historical evidence. |
| [CLAUDE](../CLAUDE.md#records) | Ownership, change control and the exact build-plan workflow. |

README, the site and topic guides describe available behavior for named versions. CHANGELOG records significant delivered behavior, and STEPS records completion and remaining work.
