# F.48 — Finish the one `keypaste` binary B.4b started

Completed 2026-10-05 at `3b32755` on `task/f48`, integrated to `main` as one commit; found and finished in one step at the founder's direction of 2026-10-05 after a review of B.4b, so it never had a STEPS row. Runs at `3b32755`: ci 37346620487 and app 37346625484, dispatched so every job ran, and dev 37341758641 on Windows with both gate sets. Install 37346630656 passed on Linux x64, Linux arm64 and macOS against the advertised 0.3.0; its Windows job did not reach the install block (F.50).

## Amendments

The founder directed on 2026-10-05:

- every `keypaste mcp` help form prints one text;
- CI scoping stays as D-0403 defines it, and the repair goes through a branch;
- every stale `keypaste-mcp` is renamed, including PRODUCT.md's `--allow-run` sentence, which is editorial;
- `Keypaste.Mcp` stays its own project;
- the work integrates straight to `main`, without a pull request.

After the first pass, the founder asked why the change added code. The second pass removes what one binary made unnecessary, and no longer supports releases before 0.5.0, as PRODUCT v1.11 (D-0416) already requires.

## Corrects B.4b

- Its record cites ci 37316706059 as "all profiles green". That run, a push scoped against `65c654d`, skipped the integration gates, the AOT publish and the script fixtures. The gates and the AOT publish passed in ci 37312699133 at `27c311a`, and the packages in app 37314704826. The script fixtures' only run, ci 37314704696, failed in `verify-release-matrix.sh`, because `release-targets.json` still declared `keypaste-mcp` and `Keypaste.Mcp.csproj`.
- "2 rules with negative controls" was not so: `CliDispatchSourceRulesTests`' control could not fail, and no rule held the dispatch's path. T-9's trim claim cited no run.
- B.4b changed `verify-install.sh` ahead of the README block it verifies, so the check of the advertised 0.3.0 required a `keypaste mcp` 0.3.0 does not have. It also left the desktop exercises requiring `keypaste-mcp`, the PRODUCT §3.9 justification unwritten, and source-build statements in the guides false.

## Evidence

- **Dispatch.** `ProgramDispatchTests` decides `keypaste mcp`, `help`, `-h`, `--help` and every bridge option go to the bridge, and `serve`, `setup`, `policy` and other verbs do not. `ServerOptionsTests` covers every help form and a refused argument. `verify-mcp-stdio.sh` step 9 runs the built binary: one help text for the three forms on stdout, naming `--client-label`, `serve`, `setup` and `policy`; `mcp serve --help` equal to `agent --help`; `mcp setup --help` equal to `setup --help`; `mcp policy --json` printing `[]` in an empty home. A dispatch that took every `mcp` argument fails that step, and one that took none fails steps 1 to 8.
- **§3.9.** `BridgeClosureTests.No_package_the_bridge_brings_has_a_module_initializer` walks the CLI's lock file from `keypaste.mcp` and reads each assembly's `<Module>` for a static constructor. Its control, `A_module_initializer_is_found`, finds the test assembly's own. `CliDispatchSourceRulesTests` and `BridgeSourceRulesTests` scan with one function, run on the real `CliApp.cs`, `AgentCommand.cs` and `Program.cs` unchanged (clean) and mutated (caught). The bridge rule now covers `src/Keypaste.Cli/Program.cs` as well as `src/Keypaste.Mcp`.
- **Setup.** `SetupVerbTests.The_running_keypaste_is_started_as_the_bridge` and `Setup_run_through_dotnet_registers_nothing`. `McpClientSetupTests` registers `keypaste` with `mcp`.
- **Release definition.** `verify-release-matrix.sh` passes against the corrected definition in ci 37346620487's scripts lane, whose fixtures ran. In app 37346625484 the bridge was added to each payload and the MSI, AppImage, app bundle and DMG checks ran `keypaste mcp --help` on what they package.
- **Lock files.** `bash scripts/dev.sh --relock`, in dev 37333854599, regenerated `Keypaste.Mcp`'s lock as Core's shape. It also refreshed three app-side locks whose `Keypaste.Core` project version (0.3.1) predated the 0.4.0 bump; locked-mode restore does not compare project versions.

## Decisions

- D-0419: `ModelContextProtocol.Core` and its three `Microsoft.Extensions.*` abstractions may share the binary that reads the master password and, as `keypaste agent`, holds the unlocked vault. This is PRODUCT law 3.9's written justification, here because no pull request carried it.
  - None of their code runs unless the bridge starts. No package in the closure has a module initializer, which NativeAOT would run in every process. A static constructor runs only when its type is first used, and no CLI file but `Program.cs` names the bridge or the SDK.
  - The bridge's path names no vault or password type.
  - What this does not bound: it is no process boundary, so a compromised pinned version would ship inside the vault's binary. The content-hash pin and the `--locked-mode` restore fix the code to the reviewed 1.4.1 package (THREATS T-9).
  - It is accepted because, by B.4a's measurements, the single binary is 14 to 16% smaller than the two it replaces, and every desktop package, which carries the CLI anyway, ships one artifact instead of two.
- D-0334 is rewritten for one binary, with its wording archived. D-0358 names `keypaste mcp --allow-run`.
- Binding only this step's code:
  - The CLI's `CommandLine` moved to Core and the bridge parses with it. Its old reason for a parser of its own, that repeated options were refused, stopped being true at V.7a.
  - `keypaste setup` registers the `keypaste` that runs it, and `--server-path` is gone.
  - `verify-install.sh` keeps the 0.3.0 check until L.1 moves the install blocks.

## Limits and follow-ups

- `upgrade-desktop.yml` makes its fixture with a published CLI, and every published CLI predates `keypaste mcp`, so it runs again once 0.5.0 is published (D-0416). `install-desktop.yml` first meets the one binary with a 0.5.0 candidate.
- The demo GIF's label changes at its next render. `site/web` pages and README's published-version text change at L.1.
- STEPS E.1d still needs G.5, though the bundle carries `keypaste` since B.4b; that dependency is the founder's to re-plan.
- F.49 holds a Windows failure seen in dev 37337588072: a grant not reused after a large listing. This step changed that test's path only in how its harness arguments are parsed, to the same options; the Windows rerun passed it, which does not close it.
- F.50 holds `install.yml`'s Windows job, cancelled before its install block since 2026-09-21. `verify-install.sh` is byte for byte the version that passed there on 2026-09-16.
