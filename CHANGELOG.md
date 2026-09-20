# Changelog

Published versions are available at `https://dl.keypaste.com/v<version>/` with checksums and corresponding source. CLI/MCP binaries are unsigned and un-notarized; the desktop has no public release. [RELEASE.md](docs/RELEASE.md) records platform support and verification requirements. The release workflow requires a section matching each tag.

## Unreleased

Deleting an entry no longer destroys it. `keypaste rm` and the desktop's Delete move the entry to the vault's KDBX recycle bin, keeping its identity, its fields and every revision in its history, and both say so rather than saying there is no undo. KeePassXC opens the same file and shows the same bin, because it is KeePass's own: the entry is where KeePassXC would have put it, and KeePassXC can restore it. A vault whose owner switched the recycle bin off in KeePassXC still deletes permanently, and the confirmation says that instead. Nothing else in keypaste can see a recycled entry — not `keypaste ls`, not the app's list, not `keypaste run`, and not an agent, whatever it has been exposed. Core lists what is in the bin, puts one back where it came from and removes one for good; the app's trash view comes next, so until then recovering one means opening the vault in KeePassXC.

The cost is worth stating plainly: a deleted value is still in the file. Erasing something keypaste wrote is now two deliberate acts, the delete and then the purge, where it used to be one. A restore is refused outright if another entry has taken the name in the meantime, because two entries answering to one name is the condition keypaste refuses everywhere else, and a recovery must not create it. An entry whose group is gone comes back at the root.

A vault that has recycled anything is written as KDBX 4.1 instead of 4.0. The field that records where an entry came from exists only in 4.1, and the vendored writer did not ask for 4.1 on its account, so a save dropped it and a restore after reopening the file had nowhere to put the entry. KeePassXC 2.7 and KeePass 2.48 and later read 4.1; older readers do not, and a vault only changes once something has actually been deleted. This is source only: there is still no desktop download, and the published CLI is unchanged.

keypaste generates passphrases as well as passwords. `keypaste add <entry> --generate --words 6` and `keypaste env set <project> <KEY> --generate --words 6` store a six-word passphrase instead of a twenty-character password, `--separator` chooses what goes between the words, and the desktop's two Generate boxes offer the same choice with the word count, the separator and what the count is worth stated beside them. `keypaste generate --words 6` prints one and stores nothing: it is the only command whose output is a fresh secret on stdout, because a passphrase meant for somebody else has to be readable and no vault holds it, and what it is made of goes to stderr so a redirect captures only the passphrase. It opens no vault and prompts for nothing.

The words come from EFF's long list of 7,776, vendored into keypaste and pinned by SHA-256; keypaste refuses to generate from a list that is not the pinned one. Each word is worth about 12.9 bits, so six words is about 78 bits and six is the floor. The separator defaults to a full stop rather than the conventional hyphen, and a hyphen is refused: four of the list's words are spelled with one, so a hyphen-joined passphrase cannot be split back into the words it was counted in. Nothing yet consumes a passphrase automatically; encrypted sharing is unimplemented and outside the focused local release.

The desktop app recovers an old password. An entry pane offers Show history: the values that entry held before, newest first with the time each was current, and the one you pick shown beside the values it has now. A revision password is dots until you hold it, like an env value, and Restore this makes that revision current. What it replaces is kept in history in its turn, so a restore can be undone by another restore, and `keypaste get --show` reads back what the app wrote. Recovering a replaced password no longer needs KeePassXC. This is source only: there is still no desktop download.

The desktop app stores a secret you already have. Untick "Generate a password" when adding an entry, or "Generate a value" when adding a variable, and a masked field appears to type or paste one into; an entry's edit form and a variable's Replace button offer the same field for a replacement, and leaving it empty keeps the value that is there. `Ctrl/Cmd+V` works in those fields, dropping the single trailing newline a copied token usually carries and refusing outright anything a keyboard could not have typed, rather than storing a quietly altered secret. The three master-password fields still ignore paste. A replaced value stays in the entry's KeePass history, and the app now reads it back.

The entry pane no longer mangles what it shows. A username, URL or notes body keeps its slashes, brackets, backslashes and line breaks — `https://example.test/path?a=b#c` displayed as `https: example.test path a b c` before — while bidi overrides, zero-width characters and other text that can misrepresent itself are still replaced. Titles and group paths are unchanged.

The desktop app creates vaults. The unlock screen offers Create beside Browse, asks where the file goes and for a master password twice, and opens the new vault on an empty entry list; it no longer tells a new user to go and run `keypaste init`. The rules deciding what may be created — an occupied path refused with the file there untouched, an empty password refused, a confirmation that must match, and nothing written until all three pass — moved into the shared core, so `keypaste init` and the app apply one set rather than two. `keypaste init` behaves exactly as it did: the same prompts, the same messages and the same exit codes, now pinned by tests. This is source only: there is still no desktop download.

The release workflow can publish the Windows MSI and Linux AppImage beside the CLI at the same version prefix, each component with a manifest and an attestation bundle of its own. The desktop packages are taken from the app workflow's run for the tag and are checked against that run's attestation before they are staged, rather than rebuilt. Nothing desktop is published while it is unsigned: the packages stay internal and unsigned, and the publication path refuses them until signing is enabled. There is still no public desktop download: publishing waits both on a signing identity and on the desktop being worth installing.

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
