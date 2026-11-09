# F.90 — Say which of two same-named items a share link came from

Completed 2026-11-09 on `backlog-2026-11`, source only; CI has not run on it, so no run IDs are recorded. Implemented only, in RELEASE's terms.

## Amendments

The founder selected this from BACKLOG's investigation candidates on 2026-11-09: new share lines and Share links rows name the item's UUID as credential lines do, and older lines stay as they are and stay ambiguous. There is no STEPS row.

## What it does

`ShareService.CreateAsync` takes the UUID from the entry it read, keeps it on `ShareInfo.Item` and in the vault's share record, and writes it in `args.item`, which F.76's writer and reader already carry, so the schema and the chain do not change. A revoke takes it from that record, never from the path. Settings' Share links shows the item's username or URL after the path where another item has the name, or `item` and the UUID's first eight characters where neither differs, and `keypaste share ls --json` gains `item`.

Reversible local choices, settled here: the row's words where nothing tells a pair apart; `share ls`'s table is unchanged, though it can list a link the desktop made to one of a pair, whose `--json` then says which; a record's `item` that is not 32 hex digits, which only another program can write, reads as none, so it never reaches an audit line; the record stays `"v":1`, since a reader ignores the new member.

## Evidence

Locally, `verify.sh test` over `ShareServiceTests` and `ShareStoreTests` (46), `ShareVerbTests` (32) and `SharingViewModelTests` (20), and `verify.sh compile`. Of the 12 new cases, 6 failed before the fix, against the old code with only the empty `ShareInfo.Item` added so they compile: `TwoItemsOfOneName_EachShareAndRevokeLineNamesItsOwnItem`, `ALogMixingOlderShareLinesWithNewOnes_VerifiesAndOnlyTheNewOnesNameAnItem`, `Add_RemembersTheItemsUuid_AcrossAReopen`, `A_share_and_its_revoke_name_the_items_uuid_on_their_lines_and_its_listing`, `TwoItemsOfOneName_EachRowSaysWhichWasShared_AndItsRevokeLineNamesIt` and `TwoItemsWithNothingToTell_EachRowNamesItsUuid`. The mixed log's chain assertions hold on old code as well, since the verifier reads bytes; it failed on its item assertion. Six guard older records and passed on old code: the four cases of `List_ARecordNamingNoUsableItem_ReadsAsNamingNone`, `Revoke_OfARecordNamingNoItem_WritesALineNamingNone` and `ARecordNamingNoItem_OnAPairsPath_StaysThePathAlone`.

## Limits and follow-ups

- `keypaste log` and the Activity table do not show `item`, for share lines as for credential lines; the line in `audit.jsonl` holds it.
- A share line counts as a use on an item's row and Agent access card, as it already did by name, so a pair's row and card now show it, a revoke included.
- `keypaste share revoke --expired` forgets records and writes no audit line, as before.
