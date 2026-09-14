# Changelog

Published versions are available at `https://dl.keypaste.com/v<version>/` with checksums and corresponding source. CLI/MCP binaries are unsigned and un-notarized; the desktop has no public release. [RELEASE.md](docs/RELEASE.md) records platform support and verification requirements. The release workflow requires a section matching each tag.

## Unreleased

Releases now publish a manifest and a Sigstore build attestation covering every asset. `gh attestation verify` with the published bundle confirms, without a GitHub account, that an asset was built by this repository's release workflow for its tag; SECURITY.md has the procedure. Attestation authenticates origin; the binaries remain unsigned and builds are not reproducible.

The bridge now tries the approver connection before reporting that `keypaste agent` is absent. Previously, worker-pool contention could exhaust the deadline before the first attempt. The connection budget is unchanged.

Saves re-read the vault between retries and refuse to overwrite a change made by another program while waiting. The earlier behavior, present since `0.1.0`, could replace that change with an older copy without retaining it in history. Reload the vault after a refusal.

Windows saves now retry transient rename collisions. A failed save's stranded `vault.kdbx.tmp` is removed only when it can be opened exclusively.

On Windows 11 24H2 and Windows Server 2025, temporary files now use a private process directory. Shared 8.3 temporary-name aliases previously allowed unrelated Keypaste or KeePass saves to exhaust the retry budget. Older Windows builds were unaffected.

Fixed a race in the KeePassLib KDF registry that could throw or corrupt the engine list when several vaults were created concurrently on first use. KeePassInterop now forces registry initialization from its type initializer. Not reachable from the CLI, agent or desktop, which each open their first vault on a single thread.

## 0.2.1-rc.1

Unadvertised candidate built from the source described under Unreleased. It is the first release published with a manifest and build attestation, and exists to verify them against public bytes before an advertised release relies on them.

## 0.2.0

Upgrade from `v0.1.0` to receive the data-preservation and approval repairs below. Old archives remain available and immutable.

This release uses the source verified by `v0.2.0-rc.1`. The unadvertised candidate was downloaded anonymously, checked against recorded asset hashes, and installed on each advertised target to exercise vault creation and environment injection.

MCP requests arriving immediately after initialization now wait for the pending identity handshake. This fixes a macOS race that refused clients which had already sent initialization. Clients that never initialize remain refused.

Download pages now state OS floors and their evidence: glibc 2.35, macOS 13 and Windows 10 1809. Linux x64 is checked on Debian 12 with an Alpine rejection control. The macOS and Windows values are cited .NET 10 floors without a Keypaste installation on those minimum versions; Linux ARM64 has no equivalent container check.

An approved field too large for one response now returns an explicit delivery refusal and audit result while retaining the connection and other grants. The secret is never truncated. Repeated requests use the recorded outcome without prompting again; large values require manual copying.

Large entry listings now fit the response budget and disclose when names were omitted. They expose the same authorized subtree, offer no paging operation, and preserve cached approvals. Concurrent requests on one connection are refused immediately with `BUSY` while an approval is pending, including listing requests. Busy responses describe the possible human wait without identifying the blocking request, and each refusal is audited.

Approval expiry now uses both wall and monotonic clocks, preventing rollback or suspension from extending access. Denial cooldowns also resist clock changes. Previously, moving the wall clock could revive an expired grant or shorten a refusal's cooldown.

Ambiguous entry paths now fail across CLI reads, desktop copy and edits. Group and title identify an entry separately, so slash-containing titles cannot redirect a read or write. `env rm` and `env set` receive the same protection. Duplicate identities are refused without rewriting the vault; listings retain duplicates, and removals with no match return exit 3. `add` permits a distinct identity that creates a path collision, reports that ambiguity and refuses further additions through an already ambiguous path.

`env pull` preserves a source changed during prompting or replaced before deletion. It records path and content, then moves and verifies the imported file during removal. Missing sources no longer report successful deletion. `env export` refuses to overwrite its source vault or any other KeePass vault, including with `--force`; checks resolve symbolic links, junctions and ancestor links.

Approval reasons retain ordinary path separators while hostile markup remains sanitized. Audit records use the resolved entry path instead of opaque handles.

`keypaste setup` configures Claude Code and Codex through their `mcp add` commands and prints configuration for Cursor and Claude Desktop. `--dry-run` previews commands, `--remove` removes only Keypaste, and repeated setup updates the configuration idempotently.

The pre-publication review added regressions for hostile-name rendering, scrubbed approval text and exception-path auditing. Sanitization now covers CLI listings, import refusals and desktop display while preserving exact values for addressing, editing and copying. The prompt marks scrubbed entries or reasons. Vault failures are audited, and an accept failure ends the approver instead of leaving its vault unlocked.

Desktop source changes include accessibility checks for the master-password mask, which exposes only placeholder and length. Paste and a screen-reader name remain missing. Clipboard writes pending at lock, quit or Clear now are cleared when they complete; quitting waits for them, and an unreadable clipboard no longer prevents scheduled clearing. Non-secret run commands remain on the clipboard.

Minimize-lock now uses the normal lock path and responds to setting changes immediately. It was observed on Windows; macOS and Linux still require manual observation. Focus loss and macOS Cmd+H are separate events. Startup now applies stored idle and theme settings before rendering, honors custom timeout values, and preserves unreadable settings files while retaining safe defaults.

First-party binaries now report the Keypaste publisher and copyright explicitly. The bundled KeePassLib carries its upstream attribution. Published `v0.1.0` metadata remains unchanged.

## 0.2.0-rc.1

Unadvertised candidate using the same source as `0.2.0`. Its published URL exercised tag-only behavior, including desktop prerelease versions, release matrices and publication. Its changes are listed under `0.2.0`.

## 0.1.0

The first advertised release uses the same behavior as `0.1.0-rc.1`. The pipeline completed, a public asset was downloaded and checked manually, and install pages moved to this version. The candidate remains at its unadvertised URL.

This version provides a local KDBX4 vault, environment injection without an environment file, and an MCP bridge for human-approved access to one field for a stated lifetime. Calls enter a local hash-chained audit log. Vault and bridge use requires no account or hosted service.

Binaries are unsigned and un-notarized; checksums do not authenticate their publisher. Approval uses the terminal, and the desktop is source-only. Linux requires glibc 2.35; musl is unsupported by these binaries. Intel Macs and Windows ARM64 use source builds. [THREATS.md](THREATS.md) T-21 describes download trust.

## 0.1.0-rc.1

The first published tag exercised the release pipeline at an unadvertised URL.

NativeAOT binaries cover `linux-x64`, `linux-arm64`, `osx-arm64` and `win-x64` without requiring a .NET runtime. The recorded binaries were about 10 MB each and started in a little under half the framework-dependent build's time. Intel macOS has no published target; [runner availability](https://docs.github.com/en/actions/reference/runners/github-hosted-runners) does not replace the project's missing build and verification work.

Release checks run against the binaries being uploaded, covering real-process approval and refusal, MCP pipes, audit tampering, injection, demo transcripts and KeePassXC compatibility on all four targets.

Linux binaries require glibc 2.35 or newer. Linux x64 is checked on Debian 12; ARM64 has no equivalent container check. Alpine and other musl distributions are unsupported. Vault format, approval behavior and audit format were unchanged.
