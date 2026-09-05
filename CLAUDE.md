# Working rules for this repository

## CI

`ci.yml` runs in full on every push to `main`, every pull request, and on dispatch. `app.yml` does the same for the desktop app and packages it on a `v*` tag. **Pushes to a feature branch run nothing.** Runners are Blacksmith at a flat rate, so nothing is scoped by what a push touched, and a run on `main` is never cancelled.

- **Commit as you go. Push once per finished, verified unit of work.**
- **Docs-only pushes skip both workflows** via `paths-ignore`. Five pages must never be in that list: `README.md`, `launch.md`, `docs/demo.md`, `docs/keepass-and-agents.md`, `site/public/index.html`. `scripts/verify-demo.sh` holds them to what the binaries print. Never add a `docs/**` entry.

## Releases are immutable

A `v*` tag builds four native binaries and publishes them to `dl.keypaste.com`. The publish job **refuses to overwrite a version that already exists**, so a bad run burns that version number and the next attempt needs a new one. Verify what can be verified before tagging.

`workflow_dispatch` resolves against the **default branch**. A workflow that exists only on a feature branch cannot be dispatched at all — it returns 404 — so a gate written alongside the thing it gates has to land on `main` first, by itself.

## Git

- **Merge locally. Never the GitHub merge button** — it stamps its own identity on the merge commit.
- Commit messages are a subject line only, no body unless asked.

## Records

- `docs/PRODUCT.md` — the constitution. §3 does not change; the rest changes only by a dated re-ratification. If a decision conflicts with it, the decision is wrong.
- `docs/STEPS.md` — the plan. One row per step: tier tag, Build line, Verify line with *fails if*. Pick up the first unticked row of the lowest tier in the lowest area, skipping BLOCKED. A step exists only if its Verify line can fail and it traces to `docs/PRODUCT.md`; otherwise it is an Ideas line.
- `DECISIONS.md` — one line per decision, and only when architecture, security or money changes. Ideas are one line each. The archive below the line is frozen.

**Rewrite, don't append.** When something changes, say what is true now and delete the old text. Git holds history. A claim on a published page may only say what a gate holds (D-0036).

## graphify — optional, local, and not something this repository ships

`graphify-out/` is a symbol index somebody may have generated on their own machine. It is gitignored, no gate reads it, and a fresh clone has none — so everything here is conditional on `graphify-out/graph.json` actually existing, and nothing below is a reason to hold up work when it does not.

- **It indexes `src/`, so use it for questions about `src/`.** `graphify query "<question>"` for a scoped subgraph, `graphify path "<A>" "<B>"` for a relationship, `graphify explain "<concept>"` for one concept. `graphify-out/GRAPH_REPORT.md` is for broad architecture review only.
- **It is the wrong index for the documents**, which is most of what changes here. Asked for the project's status it returns xUnit method names; `docs/STEPS.md` answers that in one page. Read the governance files directly.
- `graphify update .` after changing code, if you are using it at all. AST-only, no API cost.

The hook that enforces this lives in `.claude/settings.local.json` and is gitignored with it, because its command is an absolute path to one machine's binary.
