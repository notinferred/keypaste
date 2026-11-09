# F.85 — Read the open item again after a merge

Completed 2026-11-09 on `backlog-2026-11`, source only; no CI run has happened yet, so no run IDs are recorded.

## Amendments

The founder selected this from BACKLOG's investigation candidates on 2026-11-09: after a merge, the open item is read again unless it holds a draft or is being edited. There is no STEPS row.

## Judgment calls

- **The pane is read in place, not rebuilt**, so an open history stays open and a form holding nothing typed stays where it is. Only merges do this: `Reload` still leaves the pane alone after the app's own edits, which keep it current themselves.
- **An open history is read again too**, because a merge can add a revision, which drops a comparison in progress even when the merge touched another item.
- **A revealed value stays hidden**: `VaultMoved` ends the reveal before the pane is read.

## Evidence

`ShellStatusTests.AMerge_ReadsTheOpenItemAgain_InTheSamePane` failed before the change (the pane's username stayed empty) and passes after. `AMerge_LeavesTheOpenItemAlone_WhileAFormOnItHoldsADraft`, `AMerge_ThatMovedTheOpenItem_ShowsItWhereItIsNow`, `AMerge_ThatRemovedTheOpenItem_ClosesThePaneWithoutAnError` and `RevealMergeTests` hold the rest, 24 tests passing locally on macOS.

## Limits and follow-ups

A merge that lands while a form holds a draft or an edit is open is not read in once the form closes, and a terminal's edit to the open item still reaches only the list; both are in BACKLOG, and neither was run.
