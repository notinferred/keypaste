# F.75 — Open two items that share a name

Completed 2026-11-07 on `f75-same-name-items` at `af9cfdff`, source only; ci fast tier 42388490688, dev desktop 42389901512 and dev desktop-gates 42389150711 green there, with ci 42386090652 and app 42386098084 on every runner green at `1d00bd91` before the review's repairs. Dev desktop 42383319821 failed `FourPlacesJourneyTests.Every_act_is_reached_from_the_four_places` at the first commit, repaired by `1d00bd91`. Implemented only, in RELEASE's terms: no person has used this, and no package holds it.

## Amendments

The founder's direction with the batch adds that `CredentialFailure.Ambiguous` stays refused until F.76 and exposure matching does not change; both hold. The row names the agent-access card as reaching its entry by UUID, and it does not fully: its grants and audit lines are by name, because a line names a path, so on one of a pair the card says what it shows may be the other item's. Whether a line should name the item is the founder's, in BACKLOG.

## What it does

`EntryName` gained `Uuid`, which two names ignore when compared. With one, `KeePassInterop.Locate` finds that item alone, wherever it is now and whatever else shares its name, outside the bin and keypaste's own groups, and a file holding the UUID twice reaches a holder only when its name agrees too; without one, a name two items answer to is refused as before. Every `Vault` operation goes through `Locate`, so the pane's copy, reveal, history, edit, rotate, fields, tags, attachments and share, a rename, a move, a delete and a duplicate each reach the item they opened. `EntryRow`, the pane and Recommendations' findings set it; `EntryName.Of`, `ReadSavedListing` and `EntryMatch.Name` do not, so the agent path, every terminal verb and a typed name are unchanged, and `Keypaste.Cli`, the bridge and the relay cannot name the property (`BannedSymbols`).

The list compares rows, its selection, what a search matched and what an edit holds in the list by UUID. A row sharing its group and title shows what tells it apart, its username or else its URL, after the title while the window has dropped those columns, and the delete notice and question, the share dialog and Recommendations' rows name `services/Mail (alice)`. Where neither differs the pane says so and offers Rename or move…, the question says the open item, and the share dialog says the same. A row of a pair shows `—` for Last used, with the reason in a tooltip, and its Agent access card says what is recorded may be the other's and that an agent asking for the title is refused. KDBX is unchanged.

Reversible local choices, settled here:

- **The username after the title shows only under 840 wide.** Above it the Username column beside the title already says it, so writing it twice would break each fact once; the row still reads title then username at every width.
- **keypaste still makes no pair.** `RestoreRecycled` and `Relocate` refuse a name another item has, for the reason their comments give: a pair is denied to agents and takes a project's variable out of `keypaste run`. So a restore beside the other item gets a free title: Trash's existing Restore as, and Edit › Undo of a delete restoring as `Mail (restored)` and saying so. Undo of a rename that ended a pair is not offered, since core would refuse it. If the founder wants a restore to rebuild the pair, it is the one line in each and a decision about agents.
- **A `kp://` reference is not offered on one of a pair**, as it resolves to neither.
- **A key in notes renamed since the check still goes round again.** `A_renamed_entry_is_refused_as_gone` holds this; the UUID could have moved it, under a name the finding no longer has.
- **Rows of a pair sort by username, then UUID**, so they keep one order whichever way the column runs.
- **A tell is the username only this item of its name has, or else the URL only it has.** Nothing else is shown, since notes and custom fields may hold a value.
- **Username and URL cells carry the whole value in a tooltip**, since two long usernames can cut short to the same text.
- **A row of a pair hides its last-used time and its in-use dot**, rather than claim either for one item; the pane's card keeps them, hedged.
- **A file holding one UUID twice**, which only another program can make, is reached by UUID and name together, since name alone reached each on main; two holders of one name are refused, and a share of a holder whose name has changed since the dialog opened is refused.

## Evidence

`SameNameItemsTests` in `Keypaste.App.Tests`, each over both items of a pair and reading the file back: open, reveal, copy, a TOTP code, edit, rotate, fields and tags, an attachment, history's reveal and restore, rename, a drag, delete with Undo, Trash restoring each, no-Trash delete's question, duplicate, search, sort, a recommendation opening its item and naming it, dismissing one and moving the other's key, the share dialog, a pair with no username or one username, and a file holding one UUID twice. `SameNameRowTests` draws the tell at 720 and its absence at 1000, and the tooltips. `EntryActivityRowsTests.TwoItemsOfOneName_ClaimNoUseOnTheirRows_AndTheirPanesSayItMayBeTheOthers` holds the last-used and card behaviour. `verify-keepassxc-workflows.sh` has the app edit each of a pair KeePassXC made, and KeePassXC reads both back; the fast tier and the two dev profiles do not run it, so its first run is `main`'s. `SameNameItemsTests` in `Keypaste.Core.Tests`: a bare name refused for readers and writers, what an agent or a terminal reads carrying no UUID, a UUID finding its item after a rename and a move, not in the bin. `ShareServiceTests.Create_OneOfTwoItemsOfOneName_SharesThatOneByItsUuidAndRefusesTheBareName`. `VaultCredentialSourceTests` still holds `CredentialFailure.Ambiguous`. Two tests that held the old refusal were replaced: `Deleting_a_name_two_entries_answer_to_is_refused_and_says_so` and `A_name_two_entries_answer_to_is_refused_rather_than_shown_empty`.

Frames `12a`, `12b` and `12c` (a pair a username tells apart: list, 720, delete), `12d` (the access card), `12e`, `12f` and `12g` (a pair nothing tells apart: pane, 720, delete), `12h` (Recommendations) and `12i` (Trash) in `ScreenRenderer`.

## Limits and follow-ups

- The Agent access card and the row's last-used time read audit lines and grants by name, because lines name a path, so both of a pair show what is recorded under their name. Each line stays as it is; F.76's owner's log naming the picked item is where this could change. In BACKLOG.
- A share's audit line and its row under Share links name the path both items have. The sealed value is the chosen item's. In BACKLOG.
- A project's item picker lists both of a pair and adding either is refused with core's sentence, and Settings' Share links list leaves a pair out; both fail closed. A project's member row carries a bare name, so removing a pair's row reports core's "names 2 items" sentence and its keys draw blank; no wrong item is reached. In BACKLOG.
- Trash's rows have no username, by `RecycledEntry`'s rule, so two rows of one title differ only by when they were deleted, and two deleted in one minute read alike; each restores as itself.
- Two items sharing a title and with neither a username nor a URL of their own read alike everywhere but the pane's line, until one is renamed. That is why a pane says so, and why the no-Trash question says the open item.
- Recommendations' rows name a pair by the same tell, and with nothing to tell read alike; each opens and moves its own item.
- A file holding one UUID twice: Recommendations opens neither holder, saying it is no longer in the vault, since a finding opens by UUID alone.
