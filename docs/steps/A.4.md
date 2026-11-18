# A.4 — Write vault files and backups through one Core class

Completed 2026-11-18 on branch `vault-safety`, source only.

`Internal/VaultFiles.cs` now makes every write, copy, rename and removal of a vault file, its export and its backups: the backup copy and its rename, a restore's staged file and replacing rename, an archived conflicted copy, pruning, sweeping temporaries, the backup and vault directories, an export, the replacement's permissions and the stranded-temporary sweep after a failed save. The save's own replacement stays in `KeePassInterop.Replace`, through KeePassLib's `FileTransactionEx`, because only that file may reach KeePassLib. `BannedSymbols.NoFileWrites.txt` now also binds the CLI, the bridge and the browser relay, none of which wrote a file, and bans `File.Delete`, `File.Replace`, `File.Open` with an access, `File.OpenWrite`, appends and directory removal and moves besides what it banned. Inside Core a ban list cannot tell a vault path from a settings path, so `VaultFilesSourceRulesTests` reads the source instead. D- references were removed from the code touched.

## Evidence

- `VaultFilesSourceRulesTests`: no top-level `Vault*.cs`, `KeePassInterop.cs` or import file names a raw write, `FileTransactionEx` appears only in `KeePassInterop.cs`, and each rule finds its mutation.
- `bash scripts/verify.sh compile` builds the front ends under the extended list; `VaultBackupTests`, `VaultRestoreTests`, `VaultAccessChangeTests`, `VaultConflictedCopyTests`, `VaultCreationTests`, `VaultSaveTests`, `VaultSaveUnderATransactedNameTests`, `ConcurrentVaultSaveTests` and `BanExemptionsTests` pass.

## Limits and follow-ups

- The source rule covers the files that handle vault paths by name; a new Core file elsewhere that writes a vault path is not caught.
- `SourceSnapshot.DeleteIfUnchanged`, which removes a file the person imported from after the import, may remove another vault's file and does not go through `VaultFiles`.
- Moving the save's replacement itself into `VaultFiles` would mean replacing KeePassLib's file transaction, Transactional NTFS included, with keypaste's own; that is a founder decision.
