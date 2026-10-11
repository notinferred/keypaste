# F.61 — Read the client after the handshake wait

Completed 2026-10-11 at `4386616` on `task/f61`: the dispatched ci 38140060900 and app 38140067598 runs passed every job on all three runners. The integrated commit differs from that tree only in this record.

It had no STEPS row: G.2's CI found the defect in desktop gates on `macos-15` (app run 38077611239), where `verify-agent-activity.sh` expected the waiting request under its client's name, `ci-probe`, and the app showed it from "an unnamed client". The founder chose on 2026-10-10 to fix it as its own task.

## Amendments

- Scope as selected (founder, 2026-10-10): read the client's name after the wait in all three tools, show the regression failing on a control branch, give the fix its own record and CHANGELOG entry, and integrate it to main before G.5 and G.2, both of which merge it before their full CI.
- T-3 keeps the sentence naming `LateHandshakeTests` in its evidence (founder, 2026-10-11). The sentence says what the tests check: every tool's audit line, and the owner's request only for a credential request or run, since a listing is never put to the owner.

## What changed

- `request_credential`, `list_entry_names` and `run` each read the client's identity with `McpAudit.ClientOf` before `McpAudit.HandshakeCompleteAsync` waited for the handshake (F.5). A call that arrived before the `initialize` handler published `ClientInfo` waited, found the client named, and was still decided with what it had read before waiting: the owner was asked about, and `keypaste log` recorded, an unnamed client. Nothing else reads the identity before the wait: the identity a bridge sends when it attaches is read at attach time, which every tool reaches only after the wait.
- `McpAudit.CallerAsync` replaces both functions. It waits, then reads the handshake once, and whether the client is known and what it is called both come from that read. Each tool takes its client from it, so the request the owner is asked about, the decision and the audit line carry the identity read after the wait. A client that never introduces itself is still refused as `not-initialized` without anyone being asked.
- THREATS T-3 names the new tests as evidence.

## Evidence

- `LateHandshakeTests` calls each tool directly on a server whose client is unnamed the first time it is asked and named every time after, the ordering a real client cannot produce in process. Three tests check the identity in the owner's request (credential and run) and in the audit line (all three tools); a fourth checks that a client that never introduces itself is refused unasked.
- Regressions, run on a control branch carrying the new tests over the old code (`5145793` over `db47e8e`, since deleted) and failing there:

| Test | Control (old code) | Fix |
|---|---|---|
| `ACredentialRequest_ThatOvertookTheHandshake_IsPutToTheOwnerAndLoggedUnderTheNameItGave` | dev 38086887082: failed on all three runners, the owner's request naming `(null, null)` where `("test-client", "9.9.9")` was expected | dev 38086884037 and ci 38140060900: passed on all three |
| `AListing_ThatOvertookTheHandshake_IsLoggedUnderTheNameItGave` | dev 38086887082: failed on all three, the line `("exposure", null, null)` | dev 38086884037 and ci 38140060900: passed on all three |
| `ARun_ThatOvertookTheHandshake_IsPutToTheOwnerAndLoggedUnderTheNameItGave` | dev 38086887082: failed on all three, the owner's request naming `(null, null)` | dev 38086884037 and ci 38140060900: passed on all three |

- `ACallFromAClientThatNeverIntroducedItself_IsStillRefusedUnasked` passed in both runs.
- `verify-agent-activity.sh` passed in app 38140067598's desktop gates on all three runners. It meets the ordering only when the race falls that way, so that pass is not evidence of the fix; the regressions are.

## Decisions

None.

## Limits and follow-ups

- The tests call the tools on a subclassed `McpServer`, which the SDK marks experimental (MCPEXP002), rather than through its dispatch; the SDK's ordering itself is still not reproduced in process.
- No test checks the identity a bridge sends when it attaches under a late handshake; T-3 was narrowed to what the tests check instead (founder, 2026-10-11).
- The identity remains the client's unauthenticated claim (T-3).
