# F.99 — Restore the merge gate's attachment, token and refusal cases

Completed 2026-11-09 on `backlog-2026-11`, source only; ci 42729638860 at `43ab6807`, `compat` green on all three runners with G7, G8 and G11, and ci 42822341787 at the final code `4fa4c1e9`, green on the same three.

## Amendments

The founder selected this on 2026-11-09 (the backlog review's item 15): diagnose and restore G7, G8 and G11, kept as written in `47e8068a`, on real KeePassXC, without loosening what they prove. There is no STEPS row.

## Diagnosis

Each read from ci 41106436785's logs (jobs 124237393410, 124237393799 and 124237394172) and KeePassXC's source at its 2.7.6 and 2.8.0 tags. No defect in keypaste was found.

- **G7 was the gate's method.** `KdbxWriter::extractDatabase` at 2.8.0 hands the writer an empty attachment map for KDBX 4, so `m_binaryIdxMap[...]` numbers every attachment 0 in an export, which holds no bytes, where 2.7.6 still numbered them by bytes. The bytes-compare steps before it passed on `macos-15` and `windows-2025`. The gate now reads each history version by restoring it in a copy and having KeePassXC export its attachment, on every version; a `measured:` line prints the numbers. F.84 changes what is expected: KeePassXC's new attachment and the app's newer password are different fields, so the current version holds both, and each side's whole version is in history.
- **G8 was the gate's method.** After the revocation, the file's history already holds the copy's version, so `Merger::mergeHistory` changes nothing and `keepassxc-cli merge` saves nothing, printing "Database was not modified by merge operation". The file is unchanged and the app correctly finds nothing to merge. G8 asserted a merge happened, on 2.7.6 the only job that reached it. It now asserts the file is byte-identical when the app reports none. F.84 changes nothing here: keypaste's own records are not combined field by field.
- **G11 never ran**, each earlier case failing first. Its wording matches the driver and notice at this commit; a lock may follow the merge on its own thread, so the status is polled for 5 seconds.

## What each case now proves

- G7: an item's attachment survives another item's merge byte for byte; a merge joins KeePassXC's attachment to the app's newer change, and each joined version, restored and exported by KeePassXC, holds its own bytes.
- G8: after `keypaste token revoke`, the token is refused, and the record KeePassXC exports is the revoked version with the live one in history, through KeePassXC merging a copy holding the token into the file, KeePassXC merging the revocation into that copy, and that copy moved over the held file. A revocation's stamp is always a second after the version it revokes (`StampAfter`), so a tie cannot favour the copy.
- G8's last step measures THREATS' limit: a copy whose record is edited after the revocation holds a newer version, so KeePassXC's merge takes it, keypaste's merge takes KeePassXC's whole, and the token runs again until it expires. It asserts that, so a change to the limit also changes this case.
- G11: another vault is refused with its notice and not retried; a truncated file is refused and the complete one merged; a password change is refused and locks.

## Evidence

No KeePassXC exists on this machine, so only `bash -n` and the gate's XML reader and jq filters, against a hand-made export, were exercised here. ci 42729638860 ran the rest, in `compat (ubuntu-24.04)` on KeePassXC 2.7.6 and `compat (macos-15)` and `compat (windows-2025)` on 2.8.0, each in the step "Verify the app merges KeePassXC's saves through real KeePassXC", where G7, G8 and G11 ran and the gate printed `MERGE GATE PASSED` on all three. G8's last step printed that a record edited after the revocation carried the verifier back and the token ran.

## Limits and follow-ups

- With the new cases the merge step took 63 s on `windows-2025` in ci 42729638860 and 60 s in ci 42822341787, under its two-minute limit; its slowest green run before them took 46 s.
- The same-attachment conflict G7 used to cover needs the app to change an attachment through the merge, which the driver does not do.
