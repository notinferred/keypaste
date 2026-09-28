# Build plan

This plan owns the committed tasks, grouped by product track: each task's dependencies, detail and acceptance evidence for [PRODUCT](PRODUCT.md) v1.8 (D-0367). [ROADMAP](../ROADMAP.md) owns direction, track order and which tasks each milestone needs. [FEATURES](FEATURES.md) owns the capability inventory, [RELEASE](RELEASE.md) distribution evidence, [BACKLOG](BACKLOG.md) optional work, and the [step records](steps/README.md) what each completed task did. The license remains AGPL-3.0.

This file holds open work only. Finishing a task removes it from here, adds its record and evidence row under [steps](steps/README.md), and details the next task. No row is authorized by appearing here: the founder selects what is built, and an instruction to build the next task takes the first ready code row of ROADMAP's current milestone, in track order. No backlog item is automatically eligible.

## Selection and evidence

Only the next five tasks are detailed: V.7a, C.1a, V.7b, C.2 and N.1a. Later rows name a bounded outcome and the dependencies their own implementation or verifier needs; expand a selected later task before building it. Tasks are not marked implemented from a document, reader, mock response or consuming screen. Name the producer, transport, consumer and user action exercised, and retain the source/version and limitations of the observation.

Needs are build dependencies. Ships after names publication gates. External signing identities are inputs, not a queue of enrollment code. A ready row does not authorize publication, account changes or messages. Preserve the secret-path tests, real KeePassXC compatibility, stale-write refusals and release integrity checks while changing product scope.

## T1 — Everyday vault use and recovery

Custom fields and tags are ordinary KeePass data: keypaste writes them as KeePassXC reads them, and every row keeps the permanent compatibility gates (PRODUCT §4.6). C.1a's tags include the project tags T4 builds on. The main screens show everyday password-manager work; advanced controls and security recommendations live in Settings (PRODUCT §5.8).

- [ ] **V.7a — Manage custom fields in core and the CLI.** Needs: none.
  **Build:** core lists an entry's custom fields by name and protected flag, never by value, and reads one value when asked. It sets one or several fields of one entry as a single edit that makes one history revision and reports what it touched through `Edited` (D-0318), and it removes a field. A new field is written protected unless `--plain` asks otherwise; an existing field keeps its flag.

  These names are refused: empty names, names with control characters or edge whitespace, standard field names in any case, and KeePassXC's own attributes (`otp`, `TOTP Seed`, `TOTP Settings`, `_EXEC_CMD`, `KP2A_URL*`, `KPEX_*`, `KPXC_*`). Fields with those names stay readable and untouched. `UpdateEntry` never writes custom fields, and the `Vault.AddProtectedFieldUnchecked` test seam is removed.

  The CLI gains `get <entry> --field <name>`, `set <entry> --field <name>`, `field ls <entry> [--json]` and `field rm <entry> <name>`. A new `verify-keepassxc-fields.sh` joins the compatibility matrix and the workflows gate gains a field step; secret-path tests are mandatory. Traces to PRODUCT §§1, 2, 4.5 and 4.6.

  **Verify (V-V.7a):** the vault is one KeePassXC made by importing XML. The shipped CLI sets a protected field and changes a plain one. Real KeePassXC reads both with `show -a`, and its XML export marks exactly the protected one. The entry keeps its other field, tag, attachment and `otp`, and the header still says KDBX 4.0.

  After `keepassxc-cli merge` brings in a newer copy of the entry, `keypaste get --field` prints KeePassXC's value and history holds the old one. Setting three fields makes one revision. `--field otp`, `--field Password` and `--field password` are refused, and the file stays byte-identical. `field ls` and `ls` output contain none of the sentinel values.

  A core-only test does not pass, and neither does a vault keypaste wrote standing in for KeePassXC's side.
- [ ] **C.1a — Tag entries into projects and environments.** Needs: none.
  **Build:** core reads an entry's own tags and adds or removes one as an edit with a revision. It refuses a tag holding `,`, `;`, a tab, a control character or edge whitespace, and never reads or writes group tags.

  A project tag is `env:<project>` or `env:<project>:<environment>`, matched ordinally:
  - the project follows the existing name rule, without `:`;
  - the environment is `[a-z0-9][a-z0-9-]{0,31}`;
  - `env:<project>` means `dev`.

  A malformed `env:` tag is reported and grants no membership. It still protects its entry when its environment names a protected profile in any case. Projects are the tag projects together with the legacy `env/` groups, marked as legacy.

  The live-approval check (`ApproverHandler.cs:99,212-215`, `SessionHost.cs:349`) reads the entry's tags as well as its path, so D-0348's protection follows a tag. The CLI gains `env tag <project> <entry> [-p <environment>]` and `env untag`, each naming the fields that join or leave the set. `env ls [<project>]` lists projects, environments with protected ones marked, and member entries, with `--json`.

  The tags half of a new `verify-keepassxc-projects.sh` and a tag step in the workflows gate check it against real KeePassXC. The decision on the grammar supersedes D-0347 for new data. Traces to PRODUCT §§1, 2, 3.2 and 4.6.

  **Verify (V-C.1a):** the vault is one KeePassXC made with the tags `env:billing`, `env:billing:prod`, `env:billing:Prod`, `finance` and one group tag. `keypaste env ls` names billing with `dev`, a protected `prod`, and exactly the member entries. It reports `env:billing:Prod` and ignores `finance` and the group tag.

  After `keypaste env tag billing Stripe -p prod`, real KeePassXC's `show -a Tags` lists `env:billing:prod`, and the file stays KDBX 4.0.

  The approval check runs through a real `keypaste-mcp` whose `--expose` covers the entries, and a real `keypaste agent`. A `request_credential` for that entry's password is offered Allow once only. So is one for an entry KeePassXC tagged `env:billing:Prod` through merge.

  Protection shown only in a unit test does not pass, and neither do tags written by keypaste for both directions.
- [ ] **V.7b — Edit custom fields and tags in the app.** Needs: V.7a, C.1a.
  **Build:** the item pane lists an entry's custom fields by name, with protected ones masked. It reveals one while held and copies through the one clearing countdown (D-0300). It adds a field (a name, a masked value and a protected switch), changes a value, switches protection and removes a field, each through core with history. KeePassXC's own attributes are shown read-only.

  Tags appear as chips that can be added and removed, and a project tag shows its environment and a protected mark. T-22's automation sweep, D-0303's drawn-frame check and the workflows gate's AppDriver half cover the new commands, and the keypaste-design skill applies. Traces to PRODUCT §§1, 2, 4.6 and 5.2.

  **Verify (V-V.7b):** in the app, on a vault KeePassXC made, a person adds a protected field, changes another, removes a third, and adds and removes a tag. Real KeePassXC reads the values, the protection and the tags, and still finds the attachment, custom data and `otp`. The file stays KDBX 4.0.

  The automation surface carries no field value while typing, while a reveal is held or at rest. A drawn frame shows a value only while it is held.

  A view-model-only check does not pass, and neither does KeePassXC reading what core wrote without the app.
- [ ] **C.2 — Flag keys left in notes for review.** Needs: V.7a, V.7b.
  **Build:** a local check runs in the process holding the vault, on unlock and after each save. It reads the notes of every entry outside the recycle bin and keypaste's reserved group, and it finds two kinds of key:
  - single-line `KEY=value` and `export KEY=value` assignments whose key is env-named;
  - values that match a short, documented list of well-known token prefixes kept in core.

  Each finding names its entry and its key, or the token kind, and never holds or shows the value. Findings appear only under Settings › Recommendations, with a quiet count on Settings. Each starts as needing review, and no main screen shows a banner.

  Moving a finding, or all selected ones, does one edit with one revision: it writes the values as protected fields named by their keys and removes exactly those lines from the notes. The previous notes stay in history. The move is refused, with the reason, when a field already holds a different value or the notes changed since the check. A dismissed finding is remembered on this machine by entry and key, never by value.

  The decision narrows D-0278: search still never reads notes, and the check's results leave only through this list. Traces to PRODUCT §§1, 2, 3.4 and 5.8.

  **Verify (V-C.2):** the vault is one KeePassXC made. One entry's notes hold `STRIPE_SECRET_KEY=sk_test_1`, `export OPENAI_API_KEY=sk-proj-2`, a GitHub token on a line of its own, a PEM block and a sentence. Another entry's notes hold a sentence only.

  After unlocking, with no further act:
  - Settings › Recommendations lists the first entry's three findings as needing review, and not the second entry.
  - The Settings row shows the count, and no first-level screen shows a banner.
  - No value appears in the automation tree or a drawn frame.

  Moving the findings leaves the entry with protected fields holding exactly those values, in one revision. Its notes hold only the PEM block and the sentence, and a revision holds the old notes. Real KeePassXC reads the fields and the trimmed notes.

  A dismissed finding stays dismissed after a lock and unlock. Editing the notes between the check and the move refuses the move. A search for `sk_test_1` still finds nothing.

  A parser tested only on strings does not pass.
- [ ] **N.1a — Put the app in four places.** Needs: none.
  **Build:** the sidebar becomes Items, with the project rows beneath it, and Agents, with Trash and Settings in the footer, on Ctrl/Cmd+1–4. The other destinations move:
  - Activity becomes Agents › History, and the full log moves to Settings › Advanced.
  - Env profiles is reached from the project rows and from the Items "+" menu, which also holds New project and Import .env.
  - Sharing becomes an item's Share… action, with its links listed in Settings › Advanced.
  - Import .kdbx moves to "+", the first run and the macOS File menu.

  The always-visible MCP server card goes, and the Agents row shows a count and a dot. The title-bar search becomes the only search and shows its scope. Traces to PRODUCT §§1 and 5.8.

  **Verify (V-N.1a):** a driver works through the app's launch composition (D-0342). From the four places it reaches an item found by search, a project's variables, the agent history, revoking a grant, the full log and its check, creating and revoking a share link, and a KDBX import.

  The sidebar's automation tree lists exactly the four places and the project rows. The MCP card and the second search box are absent. `EntriesViewLayoutTests` holds at 960 px, and each captured frame has one amber element.

  An act left unreachable, or a removed row still listed, leaves the row open.
- [ ] **N.7 — Apply the amended brand.** Needs: none. This follows [BRAND](BRAND.md) as amended on 2026-09-28:
  - The theme follows the system.
  - Titles and headings use the sans face, and mono is kept for machine-readable text.
  - The voice is plain and calm.
  - The CLI help carries the v1.8 positioning line.
- [ ] **N.2 — Offer the person's KeePassXC database on first run.** Needs: none.
  - With no recent vault, the welcome offers three things: the databases KeePassXC last opened, opening another file, and creating a vault. The databases are read only from `LastActiveDatabase`, `LastOpenedDatabases` and `LastDatabases` in KeePassXC's local `keepassxc.ini`.
  - The lock screen stops mentioning agents.
  - The YubiKey control moves under More options unless the vault needs one.
  - T-24 gains the read.
- [ ] **N.5 — Make the item pane read like a password manager.** Needs: V.7b.
  - A web address opens (http and https only) and copies.
  - The `kp://` reference and the KDBX identifier move under "…".
  - The agent card appears only when agents can see the item or have received it.
- [ ] **N.4 — Create an item from a template.** Needs: V.7a.
  - The templates are Login, API key, Database, Server and Secure note.
  - Each has a title, a folder picker, tags and notes instead of a `/` path.
  - Each opens in KeePassXC with the same fields, protection, tags and notes.
- [ ] **N.1b — Give every advanced feature one home.** Needs: N.1a.
  - Scoped tokens, shared links, the log's hash check and diagnostics live in Settings › Advanced.
  - Per-app choices live under that app on Agents.
  - Nothing is removed, and none of it appears on a first-level screen.
- [ ] **N.12 — Mark copied secrets as concealed on macOS and Linux.** Needs: none.
  - Add the macOS pasteboard's concealed and transient types, and KDE's password-manager hint, as Windows already has.
  - T-19 is extended.
- [ ] **N.6 — Use plain words everywhere.** Needs: N.1a, N.3.
  - The app, the CLI help and the bridge's refusal texts replace stdio, exposure, grant, session and KDBX-entry wording with plain terms.
  - The approval prompts keep every element T-2's evidence relies on.
- [ ] **N.13 — Search and show fields from the terminal.** Needs: V.7a.
  - `search` looks at names only (D-0278).
  - `show` masks values and prints none without `--reveal`.

## T2 — One shared unlock session

U.1–U.3 and 4.4b gave the app, `keypaste agent` and the bridge one owner and one lock (D-0309 to D-0321). One gap remains: six CLI verbs still save without taking the owner's claim, so the app refuses agents as `vault-changed` until someone reloads.

- [ ] **N.10 — Make every saving verb take the vault's claim.** Needs: none.
  - `add`, `rm`, `access`, `env set`, `env rm` and `env pull` open the vault through `OpenHeld`, as `set` and `rotate` already do.
  - While the app or `keypaste agent` holds the vault, they are refused before the password is read. The refusal names the holder and the next step: make the change in the app, or run `keypaste lock`.
  - A rule test holds that nothing saves through `Open`.
- [ ] **N.11 — Approve a terminal edit in the unlocked app.** Needs: N.10.
  - `set`, `add`, `rm` and the env writers send the change to the vault's owner, which asks in its prompt window and writes through its session.
  - No master password crosses the pipe, and `access` stays refused.

## T4 — Project environments

A variable becomes an env-named custom field on an ordinary entry. The entry's own tag, `env:<project>` or `env:<project>:<environment>`, puts it in that environment (D-0367). Every set still leaves only through `EnvResolution`, whole or not at all (D-0337), and every child starts through `EnvLaunch`.

The `env/<project>` layout that v0.3.0 wrote stays readable indefinitely; no new variable is written in it. The syntax of `-p`, of `kp://<project>/<environment>/<KEY>` and of token scopes is kept.

- [ ] **C.1b — Resolve projects from tagged fields.** Needs: V.7a, C.1a.
  - A set is two things: the fields named `[A-Z][A-Z0-9_]{0,127}` of every entry carrying its tag, excluding names starting `KPEX_`, `KPXC_` or `KP2A_`; plus the untagged entries of its legacy group.
  - `EnvResolution` resolves it whole or not at all. These refuse the set, naming each: a key held twice, names differing only in case, an expired member, or a value holding a KeePass placeholder.
  - Every consumer uses the variables' source entries: `run` in each form, `env ls`, `env export`, `env diff`, the app's matrix, grants, activity and audit.
  - Prompts name the source entries.
  - An entry in a protected environment makes any release that includes it Allow once only.
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
- [ ] **F.24 — Expect every desktop package in the provenance self-test.** Needs: none. Priority: before `main` is pushed.
  - On local `main` at `d2ced03`, `bash scripts/verify.sh` (2026-09-28) fails `verify-provenance.sh --selftest`. The case `app-genuine-staged-directory` expects "all 4 assets" (`verify-provenance.sh:283`), but since `2348d54` added the macOS `keypaste.app` zip to the app component, the staged directory holds 6.
  - `ci.yml` and `release.yml` run the same self-test, so both fail once `main` is pushed.
  - Verify: the self-test accepts the genuine staged directory at the definition's current asset count, and still refuses one missing an asset.
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
