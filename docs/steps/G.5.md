# G.5 — Carry the CLI on PATH in every desktop install

Completed 2026-10-11 at `724f854` on `task/g5`: the dispatched ci 38186459655 and app 38186466427 runs passed every job on all three runners, install-desktop rehearsal 38187074803 of app run 38186466427 passed on `linux-x64` and `win-x64`, and upgrade-desktop rehearsal 38187076840 passed its candidates and upgrade jobs on `linux-x64` and `win-x64`. The rehearsals are not release evidence. The integrated commit `1b71162` differs from that tree in nothing; this record lands separately, after the candidate's evidence.

Candidate `v0.5.0-rc.1` at `1b71162` on main: ci 38188337280 and app 38188339052 at that commit, the tag's app run 38189762439, release run 38189762435 on its second attempt (Limits and follow-ups), install-desktop run 38191303484 and upgrade-desktop run 38191305339, each passed. `https://dl.keypaste.com/v0.5.0-rc.1/` serves its four CLI/MCP archives, the source tarball and `SHA256SUMS`, and `c10fbc8` on main lists it in `release-targets.json` as published on 2026-10-12, the UTC date, and not advertised.

## Amendments

The founder settled the row's decisions on 2026-10-10 while planning the 0.5.0 rows, and answered further questions during execution on 2026-10-10 and 2026-10-11. D-0431, D-0432 and D-0433 record what it decided.

- **Where the link goes** (2026-10-10). The MSI adds its folder to the per-user PATH and removes it on uninstall, and the AppImage dispatches `cli` as it does `mcp`. Settings links the CLI. On macOS the link is `/usr/local/bin/keypaste`, made through the system's administrator dialog, which needs no Automation permission. Settings refuses to link from a translocated bundle. On Linux the link is `~/.local/bin/keypaste`, and Settings says when that directory is not on the session's PATH.
- **How the link starts keypaste** (2026-10-10). The plan let AppRun dispatch on how it was invoked, or let the link name a small wrapper, so that `keypaste --version` never starts the app. The link is a three-line `sh` script that starts the image with `cli`. On macOS, the plan's `osascript … with administrator privileges` runs a fixed `sh` program. That program receives every path as an argument, so no path is quoted into script text.
- **Re-pointing** (2026-10-10). A newer keypaste starting re-points the Linux link, because G.2 registers that link with MCP clients. Review found a gap: a newer image whose start was handed to an older app already running re-pointed nothing (Evidence). Every start now re-points the link, including one the running app takes.
- **A foreign file at the link path** (2026-10-10). Settings names it and offers Replace behind a confirmation. The old file is kept beside the link as `keypaste.keypaste-backup`. A backup already there is never overwritten, so Replace then changes nothing and says so. G.2's setup replaces through the same call.
- **Rehearsal before the candidate** (2026-10-10). `install-desktop.yml` and `upgrade-desktop.yml` also run on the branch's own builds as a rehearsal. A rehearsal verifies no attestation, names itself a rehearsal in its run name, artifacts and observation, and never counts as release evidence. `v0.5.0-rc.1` is tagged only after both rehearsals pass.
- **F.61 first** (2026-10-10). F.61 was integrated to main before G.5. The branch merged `task/f61` (`b900b6c`) and then main at `e8b245d` (`521f43c`) before its last full runs.
- **A stock Ubuntu login** (2026-10-10). The runner image's account has a `~/.bash_profile` that only loads nvm. A login bash that finds that file never reads `~/.profile`, which is what puts `~/.local/bin` on PATH. The first install rehearsal (run 38090054591 at `13f71b6`) found no `keypaste` in a new terminal for that reason. The founder chose to model a stock Ubuntu account. Before each new-shell probe, the exercises move that file aside, record what it held, and seed `~/.profile` from `/etc/skel` when the account has none (`48008f6`). The install rehearsal then ran again.
- **RELEASE's per-commit checks** (2026-10-10). RELEASE names the three new per-commit checks in one clause, as the founder asked: the MSI's PATH entry, the AppImage's `cli`, and the macOS link on the built bundle.
- **Proof by a release candidate** (2026-10-10). The founder authorised publishing `v0.5.0-rc.1`. It is tagged from main once G.5 lands and ci and app pass at that commit. Its CLI/MCP archives go to `https://dl.keypaste.com/v0.5.0-rc.1/` and, like every published version, are permanent. The desktop packages stay internal, unsigned workflow artifacts, and README, keypaste.com and the install guides do not mention the candidate. `install-desktop.yml` and `upgrade-desktop.yml` run against it on Windows and Linux and check `keypaste --version` in a new shell, reading HKCU's PATH and the shell profile as a new terminal would.
- **macOS on the built bundle** (2026-10-10). macOS has no leg in those workflows; that leg belongs to 4.7e. Instead, `app.yml` links the built bundle's `keypaste` at `/usr/local/bin` and checks it from a new login shell.
- **The macOS link is an accepted limit** (2026-10-10). The link points into a bundle that the installing account can write. The founder accepted this, and THREATS records it (T-39). Translocated paths are still refused.
- **A keypaste earlier on the macOS PATH** (2026-10-11). Review found that Settings said a terminal runs this app's `keypaste` from `/usr/local/bin/keypaste` even when a terminal would run another copy first. For example, the CLI README installs a copy in `~/.local/bin`, a folder a shell profile often puts first. The founder decided that Settings looks, without starting a shell, in the known places that can come earlier on PATH than `/usr/local/bin`. When it finds a copy there, it says a terminal may run that copy, instead of reporting success. Asked the same day which places to check, the founder chose `~/.local/bin/keypaste` and Homebrew's `/opt/homebrew/bin/keypaste`, 3.7a's place. A copy that resolves to this bundle's `keypaste`, as a link to the one Settings made does, is skipped. Asked what the note says, the founder chose wording that tells the person what to do: "A terminal may run <copy> instead, if its folder comes before <dir> on PATH. Move or delete it so a terminal runs this app's keypaste."
- **Provisional choices** (2026-10-11). The founder confirmed G.5's provisional technical choices as taken, including decision rows that link this record before it lands.
- **No compatibility with earlier keypaste** (D-0416, confirmed 2026-10-10). The upgrade fixture is written for the 0.5.0 CLI, and nothing migrates a link or PATH entry from an earlier package.

## What changed

- The MSI has a `UserPath` component of its own. Its `Environment` row appends `[INSTALLFOLDER]` to the per-user PATH and is removed on uninstall. Its key path is an HKCU value that is the same in every version, so a major upgrade keeps one entry and an uninstall removes only that entry (D-0431).
- The AppImage's `AppRun` passes `cli`'s remaining arguments to the `keypaste` it carries, beside `mcp`.
- `Core/Linking/` reads, links, replaces and keeps current the `keypaste` a terminal finds. Windows has none, because the MSI's entry covers it.
  - On Linux, `~/.local/bin/keypaste` is a script that starts this AppImage with `cli`, or starts the `keypaste` beside the app. The script single-quotes the path, refuses one holding a line break, and names the version that wrote it.
  - The script is rewritten only when it is exactly the one keypaste wrote and it names an older version, a program that is gone, or this copy at another version.
  - On macOS, `/usr/local/bin/keypaste` is a symlink to the bundle's `Contents/MacOS/keypaste`. The fixed program runs as root and checks again what Settings read before it changes anything.
- Every start of the app keeps the link current in `Program.Run`, before the app takes its claim. A start handed to a running copy therefore re-points it too (`b5a4714`). Before, the re-point ran in the app's own launch, which only the process holding the claim reaches.
- Settings shows what is at the link path.
  - It offers Link, or Replace behind a confirmation, and reports the result.
  - It says when the link's directory is not on the session's PATH, and on macOS that the system asks for an administrator's password.
  - It also names a copy in `~/.local/bin` or `/opt/homebrew/bin` that does not resolve to this bundle, rather than saying a terminal runs this app's `keypaste` (`5a629a7`, `c3a8e39`, `21a7bd9`).
- `verify-windows-installer.sh` requires the MSI's one `Environment` row. `verify-linux-appimage.sh` runs `AppRun cli`. The new `verify-macos-cli-link.sh` runs in `app.yml` on `osx-arm64`.
- `scripts/lib/terminal.sh` starts a shell as a new terminal would: on Windows with HKLM's then HKCU's PATH, and on Linux as a login bash from an empty environment. `exercise-desktop-install.sh` and `exercise-desktop-upgrade.sh` use it after the install and after each upgrade step. Both workflows take a `rehearsal` input (D-0433).
- The version prefix is 0.5.0, and CHANGELOG has an unadvertised `0.5.0-rc.1` section. ARCHITECTURE, RELEASE and THREATS (T-39) describe the link.

## Evidence

- Test classes:
  - `LinuxCliLinkTests`: 35 cases, run on Linux and macOS. They skip on Windows, whose MSI puts the CLI on PATH.
  - `MacCliLinkTests`: 20 cases. The four real `osascript` cases run on macOS only: link, replace, a backup, and a foreign file that appeared after keypaste looked. Three more cases need POSIX files and skip on Windows.
  - `SemanticVersionTests`: 32 cases.
  - These three classes passed in ci 38186459655 on all three runners.
  - `CliLinkSettingsTests` (19 cases) and `TerminalLinkJourneyTests` (4) passed in app 38186466427.
- A defect found on the branch. ci 38063630628 at `291f0dd` failed `MacCliLinkTests.The_real_program_leaves_a_file_that_appeared_after_keypaste_looked` on `macos-15` with `IOException: Broken pipe`. The process runner wrote a UTF-8 preamble to the input of an `osascript` that had already closed it, which would fail a person's link the same way. The regression ran on a commit carrying it over the old code and failed there:

| Test | Control (old code) | Fix |
|---|---|---|
| `MacCliLinkTests.The_dialog_writes_nothing_to_osascripts_input` | dev 38064416017 at `5d34bce`: failed on `ubuntu-24.04`, the input not empty | dev 38064519017 at `cc70e5d`: passed on all three runners |

- A defect found in review. The re-point ran in the app's launch, which a second start never reaches: it hands off to the running app (D-0397) and exits. So a newer AppImage started while an older copy ran (after a start at login, for instance) left the link naming the older image. A terminal ran the older keypaste until the newer one started on its own.
  - The regressions ran on a control branch carrying them over the old start (`60edf6d`, over `b900b6c`) and failed there. 3 of the class's 4 cases failed, keeping the link current 0 times where once was expected. The fourth case, Settings linking, passed.
  - `exercise-desktop-upgrade.sh` now starts the newer image while the older app runs and requires a new terminal to run the newer version. Upgrade rehearsal 38145706906 at `b5a4714` was its first such run.

| Test | Control (old start) | Fix |
|---|---|---|
| `TerminalLinkJourneyTests.A_start_the_running_app_takes_still_keeps_the_link_current` (`atLogin` false and true) | dev 38145433895 at `60edf6d`: failed on `ubuntu-24.04` | dev 38145530761 at `b5a4714`: passed on all three runners; app 38186466427 |
| `TerminalLinkJourneyTests.A_start_keeps_the_link_current_once_before_the_app_runs` | dev 38145433895 at `60edf6d`: failed on `ubuntu-24.04` | dev 38145530761 at `b5a4714`: passed on all three runners; app 38186466427 |

- A defect found in review. On macOS, Settings said a terminal runs this app's `keypaste` from `/usr/local/bin/keypaste` without looking for one a terminal may find first (Amendments).
  - The regressions ran on a control branch carrying them over the old behaviour (`55242df`), where the link read no earlier copy and Settings ignored one, and failed there.
  - The first fix run then failed the Core regression on Linux and macOS: `File.Exists` counts a dangling link as a file. The lookup now resolves each place to what it finally names before checking it (`c3a8e39`).

| Test | Control (old behaviour) | Fix |
|---|---|---|
| `MacCliLinkTests.Reading_names_a_keypaste_a_terminal_may_find_first_unless_it_starts_this_copy` | dev 38167425939 at `55242df`: failed on `ubuntu-24.04`, no earlier copy read | dev 38168178002 at `5a629a7`: failed on Linux and macOS, a dangling link named; dev 38168545035 at `c3a8e39`: passed on all three runners; ci 38186459655 |
| `CliLinkSettingsTests.A_keypaste_a_terminal_may_find_first_is_named_and_the_link_is_never_reported_as_what_a_terminal_runs` | dev 38167525548 at `55242df`: failed on `ubuntu-24.04`, no note | dev 38167391443 at `5a629a7`: passed on all three runners; app 38186466427 with the note's second sentence (`21a7bd9`) |

- Per-commit checks in app 38186466427:
  - `verify-windows-installer.sh` required the MSI's one `Environment` row, `=-PATH` with `[~];[INSTALLFOLDER]` in the `UserPath` component. It extracted 254 payload files byte-identical.
  - `verify-linux-appimage.sh` had the image's `AppRun cli --version` report 0.5.0 and `AppRun cli mcp --help` answer as the bridge, over 253 payload files.
  - `verify-macos-cli-link.sh` linked the unzipped bundle's `keypaste` at `/usr/local/bin/keypaste`. A new `zsh -l` found it there, it reported 0.5.0, and its bridge answered.
- Selftests in ci 38186459655: `exercise-desktop-install.sh` 73 cases and `exercise-desktop-upgrade.sh` 85 cases. These include the rehearsal cases and the two Linux hand-off cases: one where the start did not re-point the link, one where it was never probed.
- install-desktop rehearsal 38187074803 at `724f854`, over app run 38186466427. This is a rehearsal, not release evidence. Both legs reported `candidate=rehearsal`, and every other check passed.
  - Windows `terminal`: a new shell ran keypaste 0.5.0 from `%LOCALAPPDATA%\Programs\keypaste\keypaste.exe`, its bridge answered, and the folder was on the user PATH once.
  - Linux `terminal`: a new login shell ran 0.5.0 from `~/.local/bin/keypaste`, which Settings linked. It read `~/.profile` with the image's nvm-only `~/.bash_profile` set aside.
- upgrade-desktop rehearsal 38187076840 at `724f854`, with a fixture CLI/MCP 0.5.0 built from that commit. This is a rehearsal, not release evidence.
  - On Windows a user PATH entry of the person's own was added first. A new shell ran 0.0.1-upgrade after install. It ran 0.0.2-upgrade after the upgrade, the interrupted install and the refused downgrade, each time from the install folder, which the user PATH named once. After uninstall a new shell found no `keypaste`, and the user PATH was as before the install.
  - On Linux a new shell ran 0.0.1-upgrade through the link Settings made. It ran 0.0.2-upgrade after the higher image was started while the lower app ran, after the higher one started on its own, after the lower one started again, and after the interrupted download. After uninstall the left script exited 127, naming the deleted image.
- install-desktop run 38191303484 on `v0.5.0-rc.1`, over the tag's app run 38189762439, dispatched from main at `c10fbc8`, whose tree differs from `1b71162` only in `release-targets.json`. Both legs observed no rehearsal, found each package built by the tag's `app.yml` (`candidate`), and passed every check.
  - Windows `terminal` passed: a new shell ran keypaste 0.5.0-rc.1 from `%LOCALAPPDATA%\Programs\keypaste\keypaste.exe`, its bridge answered, and the install folder was on the user PATH once.
  - Linux `terminal` passed: Settings linked keypaste and said "A terminal runs this app's keypaste from /home/runner/.local/bin/keypaste." A new login shell then ran 0.5.0-rc.1 from `~/.local/bin/keypaste`, and its bridge answered. It read `~/.profile`, with the image's nvm-only `~/.bash_profile` set aside.
- upgrade-desktop run 38191305339 with CLI/MCP `0.5.0-rc.1`, also dispatched from `c10fbc8`. Both legs downloaded it from `https://dl.keypaste.com/v0.5.0-rc.1/` without credentials and found its manifest and all 11 assets built by the release workflow for `v0.5.0-rc.1`. The run's candidates jobs built the 0.0.1-upgrade, 0.0.2-upgrade and 0.0.3-upgrade packages at `c10fbc8`.
  - On Windows a user PATH entry of the person's own was added first. A new shell ran 0.0.1-upgrade after install. It ran 0.0.2-upgrade after the upgrade, the interrupted install (exit 1603) and the refused downgrade (exit 1603), each time from the install folder, which the user PATH named once. After uninstall a new shell found no `keypaste`, the user PATH named the folder 0 times, and it read as before the install, the person's entry included.
  - On Linux Settings linked keypaste, and a new login shell ran 0.0.1-upgrade through the link, reading `~/.profile`. It ran 0.0.2-upgrade after the higher image was started while the lower app ran, after the higher one started on its own, after the lower one started again, and after the interrupted download. An AppImage has no installer to refuse a downgrade, so that check was not applicable. After uninstall the left script exited 127, naming the deleted 0.0.2-upgrade image.

| Run | Commit | What | Result |
|---|---|---|---|
| dev 38063045520 | `291f0dd` | `LinuxCliLinkTests`, all runners | Failed on `windows-2025` only, because every case there skips and the class run reported zero tests; passed on Linux and macOS |
| dev 38063265178 | `291f0dd` | `MacCliLinkTests`, all runners | Passed |
| dev 38063457448 | `291f0dd` | `SemanticVersionTests`, all runners | Passed |
| ci 38063630628, app 38063637914 | `291f0dd` | Full | ci failed with the broken pipe above on `macos-15`; app passed |
| dev 38064416017 | `5d34bce` | `MacCliLinkTests`, the regression over the old code | Failed, as expected |
| dev 38064519017 | `cc70e5d` | `MacCliLinkTests`, all runners | Passed |
| ci 38064696823, app 38064703561 | `cc70e5d` | Full | Passed |
| ci 38074336940, app 38074343773 | `685fdca` | Full: the MSI's PATH entry, AppRun `cli`, the macOS link check | Passed |
| ci 38075877531, app 38075884818 | `b24e186` | Full | Passed |
| dev 38086934433, dev 38087614819 | `6893446` | `TerminalLinkJourneyTests`, `CliLinkSettingsTests`, all runners | Passed (dev 38086909946 cancelled by the next dispatch) |
| ci 38088866622, app 38088874411 | `13f71b6` | Full | Passed |
| install-desktop 38090054591 | `13f71b6` | Rehearsal over app 38088874411 | Linux `terminal` contradiction, caused by the runner's nvm-only `~/.bash_profile` (Amendments); Windows passed |
| ci 38140040462, app 38140046835 | `65310a0` | Full | Passed |
| upgrade-desktop 38140052272 | `65310a0` | Rehearsal | Passed on Windows and Linux |
| install-desktop 38140868121 | `65310a0` | Rehearsal over app 38140046835 | Passed on Windows and Linux, `candidate=rehearsal` |
| ci 38141407827, app 38141414501 | `a776902` | Full | Passed every job on all three runners |
| upgrade-desktop 38141415773 | `a776902` | Rehearsal | Passed on Windows and Linux |
| install-desktop 38142166121 | `a776902` | Rehearsal over app 38141414501 | Passed on Windows and Linux, `candidate=rehearsal` |
| ci 38142672871, app 38142679846 | `b900b6c` | Full, with F.61 merged | Passed |
| install-desktop 38143380722 | `b900b6c` | Rehearsal over app 38142679846 | Passed on Windows and Linux |
| upgrade-desktop 38143382507 | `b900b6c` | Rehearsal | Passed on Windows and Linux |
| dev 38145433895 | `60edf6d` | `TerminalLinkJourneyTests`, the hand-off regressions over the old start | Failed on `ubuntu-24.04`, as expected: 3 of 4 cases |
| dev 38145530761 | `b5a4714` | `TerminalLinkJourneyTests`, all runners | Passed |
| upgrade-desktop 38145706906 | `b5a4714` | Rehearsal, the higher image started while the lower app runs | Passed on Windows and Linux |
| ci 38145725707, app 38145732582 | `b5a4714` | Full | Passed |
| dev 38167361517 | `5a629a7` | `MacCliLinkTests`, all runners | Cancelled by the next dispatch on the branch |
| dev 38167391443 | `5a629a7` | `CliLinkSettingsTests`, all runners | Passed |
| dev 38167425939, dev 38167525548 | `55242df` | The earlier-copy regressions over the old behaviour | Failed, as expected |
| dev 38168178002 | `5a629a7` | `MacCliLinkTests`, all runners | Failed on Linux and macOS: a dangling link named as an earlier copy |
| dev 38168545035 | `c3a8e39` | `MacCliLinkTests`, all runners | Passed |
| ci 38186459655, app 38186466427 | `724f854` | Full, final, with main at `e8b245d` merged | Passed every job on all three runners |
| install-desktop 38187074803 | `724f854` | Rehearsal over app 38186466427, final | Passed on `linux-x64` and `win-x64`, `candidate=rehearsal` |
| upgrade-desktop 38187076840 | `724f854` | Rehearsal, final: candidates and upgrade | Passed on `linux-x64` and `win-x64` |
| ci 38188337280, app 38188339052 | `1b71162` | Full, at the commit tagged `v0.5.0-rc.1` | Passed |
| app 38189762439 | `v0.5.0-rc.1` | The tag's packages, attested | Passed |
| release 38189762435 | `v0.5.0-rc.1` | Publishes the CLI/MCP archives to `https://dl.keypaste.com/v0.5.0-rc.1/` | Attempt 1 failed in guard, before the tag's app run had finished, and built and published nothing; attempt 2 passed guard, the four builds and publish |
| install-desktop 38191303484 | `v0.5.0-rc.1` | The candidate over the tag's app run, from main at `c10fbc8` | Passed on `linux-x64` and `win-x64` |
| upgrade-desktop 38191305339 | `v0.5.0-rc.1` | The candidate's CLI/MCP, from main at `c10fbc8` | Passed candidates and upgrade on `linux-x64` and `win-x64` |

## Decisions

D-0431, D-0432, D-0433; T-39.

## Limits and follow-ups

- No run typed an administrator's password. The dialog's path is covered by three things: the composed `osascript` arguments, the same fixed program run without elevation, and the sudo link on the built bundle. A person's dialog is R.1a's manual check.
- macOS has no install or upgrade observation; 4.7e owns it.
- The rehearsals verified no attestation and are not release evidence; only the runs against `v0.5.0-rc.1` are.
- Release run 38189762435's first attempt failed in its guard with "no successful app run for 1b71162e… at refs/tags/v0.5.0-rc.1", having found ci and app green on the commit. `release.yml` starts with the tag push, while the tag's own app run, whose packages a release takes, was still running. Nothing was built or published. After that app run succeeded, a re-run passed. The guard now waits up to 40 minutes for the tag's app run to finish before it asks whether the gates are green, through `require-green-gates.sh --wait`, so a tag whose app run finishes within that time needs no re-run; only `verify-green-gates.sh`'s fake `gh` has driven the wait, and no tag has run it yet (gaps-1c, RUNS-PENDING).
- Uninstalling leaves the Linux script and the macOS link, as removing the app leaves its login entry (G.4a); each then starts nothing. A backup that Replace kept stays where it is. These stay by founder decision 2026-10-22, and [desktop](../desktop.md#removing-keypaste) gives the commands that remove them.
- A Linux desktop session that began before `~/.local/bin` existed finds the link only after the next login; Settings says so.
- The new-shell probe on Linux models a stock Ubuntu login, not the runner image's own account (Amendments).
- The macOS link is on every account's PATH and points into a bundle the installing account can write (T-39, accepted by the founder).
- A `keypaste` earlier on the terminal's PATH still wins. On Windows it wins over the MSI's entry, which also puts the folder's other files on PATH. On Linux and macOS it wins over the link. On macOS Settings names a copy in `~/.local/bin` or `/opt/homebrew/bin`. A copy that a shell profile puts first anywhere else goes unseen, because Settings starts no shell to ask.
- The Windows desktop ZIP gets no PATH entry, kept by founder decision 2026-10-22; a Linux desktop archive can be linked from Settings.
