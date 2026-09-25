# Pre-approving with a policy file

A policy file authorizes matching credential requests without a prompt. The file exists only if you write it. Any error disables all rules, and requests follow the ordinary prompt and cached-approval path.

<a id="the-short-version"></a>

```sh
$EDITOR ~/.keypaste/policy.toml
keypaste policy ls
keypaste agent --vault ~/vaults/personal.kdbx
```

```toml
[[allow]]
client          = "claude-code"
entries         = ["env/dev/**"]
fields          = ["password"]
max_ttl_seconds = 300
max_per_hour    = 20
```

`keypaste agent` reads the file but never writes it. There is no command to edit authorization rules that an agent could persuade someone to run. In source, the desktop app does not read it: every release from a vault the app holds needs a press of Allow once or Allow for 1 hour in [its prompt](approvals.md#approving-in-the-desktop-app).

<a id="read-this-part-before-you-write-a-rule"></a>

## Pattern matching

Patterns split into a group path and title. The last segment is the title unless it is exactly `**`. Thus `env/dev*` matches `env/devops_ROOT_TOKEN` but nothing under `env/dev/`.

Use `env/dev/**` for that subtree. Policy uses the same matching language as `--expose`.

`keypaste policy ls` shows the parsed group and title separately:

```
1. The client labelled "claude-code"
   may read the password of entries whose
     group path matches   env
     title matches        dev*
   for up to 5 minutes, without asking you.
   No limit on how often.
```

Check both parsed parts before enabling the rule.

<a id="the-keys"></a>

## Rule keys

| Key | Required | What it is |
| --- | --- | --- |
| `client` | yes | The `--client-label` the bridge was started with. `"*"` means any labelled client. |
| `entries` | yes | Patterns, in the same syntax as `--expose`. Up to 16, each up to 128 characters. |
| `fields` | yes | Any of `password`, `username`, `url`, `notes`. |
| `max_ttl_seconds` | yes | 1 to 3600. The agent gets the smaller of this and `--max-ttl`. |
| `max_per_hour` | no | 1 to 1000 releases an hour through this rule. Omitted means no limit. |

Every required key must be present. Missing `fields`, `entries` or `client` disables the file, preventing a misspelled key from widening access.

The first matching rule decides. If its hourly allowance is exhausted, the request is refused without trying another rule or prompting.

<a id="client-is-the-label-you-wrote-not-the-name-the-agent-claims"></a>

### Client labels

The MCP client's self-reported name is unauthenticated and never used for authorization.

What a rule matches is `--client-label`, which you write into your MCP client's configuration:

```json
{ "command": "keypaste-mcp", "args": ["--client-label", "claude-code", "--expose", "env/**"] }
```

The configured label prevents the agent from selecting a policy identity. Another local program can still start a bridge with the same arguments and receive the same policy access. THREATS.md T-14 describes this boundary.

A bridge started with no `--client-label` matches no rule at all, including one written `"*"`.

## What a rule cannot do

A rule applies only within `--expose`, which is checked against the resolved entry first. `entries = ["**"]` on a bridge configured with `--expose "env/**"` still reaches only `env/**`. A rule can lower `--max-ttl` but cannot raise it. An hour-long rule under `keypaste agent --max-ttl 60` grants sixty seconds. A recent explicit refusal takes precedence over a policy rule. `list_entry_names` uses `--expose` alone; policy does not affect listing. A rule matches the path an entry has now, so organizing the vault changes which rule covers what: renaming a project moves every credential in it under the rules written for the new name, and out from under the rules written for the old one. Nothing warns about that — [THREATS](../THREATS.md) T-13 records it.

<a id="anything-wrong-means-everything-asks-you"></a>

## Load errors

Only the Usable state supplies rules. Other states follow the ordinary approval path without giving the requester the loading error. The terminal reports the state; policy releases name the rule in the audit and response.

| State | What `keypaste agent` says |
| --- | --- |
| No file | `policy: no file at …, so every request is shown to you.` |
| Empty, or comments only | `policy: no rules in …, so every request is shown to you.` |
| Usable | `policy: 2 rules from … [sha256:a1b2c3d4]. keypaste policy ls shows them.` |
| Malformed | `policy: … is NOT in force - line 7: 'entires' is not a key keypaste understands` |
| Unreadable | `policy: … is NOT in force - it could not be read: …` |
| Writable by others | `policy: … is NOT in force - it is writable by users other than its owner` |

Any invalid line disables the whole file because a partial interpretation could widen access.

A policy-loading error does not stop the approver from starting.

## The file format

The parser accepts a subset of TOML: `#` comments, `[[allow]]` sections, and `key = value` with double-quoted strings, whole numbers or arrays of double-quoted strings.

Unsupported syntax produces a parse error, including dotted keys, inline tables, single-quoted or multiline strings, floats, booleans, dates, hex, singular `[table]` headers and trailing commas. An unknown `[[deny]]` section also invalidates the file; unsupported rules cannot be silently skipped.

Patterns and labels cannot contain characters that hide their meaning when printed, such as bidi overrides, zero-width spaces or Unicode tags.

## Permissions

Your policy file decides what an agent may take without asking, so anything that can write it can grant that access.

On Linux and macOS, keypaste refuses a policy file or `~/.keypaste` directory writable by anyone except its owner. It leaves permissions unchanged to avoid a repair race and preserve evidence. Run `chmod 600 ~/.keypaste/policy.toml` and `chmod 700 ~/.keypaste`, then restart.

Windows has no equivalent permission check, as with the audit log and `env export`.

Keep the policy file out of synced folders. Pointing `KEYPASTE_HOME` at Dropbox or iCloud lets another machine change this machine's authorizations.

<a id="it-is-read-once-at-startup"></a>

## Reloading policy

`keypaste agent` reads the file once at startup. Changes take effect after restarting and entering the master password again.

`keypaste policy ls` reads the current file, which can differ from a running approver's copy. Both commands print a short hash of the bytes they read:

```
2 rules, from /home/you/.keypaste/policy.toml [sha256:e75ea9d3]
```

## What is written down

Every release through a rule appends a line to `~/.keypaste/audit.jsonl` with `"decision":"granted"` and `"method":"policy"`, and the reason names which rule:

```json
{"decision":"granted","method":"policy","reason":"pre-authorized by policy rule allow#1 (env/dev/**, password)"}
```

`method: prompt` is reserved for requests a person was shown. An exhausted rule records `method: policy-limit`.

The agent also prints one line to its own terminal per release:

```
keypaste: released env/dev/STRIPE_KEY to allow#1 for 300s without asking
```

<a id="the-honest-limits"></a>

## Narrowing one client

`policy.toml` adds releases; `~/.keypaste/clients.toml` only takes them away, one client at a time, keyed by the same `--client-label`. Each client has one of three policies:

| Policy | What it does |
|---|---|
| `session` | Session grants up to 1 hour: a person may give a timed grant and, in `keypaste agent`, a rule here may apply. The default. |
| `ask` | Ask every time: no timed grant is offered or used and no rule here applies. |
| `inject-only` | `request_credential` is refused before anything is read; `run` under `--allow-run` is still asked about. |

```sh
keypaste mcp policy                       # list them; --json prints the rows
keypaste mcp policy '*' ask               # every client without a row of its own
keypaste mcp policy claude-code session
```

The desktop's Agents screen sets the same file. Unlike this file's `"*"`, the `*` row there also covers a bridge started with no label, because it can only narrow; put the strict policy on `*`, since a label is whatever the client's configuration says and an agent that can edit that configuration can change it ([THREATS.md](../THREATS.md) T-36). The holder of the vault reads the file at every request, so a change applies to the next one. A file it cannot read or parse refuses every agent request until it is fixed or deleted. No policy covers `keypaste run --session` from an agent's own shell.

## Limits

A rule covers the vault's current contents. Anyone able to edit or synchronize that subtree can change what it covers. Moving `personal/bank` into `env/dev` makes it match `env/dev/**`. Policy releases show no prompt, and nobody reviews the stated reason at release time. Narrow `entries`, a low `max_per_hour` and a short `--max-ttl` limit access. Each policy request is checked against the rule and its allowance; policy releases do not populate the prompt approval cache. The returned lifetime cannot erase client copies or expire credentials at their provider.

`keypaste policy ls` shows pattern semantics but cannot preview matching entries. That requires an unlocked vault and is deferred to the GUI. The audit log names each applied rule so `keypaste log` can show its past releases.

THREATS.md T-13 through T-17 are the full versions of these.

[Claude asks for a key, you approve, the deploy runs](demo.md) shows the prompt a policy rule replaces.

## Verifying it yourself

`scripts/verify-policy-e2e.sh` tests real approver and MCP processes on Linux, macOS and Windows. It checks silent policy release, ordinary prompting outside the rule, exposure and TTL ceilings, refusal of unlabelled bridges and malformed policies, and exclusion of credentials from the audit log.
