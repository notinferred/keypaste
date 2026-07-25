# keypaste

> *"Stop pasting secrets into chats. keypaste is a local-first, KDBX-compatible vault that stores your passwords AND env variables, injects them into your projects, and lets AI agents like Claude request exactly one credential — with your approval, scoped access, and a full audit trail — without ever seeing your vault."*

**Status: Stage 0.2 — `keypaste-core` reads and writes real KDBX4 vaults.** Create a vault with a
master password, add entries, save, reopen, read back. Verified in CI against a real KeePassXC on
Linux, macOS and Windows. There is no CLI yet — that is Stage 0.3. Follow [`PLAN.md`](PLAN.md) for
what lands next; [`CORE.md`](CORE.md) is the constitution and does not change.

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
dotnet run --project tools/Keypaste.CompatFixture -- ./artifacts/compat/gen.kdbx
scripts/verify-keepassxc-compat.sh ./artifacts/compat/gen.kdbx
```

## Security

Please report vulnerabilities privately — see [`SECURITY.md`](SECURITY.md).

## License

[AGPL-3.0](LICENSE). Auditable code is the trust strategy (CORE.md §3.8).
