# Connecting keypaste to Claude

keypaste ships an MCP server, `keypaste-mcp`, that lets an AI agent see the names of things in your
vault and ask you for one credential at a time.

**Read this first: `keypaste-mcp` on its own grants nothing.** It holds no vault and makes no
decision. Everything it can do beyond refusing depends on a second process you start yourself:

```sh
keypaste agent --vault ~/vaults/personal.kdbx
```

That is where your master password is typed, and where you are asked about each request. Set this up
without it and every call is refused with a message telling you — or telling Claude to tell you — to
start one. See [**Approving an agent's request**](approvals.md) for what you actually see and decide.

The split is deliberate: your MCP client starts `keypaste-mcp`, so `keypaste-mcp` is started by
software. `keypaste agent` is started by a person, which is what makes it the only thing that ever
asks for a master password.

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

`decision` is `granted` or `denied`. `method` says how it was reached, and the distinction is the
useful part when you are reading this back later:

| `method` | What happened |
|---|---|
| `prompt` | A person was shown this exact request and answered it. With `denied`, they said no. |
| `grant-cache` | Served from a grant a person had already given, inside its lifetime. **They did not see this request's reason** — compare it with the earlier `prompt` line for the same entry. |
| `policy` | Released by a standing rule you wrote in `~/.keypaste/policy.toml`. **Nobody was asked at all**, and no `prompt` line exists to compare against — the `reason` field names which rule did it. See [policy.md](policy.md). |
| `policy-limit` | A rule covered the request but had spent its `max_per_hour` allowance. |
| `exposure` | A listing, allowed because everything named was inside your `--expose` globs. |
| `no-approver` | Nobody was running `keypaste agent`. |
| `out-of-scope` | The entry was outside your globs, or does not exist — deliberately the same answer, so an agent cannot use the difference to find out what exists. |
| `timed-out` / `busy` / `cooldown` | Nobody answered in time; somebody was already answering another; or the same request was refused a moment ago. |
| `vault-locked` / `invalid-request` / `failed` | No vault open; the arguments were wrong; something went wrong. |

Four things worth knowing:

- **The value is never in here.** `field` records *which* field was asked for, never its contents,
  and no field of this record can hold one. The agent's `entry` argument is recorded, sanitized;
  entry titles read out of your vault are not.

- **If the log cannot be written, the call is refused.** Not logged-and-continued: refused. The log
  is a precondition, because otherwise breaking it would be the way to get access that leaves no
  trace. If the server will not start, check that `~/.keypaste` is writable.
- **It grows without bound.** keypaste never rotates or trims it, because deleting lines is the
  opposite of what it is for.
- **On Linux and macOS it is created readable only by its owner**, and keypaste tightens an existing
  one that is not, saying so on stderr when it does. **On Windows there is no equivalent** — it
  inherits its directory's permissions, the same gap `keypaste env export` has.

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

`--since` takes a span (`30m`, `2h`, `7d`) or a moment (`2026-07-20`, or a full timestamp), and
`--client` matches any part of the label or the name. **A filtered view always says so**, with the
count it is showing out of the count in the file, so a narrow view can never be mistaken for the
whole log.

## Knowing it has not been edited

Every record carries `prev` — the hash of the record before it — and `hash`, over its own bytes. So a
record cannot be changed without breaking the link declared by the record after it.

```sh
keypaste log verify
```

```
3 records verified in /home/you/.keypaste/audit.jsonl.
Latest: seq 3, hash 651f0392457b29f80f3168584758418c71734077577a9c100e83225e1783dde8
```

It exits `5` if the chain is broken, and names the line and what happened to it — edited, removed,
inserted, or written by something that is not keypaste. `keypaste log` runs the same check and puts a
warning in front of the table rather than quietly showing you a file that has been altered.

**Two things it cannot do, and it says both on every pass.** The chain holds no secret, so anyone who
can write the file can recompute the whole of it; and records deleted from the *end* leave a chain
that is internally perfect, because nothing follows them to notice. For the second, write down the
hash it prints and pass it back later:

```sh
keypaste log verify --expect 651f0392457b29f80f3168584758418c71734077577a9c100e83225e1783dde8
```

That fails unless a record whose own bytes still hash to it is in the file — not merely that those
characters appear somewhere in it, which an entry name could be made to say. keypaste keeps no copy
of the anchor, on purpose: one stored next to the thing it anchors is worth nothing.
[THREATS.md](../THREATS.md) T-5 states all of this as residuals rather than leaving it to be
discovered.

Records written before this feature existed carry `"v":1` and no chain. They are reported as
predating it and are never called tampered — and `keypaste log` marks them, and anything else the
chain cannot vouch for, with a `?` in the left-hand column:

```
?  2026-07-26 14:10:00  claude-code  env/prod/PAYROLL_DB  granted   prompt

?  the hash chain does not vouch for this row. Run 'keypaste log verify'.
```

A line something else appended does not stop keypaste writing: it links past it to the last record
that is part of the chain, and `keypaste log verify` reports the line. The one thing that does stop
it is a record from a *newer* keypaste, because appending beneath that would fork the chain — upgrade,
or move the file aside to start a new log. The old file stays readable and stays verifiable.

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

**Every call is refused with "no keypaste agent is running".** Start one:
`keypaste agent --vault <path>`. It has to be running, and pointed at the same vault, for anything
to be granted. If it is running and you still see this, the two are looking at different pipe names
— pass the same `--approver <name>` to both, or set `KEYPASTE_APPROVER` for both.

**Every call says the vault is locked.** The agent is running but has no vault open. That normally
means it is still asking for the master password, or you got it wrong and it exited.

**Nothing in the audit log.** The server never started. Claude Desktop keeps its own log at
`%APPDATA%\Claude\logs\mcp-server-keypaste.log` (macOS: `~/Library/Logs/Claude/`), which captures
everything keypaste writes to stderr.

**The client reports a protocol error.** Something is writing to stdout, which on a stdio MCP server
is the protocol stream. keypaste is careful never to do this and CI asserts it, so suspect a shell
profile or a wrapper script that prints a banner.

## FAQ

**Can the agent see my passwords?** One field of one entry, after you have said yes to that exact
request, for as long as the lifetime you were shown. Never more than one field, and never anything
you did not approve — `keypaste-mcp` itself contains no vault access whatsoever, which is checkable
by reading one short file.

**Can it see my entry names?** Only the ones inside `--expose`, which defaults to `env/**`, and only
while an agent is running with the vault unlocked.

**Why does `keypaste-mcp` not just ask me for the master password?** Because your MCP client starts
it, which means software starts it — and a password prompt that software can cause to appear is a
prompt any program on your machine can imitate. There is also nowhere to put one: an MCP server's
stdin and stdout *are* the protocol stream, and Claude Desktop starts it with no terminal. Putting
the password in the client's config would place the secret that protects every other secret into a
plaintext JSON file; asking the *client* to collect it would route it through the untrusted party.
So the prompt lives in a process you start. [DECISIONS.md D-0023](../DECISIONS.md).

**Do I have to approve every single call?** No. A repeat request for the same field of the same
entry, from the same connection, inside the lifetime you approved, is served without asking again.
Change that with `--max-ttl` on the agent.

**Does anything leave my machine?** No. `keypaste-mcp` speaks stdio only and opens no sockets, and
you do not have to take that on faith: its entire dependency list is four packages pinned by content
hash in `src/Keypaste.Mcp/packages.lock.json`, and there is no HTTP client among them. Read it.

**Can a malicious MCP client pretend to be Claude?** Yes, and keypaste never makes a decision based
on the name a client gives itself — it is recorded, not trusted. `--client-label` is the name *you*
gave the server in your own config, which is why it is the one worth putting in the log, and why it
is the only name a policy rule will match. That stops the *agent* choosing which rules apply to it;
it does not stop another program on your machine starting a bridge with the same argv.
[THREATS.md](../THREATS.md) T-3 and T-14 are explicit about what this does and does not buy.

**Is it safe to point this at my personal vault?** Nothing is released without you saying yes to
that specific request, unless you wrote a policy rule covering it — and there is no policy file
unless you make one. Exposure defaults to `env/**` precisely so that the question does not depend on
your judgement about a glob.
