# keypaste

> *"Stop pasting secrets into chats. keypaste is a local-first, KDBX-compatible vault that stores your passwords AND env variables, injects them into your projects, and lets AI agents like Claude request exactly one credential — with your approval, scoped access, and a full audit trail — without ever seeing your vault."*

**Status: Stages 1 and 2 complete.** Create a vault, add entries, list them, copy a password to the
clipboard, remove entries, keep a project's environment variables in the same file — importing them
straight from an existing `.env` — and run any command with those variables injected, with nothing
written to disk. There is a way back out, too. Verified in CI against a real KeePassXC on Linux,
macOS and Windows, in both directions.

The MCP bridge is in, and so is everything CORE.md law 3.2 asks of it. An agent asks; you are shown
who is asking, for what, and why; one field value goes back for a lifetime you were told about. You
can pre-approve a narrow pattern in a policy file, which is the one path that releases without
asking. Every call is one line in a local audit log whose records are hash-chained, so
`keypaste log verify` can tell you the file is the one keypaste wrote. Follow [`PLAN.md`](PLAN.md)
for what lands next; [`CORE.md`](CORE.md) is the constitution and does not change.

**New here?** [**Replace your `.env` in 5 minutes**](docs/replace-dotenv.md) is the guide — import,
run, CI, and honest answers about lost master passwords and syncing. To see the agent bridge
instead, [**watch Claude ask for a key**](docs/demo.md) — about sixty seconds, end to end.

## Using it

```sh
keypaste init ~/vault.kdbx           # prompts for a master password, twice
export KEYPASTE_VAULT=~/vault.kdbx   # or pass --vault to every command

keypaste add github --username me --url https://github.com
keypaste ls                          # tree of groups and entries, names only
keypaste get github                  # copies to the clipboard, clears after 20s
keypaste get github --show           # prints to stdout instead
keypaste rm github --yes
```

Passwords are never echoed at a prompt, and `get` never writes a secret to stdout unless you ask
for `--show`. Data goes to stdout, everything else to stderr, so `keypaste get x --show` is safe
to pipe.

| exit code | meaning |
| --- | --- |
| 0 | success |
| 1 | usage error |
| 2 | internal or environment error (including no usable clipboard) |
| 3 | vault or entry not found |
| 4 | wrong master password |
| 5 | the audit log is not the file keypaste wrote |

When stdin is not a terminal each prompt consumes exactly one line, in a fixed order:
`init` takes the password twice, `add` and `env set` take the master password then the value, and
everything else takes the master password. That is what makes the CLI scriptable.

## Environment variables

A project's environment variables live in the group `env/<project>`, one ordinary entry per
variable — title is the name, password is the value. There is nothing keypaste-specific in the
file, so KeePassXC can read, edit, add and delete them with no knowledge of keypaste at all. CI
proves that in both directions on all three operating systems; see
[`DECISIONS.md`](DECISIONS.md) D-0014 for why this shape was chosen over custom string fields.

```sh
keypaste env pull billing                      # imports ./.env, then offers to delete it
keypaste env pull billing config/.env.prod --yes --delete-source
keypaste env set billing DATABASE_URL          # prompts for the value, hidden
keypaste env set billing STRIPE_KEY=sk_test_x  # or inline, for scripts — see the caveat below
keypaste env ls                                # projects
keypaste env ls billing                        # variable names, never values
keypaste get env/billing/DATABASE_URL --show   # read one value
keypaste env rm billing STRIPE_KEY --yes
keypaste env export billing --dotenv --stdout  # the way back out — see below
```

`env pull` reads the whole file before it writes anything: if any line is malformed it reports
every problem and imports nothing, so you never end up with half a `.env` in the vault and no
`.env` on disk. It shows a plan first — how many variables are new, updated and unchanged, by name
— and leaves unchanged ones alone.

It handles `export` prefixes, comments, all three quoting styles, and values that span lines. Two
rules differ from `dotenv`, both deliberately: a `#` only starts a comment when a space precedes it
(so `PASSWORD=hunter2#42` keeps its `#`), and a key repeated in one file is an error rather than a
coin flip. Values are stored **exactly as written** — `${VAR}` and `$VAR` are not expanded, because
expanding them would bake one machine's environment into a vault you sync to others. Inside double
quotes `\n`, `\r`, `\t`, `\\` and `\"` expand, so write `'C:\temp'` in single quotes if you mean a
Windows path.

Three things worth knowing before you rely on it, all covered in [`SECURITY.md`](SECURITY.md):
the `KEY=value` form leaves the value in your shell history and in the process list, so keypaste
warns when you use it; setting a variable that already exists keeps the old value in the entry's
KeePassXC history rather than erasing it; and deleting a `.env` is tidying, not erasure — keypaste
says so rather than offering a "shred" it could not honour.

## Running things with those variables

```sh
keypaste run dev -- npm start          # no .env on disk, nothing written to one
keypaste run prod -- ./deploy.sh
```

The `--` is required. Without it, `keypaste run dev npm start` cannot be told apart from a project
called `npm`; everything after it belongs to the command, including flags keypaste also understands.

The command inherits your environment with the project's variables merged on top, and it gets
keypaste's own stdin, stdout and stderr — so colours, prompts and progress bars work exactly as if
keypaste were not there. **The vault is closed before the command starts**, so a server you leave
running for hours is not holding a decrypted database open.

Once the command starts, its exit code is keypaste's; keypaste's own failures always print a line
beginning `keypaste run:` first. A command that does not exist reports 127 and one that is not
executable reports 126, as in a shell. Ctrl+C reaches the command, and keypaste waits for it rather
than dying first — `docker stop` and `timeout` work the same way.

It refuses to run rather than inject something ambiguous: a variable whose name is not a legal
environment variable, or two names differing only in case (two variables on Linux, one on Windows).
Both are things KeePassXC will let you create and keypaste will not, and both name every offending
key so one pass in KeePassXC fixes them.

## Getting them back out

A vault you cannot leave is a vault nobody should adopt, so there is an escape hatch. It is the one
command in keypaste that writes plaintext, and it behaves like it.

```sh
keypaste env export billing .env --dotenv        # writes a file, after confirming
keypaste env export billing --dotenv --stdout    # prints it instead, for piping
```

The format has to be named — `--dotenv` is not assumed — and writing a file prints a red warning
naming the destination and asks before it goes ahead. It will not overwrite an existing file without
`--force`, it points out a `.git` ancestor, and on Linux and macOS the file is created readable only
by you. Windows has no equivalent and keypaste says so rather than implying a permission it did not
set. Prefer `keypaste run`, which needs no file at all.

Values are written in single quotes wherever possible, because that form means the same thing to
`motdotla/dotenv`, `python-dotenv`, `godotenv`, Docker Compose v2 and `sh` alike. The handful that
cannot be — a value containing an apostrophe or a carriage return — are escaped and named on stderr,
because that is the form those readers disagree about.

## Letting an agent ask for one

`keypaste-mcp` is an MCP server. Point Claude Desktop or Claude Code at it and two tools appear:
`list_entry_names`, which returns group paths and entry names and never a value, and
`request_credential`, which asks you to release one field of one entry.

Nothing is released without you saying yes to that specific request, unless you wrote a rule in
advance that covers it. You are asked by `keypaste agent`, a command you run in your own terminal:

```sh
keypaste agent --vault ~/vaults/personal.kdbx
```

```
keypaste: an agent is asking for a credential.

  client   claude-code
  entry    env/dev/STRIPE_KEY
  field    password
  for      300 seconds

  the agent says it needs this because:
    deploy the billing service to staging

  That sentence was written by the agent, not by keypaste. Treat it as a claim.

Approve? [y/N]
```

**Your master password is typed there and nowhere else.** Any program on your machine can pop up a
window that looks like keypaste asking for it, so keypaste never gives you a reason to expect one:
no agent, and nothing an agent does, can cause a password prompt to appear. That is why the approver
is a separate process you start, rather than something the MCP server does.

Say no and the agent is told not to ask again. Say nothing for 45 seconds and that is a no. Ask for
the same field again within the lifetime you approved and you are not asked twice. Every call —
granted, denied, or malformed — appends a line to `~/.keypaste/audit.jsonl`, and the value is never
in it.

What an agent may even *name* is default-deny: out of the box that is the `env/` subtree and nothing
else, and widening it takes an explicit `--expose` glob in the client's config, which is a file you
wrote.

### Saying yes in advance

If you are approving the same thing every day, you can write it down once in
`~/.keypaste/policy.toml` and stop being asked about that one case:

```toml
[[allow]]
client          = "claude-code"     # the --client-label you gave the bridge
entries         = ["env/dev/**"]
fields          = ["password"]
max_ttl_seconds = 300
max_per_hour    = 20                # optional
```

`keypaste policy ls` shows what your rules mean in plain English — and shows what each pattern
actually *parsed to*, because the obvious way to write one is usually not what it does. There is no
policy file unless you write one, keypaste never writes it, and **anything at all wrong with it means
the whole file is ignored and every request comes back to you**.

This is the one path in keypaste that hands an agent a credential with nobody watching. A rule cannot
reach past `--expose`, cannot raise `--max-ttl`, cannot overturn a "no" you just gave, and cannot
make an entry listable — but within its pattern, no human sees the request.
[**Pre-approving with a policy file**](docs/policy.md) is the guide, including what that costs.

### Seeing what happened

Every call an agent makes is one line in `~/.keypaste/audit.jsonl`, allowed or refused.
`keypaste log` reads it back:

```
3 records in /home/you/.keypaste/audit.jsonl

  time (UTC)           client       entry               decision  method
  2026-07-26 14:03:09  claude-code  -                   granted   exposure
  2026-07-26 14:03:11  claude-code  env/dev/STRIPE_KEY  granted   prompt
  2026-07-26 14:07:44  claude-code  env/dev/STRIPE_KEY  denied    out-of-scope
```

`--denied`, `--client <text>` and `--since 2h` narrow it, and a narrowed view always says so.

Each record carries the hash of the record before it, so `keypaste log verify` tells you whether the
file is the one keypaste wrote — and tells you, every time it passes, the two things it cannot see:
a rewrite that recomputed the chain, and records deleted from the end.

[**Claude asks for a key, you approve, the deploy runs**](docs/demo.md) is the whole thing end to
end, in about sixty seconds. [**Approving an agent's request**](docs/approvals.md) is the guide to
what you are deciding. [**Connecting keypaste to Claude**](docs/mcp-setup.md) has the config
snippets, the audit log format, and how to read it. [**THREATS.md**](THREATS.md) is the threat model — prompt injection through entry
names and through the agent's stated reason, clients that cannot be authenticated, prompt fatigue,
what a reused grant costs, and what tampering with the log does and does not achieve.

## Packages

| Roadmap name | Project | Ships as |
| --- | --- | --- |
| `keypaste-core` | `src/Keypaste.Core` | library — all vault logic lives here |
| `keypaste-cli` | `src/Keypaste.Cli` | `keypaste` |
| `keypaste-mcp` | `src/Keypaste.Mcp` | `keypaste-mcp` — MCP server, stdio, holds no vault and decides nothing |

## Vault format

KDBX4 with Argon2d key derivation (2 iterations, 64 MiB, parallelism 2) and AES-256. keypaste
never invents a format and writes no cryptography of its own (CORE.md §2, §3.6): the format layer
is [KeePassLib](third_party/KeePassLib/UPSTREAM.md), vendored from KeePass 2.61 and reached through
a single file, `src/Keypaste.Core/Internal/KeePassInterop.cs`.

Any vault keypaste writes must open in KeePassXC, and anything KeePassXC writes back must be
readable by keypaste. That is not a hope — `scripts/verify-keepassxc-compat.sh` and
`scripts/verify-keepassxc-writeback.sh` prove both directions on every push against a real
`keepassxc-cli`, on all three operating systems, and the gate is permanent (CORE.md §4.6,
[`DECISIONS.md`](DECISIONS.md) D-0008 and D-0014).

Directories and namespaces use .NET's PascalCase convention; the kebab-case names above are the
roadmap's and survive where they are user-visible, in the shipped binary names.

CLI, MCP server, and the eventual GUI are all thin clients over `Keypaste.Core` — no logic is
duplicated in a frontend (CORE.md §4.3).

## Build and test

Requires the .NET SDK pinned in [`global.json`](global.json).

```sh
dotnet restore keypaste.slnx --locked-mode
dotnet build   keypaste.slnx
dotnet test    keypaste.slnx
dotnet run --project src/Keypaste.Cli
```

Warnings are errors, code style is enforced at build time, and every dependency is pinned by
`packages.lock.json`. Adding a package is therefore a two-step: declare it in
`Directory.Packages.props` and the project, then `dotnet restore --force-evaluate` and commit the
regenerated lock files.

`third_party/` is vendored source and is held to different rules — it is re-merged from upstream,
not formatted or linted to our taste. `third_party/Directory.Build.props` quarantines it, and
`dotnet format` is run with `--exclude third_party/`.

To run the KeePassXC compatibility gate locally you need `keepassxc-cli` on `PATH` (or `KPXC_CLI`
pointing at it):

```sh
export KP_COMPAT_PASSWORD=ci-master-pw
scripts/make-compat-fixture.sh ./artifacts/compat/gen.kdbx
scripts/verify-keepassxc-compat.sh ./artifacts/compat/gen.kdbx
scripts/verify-keepassxc-writeback.sh ./artifacts/compat/writeback.kdbx
```

The fixture is built by the shipped `keypaste` binary, so the gate covers the CLI as well as the
vault writer. The write-back script builds its own vault and drives both tools in turn: keypaste
modifies an entry, then KeePassXC edits and adds env variables that keypaste has to read back.

## Security

Please report vulnerabilities privately — see [`SECURITY.md`](SECURITY.md).

## License

[AGPL-3.0](LICENSE). Auditable code is the trust strategy (CORE.md §3.8).
