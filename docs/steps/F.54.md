# F.54 — Cool a refusal across connections and audit run releases

Completed 2026-10-07 at `0aab427` on `task/f54`: the dispatched ci 37639979990 and app 37639995600 runs passed all 29 of their jobs on all three runners. GitHub never created their `ci ok` and `app ok` jobs during its Actions incident of that afternoon, so both runs show as failed and cannot be re-run. The integrated commit differs from that tree in this record and in `scripts/dev.sh`, which now reports such a run as failed instead of green.

It had no STEPS row: the founder chose this group from the audit of keypaste against two engineering references on 2026-10-07 (D-0425).

## Amendments

- The plan named a per-connection idle read deadline for the owner's listener. It was left out: a bridge's connection is idle between an agent's requests, and every grant a person gave it ends when the connection does, so closing idle connections would silently withdraw "Allow for 1 hour". The connection cap bounds what idle or stalled peers can hold.
- The plan keyed only the credential cooldown by entry and field. The run path had the same escape, keyed by connection alone; keying it by the secrets asked would have let an agent try another project on the same connection, so a refused run now cools both its connection and the secrets it asked for.
- The audit's claim that `run --session` releases were never audited was a limit THREATS T-30 documented (D-0020); the founder chose to close it.

## What changed

- `ApproverHandler`'s cooldown key is the entry's handle and field, so a refusal holds back the same field from every connection for sixty seconds.
- `ApprovalGate.AskAsync` for a run takes several cooldown keys: a run cooling under any is refused unasked, and a refusal cools them all. `SessionAuthority` passes the connection and the secrets asked.
- `ApproverListener` serves at most `MaxConnections` (64) at once and closes one more as it connects; served connections are counted so a hang-up frees its place before its handler hears it ended.
- `SessionAuthority.ReleaseEnvAsync` writes the audit line for each release or refusal of a run's set through the writer the token path uses, with the method that decided it (prompt, timed grant, a refusal's own method), and refuses a release whose line cannot be written. Both owners already pass the audit log; the app opens it on first use.
- THREATS T-10, T-11 and T-30 describe the cap, the cooldown and the run audit.

## Evidence

- `ApproverHandlerTests.ADenial_CoolsTheSameFieldDownForAnotherConnection`, `SessionAuthorityRunTests.ADenial_CoolsTheSameSecretsDownForANewConnection`, `ApproverListenerTests.AConnectionOverTheCap_IsClosedAndTheOthersAreServed`, `SessionAuthorityEnvTests.A_run_release_and_a_refusal_are_each_audited_by_the_owner` and `A_run_whose_audit_line_cannot_be_written_releases_nothing` passed on all three runners in those runs.
- A security and concurrency review with a skeptic per finding confirmed four defects, all fixed before the runs: the run cooldown keyed by spelling rather than by the secrets resolved, the audit-size refusal coming after approval, refusals audited under the wrong method, and a THREATS line claiming every refusal was audited. One finding was refuted.
- The first dispatched runs (ci 37635374091, app 37635390387) failed where existing tests and gates still expected a refusal to cool only its own connection; 0aab427 brought them to the new rule. `ApproverHandlerPolicyTests.ARefusalAPersonJustGave_OutranksAMatchingRule` now reaches the state through a refusal on another connection instead of arming the gate directly, and the listener, handler and env tests and the `verify-desktop-approval.sh` and `verify-keepassxc-projects.sh` gates no longer rely on a new connection to start without a cooldown.

## Decisions

D-0425.

## Limits and follow-ups

- After a refusal, a different agent asking for the same field or the same secrets within the minute is refused too.
- A credential release is still audited by the bridge, not the owner (D-0020).
- With the audit log unwritable, every `run --session` release is refused, as a token's already was.
