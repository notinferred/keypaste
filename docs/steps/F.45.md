# F.45 — Test only what a change touches

Completed 2026-10-02 in `aeb948f`, scripts and workflows only; dev run 37028807128 on the `ci-scope` branch. It had no STEPS row: the founder directed it after a website commit ran the whole product suite ("Everything should be in packages and tested in such if modified").

## What changed

- **Pushes to `main`.** `ci.yml`'s scope job plans a push from the commit before it, as a pull request is planned from its merge base. A push whose earlier commit is missing from history, such as a new branch or a force-push, still runs every lane. A weekly schedule joins dispatches as the runs that test everything.
- **The release gate.** `require-green-gates.sh` counts only a full run toward a release: a dispatch, the schedule, or a tag's own push. A push to `main` no longer counts, so cutting a release starts by dispatching `ci.yml` at the commit. `verify-green-gates.sh` gives each fixture its event and adds `ci-green-only-on-a-main-push`, which must refuse.
- **The website.** A `site/public/index.html` edit selects only the scripts lane, where `verify-release-matrix.sh` checks its install blocks, floors and disclosures; its transcripts are checked in the next run that builds the binaries. A `site/wrangler.jsonc` edit no longer selects the compat lane. The fields gate still runs the real deploy config whenever share code changes and in every full run, so a separate test config was not introduced.
- **A pipe race.** `require-release-assets.sh` piped `printf` into `grep -q` under `pipefail`. When `grep` exited at its match before `printf` had written, the pipeline failed, and a valid asset was refused as "not a release asset". Dev run 37025563723 caught it in `verify-release-preflight.sh`'s `assets-complete` case, with `printf: write error: Broken pipe`. The same pattern in `verify-release-completion.sh` and `verify-site-disclosure.sh` becomes a here-string too.

## Evidence

- Dev run 37028807128 ran the plan for `aeb948f`, which selects every lane because `scripts/verify.sh` and `ci.yml` changed. Its scripts and workflows job passed in 1m19s: `verify-green-gates.sh` ran 15 cases and refused `ci-green-only-on-a-main-push`, and `verify-release-preflight.sh` accepted `assets-complete`. The Ubuntu test job passed in 21m18s. The run left aot, appcompat, markers, package, Windows and macOS to `ci.yml` and `app.yml`.
- `verify.sh --since origin/main --plan` on the change selected every lane, as a change to the runner should.
- The release-assets race was observed once, in dev run 37025563723, and passed in `main`'s run 37027506001 of the same script, which is the intermittency a pipe race shows. The here-string removes the pipe.

## Decisions

- D-0403.

## Limits and follow-ups

- A home-page transcript edit is checked only in the next run that builds the binaries: a CLI, README or demo-page change, a dispatch or the weekly schedule.
- A push to `main` that only edits `site/wrangler.jsonc` is not tested by the fields gate until a full run.
- The first scheduled run, and the first release cut under the new gate, are unobserved.
