# F.81 — Leave what another program's save did not change

Completed 2026-11-09 on `merge-keeps-untouched-items`, source only; ci fast tier 42635322902 green at the first attempt.

## Amendments

The founder selected this on 2026-11-09 after testing a copy of their own vault: fix the merge, the outdated `compat` gate and reading a save back before it replaces the vault. There is no STEPS row.

## Judgment calls

- **KeePassXC's `_LAST_MODIFIED` custom-data item is left out of what a merge compares**, on entries and groups. KeePassXC rewrites it on save, so every item with custom data looked changed.
- **A version KeePassXC keeps is matched without custom data**, because KeePassXC leaves custom data out of the history copies it makes, and otherwise an item it really edited was reported as changed by both.
- **A vault with a hardware key is not read back**, since that would ask the key a second time for one save.

## Evidence

On a private copy of the founder's vault, 469 entries with 371 carrying KeePassXC-Browser data, three KeePassXC saves merged: before, 371 entries changed and history records went from 758 to 3,194; after, one entry changed and history went to 761, each merge reported as `merged updated`. No password changed either way. A control with KeePassXC alone changed one entry.

`VaultMergeTests`, `VaultMergeSaveTests`, `VaultSaveTests` and `VaultBackupTests`: 85 passed locally. `AStampKeePassXcRewrote_IsNotAChangeToTheItem` and `AnItemKeePassXcRecycledAndRestamped_IsTheFilesWhole` failed before the fix, and `verify-keepassxc-merge.sh`'s new G13 failed before it. The merge, keyfile and projects gates pass locally on KeePassXC 2.7.12 on macOS.

`verify-keepassxc-projects.sh` had three expectations F.77 and F.78a left behind: `a` now starts an Always allow, custom fields are listed whatever their name, and every run's prompt says it is asked each time.

## Follow-up

The founder's copy, tested action by action on 2026-11-09, showed a save resetting the vault file's permissions: a 600 file became 644 through the rename. A save now gives the replacement the vault's own mode before the rename, and `A_save_keeps_the_permissions_the_vault_file_had` holds it.

## Runs on `main`

At `d69d5bd5`, every `compat` job passed; ci 42637103496 needed a rerun of `test (windows-2025)` for `LockedAppEndpointTests.APeerThatNeverHangsUp_HoldsNothingUp`, and app 42637103393 a rerun of `publish + selftest (macos-15)`, which was cancelled.

## Limits and follow-ups

The tests made on the founder's copy, opening without writing and 30 forced kills during a save, were one-off and are not regression tests. Reading back adds about one key derivation to each save.
