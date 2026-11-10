# F.86 — Tell the person when the audit log stops agents

Completed 2026-11-09 on `backlog-2026-11`, source only; ci 42822341787 and app 42822347029 at the final code `4fa4c1e9`, both green on Windows, macOS and Linux.

## Amendments

The founder selected this from BACKLOG's investigation candidates on 2026-11-09: one pop-up when the failure starts, naming the problem and the fix, and a message that stays on Agents until it is fixed. The same row's question about the Automatic names was not decided and stays in BACKLOG. There is no STEPS row.

## Judgment calls

- **The pop-up is a warning toast**, the app's existing pop-up. It uses fixed words, and the message on Agents gives the path and reason.
- **Once per failure spans a lock.** Each unlock builds a new shell and a new log opener, so what has been told is kept in `SessionHost`, which lasts the whole run of the app, and only a log that opens resets it. An open log stays open until the lock, so a failure can return only at a later unlock.

## Evidence

Run against the code before the fix, the three new tests failed: `A_log_that_will_not_open_pops_up_once_however_often_agents_retry_or_the_vault_relocks`, `A_log_that_opened_and_fails_again_pops_up_again` and `A_log_that_will_not_open_stays_on_Agents_until_it_opens`.

With the fix, `SessionOwnershipTests`, `AgentActivityViewTests`, `ShellStatusTests`, `AgentActivityViewModelTests` and `LanguageAndRegionTests` pass 85 of 85 locally on macOS, and `verify.sh compile` passes.

An intermittent hang in F.85's `AMergeAFormHeldOff_IsShownOnceTheFormIsCancelled(form: "edit")` stopped some local runs of those classes, including at `905bb88b` before this fix; it is in BACKLOG, undiagnosed.

## Limits and follow-ups

A log that opened and later cannot be written still refuses each release without telling the person; that is in BACKLOG. A toast shown while the window is minimized can time out unseen, and the message on Agents remains. The app was not started by hand.
