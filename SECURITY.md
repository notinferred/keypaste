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

This page describes current source behavior unless a release is named. The public CLI/MCP release is `v0.3.0`; the desktop is available from source and has no public release. [RELEASE.md](docs/RELEASE.md) records distribution status.

Replace `v0.1.0`: env export can delete your vault; env rm and env set can act on the wrong entry; get can return the wrong password; env pull can delete an edit it never imported; moving the clock back can revive an expired approval. These defects were fixed in `v0.2.0` and the current download is `v0.3.0` ([changelog](CHANGELOG.md#030)). Published archives are immutable, so the old downloads remain affected.

**If you installed `v0.2.0`, replace it.** In that release, saving while another program saves the same vault can undo its change. A retry could overwrite the other program's save with Keypaste's older copy, and the lost change had no history entry; overlapping saves within about two seconds were enough. `v0.3.0` re-reads the vault between retries and refuses rather than overwriting.

Two further `v0.2.0` defects lost nothing. On Windows 11 24H2 and Windows Server 2025, a save can fail when another keypaste or KeePass program is saving at the same time, because those builds refuse a temporary name whose 8.3 alias another transaction holds; `v0.3.0` gives each process a temporary directory nothing else names. And an agent asking for entry names on a loaded machine can be told to start keypaste agent for a process that is already running, because the connection budget could expire before the bridge tried the pipe; `v0.3.0` tries the pipe first. `v0.1.0` has the first defect too. Published archives are immutable, so both older downloads remain affected.

| Version | Supported | Status |
|---|---|---|
| `main` | Yes | Fixes land here first |
| `0.3.x` | Yes | Current release line |
| `0.2.x` | No | Superseded; affected by the defects above |
| `0.1.x` | No | Superseded; affected by the defects above |

There is no long-term support line before 1.0. Fixes enter `main` and the next release; older tags remain unchanged.

## Verifying a release

CLI/MCP downloads at `https://dl.keypaste.com/v<version>/` include `SHA256SUMS`, per-asset `.sha256` files and corresponding source; `v0.3.0` adds a manifest, `keypaste-0.3.0-manifest.json`, and an attestation bundle, `keypaste-0.3.0-provenance.sigstore.jsonl`; `v0.2.0` and earlier have neither. `release.yml` tests the NativeAOT binaries it uploads, including KeePassXC compatibility in both directions. `ci.yml` runs the unit suites against an ordinary build of the same commit. Desktop artifacts have the separate publication requirements in [RELEASE.md](docs/RELEASE.md).

The binaries are unsigned and un-notarized. Gatekeeper and SmartScreen behavior depends on the download path, machine and reputation. README documents the macOS quarantine limitation and manual workaround; its install blocks preserve quarantine. Signing and notarization remain O-0010. A Windows signature identifies a publisher but does not guarantee reputation-based prompts disappear.

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

Substitute any other asset name. A changed byte, another repository or another workflow fails the check. A version that publishes the desktop packages carries a second manifest and bundle, `keypaste-app-<version>-manifest.json` and `keypaste-app-<version>-provenance.sigstore.jsonl`, in the same prefix; an MSI or AppImage is checked with the same command against its own bundle, and the signer workflow is still `release.yml`, which attests what it publishes rather than what compiled it. The command fetches Sigstore's public trust root; to check offline, run `gh attestation trusted-root > trusted_root.jsonl` on a connected machine and add `--custom-trusted-root trusted_root.jsonl`. Without `--bundle`, `gh` fetches the attestation from GitHub and requires `gh auth login`. [verify-provenance.sh](scripts/verify-provenance.sh) `<version>` downloads a whole release and checks the manifest and every asset this way (D-0138); `--component app` does the same for the desktop packages.

An attestation shows which repository, workflow, tag and commit produced the bytes. It does not show that the source is safe, that the build is reproducible (O-0012) or that the release workflow was uncompromised ([THREATS.md](THREATS.md) T-21). README documents building from source with dependencies pinned by content hash in `packages.lock.json`.

## Security boundaries

[PRODUCT §3](docs/PRODUCT.md) owns the security laws. The master key stays in the local process. Agent releases require human approval or a user-written policy, are limited to an authorized field and lifetime, and require a local audit record. Errors deny release. Keypaste uses library cryptography and collects no telemetry on secret content or entry names. Explicit plaintext export has the limits described below. [THREATS.md](THREATS.md) records the threat model, mitigations and residual risks.

The current desktop, terminal approver and `keypaste run` do not share an unlock session. In source, one vault has one owner: the desktop and `keypaste agent` each claim the vault before its password is read, and the second is refused naming the first (THREATS.md T-29). Desktop lock ends its own session and stops serving agents; a terminal approver on another vault remains unlocked until stopped, and `run` separately opens and closes its vault before launching a child. The shared-session behavior in [PRODUCT](docs/PRODUCT.md) is a delivery requirement, not a current guarantee. It will govern new releases and launches; it cannot recall copied values, erase client transcripts or revoke credentials at their issuers.

### What unlocks a vault

A vault is unlocked by a master password, and in source by a master password and a keyfile, or by a keyfile alone where the vault was made that way elsewhere. keypaste opens every keyfile form KeePass accepts: an XML keyfile, a 32-byte file, a 64-character hex file, and any other file keyed by the hash of its contents. That last form is one edit away from losing the vault for good, so keypaste names it on stderr each time it opens such a vault and will never create one (T-28). The CLI names the file per command as `--keyfile <path>` or in `KEYPASTE_KEYFILE` and records nothing; the desktop, in source, remembers where the keyfile each vault last opened with is, beside the vault path in `recent.toml`, and never its contents. All three places are readable by anything running as the same user (T-27). A keyfile protects a vault whose file is copied away — a backup, a synced folder, a lost disk — and not a machine somebody is already executing on. Support cannot reconstruct a lost keyfile any more than a lost password. In source, `keypaste access` and the desktop's Settings change a vault's master password and attach, replace or remove a keyfile; the desktop asks for the current password first, even though the vault is open. It attaches only a keyfile that already exists, in the XML, 32-byte or hex form, and never writes key material; it never takes a password away from a vault that has one. keypaste checks at run time that it can read a known XML keyfile; a build that cannot refuses every XML keyfile rather than key the vault with the file's hash (D-0294). The file a change replaces, and every copy taken before it, still opens with the old credentials, so after changing a password or keyfile you believe exposed, delete those copies; a running `keypaste agent` keeps the vault it opened until restarted. In the current download, `v0.3.0`, there is no keyfile support and no verb that changes a vault's password or keyfile.

### Secret input in the desktop app

`ConsoleSecretPrompt` reads characters into a clearable buffer without forming a password string. Desktop input arrives from Avalonia as immutable strings that cannot be wiped, including multi-character input-method events. The OS keyboard layer, input methods and keyloggers remain outside this boundary.

The desktop uses `MaskedInput` instead of `TextBox`, whose automation peer and undo history can expose or retain text. The control stores a character count and reports characters to a clearable buffer. Its accessibility surface exposes the placeholder and mask length without a value pattern; different secrets of the same length produce the same surface. Seven fields use it: the master password, the new master password and its confirmation when a vault is created, a new entry's password and a replacement for an existing one, and a new variable's value and a replacement for an existing one. Every one of them is held to that same-surface check. Each view model clears its buffers after a successful unlock, a wrong-password failure, a create whether it succeeded or was refused, a saved or cancelled entry or variable, and locking. Argon2 receives a span without an additional complete password string.

The four entry-password and variable-value fields answer `Ctrl/Cmd+V`; the seven master-password fields do not, and no code path reads clipboard text on their behalf. A paste arrives as one immutable string that cannot be wiped, as typed input already does. One trailing line break is dropped, because copying a token out of a terminal or a file usually brings one and a credential ending in a newline fails elsewhere. Anything else a keyboard cannot produce is refused outright and the field says so, rather than being stripped: a silently altered value would be stored as the secret without anything on screen showing the difference. A bidi override or zero-width character inside a secret is kept, because it is part of the secret; the rules below govern such characters where text is drawn.

### Values the desktop app shows, and values it copies

Entry lists show titles and groups. Selecting an entry shows its username, URL and notes, which are drawn through a rule that keeps ordinary punctuation and line breaks and replaces anything that can make text misrepresent itself, including bidi overrides, zero-width and tag characters, and other control characters. Titles and group paths keep the stricter name rule, because they address an entry. The Entries screen shows a password as dots; holding it reads the current password from the vault and draws it until release, and Copy reads it when requested. A replacement is entered into a masked field rather than shown. An environment value can be revealed individually while held, and so can a password an entry no longer uses, listed in that entry history beside the current values; each of the three copies through the same clearing countdown. Its control avoids publishing the value through a `TextBlock` automation name, but screenshots, recordings, remote desktops and nearby observers can capture the visible value.

Copied secrets are cleared after twenty seconds if the clipboard still contains the copied value. Locking, quitting and Clear now also clear them. A pending clipboard write is cleared when it completes; quitting waits for that handoff. Forced termination, crashes, power loss and logout can leave a copied value behind.

Both Windows front ends request exclusion from Clipboard History and Cloud Clipboard. First-party consumers honor those formats; third-party managers can ignore them, and RDP or Citrix can copy the value to another machine. Windows does not restrict which local process may read the clipboard. Keypaste sets no equivalent history-exclusion marking on macOS or Linux (O-0019). On X11 and Wayland, `xclip` or `wl-copy` can continue serving the value after Keypaste exits.

### Editing your vault from the app

The app and CLI write through the same core library and KeePassXC compatibility boundary. If the file changes while the app holds an unlocked copy, the app refuses to save. Lock and unlock to load the other change before making yours again. This protects changes absent from the app's in-memory history. An already-open terminal approver also retains its snapshot until reopened.

In the current download, `v0.3.0`, deletion permanently removes the entry and its history, and there is no recycle bin. In source, deleting moves the entry to the vault's KDBX recycle bin instead, so it keeps its identity, fields and history until it is purged or the bin is emptied; a vault whose owner turned the recycle bin off in KeePassXC still deletes permanently, and keypaste says which it did. The desktop's Trash screen restores one, and erases one behind a confirmation of its own; the trash shows names and never a stored value. Erasing a value keypaste wrote therefore takes two deliberate acts rather than one. A vault that has recycled anything is written as KDBX 4.1 rather than 4.0, because the field recording where an entry came from exists only in 4.1; KeePassXC 2.7 and KeePass 2.48 and later read it, and older readers do not. There is no concurrent-edit merge. Entry history and the recycle bin are inside the vault, so neither protects against losing the file. In source, a save that replaces an existing vault first copies the file it is replacing into `<vault>.backups` beside it, keeping the last five; the first save after each unlock takes one, and no more often than once every fifteen minutes. A save whose copy cannot be written does not happen, and there is no setting that turns that off. The copies are the vault's own encrypted bytes, so each opens in KeePassXC with the master password it was made under — a later password change does not reach the copies already taken, and the change itself keeps one more under the old password — and none of them recovers a forgotten password. Dropping one past the fifth deletes a directory entry and no more, as every deletion here does. Five copies beside the file do not survive losing the disk or the directory: keep protected copies elsewhere as well.

In source, the desktop restores one of those copies from its locked unlock screen, asking only for the master password that copy was made under, and replaces the vault with the copy's exact bytes; the file it replaces is kept as a backup first and nothing is pruned. A restored vault therefore opens with the backup's password, so somebody who knows an earlier master password and can write to the vault's directory can roll the vault back to that copy from the unlock screen. They could already read that copy in KeePassXC, the vault they replaced is kept beside it, and asking for the current password as well would block the recovery this exists for: a changed password nobody remembers. Before the confirmation the check shows a time and counts and never a name or a value (T-26). A kept file that was itself damaged takes one of the five places and can later displace a good copy, and a vault file that briefly cannot be read, as one another program is in the middle of saving cannot, is retried within the same retry budget a save has; only one still unreadable after that budget is left unreplaced, because a file that cannot be read cannot be kept. KeePassXC and a terminal `keypaste agent` hold their own copy of the vault and do not notice a restore. The desktop's export writes one more exact encrypted copy where the person says: another offline guessing target under the same master password, outside keypaste's retention and never deleted by it.

## Memory and authorization limits

Clearable master-password buffers and zeroed derived bytes reduce retention without guaranteeing in-memory secrecy. Garbage collection can leave relocated copies; immutable strings, swap, hibernation and dumps can retain values. A debugger or process running as the same user can inspect memory. Keypaste does not use `SecureString`, which does not encrypt memory on Linux or macOS.

Human-approved fields are cached in a clearable buffer for the grant lifetime, up to `--max-ttl` (five minutes by default). Expiry, disconnect, disposal and, in source, a revoke from the desktop's Agent Activity clear owned buffers, and lookup checks expiry before returning a value. Wall and monotonic clocks jointly prevent a clock rollback or suspension from extending the grant. The original vault value was an immutable string, and the released value crosses a local pipe as plaintext into the MCP client. Keypaste cannot erase the client's copies, revoke the credential at its issuer or invalidate sessions created with it. Policy releases are evaluated on each request and do not populate this cache.

Entry resolution currently materializes standard fields, including passwords, through `Vault.ReadEntries` before approval. Authorization restricts the field released in the response; denial does not establish that the approver never read a secret into memory. `keypaste agent` keeps its vault unlocked until it stops and has no idle auto-lock.

A policy rule releases matching credentials without a prompt. The approver prints each release and the audit names the rule; the agent's reason is recorded without human review. Rules match the vault's current contents, so anyone able to write into a covered group can change what a rule authorizes. `keypaste policy ls` explains rule meaning but does not list currently covered entries.

Client labels are unauthenticated. A local program can start a bridge with another program's label and match its rules. Keep the policy file out of synced directories: redirecting `KEYPASTE_HOME` into one permits another machine to change local grants. Linux and macOS reject policy files writable by other users; Windows has no equivalent check. Policy is read at agent startup, so changes require a restart.

## Plaintext and file limits

Inline values in `keypaste env set project KEY=value` can enter shell history and remain visible in process arguments. The command warns on stderr. Use `keypaste env set project KEY` with a prompt or pipe to avoid that argument exposure; warning suppression remains O-0009.

Updating an existing variable preserves its previous value in encrypted KDBX history, subject to KeePass's ten-item history limit. KeePassXC can display it; the source-built Keypaste desktop can display and restore it through the corresponding entry's history pane. In `v0.3.0` removing the entry removes its history from the active vault. In source, removing it moves the entry and its history to the recycle bin, where both remain readable to anything that can open the vault until the entry is purged. Re-adding starts a new history either way. Credential rotation still requires revocation at the issuer.

`keypaste run` closes the vault before starting the child and passes values in the child's environment. In source, the desktop's Run and Open terminal pass a project's set the same way, into the environment of the terminal they open, which hands it to everything run there; the confirmation names the command and directory from `~/.keypaste/projects.json`, which any program running as you can edit, so read it before pressing Start. Locking the desktop does not stop the child or erase its environment. Process inspection, descendants, crash reporters and application logs can expose them. Keypaste itself writes no environment file; `verify-run-injection.sh` checks that temporary directories remain empty. On Windows, closing the console can terminate Keypaste while leaving the child running. Keypaste forwards supported termination signals and waits for the child without escalating to a hard kill, so a child that ignores termination can keep it waiting.

`keypaste env pull` deletes only the imported source when its path and content still match. Changed, replaced, linked or removed sources are retained or reported. Removal first moves the file beside itself and verifies it under that name to protect an editor's intervening save. Hard links, bind mounts, `subst`, mapped-drive versus UNC aliases and Windows 8.3 names remain unresolved identities; content checks provide additional protection.

Deletion removes a directory entry without overwriting storage. SSD remapping, copy-on-write filesystems, snapshots, backups, editor files and Git history can retain plaintext. Keypaste warns about a `.git` ancestor and offers no secure-erasure claim. Rotate credentials that were committed or shared.

`keypaste env export --dotenv` explicitly writes plaintext after the user selects a format, destination and confirmation. It warns before writing, refuses existing destinations unless `--force` is supplied, and identifies a `.git` ancestor. Linux and macOS create owner-readable files; Windows inherits directory permissions. Export refuses the source vault and any other KeePass vault, including with `--force`. Symbolic links, junctions and ancestor-directory links are resolved; the alias limits described above remain. Exported files inherit the storage and deletion risks of other plaintext files.

## Audit limits

Keypaste appends agent-access records without rotating, trimming or rewriting the log. Hash chaining detects edits, removals, insertions and foreign records unless an attacker recomputes the chain. The chain has no secret, and deleting its tail leaves no following record to expose the deletion. `keypaste log verify --expect <hash>` checks an independently retained anchor; Keypaste does not store that anchor beside the log. The log grows without bound. Linux and macOS create owner-readable logs; Windows inherits directory permissions.

Audit arguments are sanitized and bounded. `args.entry` prefers the approver's resolved path and is capped at 128 characters. The requested TTL is recorded separately from the effective grant or remaining cache lifetime. The reason retains a 200-character excerpt, original length and SHA-256. Released field values are not deliberately logged, but agent-written arguments can contain sensitive text. Treat the local log as sensitive data. `v0.1.0` records the sanitized request argument, which can be an opaque handle.

If the required audit record cannot be appended, Keypaste refuses the credential response, including a policy-authorized release. Processes running as the user can still inspect memory, keystrokes and clipboard contents; Keypaste cannot defend a compromised local account.

## Maintainer note

The reporting mailbox has been tested from an external address. GitHub private vulnerability reporting is currently disabled. KeePassLib provenance and local changes are recorded in [UPSTREAM.md](third_party/KeePassLib/UPSTREAM.md). Reports to this project and upstream do not automatically reach one another.
