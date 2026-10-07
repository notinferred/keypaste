# Working rules

## Start here

For a task, read this file, the [code map](docs/ARCHITECTURE.md) and the STEPS row or document the task names. Open another document when the task touches what it owns in the table below; do not read every document to begin, because most of them describe surfaces the task does not touch. Code cites decisions as `D-<number>` and threats as `T-<number>`; `git grep D-0123 -- '*.md'` finds the one row that governs.

## Writing

Write clean, minimal, self-documenting code. Prefer clear names and structure. Default to no comments; add a single line only to explain a non-obvious constraint or decision the code cannot express. Never restate the code or duplicate a document's explanation.

Tests exercise product behavior. A rule test over source or workflows holds a security or build invariant the compiler cannot, such as where the clipboard is read or that every Action is pinned to a commit; a test that a workflow, script or document still contains particular text is not written.

Write concise, connected prose without hard wrapping. Use headings, lists, tables and emphasis only when they help navigation or comparison. Preserve code blocks, transcripts and structured records. Remove repetition, filler, rhetorical contrasts, formulaic introductions and conclusions, and unnecessary qualifications. State what the evidence establishes and keep unresolved questions explicit. Do not invent facts or expand the task's scope.

## Documents

Documents fall into four layers that change at different rates. A base document describes the product and how to work on it, never the history of a step.

| Layer | Document | Owns | Changes when |
|---|---|---|---|
| Base | [PRODUCT](docs/PRODUCT.md) | Ratified scope and laws; conflicting decisions do not override it | Dated re-ratification with a decision row and an updated plan |
| Base | This file | Working rules and document ownership | Founder direction |
| Base | [ARCHITECTURE](docs/ARCHITECTURE.md) | Code map: projects, entry points, processes and where each concern lives | A project, entry point or process boundary changes |
| Base | [DECISIONS](DECISIONS.md) | Decisions that constrain later work, one line each. Superseded wording lives in [decisions-archive](docs/decisions-archive.md) as history, not requirements | A step makes a decision a later change could undo |
| Base | [SECURITY](SECURITY.md) and [THREATS](THREATS.md) | Reporting, security guarantees, threat model and known limits | A guarantee, threat or limit changes |
| Base | [BRAND](docs/BRAND.md) | The marks, the three colours, the type and the usage rules | Founder direction |
| Plan | [ROADMAP](ROADMAP.md) | Track order and milestones: which tasks each release or later stage needs | A milestone, its tasks or the track order changes |
| Plan | [STEPS](docs/STEPS.md) | Open tasks by product track: dependencies, detail and acceptance | A task is selected, finished or re-planned |
| Plan | [BACKLOG](docs/BACKLOG.md) | Optional ideas, investigation candidates and conditions for reconsideration; no delivery commitments | An idea is added, selected or dropped |
| Record | [steps](docs/steps/README.md) | One record per completed task, and the completed-steps evidence index | A task completes |
| Public | [README](README.md) | Introduction, published installation instructions and navigation | A release or published claim changes |
| Public | [FEATURES](docs/FEATURES.md) | Dated capabilities by surface, implementation evidence and gaps; no parity promise | A capability or gap changes |
| Public | [RELEASE](docs/RELEASE.md) | Distribution matrix, publication and installation verification | Release work |
| Public | [CHANGELOG](CHANGELOG.md) | One short entry per user-visible change, separating Unreleased work from published versions | Each user-visible change |
| Public | Guides: [desktop](docs/desktop.md), [mcp-setup](docs/mcp-setup.md), [policy](docs/policy.md), [approvals](docs/approvals.md), [replace-dotenv](docs/replace-dotenv.md), [demo](docs/demo.md), [keepass-and-agents](docs/keepass-and-agents.md), [launch](launch.md) | How to use or present one surface | The behavior they describe changes |
| Public | [keypaste.com's pages](site/web/src/content/docs/) | How keypaste works, each product and its label, design-level comparisons, the vision and the docs hub; labels live in `site/web/src/data/products.json` | A release changes a label, or a page's design changes |

### What a step writes

Finishing a task writes its record as `docs/steps/<ID>.md` from the template in [steps](docs/steps/README.md), removes its row from STEPS, and adds one CHANGELOG entry that links the record when users see a change. DECISIONS, SECURITY and THREATS change when the step makes a decision a later change could undo or changes a guarantee, threat or limit. FEATURES, the guides and README change only where the step made one of their statements false, and the claims `verify-demo.sh` checks stay true at every commit; new capabilities reach them through L.1, which rewrites them for 0.5.0 from CHANGELOG's Unreleased section.

### Records

Each fact has one authoritative owner; other documents link to it rather than restating it. Explicit user direction authorizes amendments within its scope. Product scope changes require dated re-ratification, a decision record and an updated plan; preserve the security laws in PRODUCT §3. Editorial changes preserve requirements and evidence.

Records are proportional to the risk they retire (PRODUCT §6.6). A choice with no future cost needs no row. Never write a document whose subject is another document: delete it instead. Tests on the secret, injection, sync and bridge paths are outside this rule and stay mandatory (PRODUCT §4.5); what gets cut is ceremony, never coverage.

IDs are permanent: a retired decision, threat or task ID is never given to something new, and the 5.x and 7.x task IDs of the earlier sync, hosting and organization ideas stay retired. Rewrite outdated current text in the other documents instead of appending another account, and keep history in Git. Published claims require supporting evidence (D-0036).

Use RELEASE's four states: Implemented, Packaged, Published and Installation-verified. Record version, platform and evidence. Source code, a local demo or a green pipeline cannot establish an unfinished user journey or public release. Documentation and local implementation do not authorize publication, messages or customer-data changes.

## Planning and selection

ROADMAP orders the committed work into milestones and STEPS holds its tasks; BACKLOG is not an implementation queue. The founder selects implementation work, and a documentation-only instruction starts no task. Identify the producer, transport, consumer and user action; the existence of a reader, view, protocol, fixture or package does not prove that the producing operation or complete journey works. Split oversized work into children while preserving IDs and dependencies. Completing a child leaves its siblings and parent gate open.

When the user explicitly requests the next build task, pick the first unchecked ready code task of ROADMAP's current milestone, in its track order, skipping external inputs and blocked work; a specific user selection takes precedence. Build, verify and record only the authorized task, then stop unless the user requested continued implementation. A founder amendment to a selected task's scope is recorded in its step record with the decision that made it. Nothing publishes before its Ships after gates pass.

## Local verification and delivery

A development machine needs no SDK and builds nothing. Implementation iterates on a branch: commit, then `bash scripts/dev.sh` pushes the branch, dispatches `ci` and `app` on it without opening a pull request, and follows their runs, printing each job as it finishes and stopping at the first failure with that job's log; `--wait` waits for every job. `bash scripts/dev.sh --class <FQN> [--os <runner>]` runs one test class through `dev.yml`, which is never a release gate. A dependency or `RuntimeIdentifiers` change regenerates its lock files with `bash scripts/dev.sh --relock`, which restores on the runner and writes the changed ones into the tree to commit. When the step's code and documents are finished, integrate once the branch's dispatched `ci` and `app` runs are green, and put their run IDs in the record. Do not open or approve pull requests from the founder's account.

`bash scripts/verify.sh` (or `./scripts/verify.ps1`, which selects Git Bash on Windows) is the runners' command and serves a contributor who has the pinned SDK. With no argument it runs every profile but `compat`; with a profile's name it runs that one, which is how each CI job calls it. The workflows call `format`, `backend`, `integration`, `desktop`, `desktop-gates`, `scripts` and `workflows` by name. `desktop` includes consistency tests, `desktop-gates` drives the app, the CLI and an agent as processes, and `format` runs `dotnet format` once over every project, which no build step repeats. The script owns the command list and its prerequisites; other operating systems, NativeAOT, packaging and public installation retain their separate gates. A step's cross-process check extends the gate that already covers its surface and sources `scripts/lib/`; a script gets a `--selftest` only when it guards the release path.

For documentation-only work, inspect changed claims, links, ownership and scope against source and recorded evidence. Do not start a build task or run commands that build code when the user has restricted the work to documents. A documentation review does not establish new runtime or release evidence.

Iterate a runner-only probe on a branch: push the branch and run `gh workflow run <workflow> --ref <branch>`, without a local run or a commit on `main` per attempt, and integrate once it passes. A new workflow needs its file on `main` once before it can be dispatched; later attempts run the branch's copy. Integrate a completed step as one coherent commit after its code, verifier and records are complete.

Diagnose before repairing when the mechanism is unknown. Keep the regression in the tree; delete a probe workflow or reader once its diagnosis closes, keeping its result in the step record. An inconclusive run identifies the next experiment and leaves the diagnosis open. Preserve the failing observation until a regression and repair explain it; a passing retry does not close an intermittent defect.

## CI

`ci.yml` runs on qualifying pushes to `main`, every pull request, dispatch and a weekly schedule; its push-level `paths-ignore` skips specified documents. `app.yml` has a push allowlist for desktop, CLI, core and shared build/test inputs, runs on every pull request and dispatch, and packages matching version tags. Every run runs every job, each profile as its own job on each runner, and nothing is skipped or cached (D-0423). Runs on `main` and tags are never cancelled; on any other branch a newer run cancels the one before it. `ci ok` and `app ok` aggregate each workflow's jobs and fail when any job failed or was cancelled; they are the checks a pull request needs green. Feature-branch pushes require a pull request or explicit dispatch for remote evidence. The workflow files own the exact triggers.

`README.md`, `launch.md`, `docs/demo.md`, `docs/keepass-and-agents.md` and `site/public/index.html` must trigger `ci.yml`: `scripts/verify-demo.sh` checks their transcripts against the built binaries. Never add `docs/**` to `paths-ignore`. New documentation paths trigger backend CI unless explicitly ignored.

keypaste.com deploys through Cloudflare's Git integration on pushes to `main`, watching `site/`, with root directory `site`, no build command in the dashboard and deploy command `npm run deploy`, whose wrangler build step builds the content pages in `site/web` (D-0127, D-0402). GitHub workflows do not query the live origin. Run [verify-site-disclosure.sh](scripts/verify-site-disclosure.sh) after a site deploy; [verify-site-endpoint.sh](scripts/verify-site-endpoint.sh) is also manual. Only the offline disclosure self-test runs in CI. `site/README.md` changes trigger a site deployment while skipping both GitHub workflows.

## Releases

[RELEASE.md](docs/RELEASE.md) owns release procedures and the platform/channel matrix. Published version paths are immutable. [publish-release.sh](scripts/publish-release.sh) requires a positively verified empty destination. A partial publication requires a new version; never replace its objects. `release.yml` publishes a tag only when [require-green-gates.sh](scripts/require-green-gates.sh) finds a full green run of each required workflow on its commit: a dispatch, the weekly schedule or the tag's own run (D-0403). Cut a release by dispatching `ci.yml` at the commit and tagging once it is green; no local run is needed.

## Git

Author every commit as `keypaste <contact@keypaste.com>`, including agent-written commits. Use the project identity in first-party pages and metadata while retaining required upstream attribution. Use a subject of at most 72 characters and a matching `Signed-off-by` trailer; include no other body unless requested. Correct an unintended identity before pushing. Merge locally because the GitHub merge button supplies its own identity.
