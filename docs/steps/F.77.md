# F.77 — Once or Always

Completed 2026-11-07 on `f77-once-or-always`, source only and not merged: the founder reviews PRODUCT's re-ratification, the ACAF v1.2 page and every site change first.

## Amendments

The founder selected this on 2026-11-07, with no STEPS row. Their words: "Remove the allow for 1h option", "always or once", "Security prompt should have an always allow", "Everywhere; commands get Always too".

The founder decided on 2026-11-07, after review:

- Items keep Always allow, per agent and item, the item's live TOTP codes included, as built: this is the founder's choice. A row has no end time.
- Commands lose per-command Always allow entirely: a run's prompt, the agent's `run` tool's and `keypaste run --session`'s, offers Deny and Allow once only. The command rows are removed as code: the `commands` member of `always`, the command identity and `RunProgram.FinalTarget`, their rows in Agents, `--forget` for a command, their confirmation and strings.
- A third agent mode, in the founder's words "an agent can get bypass permissions or auto mode like claude code's options": it releases what the agent can see as Automatic does and also runs its commands without asking. Protected environments are asked every time with Allow once only in every mode, a command's set included; it needs May run commands on, which the pane says rather than hiding the choice; Automatic's end time and hourly limit apply to it and a reached limit asks with Deny and Allow once; choosing it is a widening the access-change confirmation must allow, saying the agent can then run commands with these values without asking and that keypaste approves nothing about what those commands do (T-35); an agent's own `request_access_change` may ask for it; its releases and runs are their own audit kind. It is a scope change to PRODUCT's commands law, in the v1.23 draft.
- Restoring one of two same-named items from Trash restores it under its own name, and Trash rows of such a pair show their group (F.75's follow-ups). The first is done. The second is not: Trash rows already show their group at every width, and a pair as F.75 defines it shares its group and title, so both rows read the same; telling them apart needs the founder's choice of the item's own username or URL (which `RecycledEntry` does not carry, as it carries no field value), the deletion time to the second, or leaving it.
- DESIGN-PRINCIPLES' "every grant names its duration" becomes "every allow says how long it lasts".
- The terminal's list names an item by its UUID, not its name: a vault's item names are not sent over the pipe to `keypaste clients`, which any program of the person's can run.

## What it does

An item's prompt, in the app and at `keypaste approve`, offers Deny, Allow once and Always allow. Always allow allows the request once and opens the access-change confirmation, answered before the value leaves; only once the person confirms does anything beyond that release hold. For an entry in one project environment it keeps its wider scope, making that row Automatic. For any other it writes a row, by the item's UUID, for that agent. A run's prompt offers Deny and Allow once. Protected items stay asked every time, a tag added after the row included. An agent's settings on Agents list what is always allowed, project rows set to Automatic included, each with a Remove. Each release under a row is logged as `always-allowed`, never `prompt`.

Modes are Ask every time, Automatic, and Automatic with commands, which also starts the agent's runs unasked when it may run commands and every item of the run's set is Automatic with commands for it, none protected, no limit reached and no refusal cooling; its releases and runs are logged as `automatic-commands`. The app's mode control, `keypaste clients --mode`, the pipe and `request_access_change` take it, and its confirmation, in the app and at `keypaste approve`, says "<agent> can now run commands with the values it can see, without asking. keypaste approves nothing about what those commands do."

A restore from Trash, of one of a same-named pair or onto a name KeePassXC took meanwhile, comes back under its own title beside the other, so Restore as and its `(restored)` titles are gone.

Timed allowing is gone as code: `Approved`, `GrantCache`, `EnvGrantCache`, `GrantId`, `--max-ttl`, `ttl_seconds`, the Allowances list, `[h]`, `keypaste grants` and its pipe messages, and the timed mode. A stored `allow-for-a-while` reads as Ask every time, a change rewrites it to `ask`, and the CLI, the pipe and the tool refuse it as a new setting. The default mode is ask.

Storage is a new optional member, `always`, holding `items`, in the agent's `acaf` record (`AlwaysAllowed.cs`, `AcafRecord.cs`), so lock, merge, history and a KeePassXC round trip carry it as they carry the rest, and a new mode word, `automatic-commands`. [ACAF v1.2](../../site/web/src/content/docs/acaf/v1.2.mdx) specifies both, with its own schema at `site/public/acaf/v1.2/`.

## Judgment calls

- **The third mode is named "Automatic with commands"**, beside "Ask every time" and "Automatic", so the difference reads in its name; its word in ACAF, `clients.toml` and the CLI is `automatic-commands`, ordered `ask` < `automatic` < `automatic-commands` for the least-of rule, and its audit method is `automatic-commands` for its releases and its runs alike.
- **A v1.1 reader meeting `automatic-commands`** reads `mode` as `ask`, or a project row holding it as no rows, as v1.1 says of a mode or row it does not understand, and applies that record beneath the defaults, so the word never gives that reader more. keypaste's own reader fails closed the same way on any mode word it does not have.
- **A run spends one release of each limit its items fall under**, not one per value, as one release of a set; a run's set with no items takes the agent's own mode.
- **A project row is offered Automatic with commands only where it already holds it**; the agent's own mode always is. The CLI and the pipe accept it for a row.
- **With nothing holding the vault, only `--mode ask` applies as a lowering.** Before, the one other mode was the top one; now Automatic can be wider than what the vault gives, which this computer cannot read.
- **A `clients.toml` ceiling written as `automatic`** now holds that agent beneath Automatic with commands; no ceiling at all is the new top.
- **The mode control wraps** where the pane is too narrow for three segments on one line, as at 150% zoom, rather than cutting "Automatic with commands"; the other segmented controls keep one line.
- **The confirmation's commands sentence is said wherever the change lets the agent run commands unasked**: setting the mode with May run commands on, or turning May run commands on under the mode. Setting the mode with it off says only what Automatic says, and the pane says it runs nothing until it is on.
- **The run prompt says why it asks** when Automatic with commands would otherwise have started it: a protected environment, or a reached limit.
- **Restore as is removed**, since a restore now keeps its title in every case it offered one; `verify-keepassxc-workflows.sh` and `verify-keepassxc-recyclebin.sh` now expect the pair, and neither runs before `main`.
- **A reached Automatic limit offers Deny and Allow once only**, as the founder's task says, since an Always there would bypass a limit the person set. An existing row does not answer such a request either.
- **`always` is a member of the record, not a second field of the entry.** A second field would leave a 1.1 reader's reading whole, but needs its own read, write, undo, duplicate handling and saved-state comparison in the store. A member needs none, and a 1.1 reader tolerates it as its text requires: it keeps it and applies that record only beneath the defaults, which takes away and never widens. No published keypaste reads ACAF, so that reader is so far theoretical. The founder may prefer the field.
- **Keeping `"acaf": 1`.** A value other than `1` makes a 1.1 writer refuse to rewrite the record.
- **An item's row covers every field the agent can see of it**, as the founder's "this agent and this item" reads; its confirmation says so. Reach, values and the cooldown still come first.
- **A bridge with no `--client-label` is offered no Always allow**, so an unnamed agent's prompt has two buttons: its settings have nowhere to keep one.
- **Rows are read as the least of every record that bears on an agent**, so a record not wholly understood, or read while the vault differs from its file, has no row.
- **A ceiling in `clients.toml` holds no row.** A removal can be undone by merging an older copy of the vault, as THREATS T-12 says; BACKLOG holds it as an investigation candidate.
- **`keypaste clients <agent> --forget <id>`** removes an item's row, so a terminal owner has the undo the app has, and `keypaste approve` names on start each agent with rows. `--forget` with nothing holding the vault is refused, as is one naming no row or one the vault did not save. A whole agent's `--reset` the vault did not save lowers this computer's ceilings and says what is always allowed still releases.
- **An undeliverable release now cools down**, as an unfillable one does: without a grant to store, each retry would otherwise ask again for a value that cannot be sent.
- **Allow once is the prompt's one amber.** The rule is one amber per view, and it is the common answer; Always allow stays neutral.
- **Scoped-token and terminal-edit prompts are unchanged**, and browser fill's words too.
- **The confirmation is answered before the value leaves, and this differs from the founder's order.** The direction says "releases once, then opens". Here the release is authorized once, the confirmation is asked in the same exchange, and the value leaves after it, so a lock during the question refuses the release and writes no row. The agent waits up to one more window, and a refused or expired confirmation still lets the one release go. The founder may want it the other way round, which would let a value out before a lock could stop it.
- **A record too large for one reply of `keypaste clients`** is sent first without its project rows and then without its always-allowed rows, the verb saying how many of each it left out and that those rows still release. The app's list always shows them.
- **An agent's own tool cannot add or remove a row.** Only its person changes what is always allowed; a row comes only from a prompt, and a removal only from the app or a terminal.
- **Agents' list repeats project rows set to Automatic** that More's Project rows also shows: Always allow's widest result has to appear where the person looks for it. Their limits stay editable only under More.
- **`keypaste log`'s `(!)` marks an `always-allowed` line** whose reason differs from the last one a person read for that client, item and field, as it marked `grant-cache` lines, which still read as before.

## Published and specification wording changed

These await the founder's review:

- PRODUCT law 2, "Each release contains one credential with one scope and TTL, following explicit human approval or a pre-approved policy the human wrote." now has no TTL and names a confirmed Always allow row as such a policy. §3's "clear reusable grants" became "stop every release, what the person always allowed included". The 2026-09-25 amendment's "or a grant of at most 15 minutes for that command line" became once each time, or the agent set to Automatic with commands, confirmed, approving nothing about what those commands do and never starting one with a protected value unasked. v1.23 is written as a dated draft above the last ratified line, naming Automatic with commands as the founder's direction and a scope change to the commands law, with ROADMAP's version sentence.
- ACAF: v1 and v1.1 are unchanged. v1.2 adds `always` with `items` only, a row having no end time, and the mode `automatic-commands`, which alone starts a command unasked, bounded by the limits and never for a protected entry; it says what a 1.1 reader does on meeting the word, makes moving to it a confirmed widening, and replaces the command row's security considerations with one for the mode. The `mode` default is `ask`. Status is Implemented, on this branch.
- THREATS T-12, T-14, T-35, T-41, T-34's `run --session` and the bridge summary; SECURITY's release and standing-setting paragraphs.
- `docs/demo/keypaste-demo.gif` and `site/public/demo/keypaste-demo.gif` are re-recorded from this source with `capture-demo.py` and `render-gif.py`, on macOS with the capture's temporary home written as `/home/you` in the tape, and their captions lose "before its prompt offered Always for". `site/public/index.html`'s "with a time limit on reusing that approval. Expiry does not erase" became "That does not erase". `how-it-works`'s "one scope and a time limit" became "one scope", and its Always allow sentence no longer covers a command and names Automatic with commands.
- `site/public/index.html`: "once or for an hour" became "once or always" three times, and the choice line in its transcript. `site/web`: agent-approvals, agents-without-values, desktop-app, how-it-works and its security page, and the navigation's ACAF link.
- README's, launch's, demo's and keepass-and-agents' transcripts and sentences about source; README's and launch's sentences on commands now name Automatic with commands. Statements of `v0.3.0` stand.
- DESIGN's voice example and segmented control example; the design skill's "gives every grant a duration"; DESIGN-PRINCIPLES' "every allow says how long it lasts".

`docs/screenshots/approval-prompt.png` and `agents.png` are not generated by a script, so they are unchanged and stale, and README's alt text stays with them.

## Runs

At `80b25b8f`: ci 42419330456 (fast tier) green; dev desktop 42419346481 and dev desktop-gates 42419346500 green on Linux. Locally: the touched Core, CLI, MCP and App classes, `verify-mcp-run.sh` against the Release CLI, the ScreenRenderer at every theme and zoom, and `verify.sh compile`.

## Security review

A read-only review of Automatic with commands found these, each checked against the source and fixed at its cause:

- **A reset of this computer's limits said nothing of commands.** `AccessChangePrompt.ForReset` now marks each agent whose vault record is Automatic with commands with May run commands on, and the confirmation, in the app and at `keypaste approve`, names them in the commands sentence.
- **A limit that allows more said nothing of commands.** Renewing a passed end, `--until none` or raising `--per-hour` on an agent already in Automatic with commands with May run commands on brings its unasked commands back, so it now says the sentence too; under plain Automatic it does not.
- **A run under plain Automatic with a passed end said its limit was reached**, though plain Automatic never starts a run. `ItemAccess.ModeBeforeEnd` keeps the mode a passed end held back, and the run prompt gives the limit as its reason only where that mode was Automatic with commands.
- **ACAF v1.2's schema required `items`** where the reader and writer treat `"always":{}` as holding no row; the schema and the page now match the reader.
- **Stale comments** in `SessionAuthority` (Always allow on a command line, "No mode releases a run") and `OutputScrubber` now say what the code does.
- **PRODUCT's 2026-09-25 amendment** said "This is not arbitrary shell execution: no shell starts it"; the v1.23 draft now says keypaste starts the program named, never a shell of its own, and that Automatic with commands starts any command the agent names, `sh -c` included. The paragraph above it, "MCP remains bounded credential access: exposed names and one approved field, not arbitrary shell execution", is unchanged and awaits the founder.

Tests added: `SessionAuthorityRunTests.AProjectRow_StartsItsOwnItemsUnasked_AndAMixedSetAsks` and `APassedEnd_AsksAboutTheRun_NamingTheLimitOnlyWhereItHeldARunBack`, `SessionAuthorityAccessTests.AnAgentsRequestForAutomaticWithCommands_ReachesThePersonWithTheCommandsSentence`, `ApprovalPromptTests.EveryGainThatStartsCommandsUnasked_SaysSo`, `ALimitGainUnderPlainAutomatic_SaysNothingOfCommands` and `AReset_NamesEachAgentWhoseCommandsWouldStartUnasked`, `AlwaysAllowRecordTests.An_always_object_without_items_holds_no_row_and_is_understood`, and `AccessChangeWindowTests.A_commands_caution_stays_in_sight_outside_the_gains` over the prompt's, Settings', an agent's and a reset's wording. The ScreenRenderer draws `91f6-access-change-reset-commands`.

At `877193cd`: ci 42426591339 (fast tier) green; dev desktop 42426834444 and dev desktop-gates 42426859936 green on Linux. Locally: the touched Core, CLI and App classes, the ScreenRenderer at every theme and zoom, and `verify.sh compile`.

## Open

- `ApproverProtocol`'s always-not-shown count can no longer be reached: once project rows are left out, 256 item UUIDs always fit one reply. It stays, untested by a reply that needs it.
- The bridge's bound on a run stays two windows, which T-10 states; a run no longer has a confirmation after its prompt, so one window would do.

`THREATS.md` names three tests that do not exist, on `main` too: `EnvTagVerbTests.Tag_names_an_agent_set_to_Automatic_there_before_asking`, `RunSessionTests.A_command_the_prompt_cannot_show_whole_is_refused_and_recorded_by_the_owner` and `VerbTests.Get_ClipboardChangedSinceTheCopy_IsLeftAlone`. `DesktopApprovalTests.A_custom_field_is_named_on_the_prompt_and_allowing_it_answers_no_other_field` failed once locally, at the prompt that never came, with its class beside others and passed alone; it asked again before the previous release's reply had come back, so it now waits for that reply.

### Founder decisions of 2026-11-07, evening, not yet built

This branch and F.76's are held unmerged until these are built; the founder chose to stop and plan them in a new session with its own budget.

- Agent settings are split by risk. The vault keeps what an agent can see. Anything that releases without asking (Automatic, Automatic with commands, Always allow rows and a picker's remembered pick) lives only in this computer's app configuration, so a synced or edited vault cannot turn it on. PRODUCT (v1.12's storage sentence and the v1.23 draft) and ACAF v1.2 change with it.
- Automatic's hourly cap and end time are both removed; Automatic lasts until the person changes it.
- The picker's Always allow remembers the pick: it answers this agent's requests for that name with the picked item after the person confirms, kept in the app configuration.
- PRODUCT §3's "MCP remains bounded credential access … not arbitrary shell execution" is reworded in the v1.23 draft for Automatic with commands.
- Still to ask: telling a same-named pair apart in Trash (username or URL, deletion time to the second, or leave it); Agents listing a project set to Automatic twice, under Always allowed and Project rows.

Later the same evening the founder simplified the agent model, superseding the split above where they differ:

- An agent's page holds only what the person always allowed, each with Remove, and whether it may run commands. Modes, Can see patterns, project rows, limits, Give values and this computer's ceilings go. Every agent sees item names, never values.
- A value request offers Deny, Allow once and Always allow, the last for this item or for everything this agent asks; two items answering opens the picker, whose Always allow remembers the pick. A command offers Deny, Allow once or always allowing this agent to run commands. Production stays asked, Allow once only.
- Nothing about agents is kept in the vault: what is always allowed lives in this computer's app configuration. ACAF v1 and v1.1 stay published as written, marked as no longer used by keypaste; v1.2 is not published. PRODUCT v1.12's storage sentence is amended.
- Before 1.0.0, an agent may ask to save a value: it names the place and variable, and the person types or pastes the value in the app, which the agent never sees. This needs a dated PRODUCT amendment.
- A request arriving while a prompt is open waits its turn instead of being refused as busy. How a looping agent is kept from piling prompts up is still to settle.
