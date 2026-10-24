# R.1a — Exercise the integrated desktop candidate

Completed 2026-10-23 on `main` at `54294e58`: ci 39965706943, app 39965706785, and install-desktop rehearsal 39967782112 on that app run's packages, whose macOS job passed on its rerun.

## Amendments

- **Automated.** On 2026-10-23 the founder directed that R.1a run on runners rather than by hand. D-0342 already accepts an act made by the observer's own automation on the real desktop from the named package. `install-desktop.yml` performs the acts after its install exercise, in the app that exercise installed, through each platform's accessibility driver: UI Automation on Windows, the Accessibility API on macOS and AT-SPI on Linux.
- **The client.** The founder chose on 2026-10-23 to close R.1a with the requests sent by the acts' own stdio client. Claude Code 2.1.296, at `install-real-tools.sh`'s pin, registers keypaste through the app's Connect, and the client starts exactly the command that registration names and introduces itself as `claude-code`. Claude Code sending a request itself would need a model call on every run. Real tools connecting remains the work of ci's `tools` jobs.
- **Act 8.** It follows D-0466: a request after Lock is recorded `denied` and `no-approver`, and the agent is told the vault is locked in the app. `vault-locked` is for a request already waiting when the lock comes (D-0313), and the row's wording predated D-0466.
- **The timed vault.** Timing needs an Argon2 vault tuned by KeePassXC, and `keepassxc-cli db-create` writes KDBX 3 with AES-KDF. On Windows and Linux, KeePassXC's own new-database window makes the vault. On macOS, KeePassXC's Database Security changes it to KDBX 4 with Argon2d, at the rounds KeePassXC's benchmark picks.

## What changed

- **[exercise-desktop-acts.sh](../../scripts/exercise-desktop-acts.sh).** It performs the eleven acts below. Each act passes only on its evidence. It is unreached when a step could not be done or an act it needs did not pass, and a contradiction when the evidence disagrees. The script also has `--classify`, `--summary` and `--selftest`; the self-test runs in ci's scripts job and under macOS's bash 3.2.
- **Drivers.** [desktop-drive.sh](../../scripts/lib/desktop-drive.sh) defines the verbs, and `drive-desktop-windows.ps1`, `drive-desktop-macos.js` and `drive-desktop-linux.py` implement each of them. When a locator finds nothing, the driver keeps the window's tree with the records, leaving out field values.
- **[install-desktop.yml](../../.github/workflows/install-desktop.yml).**
  - It installs KeePassXC, Claude Code (`install-real-tools.sh --claude-code-only`) and, on Linux, xterm for Run.
  - The job summary carries each act's verdict.
  - The `acts-<rid>` artifact holds the screenshots, facts, audit lines and steps.
  - Each job is bounded at 25 minutes.
- **`dev.sh --acts <app-run> [--os]`.** Through dev.yml's acts mode, it installs any successful app run's packages and performs the acts with the branch's scripts. It is never evidence.

## Evidence

Rehearsal 39967782112 installed app run 39965706785's packages, built at `54294e58`, and ran the harness from the same commit.

| | Windows | macOS | Linux |
|---|---|---|---|
| System | Windows Server 2025 Datacenter 10.0.26100, image win25-vs2026 20261021.278.1 | macOS 15.7.9 (24G830), image macos15 20260907.0337.1 | Ubuntu 24.04.5 LTS, image ubuntu24 20261012.334.1 |
| Package | MSI `0d7a7a53ed6a842e7f02148143947f2bf664a344df7b2719bd402a9f865a9ba1` | DMG `7edfed0965e3fd078b9d249e5d1032407989428c9dda448d82e36934f002a4da` | AppImage `6fc9bd26e32d6c81d69ff009e97699fb0deb5f7ddb143e4601db3757a74e4001` |
| Vault, as KeePassXC tuned it | Argon2d, 33 rounds, 64 MiB | Argon2d, 25 rounds, 64 MiB | Argon2d, 48 rounds, 64 MiB |
| Argon2 library | `argon2.dll` in the package | `libargon2.1.dylib` in the package | the system's `libargon2.so.1` |
| Unlock, from Return to the item list | 1256 ms | 1468 ms | 1782 ms |
| Save, from Add to the file changing | 109–1568 ms | 25–1075 ms | 12–1265 ms |

The unlock was timed inside the driver. Each save range runs from the last poll that found the file unchanged to the first that found it changed: the file was polled every 250 ms, and the upper bound also includes starting the driver.

Every act passed on all three systems:

| Act | Its evidence |
|---|---|
| first-run | KeePassXC's recent databases offered `acts.kdbx`; the app unlocked it and wrote it to `app.toml` |
| field | The CLI reads the value typed into the item's pane, by SHA-256 |
| notes-flag | Recommendations flagged `ACTS_NOTE_KEY` in the notes; after the move the field holds the note's value and the notes no longer hold the line |
| project-run | Run's terminal started `keypaste run --session`, recorded `granted`/`prompt`, and its child received the field's value |
| edit-rerun | The next Run's child received the value replaced in the app |
| connect | Agents' Connect registered `keypaste mcp --client-label claude-code --allow-run --expose **` in Claude Code with no vault path, and that command answered |
| approve-deny | Allow once released the value, recorded `granted`/`prompt`; Deny released nothing, recorded `denied`/`prompt` |
| restore | A revision, an item from Trash and a backup were each restored, as the CLI reads them |
| change-access | The CLI refuses the old master password and opens the vault with the new one, and the app unlocks with it |
| lock-refused | After Lock a request received nothing, recorded `denied`/`no-approver`; the agent was told the vault is locked in the app, and the locked notice opened |
| timing | Above |

**Before the evidence run.** None of these dev runs is evidence:

- Dev run 39960341213 passed all 33 acts with the merged scripts, on app run 39850506098's packages.
- Dev run 39960384067 ran the Linux jobs. Its scripts and workflows job found four shellcheck findings, cleared in the R.1a commit. Its desktop tests found the race below.
- Dev runs 39960664624 (`HistoryRevealAutomationTests`) and 39964932862 (the desktop profile) passed on Linux with the fixes.

## Decisions

No new row: D-0342 accepts the automation, and D-0466 settles act 8.

## Limits and follow-ups

- **KeePassXC 2.7.12 crashed on the runners.** It ended with status 139 at start or soon after opening a database in several macOS 15 and Windows runs, in Qt 5's `QWidgetPrivate::showChildren` as it showed its window. In 39967782112's first macOS attempt it crashed twice, the third session's combo box did not open, and timing was unreached; the rerun passed. [F.68](F.68.md) found it to be KeePassXC #13521 and moved the runners to 2.8.0, which never crashed in its probe, and an act now stops when KeePassXC ends by itself (D-0526).
- **Linux screen readers.** AT-SPI under Avalonia 12.1 keeps a list's rows as it first listed them, so a project added during a session does not reach a Linux screen reader in the sidebar. The acts reach it by keyboard. [F.68](F.68.md) traced it to Avalonia's AT-SPI bridge and left the fix to Avalonia: issue #22583, pull request #22585 (D-0527).
- **The Connect plan's label.** Avalonia 12.1 names a TextBlock by its text and ignores `AutomationProperties.Name="What Run it will do"`, so a screen reader reads the plan itself, which [F.68](F.68.md) keeps as intended and removes the unused name (D-0527).
- **Fixed on the way.**
  - A screen reader announced each History revision as `Keypaste.App.ViewModels.EntryRevisionRow`. It is now named by its time and title (`dfe3bb5f`; `HistoryRevealAutomationTests`).
  - With no UI thread to post to, a Recommendations check showed its findings on a pool thread while its owner went on, which `c1b0c931` introduced. Dev run 39960384067 showed it as `RestoreBackupTests.The_notice_can_be_dismissed` failing with "An item with the same key has already been added. Key: LostTags". Such a check now runs on the asking thread (`54294e58`; `RecommendationsTests.With_no_UI_thread_a_check_shows_its_findings_on_the_thread_that_asked`). The app always posts to its UI thread, so only callers without one were affected.
- **Outside the row.** Hosted runners have no physical YubiKey, GNOME, KDE or Wayland session, screen-reader speech, administrator dialog to accept, or sleep and wake. How the app looks is for a person to judge from the screenshots in `acts-rehearsal-<rid>`.
- **4.7c2.** It repeats every act from the public 0.5.0 bytes.
