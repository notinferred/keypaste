# Build plan

This plan owns the committed tasks, grouped by product track: each task's dependencies, detail and acceptance evidence for [PRODUCT](PRODUCT.md) v1.11 (D-0416). [ROADMAP](../ROADMAP.md) owns direction, track order and which tasks each milestone needs. [FEATURES](FEATURES.md) owns the capability inventory, [RELEASE](RELEASE.md) distribution evidence, [BACKLOG](BACKLOG.md) optional work, and the [step records](steps/README.md) what each completed task did. The license remains AGPL-3.0.

This file holds open work only. Finishing a task removes it from here and adds its record under [steps](steps/README.md). No row is authorized by appearing here: the founder selects what is built, and an instruction to build the next task takes the first ready code row of ROADMAP's current milestone, in track order. No backlog item is automatically eligible.

## Selection and evidence

No open row is detailed: each names a bounded outcome and the dependencies its own implementation or verifier needs, and is detailed when it is selected, against the code as it then is. Tasks are not marked implemented from a document, reader, mock response or consuming screen. Name the producer, transport, consumer and user action exercised, and retain the source/version and limitations of the observation.

Needs are build dependencies. Ships after names publication gates. External signing identities are inputs, not a queue of enrollment code. A ready row does not authorize publication, account changes or messages. Preserve the secret-path tests, real KeePassXC compatibility, stale-write refusals and release integrity checks while changing product scope.

## K and B — Build infrastructure

These rows serve every track: a development machine builds nothing, CI runs what a change can break, and the scripts, tests and binaries lose their duplicates. ROADMAP places them in 0.5.0 by founder direction of 2026-09-30.

- [ ] **F.51 — Find why the app driver crashed after a refused unlock on macOS.** Needs: none.
  - Observed once, in dev run 37397846023 at `756cb89` on `task/v11`, job `test (macos-15)`, in `verify-session-lifecycle.sh`: with `keypaste agent` holding the vault, `Keypaste.AppDriver hold` printed the app's refusal naming the agent and its owner line, then failed with `NullReferenceException` and exit 3 where 1 was expected. It passed on Linux and Windows in that run and on the macOS rerun 37400993297, and failed in none of the 60 failed runs before it.
  - After the refusal the driver disposes the unlock screen, the Connect section and the authority. It prints an exception's type and message but not where it was thrown, so the first experiment is to print that.
  - Verify: the site named, a regression that fails as the observation did, and its repair.

## T1 — Everyday vault use and recovery

Custom fields and tags are ordinary KeePass data: keypaste writes them as KeePassXC reads them, and every row keeps the permanent compatibility gates (PRODUCT §4.6). C.1a's tags include the project tags T4 builds on. The main screens show everyday password-manager work; advanced controls and security recommendations live in Settings (PRODUCT §5.8).

- [ ] **N.6 — Use plain words everywhere.** Needs: N.1a, N.3.
  - The app, the CLI help and the bridge's refusal texts replace stdio, exposure, grant, session and KDBX-entry wording with plain terms.
  - The approval prompts keep every element T-2's evidence relies on.
- [ ] **N.13 — Search and show fields from the terminal.** Needs: V.7a.
  - `search` looks at names only (D-0278).
  - `show` masks values and prints none without `--reveal`.

## T2 — One shared unlock session

U.1–U.3 and 4.4b gave the app, `keypaste agent` and the bridge one owner and one lock (D-0309 to D-0321), and N.10 made every CLI verb that saves take the owner's claim (D-0382). N.16 made the app say when another program saved the vault and reload it in the same session (D-0412). What remains is approving a terminal edit in the unlocked app instead of refusing it.

- [ ] **N.11 — Approve a terminal edit in the unlocked app.** Needs: N.10.
  - `set`, `add`, `rm` and the env writers send the change to the vault's owner, which asks in its prompt window and writes through its session.
  - No master password crosses the pipe, and `access` stays refused.

## T4 — Project environments

A variable becomes an env-named custom field on an ordinary entry. The entry's own tag, `env:<project>` or `env:<project>:<environment>`, puts it in that environment (D-0367). Every set still leaves only through `EnvResolution`, whole or not at all (D-0337), and every child starts through `EnvLaunch`.

keypaste keeps no compatibility with its own releases before 0.5.0 (D-0416): an untagged entry under `env/<project>`, the layout 0.3.0 wrote, is an ordinary entry and no variable. The syntax of `-p`, of `kp://<project>/<environment>/<KEY>` and of token scopes is kept.

- [ ] **E.1d — Run a project from the app on macOS.** Needs: G.5.
  - Terminal.app runs the bundle's `keypaste run --session` for the project.
  - The command is passed as arguments and never spliced into script text.
  - The app's own run prompt is the only question.
  - It amends D-0340's "none on macOS".
- [ ] **P.3b1 — Resolve same-entry placeholders in released values.** Needs: C.1b.
  - `{TITLE}`, `{USERNAME}`, `{PASSWORD}`, `{URL}`, `{NOTES}` and `{S:<field>}` resolve within the entry the person approved, to KeePassXC's depth of 10. So `OPENAI_API_KEY={PASSWORD}` needs no copy.
  - `{REF:…}` stays refused; it is P.3b2 in BACKLOG.

## T3 — AI requests in the app

Every release still needs a person's answer or a rule they wrote, and the bridge stays vault-free (PRODUCT §§2 and 3.2). The bridge, the in-app prompt (4.4), the Agents screen (4.3b) and connecting from the app (2.6a) are reused.

- [ ] **C.5b — Expose tagged projects to agents.** Needs: C.1b, C.5a1.
  - The default exposure becomes `env/**` plus `tag:env:*`, and `--expose` and policy rules accept tag selectors.
  - An entry reached only through a tag exposes only its env fields.
  - Listings carry field names and project tags, never a value.
  - THREATS gains T-38: one secret shared by two environments.
- [ ] **G.2 — Connect every AI tool on this machine.** Needs: G.1.
  - `keypaste setup` and the app connect Claude Code, Codex, VS Code and Gemini CLI through their own commands.
  - Cursor, Claude Desktop and Windsurf are connected by a verified edit of their configuration file, with a backup.
  - One confirmation, or `--yes`, covers the whole run.
  - The registered bridge path survives upgrades.
  - `setup` exits 0 when any tool is connected.
- [ ] **N.3 — Show apps, requests, allowances and history on Agents.** Needs: N.1a, G.2.
  - Each detected tool gets one Connect.
  - The screen shows the waiting request, allowances with their time left and an End button, and the history.
  - The first level has no more than twelve controls.
- [ ] **G.4b — Tell the person when a locked app is asked.** Needs: G.4a.
  - A bridge that finds no owner has the running app show one notice, with no password field.
  - Only the person's click opens the unlock screen, and the request itself is still denied.
  - T-7 is amended.
- [ ] **G.3 — Add `keypaste doctor`.** Needs: G.1, G.2. Read-only checks, each with a fix line:
  - the versions match;
  - the chosen vault exists;
  - each tool's registration runs and survives upgrades;
  - the owner answers;
  - the settings files parse;
  - `keypaste` is on PATH.
- [ ] **G.8 — Set in the app what each agent can see and run.** Needs: G.1, C.5b.
  - Each connected app's row says what the app can see and whether it may run commands.
  - The owner applies the intersection with the bridge's flags, so a flag an agent edits into its own configuration can only narrow access.
- [ ] **G.9 — Have the owner write the audit line of every request.** Needs: none.
  - The app and `keypaste agent` write the audit line for every listing, credential, run and token request they answer before they reply, and refuse one they cannot record, so a request made straight to the pipe is recorded too (D-0407, T-14).
  - The bridge's line stays as a second witness.
- [ ] **N.8 — Make `keypaste` alone show where things stand.** Needs: G.1.
  - On a terminal it shows the version, the chosen vault and who holds it, the connected tools and one next step.
  - It asks for no password.
- [ ] **N.9 — Give each CLI concept one name.** Needs: none.
  - `approve` for the terminal approver, `rules` for `policy.toml`, and `clients` for per-client choices.
  - The old spellings are removed, with no aliases (D-0416).

## T5 — Desktop delivery

The Windows MSI, the Linux AppImage and the macOS DMG are internal candidates. Existing install and upgrade observations remain useful, but they are repeated for the product that [ROADMAP](../ROADMAP.md) puts in 0.5.0.

Package managers and agent marketplaces carry a version only after it is published. keypaste.com keeps its "no `curl | sh`" stance: the one-command routes are the package managers and the signed installers.

- [ ] **G.5 — Carry the CLI on PATH in every desktop install.** Needs: none.
  - The MSI adds its folder to the per-user PATH and removes it on uninstall.
  - The AppImage dispatches `cli` as it does `mcp`.
  - Settings links the CLI into the terminal on Linux and macOS.
  - `install-desktop.yml` and `upgrade-desktop.yml` check `keypaste --version` in a new shell.
- [ ] **3.5a — Enable the macOS signing identity (H-0015).** Human. Needs: none.
  - External Apple Developer enrollment as keypaste.
  - Repository-scoped Developer ID Application and notarization credentials.
- [ ] **3.5b — Sign and notarize macOS release payloads.** Needs: 4.7a2. Input: 3.5a's credentials.
  - First, a branch preflight with ad-hoc signing settles a bundle layout `codesign` can seal, and the hardened-runtime entitlements. The CoreCLR payload may have to move out of `Contents/MacOS`.
  - Then the app is signed inside-out, notarized and stapled.
  - `spctl` accepts it, and a changed byte is refused.
- [ ] **4.7e — Install and preserve the macOS desktop candidate.** Needs: 4.7a2, E.1d.
  - These are the macOS legs of `install-desktop.yml` and `upgrade-desktop.yml`: install, first run, vault acts, a project run, and an upgrade that keeps the vault and settings.
  - Publication moves into 4.7c2, and the Homebrew cask into 3.7a.
- [ ] **R.1a — Exercise the integrated desktop candidate.** Needs: 9.4, 4.6, F.15, U.2, U.3, 4.4b, 4.3b, 2.6a, E.1b, E.1c, F.2b3a, F.2b3b, V.7a, V.7b, C.2, N.1a, N.1b, N.2, N.3, N.4, N.5, N.6, N.7, N.10, N.12, C.1a, C.1b, C.1c, C.3, C.4, E.1d, C.5a, C.5b, G.1, G.2, G.4a, G.5, G.8, G.9, N.16, V.11, 4.7a2.
  - Install the internal Windows MSI, macOS DMG and Linux AppImage built at a named commit.
  - In each installed app, complete T1–T4 with a disposable vault, a real MCP client and a real child process. The acts:
    1. Open a KeePassXC database from the first run.
    2. Keep a key as a field, and review one flagged in notes.
    3. Tag its entry into a project and run the project.
    4. Edit the value and see the next run receive it.
    5. Connect a client without a vault path, and approve and deny its requests.
    6. Restore a revision, a deletion and a backup.
    7. Change access.
    8. Lock, and see later releases refused as `vault-locked`.
  - Each act records the OS and version, the package and its hash, the commit and its audit lines.
  - An unreached act leaves the row open.
- [ ] **4.7c2 — Publish and verify the first desktop release as 0.5.0.** Needs: 4.7c1, 3.5b, 4.7a2, 4.7e, G.5. Ships after: R.1a. Inputs: a verified Microsoft Artifact Signing identity as keypaste (H-0017), or the recorded cloud-HSM fallback, and 3.5a's Apple credentials.
  - Prove Windows signing with changed-byte refusal, and macOS notarization.
  - `osx-arm64` declares the `.app.zip` and the DMG. `require-release-assets.sh --publishable` requires every declared package offered, and `verify-desktop-candidate.sh` stages only the DMG, so either the zip stops being a declared package or the candidate check accepts each declared `osx-arm64` kind.
  - Then publish, through `release.yml`, immutable Windows, macOS and Linux desktop packages and the matching CLI/MCP archives as 0.5.0. This renames CHANGELOG's Unreleased section and sets the version.
  - Verify from anonymous downloads: public hashes, provenance, signatures and notarization, and an upgrade that keeps the vault, `~/.keypaste` and the recent list.
  - Rehearsal certificates, unpublished artifacts and source builds do not pass.
- [ ] **L.3 — Write the safe-agent guide.** Needs: none.
  - How to run Claude Code, Codex and Cursor so the agent cannot write `~/.keypaste` or its own MCP configuration and cannot read other processes, on each system, and what keypaste still cannot stop (T-14, T-35, T-36).
- [ ] **R.1b — Round-trip a tagged entry through the phone apps (H-0024).** Human. Needs: none.
  - Tag an entry and give it a protected env field in keypaste, edit that entry in KeePassium, Strongbox and KeePassDX, and confirm keypaste still reads the tag, the field and its protection; record each app's version.
- [ ] **L.1 — Make the released app understandable and reachable.** Needs: 4.7c2, L.3.
  - README, keypaste.com and the guides lead with the v1.8 positioning and name 0.5.0.
  - They link its three desktop downloads, hashes and verification steps.
  - They take a new user from the download to a working vault, a project run and a connected agent, as that version behaves.
  - They give the issue route and the private security-reporting route.
  - The launch copy says what keypaste adds over Varlock's KeePass plugin, Strongbox MCP and 1Password Environments, and links the safe-agent guide (L.3).
  - The guides name only the phone apps R.1b's round-trip passed.
  - Published claims are checked against the published binaries (D-0036).
  - The install blocks move from `keypaste-mcp` to `keypaste mcp`, and `verify-install.sh`'s bridge check moves with them ([F.48](steps/F.48.md)).
  - No announcement, message or signup mail is authorized by this row.
- [ ] **R.1 — Accept the installed local product.** Needs: 4.7c2, L.1.
  - Repeat every R.1a act on clean Windows, macOS and Linux desktops, from public 0.5.0 bytes checked against their hashes and provenance.
  - The acts are made offline after installation, with no account.
  - `install.yml` passes on the four CLI/MCP targets.
  - RELEASE records Installation-verified only for the targets that passed.
- [ ] **3.7a — Publish Homebrew installation and updates.** Needs: none. Ships after: 4.7c2. Input: H-0023.
  - A formula for the CLI and a cask for the app, both generated from completed release manifests.
  - Client registrations survive `brew upgrade`.
- [ ] **3.7b — Publish Scoop installation and updates.** Needs: none. Ships after: 4.7c2. Input: H-0023.
  - A Scoop bucket entry for the Windows payloads.
- [ ] **3.7c — Publish winget installation and updates.** Needs: none. Ships after: 4.7c2. Input: H-0023.
  - winget manifests for the signed MSI, with a stable publisher identity.
  - Closes O-0011 with its siblings.
- [ ] **G.6a — Offer keypaste as a Claude Code plugin.** Needs: G.1. Ships after: 4.7c2.
  - A marketplace manifest, the bridge registration, and a skill telling agents to use keypaste rather than ask for a pasted secret.
  - It is kept on a branch until then, because a push to `main` publishes it.
- [ ] **G.6b — Offer a Claude Desktop extension.** Needs: G.1. Ships after: 4.7c2.
  - An `.mcpb` bundle whose small launcher starts the installed bridge rather than carrying its own copy.
- [ ] **G.6c — List keypaste in the MCP Registry.** Needs: G.6b. Ships after: 4.7c2. Input: H-0022.
  - The registry takes `.mcpb` files only from GitHub or GitLab releases, which RELEASE does not use as a channel.
  - H-0022 decides the namespace, and whether a verified GitHub Release copy is acceptable.
- [ ] **G.6d — Offer a Gemini CLI extension.** Needs: G.1. Ships after: 4.7c2.
  - It registers the same bridge, with the same rules for agents.
- [ ] **G.6e — Publish Cursor and VS Code install links.** Needs: G.1. Ships after: 4.7c2.
  - The links are composed from the same registration `setup` writes.
  - They are used in the app, in `setup` output and on the site.
- [ ] **G.7 — Publish a one-prompt setup page.** Needs: G.3, G.6e. Ships after: 4.7c2.
  - A keypaste.com page with one prompt an agent can follow: install through a verified channel, run `keypaste setup` and `keypaste doctor`, and never ask for a pasted secret.
  - `verify-demo.sh` checks its commands.
- [ ] **L.2b — Move the guides onto keypaste.com.** Needs: L.1.
  - The guides L.1 rewrites move into `site/web` as their only home, and the repository keeps links to them; the checks that read `docs/demo.md` and the other transcript pages follow them.
  - The content pages gain a content security policy, checked in a browser against search and the diagrams.

## T6 — Daily driver

Each row keeps its historical meaning and is expanded when selected.

- [ ] **4.10a — Add Windows quick unlock.** Needs: none. Ships after: 4.7c2.
  - Resume a locked session with Windows Hello, keeping the full unlock path intact.
- [ ] **4.10b — Add macOS quick unlock.** Needs: 3.5b. Ships after: 4.7c2.
  - Touch ID, under the same session policy, using the signed app's entitlements.
- [ ] **9.2a — Implement interoperable TOTP.** Needs: V.7a.
  - Read and calculate KeePassXC `otp` attributes, keeping the seed protected.
- [ ] **9.2b — Use TOTP in the app and approval bridge.** Needs: 9.2a.
  - Show the code and its countdown.
  - Let an agent be approved for a code, but never for the seed.
- [ ] **8.1 — Install and pair the native messaging host.** Needs: none.
  - Start with a discovery: can keypaste serve the existing KeePassXC-Browser extension's protocol, as KeePassNatMsg does for KeePass 2?
  - Either way, pair only to specific extension identities, under the approval rules of §3.
- [ ] **8.3a — Fill a login with verified origin matching.** Needs: 8.1.
  - Fill only on the real registrable domain.
  - Refuse lookalikes and cross-origin frames.
- [ ] **1.4a — Specify merge and deletion semantics.** Needs: none.
  - For a vault synced as a file, decide how an external save and local changes combine without silently losing either.
- [ ] **1.4b — Implement atomic entry-level merge.** Needs: 1.4a.
  - Merge an external save instead of refusing agents until a person reloads.
- [ ] **9.1a — Define a loss-aware import pipeline.** Needs: V.7a.
  - One import contract, with preview, collision decisions and atomic commit, so nothing is dropped silently.
  - Its adapters, each Needs 9.1a: 9.1b (Bitwarden JSON), 9.1c (LastPass CSV), 9.1d1 (1Password 1PUX), 9.1d2 (1Password CSV) and 9.1e (KeePassXC CSV).
  - 9.1f guides import in the desktop.
- [ ] **V.9 — Report local password health.** Needs: C.2.
  - Find weak, reused and expired credentials locally, with no network request.
  - Report them in the same Recommendations list.
- [ ] **P.1 — Hardware-key vault unlocking.** Needs: V.1b, U.2. Ships after: R.1.
  - The desktop already unlocks a vault that needs a YubiKey slot and changes the key in Settings (D-0366), but this has not been tried with a physical key.
  - What remains:
    - creating such a vault;
    - opening it from the CLI and `keypaste agent`;
    - real-device unlock, save and reopen with KeePassXC interoperability;
    - missing-key and wrong-key refusal;
    - documented spare-key and lost-key limits.
  - OS quick unlock and browser passkeys are separate features.

## T7 — Agents without values

PRODUCT v1.9 (D-0400) adds T7–T10 after the first desktop release, and v1.10 (D-0408) adds the cloud vault to T8 and adds T11. Each row carries its purpose until it is selected and detailed.

- [ ] **X.1 — Attach approved credentials on the wire.** Needs: none.
  - The process holding the vault proxies an agent's HTTPS requests to the hosts an entry names and attaches the entry's credential under a grant the person approved; the agent holds only a placeholder (PRODUCT §2, §3.2).
  - The agent asks by host; the owner matches it to an entry's service, the person picks a personal or team entry, and the agent learns only that service, its scope and its grant (D-0405).
  - The owner writes the audit record of every request it serves.

## T8 — Cloud vault and team projects

- [ ] **X.2 — Specify the end-to-end sharing protocol.** Needs: none.
  - Member key pairs, a project key wrapped for each member, roles, and removal by re-wrapping and rotation, built from mature audited libraries (§3.6).
  - Accounts and a person's encrypted vault, the relay's first client, and one store per team that the app keeps, never inside the personal vault (D-0406).
  - Reviewed against §3 and §4.3 before any relay code.
- [ ] **X.3 — Run the relay.** Needs: X.2.
  - A relay on keypaste.com, and one a team runs itself, storing only ciphertext and wrapped keys: one C# service on .NET, which X.10 and X.13 extend (D-0409).
- [ ] **X.10 — Sync a person's vault through the relay.** Needs: X.3, 1.4b.
  - At first run everyone is offered an account and a cloud vault as the default choice: the KDBX encrypted on the device under the master password and a key file that is never uploaded, and merged across devices (D-0408).
  - Keeping the vault local stays one choice away and needs no account.
- [ ] **X.11 — Recover a cloud vault with a kit or a signed-in device.** Needs: X.10.
  - The recovery kit holds the key file as a QR code, the account address and a space for the master password; a device still signed in can set a new password. No copy of the key exists anywhere else (§3.1).
- [ ] **X.12 — Charge for the cloud past its free tier.** Needs: X.10.
  - Free up to a size limit, premium for more storage and features, and business use paid as it is used; no security feature is paid (§5.4).
- [ ] **X.4 — Share a project with members.** Needs: X.3.
  - Invite, roles for reading, writing and production, and removal with rotation reminders, in the app and the CLI.
  - A record of who fetched what.

## T9 — CI and deploys

- [ ] **X.5 — Verify machine identities at the relay.** Needs: X.3.
  - A CI job or deploy target fetches a project's values under an identity the relay verifies, never a member's key.
- [ ] **X.6 — Offer a GitHub Action.** Needs: X.5.
- [ ] **X.7 — Push syncs from a member's machine.** Needs: X.4.
  - Hosting platforms such as GitHub, Vercel and Cloudflare receive values from an approved member's machine, so the relay never decrypts.

## T10 — Organizations

- [ ] **X.8 — Sign in with SSO and provision with SCIM.** Needs: X.4.
- [ ] **X.9 — Approve production changes, export the audit and package the relay.** Needs: X.4.
  - A production change waits for a second member's approval, the relay's records export to an organization's log store, and the self-hosted relay ships as a package.

## T11 — Server access by choice

- [ ] **X.13 — Let a team project opt into server access.** Needs: X.4.
  - Once a team opts a project in, the relay's .NET service on keypaste.com or the team's own holds its key (D-0409); members see which projects a server can read, and the server's records reach each member's local log (§3.3).
  - Reviewed against §3 and §4.3 before any code.
- [ ] **X.14 — Proxy agents running in the cloud.** Needs: X.13, X.1.
- [ ] **X.15 — Let CI sign in with OIDC and store no secret.** Needs: X.13.
- [ ] **X.16 — Sync opted-in projects to hosting platforms from the server.** Needs: X.13.

## Completion

A finished task has its bounded behavior, an executed verifier and evidence naming the tested source/version. Implemented, Packaged, Published and Installation-verified stay separate. Reader-only fixtures prove a reader; they cannot prove a writer, running producer, native interaction or public release. Completed rows in [steps](steps/README.md) are historical evidence at their original scope, not blanket acceptance of the product's tracks.

The source of prior plans is Git. No completed row is extended to cover new requirements. Publication, service operation, customer contact and optional work require explicit task selection and applicable authorization.

Open IDs that changed meaning under v1.8:

- V.7 splits into V.7a (core and CLI) and V.7b (the app).
- R.1a, 4.7c2, L.1 and R.1 cover three desktops and version 0.5.0.
- 4.7a2 is the macOS DMG, since the internal bundle exists.
- 4.7e installs and preserves the macOS candidate; its publication moved into 4.7c2.
- P.3b splits into P.3b1 (same-entry placeholders) and P.3b2 (cross-entry references, in BACKLOG).
- 3.5a/b, 3.7a–c, 4.10a/b, 8.1, 8.3a, 1.4a/b, 9.1a–f, 9.2a/b and V.9 return from BACKLOG with their original meaning.
- P.1 moves from "Later vault features" to T6.

Under v1.9, X.1–X.9 and L.2 are new IDs; L.2 split into L.2a, completed, and L.2b. The 5.x and 7.x IDs of the earlier sync, hosting and organization ideas stay retired. Under v1.10, X.10–X.16 and T11 are new, and N.16, V.11, L.3 and R.1b were added on 2026-10-02.

The [rescope record](steps/rescope-2026-09-19.md) keeps the v1.7 continuity notes.

The [current release matrix](RELEASE.md#current-distribution--2026-09-07) owns public availability: a completed source step does not mean the behavior is in the current download.
