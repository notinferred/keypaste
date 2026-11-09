# F.92 — A merge says nothing of agents' retired records

Completed 2026-11-09 on `backlog-2026-11`, source only; CI has not run on it, so there are no run IDs.

## Amendments

The founder selected BACKLOG's row on an always-allowed row a merge could bring back, deciding that a removal holds on this computer and the person is told. Diagnosis found no such path: since F.78a a row lives only in the sealed `agents.toml`, which a merge cannot write, and no request reads a vault's `.keypaste/agents` records. The founder then chose to close the row with evidence and remove the wording that claimed the opposite. No hold or new notice was built.

## Evidence

`RemovedAlwaysAllowAcrossMergeTests` holds the behaviour through a real merge and through an older file replacing the vault, each carrying the old row in the agent's record. Both pass on the code before this step by design, since F.78a already closed the defect.

`SessionAuthorityMergeTests.AMergeChangingARetiredAgentRecord_CarriesIt_AndItsLineSaysNothingOfSettings` and `MergeNoticeTests.A_merge_changing_a_retired_agent_record_carries_it_and_says_nothing_of_settings` failed before the change on the words it removes, and pass. Run locally, each class once: those three with `VaultMergeTests` and `PlainWordsTests`, 59 passed. `verify.sh compile` passed.

## Limits and follow-ups

The merge still carries retired records like any entry, and ACAF v1 and v1.1 describe that; only the report of them went. Lines already written keep their words: the chain covers each line's bytes and nothing reads the reason's text. Restoring both the vault and an older `agents.toml` to matching counts would bring a row back, which takes a restore of the keypaste home and not a merge; T-15 covers the file alone.
