# Threat model

What keypaste protects, from whom, and what stays true after each mitigation. [SECURITY](SECURITY.md) holds the [security laws](SECURITY.md#security-laws), which govern this model, and the reporting policy. Tests name the threat they hold: `git grep -nw T-5 -- tests scripts` finds T-5's.

## Assumptions and scope

The model covers every surface that exists: the vault, the `keypaste` CLI, the bridge an AI tool starts (`keypaste mcp`), the approver, the desktop app, the browser relay and extension, share links and setup. Planned surfaces, such as an end-to-end-encrypted cloud vault and team projects, get no guarantee until they exist.

1. **The person's OS account is the trust boundary.** A same-user process can read memory, keystrokes and files, and write any file the account owns. It is outside keypaste's protection; entries say where such a process gains something specific.
2. **Whoever starts the bridge chooses its arguments**: `--expose`, `--client-label` and the vault. They constrain a connected client, not that process (T-14).
3. **KDBX4 and Argon2 are the cryptographic boundary.** keypaste implements no cryptography of its own (security law 6).

Out of scope: same-user attackers reading unlocked memory, audit records and keystrokes (T-18, T-19, T-20), and what a client does with what it was given.

## Terms

- **Approver**: the process holding a vault unlocked and answering agents: the desktop app or `keypaste approve` (`keypaste agent` in `v0.3.0`). The code calls it the owner. A vault has one at a time (T-29).
- **Bridge**: `keypaste mcp`, which an AI tool starts. It holds no vault and forwards requests to the approver.
- **Agent settings**: this computer's rules for each vault, in `~/.keypaste/agents/<vault>.toml`. For each category of value (password, custom field, project variable, card value, identity value, one-time code, notes, attachment) they say Ask, Allow or Block, for every agent and per agent, and whether each agent may run commands. `v0.3.0` uses a user-written `policy.toml` instead.
- **Unasked release**: a value released under an Allow, with no prompt.
- **Same-user process**: any program running as the person's own account.
- **Threat IDs** are permanent and grouped below by surface. T-45, T-46 and T-47 are retired.
- **Status**: `v0.3.0` means the published CLI and bridge have the mitigation. *Unreleased* means only the source build headed for 1.0.0 has it, which includes all of the desktop app. [RELEASE](docs/RELEASE.md) defines the states.

## Assets

| Asset | Threats |
|---|---|
| Secret values: passwords, custom fields, project variables, one-time codes, attachments | T-4, T-8, T-12 to T-14, T-18, T-19, T-25, T-30, T-35, T-38, T-56 |
| The master password and the vault's other factors: key file, YubiKey, the copy quick unlock keeps | T-7, T-20, T-22, T-27, T-28, T-37, T-44 |
| Item titles, sites and how items connect | T-1, T-4, T-24, T-55 |
| The person's decision at a prompt | T-2, T-11, T-41, T-56 |
| The audit log as evidence | T-5, T-6, T-16, T-53 |
| Agent settings and remembered browser fills | T-3, T-15, T-36, T-41, T-61 |
| The vault's contents and its newest state | T-26, T-29, T-43, T-54, T-57 |
| Logins released to a browser, and browser pairings | T-48 to T-50, T-58 to T-61 |
| Scoped tokens, share links and launch passes | T-32 to T-34, T-42 |
| That a download is its tagged source | T-9, T-21 |

## Adversaries

| Adversary | What it can do | Threats |
|---|---|---|
| The model, and the agent it drives | Compose requests from attacker-written context, write a request's reason, use every tool its bridge offers, and with a shell run `keypaste` itself | T-2, T-4, T-8, T-11, T-35, T-41, T-54 to T-57 |
| Anyone who can write the vault | Plant titles, fields, tags and values through a shared, synced or KeePassXC-edited copy | T-1, T-13, T-43 |
| An impersonating MCP client | Start the bridge under any name or label | T-3, T-14, T-36 |
| A same-user process | Everything in assumption 1 | T-14, T-15, T-30, T-34, T-42, T-48, T-50 |
| Another local user | Bind or pre-create the approver's pipe | T-10, T-29 |
| Someone holding a copy of the vault | Guess its password offline | T-20, T-27, T-37 |
| Someone at or watching the screen | Walk up, look, record, read notifications | T-23, T-25, T-51 |
| A web page | Pose as an item's site, trick a click | T-58, T-59 |
| The supply chain | A compromised dependency, Actions runner, extension, store account or share host | T-9, T-21, T-33, T-60 |
| No attacker | Edits, syncs, merges, cultures and duplicate titles | T-28, T-52, T-53 |

## Trust boundaries

| Party | Trust | Boundary |
|---|---|---|
| Person at the keyboard | Trusted | Authorizes releases. |
| `keypaste` CLI and bridge code | Trusted | Open source and auditable under security law 8. |
| Vault file | Trusted at rest | KDBX4 gives integrity and confidentiality; item text stays untrusted (T-1). |
| MCP client | Semi-trusted, unauthenticated | Starts the bridge and supplies an unverified identity (T-3). |
| Browser and keypaste's extension | Semi-trusted, unauthenticated | Start the relay under an extension identity any local process can name, and pair only once the person confirms the extension's code (T-48, T-50). |
| Model | Untrusted | Can compose requests from attacker-written context. |
| Item titles, group paths and a request's `reason` | Untrusted data | Cannot instruct keypaste (T-1, T-2). |
| Agent settings, `agents/<vault>.toml` | Trusted only while sealed | Anything unsealed reads as the defaults, which ask about every value (T-15). |

An agent lists the item titles its bridge's `--expose` reaches and asks for a field, an attachment, a linked set (T-56), a run (T-35), a save (T-54) or an item change (T-57). The approver re-checks each request, decides it by the field's category and records it before replying. A Blocked category is refused unasked, an Allowed one released unasked, and an Ask one put to the person. No agent request can make a master-password prompt appear (T-7). A lock withdraws waiting requests but does not revoke values already delivered.

## Agent requests and prompts

### T-1 — Prompt injection through entry names

**Threat.** Anyone who can write the vault puts instructions in item titles, field names or tags for the model or the person to read.

**Mitigation.** Every displayed title, field name and tag is sanitized: control, format and markup characters become spaces, titles are capped at 128 characters, and altered text is marked. Listings carry no values, wrap titles in data banners, and drop whole items past 64 KiB, saying so. keypaste never executes item text, and `env pull` imports only env-named keys.

**Limit.** Plain-language instructions survive, and there is no phrase blocklist, because paraphrases bypass it. One large title can crowd others out of a listing; `keypaste ls` still works.

**Status.** `v0.3.0`; field and tag names unreleased.

### T-2 — Prompt injection through the agent's stated reason

**Threat.** The model writes `request_credential`'s free-text `reason` to persuade the person, redraw the prompt or shape the record of why a value was asked for.

**Mitigation.** A reason over 2000 characters is refused, and the prompt strips control, newline and bidi characters, shows at most 400, and labels the rest as the agent's words in a fixed box. Deny has focus, and Allow or Return works only a second after the prompt gained focus, so a stray keystroke answers nothing. The audit line keeps a 200-character sanitized excerpt, the length and a SHA-256 of the raw text.

**Limit.** A sanitized reason can still persuade, and nothing judges whether it is honest. Sanitizing does not detect sensitive text an agent put in its arguments.

**Status.** `v0.3.0` for the terminal prompt and audit excerpt; desktop prompts unreleased.

### T-3 — Confused deputy: a malicious or impersonating client

**Threat.** A local process starts the bridge claiming to be another client, such as `claude-code`, to be credited as that agent and get its settings.

**Mitigation.** The client's own name authorizes nothing; the operator's `--client-label` selects the agent's settings, and a bridge with no label gets only the every-agent settings, no Always allow and no run. No tool lets an agent change its settings. The desktop prompt says Verified only when the system shows the caller is keypaste's bridge started by the AI tool the label names, and Not verified otherwise.

**Limit.** Whoever starts the bridge chooses the label, exposure and vault, so a label is no authentication (T-14, T-36). Verified decides nothing and does not hold against a same-user process, which can start the real tool with its own configuration or run its installed script. `keypaste approve` does not show Verified, and no Verified prompt from a real AI tool has been observed yet.

**Status.** `v0.3.0` (the label selects `policy.toml` rules); per-agent settings, Always allow and run unreleased.

### T-4 — Over-exposure of the listing surface

**Threat.** A connected agent tries to see or reach more items than intended: titles can reveal banks, employers and recovery accounts, and a wider view brings more values into an Allow's reach.

**Mitigation.** An agent sees only what its bridge's `--expose` reaches (`**` as `keypaste setup` and Connect register it, otherwise `tag:env:*`): titles, groups, tags, Website and, unless Settings hides it, the username. Every other value is decided by its category, no listing or search argument widens the view, and the approver re-checks every request. With several vaults open, a bridge reaches only the vaults its `--vault` names.

**Limit.** A wide view with a category set to Allow releases every such value unasked; `--expose "**"` reaches every item whole. A variable naming another field by placeholder carries that field out (T-8).

**Status.** `v0.3.0` for place patterns; tag selectors, setup's `**` and the visibility switches unreleased.

### T-8 — Only the requested field leaves, and only on one path

**Threat.** An agent asks for more than one field, a field it should never get, or a value carrying other fields inside it.

**Mitigation.** Listing, search and credential replies are separate message kinds, and a credential reply carries one field, or the linked set the person allowed (T-56). One-time-code seeds are refused before anything is read, passkey fields are always Blocked, and `totp` releases only the current code. Placeholders fill only from the same item's releasable fields, and a value past its limits is refused whole, never truncated.

**Limit.** A variable holding `{PASSWORD}` gives the password, under the variable's name, to whoever may get the variable. A run resolves references before its prompt, so a denial does not mean nothing was read (T-18). A one-time code with the password lets an agent sign in as the person within that period.

**Status.** `v0.3.0` for one field per release and oversized values; custom fields, codes, placeholders and attachments unreleased.

### T-11 — Prompt fatigue, and clicking yes to make it stop

**Threat.** A retrying or malicious client floods the person with prompts, or a click aimed elsewhere lands on Allow, so approval stops being a deliberate act.

**Mitigation.** One prompt shows at a time across every open vault; at most three wait per agent, and the rest are refused as `BUSY`. A refusal starts a 60-second cooldown for each field it covered, a prompt expires after 45 seconds, and the desktop's Allow works only a second after the prompt has focus. Linked sets, saves and item changes share the slot and cooldown.

**Limit.** There is no cap across different items: twenty items can mean twenty prompts. The pause is Block, or turning an agent's run off ([approvals](docs/approvals.md#when-you-say-no)). With several vaults open, a prompt names its vault in its window title, not yet on the card.

**Status.** `v0.3.0` for one prompt at a time, `BUSY` and the cooldown; the per-agent queue and desktop arming unreleased.

### T-53 — What two entries of one name did is recorded under the name they share

**Threat.** No attacker: KeePass allows two items with one title in one group, so a release, share or audit line may not say which item it concerned.

**Mitigation.** The desktop acts on items by UUID, and share and credential lines name it. An agent's request that several visible items answer to becomes a picker showing each one's title, group, username and Website, with none picked. A run, a terminal verb or a picker over nine items is refused instead.

**Limit.** Lines written before lines named UUIDs give only the path, and count for neither item.

**Status.** Unreleased.

### T-54 — An agent asks the person to save a value

**Threat.** An agent uses `request_save` to have a value saved where it names, so the vault holds a credential the person would not keep, or a working value is replaced.

**Mitigation.** The person enters the value and saves with Save, armed after a second, or with `y` at `keypaste approve`, and a replace needs confirmation. The agent learns only whether and where it was saved, and the line never holds the value. A place outside the bridge's view is refused unasked (T-4).

**Limit.** A persuaded person has stored the value, and the agent's later request for it meets its category's setting. A crash between the line and the save records a value the vault does not hold.

**Status.** Unreleased.

### T-55 — A search maps the person's infrastructure

**Threat.** An agent, or its model's provider, uses `find_access` to learn which hosts, accounts, projects and references go together.

**Mitigation.** A search reaches only the bridge's `--expose`, applied to every item and both ends of every link, and a reply holds at most 25 items. An origin is the Website's scheme, host and port, never its path. Hosts and references is off by default, a referenced value is never read, and every search is recorded (T-6).

**Limit.** Under `--expose "**"`, repeated searches can name every host, username and shared project, and with hosts and references on, every `Host` field and reference. Reading references decrypts every field of each item into the approver's memory.

**Status.** Unreleased.

### T-56 — A linked set rides one approval

**Threat.** An agent uses `request_linked_set` to bundle a field it should not get with ones the person expects, so one approval carries it.

**Mitigation.** A set names two to four fields of one item and of items `find_access` links to it within the view; anything else, or any Blocked or recently refused field, refuses the whole set unasked. Otherwise the person is asked every time, in a prompt naming every item and field, with only Deny and Allow once. The fields are read, sent and recorded together, or the set is refused.

**Limit.** Four fields at once may be read less carefully than one. A link says items share a site, project, account or reference, not that they belong to one task.

**Status.** Unreleased.

### T-57 — An agent changes an item

**Threat.** An agent uses `request_item_change` to persuade the person to trash, expire or delete the wrong item, or toggles the `needs-update` tag to mislabel an item or hide a stale-item warning.

**Mitigation.** A move to Trash, an expiry or a delete is asked every time, never covered by a rule, and stated in keypaste's words beside the agent's reason. A delete for good is refused unless keypaste's `acaf.agent-created` mark shows an approved save under the asking label created the item. The `needs-update` tag changes unasked at most once a minute per item, and every change is recorded before it is saved (T-6).

**Limit.** A persuaded person has made the change, and nothing undoes a delete for good. Labels are unauthenticated, so a bridge under the creating agent's label may delete what that agent created (T-3). An agent can hide a stale-item warning by removing the tag, though each change is logged and kept in the item's history.

**Status.** Unreleased.

## Allow and agent settings

### T-12 — A standing approval used under a reason nobody read

**Threat.** A client asks under an Allow, for any purpose it states, for every value of that category, and for any item's Website and username.

**Mitigation.** Allow takes effect only once the person confirms in the app that every value of the category will go to every agent unasked. Each unasked release is recorded as `allowed`, with the reason's excerpt and hash. A linked set and a value in a protected stage (an item tagged `prod`, `production`, `prod-…` or `production-…`) are asked every time (T-30, T-38).

**Limit.** Nobody reads the reason before an unasked release, and reasons are not judged. An untagged item follows its category's Allow, and Websites and shown usernames always leave unasked. Nothing revokes a released credential at its issuer.

**Status.** Unreleased; `v0.3.0` releases unasked under `policy.toml` rules, recorded as `policy`.

### T-13 — An Allow covers a kind of value, not the values you pictured

**Threat.** Something changes the vault after the person set a rule (a new field, a tag, a move, a sync or a merge), so the rule covers values the person never pictured.

**Mitigation.** A field's category comes from its standard place or the item's project tag, never its name, and every unasked release names its category. The app warns when renaming moves items into or out of an agent's view, and a tag change made in keypaste names the stage joined.

**Limit.** A rule covers whatever values of its category exist when a request arrives. A pattern not ending in exactly `**` constrains the title, so `env/dev*` matches `env/devops_ROOT_TOKEN`. Nothing previews which values a rule covers, and a tag added in KeePassXC or by a merge says nothing.

**Status.** Unreleased; `v0.3.0`'s `policy.toml` rules behave the same.

### T-14 — An Allow is a standing grant to anything that can reach the owner

**Threat.** A same-user process starts `keypaste mcp --expose "**"` under any label or none, or speaks the pipe itself, to collect every value released unasked.

**Mitigation.** Keep categories at Ask, and Block what agents never need. An Always allow covers one field for one label, every run is asked (T-35), and the approver records each unasked release in its own log.

**Limit.** Such a process obtains every Allowed value except a protected stage's, and under another agent's label that agent's Allows and answers too. The pipe admits only the account without authenticating the peer.

**Status.** Unreleased; `v0.3.0` has the same exposure over `policy.toml`.

### T-15 — Agent rules are a file any program of the person's can write

**Threat.** A same-user program writes a vault's file in `~/.keypaste/agents/`, or a synced or edited vault carries agent records, to change the agent settings or the remembered browser fills (T-61).

**Mitigation.** The approver follows the file only while its seal holds: an HMAC under a key the vault keeps, which an older or copied file fails. Anything unsealed reads as the defaults (every category Ask, run off) and the app says so; an unreadable file refuses every request. Agent records inside a vault are ignored.

**Limit.** The seal authenticates the process holding the unlocked vault, not the person, so a same-user process holding it or reading its memory can seal any settings. The seal does not name its vault, so a copy of the vault unlocked first could take the file.

**Status.** Unreleased; `v0.3.0` reads an unsealed `policy.toml`, with no permission check on Windows.

### T-17 — A timing oracle over Allow

**Threat.** A client times unasked replies against human-delayed ones to learn which categories are set to Allow or Block.

**Mitigation.** None; a Block refusal names no category.

**Limit.** Unmitigated, since a delay would not hide the difference from the next prompted request. The client learns nothing it could not learn by asking.

**Status.** Unreleased; `v0.3.0` has the same exposure.

### T-36 — An agent's settings bind only the bridge that names them

**Threat.** An agent that can edit its client's configuration (`.mcp.json`), or a same-user process running `keypaste use` or `keypaste clients`, takes or changes an agent's settings.

**Mitigation.** A label gives only what the person confirmed for it, an unlabelled bridge runs nothing, and every run is asked (T-35). The Agents screen says settings follow the name the connection was given, and `keypaste clients` lists them.

**Limit.** Such an agent can take another agent's label, with its run, Allows and answers, or change its `--vault` or `--expose`. No setting covers `keypaste run --session` from an agent's own shell.

**Status.** Unreleased; `v0.3.0` labels select `policy.toml` rules the same way.

### T-38 — One secret shared by two stages

**Threat.** An agent exposed to one stage of a project reaches a value shared with a stage it should not reach.

**Mitigation.** An item tagged into two stages, one of them protected, is asked about every time.

**Limit.** Exposure and protection are per item, not per stage; a value that must stay out of a stage belongs on its own item.

**Status.** Unreleased.

### T-41 — An agent asks for more access

**Threat.** An agent with a shell runs `keypaste clients`, or wears the person down with requests, to widen its settings.

**Mitigation.** No tool changes what an agent gets, `keypaste clients` can only narrow (T-15), and an Allow takes the person's confirmation in the app. Every settings change, made or refused, is an `access_change` line.

**Limit.** A person who confirms an Allow gives every agent every value of that category.

**Status.** Unreleased.

## The audit log

### T-5 — Audit log tampering

**Threat.** Anyone able to edit the audit file edits, removes or inserts lines, so the log no longer shows every access.

**Mitigation.** Every line carries the previous line's hash, so `keypaste log verify` detects an edit, removal or insertion, and `--expect <hash>` checks an anchor the person kept. Every 64 lines the approver appends a checkpoint signed with a key the vault keeps, which `log verify --checkpoints` checks. Only the approver compacts the log, under a signed record, and only a log that verifies.

**Limit.** The chain is tamper-evident, not tamper-proof: lines after the last checkpoint can be recomputed, and deleting the end leaves a valid file unless the person kept an anchor elsewhere. On Windows the log has no append-only protection. `v0.3.0` stops appending after a line longer than 4 KiB, which fails closed.

**Status.** `v0.3.0` for the chain, `log verify` and `--expect`; checkpoints and compaction unreleased.

### T-6 — Unlogged access

**Threat.** A full disk, lost permissions or an error lets an access go unrecorded, breaking security laws 3 and 7.

**Mitigation.** The bridge and `keypaste approve` refuse to start on a log they cannot write. The approver records every request before replying, the bridge records every call no approver recorded, and a failed append refuses the response.

**Limit.** A crash or lost reply after the append records an access that never reached the client. A settings change sealed when no line could record it still holds, and the person is told.

**Status.** `v0.3.0`; approver-side recording unreleased.

### T-16 — The audit vocabulary is the only evidence a person was involved

**Threat.** A line or agent-facing text presents an unasked release as a person's approval.

**Mitigation.** Each authority has its own method in the log: `prompt` for a person's answer, `allowed` for an Allow, `blocked` for a Block and `rules-changed` for a settings change. Agent-facing text uses the same words.

**Limit.** Editing the vocabulary later breaks the chain unless the attacker repairs it too (T-5).

**Status.** Unreleased; `v0.3.0` records rule releases as `policy`.

## The approver and the local machine

### T-7 — Where the master password is typed

**Threat.** An agent or MCP client tries to collect the master password or make a password prompt appear.

**Mitigation.** The password is typed only into the desktop app or the terminal running `keypaste approve`. No request can make a password prompt appear: a request to a locked vault is refused, and the app's notice opens the unlock screen only at the person's press.

**Limit.** `keypaste approve` has no idle lock. A same-user process can bring the app's unlock screen forward (T-29).

**Status.** `v0.3.0` for the terminal approver; the locked-app notice unreleased.

### T-10 — The approver channel

**Threat.** Another local user binds or connects to the approver's pipe, which carries released values in plaintext, or a peer sends bad frames or exhausts connections.

**Mitigation.** The pipe admits only the current user (`PipeOptions.CurrentUserOnly`), and the approver answers only bridges attached to its current session. Nothing that unlocks a vault crosses the pipe, frames are capped, and at most 64 connections are served.

**Limit.** Another user can pre-create the Unix socket path, so requests fail closed. There is no confidentiality against root or same-user processes, and the connecting program each line records is advisory. Cross-user refusal is untested and rests on .NET's documented pipe behavior.

**Status.** `v0.3.0` for the per-user pipe; session binding and the recorded program unreleased.

### T-18 — Memory dumping

**Threat.** A debugger, core dump, hibernation file or swap page reads plaintext from memory.

**Mitigation.** Master passwords are read into clearable buffers, derived keys are zeroed, and the approver keeps no released value. Core dumps are off on Linux and macOS, the desktop app turns off .NET's diagnostics port where it can, and crash reports carry no memory.

**Limit.** keypaste makes no claim of in-memory secrecy: garbage collection, strings and the OS leave copies, and resolution reads fields before approval. The diagnostics port stays open in some desktop starts, letting a same-user process read memory. Nothing tests memory erasure.

**Status.** Unreleased; `v0.3.0` keeps each approved credential for its grant lifetime, at most `--max-ttl` (default 300 seconds).

### T-19 — Clipboard scraping

**Threat.** Another process, a clipboard manager or history, or a remote session reads a value copied with `keypaste get` or the desktop.

**Mitigation.** The bridge and `keypaste approve` never touch the clipboard. The CLI and the desktop clear a copied value after 20 seconds, and the desktop also on lock and quit. Copies opt out of Windows Clipboard History, and the desktop's carry secret markers for macOS and KDE managers.

**Limit.** Managers may ignore the markers, nothing stops a process reading the clipboard, and remote sessions can copy it elsewhere. A crash, forced quit or power loss skips the clear.

**Status.** `v0.3.0` for `keypaste get`; desktop clearing and markers unreleased.

### T-29 — Who holds a vault

**Threat.** A second approver, a second app start, a path handed to the app, a CLI save behind the approver, or another user taking the app's pipe name splits which snapshot and lock agents meet.

**Mitigation.** The desktop and `keypaste approve` take an exclusive claim on the vault, and a second approver is refused, naming the holder. The app runs once per user, and a path handed to it only selects what the unlock screen offers, shown in full. Under a held vault, writing verbs are refused or sent to the holder (T-43).

**Limit.** A same-user process can put a file named like the person's vault on the unlock screen; the full path is the only protection. KeePassXC takes no claim, so its save in the milliseconds before keypaste's rename is overwritten; other saves are detected and merged.

**Status.** Unreleased; `v0.3.0` has no claim.

### T-43 — A terminal asks the owner to change the vault

**Threat.** A same-user program, an agent's shell included, asks the approver to change the vault without the master password, such as planting `{PASSWORD}` in a variable (T-8) or tagging an item into a project (T-13).

**Mitigation.** While a vault is held, the CLI's edits go to the holder, which changes nothing until the person allows it, with Deny focused. The prompt names the command, item and change, never a value, says any program could have typed it, and names any placeholder. Every edit is recorded before it is saved, or taken back (T-6).

**Limit.** A person who allows without reading gets the change. A crash between the line and the save records an edit the vault does not hold.

**Status.** Unreleased.

## The desktop app

### T-22 — The desktop app's accessibility layer as a read path

**Threat.** Another process queries AT-SPI or Windows UI Automation for secrets typed into the desktop app.

**Mitigation.** Every field that takes a secret is a `MaskedInput`, which exposes only a fixed name and the mask length. The app has no webview.

**Limit.** Keystrokes and pastes arrive as strings that cannot be wiped, and keyloggers are outside this control (T-18).

**Status.** Unreleased.

### T-23 — What auto-lock is for, and what it is not

**Threat.** Someone walks up to an unattended computer while a vault is unlocked.

**Mitigation.** The desktop locks after 30 minutes idle by default, settable up to 8 hours but never off, on `Ctrl/Cmd+L`, and optionally on minimize. A lock locks every open vault, clears the copy and denies waiting requests, and agent requests never count as activity.

**Limit.** Windows Hello, PIN included, and Touch ID can undo these locks (T-44). Locking cannot erase copies (T-18), and `keypaste approve` has no idle lock.

**Status.** Unreleased.

### T-24 — `recent.toml` tells anything that can read `~/.keypaste` where your vaults are

**Threat.** Anything that can read `~/.keypaste`, or a screenshot of the unlock screen, learns where the person's vaults and key files are.

**Mitigation.** The desktop records up to ten vault paths, each with its key file's path, and no names or key material; the list is owner-only on Linux and macOS and can be cleared. Agent settings name items by UUID and keep filled sites and dismissals under keys the vault holds.

**Limit.** Anyone who can read `~/.keypaste` reads what it holds in the clear, below. On Windows these files inherit profile permissions.

| File | In the clear |
|---|---|
| `recent.toml` | Vault and key file paths |
| `app.toml` | The chosen vault's path |
| `projects.json` | Each mapping's vault path, project, directory and command |
| `agents/<vault>.toml` | Agents' labels, the field each kept answer names, the name asked for in each kept pick, and the pairings' names |
| `browsers.json` | Each pairing's name, build and time |
| `sessions/<vault>.owner` | The path of a vault held open |
| `recommendations.json` | Each dismissal's vault identity |
| `audit.jsonl` | What each request named (T-5, T-9) |
| `crash-reports/` | Reports scrubbed of names, paths and values |
| `agents.id`, `save-counts/`, lock files | A random identity, numbers or nothing |

**Status.** Unreleased.

### T-25 — A value on screen, because somebody asked to see it

**Threat.** People nearby, screen captures or remote sessions see a value the person revealed.

**Mitigation.** A reveal shows one value for at most 30 seconds, and hides it when the row changes, a menu opens, or the window loses focus, minimizes or locks. The value is drawn, so the accessibility layer never carries it.

**Limit.** The value is readable for up to 30 seconds, and hiding it cannot erase a capture.

**Status.** Unreleased.

### T-51 — A notification is read by more than the person

**Threat.** The lock screen, notification history and programs that read them see what a system notification reveals, and a late press acts on something that has gone.

**Mitigation.** A notification carries only fixed words, never an item, value, vault, agent or reason. Its one action brings the prompt forward disarmed or opens the unlock screen, and never allows. It posts only while no keypaste window has focus, at most once every 30 seconds.

**Limit.** Anyone who sees it learns keypaste is here and something asked. A removal the system ignores leaves the words in history.

**Status.** Unreleased.

### T-52 — A culture changes what a rule means

**Threat.** No attacker: the desktop runs in the person's culture and the CLI, bridge and relay run invariant, so a name could mean one thing to the app and another to the CLI.

**Mitigation.** Every culture-sensitive call states its culture, and the build fails otherwise. The audit log, its hash and access matching are ordinal.

**Limit.** Letters newer than the Unicode version .NET carries can fold differently, so the app and the CLI can disagree on such a name.

**Status.** Unreleased.

## The vault and its key factors

### T-20 — A stolen vault file

**Threat.** Someone holding a copy of the vault guesses its password offline.

**Mitigation.** KDBX4 provides encryption, Argon2 key derivation and integrity, and keypaste adds no cryptography. A key file or YubiKey adds a factor a copied file lacks (T-27, T-37).

**Limit.** Protection rests on the passphrase: offline guessing has no rate limit, and a recovered passphrase opens older copies too. The audit file stays plaintext (T-5, T-9).

**Status.** `v0.3.0`; Twofish vaults unreleased.

### T-26 — Restoring a backup while the app is locked

**Threat.** Someone who knows an earlier master password and can write the vault's directory restores an old copy, or a sync tool puts one in its place.

**Mitigation.** The unlock screen restores a backup only after opening it with its own credentials, restores only the checked bytes, and keeps the replaced vault first. Every keypaste save raises a count the vault carries, and an unlock that finds a lower count than this home last saw warns once.

**Limit.** Such a person can roll the vault back, losing the newer state's availability without disclosing anything; delete old copies after a password change. The warning refuses nothing, and misses a copy whose count is not lower or a first open in a new home.

**Status.** Unreleased.

### T-27 — Naming a keyfile tells the machine where the second factor is

**Threat.** Code running as the user reads the process table, shell history, environment, configuration or `recent.toml` to learn where the key file is.

**Mitigation.** `--keyfile` and `KEYPASTE_KEYFILE` carry a location, never key material. `keypaste access` takes a new key file only from `--new-keyfile`.

**Limit.** Anything running as the user learns where to look. A key file protects against someone holding the vault without it, not against code on the unlocked machine, and a lost key file cannot be reconstructed.

**Status.** Unreleased; `v0.3.0` has no key files.

### T-28 — A keyfile that is any file at all is one edit from losing the vault

**Threat.** No attacker: an edit, a re-encoding or a sync tool rewrites a file used as a key file, and the vault it keys is lost.

**Mitigation.** keypaste opens all four KeePass key file forms, warns on every open with the any-file form and never creates one. It writes only KeePass XML key files, never over an existing file.

**Limit.** With the any-file form, any change to the file's bytes loses the vault for good. Anyone who can read a key file has that factor.

**Status.** Unreleased; `v0.3.0` has no key files.

### T-37 — A YubiKey protects a copied vault, not an unlocked one, and a lost key locks it

**Threat.** For a vault that needs a YubiKey: someone with a copy of the vault, malware driving the plugged-in key, or losing the key.

**Mitigation.** The desktop derives the key as KeePassXC does, and every save draws a fresh challenge, so a copied file needs both the password and the key's secret. Settings warn about loss first.

**Limit.** Nothing protects an open vault from a same-user process, and malware driving the key can get its answer. A lost key locks the vault for good unless a second key was programmed with the same secret. The CLI cannot open such a vault, and nothing has been tested against a physical YubiKey.

**Status.** Unreleased.

### T-44 — Quick unlock stands in for the master password after a lock

**Threat.** Anyone who can pass Windows Hello or Touch ID as this user reopens the vault after a lock without the master password, or a same-user program obtains the copy's key and reads the app's memory.

**Mitigation.** After a password unlock the app keeps the composite key's parts sealed in memory only, under a key only quick unlock gives back: on Windows a Windows Hello signature, on macOS 32 random bytes in a keychain item that only a fingerprint enrolled when it was made releases, kept on that Mac alone, never synced and deleted when the copy is dropped. Windows Hello or Touch ID asks on every keep and resume, and only the person's lock, the idle lock, minimize-lock and the tray allow a resume. Adding or removing a fingerprint makes Touch ID's copy unreadable, and only keypaste's Developer ID build reaches the keychain item.

**Limit.** Anyone who passes Windows Hello, PIN included, or whose fingerprint was enrolled, can reopen the vault after those locks. A same-user program with the key and a read of the app's memory gets parts that open every copy of the vault, and a kept copy has no maximum age. Nothing has been tested against a real Windows Hello; Touch ID was seen on one Mac, with the notarized app, on 2026-11-23.

**Status.** Unreleased, Windows and macOS.

## Runs, tokens and share links

### T-30 — A run asks the session for a whole env set

**Threat.** Anything running as the user that can start `keypaste run --session`, an agent with a shell included, asks for a project's whole env set.

**Mitigation.** A run is released only after the person allows it, Allow once only (T-34), or on macOS under the app's own launch pass (T-42); no agent setting releases it. The prompt shows the project, variables, items, command and directory, never a value, and every run is recorded (T-6).

**Limit.** The command and directory are the runner's claim, so allow only a run you started. The child keeps its values after a lock, and the standalone `keypaste run` opens the vault itself and is not audited.

**Status.** Unreleased; `v0.3.0` has only the standalone `keypaste run`.

### T-31 — A committed reference file chooses what a run injects

**Threat.** Whoever controls a cloned repository's `.env.keypaste` chooses what a run injects, including literals such as `HTTPS_PROXY` or `NODE_OPTIONS` that send secrets elsewhere.

**Mitigation.** `run` uses `./.env.keypaste` on its own only when `projects.json` maps the directory to the one project it names; anything else needs `--env-file`. It lists every literal before opening the vault.

**Limit.** A file the person names can still add literals, which the person must read.

**Status.** Unreleased.

### T-32 — A scoped token is a bearer credential

**Threat.** Anyone who copies a scoped token from a CI log or process list, or holds a token bundle and its token, reads the env sets it covers.

**Mitigation.** Tokens are inject-only, scoped to project, stage and keys, always expire, and are recorded on every request. `KEYPASTE_TOKEN` is stripped from the child, a protected set is asked about live, and a revocation survives merges.

**Limit.** A bundle cannot be revoked and its expiry is a check against the local clock, so rotate its values once it is retired or may have leaked. A copy whose token record was edited after the revocation makes the token work again until expiry.

**Status.** Unreleased.

### T-33 — Whoever holds a share link can open it

**Threat.** Anyone holding a share link, a chat unfurler included, opens what it carries, and whoever controls the share service can serve a viewer that leaks the key.

**Mitigation.** `keypaste share` and the app encrypt on this machine and upload only ciphertext, with the key in the link's fragment. Links have a view limit and expiry, can be revoked, and can require a separately sent passphrase.

**Limit.** Anyone holding the link opens it until it runs out, and can test passphrase guesses offline, so only a generated passphrase (`keypaste generate --words 6`) protects an intercepted link. End-to-end encryption holds only while share.keypaste.com, or the `--endpoint` host, serves an honest viewer.

**Status.** Unreleased.

### T-34 — A run's request is the runner's claim

**Threat.** A same-user process repeats a command a person once allowed, to have an env set released again.

**Mitigation.** Every `keypaste run --session` is a new request offered only Deny and Allow once, and no rule or remembered answer releases it.

**Limit.** What a run says it will start is its claim (T-30).

**Status.** Unreleased.

### T-35 — A run keeps values out of the result, not out of the agent's reach

**Threat.** An agent that names the command, and can often edit what it runs, uses the `run` tool to get at the values given to it.

**Mitigation.** The tool exists only under `--allow-run`, for labels the person turned run on. Every command is asked, Allow once only, showing the program, arguments, directory, variable names and reason, and output is scrubbed of each value.

**Limit.** An approved run can leak a value encoded past the scrubber, to a file or over the network, and any same-user process can read the child's environment. Runs give no confidentiality against an agent with its own shell.

**Status.** Unreleased.

### T-42 — A launch pass is a bearer for one run

**Threat.** A same-user process reads the launch request or the run's environment and presents the launch pass first, receiving the env set a macOS Run confirmed.

**Mitigation.** On macOS the app hands Terminal no value, only a 32-byte pass in an owner-only file. A pass works once, for two minutes, within its unlock, for the exact request shown, and never appears on a command line.

**Limit.** A same-user process can present the pass first; the person's window then says it was used, and Activity shows both requests. Only Terminal is supported.

**Status.** Unreleased; an unsigned bundle's launcher runs only once its quarantine is cleared.

## Install, setup and release

### T-9 — Exfiltration and telemetry

**Threat.** Values, names or the audit log leave the machine through keypaste, its dependencies or a crash report (security laws 3 and 5).

**Mitigation.** The audit log stays local, and the bridge speaks only over standard input and output and a local pipe. The build keeps keypaste's code off the network outside `keypaste share`, and packages are pinned by content hash. Only the desktop sends crash reports, once the person says yes, holding only code identifiers, the version and the system, shown exactly as sent.

**Limit.** Any package in the process could use the network, bounded only by its pin. A report shows api.keypaste.com the sender's IP address, and the first-launch screen asks for reports as a separate choice, off by default, and has not been checked against EU consent law.

**Status.** `v0.3.0` for the stdio bridge, with no crash reports; crash reports unreleased.

### T-21 — The released binary is not the source you read

**Threat.** A compromised Actions runner, action or storage credential substitutes a download's bytes, or replaces an archive together with its checksum.

**Mitigation.** `release.yml` tests the binaries it uploads and checks the version and checksums, Actions and tools are pinned, and `publish-release.sh` publishes only to an empty destination. Every asset carries a Sigstore attestation, checkable with `gh attestation verify`.

**Limit.** The attestation cannot detect a compromised workflow run, which attests what it built. Binaries are unsigned and builds are not reproducible.

**Status.** `v0.3.0`, the first release with an attestation; desktop packages unreleased ([RELEASE](docs/RELEASE.md)).

### T-39 — The terminal's `keypaste` is whatever its PATH entry or link names

**Threat.** Whoever can change what a PATH entry or link names chooses the program a terminal runs as `keypaste`, into which the person types a master password.

**Mitigation.** Each desktop install puts its own `keypaste` on PATH and replaces a file it did not write only after the person confirms, keeping it as `keypaste.keypaste-backup`. macOS Settings names an earlier `keypaste` that is not this bundle's.

**Limit.** The macOS link is on every account's PATH and points into a bundle the installing account can write, so that account chooses what `keypaste` runs for every account, root included; it is the only limit that crosses accounts, and it is accepted. An earlier `keypaste` elsewhere on PATH goes unseen.

**Status.** Unreleased.

### T-40 — Setup edits another program's configuration file and starts its command

**Threat.** Setup edits AI tools' configuration files, which hold other servers and their tokens, and starts their commands, so a bad edit or a same-named program can turn that against the person.

**Mitigation.** No vault secret enters these files (security law 4). Setup shows each edit and its backup, changes only keypaste's entry, leaves alone a file that does not parse or changed since read, and replaces it by one rename. Tools are found only in absolute PATH entries and documented install folders.

**Limit.** A tool writing its file during keypaste's edit loses that write, and the backup keeps the original's tokens. An install link names the person's home and vault paths wherever it is pasted.

**Status.** Unreleased; `v0.3.0`'s `keypaste setup` runs Claude Code's and Codex's `mcp add`.

## Browser fill

### T-48 — The browser relay holds released passwords in plaintext, and anything can start it, or ask to pair

**Threat.** A same-user process starts the relay as a browser would, or speaks its pipe, to get logins released to a browser or a pairing.

**Mitigation.** The relay holds no vault and cannot reach vault access or the audit log, and a browser's connection may only pair, list a page's logins by name and release the one picked. Pairing needs the person to confirm a six-digit code the extension also shows. A refusal holds pairing requests back for a minute, doubling up to an hour.

**Limit.** A same-user process can read decoded logins in the relay's memory and obtain what pairing and fills allow (T-59). A person who pairs without comparing the codes can pair that program.

**Status.** Unreleased.

### T-49 — Another program answers keypaste's host name

**Threat.** A same-user program writes a manifest for `com.keypaste.browser` where a browser looks first, so the browser starts it and sends it the pairing key.

**Mitigation.** Settings › Browser fill and `keypaste browser` name the program each browser starts. Settings asks before replacing another program's manifest.

**Limit.** keypaste does not watch the manifests, so a replacement is seen only when the status is read (T-50). LibreWolf reads its own folder before Firefox's, which keypaste neither writes nor reads, so a manifest there answers LibreWolf while Settings shows keypaste on.

**Status.** Unreleased.

### T-50 — A pairing is a bearer key

**Threat.** Whoever reads the pairing key from the browser profile and can reach the approver's pipe uses the pairing, or a same-user program edits `~/.keypaste/browsers.json` to add or revive one.

**Mitigation.** `browsers.json` holds each key's hash, never the key, and a pairing counts only while an unlocked vault vouches for it with an HMAC. Settings lists pairings with their last use, and Remove ends one. A pairing alone lists names; a login fills unasked only where that pairing filled it on that exact origin before (T-59, T-61).

**Limit.** A same-user process can read the profile's key and use it, since KeePassXC's protocol has only that key as proof. The seal authenticates the process holding a vault, not the person.

**Status.** Unreleased.

### T-58 — A page that is not the item's site

**Threat.** A page made to look like the item's site gets its login filled: a lookalike domain, another tenant of a shared host, a hostile subdomain, plain `http`, or a form posting elsewhere.

**Mitigation.** The extension takes the address from the browser's tab and refuses login forms in cross-origin frames. An item matches only its own host or subdomains within one registrable domain, on the same scheme and port. The first fill on each origin is asked, and `http` pages, single-label hosts and forms posting to another site are always asked, with a warning.

**Limit.** A person can fill a lookalike they misread. Whoever controls a subdomain of the item's site gets its login if the person allows the first fill there, and anyone on the network can answer for an `http` page.

**Status.** Unreleased.

### T-59 — keypaste believes the extension about which page asks, and whether the person picked

**Threat.** A page tricks the person into a click on keypaste's list, by clickjacking as Marek Tóth showed at DEF CON 33 in 2025, or a process holding a pairing's key names any page and claims the person picked.

**Mitigation.** Only a trusted click on keypaste's key icon lists logins, in a list the page can neither read nor restyle. A pick counts only on a list that has been visible, topmost and unobscured for half a second. A pick fills at once only where it filled before (T-61), and otherwise once the person allows it.

**Limit.** keypaste sees only the extension's word, not the page or the click. A process holding a pairing's key lists any site's login names and receives unasked every login that pairing filled before.

**Status.** Unreleased.

### T-60 — The extension, someone else's code, receives passwords

**Threat.** A malicious or compromised extension build, a compromised store account or the browser itself receives every login released to a browser.

**Mitigation.** keypaste answers only its own extension's builds and binds each pairing to its build. A release carries only the picked item's username, password, title and UUID. The extension is keypaste's published fork and keeps no password after filling.

**Limit.** Once released, the extension and the page hold the password, and an update could send it anywhere. keypaste cannot verify the code the browser runs.

**Status.** Unreleased; the extension is not yet in a store.

### T-61 — A login filled on a site before fills there at once

**Threat.** A same-user process holding a pairing's key, or one writing a vault's sealed settings, aims at the logins that fill without asking.

**Mitigation.** After the person allows a first fill, that login, pairing and exact origin are kept in the vault's sealed settings on this computer (T-15). It applies only to that pairing and while the item still matches the page, and Settings › Browser fill lists and forgets them.

**Limit.** Whoever holds the pairing's key gets those logins on those origins unasked (T-50, T-59). A domain that changes hands keeps what was filled on it.

**Status.** Unreleased.

## Known limits

Each entry states its own limits. These cut across many of them.

- **Same-user processes are outside protection** (assumption 1). An Allow, a pairing's key or a launch pass lets such a process take values without a keystroke (T-14, T-42, T-50).
- **No in-memory secrecy.** keypaste makes no claim that memory is cleared (T-18).
- **A released value stays released.** Locking does not revoke values already delivered, and nothing revokes a credential at its issuer or removes a client's copy (T-12, T-56).
- **The audit log is tamper-evident, not tamper-proof**, and keypaste stores no anchor for it (T-5).
- **Agent labels are not authenticated** (T-3, T-36).
- **Binaries are unsigned and builds are not reproducible** (T-21).
- **One threat is unmitigated**: timing shows which categories are set to Allow (T-17).
