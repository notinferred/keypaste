# F.57 — Wait for each reply instead of sleeping in the process gates

Completed 2026-10-07 at `90339a8` on `task/f57`: the dispatched ci 37647206161 and app 37647221423 runs passed every job on all three runners. The integrated commit differs from that tree only in this record.

It had no STEPS row: it answers the BACKLOG observation, from [F.52](F.52.md), that the process gates spent most of their time in fixed waits, as part of the founder's request of 2026-10-07 to stop waiting on CI.

## What changed

- `await_replies <seconds> <file> <id>...` in `scripts/lib/common.sh` returns once a JSON-RPC stream holds a response to each id, polling every 0.2 seconds, and returns 1 at the old sleep's length or `WAIT_SECONDS`, so a reply that never comes fails the gate's own assertion as before.
- Every `sleep` that only held an agent's input open until its answer came now waits for that answer: `verify-approval-e2e.sh` (49 s), `verify-policy-e2e.sh` (60 s, and its shutdown no longer waits a fixed second once the agents have exited), `verify-demo.sh` (24 s), `verify-log-chain.sh` (15 s, which now empties its output before each server so a previous reply cannot count), `verify-mcp-stdio.sh` (11 s), `verify-session-lifecycle.sh` (40 s), `verify-session-authority.sh` (15 s), `verify-keepassxc-projects.sh` (40 s), `verify-keepassxc-fields.sh` (28 s) and `verify-mcp-run.sh` (1 s). Requests, answers, assertions, refusals and the order of events are unchanged.

## Evidence

| Job | Before, ubuntu / macos / windows (s) | After |
|---|---|---|
| integration | 254 / 301 / 363 | 132 / 145 / 200 |
| compat | 248 / 314 / 482 | 214 / 206 / 392 |
| desktop gates | 301 / 341 / 429 | 234 / 291 / 379 |
| aot publish | 255 | 156 |

Before is main `310eb8c` (ci 37633437034, app 37633436794); after is each half alone, `f57-intg` at `a4e0a57` (ci 37640023333, app 37640039854) and `f57-desk` at `cad310f` (ci 37641358456, app 37641423782), each green once jobs GitHub could not give a runner were re-run.

## Limits and follow-ups

- Sleeps that are not holding a pipe stay: the lifecycle gate's two seconds in which a second start at login must not show a window, KeePassXC's two seconds to settle before a graceful close, a one-second kill grace, and the fields gate's wait for the clock's next second.
- `AtomicReplaceTests.A_commit_replaces_the_vault_with_what_was_written` failed once on `windows-2025` with "name reserved for use by another transaction" and passed on the re-run; it is a BACKLOG observation.
