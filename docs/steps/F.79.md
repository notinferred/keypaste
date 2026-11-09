# F.79 — Let an agent ask the person to save a value

Completed 2026-11-08 on `f76-picker`, source only and not merged, with F.76 to F.78c. Runs on Linux at `5065f175`: ci fast tier 42539967548 and dev desktop 42539968187; dev desktop-gates 42538675376 at `a7cfad23`, which changed only the save window's accessible name since; all green.

## Amendments

The founder settled the row's open points on 2026-11-08: an agent may name an existing place or a new one, the item or the variable created when there is none; `keypaste approve` asks too, as the app does; and the tool is on for every agent, since each save is the person's and the agent never receives a value. PRODUCT's v1.23 draft is amended, dated, ahead of T7.

## Judgment calls

- **One tool, `request_save`**, taking `item` and `field`, or `project`, `environment` and `key`, with `reason`; the bridge and the owner both refuse a request naming both or neither, and a field other than `password`, a custom field or a passkey's.
- **The value goes through the terminal's edits** (`SetPassword`, `AddItem` with `SetFields`, `EnvSetKey`), so a save and a terminal's change decide alike; a new item for a custom field is created with an empty password.
- **The place is checked without the value** (`ValueSaves.Check`) to tell the person whether saving creates the item or replaces a value, and again before writing; a change in between, such as the field appearing, saves nothing.
- **What the agent can see decides**, on the item that will hold the value, a new project home by the tag it will get, as a request for the value there would be.
- **A refusal holds the same place back for a minute**, under its own cooldown key, `save`, the place and the field.
- **The app's prompt** has Cancel focused and Save armed a second after it is up and only once a value is entered, with Password and Passphrase buttons for the generator and a replace checkbox in a place kept on every prompt; the value is held in the forms' `SecretField` and disposed on the UI thread once the window comes down.
- **At `keypaste approve`**, `t` reads the value masked and `g` generates a 20-character password, which is not shown; `y` saves and anything else cancels.
- **The owner's line** is a `request_credential`-shaped line under the tool `request_save`, naming the place and field and never the value.
- **Prompt windows' words stay in the view**, as the other prompts' do, so `SaveValueWindow` joins their exemption in `LanguageAndRegionTests`.

## Evidence

Run locally, each class once: Keypaste.Core.Tests `ValueSaveRequestTests` (8, new), `ApproverProtocolTests` and `SessionAuthorityEditTests`; Keypaste.App.Tests `SaveValueViewModelTests` (3, new), `SaveValueWindowTests` (1, new), `LanguageAndRegionTests` and `PlainWordsTests`; Keypaste.Cli.Tests `TerminalApprovalChannelTests`, with the save case; the whole of Keypaste.Mcp.Tests, 170 passed, with `ServerToolsTests.A_save_says_where_it_was_saved_and_a_refusal_that_it_was_not` and the tool counts at four. `bash scripts/verify.sh compile` passed. `verify-mcp-stdio.sh` now expects four tools, and five with `--allow-run`.

In CI, two first attempts failed on checks the change had to meet, each fixed at its cause: ci 42537661413 at `b2a4e92b`, cancelled once integration failed, on `verify-mcp-stdio.sh`'s count of five tools with `--allow-run`; dev desktop 42538644566 at `a7cfad23` on `InteractionRulesTests.Every_secret_field_is_named_by_a_fixed_label`, the save window's value field now named from the resources.

## Limits and follow-ups

- **A save prompt is not listed on the Agents screen** while it waits, as a credential request's is; the gate's waiting list holds requests for values only.
- **The approvals guide** does not describe the save prompt yet: guides gain new capabilities through L.1.
- **A value the person saves is one the agent can then ask for**, decided by its category like any other (THREATS T-54).
