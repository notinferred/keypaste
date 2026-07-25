# keypaste

> *"Stop pasting secrets into chats. keypaste is a local-first, KDBX-compatible vault that stores your passwords AND env variables, injects them into your projects, and lets AI agents like Claude request exactly one credential — with your approval, scoped access, and a full audit trail — without ever seeing your vault."*

**Status: Stage 0.3 — the CLI works.** Create a vault, add entries, list them, copy a password to
the clipboard, remove entries. Verified in CI against a real KeePassXC on Linux, macOS and
Windows. Env-variable injection and the MCP bridge are still to come. Follow [`PLAN.md`](PLAN.md)
for what lands next; [`CORE.md`](CORE.md) is the constitution and does not change.

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
`init` takes the password twice, `add` takes the master password then the entry password, and
everything else takes the master password. That is what makes the CLI scriptable.

## Packages

| Roadmap name | Project | Ships as |
| --- | --- | --- |
| `keypaste-core` | `src/Keypaste.Core` | library — all vault logic lives here |
| `keypaste-cli` | `src/Keypaste.Cli` | `keypaste` (verbs land in Stage 0.3) |
| `keypaste-mcp` | `src/Keypaste.Mcp` | `keypaste-mcp` (placeholder until Stage 2) |

## Vault format

KDBX4 with Argon2d key derivation (2 iterations, 64 MiB, parallelism 2) and AES-256. keypaste
never invents a format and writes no cryptography of its own (CORE.md §2, §3.6): the format layer
is [KeePassLib](third_party/KeePassLib/UPSTREAM.md), vendored from KeePass 2.61 and reached through
a single file, `src/Keypaste.Core/Internal/KeePassInterop.cs`.

Any vault keypaste writes must open in KeePassXC. That is not a hope — `scripts/verify-keepassxc-compat.sh`
proves it on every push against a real `keepassxc-cli`, on all three operating systems, and it is
permanent (CORE.md §4.6, [`DECISIONS.md`](DECISIONS.md) D-0008).

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
```

The fixture is built by the shipped `keypaste` binary, so the gate covers the CLI as well as the
vault writer.

## Security

Please report vulnerabilities privately — see [`SECURITY.md`](SECURITY.md).

## License

[AGPL-3.0](LICENSE). Auditable code is the trust strategy (CORE.md §3.8).
