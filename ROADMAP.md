# Roadmap

This file owns the order of the work and its milestones. [PRODUCT](docs/PRODUCT.md) owns the product statement, scope and laws. [STEPS](docs/STEPS.md) owns each task's dependencies and acceptance. [BACKLOG](docs/BACKLOG.md) holds ideas no milestone includes.

A milestone names the tasks it needs by ID. It promises no date, and nothing here authorizes publication or messages.

## Where things stand

The public release is CLI/MCP `0.3.0` ([RELEASE](docs/RELEASE.md)). The desktop app works from source, with internal, unsigned packages for Windows, macOS and Linux and no public release. [FEATURES](docs/FEATURES.md) owns what works today.

The unpublished changes are listed under Unreleased in [CHANGELOG](CHANGELOG.md). They ship as `0.5.0`; `0.4.0` is never published (D-0368).

[PRODUCT](docs/PRODUCT.md) v1.8 (D-0367) refocuses keypaste on people who already keep their passwords in KeePass and write software:

- keys become fields on ordinary entries;
- tags put those fields into projects;
- the app gets simpler;
- connecting an agent takes one step.

[PRODUCT](docs/PRODUCT.md) v1.9 (D-0400) adds the end state that follows the first desktop release: agents that use credentials they never hold (T7), then team projects, CI identities and organizations over an end-to-end relay (T8–T10). v1.10 (D-0408) makes the relay's first job a person's own cloud vault, free and unreadable by keypaste, and lets a team project choose server access once team projects work (T11). v1.11 (D-0416) drops compatibility with keypaste's own releases before 0.5.0, which nobody uses, so the `env/<project>` layout of 0.3 is no longer read.

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

Project environments come before AI requests because what an agent may see is built on project tags. After the first desktop release, T6 is worked alongside T7–T11. T7 comes first among those because it serves one person on one machine and needs no service. T8 starts the relay with a person's own cloud vault and then shares projects with members; T9 and T11 build on shared projects, and T10 on all of them. Within the current milestone, the next task is the first ready one in this order ([CLAUDE.md](CLAUDE.md#planning-and-selection)).

## 0.5.0: the first release of the new keypaste

One version publishes the Windows, macOS and Linux desktop apps, each carrying the CLI, together with the CLI/MCP archives for their four targets.

| Outcome | Tasks |
|---|---|
| Keys live as fields on the entries they belong to. Keys left in notes are flagged for review in Recommendations. | V.7a, V.7b, C.2 |
| A tag such as `env:billing` or `env:billing:prod` puts an entry's fields into a project, and nothing else does: keypaste keeps no compatibility with its own releases before 0.5.0. An entry that loses its tag in another app is flagged. | C.1a, C.1b, C.1c, C.6, C.4, V.11 |
| The app has four places: Items, Agents, Trash and Settings. It uses plain words, templates, a first run that finds the KeePassXC database, and the system's light or dark look, with advanced features in Settings. It says at once when another program saved the vault. | N.1a, N.1b, N.2, N.4, N.5, N.6, N.7, N.12, N.14, N.16 |
| An agent asks for one field of an entry it may see. Every common AI tool connects in one step to the vault the person chose, and the app, not the agent's own configuration, sets what each agent may see and run, and the owner records every request itself. The app stays in the menu bar or tray, and nothing saves the vault behind its owner. | C.5a, C.5b, G.1, G.2, N.3, G.4a, G.8, G.9, N.10 |
| A project runs from the app on macOS, and every desktop install puts the CLI on PATH. | E.1d, G.5 |
| Development builds nothing on the founder's machine, CI runs every check in parallel on every run (D-0423) and stays green without retries, and one binary carries the CLI and the MCP bridge. | K.6a, K.6b, F.27, F.28, B.1, B.2, B.3, F.35, F.36, F.37, B.4a, B.4b, F.48 |
| Signed, notarized and verified packages for three platforms, with guidance a new user can follow, a guide to running agents safely, and phone apps checked by hand. | F.20, F.32, F.24, F.25, 4.7a2, 3.5b, 4.7e, R.1a, 4.7c2, L.3, R.1b, L.1, R.1 |

Two outside inputs gate this release:

- a Windows signing identity (H-0017);
- Apple Developer enrollment (3.5a, H-0015).

Published version prefixes are immutable. If Apple enrollment lags, the choice at 4.7c2 is to wait, or to ship macOS as `0.5.1`.

N.4, N.7, N.12 and G.4a could move to the next milestone without breaking the journey.

## After 0.5.0: reach

These follow the publication of 0.5.0.

| Outcome | Tasks |
|---|---|
| Install with Homebrew, Scoop or winget | 3.7a, 3.7b, 3.7c |
| A Claude Code plugin, a Claude Desktop extension, an MCP Registry listing, a Gemini CLI extension, and Cursor and VS Code install links | G.6a, G.6b, G.6c, G.6d, G.6e |
| A one-prompt setup page on keypaste.com, and `keypaste doctor` | G.7, G.3 |
| A notice when a locked app is asked | G.4b |
| Terminal status, one name per CLI concept, terminal edits approved in the app, and field search | N.8, N.9, N.11, N.13 |
| Same-entry placeholders such as `{PASSWORD}` in released values | P.3b1 |
| The guides moved onto keypaste.com's Astro pages, with a content security policy (D-0401) | L.2b |

keypaste.com keeps its "no `curl | sh`" stance: the one-command routes are the package managers and the signed installers.

Two outside inputs gate this milestone:

- the MCP Registry namespace and hosting (H-0022);
- the package-manager and plugin-directory submissions (H-0023).

## T6: daily driver

This milestone makes sure a KeePassXC user loses nothing they use daily by switching.

| Outcome | Tasks |
|---|---|
| Quick unlock with Windows Hello and Touch ID | 4.10a, 4.10b |
| TOTP codes, including for agents, never releasing the seed | 9.2a, 9.2b |
| Browser fill, starting from the KeePassXC-Browser extension people already have | 8.1, 8.3a |
| Merging a synced file's changes instead of refusing until a reload | 1.4a, 1.4b |
| Importers from Bitwarden, LastPass, 1Password and KeePassXC CSV | 9.1a–9.1f |
| Local password health in Recommendations | V.9 |
| Hardware-key vaults beyond the desktop's unlock | P.1 |

## T7: agents without values

This follows the first desktop release and serves one person on one machine.

| Outcome | Tasks |
|---|---|
| An agent calls an API with a credential it never holds: the unlocked app attaches it for the hosts its entry names, under a grant the person approved, and writes the audit record | X.1 |

## T8–T11: cloud vault and teams

These follow T7. Local use keeps working without an account or network (PRODUCT §4.1), and no server ever reads a personal vault.

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
- an AI that organizes the vault;
- any server that could read a personal vault;
- PKI, KMS, privileged-access management and dynamic secrets.

KeePass-compatible apps and ordinary file sync serve phones for a local vault. [BACKLOG](docs/BACKLOG.md) keeps these and other ideas, each with the condition that would justify it.
