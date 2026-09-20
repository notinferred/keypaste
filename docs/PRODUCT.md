# Product rules

Last ratified: 2026-09-19 (v1.7, D-0245), retaining the focused plan and restoring hardware-key vault unlocking as committed later product work. Earlier versions remain in Git. Scope changes update this document, STEPS and the decision record together. The security laws in §3 remain fixed.

## 1. Product

**keypaste is a local, KeePass-compatible password manager whose unlocked session lets you use project environments and approve AI credential requests.**

The primary user wants a familiar desktop password manager and also works with developer tools or AI. The app is the main experience: create or open a vault, save and organize credentials, find and use a password, recover a mistake, and lock. Project environments and MCP extend that same experience. The CLI remains useful for explicit commands and automation.

The intended everyday journey is one desktop unlock, a credential saved or changed, a project launched with its variables, an AI request approved or denied in the app, and a lock that stops further access. This is the target, not a claim about today's separate desktop, terminal approver and runner. [FEATURES](FEATURES.md) owns the implementation inventory; [RELEASE](RELEASE.md) owns what people can install.

Credentials live in an ordinary KDBX file the person owns. Local use is free, open source and works without an account, network or subscription. Opening an existing supported KDBX is the initial migration path. “Like KeePassXC” describes familiar vault use and interoperability; it does not commit keypaste to every KeePassXC feature.

[STEPS](STEPS.md) orders five product tracks:

1. Everyday vault use and recovery: credentials, organization, generation, search, copy/reveal, safe deletion, encrypted backups and vault access settings, with hardware-key unlocking as a later extension.
2. One shared unlock session: the app, MCP and environment launches use the same live vault state and lock boundary.
3. AI requests in the app: connect a client, see and answer a bounded request, and inspect the resulting audit record.
4. Project environments: import and edit an env set, explicitly launch an app or terminal with it, and get the latest saved values on the next launch.
5. Desktop delivery: straightforward onboarding, signed distribution where required, installed-product verification, upgrade/recovery and a working feedback route.

No track is complete merely because a screen, reader, protocol or package exists. The action that produces the result and the user's path to it must work together.

## 2. Scope boundaries

KDBX is the only vault format. Keypaste preserves data it does not expose for editing, rejects unsupported operations without destroying the original, and verifies writes against real KeePassXC. A compatible file does not establish support for every unlock method, field editor or integration. Support cannot reconstruct a lost vault secret.

One unlocked session governs new secret releases in the desktop-led workflow. Manual or idle lock, an expired session after sleep, and app shutdown deny new MCP releases and environment launches, cancel pending approvals and clear reusable grants. Agent traffic cannot extend the human idle deadline. Changes saved in the app are visible to subsequent requests and launches. A changed file on disk must not silently produce stale credentials or overwrite somebody else's save. The session design must account explicitly for the existing standalone terminal approver and refuse ambiguous ownership; opening another process must not silently bypass the app's lock.

Unlocking does not itself run a command or approve an AI request. An environment launch is a user action for a named project and command. Values go into that child process's environment without creating a plaintext `.env` file or changing the machine's global environment. A terminal started this way exposes the values to programs it starts. Locking stops future releases; it cannot erase values already delivered to a process or MCP client, end their authenticated sessions, or revoke a credential at its issuer. TTL limits approval reuse, not retained copies. Stopping programs on lock is not part of the initial contract.

MCP remains bounded credential access: exposed names and one approved field, not arbitrary shell execution, bulk vault export or model-controlled vault administration. The master password is entered only in a user-initiated local unlock flow. The bridge remains vault-free. Existing user-written policies stay explicit, scoped and subordinate to the session lock.

The first desktop release targets Windows x64 and Linux x64 while retaining the four published CLI/MCP targets. Existing macOS desktop build artifacts are not a public release. Additional desktop platforms and channels require their own installation evidence and selection from [BACKLOG](BACKLOG.md).

Hardware-key vault unlocking is part of the product, planned after the first integrated desktop release under T1. It must use a KeePassXC-compatible approach, with supported devices/platforms and backup or lost-key limits verified before support is advertised. It is distinct from OS-assisted quick unlock such as Windows Hello or Touch ID, which remains an optional feature.

Sharing and merge, hosted services and sync, accounts and billing, team administration, browser filling, phone and web clients, TOTP, SSH and complete KeePassXC parity are not committed delivery tracks. BACKLOG retains useful options and the conditions for reconsidering them. There is no paid-release or enterprise milestone, promised service pricing, or obligation to implement the backlog.

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

1. Local vault workflows work without a network or account. File ownership and export preserve portability.
2. Vault, environment, policy and credential-release behavior lives in `Keypaste.Core`; CLI, desktop and integration surfaces are thin adapters. Guides and the feature inventory name differences in surface coverage.
3. CLI, GUI and MCP share one core implementation. A shared library alone does not establish a shared live session. Another secret-handling implementation requires an architecture and security review before entering the plan.
4. Every advertised platform must satisfy [RELEASE](RELEASE.md). Build success, source compatibility and a package's presence do not establish installation support.
5. Tests are mandatory for code touching encryption, injection, sync or the agent bridge.
6. Every KDBX file keypaste writes must open correctly in KeePassXC, verified in CI against real KeePassXC.
7. Ship small releases with changelogs and semantic versions, preserving published artifacts. Release work includes packaging, public distribution, native installation and upgrade/data-preservation proof. Temporary CI artifacts establish packaging only.
8. Documentation ships with the feature, with one owner per fact and version-correct instructions. Distinguish implemented, packaged, published and installation-verified behavior.

## 5. Product laws

1. Each delivered workflow has a reproducible user demonstration and evidence for its advertised behavior. An audit viewer, generated fixture or successful parse cannot prove the corresponding real action occurred.
2. The founder uses the supported workflow before asking others to rely on it. A new user can create or open a supported vault, use credentials and recover ordinary mistakes without developer assistance.
3. Public claims follow verified releases and name their limitations. Provide downloads, usage instructions and a route for defects and security reports. An announcement campaign, GitHub Release mirror or package-manager submission is not a condition of product correctness or every task's completion; publication and sending messages require their own authorization.
4. Local functions and their security are free. No security feature, encryption or signature is a paid upgrade. A commercial service requires a new product decision rather than inheriting the previous tier plan.
5. STEPS owns committed work, status and acceptance. BACKLOG owns optional ideas without delivery dates or automatic promotion. A document edit or ready dependency is not authorization to start implementation.
6. The product is usable as a local desktop password manager without enabling MCP or creating a project environment. Each integration is opt-in and explains what receives the secret.
7. Recovery requirements distinguish an earlier entry value, a deleted entry, a damaged vault file and a lost unlock secret. Prove each supported path separately; entry history is not a whole-vault backup.

## 6. Decision tiebreakers

Apply these in order:

1. Reject work that risks user trust.
2. Prefer the shortest complete daily-use journey in the five product tracks. The founder selects what is built next; ordering does not authorize execution.
3. Split work that one person cannot build and verify within two weeks into bounded children, preserving the parent's acceptance requirements.
4. Prefer demonstrable, tested outcomes with recovery from ordinary mistakes.
5. Prefer established approaches and focused, shippable work over novelty, perfection or breadth. An optional idea needs a concrete unmet need before becoming a commitment.
6. Where a question can be settled by a user rather than by more internal evidence, ship and ask. Tests on the secret, injection, sync and bridge paths are not subject to this rule (§4.5); documentation, ledger prose and acceptance narrative are. Record-keeping is proportional to the risk it retires, and a record that exists to describe another record is deleted, not maintained.
