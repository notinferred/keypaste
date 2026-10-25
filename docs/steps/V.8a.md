# V.8a — Expose bounded attachment operations

Completed 2026-10-24 on `t6` in `28c6e833`, source only. At `803e9cb3`, app 40187228211 passed every job on all three runners on its first attempt, and ci 40187220469 on its second: its first failed one job, test (windows-2025), in `LockedAppEndpointTests`, which F.70 holds, and only that job was rerun.

## Amendments

Saving an attachment writes its bytes to disk unencrypted, which PRODUCT §3.4 allowed only for a key file the person asks for. On 2026-10-24 the founder ratified PRODUCT v1.16, which adds an attachment the person asks keypaste to save, written once where they choose (D-0532). The row named no surface; this step gives Core the operations and the CLI the `keypaste attachment` verb, and leaves the app's screen to V.8b.

## What changed

- **Core.** `Vault.Attachments` lists an item's attachments as `EntryAttachment` records, name and size, without decrypting any. `AddAttachment`, `ReplaceAttachment` and `RemoveAttachment` are edits like `SetFields`: one revision each, stamped a later whole second than every version kept (D-0387), raising `Edited` for the item (D-0318), and saying through `keptInHistory` whether the vault's history limits kept the revision. `SaveAttachment` writes one attachment to a new file (D-0534). `KeePassInterop` holds the bytes as `ProtectedBinary` with memory protection on; the content is read from a `Stream` into one buffer, one byte longer than the cap, that is zeroed once KeePassLib has its copy, and the copy `SaveAttachment` reads is zeroed after the write. `VaultNameRules.IsValidAttachmentName` is the rule for a new name.
- **The cap.** Adding and replacing take at most 5 MiB (`EntryAttachment.MaximumSize`, D-0533). KeePassXC 2.7.11 and 2.8.0 have no limit and ask for confirmation above 5 MiB, warning that the database may get very large and slow (`EntryAttachmentsWidget::confirmAttachmentSelection`); a file dropped on its attachments list is not asked about. 5 MiB also sits under the 6 MiB history size KeePass and KeePassXC default to, so a revision holding one such attachment is kept. A larger attachment another program made is listed, kept, removed and saved.
- **History.** Each edit backs the item up as `CreateBackup(_database)` does, then checks the revision survived `MaintainBackups`. KeePassLib, like KeePassXC's `Entry::truncateHistory`, drops the oldest revisions while the history exceeds the vault's item or size limit, counting attachments in every revision that holds them, so a revision larger than the size limit is dropped at once. The CLI says so rather than claiming the history kept it.
- **Saving.** As `VaultKeyfile.Create` writes a key file (D-0506): staged beside the chosen path, owner-only outside Windows, and moved into place without replacing anything, so an occupied path or a directory is refused and nothing is overwritten. That fits §3.4's "written once where they choose": the bytes are written once, into the directory the person chose, and moved onto the name they chose.
- **CLI.** `keypaste attachment ls <item> [--json]`, `add <item> <file> [--name <name>]`, `replace <item> <name> <file>`, `rm <item> <name>` and `save <item> <name> <file>` (D-0535). A missing file, one over the cap, a refused name and an occupied save path are refused before the master password. `add`, `replace` and `rm` take the vault's claim as `field mv` does, and are refused naming the holder while the app or `keypaste approve` holds the vault, since D-0464's request holds 64 KiB; `ls` and `save` read the file and work then. The top-level help's verb column now fits its longest verb, `attachment`.

## Evidence

Nothing was built or run on this machine but the compile check. The batch's ci and app runs are pending. They are to show:

- `Keypaste.Core.Tests.VaultAttachmentTests`: an added attachment is one revision naming the item and survives a save byte for byte; listing is in ordinal order and an unknown item is null; sizes read as `1 byte`, `812 bytes`, `4.0 KiB` and `2.5 MiB`; an existing name and nine refused names change nothing; exactly 5 MiB is attached and one byte more is refused, from a seekable and an unseekable stream, for add and replace alike; replacing and removing keep what the attachment held in the newest revision; an attachment the item lacks changes nothing; with a 5 MiB and a 2 MiB attachment the next edit's 7 MiB revision is dropped and `keptInHistory` is false; KeePass's `statement.txt` stays byte for byte through an add, replace, remove and a field edit; and saving writes the bytes once with mode 600 outside Windows, leaves an occupied path and a directory untouched, and writes nothing for an absent attachment or item or a missing directory.
- `Keypaste.Core.Tests.VaultHistoryTests`: an add, replace and remove of an item whose versions share a second an hour ahead each take the next second.
- `Keypaste.Cli.Tests.AttachmentVerbTests`, and the verb added to `CliAppTests`, `HeldVaultRefusalTests`, `PlainWordsTests` and `SecretHygieneTests`; `SavingVerbSourceRulesTests` finds the three new `OpenHeld` calls reaching a save and the two `Open` calls reaching none.
- `scripts/verify-keepassxc-fields.sh`, in ci's `compat` jobs: on the vault KeePassXC made, the shipped CLI adds, replaces and removes an attachment; keepassxc-cli exports the added and the replaced bytes exactly, finds none after the removal, counts three more revisions and still names it in the history; the attachment KeePassXC made exports byte for byte, keypaste's `attachment save` writes the same bytes, and the file is still KDBX 4.0.

## Decisions

D-0532 (the founder's ratification), D-0533, D-0534, D-0535; D-0482 and D-0287 now name the second exception.

## Limits and follow-ups

- The 5 MiB cap is this step's choice, for the founder to confirm.
- Adding, replacing and removing from a terminal while the app or `keypaste approve` holds the vault is a BACKLOG option; V.8b gives the app its own screen.
- The vault's history size limit counts an attachment in every revision that holds it, so an item holding several MiB of attachments keeps few revisions of its other values, as in KeePass and KeePassXC.
- The stage beside a saved file can be left there by a crash between the write and the move, as for a key file.
- keypaste.com's how-it-works page still says keypaste never writes a secret to disk unencrypted, without v1.13's or v1.16's exception; its pages change with a release, so this step leaves it.
- Source only.
