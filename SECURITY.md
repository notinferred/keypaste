# Security policy

## Reporting a vulnerability

Email <security@keypaste.com>. Don't use public issues, discussions or pull requests, and note that GitHub private vulnerability reporting is off.

Include the affected version or commit, a description, reproduction steps or a proof of concept, and the expected impact. Any language is welcome, and so are anonymous reports.

| Response | Target |
|---|---|
| Acknowledgement | Within 72 hours |
| Initial assessment | Within 7 days |
| Fix or mitigation plan | Communicated when available, with a timeline |
| Coordinated disclosure | Up to 90 days, shortened by agreement or active exploitation |

**In scope:** this repository, its build and release pipeline, dependencies on the secret path, and keypaste.com's signup endpoint, because it stores email addresses. The endpoint's source is in the site's own repository.

**Out of scope:** static site content and third-party deployments.

Report a dependency's vulnerability both upstream and here, since neither report reaches the other and vendored patches are merged by hand. [UPSTREAM.md](third_party/KeePassLib/UPSTREAM.md) records KeePassLib's provenance and local changes, and [its own](third_party/KeePassLib/bouncycastle-twofish/UPSTREAM.md) those of the BouncyCastle Twofish code it compiles in to encrypt Twofish vaults.

Security reports get the highest priority. Credit in the advisory and changelog is your choice, and there is no bug bounty. Every security-relevant fix is disclosed, and a breach or serious shipped bug gets prompt, full disclosure of the incident, the exposure and the changes ([law 10](#security-laws)).

## Supported versions

The published release is the CLI and MCP bridge `v0.3.0`. The desktop app has no release yet; 1.0.0 will be its first. [RELEASE](docs/RELEASE.md) records distribution status.

| Version | Supported | Status |
|---|---|---|
| `main` | Yes | Fixes land here first |
| `0.3.x` | Yes | Current release line |
| `0.2.x` | No | Superseded; affected by the defects below |
| `0.1.x` | No | Superseded; affected by the defects below |

There is no long-term support line before 1.0. Fixes go to `main` and the next release. Published archives never change, so an old download keeps its defects.

**If you installed `v0.1.0` or `v0.2.0`, replace it with `v0.3.0`** ([changelog](CHANGELOG.md#030)). These are the recorded defects of the superseded versions:

| Defect | In | Fixed in |
|---|---|---|
| env export can delete your vault by writing over it | `v0.1.0` | `v0.2.0` |
| env rm and env set can act on the wrong entry when a path is ambiguous | `v0.1.0` | `v0.2.0` |
| get can return the wrong password | `v0.1.0` | `v0.2.0` |
| env pull can delete an edit it never imported | `v0.1.0` | `v0.2.0` |
| moving the clock back can revive an expired approval | `v0.1.0` | `v0.2.0` |
| an approved credential too large to send costs you every other grant | `v0.1.0` | `v0.2.0` |
| listing a large vault costs you every other grant | `v0.1.0` | `v0.2.0` |
| an agent can queue approval prompts | `v0.1.0` | `v0.2.0` |
| entry names can render unescaped | `v0.1.0` | `v0.2.0` |
| saving while another program saves the same vault can undo its change, with no history entry | `v0.1.0`, `v0.2.0` | `v0.3.0` |
| on Windows 11 24H2 and Windows Server 2025, a save can fail when another keypaste or KeePass program is saving at the same time; nothing is lost | `v0.1.0`, `v0.2.0` | `v0.3.0` |
| an agent asking for entry names on a loaded machine can be told to start keypaste agent for a process that is already running; nothing is lost | `v0.2.0` | `v0.3.0` |

## Verifying a release

Each version's downloads at `https://dl.keypaste.com/v<version>/` include:

- `SHA256SUMS`, a `.sha256` file per asset, and the corresponding source.
- From `v0.3.0`, a manifest, `keypaste-<version>-manifest.json`, and an attestation bundle, `keypaste-<version>-provenance.sigstore.jsonl`. `v0.2.0` and earlier have neither.
- In a version that publishes the desktop packages, a second manifest and bundle, `keypaste-app-<version>-manifest.json` and `keypaste-app-<version>-provenance.sigstore.jsonl`.

Up to `v0.3.0`, the binaries are unsigned and un-notarized, so Gatekeeper and SmartScreen may warn. [Install](docs/install.md) shows the macOS quarantine workaround, and its install blocks keep quarantine in place. From 1.0.0, the Windows programs and MSI are signed with keypaste's Authenticode identity, and the macOS code is signed with its Developer ID and notarized, with the disk image's ticket stapled; Linux binaries stay unsigned. SmartScreen may still warn while the identity builds reputation.

`release.yml` tests the NativeAOT binaries it uploads, including KeePassXC compatibility in both directions. `ci.yml` runs the unit suites against an ordinary build of the same commit.

### Checksums

A checksum catches a corrupted or truncated download. The archive and its checksum come from the same origin, so an attacker who replaces both defeats the check.

### Build attestation

From `v0.3.0`, every asset and the manifest carry a GitHub build attestation, signed through Sigstore by `release.yml` running in `notinferred/keypaste` for the release tag. Checking one needs [GitHub CLI](https://cli.github.com/) (tested with 2.86.0) and no GitHub account:

```sh
v=<version>
curl -fLO "https://dl.keypaste.com/v$v/keypaste-$v-linux-x64.tar.gz"
curl -fLO "https://dl.keypaste.com/v$v/keypaste-$v-provenance.sigstore.jsonl"
gh attestation verify "keypaste-$v-linux-x64.tar.gz" \
  --bundle "keypaste-$v-provenance.sigstore.jsonl" \
  --repo notinferred/keypaste \
  --signer-workflow notinferred/keypaste/.github/workflows/release.yml \
  --source-ref "refs/tags/v$v"
```

Substitute any other asset name. An MSI or AppImage is checked the same way against the app's bundle, and the signer workflow is still `release.yml`, which attests what it publishes rather than what compiled it. A changed byte, another repository or another workflow fails the check.

- **Offline:** the command fetches Sigstore's public trust root. Run `gh attestation trusted-root > trusted_root.jsonl` on a connected machine, then add `--custom-trusted-root trusted_root.jsonl`.
- **Without `--bundle`:** `gh` fetches the attestation from GitHub and needs `gh auth login`.
- **A whole release:** [verify-provenance.sh](scripts/verify-provenance.sh) `<version>` downloads it and checks the manifest and every asset; `--component app` does the same for the desktop packages.

An attestation shows which repository, workflow, tag and commit produced the bytes. It doesn't show that the source is safe, that the build is reproducible (it isn't) or that the release workflow was uncompromised [T-21]. To run only code you have read, [build from source](docs/install.md#building-from-source); dependencies are pinned by content hash in `packages.lock.json`.

## Security laws

These laws stand above every other rule of keypaste's and change only by the founder's dated re-ratification.

1. The vault master key never leaves the local process, including through telemetry or an encrypted server backup. There are no exceptions.
2. Agents never get the vault. Each release contains one credential with one scope, or a linked set the person approves whole in one prompt, following explicit human approval or a pre-approved policy the human wrote, of which a category's Allow the human confirmed on this computer is one. A set is never released by a rule alone. Default is deny. A browser gets one login at a time, and only at the person's pick in keypaste's own in-page list, drawn apart from the page, which lists names and no password, and that pick is their answer.
3. Every agent request for a value, a command, a save or a change to an item, and every login released to a browser with the page's origin, is recorded as its own line in a local, human-readable log before keypaste answers, recording who or what asked for which item, when, and whether it was granted or denied; nothing rewrites a line. An agent's or a browser's lookups of names, a listing or a search, keep their own lines for 30 days and are then folded into one sealed summary line per agent or browser pairing per day. The person sets how long the log is kept, one year by default; older lines are removed only as whole days, behind a sealed line recording what was removed.
4. Keypaste never writes secrets to disk unencrypted, except a key file the person asks keypaste to create, an attachment the person asks it to save and a file the person explicitly asks to export, such as `env export --dotenv`, each written once where they choose. Injection uses process environment memory only.
5. Analytics and telemetry never include secret content or entry names. Only opt-in anonymous usage counts are permitted, and the desktop app's crash reports once the person says yes to them on its first launch, a yes of its own apart from accepting the terms of use, until they turn them off in Settings; a report holds no exception message, secret content, entry or vault name, file path or value.
6. Use the KDBX4 specification (Argon2; AES-256, ChaCha20 or Twofish, the ciphers KeePassXC writes) through mature audited libraries. Never write custom cryptography or modify the format.
7. Fail closed: every error path in the bridge denies access.
8. The code is open source under AGPL-3.0 and stays open: the desktop app, the CLI, the bridge, the browser relay, the browser extension, which keeps the GPL-3.0 of the KeePassXC-Browser it is forked from, and all code that encrypts or handles vault data. keypaste's hosted services and separately sold add-ons may be closed. Auditable code establishes trust in an unknown founder.
9. Dependencies are minimized and pinned. Every new dependency on the secret path is justified in one line beside the package in `Directory.Packages.props`.
10. Provide a security policy, a private vulnerability-reporting contact and honest responses. Disclose breaches and serious shipped bugs promptly and fully.
11. The bridge and the browser relay never open a vault; each forwards requests to the process holding it.
12. A save never replaces a vault without keeping a backup and reading the new file back successfully.
13. The trust boundary is the person's OS account: other processes running as the same user are outside keypaste's protection, as [THREATS](THREATS.md#assumptions-and-scope) says.
14. Every release opens every earlier release's vaults, and KeePassXC 2.7 or later opens what keypaste writes.

## Guarantees and limits

What keypaste holds to under the [laws](#security-laws), and what it can't prevent. [THREATS](THREATS.md) has each one's detail and tests. **v0.3.0** marks what the published CLI and bridge do, which the source build keeps. **Unreleased** marks the source build headed for 1.0.0, which includes the whole desktop app. A line with neither mark holds for both.

### Agent access

- A value is released only on your answer or a rule you set: per-category Ask, Allow or Block, Ask by default, with each Allow confirmed in the app (unreleased), or a `policy.toml` rule (v0.3.0) [T-12] [T-15].
- One approval releases one field (v0.3.0), or one field or attachment (unreleased).
- A linked set of up to four fields is allowed together, asked every time, and logged one line per field (unreleased) [T-56].
- `keypaste run --session` and the `run` tool get a whole env set for a child process, asked every time (unreleased) [T-30] [T-35].
- A scoped token releases its sets to `keypaste run --token` with no prompt, except protected sets (unreleased) [T-32].
- Agents see an item's title, group, tags and website unasked, and its username unless Settings hides it (unreleased) [T-4].
- An agent sees only what its bridge's `--expose` reaches (v0.3.0) [T-4].
- A search names at most 25 items it reaches, with their origins, the usernames Settings shows and the links among them, never a value (unreleased) [T-55].
- An agent may ask you to save a value at a place it names, and never receives that value (unreleased) [T-54].
- An agent may ask you to move an item it sees whole to Trash or mark it expired, and to delete for good only an item it created through an approved save. Each is asked every time and made only once you allow it. It may add or remove the item's `needs-update` tag without asking. Each change's line is written before the vault is saved, and a change whose line can't be written isn't made (unreleased) [T-57].
- Every answered request is logged before the reply, and a release that can't be logged is refused (v0.3.0) [T-6].
- Titles and reasons are sanitized, and the reason is shown as the agent's (v0.3.0) [T-1] [T-2].
- One prompt at a time across every vault open in the app (unreleased). A prompt expires after 45 seconds, and a refusal holds the next for 60 seconds (v0.3.0) [T-11].
- No agent request can make a password prompt appear (v0.3.0) [T-7].
- The bridge holds no vault and makes no network request (v0.3.0) [T-9].
- The local pipe admits only the current user (v0.3.0) [T-10].
- Each vault has one approver; CLI edits go to it and wait for you (unreleased) [T-29] [T-43].
- Agent settings hold only while sealed, each vault's apart under its own key; otherwise every value is asked (unreleased) [T-15].
- A tool Connect sets up reaches only the vaults it was connected to, through one bridge that asks each vault's own approver (unreleased) [T-4].
- The app's prompt says Verified only when the system shows keypaste's own program on the pipe, started by the AI tool its label names, and Not verified otherwise. It decides nothing (unreleased) [T-3].

**Limits**

- Client labels are unauthenticated, so whoever starts a bridge gets what an Allow gives, even under another agent's label. Verified doesn't stop a process of the same user [T-3] [T-14] [T-36].
- A rule covers the vault's current contents, so a new field or tag changes what an Allow releases [T-13].
- Nobody reads the reason behind an unasked release, and timing reveals which categories are Allow [T-12] [T-17].
- A run's command is the runner's claim, and a run can leak values its scrubber misses [T-30] [T-35].
- A token bundle can't be revoked; rotate its values [T-32].
- There is no prompt limit across items [T-11].
- Nothing recalls a client's copies or revokes a credential at its issuer [T-12].

### Browser fill (unreleased)

- keypaste answers only its own extension's three builds, under its own host name, and never writes KeePassXC's [T-48] [T-49] [T-60].
- A browser pairs only once you confirm, in keypaste, the code its extension shows. One pairing request is asked at a time, and a refusal holds the next back for a minute up to an hour [T-48].
- One pairing per browser serves every vault unlocked on this computer. keypaste keeps only a hash of the extension's key, and the pairing counts only while an unlocked vault's seal holds it [T-50].
- Nothing is asked of keypaste as a page loads. Clicking keypaste's key icon in a login field lists the page's logins from every open vault by title, with no password [T-59].
- A login fills only when you pick it in that list, at once, and every fill is logged with the page's origin before it leaves keypaste [T-59].
- A pick counts only from a trusted click or key on a list the page hasn't faded, filtered or covered. The extension takes the site from the browser's tab, never from the page [T-59].
- An item matches a page only on its own host or a subdomain, with the same scheme and port, within one registrable domain. On an `http` page, a single-label host or a form posting to another site, the list says so before you pick [T-58].
- The extension refuses a login form in a frame from another origin before keypaste is asked [T-58].
- A release carries one item's username, password, title and UUID, and nothing else [T-60].
- Checked in real Chrome, Edge, Brave, Firefox and LibreWolf on Windows, macOS and Linux with keypaste's test build (8.3a3).

**Limits**

- Whoever holds a pairing's key can list the login titles of any site it names, and get any login listed there by claiming a pick on that site. Anything that can start keypaste as the browser can put a pairing confirmation in front of you at any time, with a code that differs from your browser's [T-48] [T-50] [T-59].
- A filled password belongs to the page and the extension, whose scripts or updates can send it anywhere [T-60].
- A page that fools you into picking from a list it didn't tamper with, or a compromised page of the site itself, gets the login picked on it. Whoever controls a subdomain of an item's site has its login listed there [T-58] [T-59].

### What unlocks a vault

- A master password. Unreleased: also a key file, with or without a password, in every KeePass form; keypaste never creates a key file that is any file at all [T-27] [T-28].
- Unreleased: a YubiKey slot in the desktop app [T-37], and Windows Hello on Windows or Touch ID on a Mac after a lock [T-44].
- New vaults are KDBX4 with Argon2d at 2 iterations, 64 MiB and parallelism 2, pinned in `src/Keypaste.Core/KdbxFormat.cs` rather than taken from KeePassLib's defaults. keypaste writes no cryptography of its own and reaches the format only through `src/Keypaste.Core/Internal/`.
- Unreleased: unlocking warns, and still opens the vault, when the file has a lower save count than the copy this computer's keypaste last opened or saved, as putting an older copy back leaves it. keypaste's own restore doesn't warn [T-26].

**Limits**

- A copy whose count isn't lower, a vault keypaste hasn't saved yet, a first open on a computer and a deleted home record go unnoticed [T-26].
- A copied vault can be guessed offline without limit [T-20].
- After a password or key file change, older copies and backups still open with the old credentials; delete them.
- A lost key file or YubiKey can't be recovered [T-27] [T-37].
- Whoever passes Windows Hello as you, PIN included, or whose fingerprint is enrolled in Touch ID, can reopen the vault while the app runs [T-44].

### The desktop app (unreleased)

- Secret fields show automation only their length [T-22]. A revealed value shows until you hide it, leave its item or window, or the vault locks [T-25].
- Copies clear after 20 seconds if unchanged, as `keypaste get` does in v0.3.0 [T-19].
- Several vaults can be open at once, each with its own claim and pipe. The idle lock, `Ctrl/Cmd+L` and quitting lock them all [T-23] [T-29].
- The idle lock comes after 30 minutes by default, and at most 8 hours [T-23].
- Notifications carry fixed words only [T-51].
- Crash reports are sent only after you agree, hold no value, name or path, and never come from the CLI or bridge [T-9]. [PRIVACY](PRIVACY.md#crash-reports) lists what a report holds.
- Each save keeps the last five copies in `<vault>.backups`, plus the newest of each of the last seven days and four weeks, restorable from the unlock screen [T-26]. Deleting moves an item to Trash; v0.3.0 deletes permanently.
- A save is refused, and replaces nothing, when the new file drops an item without recording a deletion or differs from the open vault, a hardware-key vault's included. The backup and the new file are flushed to the drive (on macOS with `F_FULLFSYNC`) before the rename that puts the file in place, and the save says so when the system won't flush the vault's folder after the rename.

**Limits**

- Screen capture and onlookers can read a revealed value [T-25].
- Clipboard managers can ignore keypaste's markers [T-19].
- `recent.toml` shows where vaults and key files are [T-24].
- An earlier master password and write access to the directory roll the vault back [T-26]; keep backups off the disk too.

### Memory

**Limits**

- There is no in-memory secrecy. Immutable strings, swap, dumps and any process running as you can expose values, resolving a request reads passwords into memory, and keypaste can't defend a compromised local account [T-18].
- v0.3.0 keeps an approved value for its `--max-ttl`; unreleased keeps none [T-18].
- `keypaste approve` has no idle lock [T-7].

### Plaintext and file limits

- `keypaste run` passes values only in the child's environment and writes no environment file, which `verify-run-injection.sh` checks (v0.3.0) [T-30].

**Limits**

- The child keeps those values after a lock, and process inspection and logs can expose them.
- `env set project KEY=value` puts the value in shell history; the command warns (v0.3.0).
- A variable's earlier values, up to ten, stay in the vault's encrypted history.
- `env export --dotenv` writes plaintext, owner-only except on Windows, where the file inherits directory permissions (v0.3.0).
- `attachment save` writes an attachment unencrypted to a file keypaste can't take back (unreleased).
- Deleting a file doesn't erase storage; rotate credentials that were committed or shared.
- A share link opens for anyone holding it, and only a generated passphrase protects an intercepted link (unreleased) [T-33].
- A reference file chooses what a run injects [T-31].

### Audit

- The log is hash-chained and appended to. `keypaste log verify --expect <hash>` checks it against an anchor you kept (v0.3.0) [T-5].
- Only the vault's approver rewrites it: lookups older than 30 days fold into one summary line per agent or browser pairing per day, and days past the period you keep it for (a year by default) are removed whole. Every other line keeps its bytes, and a line signed with the vault's key seals the result (unreleased) [T-5].
- The approver signs the chain every 64 lines and when its session ends, and `keypaste log verify --checkpoints` checks those signatures, so rewriting before the last one needs the unlocked vault (unreleased) [T-5].
- Each line written for a connection names the program on it, its process id, executable path and code signature state, as advice that refuses nothing (unreleased) [T-10].
- Item titles are sanitized. A reason is kept as a 200-character excerpt, its length and its hash (v0.3.0) [T-2]. `v0.1.0` records the request argument, which can be an opaque handle.

**Limits**

- Lines after the last checkpoint are unsigned, and the tail can be deleted without trace unless you kept an anchor; keypaste keeps none [T-5].
- A browser fill's line names the site, as its origin, where each item was filled or refused (unreleased).
- Kept forever, the log grows until archived ([archiving it](docs/mcp-setup.md#archiving-it)), and it can hold sensitive text agents wrote.

[T-1]: THREATS.md#t-1--prompt-injection-through-entry-names
[T-2]: THREATS.md#t-2--prompt-injection-through-the-agents-stated-reason
[T-3]: THREATS.md#t-3--confused-deputy-a-malicious-or-impersonating-client
[T-4]: THREATS.md#t-4--over-exposure-of-the-listing-surface
[T-5]: THREATS.md#t-5--audit-log-tampering
[T-6]: THREATS.md#t-6--unlogged-access
[T-7]: THREATS.md#t-7--where-the-master-password-is-typed
[T-9]: THREATS.md#t-9--exfiltration-and-telemetry
[T-10]: THREATS.md#t-10--the-approver-channel
[T-11]: THREATS.md#t-11--prompt-fatigue-and-clicking-yes-to-make-it-stop
[T-12]: THREATS.md#t-12--a-standing-approval-used-under-a-reason-nobody-read
[T-13]: THREATS.md#t-13--an-allow-covers-a-kind-of-value-not-the-values-you-pictured
[T-14]: THREATS.md#t-14--an-allow-is-a-standing-grant-to-anything-that-can-reach-the-owner
[T-15]: THREATS.md#t-15--agent-rules-are-a-file-any-program-of-the-persons-can-write
[T-17]: THREATS.md#t-17--a-timing-oracle-over-allow
[T-18]: THREATS.md#t-18--memory-dumping
[T-19]: THREATS.md#t-19--clipboard-scraping
[T-20]: THREATS.md#t-20--a-stolen-vault-file
[T-21]: THREATS.md#t-21--the-released-binary-is-not-the-source-you-read
[T-22]: THREATS.md#t-22--the-desktop-apps-accessibility-layer-as-a-read-path
[T-23]: THREATS.md#t-23--what-auto-lock-is-for-and-what-it-is-not
[T-24]: THREATS.md#t-24--recenttoml-tells-anything-that-can-read-keypaste-where-your-vaults-are
[T-25]: THREATS.md#t-25--a-value-on-screen-because-somebody-asked-to-see-it
[T-26]: THREATS.md#t-26--restoring-a-backup-while-the-app-is-locked
[T-27]: THREATS.md#t-27--naming-a-keyfile-tells-the-machine-where-the-second-factor-is
[T-28]: THREATS.md#t-28--a-keyfile-that-is-any-file-at-all-is-one-edit-from-losing-the-vault
[T-29]: THREATS.md#t-29--who-holds-a-vault
[T-30]: THREATS.md#t-30--a-run-asks-the-session-for-a-whole-env-set
[T-31]: THREATS.md#t-31--a-committed-reference-file-chooses-what-a-run-injects
[T-32]: THREATS.md#t-32--a-scoped-token-is-a-bearer-credential
[T-33]: THREATS.md#t-33--whoever-holds-a-share-link-can-open-it
[T-35]: THREATS.md#t-35--a-run-keeps-values-out-of-the-result-not-out-of-the-agents-reach
[T-36]: THREATS.md#t-36--an-agents-settings-bind-only-the-bridge-that-names-them
[T-37]: THREATS.md#t-37--a-yubikey-protects-a-copied-vault-not-an-unlocked-one-and-a-lost-key-locks-it
[T-43]: THREATS.md#t-43--a-terminal-asks-the-owner-to-change-the-vault
[T-44]: THREATS.md#t-44--quick-unlock-stands-in-for-the-master-password-after-a-lock
[T-48]: THREATS.md#t-48--the-browser-relay-holds-released-passwords-in-plaintext-and-anything-can-start-it-or-ask-to-pair
[T-49]: THREATS.md#t-49--another-program-answers-keypastes-host-name
[T-50]: THREATS.md#t-50--a-pairing-is-a-bearer-key
[T-51]: THREATS.md#t-51--a-notification-is-read-by-more-than-the-person
[T-54]: THREATS.md#t-54--an-agent-asks-the-person-to-save-a-value
[T-55]: THREATS.md#t-55--a-search-maps-the-persons-infrastructure
[T-56]: THREATS.md#t-56--a-linked-set-rides-one-approval
[T-57]: THREATS.md#t-57--an-agent-changes-an-item
[T-58]: THREATS.md#t-58--a-page-that-is-not-the-items-site
[T-59]: THREATS.md#t-59--keypaste-believes-the-extension-about-which-page-asks-and-whether-the-person-picked
[T-60]: THREATS.md#t-60--the-extension-someone-elses-code-receives-passwords
