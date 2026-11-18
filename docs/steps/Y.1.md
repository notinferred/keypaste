# Y.1 — Refuse a save that loses items, and keep backups longer

Completed 2026-11-18 on branch `vault-safety`, source only.

Every save's read-back now checks the parsed new file against the file it replaces and against the open vault. `KeePassInterop` keeps the entry UUIDs of the file it last read or wrote, and refuses bytes that lack one of them unless the new file's DeletedObjects records its deletion, which a delete for good, an emptied recycle bin and a merge write; a move to the recycle bin keeps the entry. It also refuses bytes whose entries and content digests differ from the open vault's. Either refusal replaces nothing. A merge's result is a fresh parse of the file it merged, so its baseline is that file; a first save has none; a key change checks the same way. Retention keeps the five newest backups plus the newest of each of the last seven UTC days and of each of the last four calendar weeks, pruning only after the new copy is named.

## Evidence

- `VaultSaveTests`: a save that loses an item nobody deleted is refused with the file unchanged; bytes holding other values than the open vault are refused; deleting an item for good saves.
- `VaultBackupTests.RetentionKeepsTheNewestFive_TheNewestOfEachOfSevenDays_AndOfEachOfFourWeeks` runs 41 days of two backups a day through `VaultBackups.Preserve` and checks the eleven kept; the count tests age copies past every daily and weekly keep.
- `VaultRecycleBinTests`, `VaultRemoveTests`, `VaultMergeSaveTests`, `VaultMergeTests`, `VaultRestoreTests`, `VaultAccessChangeTests`, `KdbxImportTests` and `VaultCheckpointTests` pass unchanged.

## Limits and follow-ups

- An overwriting save, which discards another program's write by the person's choice, is checked against the file this vault last read or wrote, not the file it discards; the backup it always takes keeps that file.
- A hardware-key vault's save is not read back until Y.3, so these checks do not reach it here.
