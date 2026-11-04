# Q.5 — Make destructive actions consistent and undoable

Completed 2026-11-04 on `q5-destructive-actions`; ci 41919729780 passed its fast tier at `e28c76a6`, and the integrated commit differs from it only in this record and its rebase over `d4843c32`, an app driver change.

## Amendments

The row lists item and group delete among the confirmations. DESIGN § Interaction has what can be undone not ask, so a delete into the recycle bin now goes at once and only a vault with no bin confirms. On 2026-11-04 the founder chose that undoing a duplicate or a new group is an ordinary delete, so in a vault with a bin the copy goes to Trash rather than being erased.

## What changed

- **Danger Allow:** `TerminalEditPrompt.DeletesForGood`, from `EditPreview.Recycles`, draws Allow as danger and reads "Delete for good"; layout, focus on Deny and arming are unchanged.
- **Undo:** `EntriesViewModel` holds the last change's inverse write and Edit › Undo runs it; the Restore link stays, offered for deletes. Core gains `Vault.RestoreRecycledGroup`.
- **Trash:** Restore takes every selected row in one save.
- **Confirmations:** Cancel first and reading Cancel on every one; "Forget them all" and a pairing's Remove confirm in place.

Choices settled in the step, each reversible:
- Any write to the vault, from anywhere, ends the undo, so a reverse never overwrites newer work; opening a form does not end it.
- A group restores to the path it was deleted from, the most recently deleted one at that path, and is refused where another group has taken the name.
- A multi-row restore that cannot put a row back says which and restores the rest; Delete for good waits for a single row.
- There is one level of undo and no redo.

## Evidence

- `UndoTests`: each of the nine changes returns the vault, read back from the file, to its live items and groups before. `TerminalEditWindowTests` shows the danger Allow and the ordinary one. `TrashTests` covers a multi-row restore with one refusal.
- Button order, drawn: `KeepUnsavedWorkTests` on every in-pane form, item and group delete, token revoke, Projects' Remove and Settings' access change, whose one form also carries a keyfile replace; `AgentActivityViewTests` on Connect; `RestoreBackupTests` on Restore backup; `CliLinkSettingsTests` on the CLI link's Replace.
- On the founder's Mac (macOS 26.6, arm64): `verify.sh compile` passed, and `verify.sh test tests/Keypaste.App.Tests` passed 1472 with 3 skipped, with `GuiEditIsVisibleToTheCliTests` and `RestoredEntryIsVisibleToTheCliTests` passing.

## Limits and follow-ups

- The file's bytes are not restored: an undo leaves times changed, a duplicate or new group in the bin, and a restored group's `LocationChanged` new.
- Button order is read from drawn positions in the headless renderer; a person has not checked it on each platform.
- A toast can cover a confirmation's action at the window's bottom right while it lasts, as `ProjectsJourneyTests` found when Start moved under one; it is in BACKLOG's investigation candidates.
