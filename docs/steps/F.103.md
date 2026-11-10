# F.103 — Record each expired link keypaste forgets

Completed 2026-11-10 on `backlog-23-25`, source only; no CI run has happened yet, so no run IDs are recorded.

## Amendments

The founder selected this on 2026-11-09: `keypaste share revoke --expired` writes one audit line per link removed, matching a normal revoke, with the item UUID. There is no STEPS or BACKLOG row.

## Judgment calls

- **The rule moved into `ShareService.ForgetExpired`**, beside `RevokeAsync`, since audit is Core's. Its line is `share-revoked` with the same Entry, Item and Field; its reason says the link was forgotten after expiring, because no server was asked.
- **What a write failure does matches a revoke.** The forgetting stands and the command fails with `the audit log could not be written`. Every remaining line is still tried, so a failure for one link loses no other link's line.
- **One save, then the lines**, as `--expired` did before. A save failure writes no line and says the links were not forgotten.

## Evidence

Run against the code before the fix, `ShareRevoke_Expired_WritesOneLinePerLinkRemoved_NamingItsItem_AndNoSecret` and `ShareRevoke_Expired_WithAnAuditLogThatCannotBeWritten_ForgetsTheLinksAndSaysSo` failed. `ForgetExpired_WithALineThatCannotBeWritten_KeepsTheRemovals_WritesTheOthers_AndSaysSo` calls the new method, so it has no run on the old code. `ShareRevoke_Expired_IsLocalOnly` no longer asserts that no line is written.

With the fix, `ShareVerbTests` pass 41 of 41 and `ShareServiceTests` 33 of 33 locally on macOS.

## Limits and follow-ups

A link the vault forgot in an earlier version has no line, and none is written now.
