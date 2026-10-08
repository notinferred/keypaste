# F.60 — Report a save whose write fails, and cool a refused run per field

Completed 2026-10-08 at `99099b1` on `task/f60`: the dispatched ci 37781610519 and app 37781626433 runs passed every job on all three runners. The integrated commit differs from that tree only in this record.

It had no STEPS row: an audit of the 2026-10-07 changes found both defects, and the founder asked for its findings to be fixed.

## Amendments

- The first version serialised the vault before opening its file. Its full runs (ci 37779953738, app 37779968026) failed `VaultSaveTests.ASaveThatCannotSucceed_GivesUpQuickly` on `macos-15` and `windows-2025`, a doomed save taking 6.2 s and 7.9 s against 5 s: each of its eight attempts now derived the key before finding the directory gone, which `PwDatabase.Save` never did, since it opened the file first. The file is again opened before the vault is serialised; an access change still verifies its bytes before opening, as before.

## What changed

- An ordinary save no longer goes through `PwDatabase.Save`. KeePassLib writes a vault's encrypted body, held in a one-megabyte block buffer, only while it closes its streams, and both `KdbxFile.DisposeStreams` and `CryptoStreamEx.Dispose` discard what that write throws, so a disk that filled mid-save had its truncated temporary file committed over the vault and the save reported as done. `KeePassInterop` now opens the transaction's file, serialises the vault to memory, writes those bytes to the file itself, flushes them to disk and only then commits, as an access change already wrote its bytes (D-0428). The vault's stamp is the SHA-256 of those bytes.
- A refused run cools each entry field it asked for under the key a refused credential request uses, besides its connection, so a run or a credential request reaching any of them is refused unasked from every connection for the minute. The run cooldown had been keyed by the whole set, so a new bridge asking with one variable more or fewer was asked about again, and a refused field did not hold back a run injecting it (D-0429).
- THREATS T-11 and `docs/mcp-setup.md`'s cooldown line say so.

## Evidence

- Regressions, run on a control branch carrying the new tests over the old code (`fabb873`, since deleted) and failing there:

| Test | Control (old code) | Fix |
|---|---|---|
| `VaultSaveTests.A_write_that_fails_partway_is_reported_and_leaves_the_vault_as_it_was` | dev 37779386355: failed on `ubuntu-24.04` and `macos-15`, the vault path holding the 0-byte pipe where a 1,637-byte vault had been; skipped on `windows-2025` | ci 37781610519: passed on `ubuntu-24.04` and `macos-15`, skipped on `windows-2025` |
| `SessionAuthorityRunTests.ADenial_CoolsEverySetSharingAFieldFromANewConnection` | dev 37779719466: failed, `Prompt` where `Cooldown` was expected | ci 37781610519: passed on all three runners |
| `SessionAuthorityRunTests.A_refused_field_holds_back_a_run_and_a_refused_run_holds_back_a_credential_request` | dev 37779719466: failed, `Prompt` where `Cooldown` was expected | ci 37781610519: passed on all three runners |

- The save test puts a named pipe at the temporary file's name; each attempt's reader takes the first kilobyte and closes, so the rest of the write fails as on a full disk. In the control runs every other test of both classes passed.

## Decisions

D-0428, D-0429.

## Limits and follow-ups

- Windows has no pipe at a file path, so the save regression runs on Linux and macOS only; Windows takes the same code path.
- Flushing to disk is not shown by a test; whether a power loss right after the rename keeps the new bytes depends on the filesystem honouring the flush.
- After a refused run, a run sharing any one of its fields, and a credential request for any of them, is refused for the minute whichever agent asks.
