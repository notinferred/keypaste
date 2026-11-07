# F.76 — Let the person pick the item an agent asked for

Completed 2026-11-07 on `f76-picker`, built on F.77's unmerged `f77-once-or-always` at `8ebdebd0`, source only and not merged. Implemented only, in RELEASE's terms: no person has used this, and no package holds it.

## Amendments

The direction this batch was built under: the picker uses F.77's choices, Deny, Allow once and Always allow, and Always allow on it applies to the picked item; it keeps the prompt's rules, including that arming needs the window to be active; candidates are capped and scroll; the audit line names the picked item by UUID as well as name; and F.75's Agent access card, which read lines by name, names the item now.

## What it does

When more than one item an agent can see answers to its request, the owner puts a picker in front of the person instead of refusing: in the app a prompt window like the credential prompt with a list of the items, at `keypaste approve` a numbered list that takes the item's number and then asks `[d]`, `[o]` or `[a]` about that item. Each item shows its title, group, username and URL and the field asked for, its value drawn as a mask of one length, since nothing is read before the person allows it. Nothing is picked for them, focus starts on Deny, and Allow once and Always allow stay off until an item is picked and the prompt has armed. Exactly the picked item's field is read, by its UUID and only while it still has the name the person saw, and the owner's line names that item's UUID in `args.item` beside the path. Every credential line that reaches one item now carries `item`, and the ledger of releases does too, so a row and an Agent access card of an item another shares its name with show only what names its UUID, where F.75 showed `—` and a caveat.

Decided here, each a reversible local choice:

- **The picker still asks under Automatic and Automatic with commands**, and an Always allow row for one of the items releases nothing unasked: the agent did not name one item. Each candidate an agent is Automatic for offers no Always allow, saying "once only: it is Automatic for this agent" (a new `OnceOnly.Automatic`), since a row there would add nothing and could pass a limit.
- **A protected candidate offers Deny and Allow once only**: its Always allow is drawn but disabled, so the buttons do not move between picks, and the prompt says why under the list.
- **Only items the agent can see are offered.** None leaves the request refused as outside what it can see; exactly one is decided as a request naming it, under the agent's mode and rows, as any request one item answers to is.
- **The cap is nine items**, one digit at the terminal; more than nine the agent can see is refused as too many to pick from. The app's list shows two rows and part of a third and scrolls.
- **A run whose set holds a path two items answer to is refused, as before.** The picker answers credential requests only; BACKLOG holds the question.
- **Always allow on the picker writes the picked item's row as any Always allow does**, but while another item the agent can see shares the name, each request is put to the picker again, so the row answers only once that agent can see the item alone under it. BACKLOG holds whether the picker should offer it at all.
- **The picker's cooldown is the shared path's**, so a refusal holds the same request back whichever item would have been picked.
- **The line's reason says a person picked the item**, "a person picked this item and approved this one request", under `prompt`.
- **Lines written before F.76 name the path alone**, so on an item of a pair they are no longer shown at all, rather than shown as perhaps the other's.

`approvals.md` and the other guides state nothing the picker makes false, so they are unchanged. THREATS' settings paragraph and T-53 now describe the picker; BACKLOG's F.75 row on lines by name is narrowed to shares, and two follow-ups are added.

## Evidence

`ItemPickerTests` (Core, 8): through a real vault, `VaultCredentialSource` and `SessionAuthority`, a request two items answer to opens the picker with both and none picked and releases nothing on a refusal; the picked item's field alone is released for each item and the owner's line names its UUID; an allow with no pick and one naming no candidate release nothing; a lock while the person picks withdraws it and releases nothing; a request one item answers to is asked through the ordinary prompt and its line names its item; Automatic with commands and a row for one item still ask; Always allow on the picker writes the picked item's row and a protected pick offers none. `DesktopApprovalTests` adds `The_picker_releases_the_picked_items_field_alone` for each item, clicking the app's real window, and `A_picked_item_denied_or_locked_releases_nothing`. `ItemPickViewModelTests` holds that an armed picker allows nothing before a pick and that the pick kept is the one made as the person answered. `TerminalApprovalChannelTests` adds the numbered list, a number then an answer, anything but a number denying, and `[a]` only for an item that offers it. `EntryActivityRowsTests.TwoItemsOfOneName_ShowOnlyWhatIsRecordedForEachItem` replaces F.75's dash test. Frames `91k`, `91k2`, `91k3` and `91k4` in `ScreenRenderer`: nothing picked, an item picked, a protected item picked, and nine items scrolled.

Locally: those classes, with `ApprovalGateTests`, `ApproverHandlerTests`, `ApproverProtocolTests`, `AuditLogTests`, `EntryActivityTests`, `RequestRecordsTests`, `SameNameItemsTests`, `SessionAuthorityRecordsTests`, `SessionAuthorityTests`, `TagProtectionTests`, `VaultCredentialSourceTests`, `AlwaysAllowRecordTests` and `AssistiveTechnologyTests`, the `ScreenRenderer` at every theme and zoom, and `verify.sh compile`.

## Runs

Pending.

## Limits and follow-ups

- No screen reader has read the picker's list; each row's name is its title, group, username and URL.
- `docs/mcp-setup.md`'s sample line has no `item`, as a sample of the args a line may hold.
- In BACKLOG: a share's line naming the path, a run's set holding a shared path, and Always allow on the picker.
