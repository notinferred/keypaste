# F.102 — Tell the person when the audit log stops being written

Completed 2026-11-10 on `backlog-23-25`, source only; no CI run has happened yet, so no run IDs are recorded.

## Amendments

The founder selected this from BACKLOG's investigation candidates on 2026-11-09: a log that opened and later cannot be written gets the same one pop-up and Agents message as a log that won't open, with this case's reason, cleared when a write succeeds. There is no STEPS row.

## Judgment calls

- **The news resets on a written line, not on an open.** F.86 reset it when a log opened; a log whose write fails opens fine, so a lock and unlock with the disk still full would otherwise tell it again. A log mended after failing to open is told again only once a line is written, which the next request does.
- **The failure is known from each append.** `AuditLog` raises what each append ended with and the app's opener keeps the last failure. A log that won't open comes first.
- **Failing closed is unchanged.** `RequestRecorder` still refuses a release whose line cannot be written.

## Evidence

Run against the code before the fix, the three new tests failed: `A_log_that_stops_being_written_pops_up_once_and_stays_on_Agents_until_a_write_succeeds`, `A_log_that_fails_to_write_again_after_a_write_succeeded_pops_up_again` and `A_log_that_still_cannot_be_written_after_a_relock_does_not_pop_up_again`. Each holds the sidecar lock another keypaste would.

With the fix, `SessionOwnershipTests`, `AgentActivityViewTests`, `AgentActivityViewModelTests` and `LanguageAndRegionTests` pass 58 of 58 and `AuditLogTests` 34 of 34 locally on macOS.

## Limits and follow-ups

A single record over the 64 KiB cap, refused for its size alone, also raises the message until the next line is written, though the log itself is fine. A pop-up shown while the window is minimized can time out unseen, and the message on Agents remains. A full disk was not reproduced; the held write lock stands in for it through the same path. The app was not started by hand.
