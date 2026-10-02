# Product rules

Last ratified: 2026-10-02 (v1.9, D-0400): after the first desktop release, keypaste's end state grows past one person's vault: an agent can use a credential it never holds, and teams share project secrets, CI identities and organization controls through an end-to-end relay that never reads a secret. v1.8's KeePass-first product and its track order still come first (D-0367). Earlier versions remain in Git. Scope changes update this document, ROADMAP, STEPS and the decision record together. The security laws in §3 remain fixed.

## 1. Product

**keypaste is a simple, local password manager on the KeePass file you already own. Logins and project secrets live as ordinary fields on ordinary entries, tagging an entry for a project makes its fields that project's environment, and an AI agent gets a secret only when you allow it.**

The first users already keep their passwords in KeePass or KeePassXC and write software; their API keys often sit in entry notes. The app is the main experience and should feel as simple as a consumer password manager: few places, plain words, sensible defaults, and advanced controls kept out of the main screens. Phones and file sync stay with the KeePass-compatible apps and sync services people already use. The CLI remains useful for explicit commands and automation.

The intended everyday journey is one unlock of the vault the person already has, a credential found and copied, an API key kept as a named field, its entry tagged into a project, the project run with its variables, an AI request approved or denied in the app, and a lock that stops further releases. This journey is the target: [FEATURES](FEATURES.md) owns what works today and [RELEASE](RELEASE.md) what people can install.

Credentials live in an ordinary KDBX file the person owns. Local use is free, open source and works without an account, network or subscription. Opening an existing supported KDBX is the migration path for KeePass users. “Like KeePassXC” describes familiar vault use and interoperability; it does not commit keypaste to every KeePassXC feature.

The end state extends the same model past one person. A local credential proxy lets an agent call an API with a credential it never holds. A team shares a project's secrets through a relay, on keypaste.com or the team's own server, that stores only ciphertext: each member's vault stays a local file, and only a project key travels, wrapped for each member. CI and deploy targets get identities the relay verifies, and syncs to hosting platforms are pushed from a member's machine, so no server decrypts. Organizations add single sign-on, provisioning, approval for production changes and audit export. None of this is required for local use.

Against hosted managers such as 1Password and LastPass, keypaste offers ownership of an ordinary file without an account or subscription; against Infisical and 1Password Environments, project secrets that are free, unlimited and local, and for a team a relay that cannot read what it stores; and before any agent receives a value, a person's answer recorded in a local audit.

The product commits to ten tracks. [ROADMAP](../ROADMAP.md) orders them into milestones and [STEPS](STEPS.md) holds their tasks:

- **T1 Everyday vault use and recovery:** credentials with custom fields and tags, organization, generation, search, copy/reveal, safe deletion, encrypted backups and vault access settings; keys left in notes flagged for review; a first run and main screens a KeePass user understands without learning keypaste's terms.
- **T2 One shared unlock session:** the app, MCP and environment launches use the same live vault state and lock boundary, and every process that saves the vault respects the one holding it.
- **T4 Project environments:** a project's environment is the env-named fields of the entries tagged for it; import a `.env` into fields, explicitly launch an app or terminal with them, and get the latest saved values on the next launch.
- **T3 AI requests in the app:** connect every common client in one step to the vault the person chose, see and answer a bounded request, and inspect the resulting audit record.
- **T5 Desktop delivery:** straightforward onboarding, signed Windows, macOS and Linux packages that include the CLI, installed-product verification, upgrade/recovery and a working feedback route, then package managers and agent marketplaces once a version is published.
- **T6 Daily driver**, after the first desktop release: quick unlock, TOTP, browser fill, merging a synced file's changes, importers from other password managers and local password health.
- **T7 Agents without values**, after the first desktop release: the process holding the vault attaches an approved credential to an agent's requests for the hosts its entry names, the agent holds only a placeholder, and the owner writes the audit record.
- **T8 Team projects:** a project's secrets shared through an end-to-end relay on keypaste.com or self-hosted; members identified by their own key pairs; roles for reading, writing and production; removing a member re-wraps the project key and prompts rotation; a record of who fetched what.
- **T9 CI and deploys:** machine identities the relay verifies, a GitHub Action, and syncs to hosting platforms pushed from a member's machine.
- **T10 Organizations:** single sign-on, SCIM provisioning, approval for production changes, audit export and a packaged self-hosted relay.

No track is complete merely because a screen, reader, protocol or package exists. The action that produces the result and the user's path to it must work together.

## 2. Scope boundaries

KDBX is the only vault format. Keypaste preserves data it does not expose for editing, rejects unsupported operations without destroying the original, and verifies writes against real KeePassXC. A compatible file does not establish support for every unlock method, field editor or integration. Support cannot reconstruct a lost vault secret.

A project's variables are env-named custom fields on ordinary entries, and an entry joins a project, or one of its environments, through its own KeePass tag: `env:<project>` or `env:<project>:<environment>`. A value lives once, however many environments use it. The one-entry-per-variable `env/<project>` layout of earlier releases stays readable, and keypaste writes no new variables in it. STEPS and the decision record own the exact grammar.

Security checks run locally and speak through a recommendations list outside the main screens. Keys found in notes are flagged for review, and nothing is moved or changed until the person confirms.

One unlocked session governs new secret releases in the desktop-led workflow. Manual or idle lock, an expired session after sleep, and app shutdown deny new MCP releases and environment launches, cancel pending approvals and clear reusable grants. Agent traffic cannot extend the human idle deadline. Changes saved in the app are visible to subsequent requests and launches. A changed file on disk must not silently produce stale credentials or overwrite somebody else's save. The session design must account explicitly for the existing standalone terminal approver and refuse ambiguous ownership; opening another process must not silently bypass the app's lock.

Unlocking does not itself run a command or approve an AI request. An environment launch is a user action for a named project and command. Values go into that child process's environment without creating a plaintext `.env` file or changing the machine's global environment. A terminal started this way exposes the values to programs it starts. Locking stops future releases; it cannot erase values already delivered to a process or MCP client, end their authenticated sessions, or revoke a credential at its issuer. TTL limits approval reuse, not retained copies. Stopping programs on lock is not part of the initial contract.

MCP remains bounded credential access: exposed names and one approved field, not arbitrary shell execution, bulk vault export or model-controlled vault administration. The master password is entered only in a user-initiated local unlock flow. The bridge remains vault-free. Existing user-written policies stay explicit, scoped and subordinate to the session lock. With T7, the owner may also attach one approved credential to an agent's HTTPS requests for the hosts its entry names, under a grant with the scope and lifetime the person approved (§3.2); the agent receives a placeholder, never the value.

The first desktop release targets Windows x64, macOS arm64 and Linux x64 while retaining the four published CLI/MCP targets, and it ships together with the next CLI/MCP release (D-0368). Every desktop package carries the CLI. Additional desktop platforms and channels require their own installation evidence and selection from [BACKLOG](BACKLOG.md).

Hardware-key vault unlocking is part of the product. The desktop unlocks such a vault in source (D-0366); creating one, the CLI and a run on a physical key remain for T6. It must use a KeePassXC-compatible approach, with supported devices/platforms and backup or lost-key limits verified before support is advertised. It is distinct from OS-assisted quick unlock such as Windows Hello or Touch ID, which T6 also commits.

Team projects over an end-to-end relay, machine identities and organization administration are committed to T8–T10, after the first desktop release. The relay is a new secret-handling implementation, so its protocol passes the architecture and security review of §4.3 before any of its code. Still uncommitted: syncing a person's whole vault through keypaste, sharing whole vault files, billing, phone and web vault clients, SSH, complete KeePassXC parity, and any service that could read a secret, such as server-side decryption, PKI, KMS, privileged-access management or dynamic secrets. Phones and the sync of a person's own vault are served by KeePass-compatible apps and ordinary file sync. Quick unlock, TOTP, browser fill, merging a synced file's changes and importers are committed to T6, after the first desktop release. BACKLOG retains the other options and the conditions for reconsidering them. Whether the hosted relay is paid is an open decision under §5.4; there is no promised service pricing or obligation to implement the backlog.

Amendment of 2026-09-25 (D-0357): share links that open a limited number of times and scoped tokens are in scope. `keypaste share` encrypts one field or an entry's login locally and keypaste.com holds only ciphertext it cannot open, serving it once `SHARE_ENABLED` is set; a scoped token is a pre-approval the person creates for `keypaste run`, with a scope and an expiry, verified and audited by the vault's owner. §3 applies to both.

Amendment of 2026-09-25 (D-0365): with `keypaste-mcp --allow-run`, MCP may also start one command the person approves, with the values in its environment and each value replaced in the output it returns (D-0358). This is not arbitrary shell execution: no shell starts it, and the person approves the exact program, arguments, directory, variable names and reason, or a grant of at most 15 minutes for that command line. Nothing enables `--allow-run` by default, and a command the agent can edit can still reveal a value (T-35).

Under v1.8, share links, scoped tokens and `--allow-run` remain in scope as advanced features, reached outside the main screens.

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
5. ROADMAP owns the order and milestones of committed work, STEPS its tasks and acceptance, and BACKLOG optional ideas without delivery dates or automatic promotion. A document edit or ready dependency is not authorization to start implementation.
6. The product is usable as a local desktop password manager without enabling MCP, creating a project environment or joining a team. Each integration is opt-in and explains what receives the secret.
7. Recovery requirements distinguish an earlier entry value, a deleted entry, a damaged vault file and a lost unlock secret. Prove each supported path separately; entry history is not a whole-vault backup.
8. Defaults over settings. A new user reaches a working vault, a project run and a connected agent without editing a configuration file or learning keypaste's internal terms. Advanced controls and security recommendations stay out of the main screens.

## 6. Decision tiebreakers

Apply these in order:

1. Reject work that risks user trust.
2. Prefer the shortest complete daily-use journey in the ten product tracks. The founder selects what is built next; ordering does not authorize execution.
3. Split work that one person cannot build and verify within two weeks into bounded children, preserving the parent's acceptance requirements.
4. Prefer demonstrable, tested outcomes with recovery from ordinary mistakes.
5. Prefer established approaches and focused, shippable work over novelty, perfection or breadth. An optional idea needs a concrete unmet need before becoming a commitment.
6. Where a question can be settled by a user rather than by more internal evidence, ship and ask. Tests on the secret, injection, sync and bridge paths are not subject to this rule (§4.5); documentation, ledger prose and acceptance narrative are. Record-keeping is proportional to the risk it retires, and a record that exists to describe another record is deleted, not maintained.
