# F.46 — Skip a lane a trusted run already passed on the same inputs

Completed 2026-10-02 in `392d128`, scripts and workflows only; full ci run 37036760821 dispatched on the `ci-cache` branch. It had no STEPS row: the founder asked for CI to work "like docker, hashing it to see if its already built and tested", and chose to build it now.

## What changed

- **Markers.** A passing job leaves a 30-day artifact named `pass--<runner>--<lane>+<lane>…` on its run. In `ci.yml` that is the scripts lane on `checks`, each runner's lanes on `test`, and `aot`. In `dev.yml` it is the scripts lane, and the lanes a runner ran in full: its backend projects, with `rules` only under Core's rules filter, and its integration and compat gates. dev's checks now run the real-aws destination check too, so its scripts marker vouches for what ci's does.
- **[lane-cache.sh](../../scripts/lane-cache.sh)** reads the markers. It trusts only runs that write access starts: `dev.yml` dispatches, and `ci.yml` dispatches, schedules and pushes to `main`, from this repository. For each of the 20 newest trusted commits, it plans `git diff <marker> HEAD` with `verify.sh`. A lane the marker names and that diff leaves untouched has passed on these inputs. Git's trees are content hashes, so an identical tree passes every lane the marker names. Any failure prints nothing, and nothing printed runs every lane.
- **The planner.** `verify.sh --plan` prints `test_matrix`: one entry per runner, with that runner's lanes, test projects and filter, less the runner:lane pairs in `VERIFY_SATISFIED`. `lanes` drops a lane only once every runner it needs has passed it. `rules` and `pages` now run on Ubuntu alone; they ran on every runner only when another lane put all three in the matrix.
- **`ci.yml`.** For a push or a pull request, the scope job asks `lane-cache.sh` and plans with its answer; its token gains `actions: read`. A dispatch and the schedule never ask, so a release's full run still runs every lane. The test job takes its matrix from `test_matrix`.

## Evidence

- Full ci run 37036760821 on `392d128` passed every job: scripts and workflows, aot, and the test job on Ubuntu, Windows and macOS. It left five markers: `pass--ubuntu-24.04--scripts`, `pass--ubuntu-22.04--aot`, `pass--ubuntu-24.04--core+cli+mcp+rules+pages+integration+compat`, and `core+cli+mcp+integration+compat` for `windows-2025` and for `macos-15`.
- Dev run 37039700390, filtered to `WorkflowRulesTests` on Ubuntu, passed and left `pass--ubuntu-24.04--rules`: dev's marker names only the lane its filter covers.
- The first dispatch, run 37033989011 on `a020837`, failed `verify-ci-scope.sh`'s case "a core change, nothing passed". Adjacent lanes share one space, so the pattern `*" core "*" cli "*" mcp "*` never matched, and a runner needing all three named the three projects instead of `all`. `392d128` tests each lane on its own.
- `verify-lane-cache.sh`, a new script fixture with a fake `gh` and `git`, passed its four cases in that run and holds the following. Trusted markers on an identical tree, or on a commit differing only in docs or in other lanes' files, satisfy their lanes. An unclaimed path, a pull request, a fork, a push to another branch, an expired marker and a failed call satisfy nothing. Its negative control, a copy that trusts every event, honours the pull request's marker.
- `verify-ci-scope.sh` passed 31 cases, five of them new matrix cases. With nothing passed, a core change runs on all three runners; with Linux passed, only Windows and macOS. A passed page edit needs no runner. Rules beside the CLI run unfiltered, and rules alone keep their filter.

## Decisions

- D-0404.

## Limits and follow-ups

- `app.yml` neither leaves nor reads markers, so desktop lanes still run on every qualifying push.
- A marker does not record the runner image, so an image update reaches a cached lane only in the weekly full run, a dispatch, or the next change to that lane's inputs.
- Markers last 30 days, and lane-cache.sh reads the 300 newest artifacts.
- No push or pull request had planned with markers when this landed; this commit's push to `main` is the first.
