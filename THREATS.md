# Threat model

This file is keypaste's threat model. For each way keypaste can be attacked or misused it says what is at stake, who the attacker is, what keypaste does about it, what stays true afterwards, which release has it and which tests hold it. [SECURITY.md](SECURITY.md) has the reporting policy and a one-line summary of each guarantee and limit. [PRODUCT §3](docs/PRODUCT.md) governs both.

It covers every surface that exists: the vault, the `keypaste` CLI, the MCP bridge an AI tool starts (`keypaste mcp`), the process holding an unlocked vault, the desktop app, the browser relay, share links and setup. Planned surfaces, such as an end-to-end-encrypted cloud vault and team projects, get no guarantee here until they exist.

## How to read an entry

Each threat has one template:

- **Asset**: what is at stake.
- **Attacker**: who, with what access.
- **Mitigation**: what keypaste does; the code holds the mechanism.
- **Limit**: what remains true after the mitigation.
- **Status**: which release has the mitigation, in [RELEASE](docs/RELEASE.md)'s states. Published today are the CLI and MCP bridge `v0.3.0`; the desktop app has no release, and the next public release is 1.0.0. *Implemented (unreleased)* means the source build headed for 1.0.0. *Published in v0.3.0* means the download has it; where `v0.3.0` differs, the line says how. *Not in v0.3.0* means that surface did not ship in it.
- **Tests**: the main test classes under `tests/` and scripts under `scripts/` that hold the mitigation, or "no test". *Unverified* names an observation no test has made.

Words used throughout:

- **Owner**: the process holding a vault unlocked and answering agents, the desktop app or `keypaste approve` (`keypaste agent` in `v0.3.0`). A vault has one owner at a time (T-29).
- **Bridge**: `keypaste mcp`, which an AI tool starts. It holds no vault and forwards requests to the owner.
- **Agent rules**: this computer's rules in `~/.keypaste/agents.toml`: for each category of value (password, custom field, project variable, card value, identity value, TOTP code, notes, attachment), Ask, Allow or Block, for every agent and per agent, plus whether each agent may run commands. `v0.3.0` uses a user-written `policy.toml` instead.
- **Unasked release**: a value released under an Allow, with no prompt.
- **Same-user process**: any program running as the person's own account.

T-45, T-46 and T-47 are retired and are not reused.

## How agent access works

An agent lists the entry names its bridge's `--expose` reaches and asks for one field, one attachment, a command run with an env set in its environment, or for the person to save a value. The bridge validates the request and forwards it to the owner of the vault it names, which re-checks it, decides it by the field's category and records it before replying; the bridge records only a call no owner recorded (T-6). A Blocked category is refused unasked, an allowed one released unasked, and an asked one put to the person in the app's prompt window or `keypaste approve`'s terminal. A scoped token's run is released under the token a person made (T-32). No agent request can make a master-password prompt appear (T-7).

A lock withdraws waiting requests, and the owner refuses agents while its vault differs from the file (T-29). Locking does not revoke values already delivered.

## Trust boundaries

| Party | Trust | Boundary |
|---|---|---|
| Person at the keyboard | Trusted | Authorizes releases. |
| `keypaste` CLI and bridge code | Trusted | Open source and auditable under law 3.8. |
| Vault file | Trusted at rest | KDBX4 gives integrity and confidentiality; entry text stays untrusted (T-1). |
| MCP client | Semi-trusted, unauthenticated | Starts the bridge and supplies an unverified identity (T-3). |
| Browser and keypaste's extension | Semi-trusted, unauthenticated | Start the relay naming an extension identity any local process can name, through the manifest keypaste writes or any other, and pair only while the person armed pairing (T-48, T-50). |
| Model | Untrusted | Can compose requests from attacker-written context. |
| Entry names, group paths and a request's `reason` | Untrusted data | Cannot instruct keypaste (T-1, T-2). |
| Agent rules, `agents.toml` | Trusted only while sealed | Followed only while the owner's seal holds; anything else reads as the defaults, which ask about every value (T-15). |

## Assumptions and scope

The local user account is uncompromised. A same-user process can read memory, keystrokes and files and is outside keypaste's protection; entries say where such a process gains something specific. Whoever starts the bridge chooses `--expose`, `--client-label` and the vault; those settings constrain a connected client, not that process (T-14). KDBX4 and Argon2 provide the cryptographic boundary, and keypaste implements no cryptography of its own (law 3.6).

Out of scope, with reasons: same-user attackers reading unlocked memory, audit records and keystrokes (T-18 covers memory, T-19 the clipboard, T-20 what KDBX rather than keypaste protects), and the model's behavior, since keypaste cannot control what a client does with what it was given.

## Threats

### T-1 — Prompt injection through entry names

**Asset.** What the model and the person take entry names to mean.

**Attacker.** Anyone who can write the vault (shared, synced or edited in KeePassXC), placing instructions in names, fields or tags.

**Mitigation.** Every displayed name, field name and tag, in the bridge, the CLI and the desktop, is sanitized: control, format, private-use, separator, unpaired-surrogate and astral tag characters and `` ` < > { } [ ] | \ / `` become spaces (replaced, so `ig<NUL>nore` does not join into `ignore`); names are capped at 128 characters and group depth at 16, and altered text is marked. Lookup, editing and copying keep the stored bytes. Listings carry no value and wrap names in BEGIN/END data banners. `env pull` imports only keys matching `[A-Za-z_][A-Za-z0-9_]*`. A listing that overflows its 64 KiB frame drops whole entries and says it is partial. The Projects screen's copied run command quotes a project name for every common shell, or is withheld. keypaste never executes entry text.

**Limit.** Plain-language instructions survive sanitizing and stay readable to the model; there is no phrase blocklist, because paraphrases bypass it. A key `env pull` rejects can still reach the terminal in its error message. One large name can crowd others out of a listing, which denies listing, not values; `keypaste ls` still works.

**Status.** Published in v0.3.0 for the CLI and bridge. Field and tag naming and the copied run command are Implemented (unreleased).

**Tests.** `EntryNameSanitizerTests`, `ServerToolsTests`, `MessageFramerTests`, `ListingSizeTests`, `LargeVaultListingTests`.

### T-2 — Prompt injection through the agent's stated reason

**Asset.** The person's decision at the prompt, and the record of why a value was asked for.

**Attacker.** The model, writing `request_credential`'s free-text `reason` to persuade the person or redraw the prompt.

**Mitigation.** The bridge and the owner each refuse a reason over 2000 characters. The prompt strips control, newline and bidi hazards, caps the shown reason at 400 characters with a notice, and has no default button or deadline a reason could change; the default is no. The desktop's prompt window has a fixed layout the reason cannot move, no default button and focus on Deny; run prompts draw the environment's name whole on its own row. The audit record keeps a sanitized 200-character excerpt, the original length and a SHA-256 of the raw text.

**Limit.** A sanitized reason can still persuade; it is labelled as the agent's words, and nothing judges whether it is honest. Sanitizing does not detect sensitive text an agent put in its arguments.

**Status.** Published in v0.3.0 for the terminal prompt and the audit excerpt; `v0.3.0` also records `args.ttl_seconds`, the requested lifetime rather than the effective one, and `v0.1.0` records the request argument, which can be an opaque handle. The desktop window and run prompts are Implemented (unreleased).

**Tests.** `AuditLogTests`, `ApprovalPromptTests`, `TerminalApprovalChannelTests`, `DesktopApprovalTests`, `DesktopRunApprovalTests`.

### T-3 — Confused deputy: a malicious or impersonating client

**Asset.** Which agent a request, prompt and audit line are attributed to, and which agent's rules apply.

**Attacker.** A local process that starts the bridge and claims to be another client, such as `claude-code`.

**Mitigation.** The client's own name, version and folder are sanitized labels that never authorize anything. What decides is the operator's `--client-label` in the MCP configuration, which selects that agent's rules, its remembered answers and whether it may run commands; a bridge with no label gets only the rules for every agent, no always answer and no run. An answer covers one request unless the person confirms Always allow or Always block for that agent and field. No tool lets an agent change the rules. Calls before `initialize` completes are refused.

**Limit.** The process that starts the bridge chooses the label, the exposure and the vault (`--vault`, or the one chosen in `app.toml`), so a label is no authentication against it (T-14, T-36). The handshake name is never authenticated.

**Status.** Published in v0.3.0, the label selecting `policy.toml` rules. Per-agent rules, always answers and run are Implemented (unreleased).

**Tests.** `ServerToolsTests`, `SessionAuthorityRecordsTests`, `LateHandshakeTests`, `SessionAuthorityRunTests`, `scripts/verify-mcp-stdio.sh`.

### T-4 — Over-exposure of the listing surface

**Asset.** Entry names, which can reveal banks, employers and recovery accounts, and the values a wide view brings into an Allow's reach.

**Attacker.** A connected agent trying to see or reach more entries than intended.

**Mitigation.** An agent sees only what its bridge's `--expose` reaches: `**` as `keypaste setup` and Connect register it, or `tag:env:*` (project entries, env-named fields only) for a bridge configured by hand without one. Of each entry it sees the title, group, tags and URL, and the username unless Settings hides it; every other value is decided by its category. `list_entry_names` takes no arguments, so an agent cannot widen its view or page. Patterns match raw group and title separately, so a title of slashes cannot pose as a group. A tag selector reaches only the entry's project variables, refusing a password, username, URL or notes before anyone is asked, and a malformed selector stops the bridge from starting. The owner re-checks every request.

**Limit.** A wide view with a category set to Allow releases every value of that category it covers without a prompt; `--expose "**"` reaches every entry whole. A variable naming another field by placeholder carries that field out (T-8). Names omitted from an overflowing listing may appear in a reply of another size.

**Status.** Published in v0.3.0 for place patterns. Tag selectors, setup's `**` and the usernames switch are Implemented (unreleased).

**Tests.** `EntryExposureTests`, `ApproverHandlerTests`, `ServerToolsTests`, `SessionAuthorityRunTests`, `FieldCategoryRequestTests`.

### T-5 — Audit log tampering

**Asset.** The audit log as evidence of every access.

**Attacker.** Anyone able to edit the audit file, which belongs to the user's account.

**Mitigation.** keypaste writes only at the end of the log: before each write it seeks to the end, so it never overwrites a record another keypaste process appended, and nothing in keypaste truncates, rewrites or deletes the file. Linux and macOS keep the log and its directory owner-only. Every record carries the previous record's hash and its own SHA-256, so `keypaste log verify` detects an edit, removal or insertion; it prints the record count and latest hash. `--expect <hash>` requires a record whose own bytes match an anchor the person kept. The writer refuses to append under a schema it cannot read, and a stray trailing line cannot disable the bridge. A line may take 64 KiB.

**Limit.** The chain is tamper-evident, not tamper-proof: it has no secret, so a writer can recompute every link, and deleting the end leaves a valid file. Detecting that needs an anchor kept where the attacker cannot edit it; keypaste stores none, by founder decision of 2026-10-22. The sidecar lock that orders keypaste's writers cannot exclude an editor or `sed -i`. Windows files inherit directory permissions, with no append-only flag or ACL (same decision). There is no rotation: the log grows until a person archives it with every keypaste process closed ([archiving it](docs/mcp-setup.md#archiving-it)). `v0.3.0` and `v0.5.0-rc.1` cap a line at 4 KiB and read back 16 KiB, so after a longer line they report the chain broken and refuse to append until a newer keypaste writes the next line; this fails closed and is accepted.

**Status.** Published in v0.3.0: append-only writes, the chain, `log verify` and `--expect`. The 64 KiB line is Implemented (unreleased).

**Tests.** `AuditChainTests`, `AuditLogTests`, `LogVerbTests`, `scripts/verify-log-chain.sh`.

### T-6 — Unlogged access

**Asset.** PRODUCT laws 3.3 and 3.7: no access without a record, and errors deny.

**Attacker.** A full disk, lost permissions or an error on the decision path that would let an access go unrecorded.

**Mitigation.** The bridge and `keypaste approve` refuse to start on a log they cannot open or continue, `keypaste approve` before asking for the password. The owner records every request it answers before replying, and the bridge records every call no owner recorded before returning anything. The owner checks its log opens before asking anyone and otherwise refuses at once. A failed append refuses the response on both sides: no value, name or variable leaves unrecorded. A `run --session` or `--token` set naming more than the 62 KiB of entry names one line holds, and an agent's run naming more than its 2 KiB reply carries, are refused before anyone is asked.

**Limit.** A crash or lost reply after the append can record an access that never reached the client, and a reply lost after recording leaves a second line. A call has no line only if the owner's log failed during the decision and the bridge also could not write, and then nothing is released. A rules change sealed when no line could record it still holds, and the person is told it was made and not recorded. A `v0.5.0-rc.1` runner reads only replies under 64 KiB, so a larger set starts nothing while the owner records the release; this over-report is accepted.

**Status.** Published in v0.3.0, exception-path auditing included (not in `v0.1.0`). Owner-side recording and run budgets are Implemented (unreleased).

**Tests.** `AuditLogTests`, `ServerToolsTests`, `SessionAuthorityRecordsTests`, `OneLinePerRequestTests`, `ApproveAuditLogTests`.

### T-7 — Where the master password is typed

**Asset.** The master password.

**Attacker.** An agent or MCP client trying to collect the password or make a password prompt appear.

**Mitigation.** The password is typed only into the desktop app or the person's terminal after they start `keypaste approve`; the bridge cannot prompt through its JSON-RPC stream or ask the client, and no request can make a password prompt appear. When the app is open and locked on the vault asked about, the request is still refused and recorded, the agent is told the vault is locked in the app, and the app shows one notice per locked spell (T-51) with no field and without taking focus; only the person's press, armed a second after it appears, opens the unlock screen. Stopping `keypaste approve` takes the same lock transition as the app, withdrawing a waiting request.

**Limit.** `keypaste approve` stays unlocked until stopped, with no idle lock. A same-user process can still bring the app's window and unlock screen forward through a second start (T-29).

**Status.** Published in v0.3.0: the password only in the terminal approver. The locked-app notice is Implemented (unreleased).

**Tests.** `SecretHygieneTests`, `LockedAppTests`, `LockedNoticeTests`, `LockedNoticeWindowTests`, `SessionLifecycleTests`. Unverified: whether a notice shown without activation stays over other windows and out of the person's typing, which headless CI displays cannot show.

### T-8 — Only the requested field leaves, and only on one path

**Asset.** Every field of an entry other than the one asked for.

**Attacker.** An agent asking for more than one field, a field it should never get, or a value carrying other fields inside it.

**Mitigation.** Listing and credential replies are separate message kinds, and a credential reply carries one field. Every TOTP seed field (`otp`, `TimeOtp-*` and the like) and a standard name in another case are refused before anything is read, and passkey fields are Blocked under every rule. Run references and `share --field` accept only env-named custom fields (`[A-Z][A-Z0-9_]{0,127}`, not `KPEX_`, `KPXC_` or `KP2A_`). `totp` releases the current code, computed after authorization; the seed never leaves. Placeholders fill in only from the same entry and only from fields that could leave by name; anything else, a loop, nesting past 10 or a value past 1 MiB refuses the value whole. An attachment is released as base64, at most 5 MiB. A value over the 64 KiB frame is refused whole, never truncated.

**Limit.** A variable holding `{PASSWORD}` gives the entry's password, under the variable's name, to whoever may get the variable, and the prompt and log name only the field asked for. A value keypaste cannot fill in is refused only after it was allowed, and its field then cools down for a minute. A run resolves every reference before its prompt, and entry resolution reads standard fields, passwords included, into memory, so a denial does not mean nothing was read (T-18). A TOTP code with the password lets an agent sign in as the person within that period.

**Status.** Published in v0.3.0: one field per release and oversized values refused whole (in `v0.1.0`, an approved credential too large to send costs you every other grant). Custom fields by any name, TOTP, placeholders and attachments are Implemented (unreleased).

**Tests.** `SecretHygieneTests`, `FieldCategoryRequestTests`, `KeePassPlaceholdersTests`, `CustomFieldReleaseTests`, `LargeCredentialTests`.

### T-9 — Exfiltration and telemetry

**Asset.** Entry names, values and the audit log (laws 3.3 and 3.5), and the code that holds them.

**Attacker.** Exfiltration through keypaste's own code or dependencies, or a crash report carrying more than it should.

**Mitigation.** The audit log stays local. The bridge speaks MCP only over standard input and output and reaches the owner through a local pipe; its code opens no listener and makes no network request. Its packages are pinned by content hash, none has a module initializer, and build rules keep the bridge's code from naming vault or password types. Crash reports come only from the desktop app's packages, only after the person agrees to the terms of use on the first-launch screen, and stop with Settings' switch. A report holds only code identifiers, the version, system and open screen, never a message, value, name, path or identifier, is at most 16 KiB, and is shown in Diagnostics exactly as sent. The CLI, `keypaste approve` and the bridge cannot send one (build-enforced). keypaste.com keeps no address and deletes a report 90 days after last seen.

**Limit.** A command the `run` tool starts may use the network (T-35). The MCP SDK carries HTTP transports, and any code in the process could use the runtime's networking: what bounds it is the pin, and a compromised pinned version would ship inside the binary that holds the vault. A name search of `v0.3.0`'s bridge suggests, without proving, that trimming removed the HTTP transports. The first-launch screen has no way to continue and refuse reports (step H-0027 checks this against EU consent law). A report shows keypaste.com the sender's IP address. Anyone can send a forged report in the grammar, so a count is evidence only when its frames name keypaste code. The route answers 503 until its database exists (step H-0028).

**Status.** Published in v0.3.0: the stdio-only bridge with a pinned closure, and no crash reports. Crash reports are Implemented (unreleased), desktop app only.

**Tests.** `BridgeClosureTests`, `DispatchOrderTests`, `CrashReportScrubbingTests`, `CrashSourceRulesTests`, `CrashSenderTests`.

### T-10 — The approver channel

**Asset.** Released values and terminal edits crossing the local pipe to and from the owner in plaintext.

**Attacker.** Another local user binding or connecting to the pipe; a peer sending bad frames or exhausting connections.

**Mitigation.** Both ends open the pipe with `PipeOptions.CurrentUserOnly`: on Windows its access list admits only the current user, and on Unix the runtime refuses a peer running as another user before anything is read. A bridge attaches by naming its vault, and the owner answers only connections attached to its current session, so a request across a lock, an unlock or another vault is refused unasked. Nothing that unlocks a vault crosses the pipe. Frames are capped at 64 KiB, except an env set or attachment reply only its asker reads, under 8 MiB; a bad frame closes only its connection, and at most 64 connections are served.

**Limit.** Another user can pre-create the predictable Unix socket path, so requests fail closed; the locked-app endpoint's name is predictable too, so taking it stops only the notice, and a same-user process holding it makes agents be told the app is open and locked when it is not. There is no confidentiality against root or same-user processes. Idle connections are never closed.

**Status.** Published in v0.3.0 for the per-user pipe. Session binding, the connection cap and the extra endpoints are Implemented (unreleased).

**Tests.** `ApproverListenerTests`, `MessageFramerTests`, `SessionAuthorityTests`, `LaunchEndpointTests`, `LockedAppEndpointTests`. Unverified: cross-user refusal, since CI has no second user account; it rests on .NET 10's documented pipe behavior.

### T-11 — Prompt fatigue, and clicking yes to make it stop

**Asset.** The person's attention, and approval as a deliberate act.

**Attacker.** A retrying or malicious client flooding the person with prompts, or a click aimed elsewhere landing on Allow.

**Mitigation.** One prompt at a time; others wait in order, at most three per agent, the rest refused as `BUSY`, and a bridge holds at most four calls. A refusal starts a 60-second cooldown for each field it covered, whichever connection or kind of request asks next, and a refused run also cools that connection's runs; refusals tell the client not to retry. A prompt expires after 45 seconds and is withdrawn when its bridge or client goes away. The desktop's Allow works only while the prompt has focus and from a second after it gained it, and Deny always works. No agent tool asks for a rules change.

**Limit.** There is no limit across different entries and no hourly cap: twenty entries can mean twenty prompts. The pause is the agent rules: Block, or turning an agent's run off, refuses its requests unasked at once ([approvals](docs/approvals.md#when-you-say-no)). An undeliverable field can prompt again after its cooldown. A platform that never reports activation leaves a prompt answerable only by refusing, and no real window has yet shown each platform's activation order. A busy response does not say which call holds the turn. Tests show no improvement in human attention.

**Status.** Published in v0.3.0: one prompt at a time with `BUSY` refusals, the 60-second cooldown and expiry on wall and monotonic clocks (in `v0.1.0`, an agent can queue approval prompts and moving the clock back can revive an expired approval). The per-agent queue, the desktop's arming and run cooldowns are Implemented (unreleased).

**Tests.** `ConcurrentRequestsTests`, `ApprovalGateTests`, `ApproverHandlerTests`, `DesktopApprovalTests`, `LockedNoticeTests`. No test kills `keypaste approve` during a prompt; `LockBoundaryTests` stops one with SIGTERM.

### T-12 — A standing approval used under a reason nobody read

**Asset.** Every value of a category set to Allow, and an entry's URL and username.

**Attacker.** A client asking under an Allow for any purpose it states.

**Mitigation.** Allow takes effect only once the person confirms, in the app, that every value of the category will go to every agent without asking; Ask and Block apply at once and end an Allow. Allow once keeps nothing and an Allow holds no value, so each release reads the vault and is recorded as `allowed`, with the reason's excerpt, length and hash, naming the category and saying nobody was asked; `keypaste approve` prints each one. Block, the agent's view and cooldowns are checked first.

**Limit.** Nobody reads the reason before an unasked release, so T-2's display controls do not apply, and reasons are not judged for meaning, since a heuristic could be bypassed or used to force prompts. A protected environment's values follow their category like any other: protection holds back a scoped token (T-32), not an Allow. An entry's URL, and its username unless Settings hides it, always leave unasked. Nothing revokes a released credential at its issuer or removes a client's copy.

**Status.** Implemented (unreleased). `v0.3.0` releases without a prompt under a `policy.toml` rule, evaluated on each request and recorded as `policy`.

**Tests.** `FieldCategoryRequestTests`, `ApproverHandlerTests`, `SessionAuthorityRecordsTests`, `AgentRulesViewModelTests`.

### T-13 — An Allow covers a kind of value, not the values you pictured

**Asset.** Values that come under a rule after the person set it.

**Attacker.** Anything that changes the vault, the app, `env tag`, KeePassXC, a synced copy or a merge, adding a field, tagging an entry or moving one.

**Mitigation.** A field's category comes from its place among KeePass's standard fields, KeePassXC's passkey format, or the entry's own project tag, never the field's name. Exposure matches raw group and title, case-sensitive. The app's organize forms say that renaming can move entries into or out of an agent's view; a tag change or history restore made in the app or by `env tag` names the environment joined and its variables. A name more than one entry answers to is put to the person (T-53). Every unasked release names its category in its audit line.

**Limit.** A rule covers whatever values of its category exist when a request arrives: a custom field added later is released under that Allow, an entry tagged into a project later has its custom fields become project variables, and a field named like a card number is a custom field like any other. Moving or renaming an entry onto `env/dev/**` brings it into view, and unless a pattern ends in exactly `**` it constrains the title, so `env/dev*` misses `env/dev/` and matches `env/devops_ROOT_TOKEN`. keypaste does not prevent this: the person reorganizing the vault is the one who set the rules. A tag added in KeePassXC or by a merge says nothing, and nothing previews which values a rule covers; `keypaste log` shows only past releases.

**Status.** Implemented (unreleased). In `v0.3.0` a `policy.toml` rule likewise covers the vault's current contents, and `keypaste policy ls` explains a rule without listing what it covers.

**Tests.** `FieldCategoryRequestTests`, `ProjectTagChangeTests`, `EntriesOrganizeTests`, `EntryExposureTests`. No test claims to stop a vault changing under a rule.

### T-14 — An Allow is a standing grant to anything that can reach the owner

**Asset.** Every value the agent rules release unasked.

**Attacker.** A same-user process that starts `keypaste mcp --expose "**"` under any label or none, or speaks the pipe protocol itself.

**Mitigation.** Keeping categories at Ask, and Block for what agents never need, limits the standing grant; an Always allow covers one field for one label, and Remove ends it. A label gains commands only where the person turned run on, and every run is asked (T-35). The owner records each unasked release in its own log as `allowed`, which no bridge argument redirects, including a request straight to the pipe, and a direct request still meets the session check, argument limits and view re-check.

**Limit.** Such a process obtains every value of each category set to Allow for every agent, and under another agent's label also that agent's Allows, Always allow answers and picks. Label and exposure come from the requester, and `CurrentUserOnly` confines the pipe to the account without authenticating the peer. A reply's `recorded` is the answering process's word. Same-user processes are outside scope, but an Allow removes the keystroke a prompt would need.

**Status.** Implemented (unreleased). In `v0.3.0` a local program can likewise start a bridge under another program's label and match its `policy.toml` rules.

**Tests.** `SessionAuthorityRecordsTests`, `SessionAuthorityRunTests`. They show what the owner records, not protection against a process choosing bridge arguments.

### T-15 — Agent rules are a file any program of the person's can write

**Asset.** The agent rules.

**Attacker.** A same-user program writing `~/.keypaste/agents.toml`, or a synced, shared, restored or KeePassXC-edited vault carrying agent records.

**Mitigation.** The owner follows the file only while its seal holds: an HMAC-SHA256 under a key the vault keeps, over this home's random identity, the vault's count of this home's sealed writes and the rules. So an older file put back, or one copied from another computer, does not hold. Anything unsealed gives the defaults (every category Ask, usernames shown, run off), left in place, and the app says the rules were changed outside keypaste; a file keypaste cannot read refuses every agent request. Agent records inside a vault are ignored. With nothing holding the vault, `keypaste clients` can only narrow, unsealed; every other change takes the owner, which seals and records it.

**Limit.** The seal authenticates the process holding the unlocked vault, not the person: a same-user process holding it, or reading keypaste's memory while it does, can seal any rules, and one that can type into Settings can confirm an Allow. Two copies that each made the key before merging settle to one key, and the other home's rules then read as the defaults until set again (fails closed). Deleting the file gives the defaults, which never give more.

**Status.** Implemented (unreleased). `v0.3.0` reads an unsealed `policy.toml` at `keypaste agent` start, rejects one other users can write on Linux and macOS (no such check on Windows), and a `KEYPASTE_HOME` in a synced directory lets another machine change this machine's grants.

**Tests.** `SealedAgentRulesTests`, `AgentRulesFileTests`, `ClientsVerbTests`, `AgentRulesViewModelTests`, `scripts/verify-policy-e2e.sh`.

### T-16 — The audit vocabulary is the only evidence a person was involved

**Asset.** The log's claim about who decided each release.

**Attacker.** A record or agent-facing text presenting an unasked release as a person's approval.

**Mitigation.** Each authority has its own method in the log: `prompt` for a person's answer, `allowed` for an Allow (URLs and shown usernames included), `blocked` for a Block, `rules-changed` for a rules change, each naming the category, and agent-facing text uses the same words. An authorized value too large to send is `undeliverable` with the authority that allowed it, decided on the reply as sent. Older methods, `v0.3.0`'s `policy` included, are still read as released by rule.

**Limit.** Editing the vocabulary after writing breaks the chain unless the attacker repairs it too (T-5).

**Status.** Implemented (unreleased). `v0.3.0` records policy releases as `policy`.

**Tests.** `AuditLogTests`, `SessionAuthorityRecordsTests`, `ServerToolsTests`, `LogRowTests`, `LargeCredentialTests`.

### T-17 — A timing oracle over Allow

**Asset.** Which categories are set to Allow or Block.

**Attacker.** A client timing an immediate unasked response against a human-delayed one.

**Mitigation.** None. A Block refusal names no category.

**Limit.** Unmitigated: an artificial delay would slow an Allow while the next prompted request still showed the difference. The client already receives what it is allowed, so learning the rules gives it nothing asking would not.

**Status.** Implemented (unreleased) as described; `v0.3.0` has the same exposure over `policy.toml`.

**Tests.** No test.

### T-18 — Memory dumping

**Asset.** Plaintext in memory during vault reads, serialization and after release.

**Attacker.** A debugger, core dump, hibernation file or swap page, same-user access included.

**Mitigation.** Master passwords are read into clearable buffers, and derived key material is zeroed after use. The owner keeps no released value for a later request: Allow once keeps nothing and neither an Allow nor an always answer holds a value, so each release reads the vault again. The bridge holds no vault, only the plaintext it delivers. Crash reports carry no memory and keypaste never collects a dump. A locked Windows app holds only the sealed quick-unlock copy (T-44).

**Limit.** keypaste makes no claim of in-memory secrecy. Garbage collection can leave relocated copies; vault reads and MCP serialization create strings that cannot be cleared; entry resolution reads standard fields, passwords included, before approval, so a denial does not mean a secret was never read; the pipe and OS can leave copies in swap or dumps. `SecureString` is unused because it does not encrypt on Linux or macOS. Nothing bounds the lifetime of these copies, the client's copies or the credential itself.

**Status.** Implemented (unreleased). `v0.3.0` keeps each human-approved credential in a clearable buffer for its grant lifetime, at most `--max-ttl` (default 300 seconds), cleared on expiry, disconnect and disposal; stopping `keypaste agent` cannot revoke what a client already has.

**Tests.** `SecretHygieneTests` checks sent bytes for planted sentinels; nothing tests memory erasure.

### T-19 — Clipboard scraping

**Asset.** A value copied with `keypaste get` or the desktop's copy buttons.

**Attacker.** Another process reading the clipboard, a clipboard manager or history, or a remote session copying it elsewhere.

**Mitigation.** The bridge and `keypaste approve` never touch the clipboard. The CLI and the desktop clear a copied value after 20 seconds if the clipboard still holds it; the desktop also clears on lock, quit, Clear now and an unexpected UI-thread error. On Windows both opt out of Clipboard History and Cloud Clipboard. The desktop marks a secret as concealed and transient for macOS pasteboard managers and as a secret for KDE's Klipper.

**Limit.** Managers may ignore these markers, and none stops a process reading the clipboard; RDP, Citrix or VDI can copy it to another machine. The CLI's macOS and Linux copies carry no marker, and `xclip` or `wl-copy` can keep serving the value after keypaste exits. Clearing needs a surviving process, so a crash elsewhere, forced termination, power loss or logout can leave the value. History, pasted copies and memory copies cannot be erased. A paste into a desktop secret field leaves the clipboard as it was, so a master password copied from elsewhere stays there. Copied `keypaste run` commands hold no secret and stay.

**Status.** Published in v0.3.0 for `keypaste get`: the 20-second clear and the Windows opt-out. The desktop's clearing and markers are Implemented (unreleased).

**Tests.** `ClipboardClearTests`, `ClipboardCountdownTests`, `ClipboardWritesDoNotOutliveTheAppTests`, `WindowsClipboardWriterTests`, `SecretMarkerTests`. Unverified: absence from Win+V for the shipped binary, and whether a given manager honours the markers.

### T-20 — A stolen vault file

**Asset.** The vault's contents when the file is copied away.

**Attacker.** Someone holding a copy of the vault, guessing offline.

**Mitigation.** KDBX4 provides content encryption (AES-256 or ChaCha20, or Twofish in a vault another program made with it), Argon2 key derivation and HMAC integrity; keypaste vendors the format implementation and adds no cryptography. A keyfile or YubiKey adds a factor a copied file lacks (T-27, T-37).

**Limit.** Protection rests on the passphrase and KDF parameters; offline guessing has no rate limit, lockout or record, and keypaste cannot make up for a weak passphrase. A recovered passphrase exposes names and values, also from an older copy. The audit file stays plaintext, and names and arguments in it can be sensitive (T-5, T-9).

**Status.** Published in v0.3.0 (KDBX4 and Argon2). Twofish vaults are Implemented (unreleased).

**Tests.** `scripts/verify-keepassxc-compat.sh`, `scripts/verify-keepassxc-writeback.sh`, `WorkflowsTests`, `TwofishCipherTests`. None tests passphrase strength.

### T-21 — The released binary is not the source you read

**Asset.** That a download is its tagged source.

**Attacker.** A compromised Actions runner, action, R2 credential or bucket account substituting bytes; anyone replacing an archive together with its checksum.

**Mitigation.** `release.yml` tests the NativeAOT binaries it uploads, KeePassXC compatibility both ways included, refuses JIT fallbacks and stray managed files, checks the version against the tag and re-verifies checksums. Actions and tools are pinned, and releases include source. `publish-release.sh` publishes only to a positively verified empty destination. `v0.3.0` carries a Sigstore attestation over every asset, checkable with `gh attestation verify` and no GitHub account.

**Limit.** A checksum from the archive's own origin cannot detect replacement of both. The attestation does not detect a compromised workflow run, action or runner, which attests what it built. Binaries are unsigned and un-notarized, and builds are not reproducible. One third-party fleet builds and tests all platforms, so a fleet compromise can evade the checks, and the publication guard does not stop a compromised credential bypassing the pipeline. Building from source moves trust to the local build chain.

**Status.** Published in v0.3.0, the first advertised release with an attestation; `v0.2.0` and earlier have none. Desktop packages are unreleased (RELEASE owns their requirements).

**Tests.** `release.yml`'s own assertions, `scripts/verify-provenance.sh --selftest`, `scripts/verify-release-destination.sh`. None detects substitution by the infrastructure running them.

### T-22 — The desktop app's accessibility layer as a read path

**Asset.** Secrets typed into the desktop app.

**Attacker.** Another process querying AT-SPI or Windows UI Automation for a control's value or name.

**Mitigation.** Every field that takes a secret is a `MaskedInput`, never a `TextBox` or `TextBlock`, whose automation exposes text. It keeps characters in a clearable buffer, draws from the count and exposes only a fixed name and the mask length, so two secrets of one length look the same. A paste goes into a buffer of its own; one trailing line break is dropped, and anything else a keyboard cannot type is refused, never silently stripped. The app has no webview.

**Limit.** Keystrokes and pastes still arrive as immutable strings that cannot be wiped, and OS input handling, input methods and keyloggers are outside this control (T-18). A master password copied to paste it stays on the clipboard (T-19).

**Status.** Implemented (unreleased); the desktop app has no release.

**Tests.** `MaskedInputAutomationTests`, `SecretFieldAutomationTests`, `VaultAccessAutomationTests`. Unverified: the new-entry forms' one-time-code fields (`NewTotp`, `NewItemOneTimeCode`) and the save-value prompt's field (`ValueInput`) are `MaskedInput` but in no automation-surface test.

### T-23 — What auto-lock is for, and what it is not

**Asset.** An unlocked vault on an unattended desktop.

**Attacker.** Someone who walks up to the unattended computer.

**Mitigation.** The desktop locks after 30 minutes without input by default, settable up to 8 hours, never to "never". Optional minimize-lock and `Ctrl/Cmd+L` take the same path: dispose the vault, empty the screen, clear the copy. The deadline checks wall and monotonic clocks, so a machine suspended past it wakes locked; agent requests never count as activity. Every lock denies waiting requests as `vault-locked` and refuses an uncommitted release.

**Limit.** On Windows, the person's own lock, the idle lock, minimize-lock and closing into the tray can be undone with Windows Hello (T-44), so there they stop only someone who cannot pass Windows Hello, PIN included. Switching windows and macOS `Cmd+H` do not minimize. Locking cannot erase copies or undo a read (T-18). A `keypaste approve` has no idle lock and must be stopped.

**Status.** Implemented (unreleased); the desktop app has no release.

**Tests.** `AppVaultSessionTests`, `MinimizeLockTests`, `ActivityWatchTests`. Native minimize was observed by hand on Windows 10 and a Linux desktop and by an observer on Linux and macOS CI. Unverified: a minimize click and `Cmd+H` on a real macOS desktop.

### T-24 — `recent.toml` tells anything that can read `~/.keypaste` where your vaults are

**Asset.** The paths of the person's vaults and keyfiles.

**Attacker.** Anything that can read `~/.keypaste`, or a screenshot of the unlock screen.

**Mitigation.** The desktop records up to ten successfully opened vault paths, each with its last keyfile's path, and no names, counts, fingerprints or key material; the file is owner-only on Linux and macOS, and rows can be removed or the list cleared. With no recent vault it reads only KeePassXC's recent-database keys, writes none of KeePassXC's files and records none until opened. The app shows only file names.

**Limit.** Anyone who can read `~/.keypaste` learns those paths, keyfile locations included (T-27), as they can read the audit log. On Windows the file inherits profile permissions, by founder decision of 2026-10-22. KeePassXC's paths add nothing a same-user process could not read, but put them on the first screen, where a screenshot captures them.

**Status.** Implemented (unreleased); the desktop app has no release.

**Tests.** `RecentVaultsTests`, `SecretHygieneTests`, `KeePassXcDatabasesTests`.

### T-25 — A value on screen, because somebody asked to see it

**Asset.** A value the person revealed: a project value, an entry's password, custom field, TOTP code or past password, a generated password or a new scoped token.

**Attacker.** People nearby, screenshots, recordings, screen sharing and remote desktops, and someone who walks up after the person looks away.

**Mitigation.** A reveal is a toggle, so switch control, a screen reader's single activation and people who cannot hold a key can use it. One value shows at a time, for at most 30 seconds, and hides when pressed again, when its row, entry or place changes, on a merge or reload, when a menu opens over it, and when the window loses focus, minimizes, goes to the tray or locks. The value is drawn by a control with no automation entry, so nothing on the accessibility surface carries it.

**Limit.** The value is readable for up to 30 seconds from one click, and hiding it cannot erase a capture. Drawing creates an immutable string, held while it shows (T-18).

**Status.** Implemented (unreleased); the desktop app has no release.

**Tests.** `RevealButtonTests`, `DrawnRevealTests`, `RevealedValueTests`, `HistoryRevealAutomationTests`, `CurrentPasswordRevealAutomationTests`.

### T-26 — Restoring a backup while the app is locked

**Asset.** The vault's newer state, and the password typed to check a backup.

**Attacker.** Someone who knows an earlier master password and can write to the vault's directory; a file swapped in between check and restore.

**Mitigation.** The unlock screen restores a whole-vault backup after opening it with the password (and keyfile) it was made under; it shows when it was taken, its counts and the file it replaces, never a name or value. The restore accepts only the checked bytes, so a file swapped in afterwards is refused, and a write between re-read and rename is detected. The password is zeroed on every outcome. The replaced vault is kept as a backup first.

**Limit.** Someone who knows an earlier password and can write the directory can roll the vault back. They could already read that copy, and the replaced vault is kept, so the loss is the newer state's availability, not disclosure; deleting old copies after a password change closes it. `keypaste access` keeps one more copy under the old credentials and says so; saves prune past five. Detection is not a lock. KeePassXC and a running `keypaste approve` do not notice a restore.

**Status.** Implemented (unreleased); not in v0.3.0.

**Tests.** `VaultRestoreTests`, `RestoreBackupTests`, `SecretHygieneTests`, `BackupTests`.

### T-27 — Naming a keyfile tells the machine where the second factor is

**Asset.** The keyfile's location.

**Attacker.** Code running as the user reading the process table, shell history, environment, profiles, MCP configuration or `recent.toml`.

**Mitigation.** `--keyfile <path>` and `KEYPASTE_KEYFILE` carry a location, never key material. The CLI records nothing about which keyfile a vault uses; the desktop remembers only where it is (T-24). `keypaste access` takes a new keyfile only from `--new-keyfile`, so a variable left in a profile cannot become the new key.

**Limit.** All those places are readable by anything running as the user, which learns where to look, as it could by watching which file keypaste opened; `--vault` has the same exposure. A keyfile protects against someone holding the vault and not the keyfile (a stolen backup, a synced folder, a lost disk), not against someone executing on the unlocked machine. A lost keyfile cannot be reconstructed.

**Status.** Implemented (unreleased); `v0.3.0` has no keyfile support.

**Tests.** `KeyfileOptionTests`, `AccessCommandTests`, `UnlockKeyfileTests`, `scripts/verify-keepassxc-keyfile.sh`.

### T-28 — A keyfile that is any file at all is one edit from losing the vault

**Asset.** Access to a vault keyed by an arbitrary file.

**Attacker.** No attacker: an edit, a re-encoding or a sync tool rewriting the file.

**Mitigation.** keypaste opens all four KeePass keyfile forms, because KeePassXC wrote all four: XML, 32 raw bytes, 64 hex characters, and any other file keyed by the hash of its contents. For that last form it warns on every open (CLI on stderr, never stdout; the desktop in Recommendations) and never creates it: the only keyfile keypaste writes is a KeePass XML keyfile the desktop makes on request, never over an existing file. A build that cannot read XML keyfiles refuses them rather than key the vault with the file's hash.

**Limit.** For the fourth form, any change to the file's bytes loses the vault for good, with no recovery but its exact former contents; an inherited vault keeps that form across a password change. A file of exactly 32 bytes or 64 hex characters is read as key material, by keypaste and KeePassXC alike, without a warning. A keyfile is not a password: anyone who can read it has that factor.

**Status.** Implemented (unreleased); `v0.3.0` has no keyfile support.

**Tests.** `VaultKeyfileTests`, `scripts/verify-keepassxc-keyfile.sh`.

### T-29 — Who holds a vault

**Asset.** One answer per vault: which snapshot and lock agents meet.

**Attacker.** A second owner process, a second app start, a path handed to the app, a CLI verb saving behind the owner, or another local user taking the app's pipe name.

**Mitigation.** The desktop and `keypaste approve` each take an exclusive claim on the vault before its password is read; a second owner is refused naming the holder, and a crash frees the claim. The app runs once per user and home: a second start only shows its window and may hand it a path, which only selects what the unlock screen offers, is shown in full if new, and is ignored while a password is typed; on Windows a path not on a drive letter is refused unread, since touching a server sends it the person's credentials. Every saving CLI verb takes the claim, so under a held vault `access`, `import`, `token` and `share` are refused and an edit goes to the holder (T-43); reading verbs take none.

**Limit.** A same-user process can make the window show and put a file named like the person's vault on the unlock screen for them to type into; the full-path line is the only protection. Another local user can take the app's pipe name or socket first, and a second start then says on stderr that keypaste did not answer, which a desktop launch does not show. A home where `app.lock` cannot be written runs without the claim. A process under another `KEYPASTE_HOME` runs beside this one. On Windows two owners of different vaults can share an explicit approver name, though a bridge reaching the wrong one is refused. KeePassXC, or keypaste under another home, can save behind the holder, which then refuses agents as `vault-changed` until merged, reloaded or restarted; `keypaste approve` does not yet merge (planned, step 1.4b4), and it refuses to save a rules change after another program changed the file.

**Status.** Implemented (unreleased). In `v0.3.0` there is no claim, and the desktop, `keypaste agent` and `keypaste run` each open the vault on their own.

**Tests.** `VaultClaimTests`, `SessionOwnershipTests`, `SessionAuthorityTests`, `HeldSavesTests`, `AppClaimTests`. `keypaste approve`'s refusal to save a rules change over another program's save: no test. Unverified: cross-user refusal on the app's pipe (T-10).

### T-30 — A run asks the session for a whole env set

**Asset.** A project's whole env set, released into a child process's environment.

**Attacker.** Anything running as the user that can start `keypaste run --session`, an agent with a shell included.

**Mitigation.** A run is admitted only from a connection attached to the owner's current session and released only after the person allows it in the app or at `keypaste approve`, Allow once only (T-34), or on macOS under the launch pass the app's own Run confirmation issued (T-42). No agent rule releases it, and a protected profile, or a set holding an entry in a protected environment, is asked about every time. The prompt shows the project, variables, entries, command and directory, never a value, and refuses what it cannot show whole. The owner records every run request and refuses an unrecordable release (T-6); the app's own Run on Windows and Linux records before its terminal starts.

**Limit.** The command and directory are the runner's claim: a same-user program can claim one command and start another, so the prompt catches a run the person can see is wrong, not a same-user adversary. Allow only a run you started. The runner holds values as strings it cannot zero, and the child keeps them after a lock. The app's Run card reads its command from `~/.keypaste/projects.json`, which any same-user program can edit, so read it before Start. The standalone `keypaste run` opens the vault itself and is not audited.

**Status.** Implemented (unreleased); `v0.3.0` has only the standalone `keypaste run`.

**Tests.** `SessionAuthorityEnvTests`, `ApproverEnvProtocolTests`, `EnvReleasePromptTests`, `DesktopEnvApprovalTests`, `RunSessionTests`.

### T-31 — A committed reference file chooses what a run injects

**Asset.** Which values a run injects, and where they are sent.

**Attacker.** Whoever controls a cloned repository's `.env.keypaste`, which can name any project and add literals such as `HTTPS_PROXY` or `NODE_OPTIONS` that send injected secrets elsewhere.

**Mitigation.** `run` uses `./.env.keypaste` on its own only when `projects.json` maps the directory to a project and the file names that project alone; anything else needs `--env-file`, the person naming the file. In reference mode `run` names the file, project and profile it resolved, lists every literal before opening the vault, refuses a literal it cannot show whole, and a `--session` prompt shows the final names and literals.

**Limit.** A file the person names, or one naming its mapped project, can still add literals; `run` lists them, and the person must read them.

**Status.** Implemented (unreleased); not in v0.3.0.

**Tests.** `RunCommandTests`, `RunSessionTests`, `ProjectInferenceTests`, `EnvReferenceFileTests`.

### T-32 — A scoped token is a bearer credential

**Asset.** The env sets a scoped token covers.

**Attacker.** Anyone who copies a token from a CI log or process list, or holds a token bundle and its token.

**Mitigation.** Tokens are inject-only for `keypaste run`, scoped to project, profile and keys, and always expire. The owner records every token request before replying. `KEYPASTE_TOKEN` is stripped from the child, and a token on the command line prints a warning. A protected set is refused unless the token allows prod, then asked about live, and never goes into a bundle. A revocation survives merges: it writes a newer record with no verifier, which KeePassXC's default merge carries over an older copy, a tombstone reads as revoked, and keypaste's merge drops any token record either copy has a tombstone for.

**Limit.** A bundle cannot be revoked, and its expiry is only keypaste's check against the opening machine's clock, so whoever holds the bundle and its token can read its values indefinitely: rotate each value once a bundle is retired or may have leaked. A copy whose token record someone edits after the revocation (in KeePassXC, or by restoring the verifier from history) is newer and makes the token work again until expiry. A token revoked by an earlier keypaste, or deleted in KeePassXC, left only a tombstone, which KeePassXC's merge does not carry, so a copy that never got it keeps the token working until expiry or a keypaste merge. A revoked record stays up to 365 days as an entry KeePassXC shows, its verifier (not the token) in history. `keypaste approve` refuses token requests after another program saves, until restarted.

**Status.** Implemented (unreleased); not in v0.3.0.

**Tests.** `TokenStoreTests`, `TokenScopeTests`, `TokenBundleTests`, `SessionAuthorityTokenTests`, `RunTokenTests`.

### T-33 — Whoever holds a share link can open it

**Asset.** The field or login a share link carries.

**Attacker.** Anyone holding the link, a chat unfurler or forwarded message included; whoever controls keypaste.com's Worker, Cloudflare account or deploying branch.

**Mitigation.** `keypaste share` and the app encrypt one field, or a login's username, password and URL, on this machine and upload only ciphertext; the key is in the link's fragment. Links have a view limit and expiry, open by `POST` behind a metadata-only `GET`, can carry a separately sent passphrase of at least 20 characters, can be revoked, and say when a view was spent. keypaste.com serves them only once `SHARE_ENABLED` is set.

**Limit.** Anyone holding the link can open it until its views or time run out. The metadata `GET` carries a passphrase check, so the holder can test guesses offline at PBKDF2-SHA256 (600,000) cost without spending a view; only a generated passphrase (`keypaste generate --words 6`) protects an intercepted link. End to end holds only while keypaste.com serves an honest viewer: whoever controls the Worker, the account or the branch can serve one that sends the key elsewhere, undetectably. A link on another host (`--endpoint`) trusts that host the same way.

**Status.** Implemented (unreleased); not in v0.3.0.

**Tests.** `ShareCryptoTests`, `ShareServiceTests`, `ShareClientTests`, `ShareVerbTests`, `site/test`.

### T-34 — A run's request is the runner's claim

**Asset.** Release of an env set to a run.

**Attacker.** A same-user process repeating a command a person once allowed.

**Mitigation.** Every `keypaste run --session` is a new connection offered Deny and Allow once only, as the founder decided on 2026-11-07; no rule or remembered answer releases it, since a run names no agent and nobody the owner trusts resolves its command.

**Limit.** What a run says it will start is its claim (T-30).

**Status.** Implemented (unreleased); not in v0.3.0.

**Tests.** `SessionAuthorityEnvTests`, `TerminalApprovalChannelTests`.

### T-35 — A run keeps values out of the result, not out of the agent's reach

**Asset.** Values given to a command an agent runs with the `run` tool.

**Attacker.** An agent that names the command and can often edit what it runs (a package script, a source file).

**Mitigation.** The tool exists only under `--allow-run`, and an agent runs nothing until the person turns run on for its label; an unlabelled bridge never runs (T-15). Every command is asked each time, Allow once only, showing the program, argv, resolved directory, variable names and reason. A set holding a Blocked value is refused unasked, and exposure, protected profiles and refusals of `PATH` and `KEYPASTE_*` apply. The owner records every run before values leave. Output is scrubbed of each value and each part filled into one by placeholder.

**Limit.** An approved run can reveal a value in a form the scrubber misses (base64, hex, reversed, split, a substring, `curl -v`'s `Authorization: Basic`), write it to a file, send it over the network or signal it by exit code and timing. While the child runs, any same-user process can read its environment, so values given only through commands give no confidentiality against an agent with its own shell. A missed value lands in the client's transcript and with the model provider. Containment has gaps: on Windows a grandchild started before the child joins its Job Object escapes, macOS kills only the tree it finds, and the directory can be swapped after approval. For a client without a shell, `run` adds approved command execution. A nested `keypaste run --session` asks separately.

**Status.** Implemented (unreleased); not in v0.3.0.

**Tests.** `RunRequestRulesTests`, `OutputScrubberTests`, `CapturedLaunchTests`, `SessionAuthorityRunTests`, `RunToolTests`.

### T-36 — An agent's settings bind only the bridge that names them

**Asset.** Each agent's rules, run permission and remembered answers.

**Attacker.** An agent that can edit its client's configuration (`.mcp.json`); a same-user process running `keypaste use` or `keypaste clients`.

**Mitigation.** A label gives only what the person confirmed for it, an unlabelled bridge runs nothing, and every run is asked (T-35). An unreadable rules file refuses every request, and an unsealed one reads as the defaults (T-15). The Agents screen says each agent's settings follow the name its connection was given, not the name it calls itself, and `keypaste clients` lists the rules.

**Limit.** Such an agent can take another agent's label and with it that agent's run, Allows and Always allow answers, change its `--vault`, or drop an `--expose` the person added. A same-user process can point every bridge without `--vault` at another vault with `keypaste use`, or turn an agent's run off. The "in use now" mark is a claim any program can make. No setting covers `keypaste run --session` from an agent's own shell.

**Status.** Implemented (unreleased). In `v0.3.0` the label selects `policy.toml` rules, with the same exposure.

**Tests.** `AgentRulesFileTests`, `SealedAgentRulesTests`, `SessionAuthorityRunTests`, `RememberedAnswerRequestTests`, `ClientsVerbTests`.

### T-37 — A YubiKey protects a copied vault, not an unlocked one, and a lost key locks it

**Asset.** A vault that needs a YubiKey slot programmed for HMAC-SHA1 challenge-response.

**Attacker.** Someone with a copy of the vault; malware driving a plugged-in key; losing the key.

**Mitigation.** The desktop derives the key as KeePassXC does, so either opens the other's vault. Every save draws a fresh challenge, so the key is asked on every open and save, and a copied file needs the password and the key's secret, which never leaves the key. Settings and the create form warn about loss first.

**Limit.** Nothing against a same-user process while the vault is open: the derived key and the key's last answer stay in memory until the lock. Malware that can drive the plugged-in key can get its answer for the current file. A lost or broken key locks the vault for good: only a second key programmed beforehand with the same secret opens it, keypaste cannot copy a secret out of a key, and backups made while it was required need it too. The CLI and `keypaste approve` cannot open such a vault.

**Status.** Implemented (unreleased); the desktop app has no release.

**Tests.** `KeePassXcChallengeResponseTests`, `HardwareKeyVaultTests`, `UnlockHardwareKeyTests`, `HardwareKeySessionTests`. Unverified: nothing has run against a physical YubiKey (real-device unlock, save and reopen, KeePassXC opening keypaste's vault, touch, timeout and slot errors).

### T-38 — One secret shared by two environments

**Asset.** A value one environment should not reach.

**Attacker.** An agent exposed to one environment of a project, such as `tag:env:billing:dev`.

**Mitigation.** An entry tagged into two environments, one of them protected, is asked about live every time. Its custom fields follow the project-variable rule whichever environment a request comes through.

**Limit.** Exposure and protection are per entry, not per environment, so a dev agent can ask for a value production also uses. keypaste does not split an entry between environments; a value that must stay out of one belongs on its own entry.

**Status.** Implemented (unreleased); not in v0.3.0.

**Tests.** `EntryExposureTests`, `SessionAuthorityRunTests`, `SessionAuthorityTokenTests`.

### T-39 — The terminal's `keypaste` is whatever its PATH entry or link names

**Asset.** The program a terminal runs as `keypaste`, into which a person types a master password.

**Attacker.** Whoever can change what a PATH entry or link names, or write the bundle a link points into.

**Mitigation.** Each desktop install puts its own `keypaste` on PATH: the MSI's folder goes last, the Linux script quotes the AppImage's path, and on macOS a fixed root program behind the administrator dialog makes the link, taking paths only as arguments. A file keypaste did not write is replaced only after the person confirms, and is kept as `keypaste.keypaste-backup`. macOS Settings names an earlier `keypaste` in `~/.local/bin` or Homebrew that is not this bundle's. The unpublished Claude Desktop extension starts the first `keypaste` in fixed places and absolute PATH entries, skipping one another account owns.

**Limit.** Known limit, accepted by the founder on 2026-10-10: the macOS link is on every account's PATH and points into a bundle the installing account can write, so that account chooses what `keypaste` runs for every account on the Mac, root included; it is the only one that crosses accounts. The MSI's folder is the account's to write and makes the app's executable and libraries findable. An earlier `keypaste` in other places goes unseen. Whoever can place a `keypaste` earlier where root or the person owns, or earlier on PATH, chooses the bridge Claude Desktop starts. Uninstalling leaves the Linux script and macOS link naming a missing path until something is put there.

**Status.** Implemented (unreleased); the desktop packages have no release.

**Tests.** `LinuxCliLinkTests`, `MacCliLinkTests`, `CliLinkSettingsTests`, `ClaudeDesktopExtensionTests`, `scripts/verify-macos-cli-link.sh`. Unverified: the administrator dialog itself, and the extension's macOS and Windows places.

### T-40 — Setup edits another program's configuration file and starts its command

**Asset.** AI tools' configuration files, which hold other servers and often their tokens (`~/.claude.json` also holds Claude Code's sign-in), and the commands setup starts.

**Attacker.** A careless edit or a crash mid-write; a same-named program in the working directory or an earlier PATH entry; a program registered for `cursor:` or `vscode:` links.

**Mitigation.** No vault secret enters these files (law 3.4), only a path and flags. `keypaste setup` and Connect show each edit and its backup for confirmation, change only keypaste's member, leave alone a file that does not parse or changed since read, replace it by one rename and keep the first original owner-only. The Agents screen only reads configurations, keeping whether keypaste is there. Tools are found only in absolute PATH entries and documented install folders, never through `cmd.exe`. Cursor's and VS Code's install links make the tool write its own file.

**Limit.** A tool writing its file between keypaste's read and rename loses that write, and a tool holding the file in memory can overwrite keypaste's entry. The backup keeps the original's tokens as long as the person keeps it. A writable earlier PATH directory can still supply the program. An install link names the person's home and vault paths wherever it is pasted, Copy link is not cleared by the countdown, and whichever program handles the link's scheme receives it.

**Status.** Implemented (unreleased). `v0.3.0`'s `keypaste setup` runs Claude Code's and Codex's `mcp add` and prints configuration for Cursor and Claude Desktop.

**Tests.** `McpConfigFileTests`, `McpClientConfigTests`, `McpClientProgramTests`, `SetupVerbTests`, `McpInstallLinkTests`. VS Code's, Claude Desktop's and, on Windows, Cursor's registrations are checked at file level only.

### T-41 — An agent asks for more access

**Asset.** The agent rules.

**Attacker.** An agent with a shell running `keypaste clients`, or wearing the person down with requests.

**Mitigation.** No tool lets an agent change what it gets, and a request names one value. `keypaste clients` can only turn run on or off, narrow an agent's rules, forget remembered answers or remove an agent, so it releases nothing more unasked, and with nothing holding the vault it can only narrow (T-15). An Allow takes the app and the person's confirmation. T-11 bounds repeated requests. Every rules change, made or refused, is the owner's `access_change` line naming the surface that asked.

**Limit.** A person who allows without reading gives the agent that value, and one who confirms an Allow gives every agent every value of that category. A program that can type into the app as the person can confirm an Allow (outside scope).

**Status.** Implemented (unreleased); not in v0.3.0.

**Tests.** `ClientsVerbTests`, `SealedAgentRulesTests`, `AgentRulesViewModelTests`, `scripts/verify-policy-e2e.sh`.

### T-42 — A launch pass is a bearer for one run

**Asset.** The env set a macOS Run or Open terminal confirmed.

**Attacker.** A same-user process that reads the launch request file or the run's environment and presents the pass first.

**Mitigation.** On macOS the app hands Terminal no value: Start issues a 32-byte launch pass held only in the owner's memory and written with the run's arguments into an owner-only request file, which the bundle's launcher claims and deletes before starting `keypaste run --session` with the pass in its environment. A pass works once, for two minutes, within the unlock that issued it, and releases only the exact request the card showed. It is never on a command line and is stripped from every child. A Run is refused before the card when its set names more than 62 KiB of entries, the audit log will not open, or the launch endpoint or temporary folder is missing.

**Limit.** Until the run presents it, a same-user process can present the pass first and receive the set; the person's window then says it was used, and Activity shows both requests. Whoever can write the bundle (T-39) can change the launcher, as they can the app. Terminal keeps the app's other environment variables for its windows. Only Terminal is supported. Windows and Linux hand the set to their terminal directly.

**Status.** Implemented (unreleased); not in v0.3.0. Gatekeeper refuses an unsigned quarantined bundle's launcher, and Terminal runs it once the quarantine is cleared. Unverified: a signed download's launcher (after macOS signing, step 3.5b) and a Run from an installed app through real Terminal.

**Tests.** `LaunchPassesTests`, `TerminalRequestsTests`, `MacTerminalLauncherTests`, `EnvLaunchTests`, `SessionAuthorityEnvTests`.

### T-43 — A terminal asks the owner to change the vault

**Asset.** The vault's contents, and what a planted value or tag later releases.

**Attacker.** A same-user program, an agent with a shell included, asking the owner to change the vault without the master password: planting a value such as a variable holding `{PASSWORD}` (T-8), removing an entry, or tagging one into a project so its fields become project variables an Allow releases (T-13).

**Mitigation.** While a vault is held, the CLI's editing verbs send their edit to the holder, which changes nothing until the person allows it in the app's prompt window (Deny focused, Allow armed after a second) or with `y` at `keypaste approve` (T-2). The prompt names the command, entry and change, never a value, says any program could have typed it, and names any placeholder a value holds. The holder checks the edit before asking and again before writing, refuses any other change, writes nothing while its vault differs from its file or after the unlock it was asked under, and takes back a failed save. Edits share the prompt slot and a refusal holds the next back a minute (T-11). Each is recorded without values. The verb reads no master password, and `access`, `import`, `token` and `share` stay refused.

**Limit.** A person who allows without reading gets the change, and a planted value is the terminal's own. A placeholder is named by the variable's own text, so one filling from a field that itself holds `{PASSWORD}` is named as that field. A script piping only a master password to a verb that reads one value sends it as that value. The cooldown holds back the person's own next edit too. A form open on the same entry saves over the edit, which stays in history. A save queued behind another at a lock is refused.

**Status.** Implemented (unreleased); not in v0.3.0.

**Tests.** `TerminalEditsTests`, `ProjectTagChangeTests`, `SessionAuthorityEditTests`, `RoutedEditTests`, `ApproveCommandTests`.

### T-44 — Windows Hello stands in for the master password after a lock

**Asset.** The vault, reopened after a lock without the master password.

**Attacker.** Anyone who can pass Windows Hello as this user while the app runs; a same-user program that can ask Windows Hello to sign and read the app's memory.

**Mitigation.** After each password unlock the app asks Windows Hello to keep it, as KeePassXC 2.8 does: the composite key's parts (the password's SHA-256, the keyfile's key and a YubiKey's slot, never its answer) are sealed with AES-256-GCM under a key from a Windows Hello signature, in memory only, never on disk. Windows Hello asks by face, finger or PIN on every keep and resume, and its key stays in Windows. Only a lock the person made, the idle lock, minimize-lock and closing into the tray allow a resume; quitting, `keypaste lock`, another vault, an access change or a key change elsewhere drop the copy. A resume asks a YubiKey for its touch again.

**Limit.** Anyone who passes Windows Hello as this user, the Windows PIN included, can reopen the vault after those locks. A same-user program should be assumed able to obtain a signature, which shows the person a prompt, and with it and a read of the app's memory it has the parts, which open this vault and every copy under the same password and keyfile in any KeePass reader, with no keyfile on disk needed. A kept copy has no maximum age. keypaste finds Windows' prompt by window class, which a later Windows can change. The CLI cannot use Windows Hello.

**Status.** Implemented (unreleased), Windows only; the desktop app has no release.

**Tests.** `VaultKeyPartsTests`, `QuickUnlockTests`, `UnlockQuickUnlockTests`, `WindowsHelloTests`. Unverified: no test has met a real Windows Hello (making the key, the prompt's focus, cancelling, the PIN, a YubiKey vault resumed).

### T-48 — The browser relay holds released passwords in plaintext, and anything can start it, or ask to pair

**Asset.** Logins released to a browser, and the pairing that admits one.

**Attacker.** A same-user process starting the relay with a browser's arguments, naming any of the three builds of keypaste's extension, or speaking the pipe as a browser.

**Mitigation.** The relay ends the extension's encrypted session, KeePassXC-Browser's protocol, itself, keeping libsodium out of the processes that hold the vault; it holds no vault and cannot reach vault access or the audit log. A browser's connection may only pair, check a pairing, fill and lock. The relay sends typed requests, never the extension's JSON, and with a fill only the pairing a check proved, so the extension's other pairings go no further. It refuses a reused nonce and logs nothing. Pairing is answered only while the person armed it in Settings or with `keypaste approve --pair-browser`, for two minutes and one confirmation; any other request is refused, recorded and told to the person. The confirmation names the build the browser claimed and says any program can claim it.

**Limit.** A same-user process can read the relay's memory, where decoded logins stay as strings until collected, and can start keypaste naming the extension or speak the pipe as a browser, obtaining what pairing and fill allow; a program asking first inside an armed window is still shown to the person, who can deny it. The relay believes the browser about which extension started it.

**Status.** Implemented (unreleased); not in v0.3.0. Fill is not implemented yet: the owner refuses every fill (planned, step 8.3a2).

**Tests.** `RelayTests`, `BrowserCallerTests`, `SessionAuthorityBrowserTests`, `ApproverBrowserProtocolTests`, and `BrowserHostTests`, where a fake browser starts the relay through the manifest. `verify-extension-load.sh` has Chromium and Firefox start the relay for keypaste's extension and pair it. Unverified: a fill in a real browser (planned, step 8.3a3).

### T-49 — Another program answers keypaste's host name

**Asset.** Which program a browser starts for keypaste's extension, and the pairing key the extension sends it.

**Attacker.** A same-user program that writes a manifest for `com.keypaste.browser` at the user-level place each browser reads before a system one.

**Mitigation.** keypaste has its own host name and answers only its own extension, so KeePassXC, which writes `org.keepassxc.keepassxc_browser`, never takes a browser from keypaste and keypaste never takes one from it. Settings › Browser fill and `keypaste browser` name the program each browser starts, another program's path included; Settings asks before turning on replaces another program's manifest, and turning off removes only a manifest keypaste wrote.

**Limit.** keypaste does not watch the manifests, so one another program writes is seen only when the status is read, and until then that program receives the extension's requests and its pairing key (T-50).

**Status.** Implemented (unreleased); not in v0.3.0.

**Tests.** `BrowserHostsTests`, `BrowserVerbTests`, `BrowserHostSettingsTests`, `BrowserHostTests`.

### T-50 — A pairing is a bearer key

**Asset.** A browser pairing, which admits fill requests.

**Attacker.** Whoever reads the browser profile, where the extension keeps the key it proves a pairing with, and can reach the owner's pipe; anyone who can write the vault.

**Mitigation.** The vault keeps only the key's SHA-256, so a vault copy gives no key, and no line, prompt or output holds it. A pairing is bound to the build that paired, and is proven only against the vault as saved. Settings lists each pairing with its last use, and Remove purges it, survives merges and unbinds its connections. A pairing releases nothing by itself: each fill is to be asked about or allowed for one exact origin (planned, step 8.3a2).

**Limit.** A same-user process can read the profile's key and present it under the build it claims (T-48), obtaining what a fill releases; KeePassXC's protocol has only that key as proof. Anyone who can write the vault can add a record, though one whose key they lack proves nothing. Last use is kept in memory and restarts with each owner start.

**Status.** Implemented (unreleased); not in v0.3.0.

**Tests.** `SessionBrowsersTests`, `VaultMergeTests`, `SessionAuthorityBrowserTests`, `BrowserPairingTests`, `BrowserHostTests`.

### T-51 — A notification is read by more than the person

**Asset.** What a system notification reveals, and what pressing it does.

**Attacker.** The lock screen, notification history and programs that read it; an agent timing interruptions; a late press.

**Mitigation.** A notification can carry only fixed words, with no place for an entry, field, group, value, vault, agent or reason. Its one action, never an Allow, brings the prompt forward (disarmed, Deny focused) or opens the unlock screen, and answers nothing. A decision, lock, unlock or quit removes it, and a later press reaches nothing. One posts only while no keypaste window has focus, at most once every 30 seconds, after the prompt is up, so a failure changes nothing.

**Limit.** Anyone who sees it learns keypaste is here and something asked then. A removal the system ignores leaves the words in history. A press of one left by a run that died can start keypaste on Windows and macOS; on Linux nothing clears it before its 45-second expiry, or until dismissed where expiry is ignored. A Windows toast's press and its stay in Notification Center are unproven. Focus and do-not-disturb modes can hide or delay a notice, which the app cannot see; the prompt still appears.

**Status.** Implemented (unreleased); the desktop app has no release.

**Tests.** `SystemNotificationTests`, `LockedNoticeTests`, `LinuxNotificationsTests`, `WindowsNotificationsTests`. Unverified: no notification has been shown to a person or posted to a real notification surface.

### T-52 — A culture changes what a rule means

**Asset.** That a name means the same to the app as to the CLI: protected environments, pipe names, audit hashes, files read back.

**Attacker.** No attacker: the desktop runs in the person's culture and the CLI, bridge and relay run invariant, so casing, calendars and comparisons can differ.

**Mitigation.** Every culture-sensitive call in Core, Infrastructure and the desktop states its culture, and the build fails otherwise; Core bans `ToUpperInvariant` in favor of one invariant casing; the vendored KeePassLib runs invariant. A person's culture reaches only time display, table sorting and the app's words. The audit log, its hash and access matching are ordinal and read the same everywhere.

**Limit.** Letters newer than the Unicode version .NET carries, such as U+A7CE, fold differently in processes whose ICU knows them, so the app and the CLI can disagree on whether a field rename is taken, and such a path can fail to find the owner's pipe. Avalonia, Yubico's SDK and Tmds.DBus were not audited; they handle no vault, audit or rule text.

**Status.** Implemented (unreleased).

**Tests.** `CultureAgreementTests`, run by `scripts/verify.sh` invariant and under `tr-TR`, `de-DE` and `ar-SA`.

### T-53 — What two entries of one name did is recorded under the name they share

**Asset.** Which of two same-named entries a release, share or record was about.

**Attacker.** No attacker needed: KeePass and KeePassXC allow two entries with one title in one group, and an agent may ask for that name.

**Mitigation.** The desktop acts on each entry by its UUID, and share and credential lines name the UUID beside the path. An agent's request that several visible entries answer to is put to the person as a picker showing each one's title, group, username and URL, never a value, with none picked; only the picked entry's field is read, while it still has the name shown, and nothing is released unasked. A picker over nine entries, a run naming such a path, a terminal verb and a typed name are refused. An entry's activity shows only lines naming its UUID.

**Limit.** Lines and Share links rows written before lines named UUIDs give only the path and count for neither entry. A release recorded before a pair existed shows on both as possibly either's.

**Status.** Implemented (unreleased); not in v0.3.0.

**Tests.** `SameNameItemsTests`, `ShareServiceTests`, `ItemPickerTests`, `DesktopApprovalTests`, `EntryActivityRowsTests`.

### T-54 — An agent asks the person to save a value

**Asset.** What the vault holds: a credential the person would not otherwise keep, or a working value replaced.

**Attacker.** An agent using `request_save` to have a value saved at a place it names (an entry's password or custom field, a new entry, or a project variable), persuading through its reason or looping the prompt.

**Mitigation.** The person types, pastes or generates the value and saves with Save, armed a second after the prompt is up, with Cancel focused, or with `y` at `keypaste approve`. A replace needs confirmation, and a value that appeared meanwhile is not overwritten. The agent learns only whether and where it was saved, and the owner's line names the place, never the value. A place outside the bridge's view is refused unasked (T-4), a refusal holds the place back a minute, and the prompt takes the one shared slot (T-11).

**Limit.** A persuaded person has stored the value, and the agent's later request for it meets its category's rule, which an Allow releases unasked (T-14). The reason is the agent's claim (T-2).

**Status.** Implemented (unreleased); not in v0.3.0.

**Tests.** `ValueSaveRequestTests`, `SaveValueWindowTests`, `TerminalApprovalChannelTests`, `ServerToolsTests`.
