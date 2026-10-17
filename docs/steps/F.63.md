# F.63 — Skip the atomic-replace commit on Windows and watch the projects gate

Completed 2026-10-16 at `2bf0494` on `task/f63-watchdog`, after the review of its second integration `63983b5`: the dispatched ci 38990470315 and app 38990479089 runs passed every job on all three runners; ci's `tools` jobs were not asked for, since F.63 changes nothing `keypaste setup` registers. The integrated commit differs from that tree only in this record.

It had no STEPS row: two Windows CI failures recorded as open observations in E.1d's and N.3's records. The founder asked on 2026-10-15 for both to be fixed.

## Amendments

- Scope as selected (founder, 2026-10-15): fix the two recorded Windows CI observations and give them a record.
- After the review of `e29f09b` (founder, 2026-10-15): the test skips on Windows rather than committing through the save gate, and one probe run establishes the cause; the hang gets a watchdog inside the gate and a BACKLOG row in place of the step limit; this record is rewritten in place.
- After the review of `63983b5` (founder, 2026-10-16): the Windows skip stays; F.57's row stays closed, the diagnosis naming why no other holder was possible; the watchdog is reworked to run detached, end the gate before anything else and end only processes the gate started, sharing a tree-ending helper with `verify-keepassxc-fields.sh`; and the step's limit returns at 15 minutes as a backstop. This record is rewritten in place again.

## Diagnosis

- `AtomicReplaceTests.A_commit_replaces_the_vault_with_what_was_written` failed twice on `windows-2025` with `IOException: The function attempted to use a name that is reserved for use by another transaction`: once in [F.57](F.57.md)'s runs, and in ci 38560217331 (attempt 1, job 115839155986), whose log puts the `KeePass_TxF_*.tmp` it was creating in the process's private temporary directory. On Windows the test never reached the atomic replace its class is about: KeePassLib commits through Transactional NTFS there, and while a commit's transacted move is in flight its temporary's `KEEPAS~1.TMP` 8.3 alias is reserved. The test committed outside the save gate. Only a transaction reserves a name, the only transactions in that private directory are keypaste's own saves, and the tests that hold a transaction themselves, through `HeldTransactedName` and V-F.6's contender, hold it in their own directories. The holder can therefore only have been a save in another test class, which is D-0122's collision inside one process.
- A probe on `windows-2025` (dev 38832734983 at `57c1eb5`, build 10.0.26100, in the process's private temporary directory) established it. With a `KeePass_TxF_` move held in an uncommitted transaction, 20 of 20 `FileTransactionEx` commits were refused with that error (6800). With nothing held, 0 of 1000 back-to-back commits were, so none of those reservations outlasted its transaction. The probe was deleted.
- Product saves take the gate before every attempt over an existing vault. The residual `EnterGateIfTransacting` documents, a vault file created between its check and KeePassLib's, transacts ungated, and a refusal there is an `IOException` the save retries.
- `scripts/verify-keepassxc-projects.sh` stopped for 28 minutes on `windows-2025` (ci 38594808270, attempt 1, job 115958668567). The job's only annotation is its 30-minute limit, yet the server closed it at 35 minutes with the step still in progress, its Post steps pending and no log kept: the runner never finished cancelling it. A step's own `timeout-minutes` is enforced by that same runner, so it could not have kept the log either. The cause is not known.
- The watchdog integrated at `63983b5` could pass a hung gate on Windows. It ended the gate with `taskkill`, and a Git Bash process ended that way gives its parent exit status 0: on `windows-2025` in ci 38989472375, a child bash ended by `taskkill` returned 0 whether stopped first or not, and one ended by Git Bash's `kill -9` returned 137 both ways. In ci 38988628610, with the deadline at 30 seconds, the projects step passed after the watchdog fired. The exit code 1 of the earlier run at 60 seconds (ci 38832968859) came from the gate itself: the by-name kill ended the `keypaste` it was waiting on, the gate printed `PROJECTS GATE FAILED: could not change the second copy's agents`, and the tree kill then found no gate.
- On Windows, Git Bash keeps a bash process as the parent of each native program it starts, so a program the gate loses track of still has a living parent; what has lost its parent is that bash. In ci 38989472375 the orphaned `keypaste agent` 8012 hung from bash 6112, whose parent had exited.

## What changed

- `A_commit_replaces_the_vault_with_what_was_written` skips on Windows with its sibling's reason and is otherwise as it was before F.63.
- `scripts/lib/common.sh` has `watchdog`, which `verify-keepassxc-projects.sh` arms at 600 seconds; the step takes about two minutes on `windows-2025` and one elsewhere. Its watcher is detached, so ending the gate's tree cannot end it. Past the deadline it prints the phase the last `step` named and every process with its parent and command line, then ends the gate before anything else, so the step fails with its log kept. On Linux and macOS it stops the gate before listing and ends it with its tree. On Windows the PowerShell listing is ended if it has not finished in a minute, the gate is ended by Git Bash's `kill -9` and each of its children's trees by `taskkill`. Last it ends each chain holding one of the gate's programs that the gate started and lost track of: the chain's top has lost its parent, which on Linux and macOS means it hangs from init, and started after the gate, which on Linux and macOS means it was not running when the watchdog was armed. A program that predates the gate, or whose chain reaches a living process outside it, is left alone.
- `end_tree` in `common.sh` ends a process tree for the watchdog and for `verify-keepassxc-fields.sh`'s worker.
- `ci.yml` gives that step a 15-minute limit, a backstop should the watchdog itself fail.

## Evidence

- The probe: dev 38832734983 at `57c1eb5`, above.
- The watchdog with its deadline at 30 seconds, beside a `keypaste agent` the gate orphaned and a bystander agent an earlier step started: in ci 38989472375 at `1635e46`, the step on every runner printed the phase and every process, failed with exit code 137, named and ended the orphan's chain and nothing else, and left the bystander running; none of the gate's own agents outlived it. ci 38988628610 at `5fece7a` found the Windows exit status above; its orphan and bystander had exited at once, through a mistake in the probe.
- ci 38834636867 and app 38834646300 (attempt 2) at `e68c829`, green on all three runners. `AtomicReplaceTests` passed both tests on Linux and macOS and skipped both on Windows.
- ci 38990470315 and app 38990479089 at `2bf0494`, green on all three runners.

## Limits and follow-ups

- The project-tags hang stays open as a BACKLOG investigation until the watchdog reports one.
- F.57's BACKLOG row on this test is removed, since the probe answers its question.
- An orphan on Linux hangs from init only where no subreaper adopts it, as on `ubuntu-24.04` (ci 38989472375); under one, such as `systemd --user` on a desktop, the watchdog leaves it running.
- On Windows, a PowerShell listing not finished in a minute is ended and `ps -W` printed instead; the gate still ends and its step fails, but its children are not known, so they outlive it until the runner's job cleanup.
- App 38834646300's first attempt failed `desktop gates (windows-2025)`: `verify-run-session.sh`, which F.63 does not touch, printed its last `ok` and exited 1 with no message. The founder chose on 2026-10-15 to re-run that job and keep the failure open as a BACKLOG investigation; the passing re-run does not close it.
