# Connecting keypaste to Claude

keypaste ships an MCP server, `keypaste-mcp`, that lets an AI agent see the names of things in your vault and ask you for one credential at a time.

The bridge holds no vault. It checks exposure and records access; the approver handles consent, grants and policy. Start the current terminal approver yourself:

```sh
keypaste agent --vault ~/vaults/personal.kdbx
```

Enter the master password and review requests in that terminal. Without the approver, calls are refused with startup instructions. [Approvals](approvals.md) explains the prompt.

The MCP client starts the bridge, while you start the approver. This keeps software-triggered requests from opening a master-password prompt.

These are the current CLI/MCP instructions. The desktop cannot approve requests or supply its unlocked session yet, and locking it does not stop a separate terminal approver. [STEPS](STEPS.md) covers the focused target: one unlock session with native approval and denial in the app.

## Before you start

Build the binary:

```sh
dotnet build keypaste.slnx -c Release
```

It lands at `artifacts/bin/Keypaste.Mcp/release/keypaste-mcp` (`keypaste-mcp.exe` on Windows). The CLI is under `artifacts/bin/Keypaste.Cli/release/`; make both built executables available on `PATH` for the examples below, or use their full paths. Published downloads are listed in [RELEASE](RELEASE.md).

## The short way

`setup` is available in CLI/MCP `v0.2.0` and later, including the published `v0.3.0`. `v0.1.0` requires manual configuration. Building the source does not replace an older binary already on `PATH`.

```sh
keypaste setup --vault ~/vaults/personal.kdbx
```

The command detects installed clients and configures Claude Code and Codex through their own commands. It prints configuration for Cursor and Claude Desktop to paste manually; those file formats have not been verified against real installs.

```
keypaste-mcp   /home/you/.local/bin/keypaste-mcp
vault          /home/you/vaults/personal.kdbx
exposure       env/** (the default; nothing else in the vault can even be named)

  claude-code      configured
  codex            configured
  cursor           has no command of its own; add this by hand:
  ...
```

`--dry-run` prints commands without changing configuration. `--remove` removes keypaste while preserving other servers. Repeating setup is idempotent and can update a moved vault path.

`setup` configures paths and exposure. Credential release still requires a running approver and authorization.

The following sections show manual configuration, including clients `setup` does not recognize.

## Claude Desktop

Open Claude menu → Settings → Developer → Edit Config, or edit the file directly:

| | |
| --- | --- |
| macOS | `~/Library/Application Support/Claude/claude_desktop_config.json` |
| Windows | `%APPDATA%\Claude\claude_desktop_config.json` |
| Linux | `~/.config/Claude/claude_desktop_config.json` |

```json
{
  "mcpServers": {
    "keypaste": {
      "command": "/absolute/path/to/keypaste-mcp",
      "args": [
        "--vault", "/absolute/path/to/vault.kdbx",
        "--client-label", "claude-desktop"
      ]
    }
  }
}
```

On Windows the backslashes must be escaped: `"C:\\Users\\you\\keypaste-mcp.exe"`.

Paths must be absolute. The client's working directory is not yours, and a relative path will resolve somewhere you did not intend.

Keep the master password out of client configuration; enter it only in the approver terminal.

Restart Claude Desktop. The server appears under the tools icon; if it does not, see Troubleshooting.

## Claude Code

```sh
claude mcp add --transport stdio --scope project keypaste \
  -- /absolute/path/to/keypaste-mcp \
     --vault /absolute/path/to/vault.kdbx \
     --client-label claude-code
```

`--scope project` writes `.mcp.json` in the repository root, which your teammates get too. The equivalent by hand:

```json
{
  "mcpServers": {
    "keypaste": {
      "command": "/absolute/path/to/keypaste-mcp",
      "args": ["--vault", "/absolute/path/to/vault.kdbx", "--client-label", "claude-code"]
    }
  }
}
```

A project-scoped `.mcp.json` is a file in the repository that you can commit; the command does not commit it. Absolute local paths usually differ between teammates and can reveal usernames or directory names. Keep master passwords and credentials out of it.

Use `--scope local` instead if you would rather keep it to your own machine.

## What the agent may see

Exposure defaults to the `env/` subtree used by `keypaste run`. Other entries cannot be named or read through the bridge.

To expose another group, add explicit patterns:

```json
"args": [
  "--vault", "/absolute/path/to/vault.kdbx",
  "--expose", "env/**",
  "--expose", "servers/staging/*"
]
```

Each exposure glob authorizes disclosure of matching entry names. Review additions carefully: names can reveal a vault inventory and help target later requests.

Patterns match the group path and the entry title as two separate things, so `*` stays inside one path segment and `**` spans any number of them. A title containing a slash is matched as a title, so it can never impersonate a deeper group.

## The two tools

`list_entry_names` takes no arguments and returns only exposed group paths and entry names. It cannot return usernames, passwords, URLs or notes, or widen exposure.

`request_credential` takes `entry`, `field`, `reason` and `ttl_seconds`. It forwards the request to `keypaste agent` for approval or a matching policy rule and returns one field. `--max-ttl` caps approval reuse, not the lifetime of the returned credential. Without an approver it refuses and names the startup command. [The demo](demo.md) shows this flow.

Anyone who can edit the vault can influence its entry names. keypaste removes control characters, invisible Unicode and structural punctuation, then labels the listing as data. Sanitization cannot eliminate prompt injection; [THREATS.md](../THREATS.md) T-1 describes the residual risk.

## The audit log

Every call, allowed or refused, appends one JSON line to `~/.keypaste/audit.jsonl`. Set `KEYPASTE_HOME` to move the directory, or `--audit-log` to move just the file.

```sh
jq -c . < ~/.keypaste/audit.jsonl
```

```json
{"v":2,"ts":"2026-07-26T14:03:11.482Z","seq":1,"pid":48122,
 "client":{"name":"claude-code","version":"1.2.3","label":"claude-code","transport":"stdio"},
 "tool":"request_credential",
 "args":{"entry":"env/dev/STRIPE_KEY","entry_kind":"path","field":"password","ttl_seconds":900,
         "reason_excerpt":"deploy the billing service to staging","reason_len":37,
         "reason_sha256":"..."},
 "decision":"granted","method":"prompt",
 "reason":"a person approved this request for 300 seconds",
 "exposure":["env/**"],
 "prev":"0000...0000","hash":"0c806dbd...14b3c7"}
```

`decision` is `granted` or `denied`; `method` records how the decision was reached:

| `method` | What happened |
|---|---|
| `prompt` | A person was shown this exact request and answered it. With `denied`, they said no. |
| `grant-cache` | A live approval was reused without showing the new reason. Compare with its original `prompt` record. |
| `policy` | A standing rule released the field without prompting. The reason identifies the rule; see [policy.md](policy.md). |
| `policy-limit` | A rule covered the request but had spent its `max_per_hour` allowance. |
| `undeliverable` | A person or policy authorized the request, but the value exceeded the reply limit. Nothing was released; the reason identifies the authorization source. |
| `exposure` | A listing, allowed because everything named was inside your `--expose` globs. |
| `no-approver` | Nobody was running `keypaste agent`. |
| `out-of-scope` | The entry was outside exposure or absent. A shared response prevents existence checks outside exposure. |
| `timed-out` / `busy` / `cooldown` | Nobody answered in time; the connection was already carrying another call, so this one was refused rather than queued behind it; or the same request was refused a moment ago. |
| `cancelled` | The client stopped waiting before anybody answered. Nobody decided anything. |
| `vault-locked` / `invalid-request` / `failed` | No vault open; the arguments were wrong; something went wrong. |
| `not-initialized` | The client called a tool before finishing the MCP handshake. Denied, with the fix named; nothing was decided. |
| `not-implemented` | Written by the early implementation of roadmap step 2.1, before approval existed; this is a step ID, not a released version. Nothing writes it now, and it is listed because the log is append-only: old records keep the word they were written with. |

The returned value is excluded from the log. `field` records the requested field. Current source logs the sanitized resolved entry path when available, including for opaque handles, otherwise the sanitized request argument. Published `v0.1.0` records the request argument. Keep secret values out of entry names and reason excerpts.

Calls are refused if the audit record cannot be written. An unwritable `~/.keypaste` can also prevent startup. The log grows without automatic rotation or trimming. On Linux and macOS, keypaste creates owner-readable logs and tightens existing permissions with a stderr notice. Windows logs inherit their directory permissions without an equivalent check.

## Reading it

```sh
keypaste log
keypaste log --denied
keypaste log --client claude-code --since 2h
```

```
3 records in /home/you/.keypaste/audit.jsonl

  time (UTC)           client       entry               decision  method
  2026-07-26 14:03:09  claude-code  -                   granted   exposure
  2026-07-26 14:03:11  claude-code  env/dev/STRIPE_KEY  granted   prompt
  2026-07-26 14:07:44  claude-code  env/dev/STRIPE_KEY  granted   grant-cache (!)

(!) served from an earlier approval, under a reason that person never saw.
```

`--since` accepts a span (`30m`, `2h`, `7d`) or timestamp (`2026-07-20` or a full timestamp). `--client` matches part of a label or name. Filtered views show both the selected and total record counts.

## Knowing it has not been edited

Each record contains its predecessor's hash in `prev` and its own hash in `hash`. Changing a record breaks the next record's declared link.

```sh
keypaste log verify
```

```
3 records verified in /home/you/.keypaste/audit.jsonl.
Latest: seq 3, hash 651f0392457b29f80f3168584758418c71734077577a9c100e83225e1783dde8
```

`keypaste log verify` exits 5 for a broken chain and identifies the line and problem. `keypaste log` performs the same check and warns before displaying affected records.

The chain has no secret key, so a file writer can recompute it. Deleting final records also leaves an internally valid chain. Both limits are printed on every successful verification. To detect loss of a previously observed record, retain its hash and supply it later:

```sh
keypaste log verify --expect 651f0392457b29f80f3168584758418c71734077577a9c100e83225e1783dde8
```

`--expect` requires a record whose bytes still hash to the supplied value. keypaste does not store the anchor beside the log because a writer could replace both. [THREATS.md](../THREATS.md) T-5 covers these limits.

Older `v:1` records lack a chain and are reported as predating it. `keypaste log` marks these and other unverifiable rows with `?`:

```
  time (UTC)           client       entry                decision  method
? 2026-07-26 14:10:00  claude-code  env/prod/PAYROLL_DB  granted   prompt

?  the hash chain does not vouch for this row. Run 'keypaste log verify'.
```

Unrecognized appended lines are reported by verification; new keypaste records link to the last valid chain record. Records from a newer keypaste version stop appending to avoid a fork. Upgrade or move the file aside to start a new log; the old file remains readable and verifiable.

## Checking it works without a client

```sh
printf '%s\n' \
  '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"probe","version":"1.0"}}}' \
  '{"jsonrpc":"2.0","method":"notifications/initialized"}' \
  '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}' \
  | keypaste-mcp --vault ~/vault.kdbx --audit-log /tmp/probe.jsonl
```

You should get two JSON lines back, the second listing `list_entry_names` and `request_credential`. CI runs a stricter version of exactly this on all three operating systems (`scripts/verify-mcp-stdio.sh`).

## Troubleshooting

If startup fails, check the executable path is absolute, the file is executable (`chmod +x`) and `~/.keypaste` is writable.

If calls report "no keypaste agent is running", start `keypaste agent --vault <path>` for the intended vault. If it is already running, check that both processes use the same `--approver <name>` or `KEYPASTE_APPROVER`. In `v0.2.0`, a half-second connection deadline can also produce this refusal under load; retry in that case. `v0.3.0` repairs that race (F.9).

A call says the vault is locked. The approver reported that no vault was available. Check its terminal and restart it with the intended vault if needed. The current CLI approver opens its pipe after successful unlock; a failed unlock and exit normally produce `no-approver` instead.

Nothing in the audit log. Confirm that the client actually called a tool, then check whether `KEYPASTE_HOME` or `--audit-log` changed the destination. A startup or write failure is another possibility; inspect the client's MCP server log for keypaste's stderr.

For protocol errors, inspect wrappers and shell profiles for text written to stdout. A stdio MCP server reserves stdout for protocol messages; CI checks that keypaste follows this rule.

## FAQ

Can the agent see my passwords? Each successful request returns one field of one entry under a human approval, its still-live cached grant, or a matching policy rule. Repeated approved requests can accumulate credentials. TTL bounds cached approval reuse; it cannot erase values already returned to the client or expire them at their provider. `keypaste-mcp` holds no vault, but it does receive and forward the released value.

Can it see my entry names? Only the ones inside `--expose`, which defaults to `env/**`, and only while the terminal approver is running with its vault unlocked.

Can it change my vault? The current MCP surface only lists names and requests values; it cannot add, edit or delete entries. Desktop and CLI editing are separate workflows.

Will it see my desktop edits immediately? No. The current terminal approver holds its own snapshot. Stop and reopen it after changing the file to read the saved values.

The master-password prompt belongs in the process you start. An MCP client can trigger bridge startup, its stdin and stdout carry the protocol, and desktop clients provide no terminal. A configuration password would be plaintext, while client-mediated input would expose it to the requester. [D-0023](decisions-archive.md) records this design.

Do I have to approve every single call? No. A repeat request for the same field of the same entry, from the same connection, inside the lifetime you approved, is served without asking again. Change that with `--max-ttl` on the agent. A [policy rule](policy.md) can authorize matching releases without an initial prompt.

Does anything leave my machine? The keypaste bridge uses local stdio and local IPC; it does not send vault data to a hosted service. Your MCP client receives the tool result and may send it to a remote model and retain it in transcripts or session files. The local bridge does not make the rest of that client workflow local. See [the demo's limits](demo.md#the-honest-limits).

A client can claim another client's name, but authorization does not use that name. Policy matches the configured `--client-label`; the agent cannot change that label, although another local program can launch a bridge with the same arguments. [THREATS.md](../THREATS.md) T-3 and T-14 describe the boundary.

Should I point this at my personal vault? The default exposure is `env/**`; only entries inside it are available through this bridge. Review that subtree, any policy rules and the client's retention behavior before using real credentials. An approval permits cached reuse on the same connection until expiry, and returned values are outside keypaste's control. Keep unrelated or high-impact credentials in a separate vault when they need a different access boundary.
