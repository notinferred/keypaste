# Roadmap

This file owns the order of the work and its milestones. [PRODUCT](docs/PRODUCT.md) owns the product statement, scope and laws. [STEPS](docs/STEPS.md) owns each task's dependencies and acceptance. [BACKLOG](docs/BACKLOG.md) holds ideas no milestone includes.

A milestone names the tasks it needs by ID. It promises no date, and nothing here authorizes publication or messages.

## Where things stand

The public release is CLI/MCP `0.3.0` ([RELEASE](docs/RELEASE.md)). The desktop app works from source, with internal, unsigned packages for Windows, macOS and Linux and no public release. [FEATURES](docs/FEATURES.md) owns what works today.

The unpublished changes are listed under Unreleased in [CHANGELOG](CHANGELOG.md). They ship as `1.0.0`, the first public desktop release; `0.4.0` and `0.5.0` are never published (D-0530).

[PRODUCT](docs/PRODUCT.md) v1.8 (D-0367) refocuses keypaste on people who already keep their passwords in KeePass and write software:

- keys become fields on ordinary entries;
- tags put those fields into projects;
- the app gets simpler;
- connecting an agent takes one step.

[PRODUCT](docs/PRODUCT.md) v1.9 (D-0400) adds the end state: agents that use credentials they never hold (T7), then team projects, CI identities and organizations over an end-to-end relay (T8–T10). v1.10 (D-0408) makes the relay's first job a person's own cloud vault, free and unreadable by keypaste, and lets a team project choose server access once team projects work (T11). v1.11 (D-0416) drops compatibility with keypaste's own releases before 0.5.0, which nobody uses, so the `env/<project>` layout of 0.3 is no longer read. v1.12 (D-0430) lets a rule the person set release a value, offers run on every bridge keypaste connects but off until the person turns it on for that agent, lets an agent ask only about its own settings, with a change that gives it more taking effect once the person confirms it, makes the Agent Credential Access Format keypaste's storage format for agent access, and adds to T7 an agent that organizes the vault without seeing its values. v1.15 (D-0530) makes the first public desktop release 1.0.0, with no release before it, and puts T6, Auto-Type, attachment editing and T7 in it.

## Track order

The tracks are PRODUCT's, and they are worked in this order:

1. T1 everyday vault use;
2. T2 the shared unlock session;
3. T4 project environments;
4. T3 AI requests;
5. T5 desktop delivery;
6. T6 daily driver;
7. T7 agents without values;
8. T8 cloud vault and team projects;
9. T9 CI and deploys, with T11 server access by choice alongside;
10. T10 organizations.

Project environments come before AI requests because what an agent may see is built on project tags. T6 and T7 are worked before 1.0.0 is published, since it carries both; T7 serves one person on one machine and needs no service. T8–T11 follow 1.0.0: T8 starts the relay with a person's own cloud vault and then shares projects with members; T9 and T11 build on shared projects, and T10 on all of them. Within the current milestone, the next task is the first ready one in this order ([CLAUDE.md](CLAUDE.md#planning-and-selection)).

## 1.0.0: the first public release

One version publishes the Windows, macOS and Linux desktop apps, each carrying the CLI, together with the CLI/MCP archives for their four targets. No release comes before it (D-0530).

| Outcome | Tasks |
|---|---|
| Keys live as fields on the entries they belong to. Keys left in notes are flagged for review in Recommendations. The terminal finds items and shows their fields without printing a value unless asked. | V.7a, V.7b, C.2, N.13 |
| A tag such as `env:billing` or `env:billing:prod` puts an entry's fields into a project, and nothing else does: keypaste keeps no compatibility with its own releases before 0.5.0. An entry that loses its tag in another app is flagged, and a value's `{PASSWORD}`-style placeholders fill in from its own entry. | C.1a, C.1b, C.1c, C.6, C.4, V.11, P.3b1 |
| The app has four places: Items, Agents, Trash and Settings. It uses plain words, templates, a first run that finds the KeePassXC database, and the system's light or dark look, with advanced features in Settings. It says at once when another program saved the vault. | N.1a, N.1b, N.2, N.4, N.5, N.6, N.7, N.12, N.14, N.16 |
| An agent asks for one field of an entry it may see. Every common AI tool connects in one step to the vault the person chose, and the app, not the agent's own configuration, sets what each agent may see and run, and the owner records every request itself. The app stays in the menu bar or tray, nothing saves the vault behind its owner, and a terminal's edit is approved where the vault is unlocked. The app tells the person when an agent finds it locked, and `keypaste doctor` says what stands in the way. | C.5a, C.5b, G.1, G.2, N.3, G.4a, G.8, G.9, N.10, N.11, G.4b, G.3, N.8, N.9 |
| A project runs from the app on macOS, and every desktop install puts the CLI on PATH. | E.1d, G.5 |
| Development builds only the compile check on the founder's machine (D-0468), CI runs every check in parallel on every run (D-0423) and stays green without retries, and one binary carries the CLI and the MCP bridge. | K.6a, K.6b, K.6c, F.27, F.28, B.1, B.2, B.3, F.35, F.36, F.37, B.4a, B.4b, F.48, F.66 |
| Signed, notarized and verified packages for three platforms, with guidance a new user can follow, a guide to running agents safely, and phone apps checked by hand. | F.20, F.32, F.24, F.25, 4.7a2, 3.5b, 4.7e, R.1a, 4.7c2, L.3, R.1b, L.1, R.1 |
| A KeePassXC user loses nothing they use daily by switching: quick unlock with Windows Hello and Touch ID, TOTP codes, including for agents, never releasing the seed, browser fill starting from the KeePassXC-Browser extension people already have, Auto-Type, attachments, merging a synced file's changes instead of refusing until a reload, importers from Bitwarden, LastPass, 1Password and KeePassXC CSV, local password health, and hardware-key vaults beyond the desktop's unlock. | 4.10a, 4.10c, 4.10b, 9.2a, 9.2b, 8.1, 8.3a, P.4a, P.4b, V.8a, V.8b, 1.4a, 1.4b, 9.1a–9.1f, V.9, P.1 |
| An agent calls an API with a credential it never holds: the unlocked app attaches it for the hosts its entry names, under a grant the person approved, and writes the audit record. An agent organizes the vault without seeing its values: it sees names and structure, plus note text where the person allows it, and proposes changes such as classifying notes, moving keys into fields or tagging entries for projects, each approved in the app. | X.1, X.17 |
| The gaps the 2026-10-22 sweep deferred are fixed, each marked **For 1.0.0.** in its step record's limits. | — |
| Once 1.0.0's bytes are published, the same day: install with Homebrew, Scoop or winget; a Claude Code plugin, a Claude Desktop extension, an MCP Registry listing, a Gemini CLI extension, and Cursor and VS Code install links; a one-prompt setup page on keypaste.com; and the guides on keypaste.com's Astro pages, with a content security policy (D-0401). | 3.7a, 3.7b, 3.7c, G.6a, G.6b, G.6c, G.6d, G.6e, G.7, L.2b |

Outside inputs gate this release:

- a Windows signing identity (H-0017);
- Apple Developer enrollment (3.5a, H-0015);
- for the reach tasks, the MCP Registry namespace and hosting (H-0022) and the package-manager and plugin-directory submissions (H-0023).

Published version prefixes are immutable. If Apple enrollment lags, the choice at 4.7c2 is to wait, or to ship macOS as `1.0.1`.

keypaste.com keeps its "no `curl | sh`" stance: the one-command routes are the package managers and the signed installers.

## After 1.0.0: cloud vault and teams

These follow 1.0.0. Local use keeps working without an account or network (PRODUCT §4.1), and no server ever reads a personal vault.

| Outcome | Tasks |
|---|---|
| An end-to-end protocol for accounts, a person's vault and shared projects, reviewed against PRODUCT §3 before any relay code | X.2 |
| A relay on keypaste.com, and one a team runs itself, that stores only ciphertext and wrapped keys | X.3 |
| Everyone is offered a free cloud vault that keypaste cannot read, recovered with a kit the person keeps or a signed-in device, with premium and business tiers | X.10, X.11, X.12 |
| A project shared with members, with roles for reading, writing and production, and removal that re-wraps its key | X.4 |
| CI and deploy targets with identities the relay verifies, a GitHub Action, and syncs pushed from a member's machine | X.5, X.6, X.7 |
| A team project that opts in lets keypaste's server or the team's own hold its key: agents running in the cloud use it through a proxy, CI signs in with OIDC, and the server syncs to hosting platforms | X.13, X.14, X.15, X.16 |
| Organizations with single sign-on, SCIM, approval for production changes and audit export | X.8, X.9 |

The cloud's tiers follow PRODUCT §5.4: free up to a size limit, premium for more storage and features, and business use paid as it is used.

## Not planned

None of these has a milestone:

- sharing whole vault files;
- phone and web vault clients, though a keypaste phone app for cloud vaults is a BACKLOG option;
- SSH;
- complete KeePassXC parity;
- any server that could read a personal vault;
- PKI, KMS, privileged-access management and dynamic secrets.

KeePass-compatible apps and ordinary file sync serve phones for a local vault. [BACKLOG](docs/BACKLOG.md) keeps these and other ideas, each with the condition that would justify it.
