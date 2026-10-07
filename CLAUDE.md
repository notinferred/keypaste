# Working rules

## Start here

For a task, read this file, the [code map](docs/ARCHITECTURE.md) and the STEPS row or document the task names. Open another document when the task touches what it owns in the table below; do not read every document to begin, because most of them describe surfaces the task does not touch. Code cites decisions as `D-<number>` and threats as `T-<number>`; `git grep D-0123 -- '*.md'` finds the one row that governs.

## Writing

Write clean, minimal, self-documenting code. Prefer clear names and structure. Default to no comments; add a single line only to explain a non-obvious constraint or decision the code cannot express. Never restate the code or duplicate a document's explanation. Comments in the members a step changes are brought to this rule; nothing sweeps comments on its own.

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
| Record | [steps](docs/steps/README.md) | One record per completed task, and the completed-steps evidence index | Once, when a task completes; never revised |
| Record | [research](docs/research/) | Dated evidence about other products and the market, one file per review | Once, when a review is made; never revised |
| Public | [README](README.md) | Introduction, published installation instructions and navigation | A release or published claim changes |
| Public | [FEATURES](docs/FEATURES.md) | Dated capabilities by surface, implementation evidence and gaps; no parity promise | A capability or gap changes |
| Public | [RELEASE](docs/RELEASE.md) | Distribution matrix, publication and installation verification | Release work |
| Public | [CHANGELOG](CHANGELOG.md) | One short entry per user-visible change, separating Unreleased work from published versions | Each user-visible change |
| Public | Guides: [desktop](docs/desktop.md), [mcp-setup](docs/mcp-setup.md), [policy](docs/policy.md), [approvals](docs/approvals.md), [replace-dotenv](docs/replace-dotenv.md), [demo](docs/demo.md), [keepass-and-agents](docs/keepass-and-agents.md), [launch](launch.md), [ui-review](docs/ui-review.md), [diagnostics](docs/diagnostics.md) | How to use, present or diagnose one surface | The behavior they describe changes |
| Public | [keypaste.com's pages](site/web/src/content/docs/) | How keypaste works, each product and its label, design-level comparisons, the vision and the docs hub; labels live in `site/web/src/data/products.json` | A release changes a label, or a page's design changes |

### What a step writes

Until 0.5.0 is published (4.7c2), finishing a task writes its record as `docs/steps/<ID>.md` from the template in [steps](docs/steps/README.md), removes its row from STEPS, and adds one CHANGELOG entry of at most three sentences that links the record when users see a change. The record stays near 60 lines: the completion line with the CI run IDs that passed it, amendments to the row as selected rather than a copy of it, the evidence, the decisions, and the limits with the STEPS rows they created. DECISIONS, SECURITY and THREATS change when the step makes a decision a later change could undo or changes a guarantee, threat or limit; a threat section the step edits is rewritten whole to current behavior. FEATURES, the guides and README change only where the step made one of their statements false, and the claims `verify-demo.sh` checks stay true at every commit; new capabilities reach them through L.1, which rewrites them for 0.5.0 from CHANGELOG's Unreleased section. The step's reasoning, the decisions binding only its own code, the defects it found and its limits go in the record and nowhere else. The step's summary names every other document it changed and why.

### Records

Each fact has one authoritative owner; other documents link to it rather than restating it. Explicit user direction authorizes amendments within its scope. Product scope changes require dated re-ratification, a decision record and an updated plan; preserve the security laws in PRODUCT §3. Editorial changes preserve requirements and evidence.

Records are proportional to the risk they retire (PRODUCT §6.6). A ledger row is one sentence under 400 characters saying what was decided and what it supersedes, not the argument that reached it. A choice with no future cost needs no row. Never write a document whose subject is another document: delete it instead. Tests on the secret, injection, sync and bridge paths are outside this rule and stay mandatory (PRODUCT §4.5); what gets cut is ceremony, never coverage.

IDs are permanent: a retired decision, threat or task ID is never given to something new. Step records and archived rows are not edited; a correction is a new row or task that names what it corrects. Rewrite outdated current text in the other documents instead of appending another account, and keep history in Git. Published claims require supporting evidence (D-0036).

Use RELEASE's four states: Implemented, Packaged, Published and Installation-verified. Record version, platform and evidence. Source code, a local demo or a green pipeline cannot establish an unfinished user journey or public release. Documentation and local implementation do not authorize publication, messages or customer-data changes.

## Planning and selection

ROADMAP orders the committed work into milestones and STEPS holds its tasks; BACKLOG is not an implementation queue. The founder selects implementation work, and a documentation-only instruction starts no task. A task is detailed when it is selected, against the code as it then is; until then a row carries an ID, name, Needs and purpose. Each detailed task has a bounded Build and a falsifiable Verify prompt that traces to PRODUCT. Identify the producer, transport, consumer and user action; the existence of a reader, view, protocol, fixture or package does not prove that the producing operation or complete journey works. Split oversized work into children while preserving IDs and dependencies. Completing a child leaves its siblings and parent gate open.

A task's Needs name only what its own code or verifier cannot run without. Time, human acts, milestone or release gates, freezes and tidier ordering are never Needs. Gates decide what may ship through Ships after, and Human rows are a separate track. Protect a recorded result by versioning or voiding it, not by a test or rule that forbids change.

When the user explicitly requests the next build task, pick the first unchecked ready code task of ROADMAP's current milestone, in its track order, skipping external inputs and blocked work; a specific user selection takes precedence. Expand a later selected row before implementing it. Build, verify and record only the authorized task, then stop unless the user requested continued implementation. Neither document edits nor dependency readiness authorize starting work, and optional BACKLOG items require selection into STEPS first. A founder amendment to a selected task's scope is recorded in its step record with the decision that made it.

During an external wait, record the pending result and its next decision. If the user authorized continuing delivery, work on the next ready code task, keeping its changes isolated. Resume the waiting task when evidence arrives. Nothing publishes before its Ships after gates pass.

## Local verification and delivery

A development machine needs no SDK and builds nothing. Implementation iterates on a branch: commit, then `bash scripts/dev.sh` pushes the branch, opens a draft pull request if it has none, and follows the `ci` and `app` runs the push starts, printing each job as it finishes and stopping at the first failure with that job's log; `--wait` waits for every job. `bash scripts/dev.sh --class <FQN> [--os <runner>]` runs one test class through `dev.yml`, which is never a release gate. A dependency or `RuntimeIdentifiers` change regenerates its lock files with `bash scripts/dev.sh --relock`, which restores on the runner and writes the changed ones into the tree to commit. When the step's code and documents are finished, integrate once the pull request's `ci` and `app` runs are green, and put their run IDs in the record.

`bash scripts/verify.sh` (or `./scripts/verify.ps1`, which selects Git Bash on Windows) is the runners' command and serves a contributor who has the pinned SDK. With no argument it runs every profile but `compat`; with a profile's name it runs that one, which is how each CI job calls it.

After a failure the command names every failed profile, prints their held output and prints the resume command. Fix the cause and resume with `--from <profile>`; do not start again. Never run a profile by name and then run the command again, because the second run repeats the first. `scripts` and `workflows` run beside the dotnet profiles, which run in sequence because both solutions build into one `artifacts/` tree. Each profile's output is held in `artifacts/verify/`.

For documentation-only work, inspect changed claims, links, ownership and scope against source and recorded evidence. Do not start a build task or run commands that build code when the user has restricted the work to documents. A documentation review does not establish new runtime or release evidence.

The command requires the pinned SDK, Git, jq, GNU timeout and running Docker. Linux and Git Bash supply GNU timeout; macOS can use `gtimeout` from coreutils. Process integration checks have an eight-minute deadline and a ten-second termination grace. `compat` is never selected automatically and needs installed KeePassXC. On Windows the offline script fixtures run in the Linux container that [scripts/container](scripts/container/Dockerfile) defines, where they take a tenth of the time; a linked worktree, whose history lies outside the mount, runs them in Git Bash, as does `VERIFY_SCRIPTS_NATIVE=1`. Use `--list` to inspect the selection and commands. The workflows call `format`, `backend`, `integration`, `desktop`, `desktop-gates`, `scripts` and `workflows` by name. `desktop` includes consistency tests, `desktop-gates` drives the app, the CLI and an agent as processes, and `format` runs `dotnet format` once over every project, which no build step repeats. The script owns the command list; other operating systems, NativeAOT, packaging and public installation retain their separate gates. A step's cross-process check extends the gate that already covers its surface and sources `scripts/lib/`; a script gets a `--selftest` only when it guards the release path.

Iterate a runner-only probe on a branch: push the branch and run `gh workflow run <workflow> --ref <branch>`, without a local run or a commit on `main` per attempt, and integrate once it passes. A new workflow needs its file on `main` once before it can be dispatched; later attempts run the branch's copy. Feature branches may hold reviewable checkpoints for pull requests or platform experiments, including a named failing regression. Record why remote execution is needed and retain its source SHA. Integrate a completed step as one coherent commit after its Build, verifier and records are complete.

Separate discovery from repair when the mechanism is unknown. A discovery row delivers a reproducible experiment and measured conclusion; its repair row depends on that result. Before a long probe, run a short preflight and state which outcomes distinguish the hypotheses. Keep the regression in the tree; delete a probe workflow or reader once its diagnosis closes, keeping its result in the step record. Retain the source SHA, platform, command, counts and limitations using [diagnostics.md](docs/diagnostics.md). An inconclusive run identifies the next experiment and leaves the diagnosis open.

Put reproducible defects in STEPS with a verifier and priority, including defects found during another task. BACKLOG may link to the task; a reproduced defect is not left as an unverified idea. Preserve the failing observation until a regression and repair explain it; a passing retry does not close an intermittent defect.

## CI

`ci.yml` runs on qualifying pushes to `main`, every pull request, dispatch and a weekly schedule; its push-level `paths-ignore` skips specified documents. `app.yml` has a push allowlist for desktop, CLI, core and shared build/test inputs, runs on every pull request and dispatch, and packages matching version tags. Every run runs every job, each profile as its own job on each runner, and nothing is skipped or cached (D-0423). Runs on `main` and tags are never cancelled; on any other branch a newer run cancels the one before it. `ci ok` and `app ok` aggregate each workflow's jobs and fail when any job failed or was cancelled; they are the checks a pull request needs green. Feature-branch pushes require a pull request or explicit dispatch for remote evidence. The workflow files own the exact triggers.

Probes run the identical backend test command used by CI. Narrowing the suite changes concurrent load and can remove the condition being measured. Run a hosted probe when local reproduction is insufficient, and retain counts and diagnostic evidence.

`README.md`, `launch.md`, `docs/demo.md`, `docs/keepass-and-agents.md` and `site/public/index.html` must trigger `ci.yml`: `scripts/verify-demo.sh` checks their transcripts against the built binaries. Never add `docs/**` to `paths-ignore`. New documentation paths trigger backend CI unless explicitly ignored.

keypaste.com deploys through Cloudflare's Git integration on pushes to `main`, watching `site/`, with root directory `site`, no build command in the dashboard and deploy command `npm run deploy`, whose wrangler build step builds the content pages in `site/web` (D-0127, D-0402). GitHub workflows do not query the live origin. Run [verify-site-disclosure.sh](scripts/verify-site-disclosure.sh) after a site deploy; [verify-site-endpoint.sh](scripts/verify-site-endpoint.sh) is also manual. Only the offline disclosure self-test runs in CI. `site/README.md` changes trigger a site deployment while skipping both GitHub workflows.

## Releases

[RELEASE.md](docs/RELEASE.md) owns release procedures and the platform/channel matrix. Published version paths are immutable. [publish-release.sh](scripts/publish-release.sh) requires a positively verified empty destination. A partial publication requires a new version; never replace its objects. `release.yml` publishes a tag only when [require-green-gates.sh](scripts/require-green-gates.sh) finds a full green run of each required workflow on its commit: a dispatch, the weekly schedule or the tag's own run (D-0403). Cut a release by dispatching `ci.yml` at the commit and tagging once it is green; no local run is needed.

A manually dispatched workflow must exist on the default branch. Use `--ref` to select the branch or tag to run. See [GitHub's manual workflow procedure](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow).

## Git

Author every commit as `keypaste <contact@keypaste.com>`, including agent-written commits. Use the project identity in first-party pages and metadata while retaining required upstream attribution. Use a subject of at most 72 characters and a matching `Signed-off-by` trailer; include no other body unless requested. Correct an unintended identity before pushing. Merge locally because the GitHub merge button supplies its own identity.

## Optional symbol index

Use graphify only when `graphify-out/graph.json` exists. It indexes `src/`; read documents directly for scope and status. `graphify query`, `graphify path` and `graphify explain` inspect symbols and relationships. If using the index, run `graphify update .` after source changes. The index and its machine-specific hook are gitignored and are not build prerequisites.
