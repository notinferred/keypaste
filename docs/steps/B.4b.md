# B.4b — Carry the MCP bridge in the CLI as `keypaste mcp`

Completed 2026-10-05 in `70c7117`; runs 37316706059 (ci), 37314704826 (app, at `65c654d` — pure test change at `70c7117` does not trigger app.yml).

## Amendments

`--help` and `-h` were removed from the CLI-verb exclusion list and routed to the bridge, so `keypaste mcp --help` shows the bridge's own options rather than the CLI subcommand listing. `verify-demo.sh` checks those options and caught the mismatch before the record was written.

## Evidence

Source rule tests: `CliDispatchSourceRulesTests` (2 rules with negative controls — no CLI file except `Program.cs` names `Keypaste.Mcp` or `ModelContextProtocol`; `Program.cs` calls `BridgeEntry.RunAsync`) and `BridgeSourceRulesTests` (extended to cover `VaultLocator` and `VaultSession`). All gate scripts updated and passing: `verify-mcp-stdio.sh`, `verify-approval-e2e.sh`, `verify-log-chain.sh`, `verify-lock-boundary.sh`, `verify-policy-e2e.sh`, `verify-agent-activity.sh`, `verify-desktop-approval.sh`, `verify-approval-e2e.sh`, `verify-mcp-run.sh`, `verify-held-saves.sh`, `verify-current-state.sh`, `verify-session-authority.sh`, `verify-session-lifecycle.sh`, `verify-demo.sh`, `verify-keepassxc-projects.sh`, `verify-keepassxc-fields.sh`. `publish-desktop-bridge.sh` now publishes `Keypaste.Cli` and checks `keypaste mcp --help`. Linux AppImage, macOS DMG and Windows installer gates each verify the combined binary answers `keypaste mcp --help`. ci run 37316706059 at `70c7117`: all profiles green on ubuntu-24.04, macos-15, windows-2025. app run 37314704826 at `65c654d`: desktop and consistency profiles green.

## Decisions

D-0418: `Keypaste.Mcp` becomes a library `Keypaste.Cli` enters through `BridgeEntry.RunAsync`, dispatched from `Program.cs` before any vault access; the combined `keypaste` replaces `keypaste-mcp` in desktop packages and gates; no CLI file except `Program.cs` names `Keypaste.Mcp` or `ModelContextProtocol`; the bridge path does not reach `VaultLocator`, `VaultSession` or `SecretInput` (PRODUCT §3.9). Supersedes D-0019's confinement of `ModelContextProtocol.Core` to the bridge process and D-0334's separate payload binary.

## Limits and follow-ups

`keypaste-mcp` remains in doc comments, script comments, and a handful of error-message prefixes in test fixtures that use explicit paths rather than the locator — none of these affect behavior or gates, and all are addressed at L.1 when that step rewrites guides and README for 0.5.0. MCP client configurations written before this change that name `keypaste-mcp` need updating to `keypaste` with `["mcp"]` as arguments; the CHANGELOG entry notes this.
