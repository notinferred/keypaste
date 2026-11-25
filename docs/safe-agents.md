# Running agents safely

keypaste asks you before an agent gets a value, but only while the agent cannot change what keypaste reads or reach around the bridge. An agent's shell commands run as you. Unconfined, they could rewrite the tool's MCP configuration to take another agent's name or vault (T-36), replace the `keypaste` the tool starts, change keypaste's settings, or read a running program's environment (T-35).

Claude Code, Codex and Cursor can each confine their agent's commands. Set your tool's sandbox as its section shows. Each tool's behavior comes from its documentation as read on 2026-10-21 ([Sources](#sources)).

This guide describes the unpublished 1.0.0 source build. For `v0.3.0`, protect `policy.toml` in place of `agents/`, and the `keypaste-mcp` binary as the program the tools start ([FEATURES](FEATURES.md)).

## What to protect

The agent's commands must not write these paths:

| Path | Why |
|---|---|
| `~/.keypaste` (`%USERPROFILE%\.keypaste`, or the folder `KEYPASTE_HOME` names) | [Agent settings](policy.md#where-they-are-kept), the vault bridges ask for, the [audit log](approvals.md#the-audit-log), and which process holds a vault |
| Each tool's MCP configuration: `~/.claude.json` and `.mcp.json`; `~/.codex/config.toml` (or `$CODEX_HOME/config.toml`) and `.codex/config.toml`; `~/.cursor/mcp.json` and `.cursor/mcp.json` | It names the program the tool starts as keypaste, with its label, vault and reach (T-36) |
| The vault and its `<vault>.backups` folder | Your items and how you get them back |
| The `keypaste` the tools start: the path `keypaste setup` prints, `/Applications/keypaste.app`, the AppImage, `%LOCALAPPDATA%\Programs\keypaste`, `~/.local/bin/keypaste` | Whatever is there is the bridge every tool starts |

The bridge itself must write `audit.jsonl` and reach the approver's pipe (T-6, T-10). Claude Code and Codex start MCP servers outside the agent's sandbox, so the settings below leave the bridge working. Do not run a whole tool inside a sandbox that keeps these paths from it, such as the standalone `srt`: its bridge would not start.

## For every tool

- Start the agent in the project's folder, never your home folder or one holding the vault or `~/.keypaste`.
- Keep the vault, `KEYPASTE_HOME` and any pinned paths out of the project and out of temporary folders.
- Add no writable folder holding a path above, and let no shell, interpreter or `keypaste` run outside the sandbox through an exclusion or allowlist.
- Do not use modes that skip the sandbox: Claude Code's `--dangerously-skip-permissions`, Codex's `--yolo` or `danger-full-access`, Cursor's Run Everything.
- Read a repository's own tool settings before you open or trust it.
- Using more than one tool in a project, check `.mcp.json`, `.codex/` and `.cursor/` are unchanged before starting another. Each sandbox protects only its own:

| Tool | Protects | Leaves writable |
|---|---|---|
| Claude Code | `.mcp.json`, `.claude` | `.codex`, `.cursor` |
| Codex | `.codex` | `.mcp.json`, `.cursor` |
| Cursor | `.cursor/*.json`, `.claude/*.json`, `.vscode/` | `.mcp.json`, `.codex` |

## Claude Code

The sandbox is off by default and runs on macOS, Linux and WSL2. It never lets commands write Claude Code's own configuration. Claude's file tools follow permission rules instead.

1. Put this in `~/.claude/settings.json`, with `~/Vaults` replaced by the folder holding your vault and key file:

   ```json
   {
     "sandbox": {
       "enabled": true,
       "failIfUnavailable": true,
       "allowUnsandboxedCommands": false,
       "filesystem": {
         "denyWrite": ["~/.keypaste", "~/Vaults"],
         "denyRead": ["~/.keypaste", "~/Vaults"]
       }
     },
     "permissions": {
       "deny": ["Edit(~/.keypaste/**)", "Edit(~/Vaults/**)", "Read(~/.keypaste/**)", "Read(~/Vaults/**)"]
     }
   }
   ```

2. Where your account can write the `keypaste` the tools start, add it to both lists, in `Edit` rules with `//` before an absolute path: `~/.local/bin/keypaste` and the AppImage on Linux, `/Applications/keypaste.app` on macOS.
3. Start Claude Code as `claude --settings '{"sandbox": {"allowUnsandboxedCommands": false}}'`, through an alias if you like.

| Setting | Why |
|---|---|
| `--settings` | Without it, a repository's `.claude/settings.json` outranks yours and can weaken the sandbox; with it, Claude Code ignores those settings. |
| `failIfUnavailable` | Exit rather than run commands unsandboxed. |
| `allowUnsandboxedCommands: false` | Refuse retries outside the sandbox. |
| `denyWrite` | Holds inside allowed folders. On Linux an entry with `*`, `?` or `[` is skipped. |
| `denyRead` | Keeps the vault and keypaste's home out of the transcript. |
| `Edit` and `Read` rules | Cover Claude's file tools, which the sandbox does not. |

- **macOS.** Seatbelt, no setup. Keep keypaste's socket off `allowUnixSockets` and `allowAllUnixSockets` off. Process visibility: undocumented.
- **Linux and WSL2.** Install `bubblewrap` and `socat`, and on Ubuntu 24.04+ the AppArmor profile from Claude Code's sandboxing page. Without the optional seccomp filter (`/sandbox` lists it), commands can reach keypaste's pipe. Leave `enableWeakerNestedSandbox` off outside a container; it exposes other processes.
- **Windows.** No native sandbox; only permission rules, which judge a command by its text. A WSL2 setup needs `keypaste approve` inside WSL2, untested.

## Codex

Codex sandboxes the agent's commands on macOS, Linux, WSL2 and Windows, and not MCP servers. Under `workspace-write`, commands can write the working folder, `/tmp`, `$TMPDIR` and added roots, while `.git`, `.agents`, `.codex` and `.aws` inside them stay read-only.

1. In `~/.codex/config.toml`:

   ```toml
   sandbox_mode = "workspace-write"
   approval_policy = "on-request"
   approvals_reviewer = "user"

   [sandbox_workspace_write]
   writable_roots = []
   ```

2. Never start Codex in your home folder. These settings cannot keep a path read-only inside a writable folder.
3. Check that the sandbox refuses a write, from the project folder:

   ```sh
   codex -c 'sandbox_mode="workspace-write"' sandbox -- /bin/sh -c 'touch ./sandbox-probe'
   codex -c 'sandbox_mode="workspace-write"' sandbox -- /bin/sh -c 'touch ~/.keypaste/sandbox-probe'
   ```

   The first must succeed, or the second proves nothing. The second must fail with "Operation not permitted" on macOS, or "Read-only file system" or "Permission denied" on Linux, and leave no file. Use your `KEYPASTE_HOME` if set; the folder must exist. `codex sandbox` ignores `sandbox_mode` in `config.toml`, so the mode goes on the command line. Where your Codex refuses this form, use `sandbox macos` or `sandbox linux`.

| Setting | Why |
|---|---|
| `on-request` | The agent asks before a command leaves the sandbox. Approving one that touches a protected path undoes the rest. |
| `approvals_reviewer = "user"` | You answer, not Codex's reviewer agent. |
| `writable_roots = []` | No added writable folder; add no `--add-dir` either. |

Codex reads a project's `.codex/config.toml` only once you trust the project. Its beta permission profiles can keep `~/.keypaste` read-only inside a writable folder, but keypaste's check does not cover them.

- **macOS.** Seatbelt, no setup. Keep keypaste's pipe off the Unix socket allowlist. Process and pipe visibility: undocumented, untested.
- **Linux and WSL2.** Install `bubblewrap`, and on Ubuntu 24.04 the AppArmor profile from Codex's sandbox page. Process visibility: undocumented.
- **Windows.** Prefer MXC (`prefer_mxc = true` under `[features]`, or `sandbox = "mxc"` under `[windows]` to fail without it), then `elevated` over `unelevated`. Process and pipe visibility: undocumented, unchecked.

## Cursor

Cursor sandboxes its agent's terminal commands on macOS and Linux. Commands can write the workspace and temporary folders, never `.cursor/*.json`, `.claude/*.json` or `.vscode/`. A command that cannot run sandboxed runs outside once approved.

1. In Settings › Agents › Approvals & Execution, choose the Allowlist run mode with sandboxing on, keep shells, interpreters and `keypaste` off the allowlist, and keep External-File Protection on.
2. In `~/.cursor/sandbox.json`:

   ```json
   {
     "type": "workspace_readwrite",
     "additionalReadwritePaths": []
   }
   ```

3. For Cursor's Agent CLI, set `approvalMode` to `allowlist` in `~/.cursor/cli-config.json` and start it with `--sandbox enabled`.

Avoid Auto-review: it sends unsandboxed commands to a classifier Cursor says is not a security boundary. A project's `.cursor/sandbox.json` outranks yours, so read it first.

- **macOS.** Seatbelt, Cursor 2.0 or later.
- **Linux.** Kernel 6.2+ with Landlock and unprivileged user namespaces; the Agent CLI may need Cursor's AppArmor package.
- **Windows.** No sandbox; only what you approve.

Process, socket and MCP server confinement: undocumented. If your team sandboxes keypaste's MCP entry, check that requests still reach keypaste.

## What a sandbox does not change

Everything outside the sandbox still runs as you ([THREATS](../THREATS.md)):

- Anything that reaches the approver through a bridge gets what you allowed ([agent settings](policy.md#limits), T-14).
- A command run through keypaste's `run` tool receives the values, and the agent often wrote it (T-35).
- An agent's settings bind only the bridge that names them (T-36).
- A process outside the sandbox can read memory, files and keystrokes and reach keypaste's pipe (T-10, T-18).
- Under the Codex and Cursor settings, commands can still read the vault, backups, key file and keypaste's home into the transcript, leaving only the master password against an offline attack (T-20, T-27).

## How keypaste checks this

CI's `tools` jobs on Linux and macOS run Codex 0.162.1's `codex sandbox` with `sandbox_mode = "workspace-write"` from a project folder: a write in that folder succeeds, and appends to a vault's agent settings in `~/.keypaste/agents/` and to Codex's `config.toml` are refused and leave both unchanged. Nothing else here is checked by keypaste: not Claude Code, Cursor, Codex's Windows sandbox, or a project's `.mcp.json`.

## Sources

Read on 2026-10-21:

- Claude Code: [Configure the sandboxed Bash tool](https://code.claude.com/docs/en/sandboxing), [All settings](https://code.claude.com/docs/en/settings-reference), [Choose a sandbox environment](https://code.claude.com/docs/en/sandbox-environments), [Configure permissions](https://code.claude.com/docs/en/permissions), [Permission modes](https://code.claude.com/docs/en/permission-modes).
- Codex: [Sandbox](https://learn.chatgpt.com/docs/sandboxing), [Agent approvals & security](https://learn.chatgpt.com/docs/agent-approvals-security), [Permissions](https://learn.chatgpt.com/docs/permissions), [Windows sandbox](https://learn.chatgpt.com/docs/windows/windows-sandbox), [Configuration reference](https://learn.chatgpt.com/docs/config-file/config-reference), [Developer commands](https://learn.chatgpt.com/docs/developer-commands), [Model Context Protocol](https://learn.chatgpt.com/docs/extend/mcp).
- Cursor: [Run Modes](https://cursor.com/docs/agent/security/run-modes), [sandbox.json reference](https://cursor.com/docs/reference/sandbox), [Agent Security](https://cursor.com/docs/agent/security), [MCP](https://cursor.com/docs/mcp), [CLI parameters](https://cursor.com/docs/cli/reference/parameters), [CLI configuration](https://cursor.com/docs/cli/reference/configuration).
