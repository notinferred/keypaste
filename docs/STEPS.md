# Build plan

This plan owns current status, build order and acceptance criteria. [PRODUCT](PRODUCT.md) owns scope, [FEATURES](FEATURES.md) the dated capability baseline, and [RELEASE](RELEASE.md) distribution requirements. [DECISIONS](../DECISIONS.md) D-0090 records the adopted structure.

Only the next five steps are detailed. Later steps have one line with ID, name, Needs and purpose; their wording remains provisional until activated. On completion, reduce a step to one line under Completed steps and expand the next unchecked step.

## Current status

The local vault, CLI/env workflow and approval bridge are published as `v0.2.0`, installed and exercised on all four native targets from clean runners. The desktop has partial entry/env screens in source; hosted service, web vault, mobile client and organization credential service remain unimplemented. Concurrent KDF startup failures (F.11a/F.11b) and save overruns (F.10a/F.10b) remain open. F.2b2 is BLOCKED on hardware.

F.6, F.8 and F.9 are closed. F.9 had two worker-pool defects: the bridge's connect deadline could expire before it tried the pipe, reporting a running approver absent, and the test host blocked workers reading its anonymous pipes (D-0128). Both are repaired in `main`; `0.2.0` contains neither repair and still discloses the first defect (D-0129). F.10 remains separate because its retry sleeps matched the requested total closely, while the first recorded interval combined save-gate waiting and attempt work. Their separate costs and the overrun mechanism remain unproved.

Release foundations are closed through R.0c. Download pages mark `0.1.0` superseded, and the pages, release definition and keypaste.com advertise `0.2.0`. R.0e corrected live deployment after finding D-0121's workflow had never deployed. R.1 still requires daily desktop creation and editing, recovery, migration, native approvals and browser workflows, with packaging, signing, publication and observed user acceptance. Step 3.8's attestations gate desktop publication through 4.7c; CLI patches can proceed independently (D-0115).

## Build order

| Milestone | Completion or activation |
|---|---|
| Working proposition | A published desktop + browser password manager with usable env/agent workflows; R.1 verifies it |
| Pilot ready | Managed encrypted sync with account/device controls, restore and observed operation; R.2 verifies private-beta entry |
| Paid release | Nontechnical onboarding, a supported phone workflow, review, billing and support; R.3 verifies consumer release |
| Expansion | Advanced KeePassXC coverage, web access and organization credentials |
| Scale | Capacity, fleet and operational improvements |

Needs name only what a step's own code or verifier cannot run without. A calendar date or usage window, a human act, a milestone or release gate, a freeze protecting a stored result and a tidier order are never Needs (D-0132). Gates decide what may ship, never what may be written: **Ships after** names the gates a built result must pass before publication, and a milestone gate's Needs are the evidence its verifier exercises. Rows marked **Human** are founder or external work on their own track; a code row needs one only when its verifier requires the artifact it produces, such as a signing identity. Protect a recorded result by versioning or voiding it, not by forbidding change.

Pick the first unchecked code step whose Needs are complete in the earliest unfinished milestone, skipping Human and BLOCKED rows; milestone order is priority, not dependency. Passing a numbered gate advances its milestone even if explicitly optional rows remain open. Keep IDs stable and split large steps into lettered children.

An unknown mechanism gets a diagnosis child before its repair. During an external wait, follow [CLAUDE.md](../CLAUDE.md#records) and continue with the next ready code step; the waiting row remains open. Aggregate rows follow their first ready child and close only when every child passes.

```text
Build the next ready step in docs/STEPS.md. Follow CLAUDE.md, implement its Build line, run its verifier and relevant checks, then record the actual result and evidence.
```

## Completed steps

Each row keeps its original bounded scope: 4.2 does not include entering an existing secret, undoing a deletion or restoring history, and the F rows are defects found after those original checks.

| Step | Name | Evidence |
|---|---|---|
| 0.1 | Shared core, locked dependencies, solution scaffold | [build properties](../Directory.Build.props) |
| 0.2 | Bidirectional KDBX compatibility | [round-trip tests](../tests/Keypaste.Core.Tests/VaultRoundTripTests.cs), [KeePassXC gate](../scripts/verify-keepassxc-compat.sh) |
| 0.3 | Core CLI vault verbs | [CLI commands](../src/Keypaste.Cli/Commands) |
| 1.1 | Env grouping and storage | [EnvStore tests](../tests/Keypaste.Core.Tests/EnvStoreTests.cs) |
| 1.2 | Env import and injection | [injection gate](../scripts/verify-run-injection.sh) |
| 1.3 | Explicit dotenv export | [export tests](../tests/Keypaste.Cli.Tests/EnvExportTests.cs) |
| 1.5b | Documented clipboard controls and residuals | [SECURITY](../SECURITY.md) |
| 2.1 | MCP transport and schemas | [stdio gate](../scripts/verify-mcp-stdio.sh) |
| 2.2 | Human approval, scoped cached grants | [approval gate](../scripts/verify-approval-e2e.sh) |
| 2.3 | Local policy pre-approvals | [policy gate](../scripts/verify-policy-e2e.sh) |
| 2.4 | Local hash-chained audit | [log-chain gate](../scripts/verify-log-chain.sh) |
| 2.4a | Reporting channel and threat model | [SECURITY](../SECURITY.md), [THREATS](../THREATS.md) |
| 2.4b | External contact delivery | [reporting channel](../SECURITY.md) |
| 2.5 | CLI/MCP demo | [verified transcript](demo.md) |
| 2.6 | Client setup command, source only and newer than v0.1.0 | [CHANGELOG](../CHANGELOG.md#020-rc1) |
| 3.0 | Public repository | D-0089 in [DECISIONS](../DECISIONS.md) |
| 3.1 | Demo GIF | [asset](demo/keypaste-demo.gif), D-0088 |
| 3.2b | CLI launch essay, distinct from the future post 3.2 | [essay](keepass-and-agents.md) |
| 3.4 | CLI/MCP pipeline and public archives — Published | [release inventory](RELEASE.md) |
| 4.1 | Desktop unlock and idle lock, source only | [session tests](../tests/Keypaste.App.Tests/Session/AppVaultSessionTests.cs) |
| 4.2 | Search, generated entries, env screens and lost-write protection, source only | [entry tests](../tests/Keypaste.App.Tests/ViewModels/EntriesViewModelTests.cs) |
| F.1a | One entry identity for selection and mutation; an ambiguous one is refused | [identity tests](../tests/Keypaste.Core.Tests/EntryIdentityTests.cs), [write-back gate](../scripts/verify-keepassxc-writeback.sh); D-0091 |
| F.1b | Export refuses its source vault and any KDBX destination | [path rule tests](../tests/Keypaste.Core.Tests/PathIdentityTests.cs); D-0092 |
| F.1c | Cleanup is bound to the imported bytes and the path they came from | [snapshot tests](../tests/Keypaste.Core.Tests/SourceSnapshotTests.cs); D-0093 |
| F.1e | One resolver for reads and removals; an ambiguous path is refused | [verb tests](../tests/Keypaste.Cli.Tests/VerbTests.cs); D-0094 |
| F.2a | Launch arms the session and the palette from `app.toml` | [startup tests](../tests/Keypaste.App.Tests/StartupSettingsTests.cs); D-0095 |
| F.2b1 | Minimizing calls the same session lock as the timeout and `Ctrl/Cmd+L` | [minimize tests](../tests/Keypaste.App.Tests/MinimizeLockTests.cs); observed on Windows 10 Pro 19045, 2026-09-08; D-0096 |
| F.2c | A clipboard write that lands after a lock or a quit is taken back | [in-flight tests](../tests/Keypaste.App.Tests/Clipboard/ClipboardWritesDoNotOutliveTheAppTests.cs); D-0098 |
| F.2d | The master-password field publishes its length and never its characters | [automation tests](../tests/Keypaste.App.Tests/Controls/MaskedInputAutomationTests.cs); D-0099 |
| F.3a | Grant expiry and the denial cooldown are held on both clocks, failing closed | [Deadline tests](../tests/Keypaste.Core.Tests/DeadlineTests.cs), [gate tests](../tests/Keypaste.Core.Tests/ApprovalGateTests.cs); D-0100 |
| F.3b | A second call on one MCP connection is refused where it arrives, not queued | [concurrency tests](../tests/Keypaste.Mcp.Tests/ConcurrentRequestsTests.cs); D-0101 |
| F.3c | Listing replies are bounded by encoded bytes, and omissions are announced | [protocol tests](../tests/Keypaste.Core.Tests/ApproverProtocolTests.cs), [large-vault tests](../tests/Keypaste.Mcp.Tests/LargeVaultListingTests.cs); D-0102 |
| F.3d | An oversized release is refused on a connection that stays open, and recorded as approved-and-undeliverable | [large-credential tests](../tests/Keypaste.Mcp.Tests/LargeCredentialTests.cs); D-0103 |
| F.4a | A release destination is refused unless a listing positively verifies it is empty | [verify-release-destination.sh](../scripts/verify-release-destination.sh), 26 cases; [tripwire tests](../tests/Keypaste.Core.Tests/ReleaseDestinationIsCheckedTests.cs); R2's own listing reply is still unobserved; D-0104, D-0106 |
| F.4b | Every binary keypaste ships reports keypaste as its publisher | [verify-publisher-metadata.sh](../scripts/verify-publisher-metadata.sh), [attribute tests](../tests/Keypaste.Core.Tests/PublisherMetadata.cs); D-0097, D-0105 |
| F.5 | A pipelined `tools/call` is waited for rather than refused, and an unnameable client is still refused — weak evidence; a third occurrence reopens it | [grace tests](../tests/Keypaste.Mcp.Tests/HandshakeGraceTests.cs); D-0113 |
| F.6 | A save no longer contends for a temporary name any other KeePass-family program wants | [V-F.6](../tests/Keypaste.Core.Tests/ConcurrentVaultSaveTests.cs) red 5 of 5 on `windows-2025` without the fix and green 5 of 5 with it, budget 1 both ways; runs 34640639830 and 34644315829 bound the two sets; D-0122, D-0123 |
| F.7 | A save refused the vault's own name is retried, and reverts nobody | [transacted-name tests](../tests/Keypaste.Core.Tests/VaultSaveUnderATransactedNameTests.cs); 6800 observed by [txf-probe](../scripts/txf-probe.cs) on Windows 10 Pro 19045 and by the regression on windows-2025, ci run 34602290950; D-0119, D-0120 |
| F.8 | A refused listing says which refusal it was, and how long it took | [ListingCall](../tests/Keypaste.Mcp.Tests/ListingCall.cs) red 2 of 2, green 8 of 8; 12 of 80 on `windows-2025`, probe run 34653284139: `no-approver` at 5834 ms against 500 ms, listener up throughout. Dispatched against a removed commit, tests unchanged. D-0124 |
| F.9 | A running approver is never reported absent for want of a free worker | [PoolShortageTests](../tests/Keypaste.Mcp.Tests/PoolShortageTests.cs) red then green for the connect and the harness pipes; run 34728679951: 0 of 80 bridge failures starved and as-found; D-0128, D-0129 |
| R.0a | One checked release definition drives both workflows and the download pages | [release-targets.json](../release-targets.json); [verify-release-matrix.sh](../scripts/verify-release-matrix.sh) refuses 31 cases; the prerelease suffix reached all three desktop targets on tag run 34542636558; D-0108 to D-0112 |
| R.0b | A release is complete only when the public bytes say so | [release-completion.sh](../scripts/release-completion.sh) recorded 0.2.0 and verified all 11 assets anonymously at the origin, run 34549357893; six fixtures in [verify-release-completion.sh](../scripts/verify-release-completion.sh); D-0116 |
| R.0c | 0.2.0 published and installed on all four native targets from clean runners | release run 34549357893; install runs 34549933842 and 34551580519, the second running README verbatim, each target creating a vault and injecting into a child. macOS and Windows floors stay `cited`: the runners sit above them, not on them |
| R.0d | What the advertised `v0.1.0` gets wrong is named where it is downloaded | [matrix gate](../scripts/verify-release-matrix.sh) holds three pages to `known_defects` both ways, at the checked-out ref; the [site check](../scripts/verify-site-disclosure.sh) asks the origin by hand, never from a workflow; D-0117, D-0127 |
| R.0e | keypaste.com deploys from a path that has actually run | Cloudflare Git build `1716bdb6` from `309aac3` deployed `1fcc038c`; the origin passes [both](../scripts/verify-site-disclosure.sh) [checks](../scripts/verify-site-endpoint.sh) and serves the ref byte-for-byte. `site.yml` deleted after four failed runs; D-0121, D-0127 |
| 10.1 | Initial hostile review and remediation | D-0084 in [DECISIONS](../DECISIONS.md) |
| K.1 | Pinned SDK installed | [global.json](../global.json), D-0076 |

The [current release matrix](RELEASE.md#current-distribution--2026-09-07) owns public availability: a completed source step does not mean the behavior is in the current download.

## Working proposition

A person can install the product, create or import a vault, save and use existing credentials, recover mistakes, inject a project's env and approve or deny agent access. Build through the shared core; desktop, CLI and browser steps name their surfaces. No account is required. R.1 closes the milestone only after the published product passes its complete user journey.

### Repair existing behavior — first ready work

The bounded 2026-09-07 review found these while the local Windows suites reported 1,169 passed and five platform-specific skips. Each needs a regression that fails before the fix, recorded in repository fixtures rather than a maintainer's temporary files.

- [ ] **F.11 — Repair concurrent KDF startup failures.** Needs: F.11a, F.11b. — Aggregate: preserve the first-use failure, repair the confirmed mechanism and retain the regression and compatibility evidence.
- [ ] **F.11a — Isolate the KDF registry's concurrent first use.** Needs: 0.2.
  **Measured:** F.9's local investigation recorded `KdfPool.GetDefaultParameters` null references in two of thirty fresh suite runs, one failing 98 tests. On 2026-09-12, setup verification at `083fa45` plus the working-tree delivery changes failed 130 CLI tests in 661 ms: the first saves reported null references and later reads found invalid headers. Its log is retained locally at `artifacts/verification/local-all-final.log`; that message alone does not establish the throw site. Five subsequent fresh full-suite runs with exception tracing passed and recorded no null-reference stack (`artifacts/verification/exception-sample-{1..5}.log`); those passes do not repair the race. On 2026-09-13, `verify.sh all` at `d28222e` plus uncommitted record edits failed `LargeVaultListingTests.InitializeAsync` with the stack `Vault.Create` → `PwDatabase..ctor` → `KdfPool.GetDefaultParameters` line 54 (`artifacts/verification/kdf-race-2026-09-13.log`), consistent with unsynchronized `EnsureInitialized` publishing a non-empty `g_l` before slot 0 is readable; no interleaving has reproduced it yet.
  **Build:** use the opt-in CLI exception trace in [diagnostics.md](diagnostics.md) and a fresh-process reproducer to separate concurrent registry initialization from later save work. Retain the failing stack and the smallest controlled interleaving; a warmed process does not test first use.
  **Verify (V-F.11a):** retained evidence identifies the throwing operation and the shared state that caused it, with a failing regression and a passing control. If no failure is captured, keep this row open and record the next distinguishing experiment before selecting F.11b's repair.
- [ ] **F.11b — Repair the confirmed KDF startup mechanism.** Needs: F.11a.
  **Build:** make initialization complete before concurrent callers can observe its result, at the boundary established by F.11a. Preserve algorithms, KDF parameters and KDBX compatibility; record any vendor modification in its provenance guide. Do not serialize the suite to conceal the race.
  **Verify (V-F.11b):** the first-use regression fails before the change and passes afterward; the full concurrent backend suite and both directions of KeePassXC compatibility pass. Counts and startup conditions remain explicit, and a passing retry alone cannot close this repair.

- [ ] **F.10 — Repair the doomed-save overrun.** Needs: F.10a, F.10b. — Aggregate: establish the mechanism, repair it and retain the failing-before/passing-after evidence; diagnosis alone does not close this defect.
- [ ] **F.10a — Separate save-gate waiting from the first attempt.** Needs: 2.1.
  **Measured:** [pool-probe run 34701431621](https://github.com/notinferred/keypaste/actions/runs/34701431621) varied the worker floor 256-fold; `ASaveThatCannotSucceed_GivesUpQuickly` failed 4, 4 and 2 of 80. Raising the floor left failures and possible pool involvement in the first attempt unresolved. Recorded retry sleeps totalled 2259–2482 ms against 2240 ms requested. The first interval was 3622–6275 ms in failures and included gate acquisition and the save itself; later attempt work measured 0 ms at the instrument's resolution. Gate contention and Argon2 work remain unconfirmed hypotheses.
  **Build:** instrument gate acquisition and first-attempt work separately, with operation identity for overlapping saves. State the timing contract: total caller wait includes gate wait, attempt work and retry sleeps; the requested retry-delay total describes the sleep schedule, not an elapsed-time bound. Keep those measures distinct. Preflight the writer and reader, then run a distinguishing experiment under the CI suite load and retain its source, command, platform, counts and interpretation per [diagnostics.md](diagnostics.md).
  **Verify (V-F.10a):** the instrument proves it can distinguish a held gate from slow attempt work; retained observations identify the mechanism or explicitly leave this row open with the next discriminating experiment. Set F.10b's concrete repair from that result before implementing it.
- [ ] **F.10b — Repair the measured save-overrun mechanism.** Needs: F.10a.
  **Build:** implement the repair selected by F.10a while preserving atomic writes, lost-write protection and bounded failure. Use a regression that induces the named mechanism. Increasing `SaveAttempts`, `SaveRetryDelayMilliseconds` or the test timeout alone is not a repair; neither is omitting gate wait or first-attempt work from a claim about total caller wait.
  **Verify (V-F.10b):** the regression fails before the repair and passes afterward on the named platform; retain counts and separate gate, work, retry and total timings under comparable load. Existing save-safety checks pass. These results, together with F.10a, close aggregate V-F.10.

- [ ] **F.2b2 — Observe minimize-lock on macOS and Linux.** Needs: F.2b1. — **BLOCKED** on a macOS machine and a Linux desktop session (2026-09-08); run F.2b1's behavior on both remaining targets, following the [desktop checklist](desktop.md#observing-minimize-lock-on-macos-and-linux), and correct [MinimizeLock](../src/Keypaste.App/MinimizeLock.cs) if an observation contradicts it.

### Release foundations

- [ ] **3.8 — Authenticate release origin and retain provenance.** Needs: R.0b.
  **Build:** Generate build attestations for every distributable, source archive and release manifest; bind verification to this repository and its release workflow. Publish a copyable verification procedure and retain the evidence with the release; make no reproducible-build claim from attestation alone.
  **Verify (V-3.8):** The documented procedure accepts an anonymously downloaded genuine release and rejects a changed byte, wrong repository identity or unrelated workflow. Every advertised asset is covered after temporary CI artifacts expire.

### Desktop packages and platform signing

Each packaging child uses [release-targets.json](../release-targets.json) through [app.yml](../.github/workflows/app.yml), preserves the prerelease suffix and keypaste publisher, and labels unsigned candidates internal everywhere. A request for a signed package must fail closed when its identity is missing; 3.5b and 3.6b own signing. An artifact establishes Packaged only; real installation remains 4.7b and public distribution remains 4.7c.

- [ ] **4.7a — Prepare desktop installers and prerelease candidates.** Needs: 4.7a1, 4.7a2, 4.7a3. — Aggregate: all three platform candidates and the shared release-matrix checks pass before downstream work may treat packaging as complete.
- [ ] **4.7a1 — Package an internal Windows installer.** Needs: R.0a, F.4b. — Package the declared Windows payload under the shared candidate rules; retain the workflow artifact and release-matrix rejection of undeclared targets. Signing and installation remain separate gates.
- [ ] **4.7a2 — Package an internal macOS bundle and DMG.** Needs: R.0a, F.4b. — Package the declared macOS payload under the shared candidate rules; retain the workflow artifacts and release-matrix rejection of undeclared targets. Notarization and installation remain separate gates.
- [ ] **4.7a3 — Package an internal Linux AppImage.** Needs: R.0a, F.4b. — Build the declared Linux payload under the shared candidate rules; retain the workflow artifact and release-matrix rejection of undeclared targets.
- [ ] **3.5a — Enable the macOS signing identity (H-0015).** Human. Needs: none. — External Apple Developer enrollment as keypaste; repository-scoped Developer ID Application and notarization credentials; a runner identifies the certificate and signs, notarizes and staples a throwaway binary, retaining team ID and expiry. Enrollment alone does not complete 3.5b.
- [ ] **3.6a — Enable the Windows signing identity (H-0017).** Human. Needs: none. — External organization-validated certificate or managed signing identity as keypaste, repository-scoped with no key retained beyond a job; on `windows-2025`, timestamp a throwaway binary, verify publisher with `signtool verify /pa /v` and reject a changed byte; retain issuer, timestamp authority and expiry. Enrollment alone does not complete 3.6b.
- [ ] **3.5b — Sign and notarize macOS release payloads.** Needs: 3.5a, 4.7a2. — Sign, notarize and staple, and fail closed when the identity is absent.
- [ ] **3.6b — Sign Windows executables and installers.** Needs: 3.6a, 4.7a1. — Authenticode-sign and timestamp the payloads and installer, and record the real install prompts.
- [ ] **4.7b — Exercise native desktop installation candidates.** Needs: 4.7a, 4.9, E.1. — Install each candidate and complete first render, vault creation, editing, env run and approval on every supported target.
- [ ] **4.7d — Preserve user data through upgrade, uninstall and recovery.** Needs: 4.7a. — Prove an upgrade keeps vaults, history and settings, and that uninstall leaves user vaults alone.
- [ ] **4.7c — Publish and verify signed desktop downloads.** Needs: 3.5b, 3.6b, 4.7a3. Ships after: 3.8, 4.7b, 4.7d, F.2b2. — Put the checked desktop candidates at permanent public URLs and re-run the checks against the public bytes.

### Browser publication

- [ ] **8.4a — Prepare browser store identities and distributables.** Needs: R.0a, 8.1. — Chrome and Firefox publisher accounts, stable extension identities bound to the native-host allowlist, and each store's package; external accounts required.
- [ ] **8.4b — Publish and test browser store installations.** Needs: 8.4a. Ships after: 4.7c, 8.3a, 8.3b, 8.3c, 8.3d, 8.3e. — Submit, get approved, then install from the live public listing on every promised browser and platform.

### Repository release protections

- [ ] **K.4 — Require release-relevant checks on main.** Needs: 3.0. — Branch protection so a failing required check cannot be merged through the normal route.
- [ ] **K.5 — Observe fork pull-request CI.** Needs: 3.0. — Run a fork pull request and confirm it builds without reaching release secrets.

### Define coverage before claiming a complete password manager

- [ ] **P.0 — Enumerate the complete versioned parity contract.** Needs: 0.2. — Audit tagged KeePassXC behaviour by behaviour and give each a disposition, platform scope and runnable acceptance case.

### Create and safely edit a vault

- [ ] **4.8 — Create a vault from the desktop.** Needs: 0.2, 4.1. — First-run Create/Open choice so a fresh install makes a working vault without a terminal.
- [ ] **4.9 — Enter and edit an existing secret.** Needs: 4.2. — Type or paste a real login or API value into the app and have it survive save, reopen and the CLI. Fix the [URL/notes display defect](ui-review.md#window-and-text-behavior); a regression must preserve ordinary punctuation and line breaks while rejecting deceptive controls.
- [ ] **V.1a — Support keyfiles in core and CLI unlock.** Needs: 0.2. — Supported KDBX keyfile forms for create, open and change-credentials.
- [ ] **V.1b — Expose vault credentials in the app.** Needs: V.1a, 4.8. — Choose a keyfile and change the master password from the desktop, with the loss consequences stated.
- [ ] **V.2a — Read and restore entry history in core.** Needs: 0.2. — Bounded history metadata and restoration that records the value it replaced.
- [ ] **V.2b — Restore an entry from the desktop.** Needs: V.2a, 4.2. — See previous revisions and put one back, with secrets masked until revealed.
- [ ] **V.3a — Implement reversible deletion.** Needs: 0.2. — Soft delete and restore through the KeePass recycle bin, with permanent delete kept separate.
- [ ] **V.3b — Add trash and recovery controls.** Needs: V.3a, 4.2. — Recover an accidental deletion without a terminal.
- [ ] **V.4a — Keep recoverable encrypted backups.** Needs: 0.2. — Versioned encrypted backups around each save, so an interrupted write cannot destroy the last good copy.
- [ ] **V.4b — Restore and export a complete vault.** Needs: V.4a, 4.8. — Restore a backup or export the whole vault, with the replaced file kept until it succeeds.

### Organize and migrate everyday credentials

- [ ] **V.5a — Add stable organization operations.** Needs: 0.2. — Group and entry create, rename, move, clone, tag and expiry through the core, with UUIDs stable across moves.
- [ ] **V.5b — Add organization and richer search to the app.** Needs: V.5a, 4.2. — Field, tag and expiry filters, and controls for the new core operations, cleared on lock.
- [ ] **V.6 — Generate passphrases.** Needs: 4.2. — Word-list passphrase generation beside the existing character generator.
- [ ] **V.7 — Manage custom fields.** Needs: 4.9. — Add, edit and remove named custom fields and keep their protected flags.
- [ ] **V.8a — Expose bounded attachment operations.** Needs: 0.2. — Enumerate, import, remove and read attachments in memory, with size limits and no plaintext temporary files.
- [ ] **V.8b — Manage attachments from the app.** Needs: V.8a, 4.9. — Attachment controls that never auto-open or execute a file.
- [ ] **V.9 — Report local password health.** Needs: V.5a, 4.2. — Find weak, reused and expired credentials locally, with no network request.
- [ ] **9.1a — Define a loss-aware import pipeline.** Needs: V.8a. — One import contract with preview, collision decisions and atomic commit, so nothing is dropped silently.
- [ ] **9.1b — Import Bitwarden JSON.** Needs: 9.1a. — The supported Bitwarden export forms as one adapter.
- [ ] **9.1c — Import LastPass CSV.** Needs: 9.1a. — The LastPass CSV adapter, including quoted multiline fields and folders.
- [ ] **9.1d1 — Import 1Password 1PUX.** Needs: 9.1a. — The 1PUX adapter, preserving supported attachments and TOTP.
- [ ] **9.1d2 — Import 1Password CSV.** Needs: 9.1a. — The documented 1Password CSV subset.
- [ ] **9.1e — Import KeePassXC CSV and open existing KDBX.** Needs: 9.1a. — CSV mapping plus onboarding an existing KDBX without touching the original.
- [ ] **9.1f — Guide import in the desktop.** Needs: 9.1a, 4.8. — Source selection, preview, conflict choices and a result report, so a new user migrates without a terminal.
- [ ] **9.2a — Implement interoperable TOTP.** Needs: 0.2. — Read and calculate KeePassXC otp attributes, keeping the seed protected.
- [ ] **9.2b — Use TOTP in the app and approval bridge.** Needs: 9.2a, 4.2, 2.2. — Show the code and countdown, and let an agent be approved for an otp field but never the seed.

### Recover concurrent changes

- [ ] **1.4a — Specify merge and deletion semantics.** Needs: 0.2. — Write down what happens to divergent edits, tombstones and moves before any code merges a vault.
- [ ] **1.4b — Implement atomic entry-level merge.** Needs: 1.4a. — The agreed merge with a no-write preview, so an older value can never overwrite a newer one.
- [ ] **1.4c — Resolve merges through CLI and GUI.** Needs: 1.4b, 4.2. — Inspect and resolve a named conflict without losing either revision.

### Native approvals and developer workflows

- [ ] **4.5 — Define and measure the daily-use tasks.** Needs: 4.2. — `docs/ux.md` with numeric thresholds for create, find, copy, restore, inject, approve, deny and fill.
- [ ] **8.2a — Specify the shared approval interaction.** Needs: 2.2. — One specification for what every approval surface shows, including how untrusted reason text is rendered.
- [ ] **4.3a — Add the authenticated desktop approval channel.** Needs: 2.2, 8.2a. — Let the desktop answer a request over an authenticated local channel, with the agent still the authority.
- [ ] **4.4 — Render native approval and denial.** Needs: 4.3a, 8.2a. — A native prompt with terminal fallback, where default, timeout and dismissal all deny.
- [ ] **4.3b — Show live agent activity and effective controls.** Needs: 4.3a. — Pending requests, audit history, live counts and per-client pause; an absent agent reads unavailable, not zero.
- [ ] **4.4b — Start and stop the approver from a user action.** Needs: 4.4. — Run one approval end to end without a terminal, with unlock input owned by the approver process.
- [ ] **E.1 — Finish the desktop environment workflow.** Needs: 4.2. — Map a project to an env set and run it from the app, with no plaintext file written.

### Browser integration

- [ ] **8.1 — Install and pair the native messaging host.** Needs: 2.2. — A vault-free native host over the local approver, paired to specific extension identities.
- [ ] **8.3a — Fill a login with verified origin matching.** Needs: 8.1. — Fill only on the real registrable domain, and refuse lookalikes and cross-origin frames.
- [ ] **8.3b — Save a new browser credential.** Needs: 8.3a. — Explicit save preview, so page content cannot write to the vault by itself.
- [ ] **8.3c — Update an existing browser credential.** Needs: 8.3b. — Update the right entry on a password change and keep the old value in history.
- [ ] **8.3d — Generate a browser credential.** Needs: 8.3b. — The core generator in the extension, saved only after confirmation.
- [ ] **8.3e — Fill supported TOTP and custom fields.** Needs: 8.3a, 9.2a. — Fill OTP codes and mapped extra fields without exposing a seed to the page.
- [ ] **8.2b — Verify approval consistency across surfaces.** Needs: 4.3b, 4.4, 8.3a. — Run the same hostile approval fixtures on terminal, native prompt, activity view and browser.

### Product acceptance

- [ ] **4.6 — Exercise actual desktop rendering.** Needs: F.2d. — Headless Skia render tests that go red if a typed character ever appears on screen or in the automation tree.
- [ ] **9.4 — Publish a versioned compatibility result.** Needs: V.1a, V.8a, 9.1a, 9.2a, 1.4b. Ships after: V.1b, V.7, V.8b, 9.1f, 9.2b, 1.4c. — Extend both KeePassXC gate directions over the finished workflows and record the upstream version tested.
- [ ] **3.10a — Ship the local product guides.** Needs: 9.1f, 9.2b, 8.3c, 8.3d, 8.3e, E.1, V.4b. — Version-correct guides for every advertised screen, linked from the app.
- [ ] **1.5a — Observe Windows clipboard history behavior.** Needs: 1.5b. — Optional: prove on a real machine that a keypaste secret never reaches clipboard history; a named residual, not a blocker.
- [ ] **R.1 — Verify the working password manager.** Needs: R.0c, F.6, F.2b2, P.0, 4.7c, 4.7d, 8.4b, K.4, K.5, 4.8, 4.9, V.1b, V.2b, V.3b, V.4b, V.5b, V.6, V.7, V.8b, V.9, 9.1b, 9.1c, 9.1d1, 9.1d2, 9.1e, 9.1f, 9.2b, 8.3c, 8.3d, 8.3e, 1.4c, 4.4b, 4.3b, E.1, 8.2b, 4.5, 4.6, 9.4, 3.10a. — **Milestone gate:** a fresh user does the whole daily journey on public downloads without terminal help.

## Pilot ready

The first managed pilot uses the published desktop app and extension. It must hide routine file management, preserve offline use and provide real device, recovery and service operations. Preparation can start earlier, but no external pilot begins before R.1 and the controls below pass.

### Account, device and relay foundations

- [ ] **H.1 — Settle hosted authentication and key boundaries.** Needs: none. — Write down account auth, device authorization, vault unlock, recovery and revocation as separate protocols, checked against PRODUCT §3.
- [ ] **5.2a — Build the relay executable and storage contract.** Needs: none. — `Keypaste.Relay` as one NativeAOT binary over SQLite and S3-compatible object storage, with no vault-decryption dependency.
- [ ] **H.2 — Implement account registration and authentication.** Needs: H.1, 5.2a. — Signup, login and account-password reset that never touch vault-unlock material.
- [ ] **H.3 — Implement account MFA and session revocation.** Needs: H.2. — An MFA factor with recovery codes, plus a session list and revoke-all.
- [ ] **H.5a — Define recoverable and unrecoverable account failures.** Needs: H.1. — Decide what a lost password, MFA, device or vault secret actually costs, and say what support cannot do.
- [ ] **H.4 — Implement trusted-device authorization.** Needs: H.2, H.5a. — Per-device service keys, explicit first-device bootstrap and revocation that stops the next fetch.
- [ ] **5.2b — Implement authorized blob sync and version retention.** Needs: H.4. — Bounded upload, compare-and-swap publication and retained encrypted versions, with no entry names in metadata or logs.
- [ ] **5.2c — Publish and exercise the self-hosted relay.** Needs: 5.2b. — The same binary an operator can install and upgrade without a Stripe account.

### Managed client experience and recovery

- [ ] **5.3a — Implement recoverable client synchronization.** Needs: 1.4b, 5.2b. — `keypaste sync`: pull, merge, push, and stay recoverable if killed at any write boundary.
- [ ] **5.3b — Build managed desktop onboarding.** Needs: H.4, 5.3a, 4.8. — Sign up and get a synced vault without choosing a file path, with local-only use still working offline.
- [ ] **5.3c — Expose device, sync and conflict controls.** Needs: 5.3a, H.3, H.4. — Device list, second-device enrollment, revoke, sync state and version restore in the app.
- [ ] **H.5b — Implement and rehearse the approved recovery flow.** Needs: H.5a, H.4. — Build only the approved recovery mechanism, and test each documented unrecoverable case.
- [ ] **H.6 — Implement account export and deletion.** Needs: 5.2b. — Export encrypted vaults and delete an account on a published schedule, leaving local files intact.

### Service operation and pilot gate

- [ ] **H.7 — Complete hosting owner enrollment (H-0019).** Human. Needs: none. — Obtain the relay host, bucket, `sync.keypaste.com` and mail account; external accounts required.
- [ ] **H.8 — Deploy the managed pilot service.** Needs: 5.2b, H.7. Ships after: H.5b, H.6. — Deploy the reviewed relay build behind TLS with backups, rollback and recorded deployment commands.
- [ ] **H.9 — Prove durability, outage handling and operational alerts.** Needs: H.8. — Restore from backup into a fresh deployment inside the declared objectives, with content-free monitoring.
- [ ] **H.10 — Establish support, privacy and incident procedures.** Human. Needs: none. — A real support channel, published terms and a rehearsed incident notice before any external user is invited.
- [ ] **5.3d — Publish the hosted-capable clients.** Needs: 5.3b, 5.3c, H.5b, H.6, H.8. Ships after: R.1. — Ship signed CLI and desktop packages containing onboarding, sync, recovery and exit, verified against the deployed relay.
- [ ] **5.7 — Publish usable sync, recovery and operator instructions.** Needs: 5.2c, 5.3c, H.5b, H.6, H.9. — `docs/sync.md` and `docs/relay.md`, plus THREATS updated for the relay boundary.
- [ ] **R.2 — Verify managed pilot entry and close the beta milestone.** Needs: R.1, H.3, H.4, H.5b, H.6, H.9, H.10, 5.3d, 5.7. — **Milestone gate:** invited nontechnical users onboard, sync two devices, drill recovery and export, with no data-loss or isolation defect left.

### Optional local unlock convenience on supported desktop systems

- [ ] **4.10a — Add Windows quick unlock.** Needs: V.1b. — Optional: resume a local session with Windows authentication, keeping the full unlock path intact. Required later by P.9.
- [ ] **4.10b — Add macOS quick unlock.** Needs: V.1b. — Optional: the macOS equivalent under the same session policy. Required later by P.9.

## Paid release

Sell hosted convenience only after the pilot works. Local functions, security, signatures and self-hosted operation stay available without a subscription. The consumer offer includes a supported phone workflow; a desktop beta does not establish that claim.

- [ ] **5.5a — Complete payment owner enrollment (H-0018).** Human. Needs: none. — Activate Stripe and supply scoped credentials; external account required.
- [ ] **5.5b — Implement hosted subscription entitlement.** Needs: 5.5a, H.2. — Checkout, verified idempotent webhooks and relay-side entitlement, with local functionality independent of payment state.
- [ ] **5.5c — Implement cancellation and failed-payment behavior.** Needs: 5.5b, H.6. — Plan state, grace dates and a working export route before any hosted data is deleted.
- [ ] **5.6 — Implement consented list confirmation.** Needs: H.7. — Double opt-in and unsubscribe for the signup list, kept separate from account and security mail.
- [ ] **M.1 — Select and prove the supported phone approach.** Needs: none. — Prototype on real iOS and Android devices and decide the promised phone route.
- [ ] **M.2a — Connect and lock the selected phone client.** Needs: M.1, H.4. — Enroll a real phone and open the vault locally, with revocation that works.
- [ ] **M.2b — Add phone login use and autofill.** Needs: M.2a. — Fill and save a login on each promised phone OS.
- [ ] **M.2c — Add phone sync, recovery and export controls.** Needs: M.2a, 5.3a, H.5b. — Converge phone and desktop after offline edits, and restore or export from the phone.
- [ ] **M.3 — Publish and verify phone availability.** Needs: M.2b, M.2c. Ships after: R.2. — Publish through the advertised public channel and verify a new user can install and upgrade.
- [ ] **10.2 — Complete independent hosted and client-boundary review.** Human. Needs: 5.3a, H.5b, M.2c, 5.5c. — An external review of relay, recovery, isolation, sync, bridge and mobile; unresolved critical findings block paid release.
- [ ] **5.8 — Prepare reviewed plans and controlled checkout.** Needs: 5.5c. — Owner-approved prices tied to real entitlements, with live checkout restricted until R.3.

### Community evidence and public communication

- [ ] **3.2 — Publish the initial community introduction (H-0006).** Human. Needs: none. Ships after: R.1. — Refresh [launch.md](../launch.md) for the product actually released, then post once per sanctioned channel.
- [ ] **3.3 — Answer the community introduction's feedback (H-0007).** Human. Needs: 3.2. — Closes when every issue and comment received on the sanctioned channels since 3.2 is classified as bug, known gap, documentation misunderstanding or design disagreement and answered, security reports have moved to `security@keypaste.com`, misleading documentation is corrected and bugs and gaps are recorded in STEPS or the Ideas table.
- [ ] **3.10b — Ship hosted and account-lifecycle guides.** Needs: 5.3c, H.5b, H.6, 5.5c, M.2c. — Help for signup, phone access, recovery, support, cancellation, export and deletion.
- [ ] **R.3 — Verify the paid consumer release.** Needs: R.2, H.10, 5.5c, 5.6, M.3, 10.2, 5.8, 3.3, 3.10b. — **Milestone gate:** independent nontechnical users complete onboarding through payment, cancellation and export on public distributions.

## Expansion

Each step follows its own Needs; publication follows its Ships after gates. Completing the consumer gate does not claim these features are available.

### Complete the KeePassXC behavior baseline

Accepted scope that can run alongside hosted work. P.0 prepares the behavior contract in Working proposition; P.9 forbids a parity claim while any promised behavior is unverified.

- [ ] **P.1 — Support hardware-key challenge response.** Needs: P.0, V.1b. — Unlock with a supported hardware key, kept separate from online account MFA.
- [ ] **P.2 — Support multiple vaults and auto-open.** Needs: P.0, V.1b, 4.8. — Several vaults open at once with separate locks and approval scopes.
- [ ] **P.3a — Complete advanced entry and database metadata.** Needs: P.0, V.5b, V.7. — The remaining icon, timestamp, expiry, tag and database-setting controls, preserving unknown metadata.
- [ ] **P.3b — Implement field references and placeholders.** Needs: P.0, V.7, P.2. — Reference and placeholder semantics with bounded recursion, where entry text cannot become a command.
- [ ] **8.5a — Implement passkey creation through the browser bridge.** Needs: P.0, 8.3a. — Origin-bound WebAuthn credential creation through the approved core boundary.
- [ ] **8.5b — Authenticate, manage and publish passkeys.** Needs: 8.5a. Ships after: 8.4b. — Passkey authentication, management and backup, shipped in public builds.
- [ ] **P.4a — Implement desktop Auto-Type on Windows and macOS.** Needs: P.0, P.3b. — User-triggered window matching and sequence typing, separate from browser autofill.
- [ ] **P.4b — Implement the supported Linux Auto-Type path.** Needs: P.0, P.3b. — The supported Linux display-server path, without advertising Wayland from an X11 result.
- [ ] **9.3a — Implement vault-backed SSH signing.** Needs: P.0, V.8a. — A local SSH agent that signs from vault attachments and never exports key bytes.
- [ ] **9.3b — Expose SSH key selection and lifecycle.** Needs: 9.3a, V.8b. — Choose, load and unload keys from the CLI and desktop, with lock removing access.
- [ ] **P.5 — Integrate Linux Secret Service.** Needs: P.0, 4.4b. — A scoped Secret Service implementation over D-Bus, with per-collection exposure.
- [ ] **P.6 — Implement KeeShare-compatible exchange.** Needs: P.0, V.1a, 1.4b. — Upstream-compatible sharing, only if its key transfer survives review against §3.
- [ ] **P.7a — Complete cipher and KDF configuration coverage.** Needs: P.0, V.1b. — The supported KDBX cipher and KDF settings, with safe migration previews.
- [ ] **P.7b — Cover remaining legacy format/import behavior.** Needs: P.0, 9.1f, P.7a. — The remaining KDBX versions and legacy import forms, reporting what cannot convert.
- [ ] **P.8 — Add privacy-reviewed compromised-password checking.** Needs: P.0, V.9. — Breach checking that sends no entry name or full password, and nothing at all by default.
- [ ] **P.9 — Verify complete KeePassXC coverage for the declared baseline.** Needs: R.1, P.0, P.1, P.2, P.3a, P.3b, 8.5b, P.4a, P.4b, 9.3b, P.5, P.6, P.7a, P.7b, P.8, 4.10a, 4.10b. — **Comparison gate:** every P.0 behavior has passing evidence on a public keypaste version.

### Distribution expansion

- [ ] **3.7a — Publish Homebrew installation and updates.** Needs: R.0b. Ships after: R.1. — CLI formula and desktop cask generated from completed release manifests.
- [ ] **3.7b — Publish Scoop installation and updates.** Needs: R.0b. Ships after: R.1. — A Scoop bucket entry for the Windows payloads.
- [ ] **3.7c — Publish winget installation and updates.** Needs: R.0b. Ships after: R.1. — winget manifests with stable publisher identity; closes O-0011 with its siblings.
- [ ] **3.9a — Add macOS Intel downloads.** Needs: 3.5b. Ships after: R.1. — Native `osx-x64` build, package, signing and install evidence.
- [ ] **3.9b — Add Windows ARM64 downloads.** Needs: 4.7a1. Ships after: R.1. — Native `win-arm64` build and packages; emulation does not count.
- [ ] **3.9c — Add Linux musl CLI/MCP downloads.** Needs: R.0a. Ships after: R.1. — A `linux-musl-x64` toolchain and install proof on a clean musl distribution.
- [ ] **3.9d — Add Linux ARM64 desktop downloads.** Needs: 4.7a3. Ships after: R.1. — The declared but unbuilt desktop RID, rendered and installed on a real machine.

### Web vault and secure sharing

- [ ] **W.1 — Prove the browser vault architecture.** Needs: 0.2, H.1. — Prototype a shared-core or WASM route and record what a compromised delivery origin can reach; blocks web delivery until answered.
- [ ] **W.2a — Implement reviewed browser onboarding and unlocking.** Needs: W.1, H.2. — Create or import and unlock in a browser, with no unencrypted vault data in storage.
- [ ] **W.2b — Implement browser editing and sync recovery.** Needs: W.2a, 5.3a, H.6. — Edit, sync, resolve conflicts and export from the browser through the approved core.
- [ ] **W.2c — Review and publish the web vault.** Needs: W.2b. Ships after: R.3. — Independent review of the web-origin boundary, then deploy the exact reviewed version.
- [ ] **5.4a — Review and implement scoped share-bundle creation.** Needs: V.1a. — A real KDBX containing only the chosen entries with its own keyfile, if the transfer conforms to §3.
- [ ] **5.4b — Publish one-download sharing and quarantine import.** Needs: 5.4a, 5.2b. Ships after: R.2. — One-download expiring bundles, imported into quarantine before anything can use them.

### Teams and enterprise credentials

R.4 needs an authorized pilot organization and scope, selected on the Human track; the code rows below do not wait for it.

- [ ] **7.1a — Settle organization ownership and sharing authority.** Needs: H.1. — Decide who owns, who can decrypt and who can recover, reviewed against §3 before any key-envelope design.
- [ ] **7.1b — Implement organization membership and permissions.** Needs: 7.1a. — Invitations, roles, collections and service accounts, default-deny, with opaque resource IDs in events.
- [ ] **7.1c — Implement reviewed shared-vault access and offboarding.** Needs: 7.1b. — Rotate access for future snapshots when a member leaves, and say plainly that retained old copies stay readable.
- [ ] **7.1d — Build organization credential administration.** Needs: 7.1c. — Organization views, invitations, roles, ownership transfer and offboarding status in the desktop.
- [ ] **7.2 — Implement attributed team approval and broker access.** Needs: 7.1c. — Approvals attributed to an identified member or service account, revoked at the next request.
- [ ] **7.3a — Add organization OIDC sign-in.** Needs: 7.1b. — One IdP through established OIDC components; SSO authenticates the account and never unlocks the vault.
- [ ] **7.3b — Add provisioning and deprovisioning.** Needs: 7.3a, 7.1c, 7.2. — A SCIM connector where a departure actually revokes sessions, devices and broker access.
- [ ] **6.1 — Prove external delegation visibility and revocation.** Needs: none. — A feasibility check of what GitHub and Google grants can really be seen and revoked.
- [ ] **6.2 — Add the supported delegation views.** Needs: 6.1. — Show only the proved provider integrations, with staleness labelled and offline never shown as zero.
- [ ] **7.4 — Build team access reviews and delegation dashboard.** Needs: 7.1d, 7.2, 7.3b. — Membership, rights and broker policy in one reviewable place, with unknown access left visible.
- [ ] **R.4 — Verify a real organization pilot.** Needs: R.2, 7.1d, 7.2, 7.3b, 7.4. — **Milestone gate:** an authorized organization onboards, shares, reviews access and offboards, independently assessed.

### Release announcements after the product gates

- [ ] **3.11 — Publish the app and hosted-release announcement.** Human. Needs: 3.10b. Ships after: R.3. — Version-correct announcement built on retained R.1/R.3 evidence.

## Scale

These steps carry build Needs like any other; their place after the release and organization gates is priority, not dependency. None of these postpones a control already required for a pilot, consumer release or organization pilot.

- [ ] **S.1 — Expand service capacity from measurements.** Needs: H.8. — Fix one bottleneck that a load measurement against the declared budget demonstrates.
- [ ] **S.2 — Support managed desktop fleet deployment.** Needs: 4.7a. — Package for one named OS and management system.
- [ ] **S.3 — Integrate one downstream credential lifecycle.** Needs: 7.2. — One reviewed rotation or temporary-credential integration for a named provider.
- [ ] **S.4 — Expand support and incident capacity.** Human. Needs: H.10. — A bounded on-call workflow extending the staffed process.
- [ ] **S.5 — Produce requested enterprise assurance evidence.** Human. Needs: 10.2. — Map implemented controls to a specific procurement request; preparing a questionnaire is not a certification.

## Completion and ID continuity

A finished step has its artifact, a passing verifier and an evidence link naming the tested version. Record source, package, public and installation evidence separately: tests of compiled code do not establish public installation or live service operation, and a documentation edit completes nothing. A gate's transitive Needs must all pass, and further findings in its acceptance journey keep it open. The five transcript pages named in [CLAUDE.md](../CLAUDE.md) must pass `verify-demo.sh` when affected.

Keep every ID traceable. These formerly large rows now select their first ready child when requested by their old ID:

| Previous ID | Current children / scope |
|---|---|
| 1.4 | 1.4a–c; keyfile support separated into V.1a/b |
| 3.5 | 3.5a/b |
| 3.6 | 3.6a/b |
| 3.7 | 3.7a–c |
| 3.9 | 3.9a–d |
| 3.10 | 3.10a/b |
| 4.3 | 4.3a/b |
| 4.7 | 4.7a–d |
| 4.7a | Aggregate of Windows 4.7a1, macOS 4.7a2 and Linux 4.7a3; downstream Needs still require all three |
| 5.2 | 5.2a–c |
| 5.3 | 5.3a–d |
| 5.4 | 5.4a/b |
| 5.5 | 5.5a–c |
| 7.1 | 7.1a–d |
| 7.3 | 7.3a/b |
| 8.2 | 8.2a/b |
| 8.3 | 8.3a–e; initial read-only scope grows through explicit save/update children |
| 9.1 | 9.1a–f; 9.1d splits into 9.1d1/d2 |
| 9.2 | 9.2a/b |
| 9.3 | 9.3a/b |
| 10.2 | Independent review before paid release; its earlier Scale placement no longer defers it |
| F.2b | F.2b1/F.2b2; the wiring closed with F.2b1, the remaining per-OS observation is F.2b2 |
| F.10 | Aggregate of diagnosis F.10a and repair F.10b; remains open until both verifiers pass |
| F.11 | Aggregate of startup diagnosis F.11a and repair F.11b; promoted from the recorded KDF failure idea |

An aggregate ID selects its first ready child while retaining the parent's completion gate. Other IDs that still name explicit rows select those rows: historical 3.2b is the completed essay, not a child of the future announcement. H-0015/H-0017 are signing enrollment, H-0018 payment, H-0019 hosting, H-0011 the site's pre-deploy procedure, and H-0006/H-0007 public communication. Prior decisions H-0002/H-0004/H-0008/H-0009/H-0012/H-0013/H-0014 remain answered in DECISIONS; H-0010 was never issued. The preceding roadmap is retained in git; D-0090 supersedes its MVP/Launch/Scale pricing-tier ordering.
