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

## Track order

The tracks are PRODUCT's, and they are worked in this order:

1. T1 everyday vault use;
2. T2 the shared unlock session;
3. T4 project environments;
4. T3 AI requests;
5. T5 desktop delivery;
6. T6 daily driver.

Project environments come before AI requests because what an agent may see is built on project tags. Within the current milestone, the next task is the first ready one in this order ([CLAUDE.md](CLAUDE.md#planning-and-selection)).

The build infrastructure rows come first: K.6a, K.6b, F.27, F.28, B.3, F.35, F.36, F.37, B.1 and B.2 before C.1b, and B.4a and B.4b after C.4 and before C.5a, so the agent tasks are written against the single binary.

## 0.5.0: the first release of the new keypaste

One version publishes the Windows, macOS and Linux desktop apps, each carrying the CLI, together with the CLI/MCP archives for their four targets.

| Outcome | Tasks |
|---|---|
| Keys live as fields on the entries they belong to. Keys left in notes are flagged for review in Recommendations. | V.7a, V.7b, C.2 |
| A tag such as `env:billing` or `env:billing:prod` puts an entry's fields into a project. Projects in the old `env/` layout keep working and can be moved. | C.1a, C.1b, C.1c, C.3, C.4 |
| The app has four places: Items, Agents, Trash and Settings. It uses plain words, templates, a first run that finds the KeePassXC database, and the system's light or dark look, with advanced features in Settings. | N.1a, N.1b, N.2, N.4, N.5, N.6, N.7, N.12, N.14 |
| An agent asks for one field of an entry it may see. Every common AI tool connects in one step to the vault the person chose. The app stays in the menu bar or tray, and nothing saves the vault behind its owner. | C.5a, C.5b, G.1, G.2, N.3, G.4a, N.10 |
| A project runs from the app on macOS, and every desktop install puts the CLI on PATH. | E.1d, G.5 |
| Development builds nothing on the founder's machine, CI runs what a change can break and stays green without retries, and one binary carries the CLI and the MCP bridge. | K.6a, K.6b, F.27, F.28, B.1, B.2, B.3, F.35, F.36, F.37, B.4a, B.4b |
| Signed, notarized and verified packages for three platforms, with guidance a new user can follow. | F.20, F.32, F.24, F.25, 4.7a2, 3.5b, 4.7e, R.1a, 4.7c2, L.1, R.1 |

Two outside inputs gate this release:

- a Windows signing identity (H-0017);
- Apple Developer enrollment (3.5a, H-0015).

Published version prefixes are immutable. If Apple enrollment lags, the choice at 4.7c2 is to wait, or to ship macOS as `0.5.1`.

N.4, N.7, N.12, G.4a and C.3 could move to the next milestone without breaking the journey.

## After 0.5.0: reach

These follow the publication of 0.5.0.

| Outcome | Tasks |
|---|---|
| Install with Homebrew, Scoop or winget | 3.7a, 3.7b, 3.7c |
| A Claude Code plugin, a Claude Desktop extension, an MCP Registry listing, a Gemini CLI extension, and Cursor and VS Code install links | G.6a, G.6b, G.6c, G.6d, G.6e |
| A one-prompt setup page on keypaste.com, and `keypaste doctor` | G.7, G.3 |
| A notice when a locked app is asked; what each agent may see and run, set in the app | G.4b, G.8 |
| Terminal status, one name per CLI concept, terminal edits approved in the app, and field search | N.8, N.9, N.11, N.13 |
| Same-entry placeholders such as `{PASSWORD}` in released values | P.3b1 |

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

## Not planned

None of these has a milestone:

- hosted sync and accounts;
- billing;
- team administration;
- phone and web clients;
- SSH;
- complete KeePassXC parity.

KeePass-compatible apps and ordinary file sync serve phones and sync. [BACKLOG](docs/BACKLOG.md) keeps these and other ideas, each with the condition that would justify it.

## How this changes

Moving a task between milestones, or reordering the tracks, is a re-plan that updates this file. A change to what a release contains is dated in [DECISIONS](DECISIONS.md). Finishing a task changes its STEPS row and its record, not this file.
