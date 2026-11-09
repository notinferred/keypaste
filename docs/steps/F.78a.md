# F.78a — Decide an agent's access by field category

Completed 2026-11-08 on `f76-picker`, source only and not merged: the founder reviews PRODUCT's v1.23 draft, THREATS and every site change first. Runs: ci 42478773405 at `45b600aa` (phases 1 and 2a); at `81f9a014`, ci's fast tier 42484932459, dev desktop 42484941647 and dev desktop-gates 42484987806 on Linux, all green.

## Amendments

The founder selected the STEPS row on 2026-11-07 and amended it during the build.

Phase 2a, by the founder's direction: `agents.toml` is sealed with an HMAC-SHA256 under a 32-byte key the vault keeps in `.keypaste/keys` (record `agent-rules`, field `key`), over this home's random identity in `agents.id` and the canonical rules. A missing or wrong seal, no key or no home identity gives the defaults and leaves the file as it is, with the reason "agent rules were changed outside keypaste and are ignored until you set them again". `keypaste clients` with nothing holding the vault cannot turn run on; turning it off or removing an agent writes the file unsealed, which reads as the defaults.

Phase 2b, the founder's decisions of 2026-11-08, applied as written:

1. An agent sees each item's title, group, tags and URL, and its username unless the Settings switch hides it; the switch hides the username alone and listing works either way. Every custom field, protected or not, is a value decided by its rule, Ask by default.
2. No decision by a field's name: a custom field of an item tagged `env:<project>[:<environment>]` is a project variable and any other is a custom field; a passkey's `KPEX_PASSKEY_*` fields are always Blocked. Beyond the KeePass standard fields and the passkey prefix, nothing in the classification reads a name (see Limits for request eligibility).
3. A rule change clears the refusal cooldown for the fields whose rule it moved.
4. An agent can request one attachment of an item by name, decided by the attachments rule, at most 5 MiB, released and logged like a field and refused while locked.
5. Settings › What agents get holds the username switch, each category's Ask, Allow and Block, the Allow confirmation and the notice that the rules were changed outside keypaste; an agent's page keeps its Run switch; the prompt windows' layout is tidied now that the Always row is gone.
6. The documents listed in the direction, and BACKLOG's question about F.78c.

The seal's counter, added to phase 2b: the vault's `agent-rules` record also holds `count`, which the owner increments and saves in the vault before writing the file on each sealed write, and the MAC covers it with the home identity and the rules. A file whose count is not the vault's current value reads as the defaults with the same reason, so an older sealed file put back is ignored, and a write whose file never lands leaves the defaults.

The founder's fixes of 2026-11-08, after phase 2b:

- **A count per home.** Each home's count is a record of its own in `.keypaste/keys`, titled `agent-rules-<home identity>` with a UUID derived from that title, holding `count`; the key stays alone in `agent-rules`. The seal covers this home's count, so a put-back older file is still refused and another home's change leaves this home's rules as they are. The founder asked for the counts in the same record, or a field per home if one field could not merge safely; neither can, because a merge, keypaste's as KeePass's and KeePassXC's, takes an entry changed in both copies as the newer whole with the older in its history, so two homes' fields in one entry would lose one of them. A record per home is only ever changed by its own home, so a merge keeps each.
- **Any custom field can be requested.** `request_credential` takes any custom field of an item the agent can name, up to 128 characters with no control character, decided by its category (`CredentialFields.IsAgentCustom`); the listing names them and a tag's reach covers them. A standard name in another case and a field a TOTP seed is read from stay refused unasked, passkey fields stay Blocked, and reserved groups stay unreachable. A field's name is scrubbed like a title in the prompt and the audit line. `kp://` references and `keypaste share --field` keep the env-named rule, which projects need.
- **Runs for these fixes:** ci's fast tier 42487930333 failed on an unquoted apostrophe in three gates' expected refusal and two Core tests asserting the old name rule; 42488575445 green at `b0d150bf`. No App code changed, so no desktop profile ran; `verify-desktop-approval.sh` and `verify-keepassxc-fields.sh` changed with the refusal's wording and run in the desktop-gates and compat jobs.
- **DESIGN.md**: the voice's "Always allow" now reads as what ships, Deny and Allow once; the `clients.toml` vocabulary row is gone, and the "what it can see" row names `--expose`'s patterns, since agents' Can see settings went too.

## Judgment calls

- **`request_attachment` is a second instance of the credential tool**, taking `entry`, `name` and `reason` and returning the bytes as base64 in the text and in structured content (`attachment`, `base64`). Its lines are `request_credential` lines with field `attachment` and the attachment's scrubbed name at the end of the reason, so the log's schema is unchanged.
- **An attachment reply may fill 8 MiB** (`ApproverProtocol.MaximumAttachmentReplyFrameBytes`), as an env set's may: only the bridge that asked reads it, which reads under that bound only for an attachment request, and every frame the owner reads stays at 64 KiB. 5 MiB as base64 fits with room for the rest of the reply.
- **The cap is checked on read**, so an attachment over 5 MiB that another program wrote is refused as too large.
- **A picked item's attachment is read by its UUID**, so two items sharing the name never make the read guess.
- **The cooldown key of an attachment** is the item and `attachment:<name>`, so refusing one attachment holds back only that one.
- **The Allow confirmation says "Every agent will receive every <category> value without asking."**, since the rules are the same for every agent, with "Each release is recorded in the log. Set Ask here to be asked again." under it; its button says what it does, such as Allow passwords. It is a secondary button, so Settings keeps no amber of its own.
- **The `custom` key** replaces `protected` in `agents.toml`, and `username = "shown"|"hidden"` replaces `details`.

## Evidence

Run locally, each class once: Keypaste.Core.Tests `FieldCategoryRequestTests`, `SealedAgentRulesTests`, `AgentRulesFileTests` and `CustomFieldReleaseTests`, 86 passed; `ApproverHandlerTests`, `ApproverProtocolTests`, `ItemPickerTests`, `ApprovalGateTests`, `ApproverListenerTests`, `ApproverClientTests`, `VaultCredentialSourceTests`, `FieldCategoryRequestTests` and `SealedAgentRulesTests`, 252 passed; Keypaste.Mcp.Tests `AttachmentToolTests`, `ServerToolsTests`, `PlainWordsTests` and `LargeCredentialTests`, passed; Keypaste.App.Tests `AgentRulesViewModelTests`, 4 passed; `ScreenRenderer` with the new frames `04-settings-agent-rules`, `-allow` and `-ignored` at every palette and at 150% and 200%, passed once the ignored notice wrapped. `bash scripts/verify.sh compile` passed.

New request-path tests: each category's Ask, Allow and Block with a protected and an unprotected custom field; the username switch and the URL under every rule; listing with usernames hidden; a rule change clearing only the cooldown it covers; attachments asked, allowed and blocked, a missing name, over and at 5 MiB, a lock, and the picker when two items answer to the name; the older sealed file put back; and over a real pipe and MCP client, a 1 MiB attachment released as base64 and logged once.

In CI, phase 2b took three fast-tier attempts and two of each Linux profile, each failure an assertion of the old behaviour, fixed at its cause: `verify-mcp-stdio.sh` counted two tools (42482332165); the field rule's wording had gained `attachment`, which `request_credential` does not take, so it was put back, and `RunToolTests` listed the tools without `request_attachment` (42482571324); `verify-connect-client.sh` expected the defaults' tagged-only listing that the removed Can see gave (42483550437); and three App tests covered removed settings: a limit's end time in the log, the Can see Learn more, and the agent card hidden from an agent with no settings (42483522379).

## Limits and follow-ups

- **A protected environment no longer holds back an Allow** on an agent's requests: its values are decided by their category like any other, as the row says ("Production is decided by these rules like any other field"); it still holds back a scoped token's run.
- **A home's first seal against two copies at once**: two copies that each create the shared key before either is merged settle to one key, so the other home's rules read as the defaults until set again. The key is made once per vault, so this needs two first seals racing.
- **Whatever holds the unlocked vault can seal any rules**; the seal authenticates the owner process, not the person (THREATS T-15).
- **An attachment is held whole** in the owner, the pipe reply, the bridge and the MCP result; BACKLOG asks whether chunking is worth it.
- **The fixes' edits to `verify-desktop-approval.sh` and `verify-keepassxc-fields.sh`** have not run in CI: desktop-gates and compat were not run after `b0d150bf`.
- **The ACAF v1 and v1.1 pages** still describe keypaste's former ceilings inside the format's text, kept as written under their new status.
