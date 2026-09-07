# PRODUCT.md — The keypaste Constitution
> **This file changes only by a dated re-ratification.** §3 does not change at all. The other sections may be amended under explicit founder direction, with a date and a `D-` row in `DECISIONS.md` saying why. Update the owning documents together; a decision that conflicts with the current text does not silently override it. Last ratified: **2026-09-07 (v1.2, D-0090)**. Earlier versions remain in git. §3 is byte-identical to v1.1.

---

## 1. What keypaste IS

**keypaste is a KeePass-compatible password manager for personal and work credentials, with environments and controlled agent access built in.**

One sentence pitch: *"Your logins, API keys and environments in a vault you own, available locally or through managed sync, with explicit control over what an app or AI agent can use."*

People should be able to create or import a vault, save and fill logins, update credentials, recover mistakes and move between devices without learning a terminal or managing file paths. Existing KeePass users retain direct access to ordinary KDBX files. Developers can map entries into environments and approve bounded agent requests within the same product. Organizations can own and govern shared passwords, API credentials and other work secrets.

The complete KeePassXC feature baseline is a tracked product objective. Daily-use workflows come first; advanced capabilities follow as explicit, verified work. [FEATURES.md](FEATURES.md) owns the dated baseline and gaps. KDBX compatibility, source implementation and complete feature coverage are different claims.

The product is **freemium**. Free is the whole local password manager and the self-hosted relay; paid plans sell managed hosting and organization capabilities. Commercial plans do not determine engineering milestones. [STEPS.md](STEPS.md) owns the build order:

1. **Working proposition:** a publicly released daily-use desktop password manager and browser extension, with environment and agent workflows integrated.
2. **Pilot ready:** managed encrypted sync and account/device operations that invited nontechnical users can complete and operators can restore.
3. **Paid release:** a validated consumer hosting experience, a supported phone workflow, reviewed trust boundaries and complete commercial/support operations.
4. **Expansion:** complete the remaining accepted KeePassXC baseline after the Working proposition gate; begin organization credential management after the Pilot ready gate and selection of a pilot scope. These tracks can progress alongside the later release work when their dependencies hold.
5. **Scale:** operational capacity and reliability work activated by measured demands.

## 2. What keypaste is NOT (the walls that remain)

- **NOT** a new proprietary vault format. KDBX or nothing.
- **NOT** a cloud service that can read your secrets. Hosted and self-hosted services store encrypted vault data and cannot unlock it. Account authentication and account recovery are separate from vault unlocking and vault recovery. Support cannot reconstruct a lost vault secret; any previously configured recovery or organization authority requires an explicit reviewed design consistent with §3.
- **NOT** an account requirement for local use. Create/open a vault and use the local product offline without signing up. Managed onboarding handles storage and sync for people who do not want to manage files; KDBX export preserves portability.
- **NOT** a proprietary hosted-only service. Hosted and self-hosted relay deployments use the same binary; self-hosting remains a supported option for individuals and organizations.
- **NOT** a general identity or privileged-access platform. Organization-owned credentials, access policy, provisioning integrations, audit and offboarding are in scope. Replacing an identity provider or promising universal downstream privilege control is outside this build plan. Credential rotation and temporary provider credentials need explicit integrations and separate verifiers.
- **NOT** everything in one release. Work follows the dependencies and milestone gates in STEPS. Earlier preparation of a named later task does not waive the intervening user-experience, security or publication gates.

## 3. Security laws (violating any of these kills the project's only asset: trust)

1. **The vault master key never leaves the local process.** No exceptions, no telemetry of it, no "encrypted backup to our servers."
2. **Agents NEVER get the vault.** Agents get: one credential, one scope, one TTL, after one explicit human approval (or a pre-approved policy the human wrote). Default is deny.
3. **Every agent access is logged** — immutably, locally, human-readable: who/what, which entry, when, granted/denied.
4. **No secret ever touches disk unencrypted** by keypaste's doing. Injection is into process environment memory, not into files.
5. **No analytics/telemetry on secret content or entry names. Ever.** Opt-in anonymous usage counts only.
6. **All crypto is boring.** Use the KDBX4 spec (Argon2, AES-256/ChaCha20) via mature audited libraries. NEVER write custom crypto. NEVER "improve" the format.
7. **Fail closed.** Any error path in the agent bridge results in denial, not exposure.
8. **The code is open source (permissive or copyleft — decided once, in docs/STEPS.md) and stays open.** Auditable code is the trust strategy for an unknown founder.
9. **Dependencies are minimized and pinned.** Every new dependency on the secret path requires written justification in the PR.
10. **Vulnerability reports get a security policy, a private contact, and honesty.** If breached or a serious bug ships, disclose fast and fully.

## 4. Engineering laws

1. **Local-first, offline-capable.** Local vault workflows work with no network or account. Managed sync adds convenience and device continuity; it is never a prerequisite for local access or export.
2. **Core-first.** Vault, environment, policy and credential-release behavior lives in `Keypaste.Core`; CLI, desktop and integration surfaces are thin adapters. A feature available in one surface and absent in another says so in its guide and feature inventory.
3. **One shared domain implementation.** CLI, GUI, MCP server and relay client reuse the core. A web or mobile client must first prove a supported core/runtime path and mature KDBX handling under §3. Do not promise a browser port or introduce a second secret-handling implementation without a recorded architecture and security review. Browser-delivered code is a separate trust boundary even when encryption runs in the client.
4. **Cross-platform distribution** for macOS, Linux and Windows, with supported architectures, browser channels and phone workflows explicitly recorded. Build success does not establish installation support. Every advertised platform must satisfy [RELEASE.md](RELEASE.md).
5. **Tests on the secret path are mandatory.** No untested code touches encryption, injection, sync, or the agent bridge.
6. **Compatibility is sacred:** any KDBX file keypaste writes must open correctly in KeePassXC. This is tested in CI against real KeePassXC.
7. **Small releases, real changelogs, semantic versioning.** Preserve published artifacts. Packaging, public distribution, native installation and upgrade/data-preservation proof are part of release work; a temporary CI artifact is not a released app.
8. **Documentation ships with the feature**, with one owner per fact and version-correct instructions. Distinguish implemented, packaged, published and installation-verified behavior.

## 5. Product laws

1. **The demo is the marketing.** Each delivery slice produces a short, demonstrable user outcome. A demo does not substitute for the milestone's acceptance evidence.
2. **Daily use precedes public reliance.** The founder uses the supported workflow daily before asking others to rely on it. New users must be able to create or migrate, use credentials and recover ordinary mistakes without developer assistance.
3. **Validated use before payment.** Publish and support the local product, then validate managed hosting with invited users. Paid consumer release requires the pilot gates, a supported phone workflow and independent review of the new hosted/client trust boundaries. Marketing announcements follow the release they describe.
4. **Monetize the convenience, never the security.** Free/self-host tier is fully secure and fully functional. Paid tiers sell hosting, sync convenience, team features, support — never "more encryption", and never a signature the free binary lacks.
5. **One founder, one executable plan.** STEPS owns the active work and its dependencies. Accepted product changes update PRODUCT, STEPS and the decision record together. Unaccepted ideas stay in DECISIONS; a strategy note is not a second queue.
6. **Free is the whole local password manager.** CLI, app, agent bridge, browser extension, TOTP, SSH, importers and the relay binary to run yourself. Paid is managed hosting and organization capabilities such as shared ownership, administrative policy and lifecycle management. Plan labels do not excuse an incomplete Free workflow or alter the release gates.
7. **Recovery and exit are product features.** Show what account recovery can restore and what vault recovery requires. Test backups, supported recovery paths, device revocation, export and deletion. Cancellation must preserve access to a user's local vault. Offboarding stops future authorized access; it cannot erase credentials or snapshots a person already retained.

## 6. Decision tiebreakers (when stuck, in order)

1. Does it protect user trust? → if it risks trust, no.
2. Does it close a required user workflow or gate in the current STEPS milestone? → prefer that work; later work follows its recorded dependencies and activation conditions.
3. Can one person build and verify the task in ≤2 weeks? → if not, split it into bounded children without dropping the parent's acceptance requirements.
4. Can a user demonstrate the outcome and recover from an ordinary mistake? → prefer the option with clearer, tested behavior.
5. Boring beats clever. Shipped beats perfect. Focused beats big.

---
*The vault remains portable, local use remains account-free, and the service cannot read secrets. A managed experience may hide file management without taking ownership away from the user. Scope changes require the dated decision and owner updates above; §3 remains unchanged.*
