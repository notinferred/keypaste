# F.99 — Restore the merge gate's attachment, token and refusal cases

Prepared 2026-11-09 on `backlog-2026-11`, source only. The gate has not run with these cases yet: none of G7, G8 and G11 has passed against KeePassXC, and G11 has never run at all. Run IDs go here once ci has run the commit.

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

No KeePassXC exists on this machine, so only `bash -n` and the gate's XML reader and jq filters, against a hand-made export, were exercised here. Everything else is what ci will show, in `compat (ubuntu-24.04)` on KeePassXC 2.7.6 and `compat (macos-15)` and `compat (windows-2025)` on 2.8.0, each in the step "Verify the app merges KeePassXC's saves through real KeePassXC".

## Limits and follow-ups

- The ci dispatch is the first run of G11 and the first of G7 and G8 on the corrected method. The new cases add perhaps 40 seconds on Windows to a step whose slowest green run took 46 seconds under a two-minute limit; the limit is left alone until that time is measured.
- The same-attachment conflict G7 used to cover needs the app to change an attachment through the merge, which the driver does not do.
- THREATS' sentence saying real KeePassXC's merge is not shown changes once G8 passes.
