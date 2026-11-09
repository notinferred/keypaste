# F.84 — Merge another device's changes field by field

Completed 2026-11-09 on `merge-by-field` at `f58e2769`, source only; ci fast tier 42683107734, dev desktop 42684358951 and dev desktop-gates 42686713837, all green on Linux.

## Amendments

The founder selected this on 2026-11-09: a sync tool's conflicted copy is reviewed and merged into the vault, and two devices' changes to different fields of one entry are both kept. There is no STEPS row.

## Judgment calls

- **A field merge needs the latest version both copies keep in history**, matched without custom data as KeePassXC's history drops it. With none, the newer version wins and the older is in history, as before. keypaste's own records are never combined.
- **A field both changed is the newer's**, the other value in history. A combined entry is stamped after both versions, and both are kept in its history.
- **A version the other copy keeps in history is treated as unchanged**, so a stale file or an older conflicted copy is no conflict.
- **A conflicted copy is named by the patterns Nextcloud, Dropbox and Syncthing use**, reviewed without writing, merged into the file, and moved in with the backups under its own name, where no rotation removes it.
- **A copy that needs other credentials is refused** with a message; the founder's default of asking for them was not built.
- **A file that is gone or unreadable offers Save what's open here**, which keeps the bad file as a backup first.
- **A group moves or renames with duplicate names inside it**, since a group's items keep their places relative to each other.

## Evidence

`VaultMergeTests`, `VaultMergeSaveTests`, `VaultOrganizeTests`, `VaultConflictedCopyTests` and `ConflictedCopyRecommendationTests` pass locally. `verify-keepassxc-merge.sh`'s G14 keeps both changes through real KeePassXC.

## Limits and follow-ups

A real phone app's files are still only simulated by KeePassXC. The review lists what comes in, and it does not list what a merge removes.
