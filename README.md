# keypaste

> *"Stop pasting secrets into chats. keypaste is a local-first, KDBX-compatible vault that stores your passwords AND env variables, injects them into your projects, and lets AI agents like Claude request exactly one credential — with your approval, scoped access, and a full audit trail — without ever seeing your vault."*

**Status: Stage 1.2 — the CLI works, and it replaces your `.env`.** Create a vault, add entries,
list them, copy a password to the clipboard, remove entries, and keep a project's environment
variables in the same file — importing them straight from an existing `.env`. Verified in CI
against a real KeePassXC on Linux, macOS and Windows, in both directions. Injecting those variables
into a child process (`keypaste run`) and the MCP bridge are still to come. Follow [`PLAN.md`](PLAN.md) for what lands next; [`CORE.md`](CORE.md) is
the constitution and does not change.

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

## Packages

| Roadmap name | Project | Ships as |
| --- | --- | --- |
| `keypaste-core` | `src/Keypaste.Core` | library — all vault logic lives here |
| `keypaste-cli` | `src/Keypaste.Cli` | `keypaste` |
| `keypaste-mcp` | `src/Keypaste.Mcp` | `keypaste-mcp` (placeholder until Stage 2) |

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
