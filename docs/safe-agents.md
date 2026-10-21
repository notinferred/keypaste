# Running agents safely

keypaste asks you before an agent gets a value and records what it gives, but that holds only while the agent cannot change what keypaste reads or reach around the bridge. An agent's shell commands run as you, so on their own they could rewrite the tool's MCP configuration to take another agent's name or vault (T-36), replace the `keypaste` the tool starts, change keypaste's settings, or read a running program's environment (T-35). Claude Code, Codex and Cursor can each confine the commands their agent runs. This guide says how to set them so that the confinement covers keypaste, and what it leaves open. Each tool's behavior comes from its documentation as read on 2026-10-21, listed under [Sources](#sources); where neither that documentation nor keypaste's own check establishes something, the guide says so.

## What the agent's commands must not write

| Path | Why |
|---|---|
| `~/.keypaste`, or `%USERPROFILE%\.keypaste` on Windows, or the folder `KEYPASTE_HOME` names: `clients.toml`, `app.toml`, `audit.jsonl`, `sessions/` | `clients.toml` holds this computer's ceilings on each agent, `app.toml` the vault every bridge without `--vault` asks for (D-0389), `audit.jsonl` the log `keypaste log` reads, and `sessions/` the claims that say which process holds a vault. |
| The tool's MCP configuration: `~/.claude.json` and a project's `.mcp.json` for Claude Code, `~/.codex/config.toml` (or `$CODEX_HOME/config.toml`) and a project's `.codex/config.toml` for Codex, `~/.cursor/mcp.json` and a project's `.cursor/mcp.json` for Cursor | It names the program the tool starts as keypaste, with its label, vault and reach (T-36). |
| The vault and the `<vault>.backups` folder beside it | Replacing or deleting the vault loses your items, and the backups are how you get them back. |
| The `keypaste` the tools start: the path on the `keypaste mcp` line `keypaste setup` prints, the app it belongs to (`/Applications/keypaste.app` on macOS, the AppImage on Linux, `%LOCALAPPDATA%\Programs\keypaste` on Windows) and `~/.local/bin/keypaste` on Linux | Whatever is at that path is the bridge every tool starts, with your approvals behind it. |

The bridge itself needs some of these. `keypaste mcp` writes `audit.jsonl` and will not start without it (T-6), lowers `clients.toml` when an agent asks for less while nothing holds the vault (T-41), reads `app.toml`, and reaches the app or `keypaste approve` over a pipe that is a socket in the temporary folder on macOS and Linux and a named pipe on Windows (T-10). Claude Code's documentation says it starts MCP servers outside the sandbox it gives the agent's commands, and Codex's that its sandbox settings do not govern them, so the settings below leave the bridge that access; Cursor's documentation does not say where it starts them. For the same reason, do not run a whole tool inside a sandbox that keeps these paths from it, such as Claude Code started under the standalone `srt` with its default settings: its bridge would not start.

## For every tool

- Start the agent in the project's folder. Each tool lets its agent write the folder it starts in, so starting it in your home folder, or in any folder that holds the vault or `~/.keypaste`, hands it those files.
- Keep the vault out of the project and out of temporary folders, which Codex and Cursor let commands write by default.
- Add no writable folder that holds a path in the table, and let no shell, interpreter or `keypaste` run outside the sandbox through an exclusion, rule or allowlist.
- Do not use the modes that skip these checks: Claude Code's `--dangerously-skip-permissions`, which also skips its protected-path checks, Codex's `--yolo` or `danger-full-access`, which use no sandbox, and Cursor's Run Everything, which uses none either.
- Read a repository's own settings for the tool before you open or trust it, since a project's settings join yours as each section says.

## Claude Code

The sandbox covers the Bash, PowerShell and Monitor commands Claude runs and every process they start. Claude's own file tools follow permission rules instead, and MCP servers and hooks run outside the sandbox with your access. Sandboxed commands can write the working folder, a per-user temporary folder and folders you added, but never Claude Code's own configuration, `~/.claude.json` and a `.mcp.json` included, whatever an allow entry says. The sandbox is off by default and runs on macOS, Linux and WSL2.

Put this in `~/.claude/settings.json`, which applies to every project, with `~/Vaults` replaced by the folder that holds your vault:

```json
{
  "sandbox": {
    "enabled": true,
    "failIfUnavailable": true,
    "allowUnsandboxedCommands": false,
    "filesystem": {
      "denyWrite": ["~/.keypaste", "~/Vaults"]
    }
  },
  "permissions": {
    "deny": ["Edit(~/.keypaste/**)", "Edit(~/Vaults/**)"]
  }
}
```

`failIfUnavailable` makes Claude Code exit rather than run commands unsandboxed when the sandbox cannot start, and `allowUnsandboxedCommands: false` makes it ignore Claude's request to retry a command outside the sandbox. `denyWrite` holds where the working folder or an added folder would otherwise allow the write, and a project's settings can add folders to write but cannot lift it. The `Edit` rules keep Claude's file tools, which the sandbox does not cover, out of the same folders. Where your account can write the `keypaste` the tools start, add it to both lists, in `Edit` rules with `//` before an absolute path: `~/.local/bin/keypaste` and the AppImage on Linux, `/Applications/keypaste.app` on macOS. On Linux and WSL2 a `denyWrite` entry must be a concrete path, and one holding `*`, `?` or `[` is skipped. Keep shells, interpreters and `keypaste` out of `sandbox.excludedCommands`, since an excluded command runs with your access.

- **macOS.** The sandbox is Seatbelt and needs no setup. It blocks every Unix socket that `sandbox.network.allowUnixSockets` does not list, so by Claude Code's documentation a `keypaste run --session` or `keypaste clients` change the agent runs cannot reach the app or `keypaste approve`, while the bridge outside the sandbox still does. Keep keypaste's socket off that list and `allowAllUnixSockets` off. The documentation does not say whether a sandboxed command can read another process; the sandbox runtime's policy in its 0.0.79 source gives process information only for processes in the same sandbox, which keypaste has not tested in Claude Code.
- **Linux and WSL2.** Install `bubblewrap` and `socat`, and on Ubuntu 24.04 and later add the AppArmor profile Claude Code's sandboxing page gives. Unix sockets are blocked only by the optional seccomp filter, which `/sandbox` lists under Dependencies when it is missing; without it the agent's commands can reach keypaste's pipe. The sandbox mounts a fresh `/proc`, which hides other processes' information from a sandboxed command; `enableWeakerNestedSandbox` gives that up, so leave it off outside a container.
- **Windows.** Claude Code runs commands unsandboxed on native Windows, and its documentation points to WSL2 instead. A Claude Code in WSL2 starts a Linux `keypaste mcp`, which needs a `keypaste approve` holding the vault inside WSL2, a setup keypaste has not tested. On native Windows the agent's commands are held back only by Claude Code's permission prompts and rules, which judge a command by its text rather than by what it does.

## Codex

Codex sandboxes the commands its agent runs and the processes they start, on macOS, Linux, WSL2 and native Windows. Its permission settings do not govern MCP servers, which run as processes of their own, so the bridge keeps its access. Under `workspace-write`, commands can write the folder Codex works in, `/tmp`, `$TMPDIR` and any writable root you add, while `.git`, `.agents`, `.codex` and `.aws` inside a writable root stay read-only; anything else needs your approval.

In `~/.codex/config.toml`:

```toml
sandbox_mode = "workspace-write"
approval_policy = "on-request"
approvals_reviewer = "user"

[sandbox_workspace_write]
writable_roots = []
```

`on-request` makes the agent ask you before a command leaves the sandbox, and approving one that touches a path in the table undoes the rest. `approvals_reviewer = "user"` keeps those questions with you rather than Codex's reviewer agent. Add no `--add-dir` or writable root that holds a path in the table, and no rule that lets a shell, an interpreter or `keypaste` run outside the sandbox. Codex reads a project's `.codex/config.toml` only once you trust the project, so read it before you do. These settings cannot keep a path read-only inside a writable folder, so never start Codex in your home folder. Codex's beta permission profiles can (`"~/.keypaste" = "read"` in a profile that extends `:workspace`), but they replace the settings above and keypaste's check does not exercise them.

To see the sandbox refuse a write, run this from the project folder; it fails and leaves no file behind:

```sh
codex sandbox -- /bin/sh -c 'touch ~/.keypaste/sandbox-probe'
```

- **macOS.** The sandbox is Seatbelt and needs no setup. Codex's documentation does not say whether a sandboxed command can read another process; the Seatbelt policy in its 0.162.1 source gives process information only within the sandbox. It treats Unix sockets as an allowlist and calls them an escape hatch, so keep keypaste's pipe off that list. Whether the agent's commands can reach keypaste's pipe without it is not documented, and keypaste has not tested it.
- **Linux and WSL2.** Install `bubblewrap`, and on Ubuntu 24.04 load the AppArmor profile Codex's sandbox page gives. Codex's bubblewrap call in its 0.162.1 source starts each command in a process namespace of its own with a fresh `/proc`, so it does not see other processes; the documentation does not say so.
- **Windows.** The native sandbox is MXC where Windows 11 offers it; otherwise `elevated`, which runs commands as separate lower-privilege users once an administrator approves its setup, or `unelevated`, a restricted token with weaker isolation. Prefer MXC, then `elevated`. The documentation does not say whether a sandboxed command can read your processes or open keypaste's named pipe, and keypaste's check does not run on Windows.

## Cursor

Cursor's sandbox covers the terminal commands its agent runs on macOS and Linux; its documentation describes none on Windows. Sandboxed commands can write the workspace and temporary folders but never Cursor's protected paths, which include `.cursor/*.json`, a project's `.cursor/mcp.json` among them, `.claude/*.json` and `.vscode/`. A command that cannot run in the sandbox runs outside it once approved.

Choose the Allowlist run mode with sandboxing on, in Settings › Agents › Approvals & Execution, keep shells, interpreters and `keypaste` off the allowlist, and keep External-File Protection on, which stops the agent changing files outside the workspace without asking. Auto-review instead sends a command that cannot use the sandbox, and a rerun outside it after a sandbox failure, to a classifier on Cursor's servers, which Cursor says is not a security boundary, and Run Everything uses no sandbox. In `~/.cursor/sandbox.json`:

```json
{
  "type": "workspace_readwrite",
  "additionalReadwritePaths": []
}
```

A project's `.cursor/sandbox.json` takes priority over yours and its paths join yours, so read it before you open a repository that has one. For Cursor's Agent CLI, set `approvalMode` to `allowlist` in `~/.cursor/cli-config.json` and start it with `--sandbox enabled`.

- **macOS.** The sandbox is Seatbelt and needs Cursor 2.0 or later, with no setup.
- **Linux.** The sandbox needs a kernel 6.2 or later with Landlock and unprivileged user namespaces; without them Cursor asks before every command instead. The desktop package carries the AppArmor profile it needs; the Agent CLI does not, and where AppArmor restricts user namespaces it needs Cursor's AppArmor package.
- **Windows.** No sandbox is documented, so the agent's commands are held back only by what you approve.

Cursor's documentation does not say whether a sandboxed command can read other processes or reach a Unix socket, or whether a stdio MCP server you configure runs inside the sandbox. A team's MCP allowlist can give a local server a network mode, No sandbox among them; if your team sandboxes keypaste's entry, check that an agent's request still reaches keypaste.

## What a sandbox does not change

A sandbox confines the agent's own commands. Everything outside it still runs as you, and keypaste's limits there stand:

- Anything that can reach the vault's holder and name an agent set to Automatic gets what that agent gets without a prompt (T-14).
- A command an agent runs through keypaste's `run` tool receives the values, and the agent often wrote what that command does (T-35).
- An agent's settings bind only the bridge that names them (T-36).
- A process running as you outside the sandbox can read memory, files and keystrokes and talk to keypaste's pipe (T-10, T-18).

## How keypaste checks this

ci's `tools` jobs on Linux and macOS run Codex 0.162.1's `codex sandbox` with `sandbox_mode = "workspace-write"` from a project folder: a write in that folder succeeds, appends to `~/.keypaste/clients.toml` and to Codex's `config.toml` are refused and leave both unchanged, and afterwards the bridge a tool registered still starts with its own keypaste home. Nothing else in this guide is checked by keypaste: Claude Code has no command that runs a shell command in its sandbox without a model, Cursor's Agent CLI lists one only as hidden, and Codex's Windows sandbox is not driven ([L.3](steps/L.3.md)).

## Sources

Read on 2026-10-21:

- Claude Code: [Configure the sandboxed Bash tool](https://code.claude.com/docs/en/sandboxing), [All settings](https://code.claude.com/docs/en/settings-reference), [Choose a sandbox environment](https://code.claude.com/docs/en/sandbox-environments), [Configure permissions](https://code.claude.com/docs/en/permissions), [Permission modes](https://code.claude.com/docs/en/permission-modes).
- Codex: [Sandbox](https://learn.chatgpt.com/docs/sandboxing), [Agent approvals & security](https://learn.chatgpt.com/docs/agent-approvals-security), [Permissions](https://learn.chatgpt.com/docs/permissions), [Windows sandbox](https://learn.chatgpt.com/docs/windows/windows-sandbox), [Configuration reference](https://learn.chatgpt.com/docs/config-file/config-reference), [Developer commands](https://learn.chatgpt.com/docs/developer-commands), [Model Context Protocol](https://learn.chatgpt.com/docs/extend/mcp).
- Cursor: [Run Modes](https://cursor.com/docs/agent/security/run-modes), [sandbox.json reference](https://cursor.com/docs/reference/sandbox), [Agent Security](https://cursor.com/docs/agent/security), [MCP](https://cursor.com/docs/mcp), [CLI parameters](https://cursor.com/docs/cli/reference/parameters), [CLI configuration](https://cursor.com/docs/cli/reference/configuration).
- Source, where the documentation is silent: Codex `rust-v0.162.1`'s [`seatbelt_base_policy.sbpl`](https://github.com/openai/codex/blob/rust-v0.162.1/codex-rs/sandboxing/src/seatbelt_base_policy.sbpl) and [`bwrap.rs`](https://github.com/openai/codex/blob/rust-v0.162.1/codex-rs/linux-sandbox/src/bwrap.rs), and `@anthropic-ai/sandbox-runtime` 0.0.79's `dist/sandbox/macos-sandbox-utils.js` on npm.
