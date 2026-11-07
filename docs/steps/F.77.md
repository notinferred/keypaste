# F.77 — Once or Always

Completed 2026-11-07 on `f77-once-or-always`, source only and not merged: the founder reviews PRODUCT's re-ratification, the ACAF v1.2 page and every site change first. Runs are named in the batch's report.

## Amendments

The founder selected this on 2026-11-07, with no STEPS row. Their words: "Remove the allow for 1h option", "always or once", "Security prompt should have an always allow", "Everywhere; commands get Always too".

## What it does

An item's prompt, in the app and at `keypaste approve`, offers Deny, Allow once and Always allow. Always allow releases once, then opens the access-change confirmation; only once the person confirms does anything beyond that release hold. For an entry in one project environment it keeps its wider scope, making that row Automatic. For any other it writes a row, by the item's UUID, for that agent. A run's prompt offers the same per exact command, the confirmation saying the command's files can change and that keypaste approves the command line, not what it does (T-35). Protected items stay asked every time, a tag added after the row included. An agent's settings on Agents list what is always allowed, each with a Remove. Each release under a row is logged as `always-allowed`, never `prompt`.

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
- **`Run` chip reads "On, asked about unless always allowed"**, since "each command still asked about" is no longer true.

## Published and specification wording changed

These await the founder's review:

- PRODUCT law 2, "Each release contains one credential with one scope and TTL, following explicit human approval or a pre-approved policy the human wrote." now has no TTL and names a confirmed Always allow row as such a policy. §3's "clear reusable grants" became "stop every release, what the person always allowed included". The 2026-09-25 amendment's "or a grant of at most 15 minutes for that command line" became always for exactly that command, confirmed. v1.23 is written as a dated draft above the last ratified line, with ROADMAP's version sentence.
- ACAF: v1 and v1.1 are unchanged. v1.2 changes "Commands are never answered without a person, whatever the mode" to a command never being answered by a mode, and the `mode` default to `ask`. Status is Implemented, on this branch.
- `site/public/index.html`: "once or for an hour" became "once or always" three times, and the choice line in its transcript. `site/web`: agent-approvals, agents-without-values, desktop-app, how-it-works and its security page, and the navigation's ACAF link.
- README's, launch's, demo's and keepass-and-agents' transcripts and sentences about source. Statements of `v0.3.0` stand.
- DESIGN's voice example and segmented control example; the design skill's "gives every grant a duration".

`docs/screenshots/approval-prompt.png` and `agents.png` are not generated by a script, so they are unchanged and stale, and README's alt text stays with them.
