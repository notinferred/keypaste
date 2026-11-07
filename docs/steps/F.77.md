# F.77 — Once or Always

Completed 2026-11-07 on `f77-once-or-always`, source only and not merged: the founder reviews PRODUCT's re-ratification, the ACAF v1.2 page and every site change first. The last fast tier is ci 42413831106 at `2b725955`, with dev desktop 42414492775 and dev desktop-gates 42413830289 green there on Linux.

## Amendments

The founder selected this on 2026-11-07, with no STEPS row. Their words: "Remove the allow for 1h option", "always or once", "Security prompt should have an always allow", "Everywhere; commands get Always too".

## What it does

An item's prompt, in the app and at `keypaste approve`, offers Deny, Allow once and Always allow. Always allow allows the request once and opens the access-change confirmation, answered before the value leaves; only once the person confirms does anything beyond that release hold. For an entry in one project environment it keeps its wider scope, making that row Automatic. For any other it writes a row, by the item's UUID, for that agent. A run's prompt offers the same per exact command, the confirmation saying the command's files can change and that keypaste approves the command line, not what it does (T-35). Protected items stay asked every time, a tag added after the row included. An agent's settings on Agents list what is always allowed, project rows set to Automatic included, each with a Remove. Each release under a row is logged as `always-allowed`, never `prompt`.

Timed allowing is gone as code: `Approved`, `GrantCache`, `EnvGrantCache`, `GrantId`, `--max-ttl`, `ttl_seconds`, the Allowances list, `[h]`, `keypaste grants` and its pipe messages, and the timed mode. A stored `allow-for-a-while` reads as Ask every time, a change rewrites it to `ask`, and the CLI, the pipe and the tool refuse it as a new setting. The default mode is ask.

Storage is a new optional member, `always`, in the agent's `acaf` record (`AlwaysAllowed.cs`, `AcafRecord.cs`), so lock, merge, history and a KeePassXC round trip carry it as they carry the rest. [ACAF v1.2](../../site/web/src/content/docs/acaf/v1.2.mdx) specifies it, with its own schema at `site/public/acaf/v1.2/`.

## Judgment calls

- **A reached Automatic limit offers Deny and Allow once only**, as the founder's task says, since an Always there would bypass a limit the person set. An existing row does not answer such a request either.
- **`always` is a member of the record, not a second field of the entry.** A second field would leave a 1.1 reader's reading whole, but needs its own read, write, undo, duplicate handling and saved-state comparison in the store. A member needs none, and a 1.1 reader tolerates it as its text requires: it keeps it and applies that record only beneath the defaults, which takes away and never widens. No published keypaste reads ACAF, so that reader is so far theoretical. The founder may prefer the field.
- **Keeping `"acaf": 1`.** A value other than `1` makes a 1.1 writer refuse to rewrite the record.
- **An item's row covers every field the agent can see of it**, as the founder's "this agent and this item" reads; its confirmation says so. Reach, values and the cooldown still come first.
- **A TOTP code can be always allowed.** It was once only because a timed grant held a stale value; a row holds no value.
- **A bridge with no `--client-label` is offered no Always allow**, so an unnamed agent's prompt has two buttons: its settings have nowhere to keep one.
- **`keypaste run --session` is offered no Always allow.** It names no agent, so there is no record to keep a row in or list it under, and its command is the runner's claim with no resolved program to bind to. If "project-run prompts" meant this prompt rather than the `run` tool's with a project, it needs a founder decision: a reserved agent name would make one, at the cost of a row in Agents that is no agent.
- **Rows are read as the least of every record that bears on an agent**, so a record not wholly understood, or read while the vault differs from its file, has no row.
- **A ceiling in `clients.toml` holds no row.** A removal can be undone by merging an older copy of the vault, as THREATS T-12 says.
- **`keypaste clients <agent> --forget <id>`** removes a row, so a terminal owner has the undo the app has. The list there shows an item's UUID, as the CLI holds no vault, and `keypaste approve` names on start each agent with rows.
- **An undeliverable release now cools down**, as an unfillable one does: without a grant to store, each retry would otherwise ask again for a value that cannot be sent.
- **Allow once is the prompt's one amber.** The rule is one amber per view, and it is the common answer; Always allow stays neutral.
- **Scoped-token and terminal-edit prompts are unchanged**, and browser fill's words too.
- **The confirmation is answered before the value leaves, and this differs from the founder's order.** The direction says "releases once, then opens". Here the release is authorized once, the confirmation is asked in the same exchange, and the value leaves after it, so a lock during the question refuses the release and writes no row. The agent waits up to one more window, the bridge's bound on a credential request and on a run is two windows, and a refused or expired confirmation still lets the one release go. The founder may want it the other way round, which would let a value out before a lock could stop it.
- **A command's program in its identity is the file its path finally names**, while what starts is the path the bridge resolved, since a program can read the name it was started by. A link pointed elsewhere, or a package upgrade behind one, asks again, and a change to that file does not (T-35).
- **Arguments are kept as written.** A secret typed as an argument stays in the record and its history, and the confirmation, ACAF v1.2 and T-35 say so; keypaste does not guess which arguments are secrets.
- **A record too large for one reply of `keypaste clients`** is sent first without its project rows and then without its always-allowed rows, the verb saying how many of each it left out and that those rows still release. The app's list always shows them. The verb does not page a single agent's record.
- **An agent's own tool cannot add or remove a row.** Only its person changes what is always allowed; a row comes only from a prompt, and a removal only from the app or a terminal.
- **`--forget` with nothing holding the vault is refused**, as is one naming no row or one the vault did not save, since a row lives only in the vault. A whole agent's `--reset` the vault did not save lowers this computer's ceilings and says what is always allowed still releases.
- **The terminal's list names an item by its UUID.** A vault's item names are not sent over the pipe to `keypaste clients`, which any program of the person's can run, past what each agent may see; this is the boundary, so it stands unless the founder chooses otherwise. The terminal confirmation names the item and prints the command that removes its row.
- **Agents' list repeats project rows set to Automatic** that More's Project rows also shows: Always allow's widest result has to appear where the person looks for it. Their limits stay editable only under More.
- **`keypaste log`'s `(!)` now marks an `always-allowed` line** whose reason differs from the last one a person read for that client, item and field, as it marked `grant-cache` lines, which still read as before.
- **`Run` chip reads "On, asked about unless always allowed"**, since "each command still asked about" is no longer true.

## Published and specification wording changed

These await the founder's review:

- PRODUCT law 2, "Each release contains one credential with one scope and TTL, following explicit human approval or a pre-approved policy the human wrote." now has no TTL and names a confirmed Always allow row as such a policy. §3's "clear reusable grants" became "stop every release, what the person always allowed included". The 2026-09-25 amendment's "or a grant of at most 15 minutes for that command line" became always for exactly that command, confirmed. v1.23 is written as a dated draft above the last ratified line, with ROADMAP's version sentence.
- `docs/demo/keypaste-demo.gif` and `site/public/demo/keypaste-demo.gif` are re-recorded from this source with `capture-demo.py` and `render-gif.py`, on macOS with the capture's temporary home written as `/home/you` in the tape, and their captions lose "before its prompt offered Always for". `site/public/index.html`'s "with a time limit on reusing that approval. Expiry does not erase" became "That does not erase". `how-it-works`'s "one scope and a time limit" became "one scope".
- ACAF: v1 and v1.1 are unchanged. v1.2 changes "Commands are never answered without a person, whatever the mode" to a command never being answered by a mode, and the `mode` default to `ask`. Status is Implemented, on this branch.
- `site/public/index.html`: "once or for an hour" became "once or always" three times, and the choice line in its transcript. `site/web`: agent-approvals, agents-without-values, desktop-app, how-it-works and its security page, and the navigation's ACAF link.
- README's, launch's, demo's and keepass-and-agents' transcripts and sentences about source. Statements of `v0.3.0` stand.
- DESIGN's voice example and segmented control example; the design skill's "gives every grant a duration".

`docs/screenshots/approval-prompt.png` and `agents.png` are not generated by a script, so they are unchanged and stale, and README's alt text stays with them.

## Open

`THREATS.md` names three tests that do not exist, on `main` too: `EnvTagVerbTests.Tag_names_an_agent_set_to_Automatic_there_before_asking`, `RunSessionTests.A_command_the_prompt_cannot_show_whole_is_refused_and_recorded_by_the_owner` and `VerbTests.Get_ClipboardChangedSinceTheCopy_IsLeftAlone`. `DesktopApprovalTests.A_custom_field_is_named_on_the_prompt_and_allowing_it_answers_no_other_field` failed once locally, at the prompt that never came, with its class beside others and passed alone; it asked again before the previous release's reply had come back, so it now waits for that reply.
