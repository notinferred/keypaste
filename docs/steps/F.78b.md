# F.78b — Let an agent's page and a prompt's answer override a category

Completed 2026-11-08 on `f76-picker`, source only and not merged, with F.76 to F.78a. Runs on Linux: ci fast tier 42495720558 and dev desktop 42496395328 at `40307c75`, dev desktop-gates 42494368704 at `138521c7`, all green.

## Amendments

The founder selected the row on 2026-11-08 and settled three of its open points before the build: an always is confirmed in the prompt itself, as a second step of the same window; `keypaste approve` offers the same choices, `[a]` and `[b]` each confirmed with `y`; and the picker's Always allow keeps the pick and releases that item's field without asking, as an item's Always allow would.

The founder's direction of 2026-11-08, after the build: `keypaste clients` lists each agent's own rules and kept answers and sets and forgets them, Allow staying in the app, since a `y` typed in the asking terminal proves no person agreed. It sets an agent's rule with `--set <value>=ask|block|settings` and forgets an Always allow or a pick with `--forget <key>`, the key twelve hex characters of a hash of the answer, shown by the list. Both go to the owner as two new rules changes, `set` and `forget`, with a target of at most 64 characters; the owner makes only what releases nothing more unasked, refusing `settings` where Settings allows what the agent's own rule did not, and the removal of an Always block, which the app's agent page makes. With nothing holding the vault the change is applied to the file and written unsealed, as `--run off` is.

## Judgment calls

- **Only a labelled agent has answers of its own.** A bridge with no `--client-label` cannot be told from another, so it is decided by Settings alone and its prompt offers Deny and Allow once.
- **Kept by the item's UUID and the field** (`attachment:<name>` for an attachment), so a rename keeps the answer and another item of the same name gets none. A pick is kept by the name exactly as the agent gave it and the field.
- **An item's answer outranks the agent's and Settings' Block**, as the row's order says; only a passkey, Blocked at every level, and the item's own Always block stop it. A remembered pick follows the same rule and is passed over when its item is blocked or out of reach, so the picker asks again.
- **A run is decided for the agent that asks**: a variable the agent's rule or an item's Always block blocks refuses the run, so a run is no way around either.
- **At most 128 kept answers and picks** (`AgentRules.MaximumRemembered`), within `agents.toml`'s own limits; once full, prompts stop offering an always and say why.
- **An always that cannot be kept**, such as a seal that cannot be written, still releases once or refuses as a plain Deny would, cooling the field down, and its line says why.
- **The picker offers no Always block**: a block names one item, and the person has not said which.
- **At the terminal any answer but `y` to the confirmation is a refusal**, since a line-based prompt has no Back.
- **The seal's text is unchanged** (`keypaste agents.toml 2`): a file with none of the new sections renders exactly as before, so F.78a's sealed files hold.
- **The published transcripts** in README, launch, demo, keepass-and-agents and keypaste.com's index ended on `[d] deny  [o] once`, which a labelled agent no longer sees; they now show `[a] always  [b] block`, and `verify-demo.sh` checks that prefix in place of forbidding it.
- **BACKLOG's question about the picker's Always allow row** is dropped: a kept pick now answers while another item shares the name.

## Evidence

Run locally, each class once: Keypaste.Core.Tests `RememberedAnswerRequestTests` (13, new), `AgentRulesFileTests`, `SealedAgentRulesTests`, `FieldCategoryRequestTests`, `ItemPickerTests`, `ApprovalGateTests` and `ApproverHandlerTests`, 150 passed; Keypaste.App.Tests `AlwaysPromptWindowTests` (3, new), `DesktopApprovalTests`, `ItemPickViewModelTests`, `AgentActivityViewTests`, `AgentsFirstLevelTests`, `AssistiveTechnologyTests`, `InterfaceZoomTests`, `PlainWordsTests`, `OneHomeJourneyTests` and `ScreenRenderer`, passed; Keypaste.Cli.Tests `TerminalApprovalChannelTests`, with the always keys' cases, passed. `bash scripts/verify.sh compile` passed.

In CI, two first attempts failed on rules the change had to meet, each fixed at its cause: ci 42493543438 at `6d60d44e`, cancelled once its test job failed, failed `ApprovalPromptTests.ThePromptHasNoMember_AReasonCouldUseToChangeTheDefaultOrTheDeadline`, which now names `OffersAlways`, set from the label and the room left, never the request's text; dev desktop 42494338092 at `138521c7` failed `LanguageAndRegionTests.Every_word_a_view_shows_comes_from_the_resources` on the agent page's four new labels, now in `Strings.resx`. The desktop-gates run at `138521c7` also ran F.78a's edits to `verify-desktop-approval.sh`; compat, which runs `verify-keepassxc-fields.sh`, has not run since `b0d150bf`.

For `keypaste clients`, run locally: `RememberedAnswerRequestTests`, `SealedAgentRulesTests`, `ApproverProtocolTests`, `ClientsVerbTests` and `TerminalApprovalChannelTests`, 192 passed, with new cases for a terminal narrowing and never widening an agent's rules, forgetting by key and never an Always block, the owner making a set and a forget and refusing a widening, the change's target round-tripping and refused when missing or too long, and the list naming each kept answer by its key; the whole of Keypaste.Cli.Tests, 951 passed. ci's fast tier 42518307133 passed at the change; its first attempt, 42517865285 at `3b88074a`, failed `CliAppTests`, which pin the help text, on the verb's new summary, now shortened and pinned. The App's change in it is one sanitizer call, so no desktop profile ran again.

New request-path tests: an item's answer, then the agent's rule, then Settings deciding the same request in turn; another label and an unlabelled bridge decided apart from them; Always allow and Always block confirmed on the prompt releasing and refusing then and unasked after; an always from a prompt that offered none, and each change without confirmation, keeping nothing; Remove asking again; a passkey Blocked under a kept Allow; the picker's kept pick answering that agent alone, and passed over for an item it always blocks; a lock withdrawing an always mid-prompt and releasing nothing under a kept one; a run refused a variable its agent blocks; a sealed always holding and a hand-written one ignored.

## Limits and follow-ups

- **A label is the operator's word**: whatever starts a bridge under an agent's label gets that agent's Allows and always answers (THREATS T-14, T-36, updated).
- **PRODUCT's v1.23 draft** is amended, by the founder's direction of 2026-11-08, to name the always answers, an agent's own rules and their order; it stays a draft until the founder's review.
- **`docs/demo/keypaste-demo.gif`** shows the older choice line, and README's prompt screenshot is older still (its alt text names Allow for 1 hour); BACKLOG's smaller possibilities hold regenerating them.
- **The agent's page names a kept item** by its current name in the open vault, or as "an item no longer in this vault" when none has that UUID.
