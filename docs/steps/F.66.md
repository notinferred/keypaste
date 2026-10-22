# F.66 — Zero-waste CI

Completed 2026-10-20 on `main` at `c70f970`, source only: ci 39514599266 and app 39514599272.

It had no STEPS row. The founder said on 2026-10-20 that each round trip took too long, and that it was an issue both that it took this long and that I had not noticed. They asked for a plan to remove wasteful CI and prevent more, where time matters most and compute too, and approved it the same day.

## Amendments

- **Founder choices.** The founder chose four remedies:
  - a compile check before every push, as a rule;
  - a diagnosis of the slow tests;
  - retrying a one-runner failure on a branch;
  - splitting the test jobs.

  They then replaced the split with Blacksmith's paid runners under a rule that manages them, "zero wasteful CI".
- **Push set.** Which jobs run on every push is decided after the test fix, with these numbers.
- **"Idk" answers.** On how to fix the slow tests and how to retry a gate, the founder answered "Idk" and approved the plan's recommendations: D-0469 and `dev.sh --profile`.
- **Blacksmith** waits for the founder to install its GitHub App, so no job runs there yet.

## Diagnosis

| Cause | Evidence |
|---|---|
| A dispatch waited behind its commit's push run | ci and app grouped runs by ref and never cancelled on `main`. The tools dispatch at `413afc3` started 694 s after it was made, when the push's ci ended at 709 s. A group also keeps only one pending run, so a third push could replace the second's. |
| Tests derived every key at the production cost | Probe 39504250962 ran the backend tests at both costs. A single production derivation took about 50 ms on Ubuntu, whose runner has a native Argon2. Elsewhere it took 190–720 ms, because KeePassLib finds no native library on Windows or macOS (table below). |
| Build errors reached CI | Six pushes failed on build or format within 2–3 min on 2026-10-19 and 2026-10-20. |
| Fix attempts reran everything | Fixing a one-runner failure reran about 29 jobs per attempt, and no mode reran one gate. |
| Jobs queued | GitHub's free plan runs 20 jobs at once and 5 on macOS (F.52). A push starts about 29 jobs, 8 of them on macOS. |
| Job limits far above real times | Limits of 15–30 min against 1–11 min of work let the `637eda8` stall run 24 min. |

Backend and desktop test time from the probe, in seconds:

| Runner | Backend, production | Backend, cheap | Desktop, production | Desktop, cheap |
|---|---|---|---|---|
| Windows | 534 | 106 | not reached | not reached |
| macOS | 463 | 89 | 431 | 167 |
| Ubuntu | 199 | 49 | 230 | 151 |

On Windows the probe stopped after the backend tests, because GitHub runs bash with `-e` and a failing test ended it. That failure is described under Limits.

## What changed

- **The rule** (D-0468, CLAUDE.md's "CI spends nothing that cannot change a verdict"):
  - compile before pushing;
  - one run per commit;
  - a one-runner failure retried on that runner from a branch;
  - a flake reruns only its failed jobs;
  - each job bounded at about twice its slowest green time;
  - paid runners only where measured, and never for jobs that sign, attest, package or read secrets;
  - fork pull requests wait for approval;
  - runs followed with `gh run watch`.
- **`verify.sh compile`.** A locked restore, a `-warnaserror` build of both solutions and the consistency project, and `dotnet format --verify-no-changes`. The build servers shut down even when it fails. It runs with the pinned SDK already in `~/.dotnet` and takes 45–54 s.
- **Workflows.**
  - On `main` and tags, each commit and event has its own concurrency group.
  - ci takes `only_tools`, which runs only the tools jobs. `require-green-gates.sh` counts a ci run only when no job was skipped.
  - dev.yml has a `profile` mode, and `dev.sh --profile` runs one profile on one runner.
  - Each job's `timeout-minutes` is about twice its slowest green time. The test and desktop-test jobs use only the runs after the test fix, since it changed their times. The limits are now 2–19 min, against 5–30 before.
- **Tests** (D-0469). `TestKeyDerivation` gives vaults a test assembly creates Argon2d at one pass over 1 MiB. Each test project turns it on from a module initializer. `Keypaste.Mcp.Tests` joins Core's internals list so its vaults take the cheap cost too. `KeyDerivationCostTests` proves three things:
  - a vault created outside the hook carries 64 MiB, 2 passes and 2 lanes;
  - a test's vault carries the token cost with the same algorithm;
  - nothing under `src/` turns the hook on.

  Two tests time a derivation and keep the production cost:
  - `VaultBackupTests`, whose kill lands within one save's derivation;
  - `SaveTimingTests`, which weighs a save's work against its gate.
- **The locked app's knocks.** The faster runs exposed a race that G.4b's tests had hit only now and then.
  - **Windows.** Probe 39506879797 heard 199 of 200 back-to-back knocks. The one lost was a bridge that connected and hung up before the listener waited again, reported as `ERROR_NO_DATA`. The pipe admits only this user there, so the endpoint now counts it as heard.
  - **Unix.** At `bc78e94`, ci 39512225195 heard 182 of 300 on Ubuntu and 297 on macOS. Making the pipe anew for each connection closed its socket, and with it every bridge queued behind the one being answered. `KnockListener` now answers connection after connection on one instance there. Windows still makes each instance anew, since disconnecting discards a reply AppClaim's second start has not yet read.
- **Fork pull requests.** The repository's fork pull request approval moved from first-time contributors to all outside contributors.

## Evidence

- **Concurrency.**
  - At `b517996` the tools-only dispatch 39504813703 created its jobs 4–14 s after the push runs. At `413afc3` the dispatch had waited 694 s.
  - It ran only the three tools jobs, skipped every other, and used 9 runner-minutes against 69 for the full dispatch.
  - Its jobs still waited 568 s for free runners, behind the push's 29 jobs.
- **Release gate.** `verify-green-gates.sh` passes 22 cases, including `ci-tools-only`, which a release refuses.
- **Per push:**

| | `413afc3` (before) | `bc78e94` (after) |
|---|---|---|
| ci | 709 s, 60 runner-min | 494 s, 42 runner-min |
| app | 687 s, 77 runner-min | 537 s, 62 runner-min |
| `test` (macOS / Ubuntu / Windows) | 618 / 329 / 699 s | 117 / 162 / 289 s |
| `desktop tests` (macOS / Ubuntu / Windows) | 627 / 362 / 676 s | 301 / 290 / 377 s |

- **Knocks.**
  - LockedAppEndpointTests passed on all three runners in dev run 39513707064. That includes `BridgesInQuickSuccession_AreEachHeard`, 300 knocks.
  - AppClaimTests and SecondStartTests passed on all three runners in dev runs 39514028853 and 39514067228.
- **Final runs.** ci 39514599266 and app 39514599272 at `c70f970`, every job green, in 503 s and 510 s, using 42 and 62 runner-minutes.

**What failed on the way:**

- **`b080272`.** The test fix. ci 39507285378 failed `LockedAppEndpointTests.APeerThatNeverHangsUp_HoldsNothingUp` on Windows; app 39507285349 passed. This was the knock race above, which the cheap derivation made likelier.
- **`bc78e94`.** The Windows half of the knock fix. ci 39512225195 failed the new 300-knock test on Ubuntu (182 heard) and macOS (297), which led to the Unix half.
- **Local check.** `dotnet restore` segfaulted once, after hanging close to the check's 10-minute limit. Its rerun passed in 45 s, and the cause is unknown.
- **A rerun I lost.** A dev run of AppClaimTests never started, because I had switched the checkout back to `main` and `dev.sh` refuses it. I reran it from a separate worktree.

## Decisions

D-0468 and D-0469 in [DECISIONS](../../DECISIONS.md). D-0438 was amended for the tools-only dispatch. No guarantee, threat or limit in THREATS changed: shipped vaults keep the production cost, and a knock heard on Windows comes from this user, whom the pipe's access list alone admits.

## Limits and follow-ups

- **Blacksmith** (D-0468's paid runners) is not installed: CI stays on GitHub's runners by founder decision 2026-10-22, so no sizing probe runs.
- **The push set** stays every job, as D-0423 says, by the same decision. Jobs still queue for runners: app's queued up to 249 s after the fix.
- **`DrawnRevealTests`.** It failed for the env surface in probe 39504250962 on Windows at the cheap cost, as it had once on Ubuntu at `1c60e88` at the production cost. Diagnosed and fixed as [G.4b](G.4b.md) records: the cell's tooltip opened during the test's hover and left a popup layer (gaps-1, RUNS-PENDING).
- **The new limits** for the test and desktop-test jobs rest on two runs each. A run that hits a limit is a failure to diagnose, not a reason to raise the limit.
- **Production derivation on Windows and macOS** was managed code: 190–720 ms per unlock against about 50 ms with Ubuntu's native library. Probe 39825491861 measured Argon2d per pass at 64 MiB and 2 lanes: on one Ubuntu runner 25.6 ms native against 75.3 ms managed (2.5–2.9× across 2 or 4 lanes and 256 MiB), and 42.4 ms on Windows and 76.8 ms on macOS, where KeePassLib found no native library. A vault KeePassXC tuned to one second with native code therefore took about three seconds to unlock there, and as long for each save, which draws a new seed. By founder decision 2026-10-22 the app and the CLI now carry the reference libargon2 on Windows and macOS and derive with it, kept only where it measures faster than managed, and Linux keeps the system's ([D-0522, D-0523](../../DECISIONS.md)). `NativeArgon2Tests` prints each runner's time per pass at 64 MiB, 2 passes and 2 lanes, native against managed: Windows x64 —, macOS arm64 —, Ubuntu x64 — (gaps-1c, RUNS-PENDING). No run yet shows the NativeAOT `keypaste` deriving with it: every check that names the library runs the tests or the app, which are JIT builds, and on Windows KeePassLib picks its import from `RuntimeInformation.ProcessArchitecture` through reflection the trim leaves unresolved ([aot-trim-baseline.txt](../../scripts/aot-trim-baseline.txt)), where a missing property means managed code, the same key more slowly.
