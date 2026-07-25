# keypaste

> *"Stop pasting secrets into chats. keypaste is a local-first, KDBX-compatible vault that stores your passwords AND env variables, injects them into your projects, and lets AI agents like Claude request exactly one credential — with your approval, scoped access, and a full audit trail — without ever seeing your vault."*

**Status: Stage 0 — scaffold only. There is no vault logic yet.** The repository currently builds
three empty packages and one test proving they are wired together. Follow [`PLAN.md`](PLAN.md) for
what lands next; [`CORE.md`](CORE.md) is the constitution and does not change.

## Packages

| Roadmap name | Project | Ships as |
| --- | --- | --- |
| `keypaste-core` | `src/Keypaste.Core` | library — all vault logic lives here |
| `keypaste-cli` | `src/Keypaste.Cli` | `keypaste` |
| `keypaste-mcp` | `src/Keypaste.Mcp` | `keypaste-mcp` (placeholder until Stage 2) |

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

## Security

Please report vulnerabilities privately — see [`SECURITY.md`](SECURITY.md).

## License

[AGPL-3.0](LICENSE). Auditable code is the trust strategy (CORE.md §3.8).
