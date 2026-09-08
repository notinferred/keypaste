# STEPS — build plan

> Owns current status, build order and acceptance criteria. [PRODUCT](PRODUCT.md) owns the product;
> [FEATURES](FEATURES.md) owns the dated capability baseline; [RELEASE](RELEASE.md) owns distribution
> requirements. [DECISIONS](../DECISIONS.md) D-0090 records this realignment.

## Current status

**Working proposition: incomplete. Hosted pilot: not ready. Paid release: not ready.**
The local vault, CLI/env workflow and approval bridge are implemented, with known defects in
data preservation, authorization timing and release checks. The desktop is a source build with
partial entry/env screens, identified in the 2026-09-07 code review; the clipboard lifetime that
review found is repaired (F.2c). Saved preferences now reach a fresh session (F.2a) and minimizing enforces
the setting that names it (F.2b1), observed on Windows only. An approval's lifetime no longer moves
when the clock does (F.3a), a second agent request no longer queues in front of the check that
was supposed to refuse it (F.3b), and a vault with more names than one reply can carry is listed and
bounded rather than dropping the connection and its grants (F.3c). The remaining F.2–F.4 repair tasks
below are open even though existing tests pass.
Everyday password-management workflows, browser integration and desktop
publication remain unfinished. No hosted service, web vault, mobile integration or organization
credential service is available.

**Next: F.3d — refuse a release that cannot be delivered, and record it as one.** F.2b2 is BLOCKED on
hardware this project does not have; it is deferred to the desktop candidate run, and 4.7b lists
it, so the desktop gate still cannot pass without it. F.4b's change is in source and checked on
the four targets this machine can build; its three remaining NativeAOT targets need one CI run,
because NativeAOT does not cross-compile. Complete the ready repairs below before new feature
work. Release foundations follow: R.0a makes release identity and supported
targets executable, R.0b/3.8 make distribution verifiable, and 4.7a prepares desktop packages without
waiting for signing enrollment. A new CLI/MCP patch requires the core/CLI/bridge and release repairs;
desktop release checks additionally require the desktop repairs.
The first new user workflow is **4.8 — create a vault in the app**, followed by **4.9 — enter and
edit an existing password or API value**. Dependencies below determine what can actually start.

The [current release matrix](RELEASE.md#current-distribution--2026-09-07) owns public availability.
A completed source task does not mean that it is present in the current download.

| Step | Status | Completed scope / evidence |
|---|---|---|
| 0.1 | Complete | Shared core, locked dependencies and solution scaffold; [build properties](../Directory.Build.props) |
| 0.2 | Complete | Basic bidirectional KDBX compatibility; [round-trip tests](../tests/Keypaste.Core.Tests/VaultRoundTripTests.cs) and [KeePassXC gate](../scripts/verify-keepassxc-compat.sh) |
| 0.3 | Complete | Core CLI vault verbs; [CLI commands](../src/Keypaste.Cli/Commands) |
| 1.1 | Complete | Env grouping and storage; [EnvStore tests](../tests/Keypaste.Core.Tests/EnvStoreTests.cs) |
| 1.2 | Complete | Env import/injection; [injection gate](../scripts/verify-run-injection.sh) |
| 1.3 | Complete | Explicit dotenv export; [export tests](../tests/Keypaste.Cli.Tests/EnvExportTests.cs) |
| 1.5b | Complete | Documented clipboard controls and residuals; [SECURITY](../SECURITY.md) |
| 2.1 | Complete | MCP transport and schemas; [stdio gate](../scripts/verify-mcp-stdio.sh) |
| 2.2 | Complete | Human approval, scoped cached grants; [approval gate](../scripts/verify-approval-e2e.sh) |
| 2.3 | Complete | Local policy pre-approvals; [policy gate](../scripts/verify-policy-e2e.sh) |
| 2.4 | Complete | Local hash-chained audit; [log-chain gate](../scripts/verify-log-chain.sh) |
| 2.4a | Complete | Reporting and threat model; [SECURITY](../SECURITY.md), [THREATS](../THREATS.md) |
| 2.4b | Complete | External contact delivery recorded in prior step 2.4b; [reporting channel](../SECURITY.md) |
| 2.5 | Complete | CLI/MCP demo; [verified transcript](demo.md) |
| 2.6 | Complete in source | Client setup command, newer than v0.1.0; [CHANGELOG](../CHANGELOG.md#unreleased) |
| 3.0 | Complete | Public repository; D-0089 in [DECISIONS](../DECISIONS.md) |
| 3.1 | Complete | Existing demo GIF; [asset](demo/keypaste-demo.gif), D-0088 |
| 3.2b | Complete | CLI launch essay; [essay](keepass-and-agents.md); distinct from future post task 3.2 |
| 3.4 | Published | CLI/MCP pipeline and public archives; [release inventory](RELEASE.md) |
| 4.1 | Complete in source | Desktop unlock and idle lock; [session tests](../tests/Keypaste.App.Tests/Session/AppVaultSessionTests.cs) |
| 4.2 | Complete in source | Basic search, generated entries, env screens and lost-write protection; [entry tests](../tests/Keypaste.App.Tests/ViewModels/EntriesViewModelTests.cs) |
| F.1a | Complete | One entry identity for selection and mutation; an ambiguous one is refused; [identity tests](../tests/Keypaste.Core.Tests/EntryIdentityTests.cs), [write-back gate](../scripts/verify-keepassxc-writeback.sh) D; D-0091 |
| F.1b | Complete | Export refuses its source vault and any KDBX destination; [export tests](../tests/Keypaste.Cli.Tests/EnvExportTests.cs), [path rule tests](../tests/Keypaste.Core.Tests/PathIdentityTests.cs); D-0092 |
| F.1c | Complete | Cleanup is bound to the imported bytes and the path they came from; [pull tests](../tests/Keypaste.Cli.Tests/EnvPullTests.cs), [snapshot tests](../tests/Keypaste.Core.Tests/SourceSnapshotTests.cs); D-0093 |
| F.1e | Complete | One resolver for reads and removals; an ambiguous path is refused, not served; [identity tests](../tests/Keypaste.Core.Tests/EntryIdentityTests.cs), [verb tests](../tests/Keypaste.Cli.Tests/VerbTests.cs), [entry tests](../tests/Keypaste.App.Tests/ViewModels/EntriesViewModelTests.cs), [write-back gate](../scripts/verify-keepassxc-writeback.sh) D; D-0094 |
| F.2a | Complete | Launch arms the session and the palette from `app.toml`; [startup tests](../tests/Keypaste.App.Tests/StartupSettingsTests.cs), [settings tests](../tests/Keypaste.App.Tests/ViewModels/SettingsViewModelTests.cs); D-0095 |
| F.2b1 | Complete | Minimizing calls the same session lock as the timeout and `Ctrl/Cmd+L`; [minimize tests](../tests/Keypaste.App.Tests/MinimizeLockTests.cs); observed by hand on Windows 10 Pro 19045, 2026-09-08 — enabled locked, disabled did not, the clipboard cleared, `app.toml` unchanged; D-0096 |
| F.2c | Complete | A clipboard write that lands after a lock or a quit is taken back, not installed; [in-flight tests](../tests/Keypaste.App.Tests/Clipboard/ClipboardWritesDoNotOutliveTheAppTests.cs), seven red first; D-0098 |
| F.2d | Complete | The master-password field publishes its length and never its characters; [automation tests](../tests/Keypaste.App.Tests/Controls/MaskedInputAutomationTests.cs), four substituted leaks turn them red; D-0099 |
| F.3a | Complete | Grant expiry and the denial cooldown are held on both clocks, each taking the reading that fails closed for it; [Deadline tests](../tests/Keypaste.Core.Tests/DeadlineTests.cs), [grant tests](../tests/Keypaste.Core.Tests/GrantCacheTests.cs), [gate tests](../tests/Keypaste.Core.Tests/ApprovalGateTests.cs), [handler tests](../tests/Keypaste.Core.Tests/ApproverHandlerTests.cs), five red first; D-0100 |
| F.3b | Complete | A second call on one MCP connection is refused where it arrives instead of queueing in front of the gate's check; [concurrency tests](../tests/Keypaste.Mcp.Tests/ConcurrentRequestsTests.cs) through a real MCP connection, [connection tests](../tests/Keypaste.Mcp.Tests/ApproverConnectionTests.cs), [client tests](../tests/Keypaste.Core.Tests/ApproverClientTests.cs), five red first with "queued behind the prompt"; D-0101 |
| F.3c | Complete | One listing reply is bounded by encoded bytes rather than by an entry count, and a reply that left names out says so before the list and after it; [protocol tests](../tests/Keypaste.Core.Tests/ApproverProtocolTests.cs) including the reproduced 1,000-name/76,060-byte case and hostile Unicode, [framer tests](../tests/Keypaste.Core.Tests/MessageFramerTests.cs) asserting nothing the protocol encodes can be refused, [listener tests](../tests/Keypaste.Core.Tests/ApproverListenerTests.cs) for the surviving connection and its grants, [listing-size tests](../tests/Keypaste.Mcp.Tests/ListingSizeTests.cs) and [large-vault tests](../tests/Keypaste.Mcp.Tests/LargeVaultListingTests.cs) over a real vault, a real pipe and a real MCP client; twenty red first; the reproduction is the documented 1,000-name case at 75 encoded bytes each — 76,060 bytes on the wire shape that failed and 76,077 with the completeness field, against a 65,535-byte payload budget; `verify-mcp-stdio.sh` and `verify-approval-e2e.sh` re-run green; D-0102 |
| 10.1 | Complete | Initial hostile review/remediation; D-0084 in [DECISIONS](../DECISIONS.md) |
| K.1 | Complete | Pinned SDK installed; [global.json](../global.json), D-0076 |

Completed rows preserve their original bounded scope. For example, 4.2 does not include entering
an existing secret, undoing deletion or restoring history. Those gaps have their own unchecked tasks.
The F tasks record defects and missing coverage discovered after those original checks; historical
completion does not waive the new repair dependencies or certify the current implementation.

**External prerequisites:** 3.5a needs Apple enrollment (H-0015); 3.6a needs an eligible Windows
signing account (H-0017); 8.4a needs extension-store accounts; H.7 needs hosted infrastructure
(H-0019); 5.5a needs a payment account (H-0018). Only the operations that need those accounts wait.
F.2b2 needs a macOS machine and a Linux desktop session, and is deferred until 4.7a produces a
candidate archive for each of those targets. The 1.5a clipboard observation needs a suitable Windows machine; an enforced-disabled history panel
is not evidence. It is a named residual, not a reason to stop independent development.

## Build order

| Milestone | Completion or activation |
|---|---|
| Working proposition | A published desktop + browser password manager with usable env/agent workflows; R.1 verifies it |
| Pilot ready | Managed encrypted sync with account/device controls, restore and observed operation; R.2 verifies private-beta entry |
| Paid release | Nontechnical onboarding, a supported phone workflow, review, billing and support; R.3 verifies consumer release |
| Expansion | Advanced KeePassXC coverage starts after R.1; web access and organization credentials follow their listed prerequisites |
| Scale | Capacity, fleet and operational improvements activated by observed requirements |

Commercial plans are defined in PRODUCT; they are not engineering milestones. Full KeePassXC
coverage is accepted follow-on scope, with P.0 defining individual behaviors and P.9 closing the
comparison. It is not silently deferred to an ideas list. Teams has its own R.4 pilot gate.

**Pick the first unchecked task with completed prerequisites in the earliest unfinished milestone.**
Passing its numbered gate advances the active milestone: optional 1.5a does not hold Working
proposition open after R.1, and optional 4.10a/b do not hold Pilot ready open after R.2. They remain
unchecked work; quick unlock is required by the later P.9 parity gate.
If a milestone has no ready task because an unchecked prerequisite lives in another section,
select that prerequisite's first ready task before its dependent gate, even if its original
milestone has closed. External prerequisites still require their actual evidence.
A named task in a later section may be prepared when its prerequisites are complete. Preparation
cannot satisfy publication, security-review or user-validation gates. A blocked account task does
not block unrelated ready work. Record the missing external action and move to the next ready task.

Each checkbox is one bounded task. **Needs** lists prerequisite IDs; **Build** describes the change;
**Verify** describes observable acceptance and must fail if that behavior or its evidence is absent.
An optional **Status** line records blockage or partial evidence; it never softens the Verify line
above it, and a task with one is still unchecked. **Within a section, order by readiness rather than
by ID:** work that can start today first, then work waiting on an action, then BLOCKED work last, so
the first bullet is the one the pickup rule will actually choose.
Keep IDs stable. Split large tasks into lettered children; a parent request selects its first ready
child, while a dependency on a split parent means all of its children. The ID map at the end
preserves historical references. An explicit numbered gate lists everything required for its outcome.

```text
Build the next ready step in docs/STEPS.md. Follow CLAUDE.md, implement its Build line,
run its verifier and relevant checks, then record the actual result and evidence.
```

## Working proposition

A person can install the product, create or import a vault, save and use existing credentials,
recover mistakes, inject a project's env and approve or deny agent access. Build through the shared
core; desktop, CLI and browser tasks explicitly name their surfaces. No account is required.
R.1 closes the milestone only after the published product passes its complete user journey.

### Repair existing behavior — first ready work

The bounded 2026-09-07 review found these issues while the local Windows suites reported 1,169
passed and five platform-specific skips. Each task needs a regression that demonstrates its failure
before the fix and verifies the corrected behavior. Keep the shared core and mature KDBX library;
these tasks repair existing behavior and do not require a product rewrite. Record reproductions
in repository fixtures/tests, so completion does not depend on a maintainer's temporary files.

- [ ] **F.3d — Refuse a release that cannot be delivered, and record it as approved but undelivered.** Needs: 2.2, F.3c.
  **Build:** [ApproverProtocol](../src/Keypaste.Core/Ipc/ApproverProtocol.cs)'s `Encode(CredentialReply)` is unbounded exactly as the listing reply was before F.3c. `notes` is a releasable field and a KDBX note has no length limit, so a **granted** reply can pass `MessageFramer`'s 64 KiB frame: the write throws, `ApproverListener.ServeAsync` swallows it into its outermost `catch`, and the `finally` revokes that connection's grants *after* a person approved. The bridge reconnects on a fresh connection id and puts a second prompt in front of the same person, and the audit line records `denied`/`failed` — "the approver could not be asked" — for a request somebody said yes to. Make the encode total: refuse an over-size reply with a bounded frame carrying no part of the value, deliver that refusal on the connection that is still open, keep that connection's grants, and give the refusal its own audit word and a reason saying the release was authorized and could not be delivered. Truncating a credential is not an option.
  **Verify (V-F.3d):** Through the real transport, a vault entry whose releasable field exceeds one frame is approved by a person and yields a bounded valid frame the connection survives: the tool returns a refusal saying the release was authorized and could not be delivered, the audit line records that word and that reason rather than `failed` and "the approver could not be asked", no byte of the field value appears in the result, the transcript or the log, the connection's existing grants remain usable and a subsequent approved request on it succeeds, and a person is asked once rather than twice. A refusal whose own reason is over-size stays a refusal under its own method. Evidence against the encoder alone cannot pass; the reproduction runs over a real pipe against a real vault.

- [ ] **F.4a — Fail closed when checking an existing release destination.** Needs: 3.4.
  **Build:** Fix [release.yml](../.github/workflows/release.yml)'s suppressed listing errors: require a successful destination check before upload and refuse an occupied version. A denied or failed `aws s3 ls` currently becomes an empty result and reaches upload. Fix this before any new publication, independently of desktop progress.
  **Verify (V-F.4a):** A controlled publisher fixture covers empty, occupied, denied, unavailable and malformed/incomplete listing responses. Only a successfully verified empty destination reaches upload; all other cases make zero write calls and preserve existing assets. Exercise this on a dry run without production credentials.

- [ ] **F.4b — Align generated first-party publisher metadata.** Needs: 0.1, 3.0.
  **Build:** Review and align the first-party Authors/Company/Copyright outputs from [Directory.Build.props](../Directory.Build.props) with the documented project identity and accurate attribution. Current Windows output exposes a personal identity despite the project-metadata rule. Apply the chosen fields consistently to CLI/MCP/app packages and retain required third-party notices.
  **Verify (V-F.4b):** Inspect built and packaged executable metadata on each claimed target: first-party identity matches the recorded project choice and required attribution remains intact. Source-property inspection alone cannot pass; changing future metadata makes no claim to remove previously published artifacts or history.
  **Status:** Built, and checked on the four targets this machine can produce (2026-09-08). First-party binaries publish as `keypaste` and the vendored assembly as upstream; [attribute tests](../tests/Keypaste.Core.Tests/PublisherMetadata.cs) in all four front-end suites and a [packaged gate](../scripts/verify-publisher-metadata.sh) wired into `release.yml` and `app.yml`. Observed by hand on Windows 10 Pro 19045: the NativeAOT `keypaste.exe`/`keypaste-mcp.exe` and the desktop win-x64, linux-x64 and osx-arm64 packages all report CompanyName/LegalCopyright/ProductName `keypaste` where a version resource exists and carry the line where one does not, `KeePassLib.dll` reports Dominik Reichl, and restoring a personal name fails both the attribute test and the gate (D-0097). Waiting on the linux-x64, linux-arm64 and osx-arm64 CLI/MCP binaries: NativeAOT does not cross-compile, so those three need one `release.yml` dispatch, which needs a push.

- [ ] **F.2b2 — Observe minimize-lock on macOS and Linux.** Needs: F.2b1.
  **Build:** Run F.2b1's behavior on the remaining two advertised desktop targets and record what each actually did, including any window manager that does not report a minimize. Change [MinimizeLock](../src/Keypaste.App/MinimizeLock.cs)'s supported-surface answer if an observation contradicts it, rather than leaving the checkbox offered where it does nothing.
  **Verify (V-F.2b2):** Current status holds a dated macOS and a dated Linux result for the enabled, disabled and after-restart cases, each naming the OS version, session type and build. An untested target keeps its unobserved status; a passing headless suite cannot substitute. **External prerequisite:** a macOS machine and a Linux desktop session. The procedure is written down in the [desktop checklist](desktop.md#observing-minimize-lock-on-macos-and-linux) so the observation does not have to be re-derived.
  **Status:** BLOCKED — neither machine is available (2026-09-08). Run it during the 4.7a/4.7b desktop candidate work, when an archive for each target exists anyway.

### Release foundations

- [ ] **R.0a — Pin release identity and the supported platform contract.** Needs: 3.4.
  **Build:** Add one checked release definition for component, version, source commit, supported OS floor/CPU, package format and signing policy; drive workflow matrices and download documentation from it. Start with the four current CLI/MCP targets and three desktop targets in RELEASE; distinguish supported downloads from source-only routes and retain an explicit unsigned policy for CLI patches until signing is available.
  **Verify (V-R.0a):** Every advertised target has a matching package/check job; a missing target, conflicting version or unsupported OS claim fails validation. A release candidate keeps its full prerelease version across CLI, MCP, desktop, archives and changelog lookup.

- [ ] **R.0b — Publish complete, immutable component releases.** Needs: R.0a, F.4a.
  **Build:** Implement a per-component manifest containing tag, commit, file hashes, URLs and signature/provenance references; upload immutable assets, verify them anonymously at the public origin, and write completion evidence only after all required checks pass. Maintain the last verified advertised version separately; a partial upload uses a new version for recovery, and cannot promote or overwrite the failed version.
  **Verify (V-R.0b):** Interrupt an upload, corrupt one public asset and omit one target in separate fixtures: none creates a complete release or changes the advertised version. A successful run's recorded public bytes match the manifest; a repeated publication cannot replace them.

- [ ] **3.8 — Authenticate release origin and retain provenance.** Needs: R.0b.
  **Build:** Generate build attestations for every distributable, source archive and release manifest; bind verification to this repository and its release workflow. Publish a copyable verification procedure and retain the evidence with the release; make no reproducible-build claim from attestation alone.
  **Verify (V-3.8):** The documented procedure accepts an anonymously downloaded genuine release and rejects a changed byte, wrong repository identity or unrelated workflow. Every advertised asset is covered after temporary CI artifacts expire.

- [ ] **R.0c — Publish and installation-verify the next CLI/MCP patch.** Needs: R.0b, 3.8, F.1a, F.1b, F.1c, F.1e, F.3a, F.3b, F.3c, F.4b.
  **Build:** Select the next version containing the existing Unreleased work, update version-specific install/setup instructions, and run tag publication plus automatic public-download checks on all four supported native CLI targets. Exercise setup and the advertised CLI/MCP approval/env workflows without an SDK; promote the CLI channel only after retained evidence passes, with the current signing limitation disclosed where it still applies.
  **Verify (V-R.0c):** A clean machine on every promised OS/CPU follows the published instructions successfully, including setup; binaries report the selected tag and match authenticated hashes. A failed target leaves the previous CLI release advertised; unfinished desktop/browser work cannot prevent an otherwise valid CLI patch.

### Desktop packages and platform signing

- [ ] **4.7a — Prepare desktop installers and prerelease candidates.** Needs: R.0a, F.4b.
  **Build:** Turn the existing three-target app archives into the selected Windows installer, a proper macOS app bundle/DMG and Linux AppImage; include required native libraries, licenses, CLI integration and uninstall ownership. Run candidate packaging with full prerelease versions and wire signing hooks that can be prepared without account credentials; label unsigned outputs as internal candidates.
  **Verify (V-4.7a):** Native package inspections find the expected entry point, version, libraries and notices on all three targets. A candidate tag survives packaging unchanged; missing signing credentials cannot produce a package labeled publicly signed or trigger promotion.

- [ ] **3.5a — Enable the macOS signing identity (H-0015).** Needs: R.0a.
  **Build:** Complete Apple Developer enrollment and establish the release signing/notarization identity with credentials stored through the existing CI secret mechanism; record identity and access ownership without recording secret values.
  **Verify (V-3.5a):** The release identity can authenticate for the required signing/notarization operation and CI has access only through its intended release environment. An enrollment receipt alone does not pass. **External prerequisite:** founder enrollment and account access.

- [ ] **3.6a — Enable the Windows signing identity (H-0017).** Needs: R.0a.
  **Build:** Confirm current Microsoft Artifact Signing eligibility and enroll the publisher; provision the release identity and restricted CI access. If that service is unavailable, record and implement an eligible Authenticode signing route before claiming Windows publication is ready.
  **Verify (V-3.6a):** The intended CI identity can sign a test payload whose signature verifies as the expected publisher, with timestamp. Application submission or payment without a functioning signing identity does not pass. **External prerequisite:** publisher verification and account access.

- [ ] **3.5b — Sign and notarize macOS release payloads.** Needs: 3.5a, 4.7a.
  **Build:** Integrate signing for the CLI/MCP payloads and app bundle, notarization of the distributed formats, and stapling where supported into the candidate/release paths. Fail closed on an absent identity, notarization rejection or invalid delivered signature; keep the same signing treatment for free downloads.
  **Verify (V-3.5b):** Candidate downloads retain valid expected signatures and pass applicable Gatekeeper/notarization assessment after normal browser quarantine. The app opens on a clean supported Mac without disabling security controls; retain the commands and actual first-open prompts.

- [ ] **3.6b — Sign Windows executables and installers.** Needs: 3.6a, 4.7a.
  **Build:** Sign and timestamp CLI/MCP/app executable payloads and the final Windows installer through the release environment, then verify the transported files before publication. Capture actual installation prompts instead of promising that a valid signature eliminates SmartScreen reputation prompts.
  **Verify (V-3.6b):** Downloaded candidates report valid Authenticode signatures, the expected publisher and timestamp; changing a signed payload fails verification. A clean supported Windows installation succeeds and its actual prompts are retained.

- [ ] **4.7b — Exercise native desktop installation candidates.** Needs: 4.7a, 4.6, 4.8, 4.9, 4.4b, 4.3b, E.1, F.1a, F.1b, F.1c, F.1e, F.2a, F.2b1, F.2b2, F.2c, F.2d, F.3a, F.3b, F.3c.
  **Build:** Add native candidate installation checks without a development SDK: first render, vault creation/open, existing-secret editing, env execution, native credential approval and activity inspection. Test Linux on the declared compatibility floor and record manual observations for UI/platform behavior automation cannot establish.
  **Verify (V-4.7b):** Each supported OS/CPU starts the installed GUI and completes these workflows using retained fixture/evidence records. A vault-only selftest, missing native library or unobserved GUI path cannot pass the desktop installation check.

- [ ] **4.7d — Preserve user data through upgrade, uninstall and recovery.** Needs: 4.7b.
  **Build:** Provide a verified update path and an explicit uninstall/data-retention policy; test the prior published CLI and app when one exists, using a retained predecessor app candidate for the first desktop release. Exercise interrupted installation, recovery, existing client registrations, settings and vaults; retain previous installers and state data compatibility before offering application rollback.
  **Verify (V-4.7d):** Upgrade preserves fixture secrets, history, envs, configuration and integrations; interruption has a documented recoverable result; uninstall retains user vaults. An older compatible app can reopen the updated fixture, or the documented backup restore succeeds when backward compatibility is unavailable; the first-app-release predecessor limitation is recorded.

- [ ] **4.7c — Publish and verify signed desktop downloads.** Needs: R.0b, 3.8, 3.5b, 3.6b, 4.7b, 4.7d.
  **Build:** Publish the exact checked desktop candidates to permanent anonymous URLs under the immutable manifest contract; rerun publisher, native installation, GUI workflow and upgrade/recovery checks against the public bytes. Record actual package sizes and release evidence; close O-0015/O-0016 only for the fulfilled package/publication requirements, and keep daily-use promotion behind R.1.
  **Verify (V-4.7c):** Every promised desktop target is anonymously downloadable, authentic, installation-verified and linked to persistent evidence for the same tag/commit. An expiring CI archive or a missing public target cannot satisfy this row.

### Browser publication

- [ ] **8.4a — Prepare browser store identities and distributables.** Needs: R.0a, 8.1.
  **Build:** Establish Chrome Web Store and Firefox AMO publisher accounts and stable extension identities; bind those identities to the native-host allowlist. Prepare each store's actual package, permission rationale, privacy/support listing and signed/review submission path from the same extension source; retain development IDs only in the explicit development path.
  **Verify (V-8.4a):** Each store package validates with its declared identity and permissions, and the production native host rejects the development/unknown extension identity. Account access and required listing/submission materials exist. **External prerequisite:** publisher account access and any required identity verification.

- [ ] **8.4b — Publish and test browser store installations.** Needs: 8.4a, 4.7c, 8.3a, 8.3b, 8.3c, 8.3d, 8.3e.
  **Build:** Submit the completed fill/save/update integration to both stores; after approval, install from the public listing on each browser/platform combination promised in the release contract. Exercise native-host discovery, locked-vault approval, phishing refusals, credential generation/save/update, and extension/app version mismatch handling; retain release IDs and verification evidence.
  **Verify (V-8.4b):** A normal browser profile installs from each live public listing and completes the advertised flows with the published desktop app. An unpacked development extension, a pending store review or one broken promised browser/platform combination cannot pass.

### Repository release protections

- [ ] **K.4 — Require release-relevant checks on main.** Needs: 3.0.
  **Build:** Configure branch protection/rulesets for the actual CLI, app and native compatibility checks, accounting for path-filtered pushes and pull-request check names.
  **Verify (V-K.4):** A disposable pull request with a failing required check cannot be merged through the normal route; successful required checks permit the intended workflow. Record the remote rule configuration and observed result.

- [ ] **K.5 — Observe fork pull-request CI.** Needs: 3.0.
  **Build:** Run a harmless authorized fork pull request through the GitHub-hosted workflows and verify its permission boundaries; do not expose release/deployment secrets to fork code.
  **Verify (V-K.5):** The fork run completes the intended build/compatibility checks, cannot access protected credentials and does not remain queued for unavailable runners.

### Define coverage before claiming a complete password manager

- [ ] **P.0 — Enumerate the complete versioned parity contract.** Needs: 0.2, 4.2.
  **Build:** Audit the tagged KeePassXC menus, commands, settings, integrations and supported format behavior against FEATURES; assign each behavior an implementation task, platform scope and comparison fixture, including any gaps this first-pass inventory missed.
  **Verify (V-P.0):** Every inspected upstream behavior has a disposition and runnable acceptance case; omissions and unsupported platforms are visible. A list of feature-family names or an undocumented “100%” cannot pass.

### Create and safely edit a vault

- [ ] **4.8 — Create a vault from the desktop.** Needs: 0.2, 4.1.
  **Build:** Add a first-run Create/Open choice and a creation flow over the existing core, with a sensible local default location, optional file selection, confirmed master-password input and atomic save. Keep account signup optional and developer configuration out of this flow.
  **Verify (V-4.8):** A fresh install creates and reopens a vault without a terminal; cancellation or mismatched confirmation creates nothing; the CLI and real KeePassXC open the result.

- [ ] **4.9 — Enter and edit an existing secret.** Needs: 4.8, 4.2.
  **Build:** Add secure explicit-value input for entries and env variables using the existing secret-input controls and core update path. Support intentional paste and replacement with clear confirmation; preserve UUIDs, history and unrelated fields.
  **Verify (V-4.9):** A supplied login/API value survives save/reopen and is visible to the CLI; masking, accessibility and lock/cancel tests expose no value; an unrelated attachment/custom field survives the edit.

- [ ] **V.1a — Support keyfiles in core and CLI unlock.** Needs: 0.2.
  **Build:** Add supported KDBX keyfile formats through KeePassInterop for create/open/change-credentials; separate this from merge. Use the mature library and keep existing password-only vaults compatible.
  **Verify (V-V.1a):** Real KeePassXC and keypaste open fixtures with every claimed keyfile form; wrong/missing required factors fail without changing the vault; changing credentials preserves entries and history.

- [ ] **V.1b — Expose vault credentials in the app.** Needs: V.1a, 4.8.
  **Build:** Add optional keyfile selection and a change-master-password/keyfile flow over the same core. Explain that loss of either required factor can lose access; keep all factors local.
  **Verify (V-V.1b):** GUI-created composite-key vaults reopen through both front ends; cancel or failed old credentials leave the file unchanged; old credentials fail after a successful change.

- [ ] **V.2a — Read and restore entry history in core.** Needs: 0.2.
  **Build:** Expose bounded history metadata and selected-version restoration using KDBX UUID/history semantics; restoration records the replaced current value instead of losing it.
  **Verify (V-V.2a):** Restoring an earlier password preserves UUID, attachments and the previously current revision; real KeePassXC reads the resulting history; failed restore writes nothing.

- [ ] **V.2b — Restore an entry from the desktop.** Needs: V.2a, 4.9.
  **Build:** Add a history list, selected revision inspection and confirmed restore; mask secrets until explicitly revealed and clear history data on lock.
  **Verify (V-V.2b):** A user restores a previous value and can still inspect the replaced revision; lock clears titles/values; switching entries cannot restore into the wrong UUID.

- [ ] **V.3a — Implement reversible deletion.** Needs: 0.2, V.2a.
  **Build:** Use KeePass recycle-bin metadata for soft delete/restore while retaining an explicit permanent-delete operation and deletion tombstones. Keep env and agent exposure from including recycled entries.
  **Verify (V-V.3a):** Delete/restore preserves UUID/history and excludes trash from ordinary list/injection; permanent delete removes it; an unrelated write opens correctly in KeePassXC.

- [ ] **V.3b — Add trash and recovery controls.** Needs: V.3a, 4.2.
  **Build:** Show recycled entries separately in the app, with restore and separately confirmed permanent deletion. Update CLI verbs/help to distinguish the two operations.
  **Verify (V-V.3b):** An accidental deletion is recoverable without a terminal; permanent-delete cancellation is harmless; restored data is usable through both front ends.

- [ ] **V.4a — Keep recoverable encrypted backups.** Needs: 0.2.
  **Build:** Add configurable versioned encrypted backups around successful vault replacement, with bounded retention, integrity checks and failure reporting. Preserve the last good vault if save or backup preparation fails.
  **Verify (V-V.4a):** Simulated interrupted writes and full-disk failures cannot destroy the last good copy; retained files contain no plaintext sentinel; backup generations restore in real KeePassXC.

- [ ] **V.4b — Restore and export a complete vault.** Needs: V.4a, 4.8.
  **Build:** Add CLI and GUI restore-preview/confirmation and encrypted whole-vault export over the existing file format; retain the replaced file until recovery succeeds.
  **Verify (V-V.4b):** A fresh installation restores an exported/backup vault with entries, history, fields and attachments intact; corrupt backups and cancelled restores leave the live vault unchanged.

### Organize and migrate everyday credentials

- [ ] **V.5a — Add stable organization operations.** Needs: 0.2, V.3a.
  **Build:** Add group create/rename/move, entry rename/move/clone and tag/expiry metadata through the core. Keep UUIDs stable for moves, fresh for clones, and make env/exposure changes explicit.
  **Verify (V-V.5a):** Duplicate titles do not misdirect edits; moves preserve history; moving into/out of env groups changes eligibility as documented; group cycles and invalid names fail without writes.

- [ ] **V.5b — Add organization and richer search to the app.** Needs: V.5a, 4.9.
  **Build:** Extend current title/group search with explicit field/tag/expiry filters and controls for the new core operations. Search secret contents only by deliberate local opt-in; clear indexes on lock.
  **Verify (V-V.5b):** A user finds, moves and renames a credential with duplicate titles present; filters match controlled fixtures; locked state exposes neither results nor cached secret text.

- [ ] **V.6 — Generate passphrases.** Needs: 4.2.
  **Build:** Add passphrase generation through a justified pinned word list and the core generator interface; expose equivalent CLI/GUI options without replacing the existing character generator.
  **Verify (V-V.6):** Generated phrases obey selected word/separator rules and reject invalid limits; deterministic test randomness verifies selection boundaries; the interface does not overstate entropy.

- [ ] **V.7 — Manage custom fields.** Needs: 4.9.
  **Build:** Add core-backed add/edit/remove workflows for named custom fields with their protected flags; distinguish standard fields from arbitrary fields and preserve unknown attributes.
  **Verify (V-V.7):** A protected custom field remains protected after GUI/CLI round-trip; duplicate or reserved names fail safely; unrelated KeePassXC metadata survives.

- [ ] **V.8a — Expose bounded attachment operations.** Needs: 0.2.
  **Build:** Add core attachment enumeration, import, removal and bounded in-memory reads through KeePassInterop. Establish size limits and deliberate-export rules consistent with PRODUCT before adding an export path.
  **Verify (V-V.8a):** Binary fixtures survive unchanged, oversize input fails before allocation/write, and ordinary operations create no plaintext temporary files; real KeePassXC reads the attachments.

- [ ] **V.8b — Manage attachments from the app.** Needs: V.8a, 4.9.
  **Build:** Add attachment metadata, import/remove controls and explicit supported viewing/export actions under V.8a's rules; never auto-open an attachment or execute its content.
  **Verify (V-V.8b):** A user adds and removes the intended attachment with duplicate filenames present; cancelled actions preserve data; lock clears displayed content and hostile files execute nothing.

- [ ] **V.9 — Report local password health.** Needs: V.5a, 4.9.
  **Build:** Compute weak/reused/expired credential findings locally over the unlocked vault and expose actionable GUI filters with explainable scoring. Network breach lookup is P.8.
  **Verify (V-V.9):** Known weak/reused/expired fixtures receive the expected findings; changing one entry updates them; lock clears results and the workflow makes no network request.

- [ ] **9.1a — Define a loss-aware import pipeline.** Needs: V.1a, V.7, V.8a.
  **Build:** Add a shared import plan/result contract with source item identities, unsupported fields, collision decisions and atomic commit. Keep export files read-only and never silently drop data.
  **Verify (V-9.1a):** Ambiguous duplicates and unmapped fields appear before writing; cancelled/failed imports preserve the original vault; repeated import follows a documented duplicate policy.

- [ ] **9.1b — Import Bitwarden JSON.** Needs: 9.1a.
  **Build:** Implement the supported Bitwarden JSON forms as one adapter, preserving supported item types and reporting unsupported/export-protection variants explicitly.
  **Verify (V-9.1b):** Synthetic logins, notes, custom fields and TOTP metadata survive import; unsupported variants refuse or report exact losses before commit; KeePassXC opens the result.

- [ ] **9.1c — Import LastPass CSV.** Needs: 9.1a.
  **Build:** Implement the CSV adapter with quoted multiline fields, folders and secure-note mapping using the shared preview/result contract.
  **Verify (V-9.1c):** Embedded separators/newlines and duplicate titles map correctly; unsupported fields are retained or explicitly rejected; no malformed row produces a partial silent import.

- [ ] **9.1d1 — Import 1Password 1PUX.** Needs: 9.1a.
  **Build:** Implement the 1PUX adapter, preserving supported attachments/TOTP and reporting unsupported item semantics through the common preview.
  **Verify (V-9.1d1):** Fixtures retain credentials, supported attachments and OTP attributes; encrypted/unsupported variants fail clearly and no unsupported field disappears without a preview warning.

- [ ] **9.1d2 — Import 1Password CSV.** Needs: 9.1a.
  **Build:** Implement the documented CSV subset through the common importer, preserving quoted fields and surfacing metadata the source format cannot represent.
  **Verify (V-9.1d2):** Multiline/duplicate-title fixtures import correctly; malformed rows or unsupported layouts cannot produce a silent partial import.

- [ ] **9.1e — Import KeePassXC CSV and open existing KDBX.** Needs: 9.1a.
  **Build:** Add CSV mapping and direct existing-KDBX onboarding that keeps the original file intact until the user explicitly saves or copies it. Report unsupported vault configuration before mutation.
  **Verify (V-9.1e):** CSV fixtures import correctly; existing KDBX onboarding preserves unknown fields/history; unsupported configurations leave the original byte-identical.

- [ ] **9.1f — Guide import in the desktop.** Needs: 9.1b, 9.1c, 9.1d1, 9.1d2, 9.1e, 4.8.
  **Build:** Add source selection, preview, conflict choices and an import result report over the adapters; explain handling of plaintext source exports without deleting user files automatically.
  **Verify (V-9.1f):** A new user imports each supported format without a terminal; cancellation writes nothing; the result names every unresolved loss and links to the imported entries.

- [ ] **9.2a — Implement interoperable TOTP.** Needs: V.7.
  **Build:** Parse and calculate supported KeePassXC otp attributes through mature primitives, explicitly handling algorithm, digits and period; expose CLI code retrieval while retaining the seed as protected data.
  **Verify (V-9.2a):** Known vectors and real KeePassXC agree at fixed times for each claimed parameter set; invalid parameters refuse; normal code output contains no seed.

- [ ] **9.2b — Use TOTP in the app and approval bridge.** Needs: 9.2a, 4.9, 2.2.
  **Build:** Show the current code/countdown in the GUI and add an approved otp field to MCP. Never return the seed; ensure OTP expiry is not confused with the approval grant's TTL.
  **Verify (V-9.2b):** Time-boundary fixtures refresh correctly; deny/timeout emits no code; agent replies contain only the code and its documented lifetime, and hidden UI trees contain no seed.

### Recover concurrent changes

- [ ] **1.4a — Specify merge and deletion semantics.** Needs: V.2a, V.3a.
  **Build:** Record the merge policy for UUID matching, timestamp/history conflicts, deleted-object tombstones, group moves and recycle-bin state. Absence alone is never deletion; preserve conflicting versions.
  **Verify (V-1.4a):** Worked fixtures cover divergent edits, rename/move, delete-versus-edit and equal timestamps; every case has a deterministic result or explicit unresolved conflict.

- [ ] **1.4b — Implement atomic entry-level merge.** Needs: 1.4a, V.4a.
  **Build:** Implement the agreed core merge with no-write preview, atomic save and history preservation; guard concurrent file replacement and leave equal-timestamp content conflicts unresolved.
  **Verify (V-1.4b):** Older values cannot overwrite newer ones; repeated merge is idempotent; missing entries are retained; delete/edit conflicts remain recoverable; unresolved merge leaves the live file unchanged.

- [ ] **1.4c — Resolve merges through CLI and GUI.** Needs: 1.4b, V.2b, V.4b.
  **Build:** Add CLI dry-run/app conflict inspection and explicit selected resolution over the same core result. Preserve both source files until commit and report unmerged entries.
  **Verify (V-1.4c):** Two edited vaults converge where safe; a person resolves a named conflict without losing either revision; the resulting vault opens in real KeePassXC.

### Native approvals and developer workflows

- [ ] **4.5 — Define and measure the daily-use tasks.** Needs: 4.8, 4.9.
  **Build:** Write docs/ux.md with task IDs, surfaces, numeric pass thresholds and observation methods for create/import/find/copy/restore/inject/approve/deny/fill. Keep secret content out of measurement.
  **Verify (V-4.5):** Every task has a numeric threshold and method; one observed fresh-user create/save task has a retained pass/fail result rather than an unmeasured claim.

- [ ] **8.2a — Specify the shared approval interaction.** Needs: 2.2, 4.5.
  **Build:** Define common fields, ordering, trusted labels, untrusted reason rendering, defaults and timeout for terminal/native/activity/browser. Separate approval reuse from credential validity.
  **Verify (V-8.2a):** Hostile multiline/bidi/fake-prompt examples have expected renderings and denial outcomes; the spec cannot imply that expiry revokes a disclosed password.

- [ ] **4.3a — Add the authenticated desktop approval channel.** Needs: 2.2, 8.2a.
  **Build:** Extend ApproverProtocol with authenticated local UI subscribe/list/answer messages; the agent remains pipe owner and authority. Scope answers to the pending request and connected user session.
  **Verify (V-4.3a):** Spoofed clients, stale answers, duplicate approvals and connection loss fail closed; a valid UI answer releases only the requested field and is auditable.

- [ ] **4.4 — Render native approval and denial.** Needs: 4.3a, 8.2a.
  **Build:** Add native notification/window presentation over the approved channel while keeping terminal fallback; default/timeout/dismissal deny, with hostile reason text rendered as data.
  **Verify (V-4.4):** Each supported OS passes approve, deny, timeout, dismiss and killed-approver cases; hostile text cannot draw a second trusted prompt; headless mode remains usable.

- [ ] **4.3b — Show live agent activity and effective controls.** Needs: 4.3a, 4.4.
  **Build:** Add pending requests, audit history, labeled connection/grant/policy counts and per-client pause. Apply pause to live authorization before confirming it, then persist it safely; an absent agent displays unavailable, not zero.
  **Verify (V-4.3b):** Pending requests appear before resolution; pause refuses the next request without relying on restart; expiry updates counts; disconnect/lock clears sensitive names and stale controls cannot approve.

- [ ] **4.4b — Start and stop the approver from a user action.** Needs: 4.4, 4.3b.
  **Build:** Provide an explicit desktop action to start/unlock the local approver and clear controls to stop/lock it; unlock input belongs to the approver process, never the MCP/control channel. MCP or extension traffic cannot trigger an unlock prompt by itself.
  **Verify (V-4.4b):** A user completes setup and one approval without a terminal; remote requests cannot create a master-password prompt; stopping the approver denies pending work and clears grants.

- [ ] **E.1 — Finish the desktop environment workflow.** Needs: 4.9, V.5b, 4.4b.
  **Build:** Add guided env import/selection and project-to-env mapping over the existing core, with a preview of injected variable names and an explicit run action. Keep secret values out of saved project configuration.
  **Verify (V-E.1):** A selected project receives only its mapped env; inherited collisions follow documented rules; cancellation injects nothing and no dotenv/plaintext temporary file is created.

### Browser integration

- [ ] **8.1 — Install and pair the native messaging host.** Needs: 4.4b, 8.2a.
  **Build:** Add a vault-free native host over the local approver, browser-specific manifests and scoped extension pairing. Register/remove only owned manifest entries; use each browser's actual supported package format.
  **Verify (V-8.1):** Wrong extension IDs/origins fail; no approver means clear unavailable state with no unlock prompt; install/uninstall preserves unrelated registrations on each promised OS/browser pair.

- [ ] **8.3a — Fill a login with verified origin matching.** Needs: 8.1.
  **Build:** Implement registrable-domain/origin rules, explicit first access, direct field filling and locked-state refusal; keep cross-origin frames denied until separately specified.
  **Verify (V-8.3a):** Domain lookalikes, substring traps and cross-origin frames receive no fill; the legitimate origin succeeds after confirmation and clipboard contents remain unchanged.

- [ ] **8.3b — Save a new browser credential.** Needs: 8.3a, 4.9.
  **Build:** Add explicit save preview with origin, destination vault/group and selected fields. Route the confirmed write through authenticated local IPC and core validation; site content cannot silently write a vault.
  **Verify (V-8.3b):** A new login is saved once in the chosen vault; cancel/locked state writes nothing; forged-origin or unsolicited page requests cannot create entries.

- [ ] **8.3c — Update an existing browser credential.** Needs: 8.3b, V.2a.
  **Build:** Add explicit password-update matching and confirmation through the shared core, preserving history and origin approval.
  **Verify (V-8.3c):** A password change updates the intended UUID and preserves its old value; ambiguous matches require selection and cancellation writes nothing.

- [ ] **8.3d — Generate a browser credential.** Needs: 8.3b, V.6.
  **Build:** Expose the core password/passphrase generator in the extension, with explicit fill and save decisions and no automatic persistence from site content.
  **Verify (V-8.3d):** Selected generator rules are honored, a generated credential saves only after confirmation and hostile origins cannot retrieve it.

- [ ] **8.3e — Fill supported TOTP and custom fields.** Needs: 8.3a, 9.2b, V.7.
  **Build:** Add explicit mapping and filling for supported extra fields and current OTP codes under the existing origin/approval rules; never expose OTP seeds to a page.
  **Verify (V-8.3e):** Approved mappings fill only the intended fields on the legitimate origin; stale OTP codes refresh correctly and denied/mismatched pages receive neither codes nor custom values.

- [ ] **8.2b — Verify approval consistency across surfaces.** Needs: 4.3b, 4.4, 8.3c, 8.3d, 8.3e.
  **Build:** Implement the shared approval fixtures on terminal, native prompt, activity and browser, recording platform exceptions explicitly.
  **Verify (V-8.2b):** The same hostile request cannot alter trusted fields on any surface; all show the specified defaults/timeout, and all refusal paths release nothing.

### Product acceptance

- [ ] **4.6 — Exercise actual desktop rendering.** Needs: 4.9, V.2b, V.3b, 4.3b, 4.4, F.2d.
  **Build:** Add Skia-backed headless render tests with Linux goldens and platform structural checks; cover masking, lock, history, native approval and narrow/wide layouts. Extend the F.2d automation protection across the completed secret-entry screens. Retain failing renders without secret fixture values.
  **Verify (V-4.6):** Deliberately exposing a typed character in rendering or automation fails; the F.2d master-password check still passes alongside all secret-entry screens. Lock removes sensitive content; native-library loading and a real first render are checked separately from non-rendering selftests.

- [ ] **9.4 — Publish a versioned compatibility result.** Needs: V.1b, V.7, V.8b, 9.1f, 9.2b, 1.4c.
  **Build:** Extend both KeePassXC gate directions for the completed workflows, recording the upstream version, supported formats and tested platforms in FEATURES. A newer upstream release opens review work rather than invalidating a dated historical result.
  **Verify (V-9.4):** Every claimed behavior has a passing fixture on its promised platform; an unsupported variant is refused/preserved explicitly; a mismatched version label fails the evidence check.

- [ ] **3.10a — Ship the local product guides.** Needs: 9.1f, 9.2b, 8.3c, 8.3d, 8.3e, E.1, V.4b.
  **Build:** Write version-correct install/create/import/find/fill/TOTP/restore/env/agent guides and link them from the app. Keep unavailable hosted, SSH and advanced features visibly separate until delivered.
  **Verify (V-3.10a):** A reviewer follows each guide against the candidate packages; every advertised screen has help and every command exists in that candidate.

- [ ] **1.5a — Observe Windows clipboard history behavior.** Needs: 1.5b.
  **Build:** On a Windows VM with history enabled, prove an ordinary control copy appears and a keypaste secret does not; observe cloud clipboard on a second signed-in machine.
  **Verify (V-1.5a):** The control appears and the secret is absent before/after clear; an enforced-disabled history panel is inconclusive. **External prerequisite:** a suitable machine. Keep this residual disclosed until observed; it does not block independent implementation or R.1.

- [ ] **R.1 — Verify the working password manager.** Needs: R.0c, P.0, 4.7c, 4.7d, 8.4b, K.4, K.5, 4.8, 4.9, V.1b, V.2b, V.3b, V.4b, V.5b, V.6, V.7, V.8b, V.9, 9.1f, 9.2b, 1.4c, 4.4b, 4.3b, E.1, 8.2b, 4.5, 4.6, 9.4, 3.10a.
  **Build:** Run the complete daily-use journey against anonymous public desktop downloads and store-installed extensions; retain results for the declared OS/browser matrix and founder daily use.
  **Verify (V-R.1):** A fresh user installs, creates/imports, saves/fills/updates, restores a mistake, injects an env, approves/denies an agent and upgrades without data loss or terminal assistance for ordinary password tasks. Missing target evidence or an unfinished required workflow fails the gate.

## Pilot ready

The first managed pilot uses the published desktop app and extension. It must hide routine file management, preserve offline use and provide real device, recovery and service operations. A web vault follows the feasibility gate in Expansion; a supported phone workflow is a paid consumer release requirement. Preparation can proceed earlier, but no external managed pilot starts before R.1 and the controls below pass.

### Account, device and relay foundations

- [ ] **H.1 — Settle hosted authentication and key boundaries.** Needs: 0.2, 2.2.
  **Build:** Record account authentication, device authorization, local vault unlocking, recovery and revocation as separate protocols; retain the .NET relay, S3-compatible blobs and SQLite metadata from D-0064. Check every key transfer against immutable PRODUCT §3: uploading a wrapped master key or inventing cryptography is not an available implementation shortcut.
  **Verify (V-H.1):** A reviewed data-flow table names each persisted field, key holder and trust boundary; server compromise cannot obtain vault-unlock material. Any unresolved conflict with §3 blocks affected implementation and the pilot instead of receiving an assumed exception.

- [ ] **5.2a — Build the relay executable and storage contract.** Needs: H.1.
  **Build:** Add `Keypaste.Relay` as the same NativeAOT executable for hosted and self-hosted operation, with pinned dependencies, configuration validation, SQLite migrations and S3-compatible encrypted-object storage ports. Define bounded object sizes, version identifiers, ETags and authorized account/device scopes before exposing routes.
  **Verify (V-5.2a):** The built candidate executable starts against temporary stores, applies migrations idempotently and rejects missing/unsafe production configuration; it has no vault-decryption dependency or plaintext secret input contract.

- [ ] **H.2 — Implement account registration and authentication.** Needs: 5.2a.
  **Build:** Add verified signup, login and account-password reset through established authentication components; rate-limit authentication and prevent account enumeration. Account credentials authenticate service access and do not become vault-unlock credentials.
  **Verify (V-H.2):** Expired/replayed verification or reset tokens fail, unverified accounts cannot sync, and account reset neither decrypts a fixture vault nor silently changes its unlock material.

- [ ] **H.3 — Implement account MFA and session revocation.** Needs: H.2.
  **Build:** Add an established MFA factor with enrollment, confirmation, recovery codes and authenticated removal; expose a bounded session inventory and revoke-one/revoke-all operations. Require fresh authentication for sensitive account changes.
  **Verify (V-H.3):** Enrollment cannot activate before proof, recovery codes work once, and a revoked session's next request is denied. Missing or invalid MFA fails closed without changing local vault access.

- [ ] **H.5a — Define recoverable and unrecoverable account failures.** Needs: H.1, H.3.
  **Build:** Specify lost account password, lost MFA, lost device and lost vault-unlock material separately. Select only a reviewed, §3-compatible trusted-device or user-held offline recovery mechanism; state the loss cases that remain unrecoverable and what support can actually do.
  **Verify (V-H.5a):** Each loss case has required user-held material, attacker assumptions and a reproducible expected outcome. Any design needing server-held wrapped master keys is rejected under §3, and a design with no viable implementation cannot pass this gate.

- [ ] **H.4 — Implement trusted-device authorization.** Needs: H.3, H.5a.
  **Build:** Register per-device service authentication keys, explicitly bootstrap the first device for a newly verified account and approve subsequent enrollment through an existing trusted device or the reviewed recovery path, and revoke devices without confusing service keys with vault keys. Reject enrollment races and replayed approval challenges.
  **Verify (V-H.4):** First-device bootstrap requires the recorded explicit confirmation and additional devices cannot enroll from account login alone; an approved second device can authenticate, a revoked device cannot fetch or upload another version, and server stores contain no vault-unlock material.

- [ ] **5.2b — Implement authorized blob sync and version retention.** Needs: H.4.
  **Build:** Add bounded upload, conditional download, compare-and-swap publication and retained encrypted versions; enforce account/device ownership on every operation and identifier. Retain the previous good version until a complete replacement is durable.
  **Verify (V-5.2b):** Cross-account IDs, stale ETags, oversized uploads and interrupted writes cannot expose or replace another vault. Sentinel secrets and entry titles appear in neither metadata, storage plaintext nor logs; a prior complete encrypted version remains downloadable after a failed upload.

- [ ] **5.2c — Publish and exercise the self-hosted relay.** Needs: 5.2b.
  **Build:** Package the same executable with an operator-selected authentication setup, storage configuration and upgrade procedure; disable commercial entitlement checks when billing is unconfigured without disabling authentication. Add its release assets and public installation proof under RELEASE.
  **Verify (V-5.2c):** A fresh operator installs the public package, enrolls two authorized devices and round-trips ciphertext without a Stripe account; an unknown device is denied. Upgrade preserves accounts and objects, and the recorded restore procedure recovers the previous working service.

### Managed client experience and recovery

- [ ] **5.3a — Implement recoverable client synchronization.** Needs: 1.4c, 5.2b.
  **Build:** Add the shared-core sync state machine and `keypaste sync`: pull, use the completed vault merge contract, persist safely and conditionally push. Keep local edits and the previous valid file recoverable through interruptions and unresolved conflicts.
  **Verify (V-5.3a):** Two devices' independent offline edits converge; conflicting same-entry edits remain inspectable without silent loss; a stale push fails. Killing the client at each write boundary leaves a valid recoverable local vault and the last confirmed remote version.

- [ ] **5.3b — Build managed desktop onboarding.** Needs: H.4, 5.3a, 4.8, 9.1f.
  **Build:** Add signup/login and create-or-import onboarding that manages the vault's local location automatically while keeping local-only startup available. Explain account sign-in and vault unlock separately and show the first successful sync before calling setup complete.
  **Verify (V-5.3b):** A fresh profile creates or imports a vault and saves a login without a terminal or choosing a filesystem path; failed signup/sync remains actionable and never claims completion. Local-only use still works with the network disabled.

- [ ] **5.3c — Expose device, sync and conflict controls.** Needs: 5.3b, H.3.
  **Build:** Add the desktop device/session list, second-device enrollment, revoke controls, last-confirmed sync state, offline state and conflict/version-restore screens over the completed core operations.
  **Verify (V-5.3c):** A user enrolls and revokes a real second device, resolves a staged offline conflict and restores an older version from the UI. Unreachable service, pending upload and completed sync display different states; stale UI cannot grant a revoked device access.

- [ ] **H.5b — Implement and rehearse the approved recovery flow.** Needs: H.5a, 5.3c.
  **Build:** Implement only the approved recovery mechanism, its enrollment confirmation and plain-language desktop guidance. Keep recovery material off server logs and require the user to confirm possession before promising recoverability.
  **Verify (V-H.5b):** A clean device recovers the fixture only with the required user-held material or authorized trusted device; support credentials and account reset alone fail. Test each documented unrecoverable case and show the matching explanation without deleting the user's remaining encrypted data.

- [ ] **H.6 — Implement account export and deletion.** Needs: 5.2b, 5.3c.
  **Build:** Add account export of encrypted vaults plus portable nonsecret account metadata, authenticated deletion with a documented grace/backup-retention policy, and desktop controls for these operations. Keep user-selected local vault files intact and make retained backup expiry explicit.
  **Verify (V-H.6):** An exported KDBX opens in KeePassXC on a fresh machine; deleted accounts lose online access and active objects follow the recorded schedule. A retained backup cannot resurrect a deleted account into service, and deletion does not remove the local vault.

### Service operation and pilot gate

- [ ] **H.7 — Complete hosting owner enrollment (H-0019).** Needs: H.1.
  **Build:** The owner obtains the relay VM, S3-compatible bucket, `sync.keypaste.com` control and transactional-mail account, records account ownership/recovery and supplies scoped deployment credentials through the secret store. Keep this explicit manual external-account task separate from application implementation.
  **Verify (V-H.7):** The operator can authenticate to each required account, recover ownership and verify DNS/storage/mail configuration; repository files contain no credentials. Missing ownership or enrollment leaves this checkbox open.

- [ ] **H.8 — Deploy the managed pilot service.** Needs: 5.2c, H.7, H.5b, H.6.
  **Build:** Publish the reviewed relay build containing the completed account, recovery and deletion operations, then deploy that exact artifact behind TLS. Run SQLite with one declared writer topology and configure durable blob storage, backups and bounded service permissions; record deployment, rollback and upgrade commands and identity.
  **Verify (V-H.8):** Two enrolled clients use the public endpoint to exchange the encrypted fixture; unauthenticated and cross-account probes fail. Restart and a deployed-version rollback preserve durable data, and public health checks identify the deployed version without revealing account or vault metadata.

- [ ] **H.9 — Prove durability, outage handling and operational alerts.** Needs: H.8, 5.3c.
  **Build:** Add content-free availability/error/capacity monitoring, bounded abuse controls and an operator runbook; define recovery objectives and backup retention from the service's actual configuration. Exercise loss of the relay machine and storage/network outages using a disposable pilot fixture.
  **Verify (V-H.9):** Restore SQLite and encrypted objects into a fresh deployment within the declared objectives; both clients reconcile after an outage without lost edits. An injected service failure reaches the operator's alert channel, and logs contain no entry names, secret values or unlock material.

- [ ] **H.10 — Establish support, privacy and incident procedures.** Needs: H.6, H.9.
  **Build:** Publish the actual support channel, service/privacy/retention terms and incident communication procedure; define issue triage, account verification and diagnostic collection that excludes vault contents and entry names. Name the operator responsible for each service obligation before inviting external pilot users.
  **Verify (V-H.10):** A support drill reaches the responsible person and resolves an account issue without asking for a vault/master password; an incident rehearsal produces the promised notice and recovery actions. Published retention and deletion statements match H.6 and H.9.

- [ ] **5.3d — Publish the hosted-capable clients.** Needs: R.1, 5.3c, H.5b, H.6, H.8.
  **Build:** Publish updated CLI/desktop packages containing managed onboarding, device controls, sync, recovery and account exit; retain signing and public installation/upgrade evidence on every supported target. Verify them against the exact deployed relay version, keeping service enrollment restricted to the pilot cohort until its gates pass.
  **Verify (V-5.3d):** A clean machine downloads the hosted-capable release and completes onboarding, two-device sync, restore and export against the deployed service. Old pre-hosting downloads, source builds or expiring CI artifacts cannot satisfy the row.

- [ ] **5.7 — Publish usable sync, recovery and operator instructions.** Needs: 5.3d, H.5b, H.6, H.9.
  **Build:** Write `docs/sync.md` for managed users and `docs/relay.md` for operators; update THREATS with relay/account/device/recovery boundaries and residual risks. Separate account recovery, vault recovery and backup restore, and describe the metadata visible to an operator.
  **Verify (V-5.7):** A reviewer follows onboarding, second-device setup, restore and export using the released software; each security or availability promise links to an implemented control and retained verification result.

- [ ] **R.2 — Verify managed pilot entry and close the beta milestone.** Needs: R.1, H.3, H.4, H.5b, H.6, H.9, H.10, 5.7.
  **Build:** Run an invited pilot on the public service with separate accounts and real devices; retain the onboarding, offline-conflict, recovery, outage and exit results plus a prioritized defect register. Invite external users only after the prerequisite controls pass; finish this gate only after their journeys are observed.
  **Verify (V-R.2):** Invited nontechnical users complete desktop onboarding, second-device use, a recovery drill and export without operator access to secrets. No unresolved data-loss, tenant-isolation or false-recovery defect remains; absent external pilot evidence keeps the milestone open.

### Optional local unlock convenience on supported desktop systems

- [ ] **4.10a — Add Windows quick unlock.** Needs: R.1, V.1b.
  **Build:** Review local key lifetime against PRODUCT §3, then use Windows authentication to resume an eligible local vault session; preserve the full master-password/keyfile path and explicit timeout/process-lifetime limits.
  **Verify (V-4.10a):** On a supported Windows machine, valid OS authentication resumes only the intended session; refusal, expiry, changed device credentials and process restart follow the documented full-unlock requirement. No server receives unlock material.

- [ ] **4.10b — Add macOS quick unlock.** Needs: R.1, V.1b.
  **Build:** Implement the reviewed macOS authentication equivalent over the same session policy, with clear unsupported-hardware fallback and no remote recovery dependency.
  **Verify (V-4.10b):** A supported Mac passes success, refusal, expiry, device-credential change and restart cases; unavailable biometrics never bypass full vault unlock and no server receives unlock material.

## Paid release

Sell hosted convenience only after the pilot works. Local functions, security, signatures and self-hosted operation remain available without a subscription. The consumer offer includes a supported phone workflow; a desktop beta does not establish that claim.

- [ ] **5.5a — Complete payment owner enrollment (H-0018).** Needs: R.2.
  **Build:** The owner activates the Stripe account, completes required business/payout configuration and supplies scoped test/live credentials through the secret store. Record plan identifiers and ownership without placing credentials or unapproved prices in the repository.
  **Verify (V-5.5a):** The operator can access checkout, customer management, webhooks and payout configuration; required activation items are complete. A test-mode payment alone does not establish live enrollment.

- [ ] **5.5b — Implement hosted subscription entitlement.** Needs: 5.5a.
  **Build:** Add Checkout, verified idempotent webhooks and relay-side entitlement checks, including duplicate/out-of-order events and customer reconciliation. Keep client/local functionality and authenticated self-hosted operation independent of payment state.
  **Verify (V-5.5b):** Invalid signatures cannot change entitlements; replayed or reordered events converge to the provider's actual state. Revoking a paid entitlement follows the documented hosted policy while local open/edit/export/env/agent operations continue unchanged.

- [ ] **5.5c — Implement cancellation and failed-payment behavior.** Needs: 5.5b, H.6.
  **Build:** Expose plan state, billing management and cancellation with explicit grace, sync-read/write and data-retention dates. Give users a working export route before any hosted-data deletion; never imply cancellation destroys their local vault.
  **Verify (V-5.5c):** Exercise renewal, failure, recovery, cancellation and grace expiry against the configured payment events; the UI dates and relay behavior agree. A canceled user can still open and export their local vault, and retained hosted data follows the published policy.

- [ ] **5.6 — Implement consented list confirmation.** Needs: H.7.
  **Build:** Complete the existing signup list's double opt-in and unsubscribe flow using the transactional-mail provider; distinguish list consent from required account/service messages. Validate and retain consent state in the appropriate service store without broadening the public signup endpoint's access.
  **Verify (V-5.6):** An unconfirmed address receives no list campaign; confirmation tokens expire and cannot subscribe another address; unsubscribe prevents subsequent list sends. Account and security messages follow their separate documented purpose.

- [ ] **M.1 — Select and prove the supported phone approach.** Needs: R.2.
  **Build:** Compare a first-party native client and supported existing KDBX-client integrations using actual iOS/Android device prototypes; decide the promised phone targets and how account/device authorization and sync work. The initial phone route must publish independently of W.2c; selecting a web-dependent route requires moving that web delivery before R.3 in the plan. KDBX support alone does not establish relay integration.
  **Verify (V-M.1):** On each proposed phone OS, a named prototype opens the fixture, safely obtains an authorized encrypted update and exercises the intended autofill route. An approach with no viable key-handling, distribution or sync path cannot be selected for paid release.

- [ ] **M.2a — Connect and lock the selected phone client.** Needs: M.1, H.5b.
  **Build:** Implement the selected client's account/device adapter and local vault-open/lock path on each advertised phone target using the architecture proved in M.1. Keep unlock material within the reviewed local process and expose enrollment/revocation state to the user.
  **Verify (V-M.2a):** A real phone enrolls through the approved path and opens the fixture; an unauthorized or revoked device cannot obtain another encrypted update. Lock, app restart and protected-store failure require the specified reauthentication and reveal no cached plaintext.

- [ ] **M.2b — Add phone login use and autofill.** Needs: M.2a.
  **Build:** Connect the selected client's login retrieval, native autofill and save/update integration to its reviewed vault path; request only the platform capabilities needed for these operations. Keep unsupported app/browser contexts explicit.
  **Verify (V-M.2b):** On each promised phone OS, a user fills the fixture login and saves a changed password; mismatched origins, cancellation and locked state release nothing. Supported applications/browsers and the retained device results match the published capability list.

- [ ] **M.2c — Add phone sync, recovery and export controls.** Needs: M.2a, 5.3a, H.5b.
  **Build:** Connect the selected client to the proven sync/recovery state machine and expose offline/conflict, restore and encrypted-export operations. Use the approved recovery authority without routing vault-unlock material through the relay.
  **Verify (V-M.2c):** A real phone and desktop converge after offline edits, preserve concurrent conflicts and restore/export through the documented UI. Service outage preserves local use and cannot appear as a completed upload; a simulator or mock alone is insufficient.

- [ ] **M.3 — Publish and verify phone availability.** Needs: M.2b, M.2c.
  **Build:** Complete required owner/store enrollment and publish the selected client or integration's supported setup instructions; retain exact store/app/OS versions and upgrade evidence. State any third-party support responsibility plainly on the download page.
  **Verify (V-M.3):** A new user installs from the advertised public channel on each promised phone OS, connects without a terminal, uses autofill and upgrades without losing the vault. An unpublished build or unsupported third-party workaround cannot close the row.

- [ ] **10.2 — Complete independent hosted and client-boundary review.** Needs: R.2, M.3, 5.5c.
  **Build:** Commission an independent review of the relay, authentication/device recovery, tenant isolation, sync/merge, released browser/native bridge and supported mobile integration; include billing authorization where relevant. Record scope, reviewed versions, remediation and a publishable report summary.
  **Verify (V-10.2):** A report and retest evidence exist for the release candidate; unresolved findings have severity, owner and a written disposition consistent with PRODUCT §3. Unresolved critical/high security defects or a law violation block paid release even if a risk-acceptance note exists.

- [ ] **5.8 — Prepare reviewed plans and controlled checkout.** Needs: 5.5c, M.3, H.10, 10.2.
  **Build:** Prepare owner-approved prices and Free/Individual/Team availability tied to actual relay entitlements; mark unlaunched Team capabilities unavailable. Link supported platforms, retention/cancellation terms and recovery limits; keep live checkout restricted to authorized validation accounts until R.3 opens general paid enrollment.
  **Verify (V-5.8):** A validation account can complete checkout and receive the corresponding entitlement while general paid enrollment remains closed; no price cell gates local security, signing or a Free feature. Support, cancellation and export links reach implemented candidate behavior.

### Community evidence and public communication

- [ ] **3.2 — Publish the initial community introduction (H-0006).** Needs: R.1, 3.0, 3.1, 3.2b.
  **Build:** Refresh launch.md's prepared CLI copy for the product actually released, record founder daily-use evidence, then post once per selected channel under the existing outreach rules. Claims and download links must match the published components; do not carry forward the obsolete mandatory “no GUI” sentence after desktop release.
  **Verify (V-3.2):** Every post has a live logged-out URL, working links and version-correct claims; absent daily-use evidence or publication keeps the row open.

- [ ] **3.3 — Complete the community feedback period (H-0007).** Needs: 3.2.
  **Build:** Spend fourteen days answering resulting issues/comments, track actionable defects and route private security reports through the documented channel.
  **Verify (V-3.3):** The full observation period has elapsed and no post-launch issue needing a response is older than 48 hours; a prepared response or an unobserved waiting period is not completion.

- [ ] **3.10b — Ship hosted and account-lifecycle guides.** Needs: R.2, 5.5c, M.3.
  **Build:** Extend app/site help for signup, phone access, account-versus-vault recovery, support, cancellation, export and deletion; link future SSH/team/web guides only when their corresponding features ship.
  **Verify (V-3.10b):** A reviewer completes each guide against the release candidate without undocumented operator help; every account screen has accurate help and unavailable features are clearly labeled. R.3 repeats these journeys against the published client and deployed service before promoting the guides as public-version instructions.

- [ ] **R.3 — Verify the paid consumer release.** Needs: R.2, H.10, 5.5c, 5.6, M.3, 10.2, 5.8, 3.3, 3.10b.
  **Build:** Publish the reviewed billing-capable client versions and deploy the exact reviewed relay version, retaining public installation, upgrade and rollback evidence. With paid enrollment still restricted, run onboarding → desktop/browser/phone use → recovery → authorized live payment → cancellation/export; resolve defects, then open general paid enrollment and verify its public entry points. Record sandbox and live evidence separately; an earlier beta deployment or payment sandbox is insufficient.
  **Verify (V-R.3):** Independent nontechnical users finish the promised journey on public distributions; all applicable RELEASE evidence exists and service/support obligations are staffed. Failed payment cannot disable local access, and no consumer-critical platform, recovery or data-loss gap is disguised as a future feature.

## Expansion

Advanced parity is accepted after R.1; the other tracks activate at their stated prerequisites. Completing the consumer gate does not claim these features are available.

### Complete the KeePassXC behavior baseline

This implementation work is accepted scope and activates after R.1. It may run alongside hosted
work when its own dependencies are ready. P.0 prepares the individual behavior contract in Working proposition; P.9 forbids
claiming complete parity while any promised behavior or platform remains unverified.

- [ ] **P.1 — Support hardware-key challenge response.** Needs: P.0, V.1b.
  **Build:** Add the baseline's supported hardware-key unlock/change-credentials flow through mature interfaces, with explicit device enrollment and failure handling. Keep this separate from online account MFA.
  **Verify (V-P.1):** A real supported hardware key opens the same protected fixture in keypaste and KeePassXC; absent/wrong keys and interrupted challenges fail without mutation. Each promised device family needs its own evidence.

- [ ] **P.2 — Support multiple vaults and auto-open.** Needs: P.0, V.1b, 4.8.
  **Build:** Add explicit simultaneous-vault selection and the baseline's linked-vault/auto-open behavior, keeping vault identities, locks and approval scopes separate.
  **Verify (V-P.2):** Duplicate entry titles across vaults never redirect edits/releases; closing one vault does not expose another; linked auto-open respects required credentials, scope and lock behavior.

- [ ] **P.3a — Complete advanced entry and database metadata.** Needs: P.0, V.5b, V.7.
  **Build:** Implement missing baseline icon, timestamp, expiry, tag and database-setting controls through the core while preserving unknown metadata. Split distinct unimplemented setting families identified by P.0 into child tasks before coding.
  **Verify (V-P.3a):** Each accepted setting round-trips through real KeePassXC without changing unrelated data; unsupported settings are preserved or refused explicitly.

- [ ] **P.3b — Implement field references and placeholders.** Needs: P.0, V.7, P.2.
  **Build:** Implement the specified reference/placeholder semantics with bounded recursion, missing-reference handling and explicit trusted execution boundaries; entry text cannot become a command by being displayed.
  **Verify (V-P.3b):** Valid references match baseline results; cycles, ambiguity and cross-vault mistakes fail safely; hostile strings cannot execute or bypass entry exposure.

- [ ] **8.5a — Implement passkey creation through the browser bridge.** Needs: P.0, 8.4b, V.7.
  **Build:** Design and implement origin-bound WebAuthn credential creation using mature platform/library support and the approved core boundary; preserve the baseline's portable storage semantics where supported.
  **Verify (V-8.5a):** A real test relying party registers an approved passkey; wrong origin/RP ID, cancelled approval and locked vault fail; private key material never appears in page-visible responses or logs.

- [ ] **8.5b — Authenticate, manage and publish passkeys.** Needs: 8.5a.
  **Build:** Add origin-bound authentication, selection/deletion and tested backup/import behavior; publish the compatible app/extension versions through their existing release channels.
  **Verify (V-8.5b):** Public builds authenticate only to the expected relying party; restored credentials work where portability is claimed; removed credentials and mismatched origins fail on each supported browser.

- [ ] **P.4a — Implement desktop Auto-Type on Windows and macOS.** Needs: P.0, P.3b.
  **Build:** Add user-triggered window matching, sequence handling and cancellation using reviewed platform permissions; keep Auto-Type separate from browser-origin autofill.
  **Verify (V-P.4a):** The intended application receives the specified sequence; focus change/cancellation/lock prevents unintended typing; both native OS workflows have retained observations.

- [ ] **P.4b — Implement the supported Linux Auto-Type path.** Needs: P.0, P.3b.
  **Build:** Add and test the baseline-supported Linux display-server path; investigate additional compositor support separately and show unavailable states where the OS cannot support the operation.
  **Verify (V-P.4b):** Native supported sessions pass focus, sequence and cancellation cases; an unsupported Wayland/session combination cannot be advertised as working from an X11 result.

- [ ] **9.3a — Implement vault-backed SSH signing.** Needs: P.0, V.8a.
  **Build:** Implement the planned local SSH-agent protocol using selected vault attachments and mature signing primitives; record how its behavior differs from loading keys into an existing agent and add any promised interoperability path explicitly.
  **Verify (V-9.3a):** A real OpenSSH client authenticates using a selected fixture key; protocol requests cannot export private key bytes; lock/disconnect clears availability and signing creates no plaintext key file.

- [ ] **9.3b — Expose SSH key selection and lifecycle.** Needs: 9.3a, V.8b.
  **Build:** Add CLI/desktop key selection, explicit enrollment, unload/lock controls and supported-agent setup instructions; publish the completed workflow.
  **Verify (V-9.3b):** A key created/stored in KeePassXC performs the promised SSH operation from the public app; unselected keys are unavailable and locking removes active access within the declared bound.

- [ ] **P.5 — Integrate Linux Secret Service.** Needs: P.0, 4.4b.
  **Build:** Add a scoped, locally authenticated Secret Service implementation using a justified maintained D-Bus dependency; define collection exposure and unlock behavior independently of MCP.
  **Verify (V-P.5):** A real compatible Linux client stores/reads only its permitted collection; unknown clients cannot unlock or enumerate unrelated content; lock and service removal revoke future access.

- [ ] **P.6 — Implement KeeShare-compatible exchange.** Needs: P.0, V.1a, 1.4c.
  **Build:** Review the exact upstream sharing/key-transfer semantics against PRODUCT §3, then implement the compliant interoperability path through mature KDBX handling. Distinguish this from keypaste share links and organization accounts.
  **Verify (V-P.6):** Real KeePassXC and keypaste exchange the agreed fixture without losing history or unrelated entries; untrusted updates cannot bypass quarantine/exposure rules. An unresolved security-law conflict keeps this feature and parity gate open.

- [ ] **P.7a — Complete cipher and KDF configuration coverage.** Needs: P.0, V.1b.
  **Build:** Expose the baseline's supported KDBX cipher/KDF settings through the mature interop library with bounded resource parameters and safe migration previews.
  **Verify (V-P.7a):** Each claimed cipher/KDF combination opens both ways with retained field/history data; unreasonable resource parameters and failed migrations leave the original intact.

- [ ] **P.7b — Cover remaining legacy format/import behavior.** Needs: P.0, 9.1f, P.7a.
  **Build:** Enumerate and implement the baseline's remaining KDBX-version and legacy import forms through mature readers; keep KDBX as the output format and report unsupported semantics before conversion.
  **Verify (V-P.7b):** Every promised legacy fixture migrates without silent field/history loss; unsupported or malformed input is unchanged and explicitly rejected. Split additional independent formats into child rows before implementation.

- [ ] **P.8 — Add privacy-reviewed compromised-password checking.** Needs: P.0, V.9.
  **Build:** Select a maintained local dataset or explicit opt-in privacy-preserving lookup contract; document transmitted data and prevent entry names or full passwords leaving the device.
  **Verify (V-P.8):** Known compromised/nonmatching fixtures classify correctly; offline and unavailable-source states are distinct; recorded requests contain no secret value or entry name, and default use sends nothing.

- [ ] **P.9 — Verify complete KeePassXC coverage for the declared baseline.** Needs: R.1, P.0, P.1, P.2, P.3a, P.3b, 8.5b, P.4a, P.4b, 9.3b, P.5, P.6, P.7a, P.7b, P.8, 4.10a, 4.10b.
  **Build:** Run every P.0 behavior/platform comparison against the public keypaste versions and the pinned upstream version; add explicit tasks for newly discovered gaps instead of hiding them inside a completion claim.
  **Verify (V-P.9):** Every promised baseline behavior has passing public-version evidence and no unresolved gap. Family counts, source-only implementations or a newer upstream version cannot be presented as proof for an untested baseline.

### Distribution expansion — follows the daily-use release

- [ ] **3.7a — Publish Homebrew installation and updates.** Needs: R.1.
  **Build:** Publish the CLI formula and desktop cask for the supported macOS architectures from completed release manifests, including authenticated hashes and required notices; automate updates only after the release channel passes its public checks.
  **Verify (V-3.7a):** A fresh supported Mac installs the intended version through Homebrew and upgrades a previous package without losing vault data. An incomplete/unverified release cannot update the tap.

- [ ] **3.7b — Publish Scoop installation and updates.** Needs: R.1.
  **Build:** Publish a Scoop bucket entry for the supported Windows payloads with hashes, expected command paths, integration and uninstall behavior, generated only from completed release manifests.
  **Verify (V-3.7b):** A fresh supported Windows profile installs and upgrades the intended version through Scoop; commands and desktop launch work and vaults survive uninstall. A wrong hash or incomplete release is rejected.

- [ ] **3.7c — Publish winget installation and updates.** Needs: R.1.
  **Build:** Produce and submit winget manifests for the supported Windows installer with stable package/publisher identity, install scope and upgrade behavior; promote a package-manager channel only when its listing is actually available.
  **Verify (V-3.7c):** A fresh supported Windows machine finds, installs and upgrades the intended public version using winget; uninstall preserves user vaults. A locally validated or pending manifest without a working public listing cannot pass; close O-0011 after all 3.7 children pass.

- [ ] **3.9a — Add macOS Intel downloads.** Needs: R.1.
  **Build:** Add macOS x64 CLI/MCP and desktop native build/package/signing jobs, declare the supported OS floor and apply the same public installation/update evidence contract before advertising the target.
  **Verify (V-3.9a):** A supported Intel Mac installs authenticated public downloads and completes the release workflows; ARM emulation or cross-compilation alone cannot pass.

- [ ] **3.9b — Add Windows ARM64 downloads.** Needs: R.1.
  **Build:** Add Windows ARM64 CLI/MCP and desktop native build/package/signing jobs, account for native UI dependencies and declare the supported OS floor before exposing downloads.
  **Verify (V-3.9b):** A supported ARM64 Windows machine runs the native public packages and passes installation, workflows and upgrade checks; x64 emulation alone cannot pass.

- [ ] **3.9c — Add Linux musl CLI/MCP downloads.** Needs: R.1.
  **Build:** Add the linux-musl-x64 toolchain, runtime compatibility tests, public packaging and install instructions; define desktop-on-musl support separately only after its native UI dependency feasibility is demonstrated.
  **Verify (V-3.9c):** A clean declared musl distribution installs authenticated CLI/MCP downloads and passes advertised workflows without a glibc compatibility layer. Neither a source build nor an unverified desktop artifact expands the supported matrix.

- [ ] **3.9d — Add Linux ARM64 desktop downloads.** Needs: R.1.
  **Build:** Complete the declared but unbuilt desktop RID with a native runner, native UI dependencies and Linux package/install checks; publish it through the same authenticated release contract as Linux x64.
  **Verify (V-3.9d):** A clean supported Linux ARM64 machine installs the public desktop package, renders the GUI and passes daily-use/upgrade checks; the existing ARM64 CLI release does not count as desktop evidence.

### Web vault and secure sharing

- [ ] **W.1 — Prove the browser vault architecture.** Needs: 0.2, H.1.
  **Build:** Prototype a mature KDBX implementation and a viable shared-core/WASM route; examine browser storage, locking, offline operation, extension integration and the web origin's power to deliver client code. Record a design decision that satisfies §3 and the shared-core law before authorizing another secret-handling implementation.
  **Verify (V-W.1):** The prototype opens/edits a compatibility fixture that KeePassXC reopens, demonstrates lock/reload behavior and documents what a compromised delivery origin can access. An unresolved core-sharing or key-boundary conflict leaves web delivery blocked with a named decision to resolve.

- [ ] **W.2a — Implement reviewed browser onboarding and unlocking.** Needs: W.1, R.2.
  **Build:** Implement account/device setup, create-or-import and unlock/lock using the core adapter approved in W.1. Apply the reviewed storage, session and delivered-code controls rather than importing server-side assumptions into browser code.
  **Verify (V-W.2a):** A fresh supported browser creates or imports the fixture without a desktop app; reload, lock and expired/revoked authorization follow the specified behavior. Storage inspection finds no unencrypted vault data or unlock material.

- [ ] **W.2b — Implement browser editing and sync recovery.** Needs: W.2a, 5.3a, H.5b.
  **Build:** Expose find/create/edit login, encrypted sync, conflict/restore and export through the approved shared domain logic. Reuse the reviewed device and recovery authority without promising support can unlock a user's vault.
  **Verify (V-W.2b):** A browser creates and updates a login that desktop and KeePassXC reopen; offline conflicts survive reconnect, and closing a tab mid-save leaves a recoverable vault. Export and the approved recovery flow work on a fresh supported browser.

- [ ] **W.2c — Review and publish the web vault.** Needs: W.2b, R.3.
  **Build:** Obtain independent review of the web-origin/client-code boundary and deploy the exact reviewed version with retained public-origin smoke tests and rollback evidence. Advertise browser-only onboarding only after the public client passes its supported-browser matrix.
  **Verify (V-W.2c):** A fresh browser completes onboarding, edit/sync, lock and restore against the actual public origin; reviewed-version identity and rollback results are retained. Unresolved critical/high security findings or an unavailable required browser path block publication.

- [ ] **5.4a — Review and implement scoped share-bundle creation.** Needs: R.2, H.1.
  **Build:** First verify that the proposed KDBX/keyfile transfer conforms to immutable §3; if it does not, stop for an explicit compliant design. Through the shared core, create a real KDBX containing only the chosen entry/subtree with a fresh independent keyfile and preserved entry UUIDs; expose bounded CLI creation.
  **Verify (V-5.4a):** KeePassXC opens the result with its keyfile; no neighboring entry, history outside the selection or source vault-unlock material appears. The recorded security decision cannot rely on silently permitting a master-key upload.

- [ ] **5.4b — Publish one-download sharing and quarantine import.** Needs: 5.4a, 5.2b.
  **Build:** Add atomic one-download encrypted bundles with expiry, size/abuse limits and recipient import into quarantine; if approved, the fragment carries only the independent bundle secret and is never sent to the relay. Require explicit promotion before an imported entry is available to env or agent policy.
  **Verify (V-5.4b):** Concurrent fetches yield one successful payload, expired/redeemed links fail, and request/access logs contain no decryption material. Import cannot overwrite an existing trusted entry or become agent-accessible merely by choosing a familiar title.

### Teams and enterprise credentials

Activate this track after R.2 and selection of an authorized pilot organization/scope. The design and implementation tasks remain required work before R.4.

- [ ] **7.1a — Settle organization ownership and sharing authority.** Needs: R.2, H.1.
  **Build:** Define personal versus organization-owned vaults/collections covering logins, notes, API keys and envs; name owners/admins/members, recovery authority and join/leave rules. Review encrypted key distribution against §3 before implementing any per-member envelope design; no server-held master-key wrapping is implicitly authorized.
  **Verify (V-7.1a):** A reviewed access/key-holder matrix explains who can decrypt each resource, recover access and authorize membership. SSO/support access grants no decryption, retained historical copies remain an explicit risk, and any unresolved §3 conflict blocks sharing implementation.

- [ ] **7.1b — Implement organization membership and permissions.** Needs: 7.1a.
  **Build:** Add invitations, roles, collections, service-account identities and ownership transfer with default-deny authorization; record administrative events using opaque resource IDs, never entry-name telemetry. Keep organization scope separate from personal accounts/vaults.
  **Verify (V-7.1b):** A role-by-operation test matrix rejects cross-organization access, privilege escalation, replayed invitations and orphaned ownership. A service account reaches only its explicitly granted collection and cannot administer membership by implication.

- [ ] **7.1c — Implement reviewed shared-vault access and offboarding.** Needs: 7.1b.
  **Build:** Implement the approved client-side sharing/key-transition model and rotate access for future snapshots when a member leaves; preserve ordinary KDBX portability. Produce an offboarding checklist for separately rotating downstream static credentials because cached old copies cannot be revoked.
  **Verify (V-7.1c):** An authorized member opens the shared fixture in KeePassXC; a nonmember and an offboarded member cannot fetch/decrypt the next rotated snapshot. The test deliberately proves that the removed member's retained old snapshot remains readable with its old key.

- [ ] **7.1d — Build organization credential administration.** Needs: 7.1c.
  **Build:** Add desktop organization/collection views, invitation acceptance, role and ownership controls, shared login/API/env workflows and offboarding status. Display pending key transitions and downstream credential rotation separately from completed membership revocation.
  **Verify (V-7.1d):** An administrator and member onboard, use an authorized shared login/env, transfer ownership and offboard through the UI. Personal vaults remain outside organization administration, and incomplete rotation cannot display as fully completed offboarding.

- [ ] **7.2 — Implement attributed team approval and broker access.** Needs: 7.1c.
  **Build:** Extend the existing approval machinery to an identified member/service account, an authorized shared approver and one scoped field response; require current membership/policy at each release. Keep vault plaintext on the authorized local broker host and separate request revocation from provider credential rotation.
  **Verify (V-7.2):** A revoked member's next request releases nothing; denied/expired requests release nothing; an allowed request is attributed locally and reaches exactly its approved response path. A previously released static password remains usable until rotated, and documentation makes no one-use promise.

- [ ] **7.3a — Add organization OIDC sign-in.** Needs: 7.1b.
  **Build:** Integrate one supported IdP through established OIDC components with tenant binding, issuer/audience validation and explicit organization policy. Scope SSO to hosted account authorization; vault unlocking continues through the reviewed client-held key path.
  **Verify (V-7.3a):** Wrong tenant/issuer/audience and invalid state fail; an IdP session without vault-unlock material yields no plaintext. IdP outage follows the recorded fail-closed and owner-recovery policy without opening an authentication bypass.

- [ ] **7.3b — Add provisioning and deprovisioning.** Needs: 7.3a, 7.1c, 7.2.
  **Build:** Implement the first supported SCIM connector and manual fallback using the same membership operations; make duplicate/reordered changes safe. Deprovisioning revokes sessions/device service access and broker requests and queues the reviewed future-snapshot key transition.
  **Verify (V-7.3b):** Replayed provisioning creates one membership; an offboarded user loses future relay/broker access and key-transition work is visible until complete. Reordering cannot re-enable a disabled member without a new authorized lifecycle action.

- [ ] **6.1 — Prove external delegation visibility and revocation.** Needs: R.2.
  **Build:** Timebox a GitHub/Google feasibility investigation to two days using user-authorized test accounts; record exact endpoints/scopes, discoverable grants, revocable grants and unavailable cases in `docs/feasibility.md`. Do not claim a complete inventory when provider APIs expose only a subset.
  **Verify (V-6.1):** A recorded authorized request reproduces each supported claim and demonstrates one safe test revocation where offered; missing scopes or inaccessible grants remain visible limitations. No real account authorization or destructive grant revocation is inferred from a documentation task.

- [ ] **6.2 — Add the supported delegation views.** Needs: 6.1.
  **Build:** Extend Agent Activity with only the proved provider integrations, permissioned revocation/deep links and configurable staleness indicators; label provider coverage and last refresh. Count local and external sources separately when completeness differs.
  **Verify (V-6.2):** A permitted test grant disappears or becomes revoked after the provider confirms revocation; offline/provider-denied states do not count as zero active grants. Boundary tests distinguish the configured stale threshold and the headline count reconciles with its visible sources.

- [ ] **7.4 — Build team access reviews and delegation dashboard.** Needs: 7.1d, 7.2, 7.3b.
  **Build:** Show organization membership, collection/service-account rights, broker policies with review/owner attribution and revoke actions. Add external delegations only when 6.2 is implemented and permissioned. Keep unknown or unobservable provider access explicit instead of claiming to show everything that can act as a teammate.
  **Verify (V-7.4):** Dashboard rights reconcile with server authorization, revocation blocks the member's next broker request, and an access review produces an attributable decision without secret/entry-name telemetry. Personal external grants are not exposed to admins without explicit authority.

- [ ] **R.4 — Verify a real organization pilot.** Needs: 7.1d, 7.2, 7.3b, 7.4.
  **Build:** Run an authorized pilot organization's onboarding, least-privilege shared credential/env use, service-account approval, access review, ownership succession and employee departure. Review the new sharing/identity/broker boundaries independently before general enterprise availability.
  **Verify (V-R.4):** The organization completes the journey with retained evidence, an independent assessment of the new team boundaries and no unresolved cross-tenant or unauthorized-decryption finding; future access is denied after departure and downstream credential-rotation responsibilities are acknowledged. A working personal subscription or a dashboard screenshot cannot close the team pilot gate.

### Release announcements after the product gates

- [ ] **3.11 — Publish the app and hosted-release announcement.** Needs: R.3, 3.10b.
  **Build:** Prepare and publish version-correct product copy using retained R.1/R.3 evidence; distinguish the public local app, hosted service and still-unreleased expansion features.
  **Verify (V-3.11):** The live announcement links to working public install/signup flows and every capability claim has a released version and evidence; a draft is not a published announcement.

## Scale

Activate these tasks by recording the observed requirement and any narrower child tasks here.
They do not postpone controls already required for a pilot, consumer release or organization pilot.

- [ ] **S.1 — Expand service capacity from measurements.** Needs: R.3, H.9.
  **Build:** When measured storage, latency or write contention exceeds the declared budget, implement one demonstrated bottleneck fix; a move beyond the SQLite single-writer topology needs a recorded migration/rollback design.
  **Verify (V-S.1):** A representative load test meets the stated budget without bypassing authorization or losing a committed version; rollback/restore preserves accounts and ciphertext.

- [ ] **S.2 — Support managed desktop fleet deployment.** Needs: R.4, 4.7d.
  **Build:** When a pilot organization needs MDM deployment, package one named OS/management-system combination with enrollment, policy and update controls over the existing signed release.
  **Verify (V-S.2):** A managed device installs and upgrades through that system, applies only its authorized organization policy and retains user vaults on removal.

- [ ] **S.3 — Integrate one downstream credential lifecycle.** Needs: R.4.
  **Build:** When an organization names a provider and authority scope, implement one reviewed rotation or temporary-credential integration, with provider-side confirmation and rollback/failure handling. Keep this distinct from promising a general IAM/PAM platform.
  **Verify (V-S.3):** The provider invalidates the old test credential or expires the temporary grant as documented; denial/outage cannot falsely report rotation or revoke unrelated access.

- [ ] **S.4 — Expand support and incident capacity.** Needs: R.3, H.10.
  **Build:** When observed support volume or availability obligations exceed the staffed process, add a bounded escalation/on-call workflow using content-free service diagnostics.
  **Verify (V-S.4):** A drill reaches the assigned responder within the stated service objective and restores the supported workflow without requesting users' secrets.

- [ ] **S.5 — Produce requested enterprise assurance evidence.** Needs: R.4, H.10, 10.2.
  **Build:** When procurement requests a specific assurance, map implemented controls and evidence to that request; arrange qualified external assessment where required and report actual scope/period.
  **Verify (V-S.5):** Every answer or public assurance claim is backed by current evidence or a named gap; preparing a questionnaire never establishes an audit certification.

## Completion and ID continuity

A checked task has its bounded implementation or decision artifact, a passing verifier and an
evidence link identifying the tested version. Run the checks the change can break; secret paths
require behavioral tests. A documentation edit does not mark its described software complete.
The five transcript pages named in CLAUDE must pass verify-demo when affected; other docs need
implementation review. Tests of compiled code do not establish public installation or live service
operation: publication tasks must also satisfy RELEASE.

Record source, package, public and installation evidence separately. Gate R.1 closes the working
proposition, R.2 the managed pilot, R.3 consumer hosting, R.4 the organization pilot, and P.9 the
declared KeePassXC comparison. A gate's transitive Needs must all pass; further findings in its
acceptance journey keep it open even if its listed tasks were checked. Optional 1.5a remains a
documented unverified observation until it can be performed.

Keep every existing ID traceable. Completed legacy rows remain in Current status; these formerly
large open rows now select their first ready child when requested by their old ID:

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

An ID that still names an explicit row selects that row: historical 3.2b is the completed essay,
not a child of the future announcement. H-0015/H-0017 are signing enrollment, H-0018 payment,
H-0019 hosting, H-0011 the site's pre-deploy procedure, and H-0006/H-0007 public communication.
Prior decisions H-0002/H-0004/H-0008/H-0009/H-0012/H-0013/H-0014 remain answered in DECISIONS;
H-0010 was never issued. The preceding roadmap is retained in git; D-0090 supersedes its
MVP/Launch/Scale pricing-tier ordering.
