# F.63 — Commit the atomic-replace test through the save gate

Completed 2026-10-15 at `f7221b2` on `task/f63`: the dispatched ci 38825403716 and app 38825413938 runs passed every job on all three runners. The integrated commit differs from that tree only in this record and in main's README screenshots and demo GIF, which landed in between.

It had no STEPS row: two Windows CI failures recorded as open observations in E.1d's and N.3's records. The founder asked on 2026-10-15 for both to be fixed.

## Amendments

- Scope as selected (founder, 2026-10-15): fix the two recorded Windows CI observations and give them a record.

## Diagnosis

- `AtomicReplaceTests.A_commit_replaces_the_vault_with_what_was_written` failed once on `windows-2025` (ci 38560217331, attempt 1, job 115839155986) with `IOException: The function attempted to use a name that is reserved for use by another transaction`, opening its `KeePass_TxF_*.tmp` in the process's private temporary directory. The test commits a `FileTransactionEx` directly, outside the save gate `KeePassInterop` holds around every transacted save in a process. A save in a test class running at the same moment could therefore hold the `KEEPAS~1.TMP` 8.3 alias the test's temporary needed. That is the collision D-0122 measured between processes, and which the gate rules out inside one. No product save commits outside the gate.
- `scripts/verify-keepassxc-projects.sh` stopped for 28 minutes on `windows-2025` (ci 38594808270, attempt 1, job 115958668567) until the job's limit cancelled it. That step takes about two minutes in every other run, and the cancelled job kept no log. Its cause is not known.

## What changed

- The test commits through `KeePassInterop.WhileNoSaveReplaces`, as every transacted save in the process does.
- `ci.yml` gives the compat job's "Verify project tags and their protection through real KeePassXC" step a ten-minute limit. A hang then fails at the step and keeps the step's log, which names the phase that stopped. The hang stays an open observation until a run shows it.

## Evidence

- ci 38825403716 and app 38825413938 at `f7221b2`, green on all three runners; the project-tags step took two minutes on `windows-2025`.
- No control run: the conflict needs another test's transaction at the same instant and cannot be forced, and its mechanism is D-0122's.
