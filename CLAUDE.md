# Working rules

## Start here

For a task, read this file, the [code map](docs/ARCHITECTURE.md) and the STEPS row or document the task names. Open another document only when the task touches what it owns in the table below.

## Writing

Write clean, minimal, self-documenting code. Prefer clear names and structure. Default to no comments; a single line may explain a constraint or decision the code cannot express. Never restate the code or a document. Take a capability from the platform, the ecosystem's standard or an established library before writing it, behind an interface keypaste owns, in the order PRODUCT §4.9 sets.

Tests exercise product behavior. A rule test over source or workflows holds a security or build invariant the compiler cannot, such as that every Action is pinned to a commit; a test that a workflow, script or document still contains particular text is not written.

Write concise, connected prose without hard wrapping. Use headings, lists, tables and emphasis only where they help navigation or comparison. Preserve code blocks, transcripts and structured records. Remove repetition, filler, rhetorical contrasts, formulaic openings and endings, and needless qualifications. State what the evidence establishes and keep unresolved questions explicit. Do not invent facts or expand the task's scope. Use the words of DESIGN's glossary.

## Documents

Documents fall into four layers. A base document describes the product and how to work on it, never a step's history.

| Layer | Document | Owns |
|---|---|---|
| Base | [PRODUCT](docs/PRODUCT.md) | Ratified scope and laws, which conflicting decisions do not override; changes only by dated re-ratification with an updated plan |
| Base | This file | Working rules and document ownership, by founder direction |
| Base | [ARCHITECTURE](docs/ARCHITECTURE.md) | Code map: projects, entry points, processes and where each concern lives |
| Base | [SECURITY](SECURITY.md) and [THREATS](THREATS.md) | Reporting, security guarantees, threat model and known limits |
| Base | [DESIGN-PRINCIPLES](docs/DESIGN-PRINCIPLES.md) | The eight design principles and their tests, changed with PRODUCT's re-ratification |
| Base | [DESIGN](docs/DESIGN.md) | The design system, the voice and its glossary, by founder direction |
| Base | [CONTRIBUTING](CONTRIBUTING.md) | Contributing, local verification, CI and sign-off |
| Base | [site/README](site/README.md) | keypaste.com's deployment, database and routes |
| Plan | [ROADMAP](ROADMAP.md) | Track order and milestones: the open tasks each release needs |
| Plan | [STEPS](docs/STEPS.md) | Open tasks by track: dependencies, detail and acceptance |
| Plan | [BACKLOG](docs/BACKLOG.md) | Ideas with conditions for reconsideration, and known defects; no commitments |
| Record | [steps](docs/steps/README.md) | One record per completed task |
| Public | [README](README.md) | Introduction, published installation and navigation |
| Public | [FEATURES](docs/FEATURES.md) | Each capability's state by surface, evidence and gaps |
| Public | [RELEASE](docs/RELEASE.md) | Distribution matrix, release procedure and installation verification |
| Public | [CHANGELOG](CHANGELOG.md) | One short entry per user-visible change, Unreleased apart from published versions |
| Public | Guides in `docs/`: desktop, mcp-setup, policy, approvals, safe-agents, replace-dotenv, demo, keepass-and-agents, launch | How to use or present one surface |
| Public | keypaste.com's pages, in InfernoCMS at `https://infernocms.com`, site `keypaste` | How keypaste works, the products and their labels (`site/web/src/data/products.json`), comparisons, the vision and the docs hub; the build pulls them into `site/web/src/content/docs/`, which git ignores ([site/README](site/README.md#content-pages)) |
| Public | Topic map, InfernoCMS's graph for site `keypaste` | Which page owns each question keypaste.com answers, the searches it targets and the questions no page answers yet |
| Public | [ACAF](https://keypaste.com/acaf/v1/) v1 and v1.1, InfernoCMS pages `acaf/v1` and `acaf/v1.1` | The Agent Credential Access Format and keypaste's status in RELEASE's terms; a format change is a new version |

### What a step writes

Finishing a task writes its record as `docs/steps/<ID>.md` from the [template](docs/steps/README.md), removes its row from STEPS and its ID from ROADMAP, and adds one CHANGELOG entry linking the record when users see a change. SECURITY and THREATS change when the step changes a guarantee, threat or limit. FEATURES, the guides and README change only to correct a statement the step made false, never to add one, and the claims `verify-demo.sh` checks stay true at every commit; L.1 rewrites them for 1.0.0.

### Records

Each fact has one owner; other documents link to it rather than restating it. User direction authorizes amendments within its scope. Product scope changes require dated re-ratification and an updated plan; preserve the security laws in PRODUCT §3. Editorial changes preserve requirements and evidence. Rewrite outdated text instead of appending another account, and keep history in Git. Published claims require supporting evidence.

Records are proportional to the risk they retire (PRODUCT §6.6): a record says what changed and why without restating the diff, and a low-risk step's is a few lines. It names no run IDs and tells no rerun story. Never write a document whose subject is another document. Tests on the secret, injection, sync and bridge paths stay mandatory (PRODUCT §4.5): what gets cut is ceremony, never coverage.

Use RELEASE's four states, Implemented, Packaged, Published and Installation-verified, with version, platform and evidence. Source, a local demo or a green pipeline cannot establish an unfinished journey or a release, and documentation authorizes no publication, messages or customer-data changes.

### IDs

IDs are permanent: a retired decision, threat or task ID is never reused, and the 5.x and 7.x task IDs stay retired.

- A task ID such as `F.112` names a STEPS row or, once done, its record.
- `H-<number>` names an act only the founder can do: a Human row in STEPS or an input a row names.
- `T-<number>` names a threat in THREATS. PRODUCT's tracks are written "Track 4", never "T4".
- `D-<number>` and `O-<number>` name decisions and open questions of a retired ledger that is gone. Code still carries some: remove them from code you touch, and ask the founder when a task depends on the reason behind one.

## Planning and selection

ROADMAP orders the committed work into milestones and STEPS holds its tasks; BACKLOG is not a queue. The founder selects implementation work, and a documentation-only instruction starts no task. A session never adds a STEPS row: a finding, follow-up or check it cannot close goes to BACKLOG, and only the founder makes it a task. Identify the producer, transport, consumer and user action: a reader, view, fixture or package does not prove the journey works.

When the user asks for the next build task, pick the first unchecked STEPS row, top to bottom, that ROADMAP's current milestone names, skipping Human rows, rows waiting on an input and rows with open Needs; the user's own pick comes first. Build, verify and record only that task, then stop unless asked to continue; a founder amendment to its scope goes in its record. Nothing publishes before its Ships after gates pass. Claude settles reversible, local choices that touch no law, scope, money, legal text or published claim, and lists them; the founder decides the rest.

## Verification and delivery

The founder's machine builds and tests in short bursts that leave nothing running: `bash scripts/verify.sh compile` before each push, and `bash scripts/verify.sh test <class or tests/project>...` for the classes a change touches; long suites stay in CI. Commit on a branch, push it with `bash scripts/dev.sh`, and integrate once the step is finished and the fast tier is green; a failure on `main` is the next work. [CONTRIBUTING](CONTRIBUTING.md#verification) owns the flags, profiles and CI. Do not open or approve pull requests from the founder's account.

Documentation-only work checks claims, links, ownership and scope against source and recorded evidence, builds nothing and proves no runtime or release behavior.

Diagnose before repairing when the mechanism is unknown, keep the regression in the tree, and delete a probe once its diagnosis closes. A passing retry does not close an intermittent defect. A check that fails without a change to what it checks is a defect: diagnose and fix it, or delete it when what it guards is held elsewhere; never keep it red or rerun it into green.

## Releases

[RELEASE](docs/RELEASE.md) owns the release procedure and matrix. Published version paths are immutable: a partial publication requires a new version. Cut a release by dispatching `ci.yml` with `-f tools=true` at the commit, then tag it once green.

## Git

Author every commit as `keypaste <contact@keypaste.com>`, agent-written too, and use that identity in first-party pages and metadata, keeping required upstream attribution. Use a subject of at most 72 characters and a matching `Signed-off-by` trailer, with no other body unless requested. Correct an unintended identity before pushing. Merge locally, because GitHub's merge button supplies its own identity.
