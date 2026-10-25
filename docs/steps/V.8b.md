# V.8b — Manage attachments from the app

Completed 2026-10-24 on `t6` in `65675b0a`, source only. At `803e9cb3`, app 40187228211 passed every job on all three runners on its first attempt, and ci 40187220469 on its second: its first failed one job, test (windows-2025), in `LockedAppEndpointTests`, which F.70 holds, and only that job was rerun.

## Amendments

None: the row is built as STEPS had it, on V.8a's core calls.

## What changed

- **The pane.** An item's preview has an Attachments section under its custom fields, `EntryAttachmentsViewModel`, which `EntryDetailViewModel` owns, reads again after a restore or an edit, and disposes with itself, so a lock leaves nothing of it. Each row is `EntryAttachmentRow`: the name, drawn as `EntryNameSanitizer` draws a name, the size as `EntryAttachment.SizeText` gives it, and Save, Replace and Remove. No content is a property of either or passes through them: adding and replacing hand `Vault.AddAttachment` and `ReplaceAttachment` a stream over the chosen file, and saving hands `Vault.SaveAttachment` the chosen path.
- **Adding, replacing and removing.** Add attachment opens the platform's open dialog and attaches the file under its own name; Replace on a row does the same for that attachment, and Remove asks first, saying history keeps it unless the item is larger than the vault's history size limit. Each is one `AppVaultSession.Write`, so one revision and one save, and refreshes a history read before it (D-0229). Core's refusals, a file over 5 MiB and a name the item already has, are shown as it words them, and a file that cannot be read is named. When `keptInHistory` is false the pane says what is gone: nothing, beyond the revision, for an add, and what the attachment held for a replace or a remove.
- **Saving.** Save opens the platform's save dialog with the overwrite prompt off, starting from `EntryAttachmentRow.SafeFileName`: the drawn name with `" * : < > ? | \ /` made underscores and no trailing dot or space, and `attachment` for a name that leaves nothing. An occupied path, which a dialog that asks anyway may have been told to replace, is refused by core and explained, with Save again to choose a free name; a saved copy is followed by where it is, that it is not encrypted, who can read it and that keypaste cannot take it back (D-0536).
- **Pickers.** `IVaultFilePicker` gains `PickAttachmentAsync` and `PickAttachmentDestinationAsync`, which `StorageProviderPicker` answers with no type filter; the shell hands its picker to Items, and Items to the pane. A picker answered after the vault locked does nothing, since the lock has disposed the pane.
- **Never opened or run.** `AttachmentSourceRulesTests` holds three rules over `src/Keypaste.App`: only the two attachment files reach an attachment; neither names a launcher, a process, `System.Diagnostics`, an address, `OpenAsync`, `NavigateUri`, a terminal or core's tool and link code; and nothing in the app calls `LaunchFile…`, `LaunchDirectory…` or `NavigateUri`, while `LaunchUriAsync` is only in `PlatformWebLauncher.cs` and `Process.Start` only in `OpenerProcess.cs`, which take a web address.

## Evidence

Nothing was built or run on this machine but the compile check. The batch's ci and app runs are pending. They are to show:

- `Keypaste.App.Tests.ViewModels.EntryAttachmentsTests`: KeePass's `statement.txt` listed as 15 bytes and an item without attachments listed empty; an added file is one revision under its own name, byte for byte, beside `statement.txt` kept byte for byte; exactly 5 MiB is attached; a cancelled picker writes nothing; a file over the cap, a name the item has and a missing file write nothing and say why in core's or the pane's words; a replace keeps the name as one revision; a remove asks first, a cancel writes nothing, and the confirmed remove is one revision; a save writes the bytes where the person chose, mode 600 outside Windows, starts from the attachment's name and says the copy is not encrypted; a save to an occupied path leaves its file and says to choose another name; with a 5 MiB and a 2 MiB attachment a remove says the attachment is gone for good; a name another program wrote with `../`, `/` and a bidi override is drawn and suggested as `.. .. .ssh txt.exe` and saved there by the name the vault holds; eight names give a safe suggested file name; and a lock while the picker is open writes nothing.
- `Keypaste.App.Tests.AttachmentSourceRulesTests`: the three rules above.
- `Keypaste.App.Tests.Session.PlainWordsTests`: every screen, with an attachment on the demo item and its remove confirmation open, holds none of the retired words.

## Decisions

D-0536.

## Limits and follow-ups

- One file at a time, from the dialog: KeePassXC also takes several at once and a file dropped on its list.
- No other name on adding: a file named as an attachment the item has is refused, so it is replaced or renamed on disk first.
- History's comparison lists no attachments; the restore caption says a restore drops those added since.
- A save dialog that asks before replacing whatever the setting says is answered with an explanation, not a second dialog.
- Checked by headless view-model tests only: no installed app has been driven, and no KeePassXC gate drives the app's attachment acts, which make the core calls `verify-keepassxc-fields.sh` drives through the CLI.
- Source only.
