# Connect an AI tool

Your AI tool starts `keypaste mcp`, the bridge, which lets its agent see item names and ask for one value at a time. The bridge holds no vault. You hold the vault unlocked, in the desktop app or with `keypaste approve`, and that approver asks you or applies the agent's [settings](policy.md). With no approver running, every call is refused with the command that fixes it. [Approvals](approvals.md) explains the prompt and the audit log.

> This guide describes the source build headed for 1.0.0. The published `v0.3.0` differs ([FEATURES](FEATURES.md)).

## Connect with `keypaste setup`

```sh
keypaste setup --vault ~/vaults/personal.kdbx
keypaste approve --vault ~/vaults/personal.kdbx
```

`setup` finds the AI tools installed here, prints every step and asks once:

```
keypaste mcp   /home/you/.local/bin/keypaste
vault          /home/you/vaults/personal.kdbx
can see        ** (each agent sees what its settings allow: every project's variables until you change them)
run            offered; each agent runs nothing until you turn run on in its settings

  claude-code      claude mcp remove --scope user keypaste
  claude-code      claude mcp add --scope user --transport stdio keypaste -- /home/you/.local/bin/keypaste mcp --vault /home/you/vaults/personal.kdbx --client-label claude-code --allow-run --expose **
  codex            not installed on this machine
  cursor           edit /home/you/.cursor/mcp.json: set mcpServers.keypaste {"command":"/home/you/.local/bin/keypaste","args":["mcp","--vault","/home/you/vaults/personal.kdbx","--client-label","cursor","--allow-run","--expose","**"]}; keeps the original as /home/you/.cursor/mcp.json.keypaste-backup
Connect keypaste to these 2 tools? [y/N] y
  claude-code      configured
  cursor           configured; the original is kept as /home/you/.cursor/mcp.json.keypaste-backup; restart Cursor to load it
```

Each tool gets a `--client-label`, which its agent settings follow (`--label` gives another), `--expose '**'` and `--allow-run`. No master password or key file goes into a tool's configuration.

| Option | Effect |
|---|---|
| `--dry-run` | Prints every step and changes nothing. |
| `--yes` | Answers the question; needed without a terminal. |
| `--remove` | Takes keypaste out of each tool, leaving its other servers alone. |
| `--expose <glob,glob>` | Narrows what every agent these tools start can see. |

Running it again is safe and updates a moved vault path. It exits 0 when any tool was connected, removed or found not connected, 3 when it found none and 6 when every tool refused. On Windows, a tool installed with pnpm or yarn is refused.

## Connect from the desktop app

In Agents, the + opens the Connect sheet. It offers a setup prompt to paste into an AI tool, the server entry to add by hand, and the tools found on this computer. Connect… shows what Add it for me changes, and nothing is written until you press it. A connected tool reaches only the vaults chosen when it was connected, and has Repair… and Disconnect…. [The desktop guide](desktop.md) covers the screen.

## Each tool

`setup --dry-run` prints the exact step for each tool. It keeps the first original of a file it edits as `<file>.keypaste-backup`. By hand, paths must be absolute; on Windows, escape backslashes in JSON (`"C:\\Users\\you\\keypaste.exe"`).

| Tool | `setup` writes |
|---|---|
| Claude Code | `claude mcp add --scope user` |
| Codex | `codex mcp add`, into `~/.codex/config.toml` |
| Gemini CLI | `gemini mcp add --scope user`; it starts keypaste only in trusted folders |
| Devin | `devin mcp add -s user` |
| VS Code | your user `mcp.json`, under `servers` with `"type": "stdio"` |
| Cursor | `~/.cursor/mcp.json`; restart Cursor |
| Claude Desktop | its config file, on macOS and Windows |

For another tool, write an entry like the ones below with its own `--client-label`.

<a id="claude-code"></a>

### Claude Code

For one repository, by hand:

```sh
claude mcp add --transport stdio --scope project keypaste \
  -- /absolute/path/to/keypaste mcp \
     --vault /absolute/path/to/vault.kdbx \
     --client-label claude-code
```

`--scope project` writes `.mcp.json` in the repository root, which teammates get if you commit it. Its absolute paths can reveal usernames; `--scope local` keeps it to your machine.

<a id="claude-desktop"></a>

### Claude Desktop

Open Claude menu → Settings → Developer → Edit Config, or edit the file:

| | |
| --- | --- |
| macOS | `~/Library/Application Support/Claude/claude_desktop_config.json` |
| Windows | `%APPDATA%\Claude\claude_desktop_config.json` |
| Linux | `~/.config/Claude/claude_desktop_config.json` |
| Windows, Store or MSIX | `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude_desktop_config.json`, once it has one |

```json
{
  "mcpServers": {
    "keypaste": {
      "command": "/absolute/path/to/keypaste",
      "args": [
        "mcp",
        "--vault", "/absolute/path/to/vault.kdbx",
        "--client-label", "claude-desktop"
      ]
    }
  }
}
```

Restart Claude Desktop. It does not see your shell's variables, so with `KEYPASTE_HOME` set, use `setup` or Connect instead.

## Which vault the bridge reaches

- The vault named by `--vault` or `KEYPASTE_VAULT`, or each vault when `--vault` is given several times.
- Otherwise, the vault chosen in the app's Settings or with `keypaste use <path>`. A running bridge keeps its vault until its tool restarts it.
- With none, the bridge is refused.

A key file goes to the approver (`--keyfile <path>` or `KEYPASTE_KEYFILE`), never to the bridge.

<a id="what-the-agent-may-see"></a>

## What the agent can see

With `--expose '**'`, as `setup` and Connect register, the agent sees every item's title, group, tags and URL, and its username unless Agents see usernames is off. Each value it asks for is decided by its [agent settings](policy.md), which by default ask you.

With no `--expose`, the bridge sees only `tag:env:*`: the variables of items tagged into a project. Any `--expose` replaces that default, so name it again when you add a group:

```json
"args": [
  "mcp", "--vault", "/absolute/path/to/vault.kdbx",
  "--expose", "tag:env:*",
  "--expose", "servers/staging/*"
]
```

- `*` stays inside one group and `**` spans any number.
- `tag:env:<project>[:<stage>]` reaches only that project's variables, never an item's password.
- Every pattern discloses the names it matches.

## The tools

| Tool | Does |
|---|---|
| `list_entry_names` | Lists the items the agent can see, with their fields and projects. Never a value. |
| `find_access` | Searches by text, host, kind, project or a related item. Never a value ([THREATS](../THREATS.md) T-55). |
| `request_credential` | Asks for one field, or an item's current TOTP code. |
| `request_linked_set` | Asks for two to four related fields at once: all or none ([Approvals](approvals.md#linked-sets), T-56). |
| `request_attachment` | Asks for one attachment, up to 5 MiB. |
| `request_save` | Asks you to save a value; the agent learns only whether it was saved (T-54). |
| `request_item_change` | Asks to move an item to Trash, mark it expired or delete one it created; tags `needs-update` without asking ([Approvals](approvals.md#item-changes), T-57). |
| `run` | Runs a command with secrets, with `--allow-run` ([below](#running-a-command-with-secrets---allow-run)). |

With several `--vault`s, results name each item's vault, and a request takes `vault` when a name is in more than one.

Item names are data: anyone who can edit the vault can use them for prompt injection ([THREATS](../THREATS.md) T-1).

## Running a command with secrets (`--allow-run`)

With `--allow-run` the bridge offers `run`. An agent runs nothing until you turn run on for it, in Agents or with `keypaste clients <agent> --run on`, and every run is asked ([Approvals](approvals.md#runs)).

`run` takes a `command` (never through a shell), an absolute `directory`, a `project` or `kp://` references in `env`, a `reason`, and an optional `timeout_seconds` (default 120, at most 600). It returns the exit code and the end of each stream, with each value replaced by `[keypaste:NAME]`. That replacement is not a boundary: a command can reveal a value another way ([THREATS](../THREATS.md) T-35).

## Ending access

- `keypaste lock --vault <path>` locks the vault now and stops every release.
- Disconnect in Agents, or `keypaste setup --remove`, takes keypaste out of a tool.

Neither erases what an agent already received. Rotate a credential at its provider when that matters.

<a id="archiving-it"></a>

## Seeing what agents did

Every call is a line in `~/.keypaste/audit.jsonl`, which `keypaste log` reads and the app shows as Activity. [Approvals](approvals.md#the-audit-log) explains checking and archiving it.

## Troubleshooting

Start with `keypaste doctor`, which reports the vault, the process holding it and each tool's registration.

| What you see | What to do |
|---|---|
| The server fails to start | Check the path is absolute and executable, and `~/.keypaste` is writable. |
| "Nothing holds this vault unlocked." | Unlock the vault in the app, or start `keypaste approve --vault <path>`, with the same `--approver` as the bridge. |
| Nothing in the audit log | Check `KEYPASTE_HOME`: the approver writes in its own home. |
| Protocol errors | Remove wrappers or shell profiles that print to stdout. |
| An old command is refused | It names its replacement. |

## Limits

The bridge does not protect against a tool that keeps released values or sends them to a remote model, another local program using an agent's label, or an agent that edits its own tool's configuration ([THREATS](../THREATS.md) T-3, T-14).
