# F.63 — Skip the atomic-replace commit on Windows and watch the projects gate

Completed 2026-10-15 at `e68c829` on `task/f63-followup`, after the review of its first integration `e29f09b`: the dispatched ci 38834636867 and app 38834646300 (attempt 2) runs passed every job on all three runners; ci's `tools` jobs were not asked for, since F.63 changes nothing `keypaste setup` registers. The integrated commit differs from that tree only in this record and in BACKLOG's row for the run-session failure below.

It had no STEPS row: two Windows CI failures recorded as open observations in E.1d's and N.3's records. The founder asked on 2026-10-15 for both to be fixed.

## Amendments

- Scope as selected (founder, 2026-10-15): fix the two recorded Windows CI observations and give them a record.
- After the review of `e29f09b` (founder, 2026-10-15): the test skips on Windows rather than committing through the save gate, and one probe run establishes the cause; the hang gets a watchdog inside the gate and a BACKLOG row in place of the step limit; this record is rewritten in place.

## Diagnosis

- `AtomicReplaceTests.A_commit_replaces_the_vault_with_what_was_written` failed twice on `windows-2025` with `IOException: The function attempted to use a name that is reserved for use by another transaction`: once in [F.57](F.57.md)'s runs, and in ci 38560217331 (attempt 1, job 115839155986), whose log puts the `KeePass_TxF_*.tmp` it was creating in the process's private temporary directory. On Windows the test never reached the atomic replace its class is about: KeePassLib commits through Transactional NTFS there, and while a commit's transacted move is in flight its temporary's `KEEPAS~1.TMP` 8.3 alias is reserved. The test committed outside the save gate. Only a transaction reserves a name, the only transactions in that private directory are keypaste's own saves, and the tests that hold a transaction themselves, through `HeldTransactedName` and V-F.6's contender, hold it in their own directories. The holder can therefore only have been a save in another test class, which is D-0122's collision inside one process.
- A probe on `windows-2025` (dev 38832734983 at `57c1eb5`, build 10.0.26100, in the process's private temporary directory) established it. With a `KeePass_TxF_` move held in an uncommitted transaction, 20 of 20 `FileTransactionEx` commits were refused with that error (6800). With nothing held, 0 of 1000 back-to-back commits were, so none of those reservations outlasted its transaction. The probe was deleted.
- Product saves take the gate before every attempt over an existing vault. The residual `EnterGateIfTransacting` documents, a vault file created between its check and KeePassLib's, transacts ungated, and a refusal there is an `IOException` the save retries.
- `scripts/verify-keepassxc-projects.sh` stopped for 28 minutes on `windows-2025` (ci 38594808270, attempt 1, job 115958668567). The job's only annotation is its 30-minute limit, yet the server closed it at 35 minutes with the step still in progress, its Post steps pending and no log kept: the runner never finished cancelling it. A step's own `timeout-minutes` is enforced by that same runner, so it could not have kept the log either. The cause is not known.

## What changed

- `A_commit_replaces_the_vault_with_what_was_written` skips on Windows with its sibling's reason and is otherwise as it was before F.63.
- `scripts/lib/common.sh` has `watchdog`. Past its deadline it prints the phase the last `step` named and every process with its parent and command line, ends the gate's processes, on Windows by name as well as by tree since an orphan is outside the tree, and ends the gate, so the step fails with its log kept. `verify-keepassxc-projects.sh` arms it at 600 seconds; the step takes about two minutes on `windows-2025` and one elsewhere. `ci.yml`'s ten-minute limit on that step is removed.

## Evidence

- The probe: dev 38832734983 at `57c1eb5`, above.
- The watchdog, with its deadline lowered on the branch: in ci 38832968859 at `8980336` (60 seconds) the `windows-2025` step printed the phase and every process with its command line and failed at 80 seconds with its log kept, while the Linux and macOS steps finished inside the minute; in ci 38833938916 at `e91ccfd` (20 seconds) the step did the same on all three runners, ended by `kill -9` with exit code 137 on Linux and macOS.
- ci 38834636867 and app 38834646300 (attempt 2) at `e68c829`, green on all three runners. `AtomicReplaceTests` passed both tests on Linux and macOS and skipped both on Windows.

## Limits and follow-ups

- The project-tags hang stays open as a BACKLOG investigation until the watchdog reports one.
- F.57's BACKLOG row on this test is removed, since the probe answers its question.
- App 38834646300's first attempt failed `desktop gates (windows-2025)`: `verify-run-session.sh`, which F.63 does not touch, printed its last `ok` and exited 1 with no message. The founder chose on 2026-10-15 to re-run that job and keep the failure open as a BACKLOG investigation; the passing re-run does not close it.
