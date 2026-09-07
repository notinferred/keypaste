# Adopted product and delivery direction

**Adopted 2026-09-07 through PRODUCT v1.2 and D-0090.** The founder requested a complete password manager, reliable public releases, managed hosting for nontechnical users, environments and organization credentials, with the build plan structured like the sibling `notinferred` project. That direction now lives in [PRODUCT.md](PRODUCT.md) and the rewritten [STEPS.md](STEPS.md). This page explains the change; it owns no status, acceptance checkboxes or competing queue.

## Product direction

**A password manager for personal and work credentials, with environments and controlled agent access built in.** The daily experience must cover creating or importing a vault, saving and filling a login, updating it and recovering a mistake. Environment mapping and approved agent requests use the same underlying vault rather than forcing every user through a developer workflow.

Local use remains offline-capable and account-free. Managed onboarding lets a nontechnical user create an account and vault without choosing KDBX paths or operating a terminal. Storage can be managed for the user while ordinary KDBX export preserves ownership. Free contains the complete local password manager and self-hosted relay; paid plans sell managed hosting and organization capabilities. Pricing plans are separate from delivery milestones.

Full KeePassXC coverage is accepted work, with a dated behavior inventory in [FEATURES.md](FEATURES.md). Daily-use coverage comes first; advanced features remain explicit Expansion work after the local product gate. The inventory must distinguish implementation, tested compatibility, platform coverage and first public availability before any completeness claim.

## Delivery structure

The useful `notinferred` model is now applied to the executable plan: **Current status**, **Build order**, then milestones containing individual stable IDs, **Needs**, **Build** and **Verify**. Completion requires retained evidence. A completed child or attractive early screen does not close the surrounding milestone.

| STEPS milestone | User outcome and boundary |
|---|---|
| [Working proposition](STEPS.md#working-proposition) | A daily-use desktop password manager and browser extension, publicly distributed on the supported platforms, with integrated env and agent workflows and recovery from ordinary mistakes. |
| [Pilot ready](STEPS.md#pilot-ready) | Invited nontechnical users can create an account, enroll devices and use encrypted sync; operators can restore the service and users can recover through documented supported paths. |
| [Paid release](STEPS.md#paid-release) | Consumer hosting has passed the pilot, phone workflow and independent security-review gates; commercial, support and exit behavior are tested before charging. |
| [Expansion](STEPS.md#expansion) | Complete advanced KeePassXC coverage after the Working proposition gate. Organization credential work follows the Pilot ready gate and a selected pilot scope. A full web vault follows client/core feasibility work. |
| [Scale](STEPS.md#scale) | Increase operational capacity and reliability when recorded load, reliability or workload evidence activates the task. |

STEPS owns the exact prerequisites and pickup rule. [CLAUDE.md](../CLAUDE.md#records) explains how to apply them. Later preparation cannot bypass a release gate. The new plan preserves existing IDs and completed evidence, separates partially built work from the remaining behavior, and keeps new cloud and enterprise functionality visibly unbuilt.

## What the broader build includes

**Publication is part of every release.** [RELEASE.md](RELEASE.md) owns the platform matrix and procedures: package and sign, publish immutable downloads or store listings, verify public installation and upgrade/data preservation, and retain the evidence. Expiring workflow artifacts establish packaging only. The browser extension needs store distribution and native-host pairing; unsupported CPU/OS combinations need an explicit status. Marketing follows the release it describes.

**Managed hosting is an account and device experience backed by encrypted sync.** The plan covers enrollment/revocation, concurrency and conflict recovery, encrypted versions, backups and restore drills, offline use, account MFA, content-free operations, support, export/deletion and payment/cancellation. The initial hosted route is desktop plus extension. A supported phone workflow is required before broad paid consumer release; a full web vault follows feasibility and trust-boundary review. Existing KDBX mobile applications do not automatically support keypaste accounts and sync.

**Account recovery is separate from vault recovery.** Support cannot manufacture a lost decryption secret. Any recovery material, trusted-device transfer or organization authority needs an explicit design and review against the unchanged PRODUCT §3. Client-side encryption does not remove the risk of malicious code delivered to an unlocked web client. Client/runtime reuse and mature KDBX handling must be demonstrated before a browser implementation is promised. D-0064's shared .NET relay and S3-compatible storage remain the chosen service foundation; this direction does not select a replacement cloud stack.

**Organization credentials extend beyond shared env sets.** The plan covers organization-owned passwords, API credentials and other work secrets; personal/work separation; collections, roles, service accounts, approval policy, provisioning, audit and offboarding. SSO authenticates an account and does not inherently unlock a vault. Revoking membership stops future authorized access; it cannot erase a previously retained credential or snapshot. Downstream credential rotation needs tested provider integrations. A general identity or privileged-access platform remains outside this scope.

## Authoritative documents

| Owner | What changed or remains there |
|---|---|
| [PRODUCT](PRODUCT.md) | Adopted audience, product boundaries, commercial shape and milestone intent. §3 remains byte-for-byte unchanged. |
| [STEPS](STEPS.md) | Actual current state, dependency order, bounded implementation prompts and release gates. |
| [FEATURES](FEATURES.md) | Dated KeePassXC comparison, implemented evidence and gaps linked to accepted work. |
| [RELEASE](RELEASE.md) | Supported distribution channels and the evidence required to call an app released and installable. |
| [DECISIONS](../DECISIONS.md) | D-0090 records why the scope and ordering changed. Older ledger rows and the frozen archive remain historical evidence. |
| [CLAUDE](../CLAUDE.md#records) | Ownership, change control and the exact build-plan workflow. |

README, the site and topic guides continue to describe available, version-correct behavior. Adopting the plan does not advertise cloud, desktop or enterprise features as already released. Significant delivered behavior belongs in CHANGELOG; completion and remaining work belong in STEPS.
