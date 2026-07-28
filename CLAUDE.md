# Working rules for this repository

## CI costs money, and pushes are what spend it

`ci.yml` runs on pushes to `main`, on pull requests, and on dispatch. **Pushes to a feature branch
run nothing.** A push of five commits is one CI run, so committing locally is free and only the
push is billable.

- **Commit as you go. Push once per finished, verified unit of work.** Not once per edit, and not
  once per file. If the work isn't done or isn't checked, it isn't ready to leave the machine.
- **Docs-only pushes skip `ci.yml`** via `paths-ignore`. The list is deliberately an allow-to-skip:
  anything not named there runs the full workflow, so a new file is covered by default. The
  governance documents are gated by `docs.yml` instead — bash only, no restore, seconds.
- **Five documents are excluded from that skip and must stay excluded:** `README.md`, `launch.md`,
  `docs/demo.md`, `docs/keepass-and-agents.md`, `site/public/index.html`. `scripts/verify-demo.sh`
  holds each of them to what the shipped binaries actually print, so editing one is a code change
  wearing a markdown extension. `scripts/verify-docs.sh` asserts that absence rather than trusting
  this paragraph, and refuses a `docs/**` entry, which would skip two of the five silently.
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

- `docs/PRODUCT.md` — the locked core. §1–6 do not change. If a decision conflicts with it, the
  decision is wrong. §2 is the "out, deliberately" list and is the ratchet.
- `docs/STEPS.md` — every step to the finished product. Done steps are one line; open steps carry
  **Build** (the agent-runnable prompt), **Owner** (what only a human can do) and **Verify** (the id
  of an independent check). The Owner Queue is at the top.
  **The admission rule:** a step may be added only if it has an accept criterion that can *fail*,
  names its verifier, and traces to a claim in `docs/PRODUCT.md`. Fails any one → it is a
  `docs/IDEAS.md` row, not a step. This is what stops the plan growing forever.
- `docs/verification.md` — one cold-run verifier per open step, each with a falsifier. A verifier
  gets that file and the repo — **never the Build lane, never the builder's transcript**, because
  shared context is how a build and its check agree with each other while both are wrong. Run the
  falsifier first. Results are PASS / FAIL / BLOCKED; "looks right" is not a result.
- `docs/IDEAS.md` — append-only: idea · who · status · why. Flip a status; never delete a row.
- `docs/ARTIFACTS.md` — what informs the product and is not in git, by location, never by value.
- `DECISIONS.md` — the reasoning, as `D-NNNN` records and `O-NNNN` open questions.

**Scrap, don't append.** When something moves, rewrite the section and delete what it replaced.
No migration narratives, no was/now, no dead names. Git holds what the file used to say.

A claim on a published page may only say what a gate or a citation can hold (D-0036). If a check
has never actually executed, it is an assertion about the world and not a check on it (D-0043) —
run it before trusting the green. Every rule here names its enforcer; the ones that cannot be
mechanized are tagged `[process]` and are second-class until they can be.
