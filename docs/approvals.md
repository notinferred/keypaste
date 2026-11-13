# Approving an agent's request

> This guide describes the source build headed for keypaste 1.0.0, which is not yet published. In the published v0.3.0 the approver is `keypaste agent`, the bridge is the separate `keypaste-mcp` binary, the prompt asks `Approve? [y/N]` and a yes keeps a timed grant, `policy.toml` rules release without asking, and there is no desktop prompt. [FEATURES](FEATURES.md) lists what each version has.

This guide owns the approval prompt, at the terminal and in the desktop app, and the audit log. What an agent gets without asking is set in [agent settings](policy.md); connecting a tool is [Connecting keypaste to Claude](mcp-setup.md).

<a id="the-short-version"></a>

```
  your terminal or the app             your MCP client
       │                                   │
  keypaste approve ◄─── local pipe ────  keypaste mcp
   • holds the unlocked vault             • checks what it can see
   • asks you                             • refuses anything out of scope
   • writes the audit line                • returns one field
```

Your MCP client starts `keypaste mcp`, which holds no vault. You hold the vault unlocked, either in the desktop app or with `keypaste approve` in a terminal you opened, and that process decides each request under your agent settings: it asks you, releases without asking, or refuses. An agent cannot trigger a master-password prompt, so you are never taught to trust a password window another program could imitate.

One vault has one holder. While the app has a vault unlocked, `keypaste approve` on that vault is refused, naming the app, and the app asks about that vault's requests itself.

## Starting the approver

```sh
keypaste approve --vault ~/vaults/personal.kdbx
```

It asks for your master password, then waits:

```
Master password:
keypaste: watching /home/you/vaults/personal.kdbx
keypaste: listening on keypaste-vault-9f3a1c02b7d54e60 for unlock 5d0c8e1a4b7f2c936e0a1d4b8c7f3e21, 45 seconds to answer.
keypaste: every value waits for you to say yes; agents see each item's URL, and its username unless Settings hides it. Press Ctrl+C to stop.
```

When your settings allow a kind of value, a line before `listening` names it, and a `~/.keypaste/policy.toml` left from v0.3.0 is named as no longer read. Leave it running in its own terminal. Ctrl+C, SIGTERM and closing the terminal lock the vault again, and a request waiting at the prompt is withdrawn and denied as `vault-locked`.

| Option | What it does |
|---|---|
| `--vault <path>` | Which vault to unlock. Or set `KEYPASTE_VAULT`; with neither, the vault chosen in the app or with `keypaste use`. |
| `--keyfile <path>` | The vault's key file, if it has one. Or set `KEYPASTE_KEYFILE`. |
| `--approval-timeout <seconds>` | How long you get to answer. Default 45, range 5–55. |
| `--approver <name>` | Which pipe to listen on. Or set `KEYPASTE_APPROVER`. Needed only if you run two. |

The pipe name is derived per user and per vault, so the vault the bridge names must be the vault the approver holds.

## The prompt

At the terminal, `keypaste approve` draws the request between two rules: who is asking, the item and field, the agent's reason under a line saying the agent wrote it, and the choice line `[d] deny  [o] once  [a] always  [b] block` with a countdown. [The demo](demo.md#review-the-request) shows it exactly as the shipped binaries print it, which CI checks.

- `agent` is the name the connecting program gave itself, which nothing checks, then in brackets the label its bridge was started with (`--client-label`). Agent settings follow the label, never the name.
- `item` and `field` name the value in your vault. A `/` inside an item's title is shown as a space, so a title such as `../../prod/ROOT_TOKEN` cannot pass for another group path.
- The reason is the agent's text. keypaste removes control characters, line breaks, invisible characters and right-to-left controls and cuts it to 400 characters. It cannot change the prompt, its default or its deadline.

The answers:

- `o` allows this request once and keeps nothing.
- `a` (Always allow) and `b` (Always block) appear only for an agent whose bridge has a label. Each asks you to confirm with `y`, then keeps that answer for that agent and that item's field until you remove it on the agent's page in Agents or with `keypaste clients <agent> --forget <key>` ([agent settings](policy.md#one-agents-own-rules)). They are not offered once an agent has 128 kept answers and picks.
- Anything else, Enter included, is a no, and so is no answer within 45 seconds. The countdown shows the seconds left.

An allow counts only once the choices have been on screen for a second, and keys typed before they appeared are discarded, so a key meant for the last prompt cannot answer this one.

When more than one item answers to the name an agent asked for, such as two logins for one site, you are asked to pick one: each shows its title, group, username and URL, none is picked for you, and only the picked item's field is sent.

<a id="approving-in-the-desktop-app"></a>

### In the desktop app

When the app has the vault unlocked, a request opens a keypaste prompt window over whatever you are doing, showing the same facts and a countdown, with the agent's reason under a line saying the agent wrote it. It offers Deny and Allow once, and for an agent whose bridge has a label, Always allow and Always block, each confirmed in the same window.

- Allow once works from a second after the prompt has focus. While another window has focus the prompt is drawn inactive and its allows stay disarmed; click into it, or press Show the prompt on Agents, and they work a second later.
- Focus starts on Deny, so Enter refuses. Deny, Escape, closing the window, 45 seconds without an answer, a lock, quitting and the client giving up each refuse.
- Agents lists the request under Waiting for you, with the agent, the item, the field and the seconds left, never the reason. You answer only in the prompt window.
- While keypaste is in the background, a request may also post a system notification that names no item, vault or agent; it never answers a request.

A request for an attachment, made by name with `request_attachment`, shows `attachment` and the name as its field, and Allow once releases that one attachment, at most 5 MiB.

## Repeat and waiting requests

Each request is asked about unless your agent settings or a kept answer decide it, or it is an item's URL, or its username while Agents see usernames is on. A release without asking shows no reason; the audit log keeps it ([THREATS](../THREATS.md) T-12). `keypaste approve` prints a line for it:

```
keypaste: released env/billing/.env to claude-code without asking (project variables allowed)
```

One prompt is shown at a time. A request that arrives meanwhile waits its turn, at most three for each agent; a fourth gets `BUSY`, and so does a call beyond what one bridge carries at once. A request whose client gives up, and every waiting request when the vault locks, leaves the queue.

## When you say no

An explicit refusal tells the agent not to retry, and the same field is refused for a minute without asking again. A timeout allows a retry, because nobody decided.

There is no pause button for an agent that keeps asking; your settings are the pause. Block a kind of value and every request for it is refused before anything in the vault is read, and `keypaste clients <agent> --run off` stops its commands the same way.

<a id="runs-that-ask-for-a-projects-variables"></a>

## Runs

`keypaste run --session <project> -- <command>` asks the process holding the vault for the project's whole set instead of opening the vault itself ([replace-dotenv](replace-dotenv.md#run-your-app)). An agent's `run` tool, through a bridge started with `--allow-run`, asks the same way ([mcp-setup](mcp-setup.md#running-a-command-with-secrets---allow-run)).

The prompt names the project, the variable names, the item each value comes from, the command and the directory, and for an agent's run the exact program and command line and the agent's reason; a loader such as `NODE_OPTIONS` is marked as changing how programs start. It never shows a value. A run offers Deny and Allow once only, every time, and no setting releases a set without asking. Allow once, or `o`, starts the command once. Refusing a run refuses the same project, command and directory for a minute. An agent runs nothing until you turn run on for it, and a run whose set holds a blocked value is refused without asking.

An item tagged into a protected environment, one named `prod`, `production`, `prod-…` or `production-…` such as `env:billing:prod`, is asked about at every run that names it and refused to a scoped token's run.

The command shown is what the run says it will start; approve only a run you started or a command whose behaviour you know ([THREATS](../THREATS.md) T-30, T-34, T-35).

## When nothing holds the vault

Everything is denied, and the agent is told how to fix it:

```
keypaste: DENIED. Nobody can approve this right now: nothing holds this vault unlocked, neither
the keypaste desktop app nor keypaste approve, and keypaste releases nothing until one does,
whatever this agent's settings say.

Ask the person you are working with to unlock this vault in the keypaste desktop app, or to
run `keypaste approve --vault <their vault>` in a terminal, and then try again. Until then,
every request will be refused. This call was recorded in the audit log as denied.
```

When the app is open with this vault locked, the agent is told to ask you to unlock it there. On macOS and Linux the first such request after a lock also posts a notification, "An agent asked for a locked vault", which names no vault; its Open keypaste shows the unlock screen with that vault selected. On Windows, and where the system cannot post one, the app shows a small notice of its own, which names the vault, has no password field and does not take the keyboard. No other notice appears until you have unlocked and the app has locked again.

## The audit log

The app or `keypaste approve` appends one JSON line to `~/.keypaste/audit.jsonl` for every call it answers, granted, denied or abandoned. The bridge writes one only for a call it refused itself, a malformed one included, or that nothing holding the vault answered. `KEYPASTE_HOME` moves the directory for the process it is set for; a bridge's `--audit-log` moves only that bridge's own lines. In the app the log is Activity, at the foot of the agents list in Agents.

```json
{"v":2,"ts":"2026-07-26T14:03:11.482Z","seq":1,"pid":48122,
 "client":{"name":"claude-code","version":"1.2.3","label":"claude-code","transport":"stdio"},
 "tool":"request_credential",
 "args":{"entry":"env/demo/.env","entry_kind":"path","field":"STRIPE_KEY",
         "reason_excerpt":"deploy the billing service to staging","reason_len":37,
         "reason_sha256":"..."},
 "decision":"granted","method":"prompt",
 "reason":"a person approved this one request",
 "prev":"0000...0000","hash":"0c806dbd...14b3c7"}
```

A line never holds the released value. It records the item, the field, the client and its label, the agent's reason, the decision and how it was reached, and also `vault`, the identity of the vault that answered, and `entries`, the items a release or run used. A `run` line carries the first 256 characters of the command line as `command` and the SHA-256 of the whole argument list as `command_sha256`. Item names and reasons are logged, so keep secret values out of them, and treat the log as sensitive.

A call is refused when its line cannot be written. The log is never rotated or trimmed. On Linux and macOS keypaste creates it owner-readable and tightens looser permissions with a notice; on Windows it inherits its folder's permissions, so keep `KEYPASTE_HOME` in a folder only you can read.

`decision` is `granted` or `denied`. `method` says how:

| `method` | What happened |
|---|---|
| `prompt` | A person was shown this request and answered it, including a run and a confirmed change to an agent's settings. |
| `allowed` | Released without asking: its kind is Allow, an Always allow was kept for it, or it is an item's URL, or its username while Agents see usernames is on. The reason says which. |
| `blocked` | Refused without asking: its kind is Block, an Always block was kept for it, or it is a passkey's field. |
| `exposure` | A listing, naming only what the agent can see. |
| `out-of-scope` | The item was outside what the agent can see, or absent; one answer for both, so nothing outside can be probed. |
| `run-off` | A run from an agent whose run is off, refused before anything was looked up. |
| `rules-changed` | A change to agent settings, on an `access_change` line naming the agent. |
| `narrowing` | On an `access_change` line: a change to an agent's settings that gave it nothing more, applied at once. |
| `no-approver` | Nothing held the vault unlocked. |
| `no-session` | The request did not reach the current unlock of the vault's holder: no vault named or chosen, another vault answered, or the vault was locked or unlocked again in between. |
| `vault-locked` | The vault was locked, or locked while the request waited. |
| `vault-changed` | The holder's copy no longer matched the file, because another program saved it, a change was still being saved, or the vault was reloaded or merged while the request waited. Nothing was read. |
| `merged` | On a `vault_merge` line: the app merged another program's save with its own changes, naming the items both changed, never a value. |
| `timed-out` / `cancelled` | Nobody answered in time / the client stopped waiting, or a lock or reload took a confirmation down. |
| `busy` / `cooldown` | Too many requests already waiting / the same field was refused a moment ago. |
| `undeliverable` | Authorized, but too large to send in one reply. Nothing was released. |
| `unfillable` | A placeholder in the value could not be filled from its field. |
| `unchanged` | A terminal edit the holder needed to write nothing for. |
| `token`, `share-created`, `share-revoked` | A scoped token's release, a share link made, a share link revoked. |
| `not-armed`, `not-paired`, `removed` | A browser asked to pair while pairing was off, presented an unknown pairing, or had its pairing removed. |
| `invalid-request` / `failed` / `not-initialized` | Wrong arguments / something went wrong / a tool called before the MCP handshake finished. |

**Older logs.** The log is append-only, so old lines keep the words they were written with. v0.3.0 wrote `grant-cache` (a live approval on the same connection served the request), `policy` and `policy-limit` (a `policy.toml` rule released it, or had spent its hourly allowance). Earlier source builds wrote `automatic`, `automatic-commands`, `always-allowed`, `inject-only` and `granted_seconds`, and the earliest build wrote `not-implemented`. Nothing in the 1.0.0 build writes them.

### Reading it

```sh
keypaste log
keypaste log --denied
keypaste log --client claude-code --since 2h
keypaste log --json
```

`--since` takes a span (`30m`, `2h`, `7d`) or a timestamp, and `--client` matches part of a label or name. A narrowed view shows both the selected and the total count. `--json` writes the records and whether the chain vouches for each.

### Knowing it has not been edited

Each record holds its predecessor's hash in `prev` and its own in `hash`, so changing one breaks the next one's link.

```sh
keypaste log verify
```

```
3 records verified in /home/you/.keypaste/audit.jsonl.
Latest: seq 3, hash 651f0392457b29f80f3168584758418c71734077577a9c100e83225e1783dde8
```

`keypaste log verify` exits 5 for a broken chain and names the line and the problem; `keypaste log` runs the same check and marks a row the chain does not vouch for with `?`. The chain has no secret key, so whoever can write the file can recompute it, and deleting the last records leaves a valid chain; both limits are printed on every pass. To detect a lost record, keep a hash somewhere else and supply it later:

```sh
keypaste log verify --expect 651f0392457b29f80f3168584758418c71734077577a9c100e83225e1783dde8
```

[THREATS](../THREATS.md) T-5 covers these limits. Records from a newer keypaste stop new lines being appended, so the chain does not fork; upgrade, or move the file aside to start a new log.

<a id="archiving-it"></a>

### Archiving it

keypaste never rotates, trims or moves the log. To start a new one:

1. Quit the app, stop `keypaste approve` and quit every AI tool that runs keypaste's bridge, so nothing has the file open. Windows cannot move a file in use.
2. Run `keypaste log verify` and keep the `Latest` hash somewhere other than this computer.
3. Move the file aside, in `KEYPASTE_HOME` if you set one:

```sh
mv ~/.keypaste/audit.jsonl ~/.keypaste/audit-2026-10.jsonl
```

```powershell
Move-Item "$HOME\.keypaste\audit.jsonl" "$HOME\.keypaste\audit-2026-10.jsonl"
```

The next record starts a new file, and the archive still verifies with `keypaste log verify --audit-log ~/.keypaste/audit-2026-10.jsonl --expect <hash>`. Activity reads only `audit.jsonl`; `keypaste log --audit-log <file>` still lists an archive.

<a id="the-honest-limits"></a>

## Limits

`keypaste approve` keeps the vault unlocked while it runs and has no idle lock; stop it to lock. It serves the vault as it was when unlocked, and once another program changes the file it refuses every request as `vault-changed` until you restart it. Locking the desktop app does not stop a terminal approver holding another vault.

keypaste keeps no released value between requests, but it cannot erase what the client, its transcripts or a launched process kept, and stopping the approver revokes nothing at the credential's provider. [SECURITY](../SECURITY.md) and [THREATS](../THREATS.md) set out these boundaries.

## Checking it yourself

`scripts/verify-approval-e2e.sh` runs the real CLI and bridge against a test vault on Linux, macOS and Windows: an approval returns the value, a refusal does not, and neither writes it to the audit log. `scripts/verify-demo.sh` checks the prompt in [the demo](demo.md) and four other public pages against the built binaries; it does not read this page.
