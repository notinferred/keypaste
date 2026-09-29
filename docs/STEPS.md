# Build plan

This plan owns the committed tasks, grouped by product track: each task's dependencies, detail and acceptance evidence for [PRODUCT](PRODUCT.md) v1.8 (D-0367). [ROADMAP](../ROADMAP.md) owns direction, track order and which tasks each milestone needs. [FEATURES](FEATURES.md) owns the capability inventory, [RELEASE](RELEASE.md) distribution evidence, [BACKLOG](BACKLOG.md) optional work, and the [step records](steps/README.md) what each completed task did. The license remains AGPL-3.0.

This file holds open work only. Finishing a task removes it from here, adds its record and evidence row under [steps](steps/README.md), and details the next task. No row is authorized by appearing here: the founder selects what is built, and an instruction to build the next task takes the first ready code row of ROADMAP's current milestone, in track order. No backlog item is automatically eligible.

## Selection and evidence

Only the next five tasks are detailed: N.4, N.1b, N.12, N.10 and C.1b. Later rows name a bounded outcome and the dependencies their own implementation or verifier needs; expand a selected later task before building it. Tasks are not marked implemented from a document, reader, mock response or consuming screen. Name the producer, transport, consumer and user action exercised, and retain the source/version and limitations of the observation.

Needs are build dependencies. Ships after names publication gates. External signing identities are inputs, not a queue of enrollment code. A ready row does not authorize publication, account changes or messages. Preserve the secret-path tests, real KeePassXC compatibility, stale-write refusals and release integrity checks while changing product scope.

## T1 — Everyday vault use and recovery

Custom fields and tags are ordinary KeePass data: keypaste writes them as KeePassXC reads them, and every row keeps the permanent compatibility gates (PRODUCT §4.6). C.1a's tags include the project tags T4 builds on. The main screens show everyday password-manager work; advanced controls and security recommendations live in Settings (PRODUCT §5.8).

- [ ] **N.4 — Create an item from a template.** Needs: V.7a.
  **Build:** New on Items starts from Login, API key, Database, Server or Secure note. Each asks for a title, a folder chosen from the vault's groups instead of a typed `/` path, tags and notes, and its own fields: a login's username, password and web address; an API key's env-named key, protected; a database's and a server's host, username and password; a secure note's notes alone. The item is created in one write with no history item, under V.7a's field-name rules and C.1a's tag rules. The keypaste-design skill applies. Traces to PRODUCT §§1 and 5.8.

  **Verify (V-N.4):** in the app, on a vault KeePassXC made, a person creates one item from each template. Real KeePassXC reads each item's title, group, fields and their protection, tags and notes, and finds no revision. A title already taken in the folder, a refused field name and an empty title each write nothing. The form's automation tree carries no typed value.

  A template shown only in a view model does not pass.
- [ ] **N.1b — Give every advanced feature one home.** Needs: N.1a.
  **Build:** Settings › Advanced, which already lists the activity log and share links (N.1a1), gains Scoped tokens, moved from Agents with New token, its one-time copy and Revoke, and Diagnostics, moved from Settings' main list. The log's hash check, Verify chain and Copy hash, is offered on the activity log only; Agents › History shows the records without it. Each connected app's choice of Session grants up to 1h, Ask every time or Inject only moves from a dropdown on its card to that app's menu on Agents. Nothing is removed. The keypaste-design skill applies. Traces to PRODUCT §5.8.

  **Verify (V-N.1b):** a driver through the app's launch composition (D-0342) reaches, from Settings › Advanced, minting and revoking a scoped token, the log's check and the diagnostics facts, and from an app's menu on Agents, changing its choice, which `clients.toml` then holds. By their automation trees, Agents' first level and Settings' main list carry none of them. Every frame of the drive passes N.1a2's amber check.

  A control left reachable only from its old place, or removed, leaves the row open.
- [ ] **N.12 — Mark copied secrets as concealed on macOS and Linux.** Needs: none.
  **Build:** a secret the desktop copies carries, in the same clipboard item as the Windows formats, `org.nspasteboard.ConcealedType` and `org.nspasteboard.TransientType`, which macOS pasteboard managers honour, and `x-kde-passwordManagerHint` holding `secret`, which KDE's Klipper honours. A copy that is not a secret, such as a `keypaste run` command, carries none. T-19 and SECURITY say what each marker asks and that none stops a process reading the clipboard, and O-0019 is answered. The CLI's `pbcopy`, `wl-copy` and `xclip` writes are unchanged, and that limit is stated. Traces to PRODUCT §3.

  **Verify (V-N.12):** `AvaloniaClipboard` hands the platform one item holding the text and every marker for a secret, and no marker for plain text. On the macOS runner, after the app copies a secret, the general pasteboard lists both nspasteboard types beside the text, and none after the clear; on a Linux runner with an X11 clipboard, `xclip -o -t TARGETS` lists `x-kde-passwordManagerHint`, whose content reads `secret`.

  A marker shown only in the source, with no platform read of what the clipboard offered, does not pass.
- [ ] **N.6 — Use plain words everywhere.** Needs: N.1a, N.3.
  - The app, the CLI help and the bridge's refusal texts replace stdio, exposure, grant, session and KDBX-entry wording with plain terms.
  - The approval prompts keep every element T-2's evidence relies on.
- [ ] **N.13 — Search and show fields from the terminal.** Needs: V.7a.
  - `search` looks at names only (D-0278).
  - `show` masks values and prints none without `--reveal`.

## T2 — One shared unlock session

U.1–U.3 and 4.4b gave the app, `keypaste agent` and the bridge one owner and one lock (D-0309 to D-0321). One gap remains: six CLI verbs still save without taking the owner's claim, so the app refuses agents as `vault-changed` until someone reloads.

- [ ] **N.10 — Make every saving verb take the vault's claim.** Needs: none.
  **Build:** `add`, `rm`, `access`, `env set`, `env rm`, `env pull` and `import`, the verbs that still save through `VaultSession.Open`, open the vault through `OpenHeld`, as `set`, `rotate`, `field rm` and `env tag` already do; `import --dry-run` still reads without the claim. While the app or `keypaste agent` holds the vault, each is refused before its password is read, and the refusal names the holder and the next step: make the change in the app, or run `keypaste lock` and try again. A rule test over `src/Keypaste.Cli` holds that `VaultSession.Open` is called only by verbs that never save. Traces to PRODUCT §2 and T2.

  **Verify (V-N.10):** with the app, through `Keypaste.AppDriver hold`, and then `keypaste agent` holding a vault, each of the seven verbs is refused naming the holder and the next step, with no password prompt on stderr and the vault's bytes and backup count unchanged, and the holder then answers an agent request instead of refusing it as `vault-changed`. With nothing holding the vault each verb saves as before. The rule test fails on a copy that adds a save through `Open`.

  A refusal shown only against a claim the test takes itself does not pass.
- [ ] **N.11 — Approve a terminal edit in the unlocked app.** Needs: N.10.
  - `set`, `add`, `rm` and the env writers send the change to the vault's owner, which asks in its prompt window and writes through its session.
  - No master password crosses the pipe, and `access` stays refused.

## T4 — Project environments

A variable becomes an env-named custom field on an ordinary entry. The entry's own tag, `env:<project>` or `env:<project>:<environment>`, puts it in that environment (D-0367). Every set still leaves only through `EnvResolution`, whole or not at all (D-0337), and every child starts through `EnvLaunch`.

The `env/<project>` layout that v0.3.0 wrote stays readable indefinitely; no new variable is written in it. The syntax of `-p`, of `kp://<project>/<environment>/<KEY>` and of token scopes is kept.

- [ ] **C.1b — Resolve projects from tagged fields.** Needs: V.7a, C.1a.
  **Build:** a project's environment is two things: the fields named `[A-Z][A-Z0-9_]{0,127}`, not starting `KPEX_`, `KPXC_` or `KP2A_`, of every entry whose own tag puts it there (`env:<project>` for `dev`, `env:<project>:<environment>` otherwise, D-0370); and the untagged entries of its legacy `env/<project>` group for that environment (D-0347). `EnvResolution` resolves the set whole or not at all, refusing it and naming each cause and its entries: a key held by two entries, two keys differing only in case, an expired member, and a value holding a KeePass placeholder such as `{PASSWORD}`. Every consumer reads the set with each variable's source entry: `run` in each form (`-p`, `--session`, `--token`, `--bundle` and `.env.keypaste` references), `env ls`, `env export`, `env diff`, the app's Env profiles matrix and Run, grants, Agents › History and the audit line's `entries`; prompts name the source entries. A release that includes a member of a protected environment offers Allow once only (D-0348, D-0371). Traces to PRODUCT §§1 and 2 and T4.

  **Verify (V-C.1b):** on a vault KeePassXC made and tagged, holding one legacy `env/<project>` variable and fields on two tagged entries, `keypaste run <project> -- <reporter>` and `run --session` through the app each start a child whose environment holds exactly the tagged fields and the legacy variable; `env export` writes a reference for each and `env diff` compares the tagged environments' key names. Each refusal starts nothing and names the entries: one key on two entries, a legacy `Api_Key` beside a tagged `API_KEY`, an expired member, and `{PASSWORD}` in a value. The app's prompt for `run --session` names the source entries, one including an `env:<project>:prod` member offers Allow once only, and each release's audit line names its source entries.

  A resolver shown only over an in-memory vault, with no child started, does not pass.
- [ ] **C.1c — Write keys onto entries.** Needs: C.1b.
  - This covers `env set`, `env pull`, `env rm` and the app's add, edit and import.
  - An existing key is updated where it lives.
  - A new key goes on the entry the person names, or else on the environment's home entry `env/<project>/.env` (`.env.<environment>`).
  - Values are written protected, with one revision per entry per operation.
  - Before a tag changes, the person is shown every field or environment the change reaches.
- [ ] **C.3 — Move a 0.3 project onto tagged fields.** Needs: C.1c.
  - It is opt-in: `env migrate <project>`, or its equivalent on the Projects screen.
  - Each legacy variable's value moves into a protected field of its environment's home entry, and the old entries are recycled, in one save, all or nothing.
  - References, tokens and `projects.json` keep working.
- [ ] **C.4 — Build the Projects screen on tags.** Needs: C.1c, C.3.
  - Projects come from tags and legacy groups.
  - An open project shows its variables by environment, with each value's source entry and any sharing marked.
  - From it the person adds keys, adds or removes entries, imports a `.env`, exports `.env.keypaste`, runs through E.1b's launch and moves a legacy project to tags.
  - Split it into C.4a and C.4b if it runs past two weeks.
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

- [ ] **C.5a — Release one env field to an agent.** Needs: V.7a.
  - `request_credential`, `kp:///…#field`, policy `fields` and `share --field` take `password`, `username`, `url`, `notes` or an env-named custom field.
  - No other custom field ever leaves.
  - T-8 is updated.
- [ ] **C.5b — Expose tagged projects to agents.** Needs: C.1b, C.5a.
  - The default exposure becomes `env/**` plus `tag:env:*`, and `--expose` and policy rules accept tag selectors.
  - An entry reached only through a tag exposes only its env fields.
  - Listings carry field names and project tags, never a value.
  - THREATS gains T-38: one secret shared by two environments.
- [ ] **G.1 — Let agents and the CLI use the vault the person chose.** Needs: none.
  - The app records a vault in `~/.keypaste/app.toml` at the first create or unlock, says so once, and lets Settings change it.
  - `--vault` and `KEYPASTE_VAULT` still win, and nothing falls back to whichever vault happens to be unlocked.
  - Bridges and client configurations then carry no vault path.
  - T-3, T-29 and T-36 are reviewed.
- [ ] **G.2 — Connect every AI tool on this machine.** Needs: G.1.
  - `keypaste setup` and the app connect Claude Code, Codex, VS Code and Gemini CLI through their own commands.
  - Cursor, Claude Desktop and Windsurf are connected by a verified edit of their configuration file, with a backup.
  - One confirmation, or `--yes`, covers the whole run.
  - The registered bridge path survives upgrades.
  - `setup` exits 0 when any tool is connected.
- [ ] **N.3 — Show apps, requests, allowances and history on Agents.** Needs: N.1a, G.2.
  - Each detected tool gets one Connect.
  - The screen shows the waiting request, allowances with their time left and an End button, and the history.
  - Per-app choices sit behind each app's menu.
  - The first level has no more than twelve controls.
- [ ] **G.4a — Stay in the menu bar or tray and open at login.** Needs: none.
  - Closing the main window still ends the session (D-0313), which amends D-0343 for the tray.
  - A background start at login holds no claim.
  - On Linux the tray is opt-in.
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
- [ ] **N.8 — Make `keypaste` alone show where things stand.** Needs: G.1.
  - On a terminal it shows the version, the chosen vault and who holds it, the connected tools and one next step.
  - It asks for no password.
- [ ] **N.9 — Give each CLI concept one name.** Needs: none.
  - `approve` for the terminal approver, `rules` for `policy.toml`, and `clients` for per-client choices.
  - The old spellings stay as aliases that say so.

## T5 — Desktop delivery

The Windows MSI, the Linux AppImage and the macOS app bundle are internal candidates. Existing install and upgrade observations remain useful, but they are repeated for the product that [ROADMAP](../ROADMAP.md) puts in 0.5.0.

Package managers and agent marketplaces carry a version only after it is published. keypaste.com keeps its "no `curl | sh`" stance: the one-command routes are the package managers and the signed installers.

- [ ] **F.20 — Diagnose the withdrawn prompt drawn on Linux.** Needs: 4.4. — app run 36040583862 at `f51fe5d`, attempt 1, `ubuntu-24.04`: `DesktopApprovalTests.A_request_withdrawn_before_its_prompt_is_drawn_never_draws_it` counted one prompt drawn where none was expected (`DesktopApprovalTests.cs:142`), its first failure in the recorded app runs. The hypothesis to test first: the test withdraws through `CancelAsync`, whose callback sets the request answered on the thread pool, and while the test awaits it the UI dispatcher may already run the show job `WindowApprovalChannel` posted, which checks `IsAnswered` only once. A branch probe repeating the case with the identical desktop test command records, in each failing iteration, whether the show job ran before the withdrawal and whether the product or only the test's arrangement lets a withdrawn prompt be drawn, per [diagnostics](diagnostics.md).
- [ ] **G.5 — Carry the CLI on PATH in every desktop install.** Needs: none.
  - Every desktop payload gains the NativeAOT `keypaste`.
  - The MSI adds its folder to the per-user PATH and removes it on uninstall.
  - The AppImage dispatches `cli` as it does `mcp`.
  - Settings links the CLI into the terminal on Linux and macOS.
  - `install-desktop.yml` and `upgrade-desktop.yml` check `keypaste --version` in a new shell.
- [ ] **4.7a2 — Package the internal macOS DMG.** Needs: none.
  - It wraps the existing `keypaste.app` bundle, with its bridge and the CLI.
  - The candidate's contents, version and `--selftest` are checked from the mounted image.
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
- [ ] **R.1a — Exercise the integrated desktop candidate.** Needs: 9.4, 4.6, F.15, U.2, U.3, 4.4b, 4.3b, 2.6a, E.1b, E.1c, F.2b3a, F.2b3b, V.7a, V.7b, C.2, N.1a, N.1b, N.2, N.3, N.4, N.5, N.6, N.7, N.10, N.12, C.1a, C.1b, C.1c, C.3, C.4, E.1d, C.5a, C.5b, G.1, G.2, G.4a, G.5, 4.7a2.
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
  - Then publish, through `release.yml`, immutable Windows, macOS and Linux desktop packages and the matching CLI/MCP archives as 0.5.0. This renames CHANGELOG's Unreleased section and sets the version.
  - Verify from anonymous downloads: public hashes, provenance, signatures and notarization, and an upgrade that keeps the vault, `~/.keypaste` and the recent list.
  - Rehearsal certificates, unpublished artifacts and source builds do not pass.
- [ ] **L.1 — Make the released app understandable and reachable.** Needs: 4.7c2.
  - README, keypaste.com and the guides lead with the v1.8 positioning and name 0.5.0.
  - They link its three desktop downloads, hashes and verification steps.
  - They take a new user from the download to a working vault, a project run and a connected agent, as that version behaves.
  - They give the issue route and the private security-reporting route.
  - Published claims are checked against the published binaries (D-0036).
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

## Completion

A finished task has its bounded behavior, an executed verifier and evidence naming the tested source/version. Implemented, Packaged, Published and Installation-verified stay separate. Reader-only fixtures prove a reader; they cannot prove a writer, running producer, native interaction or public release. Completed rows in [steps](steps/README.md) are historical evidence at their original scope, not blanket acceptance of the six tracks.

The source of prior plans is Git. No completed row is extended to cover new requirements. Publication, service operation, customer contact and optional work require explicit task selection and applicable authorization.

Open IDs that changed meaning under v1.8:

- V.7 splits into V.7a (core and CLI) and V.7b (the app).
- R.1a, 4.7c2, L.1 and R.1 cover three desktops and version 0.5.0.
- 4.7a2 is the macOS DMG, since the internal bundle exists.
- 4.7e installs and preserves the macOS candidate; its publication moved into 4.7c2.
- P.3b splits into P.3b1 (same-entry placeholders) and P.3b2 (cross-entry references, in BACKLOG).
- 3.5a/b, 3.7a–c, 4.10a/b, 8.1, 8.3a, 1.4a/b, 9.1a–f, 9.2a/b and V.9 return from BACKLOG with their original meaning.
- P.1 moves from "Later vault features" to T6.

The [rescope record](steps/rescope-2026-09-19.md) keeps the v1.7 continuity notes.

The [current release matrix](RELEASE.md#current-distribution--2026-09-07) owns public availability: a completed source step does not mean the behavior is in the current download.
