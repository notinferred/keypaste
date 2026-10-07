# F.55 — Give each rule one owner in Core

Completed 2026-10-07 at `56faf30` on `task/f55`, with F.56: the dispatched ci 37653540314 and app 37653558120 runs passed every job on all three runners. The integrated commit differs from that tree only in the records.

It had no STEPS row: the founder chose this group from the audit of keypaste against two engineering references on 2026-10-07 (D-0426).

## Amendments

- The CLI's reserved-group checks that refuse before a password prompt stay in `add`, `set`, `rotate`, `env tag` and `share`: they are refusals with their own exit codes, not filters, and they call Core's `ReservedGroups.IsReserved`.
- `Vault.AddEntry` stays unchecked, because keypaste's token and share stores write reserved names through it; `add` and `set` moved to the checked `CreateEntryAtPath`.
- `set --field` stays an upsert and the app's add field refuses an existing field (`Vault.AddField`), as the plan kept them.
- Three `LastIndexOf('/')` splits stay, because they split a `kp://` place, an indented display name and a group path rather than an entry name.
- `VaultAccessViewModel.ChangeAsync` and `RestoreBackupViewModel.ConfirmAsync` keep their own catch: an access change saves through the session itself, and a backup restore runs with no vault open.
- `AtomicFile` also took `AppSettings.Save` and `RecentVaults.Save`; `VaultBackups` and `SourceSnapshot` stay as they are, since a backup must never replace an existing one and the restore is a guarded compare-and-replace.

## What changed

- Core owns entry creation (`Vault.IsCreatable`, `CreateEntryAtPath`, `AddField`), `.env` reading with its size check before the read (`DotEnvFile.TryRead`, used by `env pull`, `run` and the app's import), the reference export and the never-over-a-vault guard (`EnvReferenceExport`, `VaultOverwriteRule`), share link settings and validation (`ShareClient.CreateTransport`, `ShareService.IsValid*`), `EntryName.Parse`, and a `RunReference` parsed once.
- `ReadEntries`, `ReadGroupPaths` and `Search` hide keypaste's own groups unless `includeReserved: true`; the stores, backups, import collision checks and `rm` opt in, and the front ends' filters are gone.
- `SessionServer.Listen` builds the owner for one unlock, used by `keypaste agent` and the app's `SessionHost`; edit revocation is a required option, so `keypaste agent` now withdraws grants when the vault is edited.
- `AppVaultSession.Write<T>` is the app's one write path: it runs the edit, saves, and maps a lock, a change on disk or a failure to one `WriteResult`; every view-model save calls it except an access change, which saves through the session itself, and a backup restore, which runs with no vault open.
- `src/Keypaste.Core/Infrastructure/` holds the pieces that know nothing of a vault: `Toml` and one `TomlWriter` (used by `app.toml`, `recent.toml` and client policies), `AtomicFile` (temp, flush, move; replacing three copies), `MessageFramer` and `CommandLine`. `InfrastructureBoundaryTests` keeps the folder free of the rest of Core.
- The MCP paste block encodes each string with `JsonEncodedText`.

## Evidence

- Regressions for the drift the audit found: `SetVerbTests.Add_ATitleCoreRefuses_IsRefused_AndWritesNothing` and `Set_ANewEntryWithATitleCoreRefuses_IsRefused_BeforeTheValue` (the CLI accepted titles the app refused), `EnvPullTests.Pull_OfAnOversizedFile_IsRefusedBeforeItIsRead` (a sparse 2 GiB file failed with an I/O error rather than the size refusal), `RecentVaultsTests.A_path_the_file_cannot_hold_is_left_out_and_every_other_vault_kept` (one such path made `recent.toml` unreadable), `McpClientSetupTests.A_block_to_paste_is_json_that_names_every_path_exactly` (a control character, quote, backslash and tab), `VaultReservedReadTests`, `SessionServerTests` and `WriteThroughSessionTests` (ten cases, all three runners).
- Each phase-1 branch and each view-model branch passed the test classes it touched on Linux before the full runs; the run IDs are in the branches' reports, not repeated here.

## Decisions

D-0426.

## Limits and follow-ups

- Behaviour users can see: `add` refuses titles with leading or trailing whitespace or a backslash; the app's reference export refuses a profile with no variables and words a repeated-key refusal as Core does; `keypaste agent` releases nothing once its session has ended.
- `verify-keepassxc-fields.sh` on `ubuntu-24.04` waited 30 seconds for the desktop's approval prompt and none appeared, once, in an earlier full run of this branch (ci 37640075673, first attempt); it passed on the re-run and is a BACKLOG observation.
- On Unix a remembered vault path containing a backslash is now skipped rather than remembered as a different file, and an `app.toml` vault path the writer cannot represent is left out while the other preferences are kept.
- `recent.toml` and `app.toml` may be up to 256 KiB, up from 64 KiB, so ten long non-ASCII paths with keyfiles still read.
- A file written through `AtomicFile` replaces a symlink at its path with a regular file, as client policies already did.
