# Working rules for this repository

## CI costs money, and pushes are what spend it

`ci.yml` runs on pushes to `main`, on pull requests, and on dispatch. **Pushes to a feature branch run nothing.** A push of five commits is one CI run, so committing locally is free and only the push is billable.

- **Commit as you go. Push once per finished, verified unit of work.** Not once per edit, and not once per file. If the work isn't done or isn't checked, it isn't ready to leave the machine.
- **Docs-only pushes skip `ci.yml`** via `paths-ignore`. The list is deliberately an allow-to-skip: anything not named there runs the full workflow, so a new file is covered by default. The governance documents are read by no gate at all, which is why they skip it.
- **Five documents are excluded from that skip and must stay excluded:** `README.md`, `launch.md`, `docs/demo.md`, `docs/keepass-and-agents.md`, `site/public/index.html`. `scripts/verify-demo.sh` holds each of them to what the shipped binaries actually print, so editing one is a code change wearing a markdown extension. **Never add a `docs/**` entry to `paths-ignore`** — it would skip two of the five silently, turning that gate off without turning anything red. A script used to assert this rather than trusting the paragraph; it was deleted with the rest of the document ceremony, so the paragraph is what you have.
- **Superseded runs on `main` are cancelled.** Only the newest commit is tested. `release.yml` is the deliberate exception and never cancels.

Before adding a workflow, ask what it costs on every push, not just what it proves.

## Releases are immutable

A `v*` tag builds four native binaries and publishes them to `dl.keypaste.com`. The publish job **refuses to overwrite a version that already exists**, so a bad run burns that version number and the next attempt needs a new one. Verify what can be verified before tagging.

`workflow_dispatch` resolves against the **default branch**. A workflow that exists only on a feature branch cannot be dispatched at all — it returns 404 — so a gate written alongside the thing it gates has to land on `main` first, by itself.

## Git

- **Merge locally. Never the GitHub merge button** — it stamps its own identity on the merge commit.
- Commit messages are a subject line only, no body unless asked.

## Records

- `docs/PRODUCT.md` — the constitution. §3 does not change. §1, §2 and §4–6 change only by a dated re-ratification recorded as a `D-` row; the current one is v1.1, D-0061. If a decision conflicts with the current text, the decision is wrong. §2 is the "out, deliberately" list and is the ratchet.
- `docs/STEPS.md` — the whole plan, in one file, grouped by area. Every step is one row: a tier tag (`[MVP]` the first dollar, `[Launch]` the Free plan finished, `[Scale]` the Team plan), a Build line, and a **Verify** line — an id, what must hold, and *fails if*. Human actions are rows too, carrying their `H-` id. Pick up the first unticked row of the lowest tier in the lowest area. **The admission rule:** a step may be added only if it has an accept criterion that can *fail*, carries that Verify line, and traces to a claim in `docs/PRODUCT.md`. Fails any one → it is a `docs/IDEAS.md` row, not a step. This is what stops the plan growing forever.
- **Run the Verify line first**, and a verifier gets that file and the repo — **never the builder's transcript**, because shared context is how a build and its check agree with each other while both are wrong. Results are PASS / FAIL / BLOCKED; "looks right" is not a result and BLOCKED is not a pass. This is `[process]`: `docs/verification.md` and `scripts/verify-docs.sh` used to enforce it and were deleted on 2026-09-04, because the bookkeeping cost more than it caught on a list of a dozen items. The checks moved into the steps as Verify lines and earn their keep; the symmetry checks did not.
- `docs/IDEAS.md` — append-only: idea · who · status · why. Flip a status; never delete a row.
- `docs/ARTIFACTS.md` — what informs the product and is not in git, by location, never by value.
- `DECISIONS.md` — the reasoning, as `D-NNNN` records and `O-NNNN` open questions.

**Scrap, don't append.** When something moves, rewrite the section and delete what it replaced. No migration narratives, no was/now, no dead names. Git holds what the file used to say.

A claim on a published page may only say what a gate or a citation can hold (D-0036). If a check has never actually executed, it is an assertion about the world and not a check on it (D-0043) — run it before trusting the green. Every rule here names its enforcer; the ones that cannot be mechanized are tagged `[process]` and are second-class until they can be.

## graphify — optional, local, and not something this repository ships

`graphify-out/` is a symbol index somebody may have generated on their own machine. It is gitignored, no gate reads it, and a fresh clone has none — so everything here is conditional on `graphify-out/graph.json` actually existing, and nothing below is a reason to hold up work when it does not.

- **It indexes `src/`, so use it for questions about `src/`.** `graphify query "<question>"` for a scoped subgraph, `graphify path "<A>" "<B>"` for a relationship, `graphify explain "<concept>"` for one concept. `graphify-out/GRAPH_REPORT.md` is for broad architecture review only.
- **It is the wrong index for the documents**, which is most of what changes here. Asked for the project's status it returns xUnit method names; `docs/STEPS.md` answers that in one page. Read the governance files directly.
- `graphify update .` after changing code, if you are using it at all. AST-only, no API cost.

The hook that enforces this lives in `.claude/settings.local.json` and is gitignored with it, because its command is an absolute path to one machine's binary.
