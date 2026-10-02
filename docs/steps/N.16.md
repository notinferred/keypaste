# N.16 — Tell the person when another program saved the vault

Completed 2026-10-02 on `task/n16` above `d45a6a4`, source only; dev 37065297929 at `48c2543`, then ci 37068836495 and app 37068839445 at `5fbd458`, which adds only documents. The squashed commit on `main` is that tree with this record's run lines.

## Amendments

The row asked for a notice with Reload as soon as the open vault stops matching its file. The founder approved this detail on 2026-10-02, with D-0412:

- **The title bar already noticed.** Since 2026-09-25 it hashed the file every five seconds and said "changed on disk", but offered nothing except locking and typing the password again. N.16 puts a notice with Reload above the screen whenever that status holds, on the same five-second check, and after every edit and save.
- **Reload asks for nothing.** `Vault.Reload` opens the file with the composite key the unlock holds, as KeePassXC's reload does: the same password hash, the keyfile material read at unlock rather than the file's current bytes, and a hardware key asked again, because its challenge comes from the file. `AppVaultSession.Reload` swaps the new vault in under the same lifetime, so connected agents keep their session, and withdraws every grant first.
- **No merge.** An edit the app could not write is discarded by Reload, and the notice says so before the click. Merging stays with 1.4a and 1.4b.
- **A key changed elsewhere locks.** When another program changed the password or keyfile, the reopen is refused and the session locks with `ReloadRefused`, whose unlock screen asks for the new ones.
- **A defect fixed on the way.** After the app's own save was refused over another program's, `Vault` checked `_pending` first, so the title bar said "unsaved changes" and agents were told a save was in progress, with their grants kept, although D-0317 says an external save zeroes them. The comparison now hashes the file while a change is pending too and treats a different file as the app's own only while a save is writing (`_writing`), so both report `ChangedOnDisk`.
- **Wording.** The twenty screen messages for a refused save say "Reload to see it" instead of "Lock and unlock to see it", and the bridge's `vault-changed` text names Reload. `keypaste agent`'s terminal still says to restart it, which is its reload.

## Evidence

Every build and test ran on GitHub; this Mac built nothing.

- **Core.** `VaultSavedStateTests`, 5 new: a reload reads another writer's save and saves on from it; a reload discards an edit whose save was refused; a password another writer changed refuses the reload and changes nothing; a keyfile vault reloads after its keyfile's bytes were replaced; a deleted file fails the reload and leaves the vault unreadable, not reloaded. `An_edit_whose_save_was_refused_is_never_read` now expects `ChangedOnDisk`. `VaultSaveStateTests`, 1 new: an edit after another writer's save is `ChangedOnDisk` with `Unwritten`, before and after its refused save, until an overwriting save. `HardwareKeyVaultTests`, 1 new: a reload asks the key again and reads the other writer's save.
- **App.** `CurrentStateTests`, 1 new, over the app's real endpoint: an agent holding a grant, another program's save, then the shell's Reload; the session ID is unchanged and the next request is prompted and releases the new value. Without the withdrawal it would be served `grant-cache` with the old one. `ShellStatusTests`, 2 new: the notice's text on the five-second check, its added sentence after a refused edit, Reload clearing it and rebuilding the screen with the other save's entry and without the edit; and a reload after a password change locking with `ReloadRefused`.
- **Gate.** [verify-current-state.sh](../../scripts/verify-current-state.sh) step 5, on the shipped `keypaste` and `keypaste-mcp` against `Keypaste.AppDriver hold`, whose new `notice` and `reload` act through a `ShellViewModel` over the held session. After `keypaste env set` under another home and the app's refused edit, `notice` names the other save and the discarded edit, `reload` answers `reloaded`, the notice is gone, and the bridge's next request on its open connection is prompted and releases V3, audited under the first session. It replaced the lock and unlock the gate ended with.

| Run | Commit | Scope | Result |
|---|---|---|---|
| 37065297929 | `48c2543` | auto, Linux and Windows | Green. Linux: backend 3258 of 3278 passed and the rest skipped, App.Tests 878 of 882, Consistency 44, the nine desktop gates, integration, and compat under KeePassXC 2.7.6. Windows: backend 3265 of 3280, App.Tests 878 of 882, Consistency 44, the desktop gates, integration, and compat under KeePassXC 2.7.12 |
| 37068836495 | `5fbd458` | ci dispatch, every lane | Green on Ubuntu, macOS and Windows, with the aot publish |
| 37068839445 | `5fbd458` | app dispatch, every job | Attempt 1 failed only `keepassxc first run (ubuntu-24.04)`: KeePassXC 2.7.6's GUI, under `QT_QPA_PLATFORM=offscreen`, listed the gate's three vaults and then died of a segmentation fault during the gate's two-second pause, before any keypaste process started, so the graceful close found no process (`verify-keepassxc-first-run.sh` line 111). Attempt 2 reran that job and the aggregate, and every job passed |

## Decisions

- D-0412.

## Limits and follow-ups

- **Five seconds.** The file is hashed on the status tick, not watched, so a save can go unnoticed for up to five seconds; agents are refused from the first request after it regardless.
- **`keypaste agent` reloads by restarting.** It never edits its vault and has no screen to offer Reload.
- **A prompt answered after a reload** releases the reloaded value under the entry name the person approved, as after an edit in the app.
- **KeePassXC crashed once in the first-run gate.** App run 37068839445's first attempt is the first time KeePassXC's GUI died there in the app runs since 2026-09-30; it is kept here, not closed by the rerun, and has no STEPS row until it recurs or is reproduced.
- **`Vault.Open` hashes after reading.** A save landing during an unlock's key derivation could be stamped as what the vault holds while it holds the older contents. This is read from the code and not reproduced, so it has no STEPS row; `Reload` hashes before it reads.
