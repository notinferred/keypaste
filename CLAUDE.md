# Working rules for this repository

## CI costs money, and pushes are what spend it

`ci.yml` runs on pushes to `main`, on pull requests, and on dispatch. **Pushes to a feature branch
run nothing.** A push of five commits is one CI run, so committing locally is free and only the
push is billable.

- **Commit as you go. Push once per finished, verified unit of work.** Not once per edit, and not
  once per file. If the work isn't done or isn't checked, it isn't ready to leave the machine.
- **Docs-only pushes skip CI** via `paths-ignore` in `ci.yml`. The list is deliberately an
  allow-to-skip: anything not named there runs the full workflow, so a new file is covered by
  default.
- **Five documents are excluded from that skip and must stay excluded:** `README.md`, `launch.md`,
  `docs/demo.md`, `docs/keepass-and-agents.md`, `site/public/index.html`. `scripts/verify-demo.sh`
  holds each of them to what the shipped binaries actually print, so editing one is a code change
  wearing a markdown extension.
- **Superseded runs on `main` are cancelled.** Only the newest commit is tested. `release.yml` is
  the deliberate exception and never cancels.

Before adding a workflow, ask what it costs on every push, not just what it proves.

## Releases are immutable

A `v*` tag builds four native binaries and publishes them to `dl.keypaste.com`. The publish job
**refuses to overwrite a version that already exists**, so a bad run burns that version number and
the next attempt needs a new one. Verify what can be verified before tagging.

`workflow_dispatch` resolves against the **default branch**. A workflow that exists only on a
feature branch cannot be dispatched at all — it returns 404 — so a gate written alongside the thing
it gates has to land on `main` first, by itself.

## Git

- **Merge locally. Never the GitHub merge button** — it stamps its own identity on the merge commit.
- Commit messages are a subject line only, no body unless asked.

## Records

- `docs/STEPS.md` — roadmap status, one line per item.
- `DECISIONS.md` — the reasoning, as `D-NNNN` records and `O-NNNN` open questions.
- `prompts.md` — the per-stage prompts and their checkboxes. **Tick the box; never add prose under
  a prompt.** Status commentary goes in `docs/STEPS.md`, reasoning goes in `DECISIONS.md`.
- `docs/PRODUCT.md` does not change.

A claim on a published page may only say what a gate or a citation can hold (D-0036). If a check
has never actually executed, it is an assertion about the world and not a check on it (D-0043) —
run it before trusting the green.
