# Working rules for this repository

## Rules

- **Only the next five steps in `docs/STEPS.md` are detailed.** Later steps stay one line — ID, name,
  Needs and what it is for — and are rough on purpose; their wording is not a contract. When a step is
  finished it shrinks to one line under Completed steps and the next unchecked step is expanded in its
  place.

## CI

`ci.yml` runs in full on qualifying pushes to `main`, every pull request, and on dispatch. Its push-level `paths-ignore` skips specified documents; once triggered, its jobs are not filtered by changed paths, and a run on `main` is never cancelled. **`app.yml` is not its mirror.** On pushes to `main` it runs on a `paths:` allowlist — `src/Keypaste.App/**`, `src/Keypaste.Cli/**`, `src/Keypaste.Core/**`, `third_party/**`, the two `Directory.*.props`, `keypaste.app.slnx`, `release-targets.json` (which its package matrix is derived from), its own file, and the App/Cli/Consistency test projects — so a push touching only `src/Keypaste.Mcp/`, `scripts/` or `keypaste.slnx` does not trigger it. Pull requests and dispatch are unfiltered; matching version tags package the app. Feature-branch pushes alone trigger neither workflow. Runners are GitHub-hosted and free, because the repository is public (D-0086).

**`site.yml` is the third, and it deploys rather than gates.** On pushes to `main` it runs on a `paths:` allowlist — everything under `site/` that wrangler uploads, plus `release-targets.json` and the two site gates, which decide what the run concludes — and on dispatch. It has no pull-request trigger, because a fork’s pull request cannot read the deploy credential and a proposed change must not reach production. It checks the page it is about to deploy, runs `npm ci` and `wrangler deploy` from `site/`, prints the version id to the run log, then asks keypaste.com through [verify-site-disclosure.sh](scripts/verify-site-disclosure.sh) and [verify-site-endpoint.sh](scripts/verify-site-endpoint.sh). Its `CLOUDFLARE_API_TOKEN` is an **environment** secret whose deployment-branch rule names `main` alone, so no other ref can read it — it is the only credential here that can change a page the public reads. It is deliberately **not** a release gate: `require-green-gates.sh` requires `ci` plus every component’s workflow, the site is not a component, and making it one would let a Cloudflare outage block a CLI tag.

- **One commit per step, written when the step is done.** Not mid-step, not per file, not as you go. A step is done when its Build line is built, its verifier has run, and STEPS, DECISIONS and CHANGELOG say what is now true. Then commit once, push once.
- **Never push to find out whether a gate passes.** If a gate runs on this machine it runs here first. A commit whose subject repairs the commit before it is the evidence this was skipped — `d69fe91`, `138f43e`, `7779603` and `14dd809` are four of them, and ten of the eighteen red `ci.yml` runs on `main` between 2026-09-05 and 2026-09-11 were for something a local command catches in under twelve seconds, at 6.2 minutes a run. A deliberate red-first run in CI is the one exception, and only when the defect is platform-specific; say so in the commit subject.
- **`DECISIONS.md` and `docs/STEPS.md` are under test. After editing either, run the gate before pushing.** Ledger rows are capped at 650 characters and STEPS completed-evidence cells at 350, and CI catching either costs a full round trip and an extra commit:
  ```
  dotnet test tests/Keypaste.Core.Tests/Keypaste.Core.Tests.csproj -c Release \
    -- --filter-class 'Keypaste.Core.Tests.RecordRowsStaySkimmableTests'
  ```
- **Run the full suite last, after the documents are written, not before.** A suite run that precedes the DECISIONS edit does not cover it. The pre-push sequence, in order and measured on Windows 10 Pro 19045:

  | When | Command | Cost |
  |---|---|---|
  | Always | `dotnet build keypaste.slnx -c Release -warnaserror` | 2 s |
  | Always | `dotnet format keypaste.slnx --no-restore --verify-no-changes --exclude third_party/` | 10 s |
  | After editing DECISIONS.md or docs/STEPS.md | the records gate above | 1 s |
  | If one of the five gated pages changed | `bash scripts/verify-demo.sh` | 20 s |
  | If `release-targets.json` or a download page changed | `bash scripts/verify-release-matrix.sh` | 87 s |
  | Last, always | `dotnet test keypaste.slnx --no-build -c Release` | 28 s |

- **A probe runs the byte-identical command `ci.yml` runs, and this is tested rather than assumed.** F.8 reproduced in `LargeVaultListingTests`, not in the `ListingSizeTests` class the probe was written for, so narrowing the command to the suspect class would have caught nothing. `dotnet test` runs the assemblies concurrently and that contention is usually the condition under test. A probe earns its runner time when the defect does not reproduce on this machine **and** the probe yields a count rather than a yes or no: [txf-probe.yml](.github/workflows/txf-probe.yml) gave D-0122 its 1387 of 1600, and [listing-probe.yml](.github/workflows/listing-probe.yml) gave F.8 and F.9 their 12 of 80 on one dispatch.
- **Some docs-only pushes to `main` skip all three workflows.** `ci.yml` skips a push only when every changed path is in its `paths-ignore`; `app.yml` skips documentation because it is outside its allowlist. Five pages deliberately trigger `ci.yml` and must never enter its ignore list: `README.md`, `launch.md`, `docs/demo.md`, `docs/keepass-and-agents.md`, `site/public/index.html`. `scripts/verify-demo.sh` holds them to what the built binaries print. New documentation paths also trigger CI unless explicitly ignored. Never add a `docs/**` entry. Documentation pull requests still run both of those. `site.yml` skips everything except its own allowlist, and `site/README.md` is outside all three.

## Releases are immutable

The release procedure and platform/channel support matrix live in [docs/RELEASE.md](docs/RELEASE.md). Published version paths must be immutable. `release.yml` publishes through [publish-release.sh](scripts/publish-release.sh), which refuses any destination it cannot positively verify as empty and is held to that by [verify-release-destination.sh](scripts/verify-release-destination.sh) (F.4a). If publication leaves a partial version, use a new version rather than replacing its objects — the guard will refuse the old one, which is the intended behaviour. Verify what can be verified before tagging.

For `workflow_dispatch`, the workflow file must exist on the **default branch** before it can be dispatched. A dispatch can then select a branch or tag with `--ref`; it does not always run the default branch. See [GitHub's manual workflow procedure](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow).

## Git

- **Merge locally. Never the GitHub merge button** — it stamps its own identity on the merge commit.
- Commit messages are a subject line only, no body unless asked.
- **Every commit is authored as the project: `keypaste <contact@keypaste.com>`.** This is a
  pseudonymous project and no individual's name or personal address belongs in its history, its
  pages, or its metadata. Set globally, enforced locally by `.git/hooks/pre-commit`, and required to
  match the `Signed-off-by` trailer by `dco.yml`. If a commit is ever authored otherwise, fix it
  before it is pushed — after a push it is public and only deleting the repository takes it back,
  which is what D-0087 cost once already.

## Records

Each fact has one authoritative owner; other documents link to it. Explicit user direction authorizes amendments within its scope; do not leave an accepted change only in a proposal or ask the user to repeat that authorization. Amend PRODUCT by its dated re-ratification rule, preserve §3, record the reason in DECISIONS, and update STEPS in the same change. A pending proposal does not amend scope, and a documentation change does not make a feature built.

| Document | Owns |
|---|---|
| [README](README.md) | Introduction, published installation instructions and navigation |
| [PRODUCT](docs/PRODUCT.md) | Ratified product scope and laws. §3 does not change; the rest changes only by a dated re-ratification. If a decision conflicts with it, the decision is wrong. |
| [STEPS](docs/STEPS.md) | Current delivery status, ordered tasks, dependencies and acceptance evidence |
| [RELEASE](docs/RELEASE.md) | Release procedure, distribution support matrix and publication/install verification requirements |
| [FEATURES](docs/FEATURES.md) | Dated feature baseline and comparison inventory, with implementation evidence and gaps |
| [ALIGNMENT](docs/ALIGNMENT.md) | Concise explanation of the adopted direction and its owner-document mapping; no independent status or task queue |
| [DECISIONS](DECISIONS.md) | Lasting decisions and reasons, and pending ideas. One line per decision, only when architecture, security or money changes; ideas are one line each. The archive below the line is frozen. |
| [CHANGELOG](CHANGELOG.md) | Significant user-visible changes, separating Unreleased work from published versions |
| [SECURITY](SECURITY.md) and [THREATS](THREATS.md) | Reporting and security guarantees; threat model and known limits, respectively |
| This file | Contribution workflow and document ownership; topic guides hold their local usage instructions |

`docs/STEPS.md` is the single executable build plan. It starts with **Current status** and **Build order**, then groups tasks under **Working proposition**, **Pilot ready**, **Paid release**, **Expansion** and **Scale**. Commercial plans belong in PRODUCT and do not set task priority. Each checkbox is one bounded task with a stable ID, explicit **Needs**, a **Build** prompt and a falsifiable **Verify** prompt. Split oversized work into lettered children; completing one child does not complete its siblings or parent gate. Record concise evidence beside completed work rather than copying implementation history into the plan.

**Pick the first unchecked task with completed Needs in the earliest unfinished active milestone, skipping BLOCKED tasks.** Passing its numbered gate advances the milestone even if explicitly optional rows remain open; those rows retain their status and later dependencies. Build and verify the selected task, update its checkbox and evidence, then stop unless the user requested several tasks. A named later task may be prepared when its prerequisites exist; preparation cannot bypass milestone or publication gates. Advanced KeePassXC coverage is accepted Expansion scope after the Working proposition gate; team work follows the Pilot ready gate and a selected pilot scope. Scale activates only on its recorded evidence trigger. Follow STEPS for the exact task dependencies and activation evidence.

A task exists only when its verifier can fail against repository artifacts or retained operational evidence and it traces to PRODUCT. Preserve meaningful IDs and references when moving work. Explicitly distinguish existing code, remaining behavior and external acceptance: source implementation, a local demo or a successful pipeline cannot close an unfinished user journey or public release gate. Keep future prompts brief until activated. A documentation or local implementation task does not authorize publishing, sending messages or changing customer data.

Use the four states defined in [RELEASE.md](docs/RELEASE.md#status-vocabulary): **Implemented**, **Packaged**, **Published** and **Installation-verified**. Record the version, platform and evidence; one state does not establish the next. A workflow artifact is not a public release, and a feature in `main` is not necessarily in the latest download.

If a gate needs an unchecked prerequisite in another section, follow that prerequisite to its first ready task before attempting the gate, including optional work left in a closed milestone. External action evidence cannot be inferred from elapsed time or local preparation.

**Rewrite, don't append.** When something changes, say what is true now and delete the old text. Git holds history. A claim on a published page may only say what a gate holds (D-0036).

## graphify — optional, local, and not something this repository ships

`graphify-out/` is a symbol index somebody may have generated on their own machine. It is gitignored, no gate reads it, and a fresh clone has none — so everything here is conditional on `graphify-out/graph.json` actually existing, and nothing below is a reason to hold up work when it does not.

- **It indexes `src/`, so use it for questions about `src/`.** `graphify query "<question>"` for a scoped subgraph, `graphify path "<A>" "<B>"` for a relationship, `graphify explain "<concept>"` for one concept. `graphify-out/GRAPH_REPORT.md` is for broad architecture review only.
- **It is the wrong index for the documents**, which is most of what changes here. Asked for the project's status it returns xUnit method names; `docs/STEPS.md` answers that in one page. Read the governance files directly.
- `graphify update .` after changing code, if you are using it at all. AST-only, no API cost.

The hook that enforces this lives in `.claude/settings.local.json` and is gitignored with it, because its command is an absolute path to one machine's binary.
