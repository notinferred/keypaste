# F.30 — Bound every apt step and fit a full dev run on Windows

Completed 2026-09-30 on `task/f30-f39` above `71a8843`, workflows only; dev run 36750904505 at `098bec6`, the same workflows and script above `a662fc3`; ci 36753070272 and app 36753073956 passed at `07be53e` on `integrate`. This record also closes F.39.

## Amendments

F.39 was taken with F.30 because both change only workflow limits. Neither row's hosted probe ran: F.30's Verify asks for a branch probe that points a source at an address that never answers, and F.39's for an auto `dev.sh --os windows` run that selects every lane and both gates. Both remain the evidence that would confirm the repairs; see Limits. F.30's Verify asked for failure within two minutes; three two-minute update attempts bound it at about seven, because a single attempt that short would also cut off a slow but healthy mirror.

## What changed

**F.30.** Every `apt-get update` and `apt-get install` in `ci.yml`, `app.yml`, `dev.yml`, `release.yml`, `install-desktop.yml` and `upgrade-desktop.yml` now goes through [apt-install.sh](../../scripts/apt-install.sh), which passes apt's own `Acquire::Retries=3`, `Acquire::http::Timeout=30` and `Acquire::https::Timeout=30`, stops each `apt-get update` after two minutes and tries it three times, then stops the install after ten minutes. A mirror that never answers now fails the step in about seven minutes, where app run 36727975787 waited for its job's 20-minute limit. The four install-only steps that relied on an earlier update in their job now update again, which costs seconds and no longer depends on step order. `app.yml` watches the script's path, and the `workflows` profile shellchecks it. No action was added.

**F.39.** `dev.yml`'s `test` job limit rises from 30 to 50 minutes. The two cancelled Windows runs spent 10 and 13.5 minutes in the backend step and 11.5 and 10 in the desktop step before setup, the gates and the cache save; 50 leaves room for both gates and stays bounded.

## Evidence

`WorkflowRulesTests` still hold: every action pinned, every directly run script committed 100755. The workflows call the script with `bash`, and it is committed 100755 regardless.

dev run 36750904505 at `098bec6`, `--class Keypaste.Core.Tests.WorkflowRulesTests --os linux --gates compat`: green in 571 s, `WorkflowRulesTests` 3 of 3 on `ubuntu-24.04`. The compat gate made the run install KeePassXC through `apt-install.sh`: the update fetched 12.5 MB in 2 s on its first attempt, the install fetched 30.8 MB in 4 min 23 s at 117 kB/s and set up KeePassXC 2.7.6, and every compat gate passed on it. The new options and the update loop therefore ran once against a slow but answering mirror, which the ten-minute install bound let finish; the retry path itself was not exercised.

## Decisions

None.

## Limits and follow-ups

The retry and timeout paths are unproven on a runner: no probe pointed a source at an address that never answers. `Acquire::http::Timeout` bounds connection and idle time, not a mirror that trickles data; the `timeout` wrapper is what bounds that. A stalled `apt-get install` fails after ten minutes rather than retrying.

The 50-minute limit is sized from two runs' step times, not from a green full Windows run; if one still overruns, the next repair is to split the backend and desktop steps across jobs rather than raise the limit again.
