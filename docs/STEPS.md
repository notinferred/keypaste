# Build plan

This plan owns the committed tasks, grouped by product track: each task's dependencies, detail and acceptance evidence for [PRODUCT](PRODUCT.md) v1.8 (D-0367). [ROADMAP](../ROADMAP.md) owns direction, track order and which tasks each milestone needs. [FEATURES](FEATURES.md) owns the capability inventory, [RELEASE](RELEASE.md) distribution evidence, [BACKLOG](BACKLOG.md) optional work, and the [step records](steps/README.md) what each completed task did. The license remains AGPL-3.0.

This file holds open work only. Finishing a task removes it from here and adds its record under [steps](steps/README.md). No row is authorized by appearing here: the founder selects what is built, and an instruction to build the next task takes the first ready code row of ROADMAP's current milestone, in track order. No backlog item is automatically eligible.

## Selection and evidence

C.1b, C.1c, C.3, C.4 and C.5a are detailed. Other rows name a bounded outcome and the dependencies their own implementation or verifier needs; a task is detailed when it is selected, against the code as it then is. Tasks are not marked implemented from a document, reader, mock response or consuming screen. Name the producer, transport, consumer and user action exercised, and retain the source/version and limitations of the observation.

Needs are build dependencies. Ships after names publication gates. External signing identities are inputs, not a queue of enrollment code. A ready row does not authorize publication, account changes or messages. Preserve the secret-path tests, real KeePassXC compatibility, stale-write refusals and release integrity checks while changing product scope.

## K and B — Build infrastructure

These rows serve every track: a development machine builds nothing, CI runs what a change can break, and the scripts, tests and binaries lose their duplicates. ROADMAP places them in 0.5.0 by founder direction of 2026-09-30.

- [ ] **B.3 — Delete duplicated brand files and finished observers.** Needs: none. `docs/design/assets/` duplicates `assets/brand/`, `docs/design/SKILL.md` is replaced by the project skill, and `docs/design/BRAND.md`'s unique rules move into [BRAND](BRAND.md); `observe-desktop.yml` goes unless R.1a needs it.
- [ ] **B.1 — Share one script library across the gates.** Needs: none. `scripts/lib/common.sh` and `scripts/lib/kpxc.sh` replace the helpers the KeePassXC and process gates each define; every assertion and negative control stays, and a missing KeePassXC still fails. Verify: the compat and process gates pass on three OSes, and shellcheck covers the library.
- [ ] **B.4b — Carry the MCP bridge in the CLI as `keypaste mcp`.** Needs: none.
  - `Keypaste.Mcp` becomes a library the CLI enters through one method, dispatched before any code that opens a vault. `keypaste mcp` keeps `serve`, `setup`, `policy` and `help` and starts the bridge otherwise, with one answer to `keypaste mcp --help` ([B.4a](steps/B.4a.md) found the name taken).
  - Source-rule tests with negative controls keep the dispatch's path into the bridge's entry method from reaching `VaultLocator`, `VaultSession` or `SecretInput`, and every other CLI file from naming `Keypaste.Mcp` or `ModelContextProtocol`, so no verb that opens a vault runs the SDK; `mcp serve`, `setup` and `policy` go to `CliApp.Run` and stay outside the first rule.
  - A gate on the published `keypaste` proves that `mcp serve`, `mcp setup` and `mcp policy` reach their verbs and that `keypaste mcp --vault <path>` answers `initialize` as the bridge; a dispatch that takes every `mcp` argument fails it.
  - A client an earlier release registered by `keypaste-mcp`'s absolute path still reaches the bridge after the upgrade, through a `keypaste-mcp` that runs `keypaste mcp` or through `keypaste setup` rewriting the registration; a gate proves it on a registration the previous release wrote.
  - A decision row supersedes D-0019's confinement of the MCP package to the bridge process, with PRODUCT §3.9's written justification for the SDK sharing the binary that reads the master password, and D-0334's separate payload binary. THREATS' scope follows, and T-9's account of the SDK's HTTP transports (F.31) extends to the one binary. The release definition, workflows, packaging, client catalog and gates name one binary; README's install blocks change at L.1.
  - The trim baseline is unchanged. The lock files of `Keypaste.Cli`, `Keypaste.Cli.Tests` and `Keypaste.Consistency.Tests` gain the SDK's closure.
- [ ] **F.30 — Keep a stalled apt mirror from failing a KeePassXC install.** Needs: none. — app run 36727975787 at `1952ca0`, attempt 1, `keepassxc workflows (ubuntu-24.04)`: `sudo apt-get update` got no answer from `azure.archive.ubuntu.com`, fell back to `archive.ubuntu.com`, printed `noble-security InRelease` at 14:20:46 and then nothing until the job's 20-minute limit cancelled it; the rerun passed in 154 s. `ci.yml`'s Linux install now stops after ten minutes, and the other `apt-get update` steps in `ci.yml`, `app.yml` and `dev.yml` have only their job's limit. Verify: a branch probe that points one source at an address that never answers shows the install steps retrying with a per-request timeout and finishing, or failing within two minutes, instead of waiting for the job's limit.
- [ ] **F.28 — Hold the app's launch tests on macOS.** Needs: none. — dev run 36732743823 at `f8979e0`, `test (macos-15)`: four `EnvLaunchThroughAppTests` failed. `Cancelling_the_confirmation_starts_nothing` read "Nothing was started: the app opens terminals on Wi…" where it expected "Nothing was started.", the start of the refusal `TerminalLaunch.cs:142` gives on macOS (D-0340), where the tests expect a platform that opens terminals. `app.yml` runs the desktop tests on `ubuntu-24.04` only. Verify: `bash scripts/dev.sh --class Keypaste.App.Tests.Session.EnvLaunchThroughAppTests --os all` is green, with each test asserting what its platform does, the refusal on macOS until E.1d changes it. It also fails at `a432ade` in dev run 36726742125, so it predates these branches; only `dev.yml` runs the desktop tests on macOS.

## T1 — Everyday vault use and recovery

Custom fields and tags are ordinary KeePass data: keypaste writes them as KeePassXC reads them, and every row keeps the permanent compatibility gates (PRODUCT §4.6). C.1a's tags include the project tags T4 builds on. The main screens show everyday password-manager work; advanced controls and security recommendations live in Settings (PRODUCT §5.8).

- [ ] **N.6 — Use plain words everywhere.** Needs: N.1a, N.3.
  - The app, the CLI help and the bridge's refusal texts replace stdio, exposure, grant, session and KDBX-entry wording with plain terms.
  - The approval prompts keep every element T-2's evidence relies on.
- [ ] **N.13 — Search and show fields from the terminal.** Needs: V.7a.
  - `search` looks at names only (D-0278).
  - `show` masks values and prints none without `--reveal`.

- [ ] **F.27 — Keep a revision KeePassXC's merge drops when two saves share a second.** Needs: none. — ci run 36711854555 at `7222d90`, `keepassxc compat (ubuntu-24.04)`: `verify-keepassxc-fields.sh:201` failed with "after the merge the history lost the value keypaste wrote", and KeePassXC's merge reported the Stripe history item at `12-04-05` as conflicting; the same gate passed on this job in run 36646739875. Hypothesis: KDBX times have one-second resolution, and when `set --field MERGED` follows the previous save within the same second, the version the merge moves into history shares its time with an existing history item and KeePassXC keeps one of them. Discovery first: a gate step that saves twice within one second and then merges reproduces the loss on every run, and one that waits past the second never does; the repair, such as each save stamping a time later than the entry's newest revision, depends on that result.
- [ ] **F.34 — Diagnose a refused Windows save attempt that outlasts its retry wait.** Needs: none. — dev run 36736924521 at `d5ea3f3`, `test (windows-2025)`, backend projects: `SaveTimingTests.ATransactedSave_DoesNotQueueBehindAnotherSavesRetryWait` failed at `SaveTimingTests.cs:139` with "the holder kept the gate through its retry wait": the holder's refused first attempt held the save gate 3224 ms, 3222 ms of it work, against a 1964 ms wait before its second attempt, while the waiting save's gate wait stayed under the 50 ms the test allows. `src` there equals `a432ade`'s, Core.Tests differs only in a doc comment, and the same test passed on Windows at `9fae2b3` in dev run 36728844369. The question to answer first is where the refused attempt spent its 3.2 s: in the transacted write that meets the held name, slowed by suite load, or in work the gate should not cover. A branch probe repeats the class with the identical backend test command on windows-2025 and records each attempt's decomposition per [diagnostics](diagnostics.md); the repair depends on that result.

## T2 — One shared unlock session

U.1–U.3 and 4.4b gave the app, `keypaste agent` and the bridge one owner and one lock (D-0309 to D-0321), and N.10 made every CLI verb that saves take the owner's claim (D-0382). What remains is approving a terminal edit in the unlocked app instead of refusing it.

- [ ] **N.11 — Approve a terminal edit in the unlocked app.** Needs: N.10.
  - `set`, `add`, `rm` and the env writers send the change to the vault's owner, which asks in its prompt window and writes through its session.
  - No master password crosses the pipe, and `access` stays refused.
- [ ] **F.29 — Answer a waiting request before the app's endpoint stops.** Needs: none. — app run 36728150916 (pull request 7 into `task/k6b` at `1952ca0`), `format + analyzers + tests` on `ubuntu-24.04`: `AppAuthorityTests.Quitting_answers_a_waiting_request_as_locked_before_the_endpoint_stops` failed at line 141 because the reply was null; the same test passed in app run 36727975787 on `1952ca0`. Hypothesis: `AppAuthority.Dispose` locks the session, which withdraws the held prompt and fires `Locked`, and `SessionHost`'s hosted endpoint then cancels its connections, waits at most its two-second stop grace and disposes the listener, so the locked answer can lose the race to the close and the client reads the end of the stream. Discovery first: repeating the test, and a variant that delays the prompt's cancellation, measure how often the reply is null; the repair depends on that result.
- [ ] **F.33 — Diagnose a connection the app's endpoint accepts after a lock on Linux.** Needs: none. — dev run 36726742125 at `a432ade`, `test (ubuntu-24.04)`: `SessionOwnershipTests.The_unlocked_vault_is_served_on_its_endpoint_and_stops_being_served_on_lock` failed at `SessionOwnershipTests.cs:123`: after `Lock` returned and `host.Endpoint` was null, `ApproverClient.TryConnectAsync` on the old endpoint returned a connected client. The test took 2 s 163 ms, just over `SessionHost`'s 2 s stop grace; it passed at `97d5284` in dev run 36727300997. The hypothesis to test first: `Hosted.Dispose` returns once that grace expires while `ApproverListener.RunAsync` still holds a server instance, either in its accept loop or in a finished connection's `ServeAsync`, and on Unix any live instance keeps the pipe's socket bound. A branch probe repeats the class with the identical desktop test command. In each failing iteration it records whether `_run.Wait` timed out and which server instances were still undisposed when `Lock` returned, per [diagnostics](diagnostics.md). The repair depends on that result.

## T4 — Project environments

A variable becomes an env-named custom field on an ordinary entry. The entry's own tag, `env:<project>` or `env:<project>:<environment>`, puts it in that environment (D-0367). Every set still leaves only through `EnvResolution`, whole or not at all (D-0337), and every child starts through `EnvLaunch`.

The `env/<project>` layout that v0.3.0 wrote stays readable indefinitely; no new variable is written in it. The syntax of `-p`, of `kp://<project>/<environment>/<KEY>` and of token scopes is kept.

- [ ] **C.1b — Resolve projects from tagged fields.** Needs: V.7a, C.1a.
  **Build:** a project's environment is two things: the fields named `[A-Z][A-Z0-9_]{0,127}`, not starting `KPEX_`, `KPXC_` or `KP2A_`, of every entry whose own tag puts it there (`env:<project>` for `dev`, `env:<project>:<environment>` otherwise, D-0370); and the untagged entries of its legacy `env/<project>` group for that environment (D-0347). `EnvResolution` resolves the set whole or not at all, refusing it and naming each cause and its entries: a key held by two entries, two keys differing only in case, an expired member, and a value holding a KeePass placeholder such as `{PASSWORD}`. Every consumer reads the set with each variable's source entry: `run` in each form (`-p`, `--session`, `--token`, `--bundle` and `.env.keypaste` references), `env ls`, `env export`, `env diff`, the app's Env profiles matrix and Run, grants, Agents › History and the audit line's `entries`; prompts name the source entries. A release that includes a member of a protected environment offers Allow once only (D-0348, D-0371). Traces to PRODUCT §§1 and 2 and T4.

  **Verify (V-C.1b):** on a vault KeePassXC made and tagged, holding one legacy `env/<project>` variable and fields on two tagged entries, `keypaste run <project> -- <reporter>` and `run --session` through the app each start a child whose environment holds exactly the tagged fields and the legacy variable; `env export` writes a reference for each and `env diff` compares the tagged environments' key names. Each refusal starts nothing and names the entries: one key on two entries, a legacy `Api_Key` beside a tagged `API_KEY`, an expired member, and `{PASSWORD}` in a value. The app's prompt for `run --session` names the source entries, one including an `env:<project>:prod` member offers Allow once only, and each release's audit line names its source entries.

  A resolver shown only over an in-memory vault, with no child started, does not pass.
- [ ] **C.1c — Write keys onto entries.** Needs: C.1b.
  **Build:** `env set`, `env pull`, `env rm` and the app's add, edit and import on Env profiles write a project's keys as fields of its entries (D-0367). An existing key is updated where it lives: on the tagged entry that holds it, or in place as a legacy `env/<project>` variable. A new key goes on the entry the person names (`--entry` in the CLI, a choice of the environment's tagged entries in the app), or else on the environment's home entry `env/<project>/.env` (`.env.<environment>` for another environment), created with its tag on first use. Values are written protected through `Vault.SetFields` and `RemoveField`, one revision per entry per operation. Before a tag changes, through `env tag`, `env untag` or the pane's chips, the person is shown every field and environment the change reaches, and nothing is written until they confirm. Traces to PRODUCT §2 and T4.

  **Verify (V-C.1c):** on a vault KeePassXC made, `env set` of a tagged key changes it on its own entry, `env set` of a new key without `--entry` creates `env/<project>/.env` tagged `env:<project>` and holding it protected, and `env pull` of a `.env` touching keys on two entries makes one revision on each; real KeePassXC reads each value, its protection and the home entry's tag, and `keypaste run` then gives a child the new values. The app's add, edit and import write the same, `env rm` removes a field and keeps it in history, and a tag change through the CLI and through the app lists the fields and environments it reaches before anything is written, and writes nothing when declined.

  A write shown only in core, with no front end and no KeePassXC read, does not pass.
- [ ] **C.3 — Move a 0.3 project onto tagged fields.** Needs: C.1c.
  **Build:** `keypaste env migrate <project>` moves a project's legacy variables, the untagged entries C.1b reads under `env/<project>`, onto tagged fields: each value becomes a protected field of its environment's home entry, `env/<project>/.env` or `.env.<environment>`, created tagged as in C.1c, and each old entry goes to the recycle bin, in one save through `OpenHeld`, all or nothing. A Core plan made first lists, by environment, each key's old and new entry and any old entry holding more than a value, whose rest stays in the bin. The CLI prints it, never a value, and asks `[y/N]`; `--yes` is required without a terminal, and `--dry-run` writes nothing and takes no claim. It is refused whole, naming each cause and entry: an environment C.1b refuses now (a key also on a tagged entry, a case-only pair, an expired member, a placeholder such as `{PASSWORD}`); a key no variable field can be named, such as `api_key`, `KPXC_X` or `URL`; an entry with an expiry, which a field cannot carry; a home entry that exists untagged or with another project tag; a project name no tag holds (D-0370); and a vault with no recycle bin. Every environment then resolves to the same keys and values, a protected one still offering Allow once only (D-0348, D-0371), so `kp://<project>/<environment>/<KEY>` references, token scopes and `projects.json` keep working; the plan warns that `kp:///env/…` references and policy rules naming a moved entry stop matching. The app's control is C.4's, whose Projects screen offers this plan. Traces to PRODUCT §§2 and 4.6 and T4.

  **Verify (V-C.3):** on a vault the published 0.3.0 CLI filled with `env/acme` variables and KeePassXC gave `staging` and `prod` subgroups, with an `env export` file, a `read:acme/staging/*` token and an app Run mapping, each environment's `keypaste run` records what a reporter child receives. `--dry-run` changes no byte, and `env migrate` under `Keypaste.AppDriver hold` is refused naming the app. After `env migrate acme --yes`, real KeePassXC reads `env/acme/.env`, `.env.staging` and `.env.prod` with their tags and protected values, the old entries in its recycle bin and no untagged entry left in `env/acme`. Each recorded run then gives its child the same environment through `-p`, the export file, `--token` and the app's Run, and `run --session -p prod` under the app names `env/acme/.env.prod` and offers Allow once only. Each refusal above, on its own seeded copy, names its cause and entries and changes no byte; a KeePassXC save made while `env migrate` waits at its prompt is kept, and nothing moves.

  A move shown only in core, with no child run before and after it and no KeePassXC read, does not pass.
- [ ] **C.4 — Build the Projects screen on tags.** Needs: C.1c, C.3.
  **Build:** the sidebar's project rows, the page's picker and New project read `ProjectCatalog`, listing tag-only projects beside the `env/` groups, which are marked legacy. An open project shows each key by environment with its value's source entry. A value whose entry serves several environments or projects is marked with them, and Replace names each of them before writing. Each environment lists its entries. Add entry tags one and Remove untags it, both behind C.1c's confirmation and deleting nothing. Keys are added, replaced, removed and imported through C.1c's writes. Export .env.keypaste references the resolved set, and Run goes through E.1b's launch, its confirmation naming the source entries. A legacy project's page offers Move to tags, listing each variable by environment with the home-entry field it becomes and the entries that will be recycled, and only a confirmation runs C.3's move. The page names the malformed tags `env ls` warns of. Traces to PRODUCT §§1, 2 and 5.8 and T4.

  **Verify (V-C.4):** the vault is one KeePassXC made, with a legacy project mapped in `projects.json`, a tag-only one with two `dev` entries (one also tagged `staging`) and an `env:<project>:Prod` tag. A driver through the app's launch composition (D-0342) finds in the automation tree both projects with only the legacy one marked, each value's source entry, the shared entry's two environments and the malformed tag. On the tag-only project it adds an entry to `staging`, removes the other from `dev`, replaces the shared value, adds a key with no entry chosen and imports a `.env`. On the legacy project it declines Move to tags, tries it over a file another program saved, then confirms. Each declined or refused act leaves the bytes unchanged. Real KeePassXC reads every tag, value and protection written, the moved project's home entries and its old entries in the recycle bin. `keypaste run` resolves the exported references; Run starts `Keypaste.EnvReporter` holding exactly `dev`'s fields, and the moved project's Run gives a child its earlier environment. Every frame passes N.1a2's amber check.

  A page shown only through its view models, with no KeePassXC read of its writes, does not pass.
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
  **Build:** `CredentialFields` becomes the one rule for what leaves by name: `password`, `username`, `url`, `notes`, or a custom field `EnvConvention.IsEnvNamedField` accepts (`[A-Z][A-Z0-9_]{0,127}`, not starting `KPEX_`, `KPXC_` or `KP2A_`) that is no standard name in any case; no other custom field ever leaves. `request_credential`'s schema, the bridge and the owner (`CredentialRequestRules`) take it, and `VaultCredentialSource` reads that field from the saved file only after a person's Allow or a rule. `kp:///<group>/<title>#<field>` takes it for `keypaste run --env-file` and the bridge's `run` tool, `policy.toml`'s `fields` takes it and `keypaste policy ls` names it, and `keypaste share --field` or a reference seals that one field under its name. Each prompt names the requested field, a run's as `entry · field`, and a grant for one field serves none of the entry's others. Any other name, such as `Recovery codes`, `otp` or `KP2A_URL_1`, is refused by the bridge and again by the owner before anyone is asked or anything read: the agent reads `keypaste: DENIED. The "field" argument must be password, username, url, notes or a custom field named like an environment variable. This call was recorded in the audit log.`, and its audit line records `field: invalid` and `method: invalid-request`. A reference or share naming one starts or uploads nothing. T-8 states which custom fields can leave. Traces to PRODUCT §§2 and 3 and T3.

  **Verify (V-C.5a):** on a vault KeePassXC made, `api/OpenAI` holds distinct sentinels in its password, a protected `OPENAI_API_KEY`, `Recovery codes`, `otp` and `KP2A_URL_1`. A shipped `keypaste-mcp --expose 'api/**'` over stdio asks the app, held by `Keypaste.AppDriver hold`: the drawn prompt reads `field=OPENAI_API_KEY`, Allow once returns exactly its sentinel, audited with that field, and once a second request is allowed for 1 hour a `password` request still draws a prompt. `Recovery codes`, `otp`, `KP2A_URL_1` and `URL` each get the denial above and no prompt. Through `keypaste agent` with stdin at EOF, a rule with `fields = ["OPENAI_API_KEY"]` releases it as `policy` while `password` reaches a prompt, a rule naming `Recovery codes` releases nothing unprompted, and the `run` tool under `--allow-run` naming `kp:///api/OpenAI#OPENAI_API_KEY` is asked about as `api/OpenAI · OPENAI_API_KEY`. That reference given to `keypaste run --env-file` puts that sentinel in the child, and `#Recovery%20codes` starts nothing. `keypaste share --field OPENAI_API_KEY` and its reference form, against the Worker on loopback, make links `share-crypto.js` opens to that one field; `--field "Recovery codes"` uploads nothing. No other sentinel reaches a result, the wire, a child, a payload or the log.

  A custom field released only in core or an in-process bridge, with no shipped `keypaste-mcp` request answered on a vault KeePassXC made, does not pass.
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
- [ ] **F.31 — Say which HTTP transports the bridge's SDK carries.** Needs: none. T-9 says the bridge's dependency closure "contains no HTTP client", but `ModelContextProtocol.Core` 1.4.1's `net10.0` assembly carries `HttpClientTransport` and `StreamableHttpServerTransport` ([B.4a](steps/B.4a.md)). T-9 names them, says the bridge constructs only `StdioServerTransport`, and says their names are absent from the published `keypaste-mcp`, which suggests without proving that trimming removed them. Verify: each statement matches the locked package, the bridge's source and a name search of the published binary.

## T5 — Desktop delivery

The Windows MSI, the Linux AppImage and the macOS DMG are internal candidates. Existing install and upgrade observations remain useful, but they are repeated for the product that [ROADMAP](../ROADMAP.md) puts in 0.5.0.

Package managers and agent marketplaces carry a version only after it is published. keypaste.com keeps its "no `curl | sh`" stance: the one-command routes are the package managers and the signed installers.

- [ ] **F.32 — Keep a prompt a bridge hang-up withdrew off the screen.** Needs: none. — dev run 36738139311 at `ee3f820` on `task/f20-probe-repaired`, `ubuntu-24.04`: with [F.20](steps/F.20.md)'s repair, a real gate whose token descends from one cancelled with `CancelAsync`, as `ApproverListener.cs:193` withdraws when the bridge hangs up, drew the prompt in 99 of 100 iterations when the UI thread ran its queue straight after the cancel, and in 50 of 50 with that token's callbacks held until it had. The tokens linked below stay live until the pool runs those callbacks, so the show job's check passes and the prompt stays up until the posted take-down; the gate still answers the request as cancelled. Verify: through `ApproverListener`, a peer that hangs up while the show job is queued, with the UI thread running its queue as soon as the token the listener gave its handler is cancelled, draws no prompt in 100 iterations on `ubuntu-24.04`.
- [ ] **G.5 — Carry the CLI on PATH in every desktop install.** Needs: none.
  - Every desktop payload gains the NativeAOT `keypaste`, and the macOS bundle and DMG checks require it as they require `keypaste-mcp`.
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
  - `osx-arm64` declares the `.app.zip` and the DMG. `require-release-assets.sh --publishable` requires every declared package offered, and `verify-desktop-candidate.sh` stages only the DMG, so either the zip stops being a declared package or the candidate check accepts each declared `osx-arm64` kind.
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
