# Security policy

## Reporting a vulnerability

Email `security@keypaste.com` with the affected version or commit, a description, reproduction steps or proof of concept, and the expected impact. Reports in any language and anonymous reports are welcome. Do not use public issues, discussions or pull requests for vulnerabilities.

| Response | Target |
|---|---|
| Acknowledgement | Within 72 hours |
| Initial assessment | Within 7 days |
| Fix or mitigation plan | Communicated when available, with a timeline |
| Coordinated disclosure | Up to 90 days, shortened by agreement or active exploitation |

Security reports receive the highest priority. Credit in advisories and the changelog is optional; there is no bug bounty. Breaches and serious shipped bugs receive prompt, full disclosure covering the incident, exposure and changes, as required by PRODUCT §3.10. All security-relevant fixes are disclosed.

Reports cover this repository, its build and release pipeline, and dependencies on the secret path. The signup endpoint at `site/src/worker.js` is also in scope because it stores email addresses. Static site content and third-party deployments are outside this reporting scope. Report upstream dependency vulnerabilities upstream and here; vendored patches must be merged manually.

## Supported versions

The published release is the CLI and MCP bridge `v0.3.0`. The desktop app has no release; the next public release, 1.0.0, is its first. [RELEASE.md](docs/RELEASE.md) records distribution status.

| Version | Supported | Status |
|---|---|---|
| `main` | Yes | Fixes land here first |
| `0.3.x` | Yes | Current release line |
| `0.2.x` | No | Superseded; affected by the defects below |
| `0.1.x` | No | Superseded; affected by the defects below |

There is no long-term support line before 1.0. Fixes enter `main` and the next release; older tags remain unchanged. Published archives are immutable, so old downloads keep their defects.

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

CLI/MCP downloads at `https://dl.keypaste.com/v<version>/` include `SHA256SUMS`, per-asset `.sha256` files and corresponding source; `v0.3.0` adds a manifest, `keypaste-0.3.0-manifest.json`, and an attestation bundle, `keypaste-0.3.0-provenance.sigstore.jsonl`; `v0.2.0` and earlier have neither. `release.yml` tests the NativeAOT binaries it uploads, including KeePassXC compatibility in both directions. `ci.yml` runs the unit suites against an ordinary build of the same commit.

The binaries are unsigned and un-notarized, so Gatekeeper and SmartScreen may warn; README documents the macOS quarantine workaround, and its install blocks preserve quarantine.

Checksums detect corrupted or truncated downloads. Because the archive and checksum share an origin, an attacker who replaces both defeats that check.

`v0.3.0` also carries a GitHub build attestation, signed through Sigstore by `release.yml` running in `notinferred/keypaste` for the release tag, covering every asset and the manifest. `v0.2.0` and earlier have none. Checking one needs [GitHub CLI](https://cli.github.com/) (established with 2.86.0) and no GitHub account:

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

Substitute any other asset name. A changed byte, another repository or another workflow fails the check. A version that publishes the desktop packages carries a second manifest and bundle, `keypaste-app-<version>-manifest.json` and `keypaste-app-<version>-provenance.sigstore.jsonl`, in the same prefix; an MSI or AppImage is checked with the same command against its own bundle, and the signer workflow is still `release.yml`, which attests what it publishes rather than what compiled it. The command fetches Sigstore's public trust root; to check offline, run `gh attestation trusted-root > trusted_root.jsonl` on a connected machine and add `--custom-trusted-root trusted_root.jsonl`. Without `--bundle`, `gh` fetches the attestation from GitHub and requires `gh auth login`. [verify-provenance.sh](scripts/verify-provenance.sh) `<version>` downloads a whole release and checks the manifest and every asset this way; `--component app` does the same for the desktop packages.

An attestation shows which repository, workflow, tag and commit produced the bytes. It does not show that the source is safe, that the build is reproducible (it is not) or that the release workflow was uncompromised ([THREATS.md](THREATS.md) [T-21]). README documents building from source with dependencies pinned by content hash in `packages.lock.json`.

## Guarantees and limits

[PRODUCT §3](docs/PRODUCT.md) owns the security laws, and [THREATS.md](THREATS.md) each guarantee's detail and tests. **v0.3.0** marks what the published CLI and bridge have (the source build keeps it); **unreleased** marks the source build headed for 1.0.0, which includes all of the desktop app. A line with neither mark holds for both.

### Agent access

- A release needs a person's answer or a rule the person set: per-category Ask, Allow or Block, Ask by default, each Allow confirmed in the app (unreleased); a `policy.toml` rule (v0.3.0) [T-12] [T-15].
- One approval releases one field or attachment to an agent (v0.3.0: one field); a whole env set to a child process for `keypaste run --session` and the `run` tool, asked every time (unreleased) [T-30] [T-35].
- A scoped token releases its sets to `keypaste run --token` with no prompt, except protected sets (unreleased) [T-32].
- An entry's title, group, tags and URL, and its username unless Settings hides it, reach agents unasked (unreleased) [T-4].
- The bridge holds no vault and makes no network request (v0.3.0) [T-9].
- Every answered request is recorded before the reply, and an unrecordable release is refused (v0.3.0) [T-6].
- An agent sees only what its bridge's `--expose` reaches (v0.3.0) [T-4].
- Names and reasons are sanitized, and the reason is shown as the agent's (v0.3.0) [T-1] [T-2].
- One prompt at a time; 45-second expiry; 60-second hold after a refusal (v0.3.0) [T-11].
- No agent request can make a password prompt appear (v0.3.0) [T-7].
- The local pipe admits only the current user (v0.3.0) [T-10].
- One owner per vault; CLI edits go to it and wait for the person (unreleased) [T-29] [T-43].
- Agent rules hold only while sealed; anything else asks about every value (unreleased) [T-15].
- Limit: client labels are unauthenticated, so whoever starts a bridge gets what an Allow gives, also under another agent's label [T-3] [T-14] [T-36].
- Limit: a rule covers the vault's current contents, so a new field or tag changes what an Allow releases [T-13].
- Limit: nobody reads the reason behind an unasked release, and timing reveals which categories are Allow [T-12] [T-17].
- Limit: a run's command is the runner's claim, and a run can leak values its scrubber misses [T-30] [T-35].
- Limit: a token bundle cannot be revoked; rotate its values [T-32].
- Limit: there is no prompt limit across entries [T-11].
- Limit: nothing recalls a client's copies or revokes a credential at its issuer [T-12].

### What unlocks a vault

- A master password; unreleased, also a keyfile, with or without a password, in every KeePass form; keypaste never creates a keyfile that is any file at all [T-27] [T-28].
- Unreleased: a YubiKey slot in the desktop [T-37], and Windows Hello after a lock on Windows [T-44].
- New vaults are KDBX4 with Argon2d at 2 iterations, 64 MiB and parallelism 2, pinned in `src/Keypaste.Core/KdbxFormat.cs` rather than taken from KeePassLib's defaults; keypaste writes no cryptography of its own and reaches the format only through `src/Keypaste.Core/Internal/KeePassInterop.cs`.
- Limit: a copied vault can be guessed offline without limit [T-20].
- Limit: after a password or keyfile change, older copies and backups still open with the old credentials; delete them.
- Limit: a lost keyfile or YubiKey cannot be recovered [T-27] [T-37].
- Limit: whoever passes Windows Hello as the person, PIN included, can reopen the vault while the app runs [T-44].

### The desktop app (unreleased)

- Secret fields show automation only their length [T-22]; a revealed value shows at most 30 seconds [T-25].
- Copies clear after 20 seconds if unchanged, as `keypaste get` does in v0.3.0 [T-19].
- Idle lock after 30 minutes by default, at most 8 hours [T-23].
- Notifications carry fixed words only [T-51].
- Crash reports are sent only after consent, hold no value, name or path, and never come from the CLI or bridge [T-9].
- A save keeps the last five copies in `<vault>.backups`, restorable from the unlock screen [T-26]; deletion goes to the recycle bin (v0.3.0 deletes permanently).
- Limit: screen capture and onlookers can read a revealed value [T-25].
- Limit: clipboard managers can ignore keypaste's markers [T-19].
- Limit: `recent.toml` shows where vaults and keyfiles are [T-24].
- Limit: an earlier master password and write access to the directory roll the vault back [T-26]; keep backups off the disk too.

### Memory

- Limit: no in-memory secrecy; immutable strings, swap, dumps and any process running as you can expose values, resolving a request reads passwords into memory, and keypaste cannot defend a compromised local account [T-18].
- Limit: v0.3.0 keeps an approved value for its `--max-ttl`; unreleased keeps none [T-18].
- Limit: `keypaste approve` has no idle lock [T-7].

### Plaintext and file limits

- `keypaste run` passes values only in the child's environment and writes no environment file, which `verify-run-injection.sh` checks (v0.3.0) [T-30].
- Limit: the child keeps those values after a lock, and process inspection and logs can expose them.
- Limit: `env set project KEY=value` puts the value in shell history; the command warns (v0.3.0).
- Limit: a variable's earlier values stay in the vault's encrypted history, up to ten.
- Limit: `env export --dotenv` writes plaintext, owner-only except on Windows, where it inherits directory permissions (founder decision, 2026-10-22) (v0.3.0).
- Limit: `attachment save` writes an attachment unencrypted to a file keypaste cannot take back (unreleased).
- Limit: deleting a file does not erase storage; rotate credentials that were committed or shared.
- Limit: a share link opens for anyone holding it, and only a generated passphrase protects an intercepted link (unreleased) [T-33].
- Limit: a reference file chooses what a run injects [T-31].

### Audit

- The log is append-only within keypaste and hash-chained; `keypaste log verify --expect <hash>` checks it against an anchor you kept (v0.3.0) [T-5].
- Entries are sanitized; a reason is kept as a 200-character excerpt, its length and hash (v0.3.0) [T-2]; `v0.1.0` records the request argument, which can be an opaque handle.
- Limit: the chain has no secret, so its tail can be deleted without trace unless you kept an anchor; keypaste keeps none (founder decision, 2026-10-22) [T-5].
- Limit: the log grows until archived ([archiving it](docs/mcp-setup.md#archiving-it)) and can hold sensitive agent-written text.

## Maintainer note

The reporting mailbox has been tested from an external address. GitHub private vulnerability reporting is currently disabled. KeePassLib provenance and local changes are recorded in [UPSTREAM.md](third_party/KeePassLib/UPSTREAM.md), and those of the BouncyCastle Twofish code it compiles in, which encrypts a Twofish vault, in [its own](third_party/bouncycastle-twofish/UPSTREAM.md). Reports to this project and upstream do not automatically reach one another.

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
[T-44]: THREATS.md#t-44--windows-hello-stands-in-for-the-master-password-after-a-lock
[T-51]: THREATS.md#t-51--a-notification-is-read-by-more-than-the-person
