# B.4a — Measure the bridge built into the CLI

Completed 2026-09-30, documents only; dev run 36735077152 selected no lane for them. The experiment is branch `exp/b4a` at `a02f9ac`, two commits above `a432ade`, kept and not merged. It was measured in ci run 36727509499 at its first commit, `18df45b`, and ci run 36728482576 at `a02f9ac`.

## Amendments

None to the row. Beyond it, the measurement adds macOS arm64 and Windows x64 to Linux, adds peak memory, and runs the bridge's gates through `keypaste mcp`.

## The experiment

`exp/b4a` makes the smallest change that lets the NativeAOT CLI run the bridge:

- `Keypaste.Cli` references `Keypaste.Mcp`, which stays an executable; the bridge grants `keypaste` its internals, and its `Main` becomes internal.
- The CLI's `Program.Main` hands `keypaste mcp <args>` to the bridge's `Main` before `CliApp.Run`, so no verb, `VaultLocator`, `VaultSession` or prompt runs first.
- The lock files of the CLI and `Keypaste.Cli.Tests` gain the bridge's closure, copied from `Keypaste.Mcp`'s: `ModelContextProtocol.Core` 1.4.1 and three `Microsoft.Extensions.*` abstractions. Both runs' locked restore, format and analyzers passed.
- `ci.yml`'s aot job publishes `a432ade`'s two binaries from a worktree on the same runner and runs `scripts/b4a-measure.py` over both layouts. It then runs `verify-mcp-stdio.sh`, `verify-approval-e2e.sh`, `verify-mcp-run.sh`, `verify-policy-e2e.sh` and `verify-log-chain.sh` with `KEYPASTE_MCP_BIN` set to a wrapper that execs `keypaste mcp`. A `b4a-measure` job publishes and measures on `macos-15` and `windows-2025`.

The script times each start from spawn: to exit for `keypaste --version` and the bridge's `--help`, and to the bridge's reply to `initialize`. The six cases are interleaved, with three warm-up rounds and then 60 rounds with a warm file cache. On Linux and macOS, 15 more rounds drop the cache before every start (`drop_caches`, `purge`). Peak memory is each child's `ru_maxrss`.

## Evidence

**Trim diagnostics.** On linux-x64 in both runs, and on osx-arm64 and win-x64, the one binary's publish emitted the same 15 IL diagnostics as `a432ade`'s CLI. Every one is in `third_party/KeePassLib` and in the baseline, and the bridge's own publish emitted none. The count took every `IL` code in the log, so a warning ILC folds per assembly (IL2104, IL3053) would have shown. `verify-aot-trim.sh` passed on the one binary's log alone and beside the bridge's.

**Size** in bytes, run 36728482576; the Linux sizes equal run 36727509499's:

| | linux-x64 | osx-arm64 | win-x64 |
|---|---|---|---|
| `keypaste` + `keypaste-mcp` today | 13,944,640 + 9,126,056 | 13,002,888 + 8,839,616 | 13,704,704 + 10,981,376 |
| One binary | 19,344,064 | 18,313,080 | 21,177,344 |
| Saved, plain and under `xz -9` | 16.2%, 18.7% | 16.2%, 18.9% | 14.2%, 18.2% |

**Start time**, median milliseconds with a warm cache, today then one binary:

| Case | linux-x64, run 1 | linux-x64, run 2 | osx-arm64 | win-x64 |
|---|---|---|---|---|
| `keypaste --version` | 7.1 → 10.9 | 3.7 → 5.1 | 11.2 → 12.4 | 14.5 → 14.4 |
| bridge `--help` | 6.3 → 10.4 | 2.9 → 4.9 | 9.7 → 11.9 | 11.6 → 13.5 |
| bridge to its `initialize` reply | 12.1 → 16.0 | 6.9 → 9.1 | 12.9 → 15.5 | 19.4 → 21.9 |

With the cache dropped, run 1 on Linux gave 20.1 → 26.3, 14.9 → 23.0 and 24.3 → 34.5 ms, with each p90 at most 9 ms above its median. Run 2's cold rounds on Linux and macOS spread over hundreds of milliseconds (p90 to 374 ms and 1.6 s), and their medians do not order the two layouts.

**Peak memory**, MiB, today then one binary: on macOS, 9.6 → 13.3 for `--version`, 8.5 → 12.6 for `--help` and 12.7 → 17.8 to `initialize`; on Linux in run 1, 16.6 → 26.2 to `initialize`. Linux gives a child that Python starts with vfork its parent's peak at exec, so no Linux figure is below the measuring process's own. In run 1, today's `--version` and `--help` both read exactly 11,896 KiB, that floor, so they are at most 11.6 against the one binary's 18.7 and 14.0. Every run 2 figure reads 281. Windows reports none.

**Gates.** Every job of run 36728482576 passed, including the test and compat jobs on three platforms. In its aot job the five bridge gates passed through `keypaste mcp`, with the one binary also serving as `keypaste agent`. Then every existing aot gate passed against the one `keypaste`, including the four KeePassXC 2.6.6 gates on vaults it wrote.

**Names in the binaries.** The bridge and the one binary each hold `ModelContextProtocol` four times and `StdioServerTransport` once, and today's CLI neither. No binary holds `HttpClientTransport`, `StreamableHttp` or `SseClient`. NativeAOT keeps names for only some members, so this suggests, without proving, that trimming removed the HTTP transports.

## Conclusion

Proceed with B.4b. No new trim diagnostic appeared on any of the three platforms, which was the row's only stop condition. One binary is 14 to 16% smaller than the two it replaces. Each start costs up to 4.1 ms more with a warm cache and up to 10.2 ms more cold on Linux. Peak memory grows by 3.7 to 5.1 MiB on macOS, by 9.7 MiB to `initialize` on Linux, and by at least 7.1 and 2.4 MiB for Linux's `--version` and `--help`.

## What B.4b must hold

- **The name is taken.** `keypaste mcp` already has `serve`, `setup`, `policy` and `help` (`McpCommand`, D-0360). The AppImage's `AppRun` already starts the bridge for `mcp` (D-0334), and `verify-linux-appimage.sh` expects `usage: keypaste-mcp` from `AppRun mcp --help`.
- **No gate runs the verbs' side of the dispatch.** The experiment's `Main` took every `mcp` argument, so the binary's `mcp serve`, `setup` and `policy` did not work, yet every job of run 36728482576 passed: `CliAppTests` and `McpPolicyVerbTests` enter through `CliHarness`, which calls `CliApp.Run` below `Main`, and no gate runs those verbs on a built binary. B.4b needs a gate on the published `keypaste` that reaches both sides of the dispatch.
- **How the SDK enters the vault's process (PRODUCT §3.9).** Today `ModelContextProtocol.Core` is linked only into `keypaste-mcp`, which never opens a vault. In one binary its code is in the image of the process that reads the master password and, as `keypaste agent`, holds the unlocked vault. None of it runs there unless reached: NativeAOT runs every module initializer at start-up, and neither the four packages' `net10.0` assemblies nor `src/` and `third_party/` use `ModuleInitializerAttribute`; static constructors run on first use; and the dispatch comes first. What grows is the reach of a compromised pinned version, which would ship inside the vault's binary; the decision replacing D-0019's confinement has to justify that.
- **A second source rule.** The rule keeping `VaultLocator`, `VaultSession` and `SecretInput` away can hold only for the dispatch's path into the bridge, because `mcp serve` and `mcp setup` open a vault through `AgentCommand` and `SetupCommand`. The guarantee also needs the reverse: no CLI file except the dispatch names `Keypaste.Mcp` or `ModelContextProtocol`. `BridgeSourceRulesTests` keeps holding the bridge's own files. The experiment's `InternalsVisibleTo` exposed every bridge internal to the CLI; one entry method is narrower.
- **No executable reference.** Run 36727509499's aot job failed at "prove they are AOT", because the CLI publish had copied `keypaste-mcp.runtimeconfig.json`: an executable's project reference brings its runtime configuration, on all three platforms. A library has none. Run 36728482576 removed the file before the existing gates.
- **Registrations already written.** v0.3.0's `keypaste setup` registers `keypaste-mcp` by absolute path, as `McpServerLocator` does today, so removing that file breaks every client so registered; an AppImage registration names the image instead (D-0334).
- **T-9.** "That closure contains no HTTP client" holds for packages, but `ModelContextProtocol.Core.dll` carries `HttpClientTransport` and `StreamableHttpServerTransport`, which the bridge never constructs. The statement is already wrong for today's bridge, so F.31 corrects it there.

## Decisions

None.

## Limits and follow-ups

- **One runner each.** Each figure comes from one hosted runner per platform, Linux twice; the spread between runners was not measured, and `linux-arm64` was not measured at all. Windows has no cache drop, so it gives warm starts only.
- **Not B.4b's build.** The bridge stayed an executable. `Keypaste.Consistency.Tests`' lock file was left as it was, and `app.yml` was not run.
- **The desktop payload grows.** The desktop packages carry `keypaste-mcp` alone (D-0334); carrying the one binary instead adds about 10 MB to each (9.5 MB on macOS).
- **Cancelled jobs.** Run 36727509499's `test (windows-2025)` and `test (macos-15)` were cancelled so run 36728482576 could start; its gate, `test (ubuntu-24.04)` and three compat jobs passed.
