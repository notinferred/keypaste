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

This page describes current source behavior unless a release is named. The public CLI/MCP release is `v0.2.0`; the desktop is available from source and has no public release. [RELEASE.md](docs/RELEASE.md) records distribution status.

Replace `v0.1.0`: env export can delete your vault; env rm and env set can act on the wrong entry; get can return the wrong password; env pull can delete an edit it never imported; moving the clock back can revive an expired approval. These defects are fixed in `v0.2.0` ([changelog](CHANGELOG.md#020)). Published archives are immutable, so the old downloads remain affected.

<!-- defects:0.2.0 -->
`v0.2.0` has three disclosed defects. In that release, saving while another program saves the same vault can undo its change. A retry can overwrite the other program's save with Keypaste's older copy; the lost change has no history entry. The condition requires overlapping saves within about two seconds.

In the same release, on Windows 11 24H2 and Windows Server 2025, a save can fail when another keypaste or KeePass program is saving at the same time. A transaction can reserve a temporary name's 8.3 alias even for an unrelated vault. After about two seconds of retries, Keypaste reports failure without committing the save. Older Windows builds accept the name.

Also, an agent asking for entry names on a loaded machine can be told to start keypaste agent for a process that is already running. The connection budget can expire before the bridge tries the pipe. The request releases no credential or entry names and writes nothing. `v0.1.0` has the first two defects. Repairs for all three are in `main` and remain unreleased; see the Unreleased changelog and STEPS F.6/F.9.
<!-- /defects:0.2.0 -->

| Version | Supported | Status |
|---|---|---|
| `main` | Yes | Fixes land here first |
| `0.2.x` | Yes | Current release line |
| `0.1.x` | No | Superseded; affected by the defects above |

There is no long-term support line before 1.0. Fixes enter `main` and the next release; older tags remain unchanged.

## Verifying a release

CLI/MCP downloads at `https://dl.keypaste.com/v<version>/` include `SHA256SUMS`, per-asset `.sha256` files and corresponding source; releases after `v0.2.0` add a manifest, `keypaste-<version>-manifest.json`, and an attestation bundle, `keypaste-<version>-provenance.sigstore.jsonl`. `release.yml` tests the NativeAOT binaries it uploads, including KeePassXC compatibility in both directions. `ci.yml` runs the unit suites against an ordinary build of the same commit. Desktop artifacts have the separate publication requirements in [RELEASE.md](docs/RELEASE.md).

The binaries are unsigned and un-notarized. Gatekeeper and SmartScreen behavior depends on the download path, machine and reputation. README documents the macOS quarantine limitation and manual workaround; its install blocks preserve quarantine. Signing and notarization remain O-0010. A Windows signature identifies a publisher but does not guarantee reputation-based prompts disappear.

Checksums detect corrupted or truncated downloads. Because the archive and checksum share an origin, an attacker who replaces both defeats that check.

Releases after `v0.2.0` also carry a GitHub build attestation, signed through Sigstore by `release.yml` running in `notinferred/keypaste` for the release tag, covering every asset and the manifest. `v0.2.0` and earlier have none. Checking one needs [GitHub CLI](https://cli.github.com/) (established with 2.86.0) and no GitHub account:

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

Substitute any other asset name. A changed byte, another repository or another workflow fails the check. The command fetches Sigstore's public trust root; to check offline, run `gh attestation trusted-root > trusted_root.jsonl` on a connected machine and add `--custom-trusted-root trusted_root.jsonl`. Without `--bundle`, `gh` fetches the attestation from GitHub and requires `gh auth login`. [verify-provenance.sh](scripts/verify-provenance.sh) `<version>` downloads a whole release and checks the manifest and every asset this way (D-0138).

An attestation shows which repository, workflow, tag and commit produced the bytes. It does not show that the source is safe, that the build is reproducible (O-0012) or that the release workflow was uncompromised ([THREATS.md](THREATS.md) T-21). README documents building from source with dependencies pinned by content hash in `packages.lock.json`.

## Security boundaries

[PRODUCT §3](docs/PRODUCT.md) owns the security laws. The master key stays in the local process. Agent releases require human approval or a user-written policy, are limited to an authorized field and lifetime, and require a local audit record. Errors deny release. Keypaste uses library cryptography and collects no telemetry on secret content or entry names. Explicit plaintext export has the limits described below. [THREATS.md](THREATS.md) records the threat model, mitigations and residual risks.

### The desktop app's master password field

`ConsoleSecretPrompt` reads characters into a clearable buffer without forming a password string. Desktop input arrives from Avalonia as immutable strings that cannot be wiped, including multi-character input-method events. The OS keyboard layer, input methods and keyloggers remain outside this boundary.

The desktop uses `MaskedInput` instead of `TextBox`, whose automation peer and undo history can expose or retain text. The control stores a character count and reports characters to the view model. Its accessibility surface exposes the placeholder and mask length without a value pattern; different passwords of the same length produce the same surface. The view model clears its buffer after successful unlock, wrong-password failure and locking. Argon2 receives a span without an additional complete password string. Clipboard paste is currently unsupported in this field.

### Values the desktop app shows, and values it copies

Entry lists show titles and groups. Selecting an entry shows its username, URL and notes. The Entries screen never displays its password; Copy reads it from the vault when requested. `keypaste get --show` provides explicit display. An environment value can be revealed individually while held. Its control avoids publishing the value through a `TextBlock` automation name, but screenshots, recordings, remote desktops and nearby observers can capture the visible value.

Copied secrets are cleared after twenty seconds if the clipboard still contains the copied value. Locking, quitting and Clear now also clear them. A pending clipboard write is cleared when it completes; quitting waits for that handoff. Forced termination, crashes, power loss and logout can leave a copied value behind.

Both Windows front ends request exclusion from Clipboard History and Cloud Clipboard. First-party consumers honor those formats; third-party managers can ignore them, and RDP or Citrix can copy the value to another machine. Windows does not restrict which local process may read the clipboard. Keypaste sets no equivalent history-exclusion marking on macOS or Linux (O-0019). On X11 and Wayland, `xclip` or `wl-copy` can continue serving the value after Keypaste exits.

### Editing your vault from the app

The app and CLI write through the same core library and KeePassXC compatibility boundary. If the file changes while the app holds an unlocked copy, the app refuses to save. Lock and unlock to load the other change before making yours again. This protects changes absent from the app's in-memory history.

## Memory and authorization limits

Clearable master-password buffers and zeroed derived bytes reduce retention without guaranteeing in-memory secrecy. Garbage collection can leave relocated copies; immutable strings, swap, hibernation and dumps can retain values. A debugger or process running as the same user can inspect memory. Keypaste does not use `SecureString`, which does not encrypt memory on Linux or macOS.

Human-approved fields are cached in a clearable buffer for the grant lifetime, up to `--max-ttl` (five minutes by default). Expiry, disconnect and disposal clear owned buffers, and lookup checks expiry before returning a value. Wall and monotonic clocks jointly prevent a clock rollback or suspension from extending the grant. The original vault value was an immutable string, and the released value crosses a local pipe as plaintext into the MCP client. Keypaste cannot erase the client's copies, revoke the credential at its issuer or invalidate sessions created with it. Policy releases are evaluated on each request and do not populate this cache.

Entry resolution currently materializes standard fields, including passwords, through `Vault.ReadEntries` before approval. Authorization restricts the field released in the response; denial does not establish that the approver never read a secret into memory. `keypaste agent` keeps its vault unlocked until it stops and has no idle auto-lock.

A policy rule releases matching credentials without a prompt. The approver prints each release and the audit names the rule; the agent's reason is recorded without human review. Rules match the vault's current contents, so anyone able to write into a covered group can change what a rule authorizes. `keypaste policy ls` explains rule meaning but does not list currently covered entries.

Client labels are unauthenticated. A local program can start a bridge with another program's label and match its rules. Keep the policy file out of synced directories: redirecting `KEYPASTE_HOME` into one permits another machine to change local grants. Linux and macOS reject policy files writable by other users; Windows has no equivalent check. Policy is read at agent startup, so changes require a restart.

## Plaintext and file limits

Inline values in `keypaste env set project KEY=value` can enter shell history and remain visible in process arguments. The command warns on stderr. Use `keypaste env set project KEY` with a prompt or pipe to avoid that argument exposure; warning suppression remains O-0009.

Updating an existing variable preserves its previous value in encrypted KDBX history, subject to KeePass's ten-item history limit. KeePassXC can display it; Keypaste currently cannot. Removing the entry removes its history from the active vault, and re-adding starts a new history. Credential rotation still requires revocation at the issuer.

`keypaste run` passes values in the child's environment. Process inspection, descendants, crash reporters and application logs can expose them. Keypaste itself writes no environment file; `verify-run-injection.sh` checks that temporary directories remain empty. On Windows, closing the console can terminate Keypaste while leaving the child running. Keypaste forwards supported termination signals and waits for the child without escalating to a hard kill, so a child that ignores termination can keep it waiting.

`keypaste env pull` deletes only the imported source when its path and content still match. Changed, replaced, linked or removed sources are retained or reported. Removal first moves the file beside itself and verifies it under that name to protect an editor's intervening save. Hard links, bind mounts, `subst`, mapped-drive versus UNC aliases and Windows 8.3 names remain unresolved identities; content checks provide additional protection.

Deletion removes a directory entry without overwriting storage. SSD remapping, copy-on-write filesystems, snapshots, backups, editor files and Git history can retain plaintext. Keypaste warns about a `.git` ancestor and offers no secure-erasure claim. Rotate credentials that were committed or shared.

`keypaste env export --dotenv` explicitly writes plaintext after the user selects a format, destination and confirmation. It warns before writing, refuses existing destinations unless `--force` is supplied, and identifies a `.git` ancestor. Linux and macOS create owner-readable files; Windows inherits directory permissions. Export refuses the source vault and any other KeePass vault, including with `--force`. Symbolic links, junctions and ancestor-directory links are resolved; the alias limits described above remain. Exported files inherit the storage and deletion risks of other plaintext files.

## Audit limits

Keypaste appends agent-access records without rotating, trimming or rewriting the log. Hash chaining detects edits, removals, insertions and foreign records unless an attacker recomputes the chain. The chain has no secret, and deleting its tail leaves no following record to expose the deletion. `keypaste log verify --expect <hash>` checks an independently retained anchor; Keypaste does not store that anchor beside the log. The log grows without bound. Linux and macOS create owner-readable logs; Windows inherits directory permissions.

Audit arguments are sanitized and bounded. `args.entry` prefers the approver's resolved path and is capped at 128 characters. The requested TTL is recorded separately from the effective grant or remaining cache lifetime. The reason retains a 200-character excerpt, original length and SHA-256. Released field values are not deliberately logged, but agent-written arguments can contain sensitive text. Treat the local log as sensitive data. `v0.1.0` records the sanitized request argument, which can be an opaque handle.

If the required audit record cannot be appended, Keypaste refuses the credential response, including a policy-authorized release. Processes running as the user can still inspect memory, keystrokes and clipboard contents; Keypaste cannot defend a compromised local account.

## Maintainer note

The reporting mailbox has been tested from an external address. GitHub private vulnerability reporting is currently disabled. KeePassLib provenance and local changes are recorded in [UPSTREAM.md](third_party/KeePassLib/UPSTREAM.md). Reports to this project and upstream do not automatically reach one another.
