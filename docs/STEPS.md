# Build plan

This plan owns current status, build order and acceptance criteria. [PRODUCT](PRODUCT.md) owns scope, [FEATURES](FEATURES.md) the dated capability baseline, and [RELEASE](RELEASE.md) distribution requirements. [DECISIONS](../DECISIONS.md) D-0090 records the adopted structure and D-0176 the current order.

Only the next five steps are detailed. Later steps have one line with ID, name, Needs and purpose; their wording remains provisional until activated. On completion, reduce a step to one line under Completed steps and expand the next unchecked step.

## Current status

`0.3.0` publishes the local vault, the CLI/env workflow and the approval bridge on all four native targets, installed from clean runners and carrying no known defects; `0.2.0` and `0.1.0` stay served with their defects disclosed. The desktop is not publishable yet: its packages build, install, upgrade and uninstall without losing user data on runners, and 4.7c1 built the path that would publish them. 4.8 gave it vault creation in source and 4.9 gave it storing and replacing a secret somebody already has, both on rules it shares with the CLI, so a person can now keep a credential without a terminal. V.2a made that history readable and restorable in the core and V.2b put it on the entry pane, so an ordinary mistake can be recovered without KeePassXC. V.6 added word-list passphrases beside the character generator on both front ends, which is what 5.4a will protect a share with. Sharing, receiving, the drop relay and native approval are unimplemented, and F.15 records a documented unlock-screen shortcut that does nothing.

1.4a is the next ready code row, then 1.4b. Signing and publication (4.7c2) come last, gated on that desktop work and on the identity it names as its input. The macOS desktop app and all Apple signing are Expansion (D-0201). Closed rows keep their evidence in [Completed steps](#completed-steps); the reasoning is in [DECISIONS](../DECISIONS.md).

## Build order

| Milestone | Completion or activation |
|---|---|
| Working proposition | Signed, published Windows and Linux desktop apps and the CLI/MCP on all four current targets, in which a developer keeps, injects, shares and receives env and approves agents in a native dialog, with the hosted drop relay live and self-hostable; R.1 verifies it |
| Pilot ready | Invited small teams share production env through the hosted relay over an observed period, with restore by redeployment; R.2 verifies it |
| Paid release | The team plan: hosted directory, revocation, attribution, team audit and policy, with billing, support and independent review; R.3 verifies it |
| Expansion | Whole-vault managed sync, browser filling, importers, phone approval, the remaining KeePassXC baseline and the OIDC/SCIM tier |
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
| 3.2b | CLI launch essay | [essay](keepass-and-agents.md) |
| 3.4 | CLI/MCP pipeline and public archives — Published | [release inventory](RELEASE.md) |
| 3.6b | Windows binaries and MSI signed via a pinned dlib, rehearsed | Run 35174017853 at `3b30bd1`: payload and MSI accepted under the runner root with a Microsoft timestamp; a changed byte, a missing timestamp, a cert outside the root and an absent identity all refused. Run 35174026029 left the MSI NotSigned; D-0204 |
| 3.8 | Every CLI/MCP asset and its manifest are attested; the bundle is published with them | [verify-provenance.sh](../scripts/verify-provenance.sh) 14 cases; release run 34888642785 published 0.2.1-rc.2; logged-out `gh` accepted it and refused a changed byte, another repo and `ci.yml`; install run 34890637034 verified it; D-0138 |
| 4.1 | Desktop unlock and idle lock, source only | [session tests](../tests/Keypaste.App.Tests/Session/AppVaultSessionTests.cs) |
| 4.2 | Search, generated entries, env screens and lost-write protection, source only | [entry tests](../tests/Keypaste.App.Tests/ViewModels/EntriesViewModelTests.cs) |
| 4.7a | Windows and Linux desktop installer candidates, both declared in the checked definition | 4.7a1 and 4.7a3; the matrix gate refuses an undeclared or mislabelled package of either kind; macOS 4.7a2 is Expansion (D-0201) |
| 4.7a1 | Internal unsigned per-user Windows MSI | [verify-windows-installer.sh](../scripts/verify-windows-installer.sh); matrix gate 38 cases. Tag run 34905575518 at 3627d77: `0.2.1-rc.3` MSI add844d7, 227 files extracted byte-identical, `--selftest` passed, attestation verified; D-0139 |
| 4.7a3 | Internal unsigned Linux AppImage, pinned tool and runtime | [verify-linux-appimage.sh](../scripts/verify-linux-appimage.sh); matrix gate 46 cases, a changed pin refused. Tag run 35142539184 at e69208d: `0.3.1-rc.1` AppImage 8d695b80, 226 files unpacked byte-identical, `--selftest` passed, attestation verified; D-0203 |
| 4.7b | Internal MSI and AppImage installed and driven on fresh runners | install-desktop run 35249621134 at a147815 on `0.3.1-rc.2` app run 35247267811: all 8 checks pass on `windows-2025` and `ubuntu-24.04`. Refusals proved by 13 and 33 fixture cases only; approval at the terminal agent; no SmartScreen prompt met; D-0205 |
| 4.7c1 | The desktop publication path, built against the fakes | `app` gains an origin and its own manifest and bundle in the CLI's prefix; `--component` seams the three gates; an offered package is refused unsigned. [preflight](../scripts/verify-release-preflight.sh) 34, provenance 19, destination 26, matrix 76; D-0215 to D-0219 |
| 4.8 | Create a vault from the desktop, source only | Rules shared with `keypaste init` as [VaultCreation](../src/Keypaste.Core/VaultCreation.cs): [core](../tests/Keypaste.Core.Tests/VaultCreationTests.cs) 10, [create](../tests/Keypaste.App.Tests/ViewModels/UnlockCreateTests.cs) 12, consistency 3, D-0099 on both fields, `init` unchanged; D-0222 |
| 4.9 | Store and replace an existing secret from the desktop, source only | Four masked fields over one `SecretField`: [screens](../tests/Keypaste.App.Tests/ViewModels/ExistingSecretTests.cs) 19, [D-0099](../tests/Keypaste.App.Tests/Controls/SecretFieldAutomationTests.cs) 9 on all four, cli 7, paste 17, display 31; compat 2.7.10; D-0225, D-0226 |
| 4.7d | User data survives upgrade, a failed install, a refused downgrade and uninstall | upgrade-desktop run 35284227304 at `cfac582`: seven checks pass on `windows-2025` and `ubuntu-24.04`; the vault and its history, `app.toml`, `recent.toml`, `policy.toml` and the audit chain are byte-identical after every transition; D-0206 to D-0214 |
| V.2a | Read and restore entry history in core | One ordering rule behind `ReadHistory` and `RestoreRevision`: [core](../tests/Keypaste.Core.Tests/VaultHistoryTests.cs) 15, dropping the tiebreak fails 7 and the touch 1; [history gate](../scripts/verify-keepassxc-history.sh) on keepassxc-cli 2.7.10; D-0227 to D-0229 |
| V.2b | Restore an entry from the desktop, source only | History on the pane over V.2a's read: [screens](../tests/Keypaste.App.Tests/ViewModels/EntryHistoryTests.cs) 18, [drawn-value D-0099](../tests/Keypaste.App.Tests/Controls/HistoryRevealAutomationTests.cs) 6, cli 3 via `get --show`, hygiene 10; the gate keeps its driver; D-0230 to D-0233 |
| V.6 | Generate passphrases, source only | EFF's 7,776-word list vendored, embedded, pinned by `WordList.Digest` and refused on a mismatch: [list](../tests/Keypaste.Core.Tests/WordListTests.cs) 9, [generator](../tests/Keypaste.Core.Tests/PassphraseGeneratorTests.cs) 27, biased-and-fair pair 3, cli 53, app 21, consistency 1; D-0234 to D-0239 |
| F.1a | One entry identity for selection and mutation; an ambiguous one is refused | [identity tests](../tests/Keypaste.Core.Tests/EntryIdentityTests.cs), [write-back gate](../scripts/verify-keepassxc-writeback.sh); D-0091 |
| F.1b | Export refuses its source vault and any KDBX destination | [path rule tests](../tests/Keypaste.Core.Tests/PathIdentityTests.cs); D-0092 |
| F.1c | Cleanup is bound to the imported bytes and the path they came from | [snapshot tests](../tests/Keypaste.Core.Tests/SourceSnapshotTests.cs); D-0093 |
| F.1e | One resolver for reads and removals; an ambiguous path is refused | [verb tests](../tests/Keypaste.Cli.Tests/VerbTests.cs); D-0094 |
| F.2a | Launch arms the session and the palette from `app.toml` | [startup tests](../tests/Keypaste.App.Tests/StartupSettingsTests.cs); D-0095 |
| F.2b1 | Minimizing calls the same session lock as the timeout and `Ctrl/Cmd+L` | [minimize tests](../tests/Keypaste.App.Tests/MinimizeLockTests.cs); observed on Windows 10 Pro 19045, 2026-09-08; D-0096 |
| F.2b2 | Minimize-lock observed on macOS and Linux | observe-desktop run 35014816424 at `9b18750`, 2026-09-15: enabled, control, disabled and restart pass on `ubuntu-24.04` and `macos-15`, plus `Cmd+H`, none contradicting. A resting-pointer restore, a person's click and a real keystroke are unreached and belong to F.2b3; D-0199, D-0200 |
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
| F.8 | A refused listing says which refusal it was, and how long it took | [ListingCall](../tests/Keypaste.Mcp.Tests/ListingCall.cs) red 2 of 2, green 8 of 8. Probe run 34653284139, 12 of 80 on `windows-2025`: `no-approver` at 5834 ms against 500 ms with the listener up throughout, dispatched against a removed commit with tests unchanged; D-0124 |
| F.9 | A running approver is never reported absent for want of a free worker | [PoolShortageTests](../tests/Keypaste.Mcp.Tests/PoolShortageTests.cs) red then green for the connect and the harness pipes; run 34728679951: 0 of 80 bridge failures starved and as-found; D-0128, D-0129 |
| F.10a | A save's wait is split into check, gate, attempts and sleeps | [SaveTimingTests](../tests/Keypaste.Core.Tests/SaveTimingTests.cs) name a held gate's holder. Windows 10 19045, 30 suites: 55 of 60 doomed saves gate-dominated, first attempt ≤3 ms. pool-probe 34804202152 at 6016c88: 45 of 48, gate up to 5012 ms; D-0134 |
| F.10b | Only an attempt over an existing vault takes the save gate | [Regression](../tests/Keypaste.Core.Tests/SaveTimingTests.cs) red at 1287 ms of gate wait, green ungated; vendored rule pinned. pool-probe 34806033453 at 4b6a20b: 48 of 48 doomed saves ungated, first interval ≤108 ms; closes F.10, D-0136 |
| F.11 | Concurrent first vaults see one completely built KDF registry | [KdfFirstUseTests](../tests/Keypaste.Core.Tests/KdfFirstUseTests.cs): red 113 of 400 fresh contexts in the full suite, warmed control 0; green 0 of 400 after the repair, suite 0 failed of 1,235; KeePassXC 2.7.10 both ways; D-0133 |
| F.12 | A save sleeping between attempts holds no save gate | [Regressions](../tests/Keypaste.Core.Tests/SaveTimingTests.cs) red then green: gate wait 1113 ms to 0; an in-process commit reverted, then refused. pool-probe 34846842996 at 58be30c: no hold across a sleep in 2,088 saves, 96 gated twice or more; D-0137 |
| F.13 | A window restored under a resting pointer is not activity | [Regressions](../tests/Keypaste.App.Tests/ActivityWatchTests.cs) red 2 of 2 then green, movement still defers. Windows 10 Pro 19045, 2026-09-15: the `disabled` contradiction passes, no touch at restore, lock 11 ms late, the other three pass, dispatch pending; D-0202 |
| F.14 | A failed upgrade leaves the previous version installed | `MajorUpgrade` scheduled `afterInstallFinalize` (D-0214). interrupt-probe 35278766000 left nothing installed; 35284216120 at `cfac582` fails the same way at 1603 with `0.0.1-upgrade` registered and runnable, and 4.7d's interrupted check passes with the data identical |
| R.0a | One checked release definition drives both workflows and the download pages | [release-targets.json](../release-targets.json); [verify-release-matrix.sh](../scripts/verify-release-matrix.sh) refuses 31 cases; the prerelease suffix reached all three desktop targets on tag run 34542636558; D-0108 to D-0112 |
| R.0b | A release is complete only when the public bytes say so | [release-completion.sh](../scripts/release-completion.sh) recorded 0.2.0 and verified all 11 assets anonymously at the origin, run 34549357893; six fixtures in [verify-release-completion.sh](../scripts/verify-release-completion.sh); D-0116 |
| R.0c | 0.2.0 published and installed on all four native targets from clean runners | release run 34549357893; install runs 34549933842 and 34551580519, the second running README verbatim, each target creating a vault and injecting into a child. macOS and Windows floors stay `cited`: the runners sit above them, not on them |
| R.0d | What the advertised `v0.1.0` gets wrong is named where it is downloaded | [matrix gate](../scripts/verify-release-matrix.sh) holds three pages to `known_defects` both ways, at the checked-out ref; the [site check](../scripts/verify-site-disclosure.sh) asks the origin by hand, never from a workflow; D-0117, D-0127 |
| R.0e | keypaste.com deploys from a path that has actually run | Cloudflare Git build `1716bdb6` from `309aac3` deployed `1fcc038c`; the origin passes [both](../scripts/verify-site-disclosure.sh) [checks](../scripts/verify-site-endpoint.sh) and serves the ref byte-for-byte. `site.yml` deleted after four failed runs; D-0121, D-0127 |
| R.0f | 0.3.0 published with the F repairs, and the pages moved off `0.2.0`'s defects | release run 35046281003 at `307bb54`; [verify-provenance.sh](../scripts/verify-provenance.sh) confirmed the manifest and all 11 assets anonymously at the origin. Install run 35047059579 passed all four targets; D-0195 |
| 10.1 | Initial hostile review and remediation | D-0084 in [DECISIONS](../DECISIONS.md) |
| K.1 | Pinned SDK installed | [global.json](../global.json), D-0076 |

The [current release matrix](RELEASE.md#current-distribution--2026-09-07) owns public availability: a completed source step does not mean the behavior is in the current download.

## Working proposition

A developer can install the signed Windows or Linux desktop app and the CLI/MCP, which ships for Windows, macOS and Linux, create a vault, store an existing secret, keep and inject a project's env, share an env set and receive one, approve an agent request in a native dialog, recover an ordinary mistake and read the audit, with the hosted drop relay live and self-hostable. Build through the shared core; desktop, CLI and relay steps name their surfaces. No account is required. The macOS desktop app, its packaging and all macOS signing are Expansion work (D-0201); the macOS CLI stays advertised, unsigned and un-notarized as disclosed. R.1 closes the milestone only after the published product passes this journey.

### Share and receive

- [ ] **1.4a — Specify merge and deletion semantics.** Needs: 0.2.
  **Build:** a document in `docs/` states keypaste's merge semantics, which are KeePassXC 2.7.x's (D-0157, law 4.6): entries match by UUID and never by title or path; the revision with the later modification time wins and the one it displaces becomes a history item rather than being dropped; a `DeletedObjects` tombstone removes an entry when the tombstone is newer than the entry it names, and loses to a later edit; a move resolves by `LocationChanged`; and a tie on modification time is decided one stated way rather than by whichever file was read first. It states what is not merged — the master key, KDF parameters and recycle-bin settings — and that a field-level three-way merge is impossible because KDBX stores no common base. Each rule names the `keepassxc-cli merge` fixture that establishes it. No merge code ships in this step; 1.4b implements what this specifies.
  **Verify (V-1.4a):** a fixture pair per rule is built by writing two divergent copies of one vault and merging them with `keepassxc-cli merge`, and a test reads each result and asserts the stated outcome — the surviving value, the history item it displaced, the tombstone that applied and the one that did not, and the group each moved entry landed in. The same tests fail against a written-down rule that contradicts its own fixture, so the document cannot drift from the tool it claims to follow; the KeePassXC version that produced every fixture is recorded beside them.
- [ ] **1.4b — Implement atomic entry-level merge.** Needs: 1.4a.
  **Build:** `Keypaste.Core` gains a merge of one KDBX file into an open vault under the rules 1.4a wrote down, matching entries by UUID and never by title or path. A merge answers a preview first — every entry it would add, change or delete, named, with no value in it and nothing written — and applies only when it is asked to. Each revision it displaces becomes a history item through the same path a restore uses (V.2a, D-0014), and an older revision never overwrites a newer one. The whole merge commits through one `Save` or not at all, so a refused or failed merge leaves the vault as it was. No CLI or desktop surface is added; 1.4c owns both.
  **Verify (V-1.4b):** core tests run 1.4a's fixture pairs through the merge and reach the outcome that document states for each rule, including the tie it decides; a preview of every fixture names the same entries the merge then changes and writes nothing, checked against the file's bytes; a merge that would replace a newer revision with an older one leaves the newer one current and the older one in history; a source entry whose UUID the vault does not hold is added, and one the vault deleted later is not resurrected; a merge abandoned part-way leaves the vault byte-identical; and the merged vault opens in KeePassXC through the compatibility gate with every surviving revision in its history.
- [ ] **1.4c — Receive a share through the CLI and desktop.** Needs: 1.4b, 4.2.
  **Build:** `keypaste receive <file>` takes a share file and its passphrase, prints 1.4b's preview — every entry it would add, change or delete, named, with no value in it — and merges only when confirmed. A desktop receive screen shows the same preview over the same core call and merges on the same confirmation. A conflict is named and resolved by 1.4a's rules, so neither revision is lost: the displaced one becomes a history item, which V.2b's pane can restore. Each entry whose expiry has passed is warned on in both surfaces, with the statement that expiry cannot recall a downloaded copy and revocation means rotating the credential (D-0186). The file comes from the filesystem; 5.4b adds the relay drop.
  **Verify (V-1.4c):** CLI tests receive 1.4a's fixture pairs and reach the outcome that document states for each, with the preview naming the same entries the merge then changes and the vault byte-identical until the confirmation; a refused confirmation and a wrong passphrase each write nothing and print no entry name; an expired entry prints the warning and is still merged; headless tests drive the desktop screen to the same preview, the same refusal on cancel and the same conflict report; a consistency test receives one file through each surface and reaches the same vault, read back through `keypaste ls` and `get --show`; and the merged vault opens in KeePassXC through the compatibility gate.
- [ ] **5.4a — Create an encrypted share file.** Needs: V.6, 4.2.
  **Build:** `keypaste share` and a desktop share action write a KDBX4 file holding only the chosen entries or env set, protected by a six-word passphrase V.6's generator produces, to a local path and with no relay (law 4.1). The passphrase is printed once, on its own, with the instruction to send it by another channel; nothing stores it. `--expires` sets each named entry's expiry, stated beside the fact that a downloaded copy cannot be recalled and revocation means rotating the credential (D-0186). The source vault is opened read-only and is not modified; 5.4b owns the relay drop and 1.4c the receiving side.
  **Verify (V-5.4a):** CLI tests show the written file holds exactly the chosen entries and no others, that it opens with the printed passphrase and not with the source vault's master password, and that the source vault is byte-identical afterwards; the passphrase is six words from the pinned list and appears on stdout once and in no file; `--expires` sets the expiry each entry carries when read back and prints the recall warning; a destination that exists is refused without `--force` and a destination inside the source vault's path is refused outright; headless tests drive the desktop action to the same file through the same core call, and a consistency test shares through each surface and reaches files whose entries read back the same; the share opens in KeePassXC through the compatibility gate.
- [ ] **5.4b — Share and receive through a relay drop.** Needs: 5.4a, 5.2b, 1.4c. — Upload a share file to a drop, optionally deleted on first download, and print its link with the instruction to send the passphrase by another channel; `keypaste receive <link>` and the desktop fetch the drop and hand it to 1.4c. Works against the hosted and a self-hosted relay; version 1 shares are unsigned (D-0187).
- [ ] **E.1 — Finish the environment workflow.** Needs: 4.2. — Map a project to an env set and run it from the app, with no plaintext file written. `keypaste run` and the app refuse to inject an expired value, naming the entry and stating that expiry cannot recall a copy already shared and revocation means rotating the credential.

### Drop relay

- [ ] **5.2a — Build the drop relay executable and storage contract.** Needs: none. — `Keypaste.Relay` as one NativeAOT binary over SQLite and S3-compatible object storage, with no vault-decryption dependency (D-0064). A drop is sealed bytes the relay cannot read under an unguessable ID, at most 1 MB and kept at most 7 days, optionally deleted on first download, with no account.
- [ ] **5.2b — Serve drops within their limits.** Needs: 5.2a. — Upload and download endpoints that refuse a drop over 1 MB, expire drops at 7 days, delete on first download when asked, rate-limit each client and log no drop contents (D-0181).
- [ ] **5.2c — Publish and exercise the self-hosted relay.** Needs: 5.2b. — The same binary an operator can install and upgrade on a clean host without a Stripe account, with a drop uploaded and downloaded through it (D-0180).
- [ ] **H.7 — Complete hosting owner enrollment (H-0019).** Human. Needs: none. — Obtain the relay host, bucket, relay domain and mail account; external accounts required.
- [ ] **H.8 — Deploy the hosted drop relay.** Needs: 5.2b. — **Input:** H.7's host SSH access, bucket keys and the relay domain's DNS record. Deploy the relay build behind TLS with its free drop limits (PRODUCT §5.8), rollback and recorded deployment commands; until the input exists, those commands are verified against a local Docker host (D-0165, D-0185).
- [ ] **5.7 — Publish usable share, receive and relay operator instructions.** Needs: 5.2c, 5.4b. — `docs/share.md` and `docs/relay.md`, keeping [THREATS](../THREATS.md) T-26 true for the shipped behavior (D-0188).

### Native approvals

- [ ] **8.2a — Specify the shared approval interaction.** Needs: 2.2. — The shipped terminal prompt is the specification every approval surface renders: its fields, the line saying the reason was written by the agent, D-0084's sanitising and D-0027's two refusals, with no surface-specific variant (D-0158).
- [ ] **4.3a — Add the authenticated desktop approval channel.** Needs: 2.2, 8.2a. — Let the desktop answer a request over an authenticated local channel, with the agent still the authority.
- [ ] **4.4 — Render native approval and denial.** Needs: 4.3a, 8.2a. — A native prompt with terminal fallback, where default, timeout and dismissal all deny.

### Sign and publish the desktop

The packages themselves are finished: 4.7a declared them, 4.7b installed and drove them on fresh runners, 4.7d proved upgrade and uninstall keep user data, and 4.7c1 built the path that publishes them. What is left is signing and the release itself, and it comes last on purpose — an installer nobody can yet use is not worth signing. A request for a signed package fails closed when its identity is missing (3.6b); an artifact establishes Packaged only, and public distribution with its installation evidence is 4.7c2.

- [ ] **4.7c2 — Publish and verify signed desktop and CLI downloads.** Needs: 4.7c1. Ships after: 4.8, 4.9, E.1, 4.4, V.2b. — **Input:** a Microsoft Artifact Signing identity as keypaste, or an OV certificate in a cloud HSM only if Artifact Signing refuses eligibility (D-0143), repository-scoped with no key retained beyond a job, set as the five repository variables `sign-windows.sh` reads: endpoint, account, certificate profile, client ID and tenant ID. Enrolment alone is not the gate: on `windows-2025` the identity must timestamp a throwaway binary, verify with `signtool verify /pa /v` and refuse a changed byte, with issuer, timestamp authority and expiry retained (H-0017, D-0221). Flip the definition to `authenticode` with `internal: false`, `signed: true` and `-internal-unsigned` dropped from both patterns, tag, publish to permanent public URLs, and record the tag, commit, hashes, URLs and signing identity. The gates it ships behind are both halves of readiness: an identity to sign with, and a desktop a user can actually use once installed — a published installer that cannot create a vault is not a release. 3.6b, 4.7b, 4.7d and F.2b2 were gates too and have passed; their evidence rows keep them. Nothing publishes before them; 4.7c1's path already refuses an unsigned package, so the definition turns this on rather than a remembered edit.
  **Verify (V-4.7c2):** the public run records the tag, commit, hashes, URLs and signing identity, and `verify-provenance.sh --component app` passes anonymously at the origin. It is also the first observation of five things only a tag can answer, each of which the fakes define rather than witness: that `actions/download-artifact` accepts `run-id` alongside `pattern` and `merge-multiple`, failing which the step becomes one download per RID; that `attestations: write` admits the read `gh attestation verify` makes, failing which the job declares `attestations: read` beside it; that GitHub answers a tag-push run with `head_branch` set to the tag, which is what `--run-id` matches on; that two `attest-build-provenance` steps in one job return two distinct `bundle-path` outputs, which is why each bundle is copied straight after its own step; and that `gh attestation verify` accepts `release.yml` as `--signer-workflow` for bytes `app.yml` compiled. Record each as observed or contradicted; a contradiction opens its own row. The first real-release upgrade is a dated observation here.

### Product acceptance

- [ ] **R.1 — Verify the working share-and-approve product.** Needs: 4.7c2, 4.8, 4.9, E.1, 5.4a, 5.4b, 1.4c, 4.4, V.2b, 2.4, H.8, 5.2c, 5.7. — **Milestone gate:** on public downloads, with the Windows and Linux desktop apps signed and the CLI/MCP on all four current targets, a developer creates a vault, stores an existing secret, keeps and injects a project's env, shares an env set through the hosted drop relay and receives one, approves an agent request in the native dialog, recovers a mistake from entry history and reads the audit; the same share also passes through a self-hosted relay (D-0177).

## Pilot ready

The pilot uses the published desktop app and CLI. Invited small teams share production env through the hosted drop relay over an observed period, and the operator restores the service by redeploying it. Preparation can start earlier, but no external pilot begins before R.1 and the controls below pass.

### Service operation and pilot gate

- [ ] **H.9 — Prove restore by redeployment, outage handling and operational alerts.** Needs: H.8. — Rebuild the service from its recorded commands on a fresh host inside the declared objectives; drops in flight are lost and senders share again, and monitoring stays content-free.
- [ ] **H.10 — Establish support, privacy and incident procedures.** Human. Needs: none. — A real support channel, published terms and a rehearsed incident notice before any external user is invited.
- [ ] **R.2 — Verify the small-team pilot and close the beta milestone.** Needs: H.9. Ships after: R.1, H.10. — **Milestone gate:** invited small teams share production env through the hosted relay and receive it by merge over an observed period recorded with its dates, and an operator restores the service by redeployment, with no data-loss or disclosure defect left (D-0178).

## Paid release

Sell the team plan only after the pilot works. Local functions, security, signatures, self-hosted operation, sealing and signing shares with locally generated keys, and hosted drops within their free limits stay available without a subscription. The team plan sells the hosted directory, revocation, attribution, team audit and policy, and support.

### Team plan accounts and devices

- [ ] **H.1 — Settle hosted authentication and key boundaries.** Needs: none. — Write down account auth, device authorization, vault unlock, recovery and revocation as separate protocols, checked against PRODUCT §3, within D-0162: the account password is verified server-side with Argon2id and never derives the vault key, MFA is WebAuthn or TOTP, and each device holds its own keypair.
- [ ] **H.2 — Implement account registration and authentication.** Needs: H.1, 5.2a. — Signup, login and account-password reset that never touch vault-unlock material.
- [ ] **H.3 — Implement account MFA and session revocation.** Needs: H.2. — An MFA factor with recovery codes, plus a session list and revoke-all.
- [ ] **H.5a — Define recoverable and unrecoverable account failures.** Needs: H.1. — Document what a lost password, MFA, device or vault secret costs under D-0163: the account recovers by email plus recovery codes, the vault secret is unrecoverable unless the user configured a recovery keyfile, and support never restores vault access.
- [ ] **H.4 — Implement trusted-device authorization.** Needs: H.2, H.5a. — Per-device service keys, explicit first-device bootstrap and revocation that stops the next fetch.
- [ ] **H.5b — Implement and rehearse the approved recovery flow.** Needs: H.5a, H.4. — Build only the approved recovery mechanism, and test each documented unrecoverable case.
- [ ] **H.6 — Implement account export and deletion.** Needs: H.2. — Export an account's data and delete it on a published schedule, removing its directory membership and leaving local files intact (D-0184).

### Team sharing and governance

- [ ] **7.1a — Settle team ownership, sealing and sharing authority.** Needs: H.1. — Decide who owns a team directory, who can revoke and attribute, and who can recover, reviewed against §3 before any key-envelope design; keys stay generated locally and the directory only publishes them.
- [ ] **7.1b — Implement the hosted team directory.** Needs: 7.1a, H.2. — Accounts as members: invitations, roles and service accounts, default-deny, publishing each member's locally generated public key, with opaque resource IDs in events (D-0189).
- [ ] **7.1c — Seal and sign shares with locally generated keys.** Needs: H.4, 5.4b. — Generate a keypair locally, seal a share to a recipient's public key and sign it with the sender's key through mature libraries, with keys exchanged by any means, including a self-hosted relay, and no subscription; a signature the receiver cannot verify is refused. Free when it ships (PRODUCT §5.4, D-0190).
- [ ] **7.2 — Revoke, attribute and govern team shares and approvals.** Needs: 7.1b, 7.1c. — Links a sender or admin can revoke before download, every team share and agent approval attributed to a member or service account and recorded in team audit, and team policy refused at the next request after removal; a downloaded copy stays readable and rotation is the revocation (D-0191).

### Billing and review

- [ ] **5.5a — Complete payment owner enrollment (H-0018).** Human. Needs: none. — Activate Stripe and supply scoped credentials; external account required.
- [ ] **5.5b — Implement hosted subscription entitlement.** Needs: H.2. — **Input:** 5.5a's restricted live key and webhook signing secret, needed only for live mode. Checkout, verified idempotent webhooks and relay-side entitlement, with local functionality independent of payment state, verified against stripe-mock until the input exists (D-0167).
- [ ] **5.5c — Implement cancellation and failed-payment behavior.** Needs: 5.5b, H.6. — Plan state, grace dates and a working export route before any hosted data is deleted.
- [ ] **5.8 — Prepare reviewed plans and controlled checkout.** Needs: 5.5c. — Plans tied to real entitlements, with live checkout restricted until R.3. Every price stores `origin: draft`; a failing test refuses a draft price reaching Stripe live mode, the site or checkout, and the row's status reads built on drafts, review pending, never done. Owner approval of prices is an R.3 release-checklist item (D-0168).
- [ ] **10.2 — Complete independent hosted and team-boundary review.** Human. Needs: 7.2, H.5b, 5.5c, H.8. — An external review of the drop relay, accounts, recovery, sealing and signing, revocation, bridge and billing; unresolved critical findings block paid release (D-0192).
- [ ] **R.3 — Verify the paid team release.** Needs: H.3, H.5b, H.6, 7.2, 5.5c, 5.8, 10.2. Ships after: R.2. — **Milestone gate:** independent small teams subscribe, publish keys in the hosted directory, send sealed and signed shares, revoke a link, read attributed team audit under policy, reach support, then cancel and export, on public distributions. Its release checklist also requires owner-approved 5.8 prices, with no `origin: draft` remaining (D-0179).

## Expansion

Each step follows its own Needs; publication follows its Ships after gates. Completing the team-plan gate does not claim these features are available.

### Repository release protections

- [ ] **K.4 — Merge to main only through a pull request whose runner-only checks passed.** Needs: 3.0. — An empty-bypass ruleset requiring a pull request and only the checks `verify.sh` cannot run locally, unfiltered so records-only pull requests merge, with merge commits keeping the keypaste author and trailer; amends CLAUDE.md's merge-locally route.
- [ ] **K.5 — Observe fork pull-request CI.** Needs: 3.0. — **Input:** one pull request number from a fork owned by an account other than `notinferred` or the `keypaste` organization. Record its runs under the `first_time_contributors` policy, and add a test refusing `pull_request_target` or a secret in any pull-request-triggered workflow (D-0153).

### Local vault depth

- [ ] **V.1a — Support keyfiles in core and CLI unlock.** Needs: 0.2. — Supported KDBX keyfile forms for create, open and change-credentials.
- [ ] **V.1b — Expose vault credentials in the app.** Needs: V.1a, 4.8. — Choose a keyfile and change the master password from the desktop, with the loss consequences stated.
- [ ] **V.3a — Implement reversible deletion.** Needs: 0.2. — Soft delete and restore through the KeePass recycle bin, with permanent delete kept separate.
- [ ] **V.3b — Add trash and recovery controls.** Needs: V.3a, 4.2. — Recover an accidental deletion without a terminal.
- [ ] **V.4a — Keep recoverable encrypted backups.** Needs: 0.2. — Versioned encrypted backups around each save, so an interrupted write cannot destroy the last good copy.
- [ ] **V.4b — Restore and export a complete vault.** Needs: V.4a, 4.8. — Restore a backup or export the whole vault, with the replaced file kept until it succeeds.
- [ ] **V.5a — Add stable organization operations.** Needs: 0.2. — Group and entry create, rename, move, clone, tag and expiry through the core, with UUIDs stable across moves.
- [ ] **V.5b — Add organization and richer search to the app.** Needs: V.5a, 4.2. — Field, tag and expiry filters, and controls for the new core operations, cleared on lock.
- [ ] **V.7 — Manage custom fields.** Needs: 4.9. — Add, edit and remove named custom fields and keep their protected flags.
- [ ] **V.8a — Expose bounded attachment operations.** Needs: 0.2. — Enumerate, import, remove and read attachments in memory, with size limits and no plaintext temporary files.
- [ ] **V.8b — Manage attachments from the app.** Needs: V.8a, 4.9. — Attachment controls that never auto-open or execute a file.
- [ ] **V.9 — Report local password health.** Needs: V.5a, 4.2. — Find weak, reused and expired credentials locally, with no network request.
- [ ] **4.10a — Add Windows quick unlock.** Needs: V.1b. — Optional: resume a local session with Windows authentication, keeping the full unlock path intact. Required later by P.9.

### Importers and TOTP

- [ ] **9.1a — Define a loss-aware import pipeline.** Needs: V.8a. — One import contract with preview, collision decisions and atomic commit, so nothing is dropped silently.
- [ ] **9.1b — Import Bitwarden JSON.** Needs: 9.1a. — The supported Bitwarden export forms as one adapter.
- [ ] **9.1c — Import LastPass CSV.** Needs: 9.1a. — The LastPass CSV adapter, including quoted multiline fields and folders.
- [ ] **9.1d1 — Import 1Password 1PUX.** Needs: 9.1a. — The 1PUX adapter, preserving supported attachments and TOTP.
- [ ] **9.1d2 — Import 1Password CSV.** Needs: 9.1a. — The documented 1Password CSV subset.
- [ ] **9.1e — Import KeePassXC CSV and open existing KDBX.** Needs: 9.1a. — CSV mapping plus onboarding an existing KDBX without touching the original.
- [ ] **9.1f — Guide import in the desktop.** Needs: 9.1a, 4.8. — Source selection, preview, conflict choices and a result report, so a new user migrates without a terminal.
- [ ] **9.2a — Implement interoperable TOTP.** Needs: 0.2. — Read and calculate KeePassXC otp attributes, keeping the seed protected.
- [ ] **9.2b — Use TOTP in the app and approval bridge.** Needs: 9.2a, 4.2, 2.2. — Show the code and countdown, and let an agent be approved for an otp field but never the seed.

### Desktop depth and product checks

- [ ] **4.5 — Define and measure the daily-use tasks.** Needs: 4.2. — `docs/ux.md` with numeric thresholds for create, find, copy, restore, inject, approve, deny and fill, measured by automated step and keystroke counts. Every threshold stores `origin: draft`; a failing test refuses a draft threshold deciding any gate or published usability claim, and the row's status reads built on drafts, review pending, never done. Review precedes the first gate or published claim that relies on a threshold (D-0156, D-0194).
- [ ] **4.3b — Show live agent activity and effective controls.** Needs: 4.3a. — Pending requests, audit history, live counts and per-client pause; an absent agent reads unavailable, not zero.
- [ ] **4.4b — Start and stop the approver from a user action.** Needs: 4.4. — Run one approval end to end without a terminal, with unlock input owned by the approver process.
- [ ] **F.2b3 — Record minimize-lock on real macOS and Linux desktops.** Needs: F.2b2. — **Input:** one record per OS from a real macOS machine and a real Linux X11/XWayland desktop, entered in this row, covering only what a runner cannot observe: the window manager's own minimize control and, on macOS, `Cmd+H` in a logged-in session. A contradiction from either source opens its own F row against [MinimizeLock](../src/Keypaste.App/MinimizeLock.cs) (D-0200).
- [ ] **F.15 — The unlock screen answers no keyboard shortcut.** Needs: none. — [desktop.md](desktop.md) states `Ctrl/Cmd+O` opens a vault, in the prose and in the keyboard table, and no such binding exists: `App.OnShortcut` returns before reading a key whenever the shell is null, which is exactly the unlock screen, so every chord is inert there. Found while building 4.8 and left alone by it. Bind the documented shortcut on the unlock screen, or delete the claim; either way the two must agree. **Verify (V-F.15):** a headless test drives `Ctrl/Cmd+O` on the unlock screen and fails on today's build, then passes; a second test holds `desktop.md`'s keyboard table to the chords the app answers, so a row with no binding fails.
- [ ] **4.6 — Exercise actual desktop rendering.** Needs: F.2d. — Headless Skia render tests that go red if a typed character ever appears on screen or in the automation tree.
- [ ] **9.4 — Publish a versioned compatibility result.** Needs: V.1a, V.8a, 9.1a, 9.2a, 1.4b. Ships after: V.1b, V.7, V.8b, 9.1f, 9.2b, 1.4c. — Extend both KeePassXC gate directions over the finished workflows and record the upstream version tested.
- [ ] **3.10a — Ship the local product guides.** Needs: 4.2. — Version-correct guides for every shipped screen, linked from the app, and a test that fails when a screen has no guide, so each later screen's row brings its own (D-0161).
- [ ] **1.5a — Observe Windows clipboard history behavior.** Needs: 1.5b. — Optional: prove on a real machine that a keypaste secret never reaches clipboard history; a named residual, not a blocker.

### macOS desktop

These rows carry the macOS desktop app, its packaging and all Apple signing, moved from Working proposition (D-0201). The macOS CLI stays advertised, unsigned and un-notarized as disclosed, until 3.5b.

- [ ] **4.7a2 — Package an internal macOS bundle and DMG.** Needs: R.0a, F.4b.
  **Build:** extend `app.yml`'s `osx-arm64` job to assemble `Keypaste.app` with an `Info.plist` carrying the full version and keypaste identity, and place it in a DMG made with `hdiutil`, which ships with macOS and mounts read-only without running anything, so it passes D-0140's test with no pin. Declare both in [release-targets.json](../release-targets.json), preserve the prerelease suffix and label the artifact internal and unnotarized. Signing and notarization (3.5b) and installation (4.7b) stay out of scope.
  **Verify (V-4.7a2):** a candidate-tag run uploads a DMG whose bundle, mounted with `hdiutil attach -readonly -nobrowse`, reports the definition's version and publisher, and whose executable passes `--selftest` from inside the bundle; `verify-release-matrix.sh` refuses an undeclared or mislabelled macOS package. The publisher check reads only Info.plist strings this step writes; proof of the publisher is 3.5b's signature.
- [ ] **3.5a — Enable the macOS signing identity (H-0015).** Human. Needs: none. — External Apple Developer enrollment as keypaste; repository-scoped Developer ID Application and notarization credentials; a runner identifies the certificate and signs, notarizes and staples a throwaway binary, retaining team ID and expiry. Enrollment alone does not complete 3.5b.
- [ ] **3.5b — Sign and notarize macOS release payloads.** Needs: 4.7a2. — **Input:** 3.5a's Developer ID Application `.p12` and its password, an App Store Connect API key (`.p8`, key ID, issuer ID) and the team ID, as repository secrets. Sign, notarize and staple the app bundle and the CLI/MCP binaries `release.yml` publishes, and fail closed when the identity is absent. Until the input exists the verifier runs on an ad-hoc `codesign -s -` identity and a fake `xcrun notarytool` and `stapler`, and nothing records the payload as signed (D-0145).
- [ ] **4.7e — Install, preserve and publish the macOS desktop app.** Needs: 4.7a2. Ships after: 3.5b. — The macOS clauses of 4.7b, 4.7d and 4.7c2 (D-0201): install the candidate and complete its first render, vault operations, env run and approval; prove upgrade and uninstall keep user data; and build the publication path, including the Homebrew cask generated from the published DMG and its hash, then put the signed and notarized app at a permanent public URL.
- [ ] **4.10b — Add macOS quick unlock.** Needs: V.1b. — Optional: the macOS equivalent under the same session policy. Required later by P.9.

### Browser integration and publication

- [ ] **8.1 — Install and pair the native messaging host.** Needs: 2.2. — A vault-free native host over the local approver, paired to specific extension identities.
- [ ] **8.3a — Fill a login with verified origin matching.** Needs: 8.1. — Fill only on the real registrable domain, and refuse lookalikes and cross-origin frames.
- [ ] **8.3b — Save a new browser credential.** Needs: 8.3a. — Explicit save preview, so page content cannot write to the vault by itself.
- [ ] **8.3c — Update an existing browser credential.** Needs: 8.3b. — Update the right entry on a password change and keep the old value in history.
- [ ] **8.3d — Generate a browser credential.** Needs: 8.3b. — The core generator in the extension, saved only after confirmation.
- [ ] **8.3e — Fill supported TOTP and custom fields.** Needs: 8.3a, 9.2a. — Fill OTP codes and mapped extra fields without exposing a seed to the page.
- [ ] **8.2b — Verify approval consistency across surfaces.** Needs: 4.3b, 4.4, 8.3a. — Run the same hostile approval fixtures on terminal, native prompt, activity view and browser.
- [ ] **8.4a — Prepare browser store identities and distributables.** Needs: R.0a, 8.1. — **Input:** a Chrome Web Store developer registration and AMO API credentials, as repository secrets. Stable extension identities (a Chrome key pair and a gecko ID) bound to the native-host allowlist, and each store's package, are built without either account; only store upload waits on the input (D-0159).
- [ ] **8.4b — Publish and test browser store installations.** Needs: 8.4a. Ships after: 4.7c2, 8.3a, 8.3b, 8.3c, 8.3d, 8.3e. — Submit, then install from the live public listing on every promised browser and platform. The submission and install checks run first on a self-distributed signed XPI and an unpacked CRX; each store's approval is recorded as a dated observation (D-0160).

### Managed sync and phone

- [ ] **5.3a — Implement recoverable client synchronization.** Needs: 1.4b, 5.2d. — `keypaste sync`: pull, merge, push, and stay recoverable if killed at any write boundary.
- [ ] **5.3b — Build managed desktop onboarding.** Needs: H.4, 5.3a, 4.8. — Sign up and get a synced vault without choosing a file path, with local-only use still working offline.
- [ ] **5.3c — Expose device, sync and conflict controls.** Needs: 5.3a, H.3, H.4. — Device list, second-device enrollment, revoke, sync state and version restore in the app.
- [ ] **5.3d — Publish the hosted-capable clients.** Needs: 5.3b, 5.3c, H.5b, H.6, H.8. Ships after: R.1. — Ship signed CLI and desktop packages containing onboarding, sync, recovery and exit, verified against the deployed relay, with `docs/sync.md` (D-0183).
- [ ] **M.1 — Select and prove the supported phone approach.** Needs: none. — Prototype Avalonia mobile over `Keypaste.Core`, with .NET MAUI as the fallback only if autofill-extension memory limits fail (D-0169), on the `macos-15` iOS simulator and an Android emulator; the run on real iOS and Android devices is recorded as a dated observation (D-0170).
- [ ] **M.2a — Connect and lock the selected phone client.** Needs: M.1, H.4. — Enroll a real phone and open the vault locally, with revocation that works.
- [ ] **M.2b — Add phone login use and autofill.** Needs: M.2a. — Fill and save a login on each promised phone OS.
- [ ] **M.2c — Add phone sync, recovery and export controls.** Needs: M.2a, 5.3a, H.5b. — Converge phone and desktop after offline edits, and restore or export from the phone.
- [ ] **M.3 — Publish and verify phone availability.** Needs: M.2b, M.2c. Ships after: R.2. — Publish through the advertised public channel and verify a new user can install and upgrade.

### Define coverage before claiming a complete password manager

- [ ] **P.0 — Enumerate the complete versioned parity contract.** Needs: none. — One contract for KeePassXC 2.7.12 derived from the `docs/topics` tree at the recorded tag SHA, each behavior with a stable ID, source path, disposition stored as `origin: draft`, platform scope and proving row; a test refuses coverage or parity claims while a draft remains, and review is a P.9 release-checklist item (D-0154, D-0193).

### Complete the KeePassXC behavior baseline

Accepted scope that can run alongside hosted work. P.0 prepares the behavior contract; P.9 forbids a parity claim while any promised behavior is unverified.

- [ ] **P.1 — Support hardware-key challenge response.** Needs: P.0, V.1b. — Unlock with a supported hardware key, kept separate from online account MFA; verified against an emulated challenge-response fixture, with the run on a real key recorded as a dated observation (D-0175).
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
- [ ] **P.9 — Verify complete KeePassXC coverage for the declared baseline.** Needs: P.0 with no `origin: draft` remaining, P.1, P.2, P.3a, P.3b, 8.5b, P.4a, P.4b, 9.3b, P.5, P.6, P.7a, P.7b, P.8, 4.10a, 4.10b. Ships after: R.1. — **Comparison gate:** every P.0 behavior has passing evidence on a public keypaste version.

### Distribution expansion

- [ ] **3.7a — Publish Homebrew installation and updates.** Needs: R.0b. Ships after: R.1. — A CLI formula generated from completed release manifests; the desktop cask belongs to 4.7e (D-0149, D-0201).
- [ ] **3.7b — Publish Scoop installation and updates.** Needs: R.0b. Ships after: R.1. — A Scoop bucket entry for the Windows payloads.
- [ ] **3.7c — Publish winget installation and updates.** Needs: R.0b. Ships after: R.1. — winget manifests with stable publisher identity; closes O-0011 with its siblings.
- [ ] **3.9a — Add macOS Intel downloads.** Needs: 4.7a2. Ships after: 3.5b, R.1. — Native `osx-x64` build, package, signing and install evidence.
- [ ] **3.9b — Add Windows ARM64 downloads.** Needs: 4.7a1. Ships after: R.1. — Native `win-arm64` build and packages; emulation does not count.
- [ ] **3.9c — Add Linux musl CLI/MCP downloads.** Needs: R.0a. Ships after: R.1. — A `linux-musl-x64` toolchain and install proof on a clean musl distribution.
- [ ] **3.9d — Add Linux ARM64 desktop downloads.** Needs: 4.7a3. Ships after: R.1. — The declared but unbuilt desktop RID, rendered and installed on a real machine.

### Web vault

- [ ] **W.1 — Prove the browser vault architecture.** Needs: 0.2. — Prototype a shared-core or WASM route and record what a compromised delivery origin can reach; blocks web delivery until answered.
- [ ] **W.2a — Implement reviewed browser onboarding and unlocking.** Needs: W.1, H.2. — Create or import and unlock in a browser, with no unencrypted vault data in storage.
- [ ] **W.2b — Implement browser editing and sync recovery.** Needs: W.2a, 5.3a, H.6. — Edit, sync, resolve conflicts and export from the browser through the approved core.
- [ ] **W.2c — Review and publish the web vault.** Needs: W.2b. Ships after: R.3. — Independent review of the web-origin boundary, then deploy the exact reviewed version.

### Teams and enterprise credentials

R.4 needs an authorized pilot organization and scope, selected on the Human track; the code rows below do not wait for it.

- [ ] **7.1d — Build organization credential administration.** Needs: 7.1c. — Organization views, invitations, roles, ownership transfer and offboarding status in the desktop.
- [ ] **7.3a — Add organization OIDC sign-in.** Needs: 7.1b. — One IdP through established OIDC components; SSO authenticates the account and never unlocks the vault.
- [ ] **7.3b — Add provisioning and deprovisioning.** Needs: 7.3a, 7.1c, 7.2. — A SCIM connector where a departure actually revokes sessions, devices and broker access.
- [ ] **6.1 — Prove external delegation visibility and revocation.** Needs: none. — A feasibility check of what GitHub and Google grants can really be seen and revoked.
- [ ] **6.2 — Add the supported delegation views.** Needs: 6.1. — Show only the proved provider integrations, with staleness labelled and offline never shown as zero.
- [ ] **7.4 — Build team access reviews and delegation dashboard.** Needs: 7.1d, 7.2, 7.3b. — Membership, rights and broker policy in one reviewable place, with unknown access left visible.
- [ ] **R.4 — Verify a real organization pilot.** Needs: 7.1d, 7.2, 7.3b, 7.4. Ships after: R.2. — **Milestone gate:** an authorized organization onboards, shares, reviews access and offboards, independently assessed.

### Community evidence and public communication

- [ ] **5.6 — Implement consented list confirmation.** Needs: none. — **Input:** H.7's SMTP credentials. Double opt-in and unsubscribe for the signup list, kept separate from account and security mail, verified against a mailpit fixture until the input exists (D-0166).
- [ ] **3.10b — Ship hosted and account-lifecycle guides.** Needs: 5.3c, H.5b, H.6, 5.5c, M.2c. — Help for signup, phone access, recovery, support, cancellation, export and deletion.
- [ ] **3.11 — Publish the app and hosted-release announcement.** Human. Needs: 3.10b. Ships after: R.3. — Version-correct announcement built on retained R.1/R.3 evidence.

### Relay sync storage

- [ ] **5.2d — Implement authorized blob sync and version retention.** Needs: H.4, 5.2a. — Bounded upload, compare-and-swap publication and retained encrypted versions on the relay binary, with no entry names in metadata or logs; 5.2b's former scope (D-0182).

## Scale

These steps carry build Needs like any other; their place after the release and organization gates is priority, not dependency. None of these postpones a control already required for a pilot, consumer release or organization pilot.

- [ ] **S.1 — Expand service capacity from measurements.** Needs: H.8. — Fix one bottleneck that a load measurement against the declared budget demonstrates.
- [ ] **S.2 — Support managed desktop fleet deployment.** Needs: 4.7a1. — A per-machine variant of the Windows MSI deployed through Intune, since D-0139's per-user package cannot be assigned to a device (D-0151, D-0152).
- [ ] **S.3 — Integrate one downstream credential lifecycle.** Needs: 7.2. — One reviewed rotation or temporary-credential integration for a named provider.
- [ ] **S.4 — Expand support and incident capacity.** Human. Needs: H.10. — A bounded on-call workflow extending the staffed process.
- [ ] **S.5 — Produce requested enterprise assurance evidence.** Human. Needs: 10.2. — Map implemented controls to a specific procurement request; preparing a questionnaire is not a certification.

## Completion and ID continuity

A finished step has its artifact, a passing verifier and an evidence link naming the tested version. Record source, package, public and installation evidence separately: tests of compiled code do not establish public installation or live service operation, and a documentation edit completes nothing. A gate's transitive Needs must all pass, and further findings in its acceptance journey keep it open. The five transcript pages named in [CLAUDE.md](../CLAUDE.md) must pass `verify-demo.sh` when affected.

Keep every ID traceable. These formerly large rows now select their first ready child when requested by their old ID:

| Previous ID | Current children / scope |
|---|---|
| 1.4 | 1.4a–c; keyfile support separated into V.1a/b |
| 3.5 | 3.5a/b, in Expansion with the macOS desktop app (D-0201) |
| 3.6 | 3.6b; the 3.6a enrolment (H-0017) became 4.7c2's Input, since it gates nothing else (D-0221) |
| 3.7 | 3.7a–c |
| 3.9 | 3.9a–d |
| 3.10 | 3.10a/b |
| 4.3 | 4.3a/b |
| 4.7 | 4.7a–e; 4.7e carries the macOS clauses of 4.7b–d (D-0201) |
| 4.7a | Aggregate of Windows 4.7a1 and Linux 4.7a3; macOS 4.7a2 moved to Expansion with the macOS desktop app (D-0201) |
| 4.7c | 4.7c1 built the publication path; 4.7c2 publishes, and ships only once there is an identity to sign with and a desktop worth installing (D-0219) |
| 5.2 | 5.2a–d; 5.2d carries the sync storage 5.2b held before it became drops |
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
| F.2b | F.2b1/F.2b2/F.2b3; the wiring closed with F.2b1, the runner observation with F.2b2, and the real-desktop record is F.2b3 |
| F.10 | Closed by diagnosis F.10a and repair F.10b; the gate held across retry sleeps is F.12 |

An aggregate ID selects its first ready child while retaining the parent's completion gate. Other IDs that still name explicit rows select those rows: historical 3.2b is the completed essay and stands alone. H-0015/H-0017 are signing enrollment, H-0018 payment, H-0019 hosting, and H-0011 the site's pre-deploy procedure. H-0006/H-0007 were the community introduction and its feedback, withdrawn by D-0220. Prior decisions H-0002/H-0004/H-0008/H-0009/H-0012/H-0013/H-0014 remain answered in DECISIONS; H-0010 was never issued. The preceding roadmap is retained in git; D-0090 supersedes its MVP/Launch/Scale pricing-tier ordering.
