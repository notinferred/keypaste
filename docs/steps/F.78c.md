# F.78c — Give cards and identities categories of their own

Completed 2026-11-08 on `f76-picker`, source only and not merged, with F.76 to F.78b. Runs: RUNS.

## Amendments

The row recognised a card or an identity by the field names keypaste's importers write, which F.78a's decision of 2026-11-08 that nothing is decided by a field's name questioned, as BACKLOG asked. The founder decided on 2026-11-08 to recognise them by a tag instead: an item tagged `Credit Card` is a card and one tagged `Identity` an identity, the tags 1Password's 1PUX import already writes, and keypaste's Bitwarden import now writes them too. The BACKLOG question is answered and dropped.

## Judgment calls

- **Only protected custom fields** of a tagged item are card or identity values, the row's "secret fields"; its plain fields, such as a card's brand, stay custom fields.
- **A project tag decides first**: a tagged card in a project keeps its fields project variables, so F.78a's rule for projects is unchanged.
- **The tags match in any case**, so `identity` counts; any other spelling, and an untagged card, releases nothing more than a protected custom field.
- **The keys** in `agents.toml` and `keypaste clients --set` are `card` and `identity`, and the categories read as "card values" and "identity values"; Settings and an agent's page list them after project variables.
- **A section of `agents.toml` may hold** as many keys as there are categories and two, since `[[rules]]` and `[[agent]]` now hold eight categories.
- **Whether a field is protected** is read without its value (`Vault.Fields`), so deciding a category still reads no value.

## Evidence

Run locally, each class once: Keypaste.Core.Tests `FieldCategoryRequestTests`, with the card and identity among each category's Ask, Allow and Block theories and a new case for a card's plain field, a hand-made untagged card and a project tag deciding first, `BitwardenJsonReaderTests`, with the import's tags, `AgentRulesFileTests`, `RememberedAnswerRequestTests`, `SealedAgentRulesTests`, `ApproverHandlerTests` and `ItemPickerTests`, 155 passed; Keypaste.App.Tests `AgentRulesViewModelTests`, `ScreenRenderer`, `LanguageAndRegionTests`, `PlainWordsTests` and `InterfaceZoomTests`, and Keypaste.Cli.Tests `ClientsVerbTests`, 122 passed. `bash scripts/verify.sh compile` passed.

## Limits and follow-ups

- **A card or identity imported from Bitwarden before this change** carries no tag, so its fields stay protected custom fields until the person tags it.
- **Only the Bitwarden and 1Password 1PUX imports** tag cards and identities; the CSV imports carry no item type to read.
