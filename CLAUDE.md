# Working rules

## Writing

Write clean, minimal, self-documenting code. Prefer clear names and structure. Default to no comments; add a single line only to explain a non-obvious constraint or decision the code cannot express. Never restate the code or duplicate a document's explanation.

Write concise, connected prose without hard wrapping. Use headings, lists, tables and emphasis only when they help navigation or comparison. Preserve code blocks, transcripts and structured records. Remove repetition, filler, rhetorical contrasts, formulaic introductions and conclusions, and unnecessary qualifications. State what the evidence establishes and keep unresolved questions explicit. Do not invent facts or expand the task's scope.

## CI

`ci.yml` runs on qualifying pushes to `main`, every pull request and dispatch. Its push-level `paths-ignore` skips specified documents; triggered jobs run without path filtering, and runs on `main` are never cancelled. `app.yml` has a push allowlist for desktop, CLI, core and shared build/test inputs. Its pull requests and dispatches are unfiltered; matching version tags package the app. Feature-branch pushes require a pull request or explicit dispatch for remote evidence. The workflow files own the exact triggers.

Probes run the identical backend test command used by CI. Narrowing the suite changes concurrent load and can remove the condition being measured. Run a hosted probe when local reproduction is insufficient, and retain counts and diagnostic evidence.

`README.md`, `launch.md`, `docs/demo.md`, `docs/keepass-and-agents.md` and `site/public/index.html` must trigger `ci.yml`: `scripts/verify-demo.sh` checks their claims against the built binaries. Never add `docs/**` to `paths-ignore`. New documentation paths trigger backend CI unless explicitly ignored; documentation pull requests run both workflows.

keypaste.com deploys through Cloudflare's Git integration on pushes to `main`, watching `site/`, with root directory `site`, no build command and deploy command `npm run deploy` (D-0127). GitHub workflows do not query the live origin. Run [verify-site-disclosure.sh](scripts/verify-site-disclosure.sh) after a site deploy; [verify-site-endpoint.sh](scripts/verify-site-endpoint.sh) is also manual. Only the offline disclosure self-test runs in CI. `site/README.md` changes trigger a site deployment while skipping both GitHub workflows.

## Local verification and delivery

Run `./scripts/verify.ps1` in PowerShell or `bash scripts/verify.sh` in Bash after the final code and document edits. The PowerShell launcher selects Git Bash on Windows. The default `all` profile validates workflows, prepares both solutions and the separate consistency project, checks offline scripts and real CLI/MCP process interactions, and runs all three test targets in Release.

The command requires the pinned SDK, Git, jq, GNU timeout and running Docker. Linux and Git Bash supply GNU timeout; macOS can use `gtimeout` from coreutils. Process integration checks have an eight-minute deadline and a ten-second termination grace. Only `all` and `workflows` need Docker; `compat` also needs installed KeePassXC. Use `--list` to inspect commands, or select `backend`, `desktop`, `records`, `scripts`, `integration`, `workflows` or `compat` during development. `desktop` includes consistency tests. The script owns the command list; other operating systems, NativeAOT, packaging and public installation retain their separate gates.

Run locally executable checks before pushing. Use `records` after editing DECISIONS or STEPS, then `all` after final edits. Feature branches may hold reviewable checkpoints for pull requests or platform experiments, including a named failing regression. Record why remote execution is needed and retain its source SHA. Integrate a completed step as one coherent commit after its Build, verifier and records are complete.

Separate discovery from repair when the mechanism is unknown. A discovery row delivers a reproducible experiment and measured conclusion; its repair row depends on that result. Before a long probe, run a short preflight and state which outcomes distinguish the hypotheses. Keep instruments, readers and regressions in the tree. Retain the source SHA, platform, command, counts and limitations using [diagnostics.md](docs/diagnostics.md). An inconclusive run identifies the next experiment and leaves the diagnosis open.

Put reproducible defects in STEPS with a verifier and priority, including defects found during another task. The ideas table may link to the task. Preserve the failing observation until a regression and repair explain it; a passing retry does not close an intermittent defect.

## Releases

[RELEASE.md](docs/RELEASE.md) owns release procedures and the platform/channel matrix. Published version paths are immutable. [publish-release.sh](scripts/publish-release.sh) requires a positively verified empty destination. A partial publication requires a new version; never replace its objects. Complete available verification before tagging.

A manually dispatched workflow must exist on the default branch. Use `--ref` to select the branch or tag to run. See [GitHub's manual workflow procedure](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow).

## Git

Author every commit as `keypaste <contact@keypaste.com>`, including agent-written commits. Use the project identity in first-party pages and metadata while retaining required upstream attribution. Use a subject of at most 72 characters and a matching `Signed-off-by` trailer; include no other body unless requested. Correct an unintended identity before pushing. Merge locally because the GitHub merge button supplies its own identity.

## Records

Each fact has one authoritative owner; other documents link to it. Explicit user direction authorizes amendments within its scope. Product scope changes require dated re-ratification, a decision record and an updated plan; preserve the security laws in PRODUCT §3. Editorial changes preserve requirements and evidence.

| Document | Owns |
|---|---|
| [README](README.md) | Introduction, published installation instructions and navigation |
| [PRODUCT](docs/PRODUCT.md) | Ratified scope and laws; conflicting decisions do not override it |
| [STEPS](docs/STEPS.md) | Delivery status, ordered tasks, dependencies and acceptance evidence |
| [RELEASE](docs/RELEASE.md) | Distribution matrix, publication and installation verification |
| [FEATURES](docs/FEATURES.md) | Dated capability baseline, implementation evidence and gaps |
| [ALIGNMENT](docs/ALIGNMENT.md) | Adopted direction and links to its owning documents |
| [DECISIONS](DECISIONS.md) | Architecture, security and money decisions, pending ideas, and links to historical decisions; one line per current record |
| [CHANGELOG](CHANGELOG.md) | Significant user-visible changes, separating Unreleased work from published versions |
| [SECURITY](SECURITY.md) and [THREATS](THREATS.md) | Reporting, security guarantees, threat model and known limits |
| [BRAND](docs/BRAND.md) | The marks, the three colours, the type and the usage rules |
| This file | Contribution workflow and document ownership |

STEPS is the executable build plan. Keep only the next five tasks detailed; later rows carry an ID, name, Needs and purpose. Each detailed task has a bounded Build and a falsifiable Verify prompt that traces to PRODUCT. Split oversized work into children while preserving IDs and dependencies. Completing a child leaves its siblings and parent gate open. Completed work moves to a concise evidence row, and the next task gains detail.

A task's Needs name only what its own code or verifier cannot run without. Time, human acts, milestone or release gates, freezes and tidier ordering are never Needs. Gates decide what may ship through Ships after, and Human rows are a separate track. Protect a recorded result by versioning or voiding it, not by a test or rule that forbids change.

Pick the first unchecked code task with completed Needs in the earliest unfinished milestone, skipping Human and BLOCKED tasks. Follow an unchecked prerequisite to its first ready task even if it belongs to a closed milestone. Build, verify and record the selected task, then stop unless the user requested continued work. A milestone's numbered gate may advance it while optional rows remain open; those rows keep their dependencies.

During an external wait, record the pending result and its next decision. If the user authorized continuing delivery, work on the next ready code task, keeping its changes isolated. Resume the waiting task when evidence arrives. Nothing publishes before its Ships after gates pass.

Use RELEASE's four states: Implemented, Packaged, Published and Installation-verified. Record version, platform and evidence. Source code, a local demo or a green pipeline cannot establish an unfinished user journey or public release. Documentation and local implementation do not authorize publication, messages or customer-data changes.

Rewrite outdated text instead of appending another account. Keep current claims with their owners and history in Git. Published claims require supporting evidence (D-0036).

## Optional symbol index

Use graphify only when `graphify-out/graph.json` exists. It indexes `src/`; read documents directly for scope and status. `graphify query`, `graphify path` and `graphify explain` inspect symbols and relationships. If using the index, run `graphify update .` after source changes. The index and its machine-specific hook are gitignored and are not build prerequisites.
