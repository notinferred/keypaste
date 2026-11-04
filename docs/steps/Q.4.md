# Q.4 — Keep unsaved work

Completed 2026-11-04 on `q4-keep-unsaved-work`.

## Amendments

The founder settled three gaps in the row on 2026-11-04. Escape on a form holding typed content asks "Discard …?" first, where the row had it cancel at once; the Cancel button still closes without asking. Choosing another row on Items asks too, and a merge of another program's save leaves a screen holding a draft as typed, as it already left Edit. A draft is any typed text in any form, not only the three the row names.

## What changed

- **One guard** (`ShellViewModel.Leave`): the sidebar, shortcuts, a search typed elsewhere, Back, Reload and every command that goes to a place run through it. A screen reports its draft through `IHoldsDraft`; Discard rebuilds the screen, so a draft goes even where the move stays on the same screen. The dialog is drawn over every other, focus on Cancel, Escape keeping the draft.
- **One behaviour for keys** (`Controls/FormKeys.cs`): a form declares its confirm, its Cancel and its draft. A danger form declares no confirm, and `ShowInView` puts the keyboard in each form as it opens, on its first field, else its confirm, else its quiet button where the confirm is a danger one.
- `MaskedInput` leaves Escape unhandled so it reaches the form, and Return unhandled when nothing listens for it. The Share dialog gains Cancel.
- Errors under their fields as typed: New item's title, key name and tag, Rename or move's name and Projects' key name, which uses core's `EnvStore.IsNewFieldName`. Create waits for a title.

Choices settled in the step, each reversible:
- A password keypaste generated is not a draft; one typed over it is.
- Return in a tag box adds the tag rather than creating the item, and a multi-line field keeps Return for its newline.
- New item from the generator leaves the generator open behind the question, since closing it disposes the value.
- An agent's limit or pattern being typed survives the settings' refresh, and a screen left as typed through a merge shows the merge once it next reloads.
- Rows and combo choices are not drafts: Move group and Add an item to an environment close without asking.

## Evidence

- `KeepUnsavedWorkTests` (77), drawn in the real shell: a draft kept after Cancel and gone after Discard from the sidebar, a shortcut, a search and a row; no question on an untouched form; a lock discarding without asking; the generator waiting; Return confirming and writing on 18 forms and writing nothing on 7 danger forms; Escape on all 25, with the question on 11; masked Escape; the Share dialog; each error under its field.
- `AgentSettingsViewModelTests` holds the limit through a refresh until applied, and `ShellStatusTests` holds a draft through a real merge.
- On the founder's Mac (macOS 26.6, arm64): `verify.sh test tests/Keypaste.App.Tests` and `verify.sh compile` passed.

## Limits and follow-ups

- Escape in a masked field clears it before the form asks, as the row says, so a form whose only draft is that field closes without asking.
- Tests do not drive Return and Escape on these forms, which declare them the same way: Connect, Run's Start, the CLI link's Replace, Recommendations' Add tag, a project's tag change and adding an item to an environment.
- Within a screen, changing an agent's limit target or a form replacing another open form still drops what was typed there.
