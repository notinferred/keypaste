# K.7 — The architecture rules hold in the compiler

Completed 2026-10-30 at `96c60a6e` on `k7`, source only; runs ci 41145606032 at that commit and app 41144191788 at `7557e01b`, which differs from it only in one test's data. The integrated commit differs from `96c60a6e` only in this record.

## Amendments

- ArchUnitNET is not adopted (founder, 2026-10-30, D-0675). The one namespace rule an analyzer could not hold, that the vault-free pieces use no other keypaste namespace, is held by moving them into `Keypaste.Infrastructure`, a project that references nothing.
- Added with the founder's approval of the plan (2026-10-30): Core references KeePassLib under an extern alias, which makes ARCHITECTURE's "only `Internal/KeePassInterop.cs` touches KeePassLib", held by no test before, a compiler fact; and `Directory.Build.targets` holds the reference rules the `.csproj`-reading tests held.
- `AttachmentSourceRulesTests` keeps one rule the plan moved: the two attachment files name no launcher, process, address or core launch, tool or link code. Those core namespaces serve other app files, and a ban list covers a whole project.

## What changed

- `Microsoft.CodeAnalysis.BannedApiAnalyzers` 5.6.0, the Roslyn of SDK 10.0.302, runs in every `src` project through `src/Directory.Build.props`, over each project's `BannedSymbols.txt` and the shared `NoKeePassLib`, `NoNetwork`, `NoVault`, `NoCrashReports` and `NoFileWrites` lists. Fourteen files carry an exemption, each a `#pragma` with its reason, and `BanExemptionsTests` lists them and refuses any other mention of RS0030 in `src` and the repository's build and style files.
- The network ban names the types that connect, not `System.Net`: the owner's files only catch `SocketException`, which opens nothing.
- `Keypaste.Infrastructure` is in both solutions, `app.yml`'s paths, `sign-windows.sh`'s first-party files, `verify-publisher-metadata.sh` and app.yml's signature check, since the desktop payload now carries its DLL.
- Deleted: `InfrastructureBoundaryTests`, `CliDispatchSourceRulesTests`, `BridgeSourceRulesTests`, `RelaySourceRulesTests`, `RecommendationsSourceRulesTests`, `BrowserSourceRulesTests`, `ClipboardSourceRulesTests`, whose marker-name test moved into `SecretMarkerTests`, and `TheAppSharesTheWriterTests`. Trimmed: `AttachmentSourceRulesTests`, `SodiumSourceRulesTests` to the `DllImport`, `CrashSourceRulesTests` without its CLI and bridge rule, and `KeyDerivationCostTests` without its scan. Added: `BanExemptionsTests` and `DispatchOrderTests`, which holds that `Program.cs` dispatches to the bridge and the relay first.

## Evidence

- Deliberate violations on the founder's Mac, each project built alone with `--no-dependencies`, each failing with its own error and then reverted:

  | Violation | Error |
  |---|---|
  | Infrastructure names `Keypaste.Core.Vault` | CS0234 |
  | Core: `Dns.GetHostName()` in `Recommendations/`, `new HttpClient()` in `Browser/` | RS0030 `Dns`, `System.Net.Http` |
  | Core: `typeof(KeePassLib.PwDatabase)` outside `KeePassInterop.cs` | CS0246 |
  | Core: `TestKeyDerivation.Cheap = true` | RS0030 |
  | App: `TryGetTextAsync` by extension and by static call | RS0030 `ClipboardExtensions.TryGetTextAsync` |
  | App: `LaunchFileInfoAsync`, `LaunchUriAsync`, `IWebLauncher.OpenAsync`, `Process.Start` | RS0030 `LauncherExtensions`, `ILauncher`, `IWebLauncher.OpenAsync`, `Process.Start(string)` |
  | App: `vault.Attachments`, `File.WriteAllText`, KeePassLib, `HttpClient` | RS0030 each |
  | CLI: `Keypaste.Mcp.BridgeEntry`, `CrashReport` | RS0030 `Keypaste.Mcp`, `Keypaste.Core.CrashReports` |
  | Bridge: `Vault`, `SecretInput`; relay: `VaultCreation`, `AuditLog` | RS0030 each |
  | Mvvm: a reference to `Keypaste.Browser` and `DirectPInvoke` of libsodium | the three `Directory.Build.targets` errors |

- `verify.sh compile` passed on the founder's Mac in 53 s. Locally: Core.Tests 155 passed over `BanExemptionsTests`, `CrashSourceRulesTests`, `KeyDerivationCostTests`, `PassphraseSourceRulesTests` and the moved files' classes; Cli.Tests 12 over `DispatchOrderTests`, `BridgeClosureTests`, `DoctorSourceRulesTests` and `SavingVerbSourceRulesTests`; Browser.Tests 2 over `SodiumSourceRulesTests`; App.Tests 5 over `AttachmentSourceRulesTests` and `SecretMarkerTests`.
- Relock dev 41144085371 found every lock file already matching its project.
- app 41144191788 at `7557e01b`: every job passed but `keepassxc first run (macos-15)`, BACKLOG's former F.74, failing as it does on `main` at `4099fc45`; the founder chose to integrate past it (2026-10-30). ci 41144182217 at the same commit failed `test` on all three runners in `CrashReportShapeTests.Only_what_the_scrubber_would_write_reads_back`: the namespace rename had also changed a sample frame's type in that test's data, which `site/test/crash-vector.json` still names, so its mutation found nothing to replace. `96c60a6e` restored the string. ci 41145606032 at `96c60a6e` then passed every job.

## Decisions

D-0675.

## Limits and follow-ups

- A ban entry that names no symbol, by a typo or a rename, bans nothing and says nothing. The violations above show one entry of each rule resolving, not every entry.
- XAML is compiled outside Roslyn, so no ban reaches an `.axaml` file; no `HyperlinkButton` or `NavigateUri` is in one today.
- `site/test/crash-vector.json`'s sample frame, and `CrashReportShapeTests`' mutation of it, still name `Keypaste.Core.Infrastructure.AtomicFile`, a made-up stack the report grammar still accepts; changing the vector would deploy keypaste.com.
- BACKLOG gains a read-only vault for verbs that never save, which would retire `SavingVerbSourceRulesTests`' tracer, and the crash-report code in a project of its own.
