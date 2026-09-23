# Engineering decisions

[PRODUCT](docs/PRODUCT.md) owns scope, [STEPS](docs/STEPS.md) owns committed delivery, and [BACKLOG](docs/BACKLOG.md) owns optional ideas. This ledger holds only decisions that constrain later work: a change a future step could make that would undo them. A decision that binds only one step's code lives in that step's [record](docs/steps/README.md), and superseded wording lives in the [archive](docs/decisions-archive.md); `git grep D-<number> -- '*.md'` finds any ID. IDs are never reused.

## Ledger

| id | date | decision | supersedes |
|---|---|---|---|
| D-0301 | 2026-09-23 | A secret input's accessible name is its attached name or, failing that, its placeholder, never anything derived from what was typed; the mask stays in the tree as D-0099 allows | — |
| D-0300 | 2026-09-23 | The entry pane reveals the current password while held, under the env hold rule, and every surface that holds a secret (current password, env value, history revision) copies through the one clearing countdown, although KeePassXC copies no revision | D-0231's "the current one stays copy-only" |
| D-0297 | 2026-09-22 | A vault is created with a keyfile only through `VaultCreation`, which still requires a master password and refuses the keyfile on the grounds an access change does, through one shared rule: missing or unusable, keyed by its hash, or the vault and its copies; keypaste makes no keyfile-only vault, and `keypaste init` takes no keyfile | — |
| D-0296 | 2026-09-22 | A desktop access change asks for the current master password and checks it by opening the file from disk with it and the session's keyfile before anything is written, then keeps the app unlocked by reopening the vault under the new factors and swapping it into the session without reporting a lock, as KeePassXC stays open; a failed reopen locks with its own reason | — |
| D-0295 | 2026-09-22 | The desktop remembers in `recent.toml` the path of the keyfile each vault last opened or was created with, and the unlock screen offers it again, as KeePassXC does; it records a location and never key material, and the CLI still records nothing and names a keyfile per command | D-0285's "`recent.toml` keeps vault paths alone" |
| D-0294 | 2026-09-22 | The CLI's trim roots keep the `Kfx*` types `XmlSerializer` reads a KeePass XML keyfile through, and keypaste permanently checks that the vendored branch reads a known v2.0 keyfile; a build that cannot refuses every keyfile `Inspect` identifies as XML, exit 3, rather than key by its hash, while a document that does not parse still falls back as in KeePassXC | — |
| D-0292 | 2026-09-22 | KeePassXC compatibility is required at the file level: keypaste may refuse to make something KeePassXC would make, for safety, but never refuses to open, edit or save an existing KeePassXC vault, so a vault keyed to an arbitrary file can still have its password changed | — |
| D-0288 | 2026-09-22 | An access change never removes a vault's password: a password vault keeps one, and a keyfile-only vault may swap its keyfile and stay keyfile-only but loses the keyfile only for a password set in the same change; keypaste still opens keyfile-only vaults as D-0282 requires | V.1a2's "never leave a vault without a master password" |
| D-0287 | 2026-09-22 | keypaste attaches an existing keyfile and never writes one, because PRODUCT §3.4 forbids writing a secret to disk unencrypted; it attaches XML, 32-byte and 64-hex keyfiles and refuses only the hashed-any-file form, and keyfile generation is a BACKLOG option | D-0281's "V.1a2 will write only the XML form" and V.1a2's generate clause |
| D-0285 | 2026-09-21 | A keyfile is named per command through `--keyfile` and `KEYPASTE_KEYFILE`, and nothing records which keyfile a vault uses; `recent.toml` keeps vault paths alone, and whether the desktop remembers one is V.1b's decision where the unlock screen is | storing the keyfile path beside the vault path for convenience |
| D-0282 | 2026-09-21 | `KeePassInterop.BuildKey` omits `KcpPassword` when the password is empty and a keyfile is given, because an empty password component is a different key from none and would refuse every keyfile-only vault KeePassXC wrote; with no keyfile an empty password stays wrong | adding an empty password component whenever one was typed |
| D-0281 | 2026-09-21 | keypaste opens every keyfile form KeePass accepts — XML, 32-byte, 64-character hex, and any file keyed by its hash — and `VaultKeyfile` classifies rather than derives, in `KcpKeyFile`'s own order; V.1a2 will write only the XML form, so a vault keypaste protects cannot acquire the fragile one | refusing the hashed-file form outright, which strands existing KeePassXC vaults |
| D-0278 | 2026-09-21 | `Vault.Search` matches titles, group paths, usernames and URLs in core and returns entry names with `MatchedFields`, never a value; passwords, notes and protected fields are never read, since a match on a note could confirm a recovery code | matching in a front end, which would hold every username it compared |
| D-0275 | 2026-09-20 | Renaming an env project carries its entries under whatever policy rule or exposure glob matches the new path, which keypaste does not prevent and THREATS T-13 now records; refusing it would put policy knowledge in the vault write path and a policy file edited afterwards reopens it anyway | preventing the widening, or leaving it unrecorded |
| D-0273 | 2026-09-20 | `env` at the root and the recycle bin are reserved: no group may be created as, renamed to or renamed from either, because that rename silently reclassifies a whole subtree; `EnsureGroup` still creates `env` when a variable is stored | ordinary group rules for the two groups keypaste assigns meaning to |
| D-0271 | 2026-09-20 | An ordinary move does not write `PreviousParentGroup`, so an organized vault stays KDBX 4.0 and only recycling raises it to 4.1 (D-0247); tidying a folder must not cost the readers below KeePassXC 2.7 that deleting does, and one another tool wrote is left alone | stamping it on every move, as KeePassXC does |
| D-0265 | 2026-09-20 | A restore is a byte copy staged beside the vault and renamed over it under the save gate, so the restored vault opens with the backup's password and no second KDBX writer exists; staging in the backup directory was rejected because a junction to another volume turns the rename into a copy a kill can leave half written | re-keying a restored vault to the current password |
| D-0264 | 2026-09-20 | A whole-vault backup is restored on the locked unlock screen with only the password it was made under, so a damaged or missing vault is reachable; export lives in Settings, the CLI has neither verb, and rollback by an old-password holder is accepted because the replaced vault is kept (T-26) | a Backups destination in the unlocked shell; requiring the live password |
| D-0263 | 2026-09-20 | `verify-keepassxc-backup.sh` defeats the fifteen-minute floor by renaming a backup's stamp, and the tripwire asserts no `KEYPASTE_BACKUP` variable appears in it, because a knob that shortens the floor is a knob that turns backups off | an environment variable or flag the gate could set |
| D-0258 | 2026-09-20 | Backups live in `<vault filename>.backups` beside the vault, named `<stem>.<UTC><source extension>`, five kept, taken once per unlock and at most every fifteen minutes; keying on the whole filename stops two vaults differing by extension sharing a directory | keying on the stem, or naming every copy .kdbx |
| D-0257 | 2026-09-20 | The backup is taken inside `TryAttempt`, under the save gate after the re-read and before the write, and skips first creation; a failed copy raises `VaultBackupException`, outside `IsTransient`, so no retry absorbs it | a backup before the save, outside the gate |
| D-0253 | 2026-09-20 | The app erases one named recycled entry at a time behind its own confirmation, and exposes no empty-the-bin action; emptying stays in KeePassXC | a single control that destroys everything recoverable |
| D-0248 | 2026-09-20 | The recycle bin is skipped by ReadEntries, ReadGroupPaths and Locate, so every listing, lookup, env injection and credential release excludes a deleted entry without a filter of its own, and `keypaste ls` omits the one group KeePassXC still shows | ListCommand's claim that nothing is ever hidden |
| D-0247 | 2026-09-20 | The KEYPASTE_KDBX_4_1_MOVES guard raises a written file to KDBX 4.1 when an object carries a PreviousParentGroup, so a vault that has recycled anything needs KeePassXC 2.7 or KeePass 2.48; no repository document stated a KDBX version or reader floor to amend | a 4.0 save dropping where a recycled entry came from |
| D-0246 | 2026-09-20 | Deleting an entry moves it to the vault's KDBX recycle bin with its identity, fields and history unless the vault's own RecycleBinEnabled is off, and erasing a value is a separate purge or empty of what is in the bin | deleting outright as both the ordinary delete and the only erasure |
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
