# Roadmap

The order of the committed work and the open tasks each milestone needs; [CLAUDE.md](CLAUDE.md#documents) names the owner of everything else.

A milestone names its open tasks by ID and may carry a target date the founder sets: a target is a plan, not a promise, and nothing here authorizes publication or messages.

## Where things stand

The public release is CLI/MCP `v0.3.0` ([RELEASE](docs/RELEASE.md)). The desktop app is implemented and unreleased, with internal, unsigned packages for Windows, macOS and Linux. [FEATURES](docs/FEATURES.md) owns each capability's state.

The unpublished changes, listed under Unreleased in [CHANGELOG](CHANGELOG.md), ship as `1.0.0`, the first public desktop release; `0.4.0` and `0.5.0` are never published. What 1.0.0 still needs is browser fill, the fixes still marked For 1.0.0, macOS signing and Touch ID, and then publication, its checks and the reach that follows it.

## Track order

The tracks are PRODUCT's, and they are worked in this order:

1. Track 1, everyday vault use;
2. Track 2, the shared unlock session;
3. Track 4, project environments;
4. Track 3, AI requests;
5. Track 6, daily driver;
6. Track 5, desktop delivery;
7. Track 7, agents without values;
8. Track 8, cloud vault and team projects;
9. Track 9, CI and deploys, with Track 11, server access by choice, alongside;
10. Track 10, organizations.

Project environments come before AI requests because what an agent may see is built on project tags. Track 6 is worked before 1.0.0 is published, which carries it. Track 7 follows 1.0.0; it serves one person on one machine and needs no service. Tracks 8 to 11 follow it: Track 8 starts the relay with a person's own cloud vault and then shares projects with members; Tracks 9 and 11 build on shared projects, and Track 10 on all of them. Within the current milestone, the next task is the first ready one in this order ([CLAUDE.md](CLAUDE.md#planning-and-selection)).

## 1.0.0: the first public release

One version publishes the Windows, macOS and Linux desktop apps, each carrying the CLI, together with the CLI/MCP archives for their four targets. No release comes before it. It targets Tuesday 2026-12-08, which holds only if the outside inputs below start by 2026-11-02.

| Outcome | Open tasks |
|---|---|
| Worked next, before release work: browser fill is finished, from installing the host per browser and keypaste's own extension in the three stores to filling in real browsers, and the fixes still marked For 1.0.0 are closed. | 8.1f, 8.1g, 8.3a2, 8.3a3, F.112 |
| The Mac app is signed and notarized, and Touch ID quick unlock follows as soon as it is. | 3.5b, 4.10b |
| Signed, notarized and verified packages for three platforms are published as 1.0.0, with guidance a new user can follow and the phone apps checked by hand, and the installed product is accepted. | 4.7c2, R.1b, L.1, R.1 |
| `keypaste approve` merges another program's save as the app does; it does not hold the release. | 1.4b4 |
| Once 1.0.0's bytes are published, the same day: install with Homebrew, Scoop or winget; a Claude Code plugin, a published Claude Desktop extension, an MCP Registry listing, a Gemini CLI extension; a one-prompt setup page on keypaste.com with the Cursor and VS Code install links; and the guides on keypaste.com's Astro pages, with a content security policy. | 3.7a, 3.7b, 3.7c, G.6a, G.6c, G.6d, G.7, L.2b |

Outside inputs gate this release, each taken up when the work that needs it is next:

- Apple's Developer ID and notarization credentials, when 3.5b reaches signing (3.5a, H-0015);
- a Windows signing identity (H-0017), before 4.7c2, allowing up to 20 business days for Microsoft's validation;
- the terms of use approved by the founder or a lawyer (H-0027), and the crash-report database made and switched on (H-0028), before 4.7c2;
- the extension's store accounts, IDs and Chrome signing key (H-0029), before 8.1g, allowing days per store for review;
- for the reach tasks, the MCP Registry namespace and hosting (H-0022) and the package-manager and plugin-directory submissions (H-0023).

The gaps a person would notice are the ones still marked **For 1.0.0.** in step records' limits, which F.112 closes; the test-coverage gaps marked **After 1.0.0.** and the defects [BACKLOG](docs/BACKLOG.md#known-defects-and-flaky-tests) lists do not hold the release.

A person's check on real hardware, such as Windows Hello or a physical YubiKey, does not hold the release: each is a BACKLOG entry, and a published claim about it waits for its evidence. Two person's checks do hold it, by the founder's decisions of 2026-11-09: a screen reader on each platform reading an unlock error, a toast and a prompt, with a VoiceOver user able to press Allow; and an approval prompt answerable on all three platforms.

Published version prefixes are immutable. If macOS signing lags, the choice at 4.7c2 is to wait, or to ship macOS as `1.0.1`.

keypaste.com keeps its "no `curl | sh`" stance: the one-command routes are the package managers and the signed installers.

## After 1.0.0: agents without values, then cloud vault and teams

These follow 1.0.0. Local use keeps working without an account or network (PRODUCT §4.1), and no server ever reads a personal vault.

| Outcome | Open tasks |
|---|---|
| An agent calls an API with a credential it never holds: the unlocked app attaches it for the hosts its entry names, under a grant the person approved, and writes the audit record. An agent organizes the vault without seeing its values: it sees names and structure, plus note text where the person allows it, and proposes changes such as classifying notes, moving keys into fields or tagging entries for projects, each approved in the app. | X.1, X.17 |
| An end-to-end protocol for accounts, a person's vault and shared projects, reviewed against PRODUCT §3 before any relay code | X.2 |
| A relay on keypaste.com, and one a team runs itself, that stores only ciphertext and wrapped keys | X.3 |
| Everyone is offered a free cloud vault that keypaste cannot read, recovered with a kit the person keeps or a signed-in device, with premium and business tiers | X.10, X.11, X.12 |
| A project shared with members, with roles for reading, writing and production, and removal that re-wraps its key | X.4 |
| CI and deploy targets with identities the relay verifies, a GitHub Action, and syncs pushed from a member's machine | X.5, X.6, X.7 |
| A team project that opts in lets keypaste's server or the team's own hold its key: agents running in the cloud use it through a proxy, CI signs in with OIDC, and the server syncs to hosting platforms | X.13, X.14, X.15, X.16 |
| Organizations with single sign-on, SCIM, approval for production changes and audit export | X.8, X.9 |

The cloud's tiers follow PRODUCT §5.4: free up to a size limit, premium for more storage and features, and business use paid as it is used.

## Not planned

[PRODUCT §2](docs/PRODUCT.md#2-scope-boundaries) lists what is not committed, such as sharing whole vault files, SSH and any server that could read a personal vault; none of it has a milestone. [BACKLOG](docs/BACKLOG.md) keeps those and other ideas, each with the condition that would justify it.
