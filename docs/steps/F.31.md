# F.31 — Say which HTTP transports the bridge's SDK carries

Completed 2026-09-30 on `task/f31` above `a662fc3`, documents only; no dev run, because nothing that builds changed; ci 36753070272 and app 36753073956 passed at `07be53e` on `integrate`.

## Amendments

- T-9 was rewritten whole, as CLAUDE.md requires of an edited threat section. Beyond the row it states that the bridge's own code makes no network request, that a `run` command may (T-35), and that `Keypaste.Core`, which the bridge links, holds `ShareClient`, constructed only by the CLI's `share` and the desktop app.
- The row's name search was made over the published `v0.3.0` binaries rather than a fresh build; `v0.3.0` at `307bb54` pins the same SDK and constructs the same one transport as `main`.

## Evidence

**The locked package.** `src/Keypaste.Mcp/packages.lock.json` resolves `ModelContextProtocol.Core` 1.4.1 with content hash `G/hOBZkJ…RxkQ==` and dependencies `Microsoft.Extensions.AI.Abstractions` 10.5.2 and `Microsoft.Extensions.Logging.Abstractions` 10.0.7, the latter bringing `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.7. nuget.org's `modelcontextprotocol.core.1.4.1.nupkg`, with its `.signature.p7s` removed, has SHA-512 `G/hOBZkJ…RxkQ==`, the lock file's hash; the signed file's differs, as NuGet's content hash excludes the repository signature. The package's `lib/net10.0/ModelContextProtocol.Core.xml` documents these transport types:

- Client, HTTP: `HttpClientTransport`, `HttpClientTransportOptions`, `HttpTransportMode`, `AutoDetectingClientSessionTransport`, `SseClientSessionTransport`, `StreamableHttpClientSessionTransport`.
- Server, HTTP: `StreamableHttpServerTransport`, `StreamableHttpPostTransport`, `SseResponseStreamTransport`.
- Stdio and stream: `StdioServerTransport`, `StreamServerTransport`, `StdioClientTransport`, `StreamClientTransport` and their session and option types.

The DLL references `System.Net.Http`, and the package declares no HTTP package, so the client transport uses the runtime's.

**The bridge's source.** `src/Keypaste.Mcp/Program.cs` `ServeAsync` constructs `new StdioServerTransport(serverOptions, loggerFactory: null)` and nothing else; a case-insensitive search of `src/Keypaste.Mcp` for `transport` and `http` finds only that construction, its use by `McpServer.Create` and the csproj's comment, and no file there names `ShareClient`. Across `src/`, `HttpClient`, `System.Net.Http` or `Socket` appear only in `Keypaste.Core/Sharing/ShareClient.cs`, `Keypaste.Cli/Commands/ShareCommand.cs` and `Keypaste.App/ViewModels/ShellViewModel.cs`. The approver connection is `NamedPipeClientStream` in `Keypaste.Core/Ipc/ApproverClient.cs`.

**The published binary.** The four `v0.3.0` archives from `https://dl.keypaste.com/v0.3.0/` passed `shasum -a 256 -c SHA256SUMS`. Byte counts of each name, as UTF-8, in `keypaste-mcp` (linux-x64, linux-arm64, osx-arm64) and `keypaste-mcp.exe` (win-x64), identical on all four:

| Name | Count |
|---|---|
| `ModelContextProtocol` | 4 |
| `StdioServerTransport`, `StreamServerTransport` | 1 each |
| `HttpClientTransport`, `StreamableHttp`, `SseClient`, `SseResponseStreamTransport`, `AutoDetectingClientSessionTransport`, `McpHttpClient`, `StdioClientTransport`, `HttpClient`, `ShareClient` | 0 |
| `System.Net.Http` | 2, one within `System.Net.Http.dll` |
| `SocketsHttpHandler` | 1, in the switch `System.Net.SocketsHttpHandler.Http3Support` |

UTF-16 counts were 0 for every name except one `ModelContextProtocol` in the Windows binary. B.4a found `HttpClientTransport`, `StreamableHttp` and `SseClient` absent from the bridge built at `a432ade` in ci run 36728482576.

## Decisions

None.

## Limits and follow-ups

- The name search suggests, without proving, that trimming removed the HTTP transports: NativeAOT keeps names for only some members, and `System.Net.Http.dll` is still named. No disassembly or `ilc` map was examined.
- `docs/mcp-setup.md` ("the keypaste bridge uses local stdio and local IPC") and SECURITY.md make no claim about the SDK's contents and were left unchanged. `docs/replace-dotenv.md`'s "the vault path contains no network code" was outside the row and was not examined.
- B.4b's row already carries T-9's account to the one binary.
