# F.87 — Hold the app's bans in the attachment files

Completed 2026-11-09 on `backlog-2026-11`, source only; no CI run has happened yet, so no run IDs are recorded.

## Amendments

The founder selected this from BACKLOG's investigation candidates on 2026-11-09: wrap only the calls that need the exemption and restore RS0030 after each. There is no STEPS row.

## Evidence

Neither attachment file turns RS0030 off for the whole file now. `EntryAttachmentRow.cs` exempts the read of an attachment's name and size; `EntryAttachmentsViewModel.cs` exempts the five `Vault` attachment calls, three `AttachmentSaveOutcome` cases and the two uses of `EntryAttachment.MaximumSize`, in nine pragmas, the banned uses a build listed with the file-wide pragmas removed. `BanExemptionsTests` lists the new count.

`AttachmentSourceRulesTests` shrank by what the compiler now holds there: `System.Diagnostics`, `IWebLauncher` and `OpenAsync`. It keeps core's launch, process, tool and link code, the app's `Opener` delegates and `Action<Uri>`, `NavigateUri` and a P/Invoke of `ShellExecute`, none of which any ban covers for two files alone.

A probe in `EntryAttachmentsViewModel.Dispose`, outside the wrapped lines, failed the app's build with RS0030 on `File.WriteAllText`, `System.Net.Http` and `Process.Start`, and was removed.

`AttachmentSourceRulesTests` passes 2 of 2 and `BanExemptionsTests` 4 of 4 locally on macOS, and `verify.sh compile` passes.

## Limits and follow-ups

The analyzer does not flag a banned type named only in a declaration, so `EntryAttachment` parameters and an `AttachmentSaveOutcome` local need no exemption, here or elsewhere in the app. The app was not started by hand.
