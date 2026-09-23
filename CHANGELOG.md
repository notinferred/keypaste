# Changelog

Published versions are available at `https://dl.keypaste.com/v<version>/` with checksums and corresponding source. CLI/MCP binaries are unsigned and un-notarized; the desktop has no public release. [RELEASE.md](docs/RELEASE.md) records platform support and verification requirements. The release workflow requires a section matching each tag.

## Unreleased

All of this is source only: there is still no desktop download, and the published CLI is unchanged. Each entry that names a step links its record, which holds the full account.

`keypaste access` changes a vault's master password and attaches, replaces or removes an existing KeePass XML, 32-byte or 64-character hex keyfile. It never creates a keyfile and never removes a password, opens the new bytes with the new credentials before and after replacing the file, and keeps the replaced file as a backup; that copy and every earlier one still open with the old credentials, so delete them if those were exposed. The native-compiled CLI no longer reads an XML keyfile as the hash of the whole file ([V.1a2](docs/steps/V.1a2.md)).

Every command that opens a vault takes `--keyfile <path>` or `KEYPASTE_KEYFILE`, opening all four KeePass keyfile forms and a vault a keyfile alone protects. A vault keyed to an arbitrary file is named on stderr each time it opens, because editing that file loses the vault. A missing, empty or unreadable keyfile is refused before the password prompt. `keypaste setup` takes no keyfile, nothing records which keyfile a vault uses, and the desktop cannot open a keyfile vault yet ([V.1a1](docs/steps/V.1a1.md)).

The desktop app creates and renames groups, and renames and moves an entry in one write that keeps its identity and history; a refusal writes nothing and says why. The forms state when a rename changes which policy rule or agent exposure applies, and what renaming a project costs `keypaste run`. Search also covers usernames and URLs and never reads a password, a note or a protected field ([V.5b](docs/steps/V.5b.md)).

Core renames and moves groups and entries, keeping identity, history and data keypaste does not model; renaming the group `env/billing` renames the project. `env` at the root and the recycle bin are reserved names. An organized vault stays KDBX 4.0, and restoring a revision no longer renames the entry back ([V.5a](docs/steps/V.5a.md)).

The desktop's locked unlock screen restores a backup, opened with the master password it was made under, over a vault that is healthy, damaged or missing, keeping the file it replaces. A restored vault opens with the backup's password. Settings exports an exact encrypted copy to a new file ([V.4b](docs/steps/V.4b.md)).

A save over an existing vault first copies it into `<vault>.backups` beside it, keeping five, once per unlock and at most every fifteen minutes. A save whose copy cannot be written does not happen, and nothing turns that off. Each copy opens with the master password it was made under and is another offline guessing target; five copies beside the file do not survive losing the disk ([V.4a](docs/steps/V.4a.md)).

The desktop's Trash screen restores one recycled entry, or erases one behind its own confirmation, and Delete offers an immediate Restore; emptying the whole bin stays in KeePassXC ([V.3b](docs/steps/V.3b.md)).

`keypaste rm` and the desktop's Delete move an entry to the vault's KDBX recycle bin with its fields and history, unless the vault's recycle bin is switched off; a recycled entry is hidden from listing, `keypaste run` and agents. A vault that has recycled anything is written as KDBX 4.1, which KeePassXC 2.7 and KeePass 2.48 and later read ([V.3a](docs/steps/V.3a.md)).

Passphrases: `--generate --words N` on `add` and `env set`, the desktop's Generate boxes, and `keypaste generate --words N`, which prints one to stdout and stores nothing. Words come from EFF's long list, pinned by SHA-256; six words, about 78 bits, is the minimum. The separator defaults to `.` and a hyphen is refused, because four list words contain one.

The desktop shows an entry's history and restores a revision; the replaced value stays in history, so a restore can be undone by another.

The desktop stores an existing secret typed or pasted into a masked field. Paste drops one trailing line break and refuses anything a keyboard could not type; the master-password fields still ignore paste.

The entry pane shows usernames, URLs and notes with their punctuation and line breaks intact, while still replacing bidi overrides, zero-width characters and other text that can misrepresent itself.

The desktop creates vaults under the same rules as `keypaste init`, whose prompts, messages and exit codes are unchanged.

The release workflow can publish the Windows MSI and Linux AppImage beside the CLI at the same version prefix, each with its own manifest and attestation, and refuses them while they are unsigned.

## 0.3.0

Upgrade from `v0.2.0` to receive the save and approval-bridge repairs below. Two of them preserve data that `v0.2.0` can lose or refuse to write. Old archives remain available and immutable.

Saves re-read the vault between retries and refuse to overwrite a change made by another program while waiting. The earlier behavior, present since `0.1.0`, could replace that change with an older copy without retaining it in history. Reload the vault after a refusal.

On Windows 11 24H2 and Windows Server 2025, temporary files now use a private process directory. Shared 8.3 temporary-name aliases previously allowed unrelated Keypaste or KeePass saves to exhaust the retry budget, after which the save reported failure without committing. Older Windows builds were unaffected.

The bridge now tries the approver connection before reporting that `keypaste agent` is absent. Previously, worker-pool contention could exhaust the deadline before the first attempt, so a listing on a loaded machine could be told to start an approver that was already running. The connection budget is unchanged.

Saving a vault file that does not exist yet no longer waits behind other saves, and a save sleeping between retries no longer holds the in-process lock that orders them. Only an attempt that can transact takes it. A save that queued behind another save committing in the same process is now refused as changed on disk rather than overwriting it.

Windows saves now retry transient rename collisions. A failed save's stranded `vault.kdbx.tmp` is removed only when it can be opened exclusively.

Releases now publish a manifest and a Sigstore build attestation covering every asset. `gh attestation verify` with the published bundle confirms, without a GitHub account, that an asset was built by this repository's release workflow for its tag; SECURITY.md has the procedure. Attestation authenticates origin; the binaries remain unsigned and builds are not reproducible.

Fixed a race in the KeePassLib KDF registry that could throw or corrupt the engine list when several vaults were created concurrently on first use. KeePassInterop now forces registry initialization from its type initializer. Not reachable from the CLI, agent or desktop, which each open their first vault on a single thread.

In the desktop source, which this release does not publish, restoring the window no longer postpones the idle lock when the pointer has not moved, and input arriving after the idle deadline locks the vault instead of extending it. On Windows a restore delivers a pointer move at the resting cursor, which previously counted as somebody being there.

## 0.3.1-rc.2

Unadvertised candidate whose CLI/MCP behavior matches `0.3.0`. It exists to install the internal, unsigned Windows MSI and Linux AppImage on fresh runners and exercise the installed app, and it is the first tag through the Windows signing steps, which sign nothing while signing is not enabled. The desktop packages stay workflow artifacts and are not published.

## 0.3.1-rc.1

Unadvertised candidate whose CLI/MCP behavior matches `0.3.0`. It exists to run the desktop packaging tag path, which now also builds an internal, unsigned Linux AppImage kept as a workflow artifact and not published.

## 0.2.1-rc.3

Unadvertised candidate whose CLI/MCP behavior matches `0.3.0` and `0.2.1-rc.2`. It exists to run the desktop packaging tag path, which now builds an internal, unsigned per-user Windows MSI kept as a workflow artifact and not published.

## 0.2.1-rc.2

Unadvertised candidate whose CLI/MCP behavior matches `0.3.0`. It is the first release published with a manifest and build attestation, and exists to verify them against public bytes before an advertised release relies on them. The `v0.2.1-rc.1` tag published nothing: its release guard stopped before any build.

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
