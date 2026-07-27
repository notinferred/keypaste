# Pre-approving with a policy file

Everything in keypaste up to now asks you. A policy file is how you say yes in advance to a narrow,
repeating case — so `keypaste agent` releases that one credential without drawing a prompt.

**This is the only feature in keypaste that hands an agent a secret with nobody watching.** That is
the point of it, and it is also the reason every part of it is built to fail towards asking you.
There is no policy file unless you write one, and anything at all wrong with the one you wrote means
the whole of it is ignored and every request comes back to you.

## The short version

```sh
$EDITOR ~/.keypaste/policy.toml
keypaste policy ls          # check it means what you meant
keypaste agent --vault ~/vaults/personal.kdbx
```

```toml
[[allow]]
client          = "claude-code"     # the --client-label you gave the bridge
entries         = ["env/dev/**"]    # which entries
fields          = ["password"]      # which field of them
max_ttl_seconds = 300               # the longest grant to issue
max_per_hour    = 20                # optional; omitted means no limit
```

keypaste reads this file and never writes it. There is no `keypaste policy add`, on purpose: a
command that edits your authorization file is a command an agent could talk somebody into running.

## Read this part before you write a rule

**`entries = ["env/dev*"]` almost certainly does not mean what you think.**

A pattern is split into a *group path* and a *title*. Unless the last segment is exactly `**`, the
last segment is the **title**. So `env/dev*` means *group exactly `env`, title starting `dev`*. It
matches nothing at all under `env/dev/`, and it does match an entry sitting directly in `env` called
`devops_ROOT_TOKEN` — which you probably were not thinking about.

For a subtree, write `env/dev/**`.

This is not a quirk keypaste can fix without inventing a second, subtly different matching language
from the one `--expose` already uses, which is worse. What it does instead is never echo your line
back at you. `keypaste policy ls` prints the two halves your pattern actually parsed to:

```
1. The client labelled "claude-code"
   may read the password of entries whose
     group path matches   env
     title matches        dev*
   for up to 5 minutes, without asking you.
   No limit on how often.
```

Read that block, not the line you typed. If the two halves are not what you meant, the rule is not
what you meant.

## The keys

| Key | Required | What it is |
| --- | --- | --- |
| `client` | yes | The `--client-label` the bridge was started with. `"*"` means any *labelled* client. |
| `entries` | yes | Patterns, in the same syntax as `--expose`. Up to 16, each up to 128 characters. |
| `fields` | yes | Any of `password`, `username`, `url`, `notes`. |
| `max_ttl_seconds` | yes | 1 to 3600. The agent gets the smaller of this and `--max-ttl`. |
| `max_per_hour` | no | 1 to 1000 releases an hour through this rule. Omitted means no limit. |

**Nothing defaults.** A missing `fields` does not mean every field; a missing `entries` does not mean
everything; a missing `client` does not mean anyone. Each of those would be a way for a typo in a key
name to silently widen a rule, so every one of them is a refusal instead.

Rules are tried in order and the first one that matches decides. A rule that matches but has spent
its hourly allowance refuses the request — it does not fall through to the next rule, and it does not
escalate to a prompt.

### `client` is the label *you* wrote, not the name the agent claims

The name an MCP client asserts about itself is unauthenticated: any process that can start
`keypaste-mcp` can call itself `claude-code`. keypaste has never made an authorization decision from
it and does not start now.

What a rule matches is `--client-label`, which you write into your MCP client's configuration:

```json
{ "command": "keypaste-mcp", "args": ["--client-label", "claude-code", "--expose", "env/**"] }
```

Be clear about what this buys, because it is less than it looks. It means the *agent* cannot choose
which rules apply to it. It does not mean another program on your machine could not start a bridge
with the same argv — it could, and then your rules would apply to it too. **Client-scoped policy
narrows convenience, not authority.** THREATS.md T-14 is the full argument.

A bridge started with no `--client-label` matches **no rule at all**, including one written `"*"`.

## What a rule cannot do

- **It cannot widen `--expose`.** The bridge's exposure is checked first, against the resolved entry,
  and a rule is only ever consulted for something already inside it. A rule saying `entries = ["**"]`
  under a bridge started `--expose "env/**"` reaches exactly `env/**`.
- **It cannot raise `--max-ttl`.** Both ceilings apply and the rule may only lower. A rule asking for
  an hour under `keypaste agent --max-ttl 60` grants sixty seconds.
- **It cannot overturn a refusal you just gave.** A "no" you typed is more specific and more recent
  than a rule you wrote last month.
- **It cannot make an entry listable.** `list_entry_names` is governed by `--expose` alone; the
  listing path is never handed the policy at all.

## Anything wrong means everything asks you

There are six states the file can be in. To an *agent* they are all the same thing — no rules, so
every request is shown to you — and that is deliberate: a request must not be able to work out
whether you have a policy at all. To you they are six different messages, because "I wrote a rule
and it is not working" and "I have no rules" need different next steps.

| State | What `keypaste agent` says |
| --- | --- |
| No file | `policy: no file at …, so every request is shown to you.` |
| Empty, or comments only | `policy: no rules in …, so every request is shown to you.` |
| Usable | `policy: 2 rules from … [sha256:a1b2c3d4]. keypaste policy ls shows them.` |
| Malformed | `policy: … is NOT in force - line 7: 'entires' is not a key keypaste understands` |
| Unreadable | `policy: … is NOT in force - it could not be read: …` |
| Writable by others | `policy: … is NOT in force - it is writable by users other than its owner` |

A file that is *partly* wrong is not partly in force. Two good rules and one bad line means **zero**
rules — there is no way to know whether the difference between what the file says and what you meant
is narrower or wider, so the only safe reading of it is that it says nothing.

None of these stops the agent starting. A typo in a policy file must not be a way to lock you out of
your own vault.

## The file format

A deliberately small subset of TOML: `#` comments, `[[allow]]` section headers, and `key = value`
where the value is a double-quoted string, a whole number, or an array of double-quoted strings.

Anything else is a parse error naming the construct — dotted keys, inline tables, single-quoted or
multi-line strings, floats, booleans, dates, hex, a singular `[table]` header, a trailing comma. So
is a `[[deny]]` section: a rule shape from a later keypaste invalidates the file rather than being
skipped while the `[[allow]]` rules stay in force.

A pattern or a label containing anything that would not survive being printed as written — a bidi
override, a zero-width space, a Unicode tag character — is also refused, so a rule cannot render as
`env/dev/**` while meaning `env/**`.

## Permissions

Your policy file decides what an agent may take without asking, so anything that can write it can
grant that access.

On Linux and macOS, keypaste **refuses** a policy file — or a `~/.keypaste` directory — that is
writable by anyone but its owner, and says so. It does not repair it. Repairing would be a race, and
it would erase the evidence that something was wrong with an authorization document. Run
`chmod 600 ~/.keypaste/policy.toml` and `chmod 700 ~/.keypaste` and restart.

**On Windows there is no equivalent check**, the same gap the audit log and `env export` have. This
is stated rather than papered over: a half-check that passes on a world-writable directory is worse
than none, because it implies one happened.

**Keep this file out of synced folders.** `~/.keypaste` is deliberately not beside your vault. If you
point `KEYPASTE_HOME` at Dropbox or iCloud, another machine writes this machine's authorizations.

## It is read once, at startup

`keypaste agent` reads the file when it starts and holds those rules for the whole session. Editing
it changes nothing until you restart the agent — which means re-typing your master password, and
which means a policy only ever comes into force with you present.

`keypaste policy ls` reads the file *now*. If the agent has been running since before you edited it,
the two can disagree — so both print a short hash of the exact bytes they read:

```
2 rules, from /home/you/.keypaste/policy.toml [sha256:e75ea9d3]
```

Same hash, same rules.

## What is written down

Every release through a rule appends a line to `~/.keypaste/audit.jsonl` with
`"decision":"granted"` and `"method":"policy"`, and the reason names which rule:

```json
{"decision":"granted","method":"policy","reason":"pre-authorized by policy rule allow#1 (env/dev/**, password)"}
```

It is never `"method":"prompt"`. That word means a person was shown that specific request, and on
this path nobody was. A request refused for spending its allowance is `"method":"policy-limit"`.

The agent also prints one line to its own terminal per release:

```
keypaste: released env/dev/STRIPE_KEY to allow#1 for 300s without asking
```

That line and the audit log are the only signals that a silent release happened. There is no prompt
to notice.

## The honest limits

- **A rule is a standing grant over a part of your vault as it is now, not as it was when you wrote
  it.** Anything that can write into that part — a synced vault, a colleague on a shared file, a
  hostile `.env` you imported — chooses what the rule covers. Move `personal/bank` into `env/dev`
  and a rule for `env/dev/**` covers it.
- **With a rule in force, no human sees the request.** The agent's stated reason is recorded and read
  by nobody. The controls that exist are narrow `entries`, a small `max_per_hour`, and a short
  `--max-ttl`.
- **A rule names a client label any process on your machine could claim.** See above.
- **There is no way to see which entries a rule matches today.** `keypaste policy ls` shows what each
  rule *means*, not what it currently *covers*. That is the mitigation this feature most wants and it
  needs the vault, so it belongs with `keypaste log` in Stage 2.4.

THREATS.md T-13 through T-17 are the full versions of these.

## Verifying it yourself

`scripts/verify-policy-e2e.sh` runs a real `keypaste agent` and a real `keypaste-mcp` as separate
processes and asserts that a covered request returns the credential with no prompt drawn, that the
same agent still prompts for anything outside the rule, that a rule cannot reach past `--expose` or
raise `--max-ttl`, that an unlabelled bridge matches nothing, that a malformed file grants nothing,
and that the credential never reaches the audit log. It runs on Linux, macOS and Windows in CI.
