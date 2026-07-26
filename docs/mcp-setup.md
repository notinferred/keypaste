# Connecting keypaste to Claude

keypaste ships an MCP server, `keypaste-mcp`, that lets an AI agent see the names of things in your
vault and ask you for one credential at a time.

**Read this first: in this version it grants nothing.** Both tools refuse every call. That is not a
bug and not a misconfiguration — the human approval flow they would need does not exist yet, and
keypaste denies by default rather than granting without one. If you set this up expecting it to hand
Claude a password, it will not, and the refusal will say so.

What you get today is the shape: the server connects, the two tools appear, every call is refused
with a reason, and every call is written to an audit log you can read. That is worth having early
because it is the part that has to be right before anything is ever released.

---

## Before you start

Build the binary:

```sh
dotnet build keypaste.slnx -c Release
```

It lands at `artifacts/bin/Keypaste.Mcp/release/keypaste-mcp` (`keypaste-mcp.exe` on Windows). There
is no installer yet, so note the absolute path — you will need it twice below.

## Claude Desktop

Open the config file — Claude menu → Settings → Developer → Edit Config, or by hand:

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

**Paths must be absolute.** The client's working directory is not yours, and a relative path will
resolve somewhere you did not intend.

**There is no place here to put a master password, and there never will be.** That is the point of
the design, not an omission — see the FAQ.

Restart Claude Desktop. The server appears under the tools icon; if it does not, see
*Troubleshooting*.

## Claude Code

```sh
claude mcp add --transport stdio --scope project keypaste \
  -- /absolute/path/to/keypaste-mcp \
     --vault /absolute/path/to/vault.kdbx \
     --client-label claude-code
```

`--scope project` writes `.mcp.json` in the repository root, which your teammates get too. The
equivalent by hand:

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

A project-scoped `.mcp.json` is **committed to your repository**. Paths are fine in it. Nothing else
about keypaste belongs in it, and there is nothing secret to put there anyway.

Use `--scope local` instead if you would rather keep it to your own machine.

## What the agent may see

By default, only the `env/` subtree — the project variables `keypaste run` uses. Nothing else in
your vault is nameable, let alone readable.

Widen it only if you mean to:

```json
"args": [
  "--vault", "/absolute/path/to/vault.kdbx",
  "--expose", "env/**",
  "--expose", "servers/staging/*"
]
```

> **This file is your approval for what the server may name.** Every glob you add is a set of entry
> names an agent may read. Entry names are not passwords, but a complete inventory of your vault is
> what turns a vague request into a targeted one. Most people should leave the default alone.

Patterns match the group path and the entry title as two separate things, so `*` stays inside one
path segment and `**` spans any number of them. A title containing a slash is matched as a *title*,
so it can never impersonate a deeper group.

## The two tools

**`list_entry_names`** returns group paths and entry names only — never a user name, password, URL or
note. It takes no arguments at all, deliberately: there is no parameter an agent could use to widen
what it sees.

**`request_credential`** takes `entry`, `field`, `reason` and `ttl_seconds`. In this version it
returns DENIED every time and tells the agent not to retry.

Entry names come out of your vault, which means they are written by anyone who can edit it. keypaste
strips control characters, invisible Unicode, and the punctuation that carries structure in what a
model reads, then wraps the whole listing in a marked block that says it is data rather than
instructions. That is a real mitigation and it is not a complete one — see
[THREATS.md](../THREATS.md) T-1, which is honest about what a sanitizer cannot do.

## The audit log

Every call, allowed or refused, appends one JSON line to `~/.keypaste/audit.jsonl`. Set
`KEYPASTE_HOME` to move the directory, or `--audit-log` to move just the file.

```sh
jq -c . < ~/.keypaste/audit.jsonl
```

```json
{"v":1,"ts":"2026-07-26T14:03:11.482Z","seq":1,"pid":48122,
 "client":{"name":"claude-code","version":"1.2.3","label":"claude-code","transport":"stdio"},
 "tool":"request_credential",
 "args":{"entry":"env/dev/STRIPE_KEY","entry_kind":"path","field":"password","ttl_seconds":900,
         "reason_excerpt":"deploy the billing service to staging","reason_len":37,
         "reason_sha256":"..."},
 "decision":"denied","method":"not-implemented",
 "reason":"there is no approval path in this version, so the default deny stands",
 "exposure":["env/**"]}
```

Three things worth knowing:

- **If the log cannot be written, the call is refused.** Not logged-and-continued: refused. The log
  is a precondition, because otherwise breaking it would be the way to get access that leaves no
  trace. If the server will not start, check that `~/.keypaste` is writable.
- **It grows without bound.** keypaste never rotates or trims it, because deleting lines is the
  opposite of what it is for.
- **On Linux and macOS it is created readable only by its owner**, and keypaste tightens an existing
  one that is not, saying so on stderr when it does. **On Windows there is no equivalent** — it
  inherits its directory's permissions, the same gap `keypaste env export` has.

`keypaste log`, which renders this as a readable table, and the per-line hash chain that makes
tampering detectable, both arrive in a later stage. Until then the file is append-only by
construction and nothing more; [THREATS.md](../THREATS.md) T-5 states exactly what that does and
does not claim.

## Checking it works without a client

```sh
printf '%s\n' \
  '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"probe","version":"1.0"}}}' \
  '{"jsonrpc":"2.0","method":"notifications/initialized"}' \
  '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}' \
  | keypaste-mcp --vault ~/vault.kdbx --audit-log /tmp/probe.jsonl
```

You should get two JSON lines back, the second listing `list_entry_names` and `request_credential`.
CI runs a stricter version of exactly this on all three operating systems
(`scripts/verify-mcp-stdio.sh`).

## Troubleshooting

**The server shows as failed to start.** Check the path is absolute and the file is executable
(`chmod +x`). Then check `~/.keypaste` is writable — an unwritable audit log stops the server on
purpose.

**Every call says the vault is locked.** Expected in this version. There is no way to unlock a vault
from an MCP server yet; see the FAQ and [THREATS.md](../THREATS.md) T-7.

**Nothing in the audit log.** The server never started. Claude Desktop keeps its own log at
`%APPDATA%\Claude\logs\mcp-server-keypaste.log` (macOS: `~/Library/Logs/Claude/`), which captures
everything keypaste writes to stderr.

**The client reports a protocol error.** Something is writing to stdout, which on a stdio MCP server
is the protocol stream. keypaste is careful never to do this and CI asserts it, so suspect a shell
profile or a wrapper script that prints a banner.

## FAQ

**Can the agent see my passwords?** No. No code path in this version reads a field value at all —
`request_credential` contains no vault access whatsoever, which is checkable by reading one short
file. When it does, in a later stage, it will be after you have approved that specific request.

**Can it see my entry names?** Not yet, because the vault is locked. When it can, only the ones
inside `--expose`, which defaults to `env/**`.

**Why can't it unlock the vault?** An MCP server's stdin and stdout *are* the protocol stream, and
Claude Desktop starts it with no terminal, so there is nowhere to prompt you. The two obvious
workarounds are both worse than waiting: putting the master password in the client's config file
would place the secret that protects every other secret into a plaintext JSON file, and asking the
client to collect it would route it through the untrusted party. The unlock channel arrives with the
approval channel, and whatever owns one should own the other.

**Does anything leave my machine?** No. `keypaste-mcp` speaks stdio only and opens no sockets, and
you do not have to take that on faith: its entire dependency list is four packages pinned by content
hash in `src/Keypaste.Mcp/packages.lock.json`, and there is no HTTP client among them. Read it.

**Can a malicious MCP client pretend to be Claude?** Yes, and keypaste never makes a decision based
on the name a client gives itself — it is recorded, not trusted. `--client-label` is the name *you*
gave the server in your own config, which is why it is the one worth putting in the log.
[THREATS.md](../THREATS.md) T-3 is explicit about what this does and does not buy.

**Is it safe to point this at my personal vault?** In this version, in the narrow sense that nothing
can be released: yes. The broader answer is that exposure defaults to `env/**` precisely so that the
question does not depend on your judgement about a glob.
