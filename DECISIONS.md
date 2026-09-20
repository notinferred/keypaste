# Engineering decisions

[PRODUCT](docs/PRODUCT.md) owns scope, [STEPS](docs/STEPS.md) owns committed delivery, and [BACKLOG](docs/BACKLOG.md) owns optional ideas. This ledger records choices that still constrain work within that scope. Original wording and superseded plans remain in the [archive](docs/decisions-archive.md); they do not restore removed commitments.

## Ledger

| id | date | decision | supersedes |
|---|---|---|---|
| D-0245 | 2026-09-19 | PRODUCT v1.7 restores hardware-key vault unlocking as a later T1 feature (P.1), after the first integrated desktop release without changing its gates or next tasks; OS-assisted quick unlock remains optional | v1.6 excluding hardware-key unlock; P.1 parked with quick unlock in BACKLOG |
| D-0244 | 2026-09-19 | The target design uses one local unlocked session for desktop access, MCP releases and project launches, with lock denying new releases and launches but unable to recall values already delivered; the process arrangement is resolved in T2 and is not implemented by this decision | D-0054's independent agent as future authority; D-0241's delayed native approval |
| D-0243 | 2026-09-19 | PRODUCT v1.6 restores the founder's local password-manager, MCP and project-env goal, limits committed work to T1–T5, and parks other product tracks in BACKLOG without implied delivery, full KeePassXC parity or per-milestone announcement and package-manager obligations | v1.5; D-0061/D-0090/D-0176/D-0241 scope and order; the previous STEPS roadmap |
| D-0242 | 2026-09-19 | The active ledger keeps concise decisions that constrain future changes, while historical wording and superseded plans belong in the archive and optional ideas belong only in BACKLOG | D-0090's ledger-for-everything; ALIGNMENT as an owner; the former Ideas table |
| D-0240 | 2026-09-19 | The history compatibility reader consumes its whole input stream before reporting its result, preserving pipefail without the broken pipe caused by an early awk exit | the history gate's early exit |
| D-0239 | 2026-09-18 | Passphrases default to a dot separator and reject any separator present in the word list so their words can be counted unambiguously | the proposed hyphen default |
| D-0237 | 2026-09-18 | Explicit `generate --words N` prints a newly generated passphrase once on stdout, reports provenance and entropy on stderr, and opens no vault or file | the former ban on a standalone generator; no commitment to sharing |
| D-0236 | 2026-09-18 | The implemented passphrase generator keeps six words as its minimum and default and thirty-two as its maximum, independently of the removed sharing plan | the former explanation making a share workflow necessary |
| D-0235 | 2026-09-18 | The verbatim EFF long list is vendored under CC-BY-4.0, embedded in Core and checked against the single digest in `WordList.Digest` at first use | generated or unverified duplicate word lists |
| D-0234 | 2026-09-18 | Character and word recipes are distinct cases of `SecretRecipe`, and passphrase generation appends because a word count does not determine an exact character count | a character-only recipe and exact-span primitive |
| D-0232 | 2026-09-18 | Intentional secret reveal must leave the window's accessibility surface unchanged while independent rendering evidence proves the secret was actually drawn | applying a typed-field differential to a reveal with no keystrokes |
| D-0230 | 2026-09-18 | The history compatibility gate retains its unshipped restore driver because the CLI has no restore command and the shipped desktop must expose no automation unlock seam | D-0228's proposed removal of the driver after desktop history |
| D-0229 | 2026-09-18 | History indices address one newest-first reading, with raw append order breaking timestamp ties, and are never persistent revision identifiers | exposing KeePassLib's raw history positions |
| D-0227 | 2026-09-18 | Restoring a revision stamps it with the restore time and preserves the replaced value in history with its original time | KeePassLib's untouched restore timestamps |
| D-0226 | 2026-09-18 | Username, URL and notes use display sanitization that preserves printable content and line breaks, titles and paths retain name sanitization, and secret characters are never sanitized | name sanitization on display fields |
| D-0225 | 2026-09-18 | Entry and variable paste removes one trailing line break and otherwise rejects unsupported characters without silently altering the secret, through the shared Core rule | the unsupported-paste claim for all secret fields |
| D-0224 | 2026-09-18 | Local verification selects affected profiles, resumes after failures and uses pinned Linux fixtures on Windows, while unmapped changes and hosted CI retain the full checks under CLAUDE.md | a full local run after every final edit |
| D-0223 | 2026-09-18 | Desktop file selection uses `IVaultFilePicker` so cancellation and occupied-path behavior can be exercised through the same view-model path as a user action | file pickers called directly by the view |
| D-0222 | 2026-09-18 | CLI and desktop vault creation share `Core.VaultCreation` and its overwrite, password and confirmation rules, while low-level `Vault.Create` remains available for fixtures | separate CLI and desktop creation refusals |
| D-0218 | 2026-09-17 | Published desktop provenance verifies the publication workflow, while the separate build attestation is checked before staging and is not independently served at the public origin | no claim of publicly verifiable desktop build provenance |
| D-0217 | 2026-09-17 | `internal: false` offers a desktop package for publication, and release validation refuses an offered package that is unsigned or has signing policy `none` | manually enabling desktop publication without definition checks |
| D-0216 | 2026-09-17 | The desktop manifest identifies its publication provenance separately from its build workflow, and publication stages the already-verified candidate without rebuilding | one workflow identity for building and publishing |
| D-0215 | 2026-09-17 | Desktop and CLI artifacts use one immutable version prefix with separate component manifests and provenance bundles | D-0138's single manifest per release |
| D-0214 | 2026-09-17 | Windows major upgrades remove the previous installation only after the new installation commits so a failed transaction leaves the previous app runnable | WiX's default removal before installation |
| D-0207 | 2026-09-17 | The release definition owns the WiX SDK digest and the installer script owns its restore, pin validation and build | a duplicated SDK pin and inline workflow builder |
| D-0208 | 2026-09-17 | One script owns the pinned Windows KeePassXC download across compatibility, release and upgrade checks | separate workflow copies of the download |
| D-0204 | 2026-09-16 | Windows signing uses the pinned Artifact Signing client through `sign-windows.sh`, with required identity variables and fail-closed pin and configuration checks | an unpinned signing client or ad hoc signing path |
| D-0203 | 2026-09-16 | The AppImage definition pins both appimagetool and its runtime and passes the runtime explicitly to prevent an unpinned download during packaging | pinning appimagetool alone |
| D-0202 | 2026-09-15 | Restoring a minimized window does not count a resting pointer as activity, and activity after the idle deadline locks instead of reviving the session | position-blind pointer activity and unconditional deadline extension |
| D-0199 | 2026-09-15 | Native desktop observations use a test-only observer through the real startup path, keeping unlock automation hooks out of shipped binaries | adding an automation unlock seam to the app |

These implementation decisions describe existing behavior unless explicitly marked as a target; D-0244 requires new work and verification before any shared-session claim. Security guarantees and known limits remain in [SECURITY](SECURITY.md) and [THREATS](THREATS.md).

## Optional ideas

[BACKLOG](docs/BACKLOG.md) is the only owner of useful options outside T1–T5. A former promoted status, archived dependency or technical design does not schedule that work. Reconsidering one requires an explicit scope decision and an updated plan.

## External records

Private business notes remain outside git at `~/Nextcloud/keypaste/business.md` and `~/Nextcloud/keypaste/keypastebusinessnotes-2026-09-04.md`; they were not read for this rewrite and do not override PRODUCT or commit work. Financial figures and credentials must not be copied into this ledger.

The following locations describe existing infrastructure; they create no relay, hosting or billing commitment. Operational instructions and release evidence remain with their owners.

| record | location or owner |
|---|---|
| Landing page | Cloudflare Worker `keypaste-site`; [site/README.md](site/README.md) owns deployment |
| Immutable release downloads | Cloudflare R2 at `dl.keypaste.com`; [RELEASE](docs/RELEASE.md) owns publication and installation evidence |
| Signup database | PlanetScale Postgres through Cloudflare Hyperdrive; [site/schema.sql](site/schema.sql) and the site guide |
| Database credential | Cloudflare Hyperdrive configuration only, absent from this repository |
| Vulnerability reports | [SECURITY.md](SECURITY.md) owns the tested private reporting route |
| DNS | Cloudflare for `keypaste.com` and `dl.keypaste.com` |
| CI runners | GitHub-hosted runners; the workflow files own their exact matrix |
| Windows signing identity | Required for public signed desktop delivery; not established by the fixture signing rehearsal |
| Apple signing and notarization | No enrollment or notarized desktop release is established; any advertised support needs its own evidence |
| Repository | Public `notinferred/keypaste`; the `keypaste` organization is held empty |
| npm and crates names | Neither is a current distribution channel or registration task |
| Trademark | Not filed; the historical risk decision is D-0058 |
| Demo recording | [docs/demo/keypaste-demo.gif](docs/demo/keypaste-demo.gif), mirrored under `site/public/demo/` |
| KDBX implementation | [KeePassLib upstream record](third_party/KeePassLib/UPSTREAM.md) owns provenance, license and vendor changes |
| Packaging and signing pins | [release-targets.json](release-targets.json) and the relevant project lockfiles |

## History

The [decision archive](docs/decisions-archive.md) retains previous product ratifications, plan changes and implementation evidence. It is reference material, not an executable backlog.
