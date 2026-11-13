# Connecting keypaste to Claude

> This guide describes the source build headed for keypaste 1.0.0, which is not yet published. The published v0.3.0 ships the bridge as its own `keypaste-mcp` binary, names the approver `keypaste agent`, connects only Claude Code and Codex with `keypaste setup` (printing a block to paste for Cursor and Claude Desktop), and has no desktop app. [FEATURES](FEATURES.md) lists what each version has.

keypaste's MCP bridge, `keypaste mcp`, lets an AI agent see the names of items in your vault and ask for one value at a time. Your MCP client starts the bridge, which holds no vault and checks what the agent can see. You hold the vault unlocked, in the desktop app or with `keypaste approve` in a terminal, and that process asks you or applies your [agent settings](policy.md). Without it, every call is refused with the command that fixes it. [Approvals](approvals.md) explains the prompt and the audit log.

```sh
keypaste approve --vault ~/vaults/personal.kdbx
```

The bridge reaches whichever keypaste process holds the vault named by its `--vault` or `KEYPASTE_VAULT`, or, with neither, the vault you chose, which the app records at its first create or unlock and Settings or `keypaste use <path>` changes. A bridge with no vault named or chosen is refused. A running bridge keeps the chosen vault it started with until its client restarts it.

## The short way

```sh
keypaste setup --vault ~/vaults/personal.kdbx
```

`setup` finds the AI tools installed here and connects Claude Code, Codex, Gemini CLI and Devin through each tool's own `mcp add` at user scope. VS Code, Cursor and, on macOS and Windows, Claude Desktop have no such command, so `setup` changes only keypaste's entry in the file each one reads (VS Code's user `mcp.json`, `~/.cursor/mcp.json`, `claude_desktop_config.json`), keeps the first original as `<file>.keypaste-backup`, and leaves alone a file that does not parse, holds a key twice or changes while it works. It prints every step and asks once; `--yes` answers for you, and without a terminal nothing changes unless you pass it.

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

- Each tool is registered with its id as its label (`--client-label claude-code`), which its agent settings follow; `--label` gives another. It is registered with `--expose '**'` and `--allow-run` ([what it can see](#what-the-agent-can-see), [running a command](#running-a-command-with-secrets---allow-run)); `--expose <glob,glob>` narrows what every agent these tools start can see.
- `--vault` is written only for a vault other than the chosen one. Under a `KEYPASTE_HOME` other than `~/.keypaste`, `setup` also writes the vault, `--audit-log` and `--approver`, and a set `KEYPASTE_APPROVER` writes `--approver`, because a tool may start the bridge without your shell's variables.
- A tool's command is found only in the absolute entries of `PATH`; the macOS app, which Finder starts without your shell's `PATH`, also looks in each tool's install folders. On Windows a tool installed with pnpm or yarn is refused; install it with npm or the vendor's installer. Gemini CLI starts keypaste only in folders you have trusted and strips variables whose names contain `KEY`.
- `--dry-run` prints every step and changes nothing. `--remove` takes keypaste out of each tool that has it and leaves the tool's other servers alone; a tool without keypaste is reported `not connected`. `setup` exits 0 when any tool was connected, removed or found not connected, 3 when it found none and 6 when every tool it found refused. Running it again is safe and updates a moved vault path.

No master password, key file or session identifier is written into a client's configuration.

## From the desktop app

Agents lists the AI tools found on this computer and every agent that asked this vault in the last 30 days. Add an agent offers a setup prompt to copy, a specific app to connect, or MCP to add by hand. A tool's Connect (named for it, as in Connect Claude Code) shows exactly what it will run or edit, the same registration `setup` writes, and writes nothing until you press Connect in that pane. Check the connection then starts the registered command as the client would and asks for one value with a reason saying it is a check, which opens the app's prompt like any other request. A connected tool has Connect again, for a moved vault or app, and Disconnect. [The desktop guide](desktop.md) covers the screen.

The app registers the `keypaste` beside it, or the first one on `PATH`, started as `keypaste mcp`. From an AppImage, which mounts somewhere new each time it starts, it registers `~/.local/bin/keypaste`, creating it first, or replacing another program's file there and keeping it as `~/.local/bin/keypaste.keypaste-backup`. Deleting the `.AppImage` breaks the registration until another one starts.

## By hand

For a client `setup` does not know, or a scope other than your own.

<a id="claude-desktop"></a>

### Claude Desktop

Open Claude menu → Settings → Developer → Edit Config, or edit the file directly:

| | |
| --- | --- |
| macOS | `~/Library/Application Support/Claude/claude_desktop_config.json` |
| Windows | `%APPDATA%\Claude\claude_desktop_config.json` |
| Linux | `~/.config/Claude/claude_desktop_config.json` |

The packaged Claude Desktop on Windows (Microsoft Store or MSIX) reads its own copy at `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude_desktop_config.json` once it has one.

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

Paths must be absolute, because the client's working directory is not yours. On Windows, escape the backslashes: `"C:\\Users\\you\\keypaste.exe"`. Restart Claude Desktop; the server appears under the tools icon. If you set `KEYPASTE_HOME`, connect Claude Desktop with `keypaste setup` or the app's Connect instead, since Claude Desktop does not see your shell's variables.

<a id="claude-code"></a>

### Claude Code

```sh
claude mcp add --transport stdio --scope project keypaste \
  -- /absolute/path/to/keypaste mcp \
     --vault /absolute/path/to/vault.kdbx \
     --client-label claude-code
```

`--scope project` writes `.mcp.json` in the repository root, which your teammates get too if you commit it. Absolute local paths usually differ between teammates and can reveal usernames, so use `--scope local` to keep it to your own machine. The same by hand:

```json
{
  "mcpServers": {
    "keypaste": {
      "command": "/absolute/path/to/keypaste",
      "args": ["mcp", "--vault", "/absolute/path/to/vault.kdbx", "--client-label", "claude-code"]
    }
  }
}
```

## If your vault has a key file

`keypaste approve` opens the vault, so it is the process that needs the key file: add `--keyfile <path>` beside its `--vault`, or set `KEYPASTE_KEYFILE`. `keypaste mcp` never opens a vault, so its configuration takes no key file and `setup` writes none.

<a id="what-the-agent-may-see"></a>

## What the agent can see

`keypaste setup` and the app's Connect register `--expose '**'`, so an agent sees every item outside keypaste's own groups: its title, group, tags and URL, and its username unless Agents see usernames is off. Each value it asks for is then decided by its [agent settings](policy.md), which by default ask you about everything.

A bridge you configure by hand with no `--expose` sees only `tag:env:*`: the variable-named fields of items tagged into a project, the same variables `keypaste run` uses. Any `--expose` replaces that default, so name it again when you add a group:

```json
"args": [
  "mcp", "--vault", "/absolute/path/to/vault.kdbx",
  "--expose", "tag:env:*",
  "--expose", "servers/staging/*"
]
```

A pattern matches the group path and the title separately: `*` stays inside one path segment and `**` spans any number. A title containing a slash is matched as a title, so it cannot pass for a deeper group. `tag:env:<project>[:<environment>]` reaches only that project's variables, never an item's password, though a variable whose value holds a placeholder such as `{PASSWORD}` carries that field filled in. Every pattern discloses the matching names, which can reveal your inventory and help target later requests.

## The tools

- `list_entry_names` takes no arguments and returns, for each item the agent can see, its group path and title, the fields it may ask for and its project tags. It never returns a value.
- `request_credential` takes `entry`, `field` and `reason` and returns one field, or with `field` set to `totp` the item's current code, never its seed.
- `request_attachment` takes `entry`, `name` and `reason` and returns one attachment by its exact name, as base64; one over 5 MiB is refused.
- `request_save` takes `item` and `field`, or `project`, `environment` and `key`, with `reason`, and asks you to save a value there that you type, paste or generate. The item is created when there is none, a replace needs your confirmation, and the agent learns only whether it was saved ([THREATS](../THREATS.md) T-54).
- `run`, offered by a bridge started with `--allow-run`, is below.

The MCP surface cannot add, edit or delete items except through `request_save`. Anyone who can edit the vault can influence item names; keypaste strips control characters, invisible Unicode and structural punctuation and labels the listing as data, which cannot rule out prompt injection ([THREATS](../THREATS.md) T-1).

## Running a command with secrets (`--allow-run`)

With `--allow-run` the bridge offers `run`. `setup` and Connect add it, and an agent still runs nothing until you turn run on for it, on its row in Agents or with `keypaste clients <agent> --run on`.

`run` takes `command` (the program and its arguments, one per item, never through a shell), `directory` (an existing absolute path), either `project` with an optional `profile` and `keys` or `env` mapping variable names to `kp://` references, a `reason`, and an optional `timeout_seconds` (default 120, at most 600). `PATH`, `KEYPASTE_*` and literal values cannot be set, and the program is looked up only in absolute `PATH` entries. Every run is asked, with Deny and Allow once ([Approvals](approvals.md#runs)).

The bridge starts the command with the values in its environment and stdin closed, and returns its exit code and the last 16384 characters of each stream with every literal or escaped occurrence of a value replaced by `[keypaste:NAME]`. If the client asked for progress, the bridge reports every 10 seconds while you decide and while the command runs; a client that times tool calls out sooner should raise its limit. If the client cancels, the command is stopped. The replacement is a safety net, not a boundary: a command can still reveal a value in another form, in a file or over the network ([THREATS](../THREATS.md) T-35).

## Ending access

`keypaste lock --vault <path>` asks the app or `keypaste approve` holding the vault to lock now: every release stops and `keypaste approve` ends. It asks for no password. Disconnect in Agents, or `keypaste setup --remove`, takes keypaste out of a tool. Neither erases what an agent already received; rotate a credential at its provider when that matters.

<a id="archiving-it"></a>

## Seeing what agents did

Every call is a line in `~/.keypaste/audit.jsonl`, which `keypaste log` reads and the app shows as Activity under Agents. [Approvals](approvals.md#the-audit-log) explains the log, how to check it and how to archive it.

## Checking it works without a client

```sh
printf '%s\n' \
  '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"probe","version":"1.0"}}}' \
  '{"jsonrpc":"2.0","method":"notifications/initialized"}' \
  '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}' \
  | keypaste mcp --vault ~/vault.kdbx --audit-log /tmp/probe.jsonl
```

You should get two JSON lines back, the second listing `list_entry_names`, `request_credential`, `request_attachment` and `request_save`. CI runs a stricter version of this on all three operating systems (`scripts/verify-mcp-stdio.sh`).

## Troubleshooting

- **Startup fails.** Check that the executable path is absolute, the file is executable (`chmod +x`) and `~/.keypaste` is writable. `keypaste doctor` reports the vault, the process holding it and each AI tool's registration.
- **"Nothing holds this vault unlocked."** Unlock the vault in the app, or start `keypaste approve --vault <path>` for the vault the bridge names. If one is running, check that both use the same `--approver` or `KEYPASTE_APPROVER`.
- **The vault is locked, or `vault-changed`.** The holder locked, or another program saved the file. The app merges that save and answers again; `keypaste approve` needs a restart.
- **Nothing in the audit log.** Confirm the client called a tool, then check `KEYPASTE_HOME`: the app or `keypaste approve` writes in its own home, and `--audit-log` moves only the bridge's lines. The client's MCP server log shows keypaste's stderr.
- **Protocol errors.** Look for wrappers or shell profiles that print to stdout, which a stdio MCP server reserves for protocol messages.
- **An old command is refused.** `keypaste agent`, `keypaste mcp serve`, `keypaste mcp setup` and `keypaste mcp policy` are refused in the 1.0.0 build, each naming the command that replaced it, and `keypaste doctor` flags a client still registered to start `keypaste-mcp`.

What the bridge does not protect against, such as a client that keeps released values in its transcripts or sends them to a remote model, another local program starting a bridge under an agent's label, or an agent that edits its own client's configuration, is set out in [SECURITY](../SECURITY.md) and [THREATS](../THREATS.md) (T-3, T-14).
