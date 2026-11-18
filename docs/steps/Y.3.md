# Y.3 — Read back hardware-key vaults, and flush backups and renames to disk

Completed 2026-11-18 on branch `vault-safety`, source only.

A hardware-key vault's save is now read back like any other ([law 12](../../SECURITY.md#security-laws)), with Y.1's checks. The key is not asked again: the bytes' challenge is their KDF seed, the save just asked the key with that seed, and `HardwareKey` keeps the last answer for the same challenge, so the read-back reuses it. Backups are copied by `VaultFiles.Copy`, flushed before their rename. On Linux and macOS each rename of a backup, a restore and a save is followed by an `fsync` of its directory (`VaultFiles.SyncDirectory`); on Windows those renames use `MoveFileEx` with `MOVEFILE_WRITE_THROUGH`, the save's through a new vendored KeePassLib guard, `KEYPASTE_DURABLE_REPLACE`, which also covers the Transactional NTFS fallback.

## Evidence

- `HardwareKeyVaultTests`: a save reads its bytes back with the device disconnected after the save asked it, and bytes that do not open are refused with the file unchanged; `Every_save_asks_the_key_with_a_new_challenge_and_the_vault_still_opens` still counts one question per save.
- `VaultFilesTests`: a copy is byte-identical and owner-only, a move replaces only when told to and names a missing source, and a directory flush succeeds off Windows.

## Limits and follow-ups

- A directory flush is best effort: it follows a rename that already happened, so a file system that refuses it does not fail the save. macOS's `fsync` reaches the drive's cache, not `F_FULLFSYNC`'s platter.
- No test observes a flush reaching the disk; Windows' write-through and its error mapping are exercised only by CI's Windows run.
