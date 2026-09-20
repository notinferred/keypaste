# Engineering decisions

[PRODUCT](docs/PRODUCT.md) owns scope, [STEPS](docs/STEPS.md) owns committed delivery, and [BACKLOG](docs/BACKLOG.md) owns optional ideas. This ledger records choices that still constrain work within that scope. Original wording and superseded plans remain in the [archive](docs/decisions-archive.md); they do not restore removed commitments.

## Ledger

| id | date | decision | supersedes |
|---|---|---|---|
| D-0270 | 2026-09-20 | A password typed on the restore panel, and a checked backup waiting on its confirmation, are zeroed after the session's idle timeout, on minimize when that setting is on, and on every other way out; a correct master password one click from an open vault does not wait indefinitely on a locked screen | the locked screen having no deadline of any kind |
| D-0269 | 2026-09-20 | The unlock screen selects a missing or non-KDBX path, for restoring only, when `VaultBackups.List` names copies beside it; a missing vault is reachable from the recent list alone, and a forgotten one is recovered by opening a backup directly | `Offer` refusing every path that is not a readable vault |
| D-0268 | 2026-09-20 | `Vault.ExportTo` writes the saved file's bytes to a new path with a guard of its own: it refuses an existing file or directory, the vault under any spelling, the backup directory and anything inside it whatever the name, and a vault changed on disk; D-0092's KDBX-destination refusal stays `env export`'s, since that writer is plaintext | reusing or parameterising `TryRefuseTheVault` |
| D-0267 | 2026-09-20 | `VaultBackups.Restore` accepts only what `Inspect` returned, which carries the vault path and an internal digest of the bytes it opened, and re-reads the live file before every rename attempt; a backup swapped after its check, another vault's token and a vault that changed or appeared mid-restore are each refused with nothing replaced | a restore taking a path and a password |
| D-0266 | 2026-09-20 | A restore keeps the file it replaces as a floor-exempt backup unless a listed copy already holds its exact bytes, and never prunes, leaving that to the next save; `SaveOverwriting` keeps D-0259's rule because it discards somebody else's write, no longer because it is the restore path | D-0259's rationale, and a restore that pruned the copy it was restoring |
| D-0265 | 2026-09-20 | A restore is a byte copy staged beside the vault and renamed over it under the save gate, so the restored vault opens with the backup's password and no second KDBX writer exists; staging in the backup directory was rejected because a junction to another volume turns the rename into a copy a kill can leave half written | re-keying a restored vault to the current password |
| D-0264 | 2026-09-20 | Restoring a whole-vault backup is a locked operation on the unlock screen, asking only for the password the backup was made under, so it reaches a vault that is damaged, not KDBX or missing (PRODUCT §5.7); export lives in Settings, the CLI gets neither verb, and the rollback an old-password holder with disk access gains is accepted because the replaced vault is kept | a Backups destination in the unlocked shell, and requiring the live vault's password |
| D-0263 | 2026-09-20 | `verify-keepassxc-backup.sh` defeats the fifteen-minute floor by renaming a backup's stamp, and the tripwire asserts no `KEYPASTE_BACKUP` variable appears in it, because a knob that shortens the floor is a knob that turns backups off | an environment variable or flag the gate could set |
| D-0262 | 2026-09-20 | A backup is a byte copy and not a KDBX write, so D-0050's condition for giving `app.yml` a KeePassXC job of its own does not fire; `ci.yml`'s compat job covers the copy on all three operating systems | a second KeePassXC job on app.yml |
| D-0261 | 2026-09-20 | `GenerateFlagTests` asks that no word of a generated passphrase reaches stderr, instead of that stderr holds no full stop; the separator is a full stop and stderr now carries a file path, so absence of the character was a proxy a sentence could break with nothing leaked | the separator's absence as the stderr claim |
| D-0260 | 2026-09-20 | The CLI names the backup directory once, on stderr, on the save that creates it, and is silent after; a directory of encrypted vaults appearing beside somebody's file unannounced is the discovery PRODUCT §6.1 calls a risk to trust, and a line on every save is noise a script would silence | silent creation, or a line on every save |
| D-0259 | 2026-09-20 | `SaveOverwriting` always backs up, exempt from both the per-unlock flag and the floor, and does not set the flag; it is the restore path, and the vault a restore replaces is the copy needed when the restore was the wrong one | one suppression rule shared by both save paths |
| D-0258 | 2026-09-20 | Backups live in `<vault filename>.backups` beside the vault, named `<stem>.<UTC><source extension>`, keeping five, taken once per unlock and at most every fifteen minutes; keyed on the whole filename and carrying the source's own extension, so two vaults differing only by extension cannot share a directory or prune each other and no copy restates a format nothing decrypted | keying on the stem, or naming every copy .kdbx |
| D-0257 | 2026-09-20 | The backup is taken inside `TryAttempt`, after the gated re-read and before the write, so it holds the save gate, copies bytes the re-read has just shown keypaste opened, and skips first creation on `gated` — the gate's own existing condition; a failed copy raises `VaultBackupException`, outside `IsTransient`, so the retry budget cannot absorb it | a backup before the save, outside the gate |
| D-0256 | 2026-09-20 | `RemoveEntry` returns the RecycledEntryId it produced, so the app's Undo restores the row it just made rather than inferring it from a before-and-after diff of the bin, and which row a delete produced stays core's answer (PRODUCT §4.2) | an adapter inferring a deletion's identity |
| D-0255 | 2026-09-20 | `SetRecyclesDeletedEntries` stays internal and visible to the test projects rather than becoming public API, because KeePassXC owns a vault's recycle-bin setting and keypaste has no supported way to write it; a vault with the bin off is buildable for tests and this is no precedent for keypaste writing that setting | a public setter, or an untested bin-off path |
| D-0254 | 2026-09-20 | The compatibility gate keeps driving the trash through `tests/Keypaste.VaultRestorer` now that the desktop restores and purges, because a bash gate cannot press a button; the driver goes when a shipped command-line surface performs them | retiring the driver on the strength of a GUI surface |
| D-0253 | 2026-09-20 | The app erases one named recycled entry at a time behind its own confirmation, and exposes no empty-the-bin action; emptying stays in KeePassXC | a single control that destroys everything recoverable |
| D-0252 | 2026-09-20 | A trash row carries a title, the group it came from and when it went, and no field value, so a lock clears the list, the selection, the pending confirmation and the outcome line rather than a revealed value; V-V.3b is amended to that claim | V-V.3b's requirement to clear displayed trash values |
| D-0251 | 2026-09-20 | Trash is the sixth sidebar destination, built and disposed by the shell like every other, so navigation and lock clear it by the rule every screen already follows | a trash pane inside Entries |
| D-0250 | 2026-09-20 | A recycled entry is addressed by an opaque identity carried on its trash row rather than by name, path or list position, because two entries of one title land in one bin and an index addresses a reading rather than a thing | name-only addressing, for a deleted entry |
| D-0249 | 2026-09-20 | A restore is refused whole when another entry already answers to the name it would produce, and lands at the root when the group it came from is gone or is itself recycled | inventing a destination for a recovery; recreating the ambiguity D-0091 refuses |
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
