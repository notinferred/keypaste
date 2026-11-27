# Approving an agent's request

When an agent asks for a value, keypaste asks you. You keep the vault unlocked in the desktop app or with `keypaste approve`, answer each request, and check the audit log for what was released. [Agent settings](policy.md) decide which requests reach you; [Connecting keypaste to Claude](mcp-setup.md) covers connecting a tool.

This guide describes the unpublished 1.0.0 source build. In `v0.3.0` the approver is `keypaste agent`, its prompt asks `Approve? [y/N]`, and there is no desktop prompt ([FEATURES](FEATURES.md)).

<a id="the-short-version"></a>

## How it works

```
  your terminal or the app             your MCP client
       │                                   │
  keypaste approve ◄─── local pipe ────  keypaste mcp
   • holds the unlocked vault             • checks what it can see
   • asks you                             • refuses anything out of scope
   • writes the audit line                • returns one field, or a set
```

Your AI tool starts the bridge, `keypaste mcp`, which holds no vault. The approver, the app or `keypaste approve`, holds the vault unlocked and decides each request under your agent settings: ask you, release, or refuse. An agent cannot trigger a master-password prompt, so you never learn to trust a password window another program could imitate.

One vault has one approver. While the app has a vault unlocked, `keypaste approve` on that vault is refused, naming the app.

## Start the approver

In the desktop app, unlock the vault. In a terminal:

```sh
keypaste approve --vault ~/vaults/personal.kdbx
```

It asks for your master password, then waits. Leave it running in its own terminal. Ctrl+C, SIGTERM and closing the terminal lock the vault, and a request waiting at the prompt is denied as `vault-locked`.

| Option | What it does |
|---|---|
| `--vault <path>` | Which vault to unlock. Or `KEYPASTE_VAULT`; with neither, the vault chosen in the app or with `keypaste use`. |
| `--keyfile <path>` | The vault's key file. Or `KEYPASTE_KEYFILE`. |
| `--approval-timeout <seconds>` | How long you get to answer. Default 45, range 5–55. |
| `--approver <name>` | Which pipe to listen on, needed only if you run two. Or `KEYPASTE_APPROVER`. |

The pipe name is derived per user and per vault, so the bridge must name the vault the approver holds.

<a id="the-prompt"></a>

## Answer a request

One prompt shows at a time. Up to three requests per agent wait their turn; a fourth gets `BUSY`.

### At the terminal

`keypaste approve` shows the request with the choice line `[d] deny  [o] once  [a] always` and a countdown. [The demo](demo.md#review-the-request) shows it exactly.

- `agent` is the name the program gave itself, which nothing checks, then in brackets its bridge's `--client-label`. Settings follow the label, never the name.
- `item`, `group` and `field` say what is asked for. A `/` in a title shows as a space, so a title cannot pass for another group path.
- The reason is the agent's own text, stripped of control and invisible characters and cut to 400 characters ([THREATS](../THREATS.md) T-2).

| Key | Answer |
|---|---|
| `o` | Allow once and keep nothing. |
| `a` | Always allow, kept at once for that agent and that item's field ([agent settings](policy.md#one-agents-own-rules)); keypaste then shows the `keypaste clients <agent> --forget <key>` command that undoes it. Not offered without a label, past 128 kept answers, or in a [protected stage](policy.md#protected-stages). |
| Anything else | No, Enter included. So is the countdown ending. |

An allow counts only after the choices have been on screen for a second, so a key meant for the last prompt cannot answer this one.

<a id="approving-in-the-desktop-app"></a>

### In the desktop app

A card opens over whatever you are doing, such as "Give claude-code STRIPE_SECRET_KEY?", with the item, its vault and the agent's own words. It offers Deny, Allow once and, for a labelled agent, Always allow, which keeps that answer at once. Protected stages, commands and linked sets offer no Always allow. To block a kind of value, use the agent's page in Agents.

- **Verified** means keypaste checked with the system that the asker is keypaste's own `keypaste`, started by the tool the label names. Otherwise it says **Not verified**. Verified changes no rule ([THREATS](../THREATS.md) T-3).
- Focus starts on Deny. A click on an allow works once the card has had focus for 0.3 seconds; Return and Space wait a second.
- Deny, Escape, closing the card, the timer, a lock or quitting each refuse.
- Agents lists waiting requests under Waiting for you; you answer only in the card.

### Several matches, attachments

When several items match the name asked for, you pick one, and only its field is sent. A `request_attachment` releases one attachment by name, at most 5 MiB.

<a id="linked-sets"></a>

### Linked sets

A `request_linked_set` asks for two to four fields from one item and the items `find_access` links to it. It is asked every time, even when your settings allow each field, with Deny and Allow once only (`y` at the terminal). Deny sends nothing and holds each field back for a minute. A set holding a blocked or recently refused field, or an item out of reach, is refused without asking.

<a id="item-changes"></a>

### Item changes

A `request_item_change` asks to move an item to Trash, mark it expired, or delete it for good. Delete is offered only for an item that same agent created through a `request_save` you approved. The change is asked every time, and no setting allows one. Deny holds the item back for a minute. Adding or removing the tag `needs-update` is not asked ([the tools](mcp-setup.md#the-tools)).

<a id="runs-that-ask-for-a-projects-variables"></a>

### Runs

`keypaste run <project> -- <command>` asks the approver for the project's env set while it holds the vault, and `--session` asks only it ([replace-dotenv](replace-dotenv.md#run-your-app)). An agent's `run` tool asks the same way once you turn run on ([agent settings](policy.md#running-commands)).

The prompt names the project, variable names, source items, command, directory and, for an agent, its reason, never a value. A run from a terminal or an agent is asked every time, with Deny and Allow once only. Refusing it refuses the same run for a minute. Approve only a run you started or a command you understand ([THREATS](../THREATS.md) T-30, T-34, T-35).

A Run or Open terminal you press in the desktop app, or Run again in its Dock or tray menu, is your answer and isn't asked, except in a protected stage; its line's method is `pressed` ([desktop guide](desktop.md#run-a-command-with-a-projects-variables)).

## When you say no

A refusal tells the agent not to retry, and that field is refused for a minute without asking. Changing that kind in agent settings ends the wait. A timeout allows a retry.

To stop an agent that keeps asking, block the kind of value, or turn its commands off with `keypaste clients <agent> --run off`.

## Requests that don't reach you

[Agent settings](policy.md) and kept answers release or refuse some requests without asking, and `keypaste approve` prints a line for each. Nobody reads the reason of a release without asking; the audit log keeps it ([THREATS](../THREATS.md) T-12).

When nothing holds the vault unlocked, every request is denied and the agent is told to ask you to unlock it. When the app is open with the vault locked, it tells you an agent asked.

## The audit log

The approver appends one JSON line to `~/.keypaste/audit.jsonl` for every call it answers. The bridge writes one only for a call it refused itself or that no approver answered. `KEYPASTE_HOME` moves the folder; a bridge's `--audit-log` moves only its own lines. In the app the log is Activity, in Agents, which lists your browser's requests as yours and hides its lookups until you show them; the file keeps every line.

A call whose line cannot be written is refused. A line never holds a value, but item names and reasons are logged, so treat the log as sensitive. On Windows it inherits its folder's permissions, so keep `KEYPASTE_HOME` in a folder only you can read.

### Read it

```sh
keypaste log
keypaste log --denied
keypaste log --client claude-code --since 2h
keypaste log --json
```

<a id="knowing-it-has-not-been-edited"></a>

### Check it has not been edited

```sh
keypaste log verify
keypaste log verify --checkpoints
keypaste log verify --expect <hash>
```

Each line holds its predecessor's hash, so `keypaste log verify` finds an edited line and exits 5. Whoever can write the file can recompute the chain, so the approver signs a checkpoint every 64 lines and at lock with a key the vault holds; `--checkpoints` asks for the master password and checks them. Lines after the last checkpoint are unsigned, and deleting the last lines leaves a valid chain. To catch that, keep the `Latest` hash elsewhere and pass it to `--expect` ([THREATS](../THREATS.md) T-5).

### How long lines stay

Requests for values, runs, saves and changes, and your browser's fills, keep their own lines. An agent's lookups and your browser's are folded after 30 days into one `log_summary` line per agent or browser pairing per day. Settings › Keep activity for sets how long anything is kept: 90 days, 1 year (default), 2 years or forever. While `app.toml` can't be read, nothing is removed.

<a id="archiving-it"></a>

### Archive it

1. Quit the app, `keypaste approve` and every AI tool running the bridge.
2. Run `keypaste log verify` and keep the `Latest` hash off this computer.
3. Move the file aside:

```sh
mv ~/.keypaste/audit.jsonl ~/.keypaste/audit-2026-10.jsonl
```

```powershell
Move-Item "$HOME\.keypaste\audit.jsonl" "$HOME\.keypaste\audit-2026-10.jsonl"
```

The next record starts a new file. Check the archive with `keypaste log verify --audit-log <file> --expect <hash>`.

### What a line holds

| Field | Holds |
|---|---|
| `ts`, `seq`, `pid` | When, its place in the log, the process |
| `client` | The agent's name, version, label and transport |
| `tool`, `args` | The tool and the item, field and the agent's reason; a run's `command` and `command_sha256` |
| `vault`, `entries` | The vault that answered and the items used |
| `decision` | `granted` or `denied` |
| `method` | How it was decided, below |
| `prev`, `hash` | The chain |

| `method` | Meaning |
|---|---|
| `prompt` | You answered |
| `picked` | A login filled at your pick in keypaste's list in the page |
| `pressed` | A run you started with Run or Run again in the app |
| `allowed` / `blocked` | Released or refused without asking by a setting or kept answer |
| `exposure` | A listing |
| `out-of-scope` | Out of reach or absent, one answer for both |
| `run-off` | A run from an agent with run off |
| `rules-changed` / `narrowing` | A settings change on an `access_change` line |
| `no-approver` / `no-session` / `vault-locked` | Nothing held the vault, or not this unlock |
| `vault-changed` / `merged` | Another program saved the vault |
| `timed-out` / `cancelled` / `busy` / `cooldown` | No answer, withdrawn, queue full, refused a moment ago |
| `undeliverable` / `unfillable` / `unchanged` | Too large, a placeholder unfilled, nothing to write |
| `token`, `share-created`, `share-revoked` | A scoped token's release, a share link made or revoked |
| `not-armed`, `not-paired`, `removed` | Browser pairing refusals and removal |
| `invalid-request` / `failed` / `not-initialized` | Bad arguments, an error, a call before the handshake |

v0.3.0 lines may carry `grant-cache`, `policy` or `policy-limit`, which 1.0.0 never writes.

<a id="the-honest-limits"></a>

## Limits

- `keypaste approve` keeps the vault unlocked while it runs and has no idle lock; stop it to lock. Locking the desktop app does not stop a terminal approver holding another vault.
- After another program saves the vault, `keypaste approve` refuses requests as `vault-changed` until it merges that save; a save it cannot merge keeps them refused until you restart it.
- keypaste keeps no released value, but cannot erase what the client or a launched process kept, and stopping the approver revokes nothing at the provider.

[SECURITY](../SECURITY.md) and [THREATS](../THREATS.md) set out the guarantees.

## Checking it yourself

`scripts/verify-approval-e2e.sh` runs the real CLI and bridge against a test vault on Linux, macOS and Windows: an approval returns the value, a refusal does not, and neither writes it to the audit log. `scripts/verify-demo.sh` checks [the demo](demo.md)'s prompt against the built binaries.
