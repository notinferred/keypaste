# G.3 — Add `keypaste doctor`

Completed 2026-10-20 on `main` at `413afc3`, source only: ci 39460137047 and app 39460137950, and ci 39460177139, dispatched with its `tools` jobs. The step was built directly on `main` alongside G.4b, in several commits, each pushed and run; this record and the STEPS, ROADMAP and CHANGELOG edits follow in one more.

## Amendments

The founder selected G.3 on 2026-10-19 with P.3b1 and G.4b and moved all three into 0.5.0. The approved plan settled what the row left open (D-0467):

- **The owner's version.** The owner's attach reply carries its version, so doctor can compare the app's or `keypaste agent`'s copy with this one.
- **Exit codes.** `7` when something needs fixing. A note, such as nothing holding the vault unlocked or a locked app, does not count, and `1` stays a usage error.
- **What "runs" means.** Doctor starts only the `keypaste` each tool's configuration names, with `--version` and `mcp --help …`, and the one PATH finds, with `--version`. It writes nothing and never starts an AI tool.

During the build the founder chose to refuse an approver name starting `keypaste-locked-` everywhere one is read (D-0466). Doctor therefore reports a `KEYPASTE_APPROVER` naming G.4b's pipe as a pipe name keypaste does not accept, and never connects to it.

## What changed

- **`Core/Health/`.** `HealthCheck.RunAsync` gathers findings, each `ok`, `fix` or `note`, with where it applies and a fix line. It has six readers:
  - **versions:** this copy, the owner's, each registration's and PATH's;
  - **the vault:** `--vault`, `KEYPASTE_VAULT` or the choice in `app.toml`, of which only the KDBX header is read;
  - **each tool:** its registration, whether it starts keypaste with arguments keypaste accepts, and whether it survives an upgrade, such as a disk image, a translocated copy, an AppImage mount or a broken Linux link;
  - **the owner:** whether it answers an attach, naming no tool, within 10 seconds;
  - **the settings files:** each one with its line;
  - **PATH.**
- **Read-only helpers.** These are `McpEntryReading.Launch`, which reads a registration's command and arguments from JSON and Codex's TOML, and `McpClientSetup.LooksInstalled`, `McpServerLocator.IsTransient` and `VaultClaim.HolderOf`, which reads `.owner` and never opens `.lock`. `AppSettings.TryLoad` and `RecentVaults.TryLoad` name the problem.
- **The wire.** `AttachReply.OwnerVersion` is optional and display-only, written as `owner_version`, and refused past 64 characters. Protocol version 2 is kept, since a bump would break at framing every pair of differing versions doctor exists to report.
- **The verb.** `keypaste doctor [--vault <path>] [--json]` prints one row per finding, its path in grey and its fix after `›`, then `N to fix` or `nothing to fix`. Everything another program printed is sanitized, and `--json` gives one object per finding. The fix for `clients.toml` never says to delete it, since deleting it lifts this computer's limits on agents.

## Evidence

ci 39460137047 and app 39460137950, and ci 39460177139, dispatched with its `tools` jobs, at `413afc3`:

- **Core.** `HealthCheckTests` covers the stability and version tables, the settings wording, relative PATH entries, an unreadable launch, and a registration that starts another program. Also `McpClientConfigTests` (each tool's format, Codex's string forms and Windows paths), `VaultClaimTests` (`HolderOf` takes nothing), `AppSettingsTests` and `RecentVaultsTests` (`TryLoad` names the line), and `ApproverProtocolTests` and `SessionAuthorityTests` (`owner_version`).
- **CLI.**
  - `DoctorVerbTests`: each finding, its wording and the exit codes. A `KEYPASTE_APPROVER` naming a locked app's pipe is a fix, and a real listener there hears nothing.
  - `Doctor_changes_no_byte_under_home_starts_no_ai_tool_and_asks_nothing`: the home is unchanged byte for byte, every process started is `keypaste --version` or `mcp --help …`, and no audit log or session appears.
  - `DoctorSourceRulesTests`: doctor and `Health/` reach no vault opening, password read, claim, prompt, file write, setup change or locked-app knock, with a control proving the scan finds an inserted call.
  - Also `CliAppTests` (23 verbs), and rows in `SecretHygieneTests` and `HostileNameRenderingTests`.
- **MCP.** `ServerOptionsTests`: `--help` before every pinned flag is help and opens no audit log.
- **Gates.**
  - `scripts/verify-connect-client.sh`: while the app holds the vault, `doctor --json` exits 0 with versions, vault, owner and Claude Code ok and Cursor a note, and leaves the home and the audit log unchanged. After the app shuts down, the owner is a note and doctor still exits 0.
  - `scripts/verify-real-tools.sh`: after setup, every real tool is ok, nothing changes, and no default audit log appears.

**What failed on the way, all on `main`:**

- `84183f7`, this step's code, pushed with G.4b's `1746ced`, failed to build Core on IDE0055 in the attach reply's reading (ci 39423606611, app 39423606604); `930469d` reformatted it.
- The real-tools run 39444475403 at `1c60e88` passed its `tools` jobs, `verify-real-tools.sh` among them, on all three runners; its Windows `test` job failed on G.4b's `LockedAppTests`, recorded in [G.4b](G.4b.md), as are the later failures before the evidence above.

## Decisions

D-0467 in [DECISIONS](../../DECISIONS.md). No guarantee, threat or limit in THREATS changed: doctor reads what the CLI already reads, starts only keypaste, and attaches as `keypaste grants` does.

## Limits and follow-ups

- Whether a registration survives an upgrade is judged from what it points at; only an upgrade run proves it.
- A Homebrew install's path under `Cellar` is not yet recognized as one that moves on upgrade (3.7a).
- The app's version is known only while it holds the vault, and a locked app reads as nothing holding the vault unlocked.
- Source only.
