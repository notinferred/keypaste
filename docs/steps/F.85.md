# F.85 — Read the open item again after a merge

Completed 2026-11-09 on `backlog-2026-11`, source only; no CI run has happened yet, so no run IDs are recorded.

## Amendments

The founder selected this from BACKLOG's investigation candidates on 2026-11-09: after a merge, the open item is read again unless it holds a draft or is being edited. There is no STEPS row.

The founder then widened it the same day. Save writes only what the person changed, and where both sides changed one field the person's Save wins with the other in history. The open item is also read again after a terminal's edit, and when a form that held off a change closes, only if one landed while it was open.

## Judgment calls

- **The pane is read in place, not rebuilt**, so an open history stays open and a form holding nothing typed stays where it is. `Reload` still leaves the pane alone after the app's own edits, which keep it current themselves.
- **An open history is read again too**, because a merge can add a revision, which drops a comparison in progress even when the merge touched another item.
- **A revealed value is hidden before each re-read**, a terminal's edit to another item included, as a merge already does.
- **Rename or move had the same defect** and follows the same rule. Field forms, tags, attachments, group forms and project variables each write one thing the person named, so they needed no change.

## Evidence

Run against the code before the fixes, `ShellStatusTests` and `RevealMergeTests` failed 9 of 35: `AnEditSavedAcrossAMerge_KeepsTheFieldItDidNotTouch`, `ARenameSavedAcrossAMerge_KeepsTheGroupItDidNotTouch`, `AMergeAFormHeldOff_IsShownOnceTheFormIsCancelled` (edit and add field), `AMergeAnEditHeldOff_IsShownOnceTheEditIsSaved`, `ATerminalEdit_ReachesTheOpenItem`, `ATerminalEditAFormHeldOff_IsShownOnceTheFormIsCancelled` and both new reveal tests. `AnEditSavedAcrossAMerge_WinsTheFieldBothChanged_AndHistoryKeepsTheOther` passed before, because the old Save already overwrote that field; it fails when Save is changed to keep the other side's value. Those classes with `EntriesViewModelTests`, `EntryFieldsTests`, `EntriesOrganizeTests`, `EntryHistoryTests` and `SecretHygieneTests` pass locally on macOS.

## Limits and follow-ups

When the Save itself meets another program's save, the merge still keeps the newer of a field both changed, so a device with a clock ahead can win; that is in BACKLOG. Moving from one field form straight to another keeps a held change waiting until a form closes.
