# Build plan

This plan owns the committed tasks, grouped by product track: each task's dependencies, detail and acceptance evidence for [PRODUCT](PRODUCT.md) v1.21 (D-0685). [ROADMAP](../ROADMAP.md) owns direction, track order and which tasks each milestone needs. [FEATURES](FEATURES.md) owns the capability inventory, [RELEASE](RELEASE.md) distribution evidence, [BACKLOG](BACKLOG.md) optional work, and the [step records](steps/README.md) what each completed task did. The license remains AGPL-3.0.

This file holds open work only. Needs are build dependencies; time and human acts are never Needs, and a Human row is its own track. Ships after names publication gates. External signing identities are inputs, not a queue of enrollment code. A ready row does not authorize publication, account changes or messages. Preserve the secret-path tests, real KeePassXC compatibility, stale-write refusals and release integrity checks while changing product scope.

## Q — At home on every platform

The audit of 2026-10-31 checked the desktop against Apple's Human Interface Guidelines ([DESIGN-PRINCIPLES](DESIGN-PRINCIPLES.md) §6, D-0682). Q.1 to Q.12 and Q.18 gate 1.0.0 and Q.13 to Q.17 follow it. Each row keeps every control name the tests use and DESIGN's one amber per view, keeps the prompts' and masked input's security behaviour (D-0232, D-0301, D-0326, D-0456, D-0634), says Ctrl where macOS says ⌘, and updates [DESIGN](DESIGN.md) and [the desktop guide](desktop.md) where it changes what they describe. A row that reverses a decision writes the row that supersedes it.

- [ ] **Q.11 — Open on a first run anyone understands.** Needs: none. Input: H-0027 for where the terms appear.
  - With no vault and no KeePassXC database found, the welcome's first action is Create a vault, with one plain sentence on what a vault and its master password are; someone with KeePassXC databases still sees them first (D-0681).
  - The terms of use leave the first launch (D-0594), whose own copy says running keypaste needs no agreement, and appear where their two services start: the Share… dialog, and the crash-report choice, asked once on the unlock screen with Send crash reports and Don't send as two equal buttons and no default.
  - One word list beside DESIGN § Voice: Trash, Group, Title, key file, top level and Disconnect, replacing recycle bin, Folder, Name, keyfile and the root where the copy uses them for these; and the words a new person cannot know are rewritten: `--client-label`, ceilings, selectors as `--expose` takes them, unlock ID and the relay.
  - Accepted when the first-run tests reach the vault with no terms screen, Share… and the crash-report choice show the terms, and the copy reads against the word list.
- [ ] **Q.12 — Calm the reveal and the agent settings.** Needs: none.
  - Reveal is held (D-0300), which a person with a motor impairment, switch control or a screen reader's single activation cannot do. It becomes a toggle that hides again after a short time, on leaving the item, when the window loses focus and on lock, the value still drawn and never put in the accessibility tree (D-0232).
  - An agent's settings show Mode, Can see and May run commands; its project rows, patterns and limits sit behind one More, open whenever any of them is set, and "Advanced: patterns" becomes a disclosure.
  - Every choice of one among several is the log filter's segmented control, in place of chips and ghost buttons that wrap (an agent's Mode, Give values and May run commands), with durations in words ("1 hour", "7 days").
  - Accepted when the reveal tests, D-0232's surface check among them, pass for the toggle and each way it hides, and an agent's settings open on those three controls.
- [ ] **Q.13 — Take files by drag and from the system.** Needs: none.
  - Only the unlock screen takes a dropped file, of any kind, with nothing highlighted. A `.kdbx`, another app's export, a `.env` or an attachment dropped anywhere goes to its import or attachment flow, highlighted only when it is accepted, and an item dropped on a group moves there.
  - `.kdbx` is declared to macOS as an alternate handler, so KeePassXC stays the default, to Linux by its MIME type and to Windows by a ProgId; a file the system hands the app, or a path argument passed on through the second start's pipe (D-0397), opens on the unlock screen.
- [ ] **Q.14 — Use the system's notifications.** Needs: none.
  - A request that arrives while keypaste is in the background also posts a time-sensitive notification that names no item, whose action brings the prompt forward through `BringForward` (D-0456), and the locked notice becomes an ordinary notification with Open keypaste (D-0466).
- [ ] **Q.15 — Sort and size the tables.** Needs: none.
  - Items, Trash and the activity log sort by a column head, again to reverse, and resize their columns; heads stay in capitals (DESIGN).
- [ ] **Q.16 — Read in any language and region.** Needs: none.
  - Dates and times people read follow the current culture and machine text stays invariant; strings move to resources; right-to-left layout follows the culture, with a chevron-left icon for Back and secrets kept left to right.
- [ ] **Q.17 — Finish the polish.** Needs: Q.5.
  - Window titles name the place; the pointing hand appears only on links; a prompt is drawn inactive while another window has focus and is disarmed again on activation; toasts sit under the titlebar; scroll bars follow the system's setting; End is drawn plain and End all confirms; a delete into Trash asks nothing once undo exists, while a delete for good still asks; buttons are named by their result ("Connect Claude Code", "Reset clients.toml") and every command that opens a form ends in an ellipsis; tooltips say what a control does, with Learn more beside selectors and token scopes; and Appearance's Theme gives way to the system's setting, kept only where a Linux desktop reports none.

## T1 — Everyday vault use and recovery

The founder's check of Q.2 on 2026-11-01 found these, and they gate 1.0.0 (D-0683).

- [ ] **F.75 — Open two items that share a name.** Needs: none.
  - The desktop reaches an item by its group path and title (`EntryRow`, `EntryName`), and `Vault.Find(EntryName)` refuses a name more than one entry answers to, so two items with one title in one group, such as two logins for one site, each show "More than one entry answers to that name" in place of their pane, at every `Find(name)` in `EntriesViewModel` and `EntryDetailViewModel`. KeePass and KeePassXC allow such a pair, and imported vaults often hold one.
  - Rows, the pane and what it does (copy, reveal, history, edit, rename or move, delete), Trash, the agent-access card and Recommendations reach their entry by its KeePass UUID, and a row whose title another row in its group shares shows its username after the title. An agent's request that more than one entry answers to is F.76's.
  - Accepted when tests over a vault holding two items with one title in one group open, copy, edit, rename, delete and restore each as itself.

## T3 — AI requests in the app

Every release still needs a person's answer or a rule they set, and the bridge stays vault-free (PRODUCT §§2 and 3.2). The bridge, the in-app prompt (4.4), the Agents screen (4.3b) and connecting from the app (2.6a) are reused.

- [ ] **F.76 — Let the person pick the item an agent asked for.** Needs: none. Worked first after K and B (founder, 2026-11-03, D-0684).
  - People keep two items for one website, and an agent's request that more than one item answers to is refused with "more than one item answers to that name" (`CredentialFailure.Ambiguous`, `VaultCredentialSource`), so the agent is left to guess another name. Instead, the app says the agent is requesting a credential and asks the person to pick one.
  - The picker is not closed to the person: each item that answers shows its title, group, username, URL and the field the agent asked for, so the person sees the field they are picking, its value masked as everywhere else.
  - It is the agent's prompt, keeping the prompt's rules (D-0326, D-0456, D-0634): no default, focus on Deny, the allows disarmed for a second and a fixed layout. Exactly the picked item's one field is released, and the owner's log names that item.
  - Accepted when tests on the request path show a request two items answer to opening the picker, the picked item's field released and logged, Deny and a lock releasing nothing, and a request one item answers to unchanged.

## T5 — Desktop delivery

The Windows MSI, the Linux AppImage and the macOS DMG are internal candidates. Existing install and upgrade observations remain useful, but they are repeated for the product that [ROADMAP](../ROADMAP.md) puts in 1.0.0.

Package managers and agent marketplaces carry a version only after it is published. keypaste.com keeps its "no `curl | sh`" stance: the one-command routes are the package managers and the signed installers.

- [ ] **3.5a — Enable the macOS signing identity (H-0015).** Human. Needs: none.
  - Start by 2026-11-02: ROADMAP's 2026-12-08 date for 1.0.0 holds only if the outside inputs start by then (D-0668), and enrollment can take days to weeks.
  - External Apple Developer enrollment as keypaste.
  - Repository-scoped Developer ID Application and notarization credentials.
- [ ] **3.5b — Sign and notarize macOS release payloads.** Needs: 4.7a2. Input: 3.5a's credentials.
  - First, a branch preflight with ad-hoc signing settles a bundle layout `codesign` can seal, and the hardened-runtime entitlements. The CoreCLR payload may have to move out of `Contents/MacOS`.
  - Then the app is signed inside-out, notarized and stapled.
  - `spctl` accepts it, and a changed byte is refused.
  - Terminal must be seen running the launcher of a downloaded bundle macOS opened. On `macos-15` Gatekeeper refuses an unsigned copy's launcher while it is quarantined ([4.7e1](steps/4.7e1.md)).
  - `upgrade-desktop.yml`'s macOS leg runs again on the first signed bundle, to see whether macOS App Management permission stops it replacing `/Applications/keypaste.app` ([4.7e2](steps/4.7e2.md)).
  - `libargon2.1.dylib`, in the bundle's `Contents/MacOS` and beside `keypaste` in the CLI archive, is signed with the same Developer ID and hardened runtime before what loads it: the hardened runtime's library validation refuses a dylib from another team, and keypaste then derives in managed code without failing, so the signed DMG's `--selftest` must still name it (D-0523).
- [ ] **H-0027 — Approve the terms of use.** Human. Needs: none.
  - The founder or a lawyer approves the draft of 2026-10-28, filling in the legal entity, governing law and place of establishment, and decides whether the first-run choice satisfies EU consent law.
- [ ] **H-0028 — Make the crash-report database and switch the route on.** Human. Needs: 4.11b.
  - Follow [Switching crash reports on](../site/README.md#switching-crash-reports-on-h-0028) in `site/README.md`: create the D1 database, add its `CRASH_DB` binding and `CRASH_ENABLED` to `wrangler.jsonc`, apply `crash-schema.sql`, and integrate that one commit ([4.11b](steps/4.11b.md)).
  - Acceptance: before it, `verify-site-endpoint.sh` sees the deployed route answer 503; after its deploy, the script sees the route's refusals, and a probe report is stored, read back with `wrangler d1 execute` and deleted.
- [ ] **4.7c2 — Publish and verify the first desktop release as 1.0.0.** Needs: 4.7c1, 3.5b, 4.7a2, 4.7e, G.5. Ships after: R.1a, the engineering rows K.7 to K.12 and B.5 (D-0672), every T6 task but 1.4b4 (D-0669), V.8a and V.8b, 4.11a to 4.11c, Q.1 to Q.12 (D-0682), Q.18 (D-0686), F.75 and V.12 (D-0683), F.76 (D-0684), and the fixes still marked for 1.0.0 (D-0669). Inputs: a verified Microsoft Artifact Signing identity as keypaste (H-0017), or the recorded cloud-HSM fallback, 3.5a's Apple credentials, the approved terms of use (H-0027) and the crash-report database switched on (H-0028).
  - Prove Windows signing with changed-byte refusal, and macOS notarization.
  - `osx-arm64` publishes the DMG alone; the `.app.zip` it is built from is a build intermediate, not a declared package (D-0513).
  - Then publish, through `release.yml`, immutable Windows, macOS and Linux desktop packages and the matching CLI/MCP archives as 1.0.0. This renames CHANGELOG's Unreleased section and sets the version: `VersionPrefix`, release-targets.json, the site's labels and every other place that still names 0.5.0.
  - Verify from anonymous downloads: public hashes, provenance, signatures and notarization, and an upgrade that keeps the vault, `~/.keypaste` and the recent list.
  - Rehearsal certificates, unpublished artifacts and source builds do not pass.
- [ ] **R.1b — Round-trip a tagged entry through the phone apps (H-0024).** Human. Needs: none.
  - Tag an entry and give it a protected env field in keypaste, edit that entry in KeePassium, Strongbox and KeePassDX, and confirm keypaste still reads the tag, the field and its protection; record each app's version.
- [ ] **L.1 — Make the released app understandable and reachable.** Needs: 4.7c2, L.3.
  - README, keypaste.com and the guides lead with the v1.20 positioning (D-0681) and name 1.0.0.
  - They link its three desktop downloads, hashes and verification steps.
  - They take a new user from the download to a working vault, a project run and a connected agent, as that version behaves.
  - README's demo GIF and every screenshot are re-rendered from the 1.0.0 binaries.
  - They give the issue route and the private security-reporting route.
  - The launch copy says what keypaste adds over Varlock's KeePass plugin, Strongbox MCP and 1Password Environments, and links the safe-agent guide (L.3).
  - The guides name only the phone apps R.1b's round-trip passed.
  - Published claims are checked against the published binaries (D-0036).
  - The install blocks move from `keypaste-mcp` to `keypaste mcp`, and `verify-install.sh`'s bridge check moves with them ([F.48](steps/F.48.md)).
  - No announcement, message or signup mail is authorized by this row.
- [ ] **R.1 — Accept the installed local product.** Needs: 4.7c2, L.1.
  - Repeat every R.1a act on clean Windows, macOS and Linux desktops, from public 1.0.0 bytes checked against their hashes and provenance.
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
  - It is built on a branch and merged only then, because a push to `main` publishes it; an earlier build on `hold/G.6a` was deleted on 2026-10-30.
- [ ] **G.6c — Publish the Claude Desktop extension and list keypaste in the MCP Registry.** Needs: G.6b. Ships after: 4.7c2. Input: H-0022.
  - The extension G.6b packs becomes a release asset, signed with `mcpb sign`, and an install check sees Claude Desktop install it and start `keypaste mcp` through it.
  - The registry takes `.mcpb` files only from GitHub or GitLab releases, which RELEASE does not use as a channel.
  - H-0022 decides the namespace, and whether a verified GitHub Release copy is acceptable.
- [ ] **G.6d — Offer a Gemini CLI extension.** Needs: G.1. Ships after: 4.7c2.
  - It registers the same bridge, with the same rules for agents.
- [ ] **G.7 — Publish a one-prompt setup page.** Needs: G.3, G.6e. Ships after: 4.7c2.
  - A keypaste.com page with one prompt an agent can follow: install through a verified channel, run `keypaste setup` and `keypaste doctor`, and never ask for a pasted secret.
  - `verify-demo.sh` checks its commands.
  - The page also offers G.6e's Cursor and VS Code install links. A link there cannot name one computer's paths as `setup`'s do, so the page decides what its links start, or points to `keypaste setup --link`.
- [ ] **L.2b — Move the guides onto keypaste.com.** Needs: L.1.
  - The guides L.1 rewrites move into `site/web` as their only home, and the repository keeps links to them; the checks that read `docs/demo.md` and the other transcript pages follow them.
  - The content pages gain a content security policy, checked in a browser against search and the diagrams.

## T6 — Daily driver

Each row keeps its historical meaning and is expanded when selected. Designs the founder approved on 2026-10-24 split 8.1 and 8.3a (D-0548), 1.4b (D-0537 to D-0546), and P.4a and P.4b (D-0547) into children, which keep their parents' IDs; a parent closes when all its children have. P.4a1 is done, and the typing children, P.4a2 to P.4a5 and P.4b, left 1.0.0 for BACKLOG (D-0632).

- [ ] **4.10b — Add macOS quick unlock.** Needs: 3.5b.
  - Touch ID, under the same session policy, using the signed app's entitlements.
- [ ] **8.1e — Install and remove the host per browser.** Needs: 8.1c.
  - Settings › Browser fill and `keypaste browser` turn the host on and off for Chrome, Chromium, Edge, Brave and Firefox at the user-level places KeePassXC 2.8.0's `NativeMessageInstaller.cpp` writes, which each browser reads before a system one: on Windows the `org.keepassxc.keepassxc_browser` key under Chrome's, Chromium's, Edge's or Mozilla's `HKCU` `NativeMessagingHosts`, Brave reading Chrome's, naming a JSON file under `%LOCALAPPDATA%\keypaste\NativeMessagingHosts\`, and elsewhere each browser's own manifest directory. The manifest allows only the three identities and names the `keypaste` a terminal finds: the MSI's per-user `keypaste.exe`, the app bundle's `keypaste`, re-pointed at each start as the Linux link is (D-0431), `~/.local/bin/keypaste` on Linux, made when absent (D-0435), or a CLI archive's running binary.
  - Turning on names the program the manifest it replaces points at ("KeePassXC will stop answering Chrome") and keeps a byte copy of that manifest, and of the Windows value, under `~/.keypaste/browsers/replaced/`. Turning off removes keypaste's manifest only while it still names keypaste and puts the copy back, leaving one KeePassXC has since rewritten. Each browser's status is keypaste, KeePassXC with its path, another program with its path, keypaste at a path that no longer exists, or not set up.
  - Accepted when, in `verify-browser-host.sh` on each runner, the fake browser finds and starts the manifest where that browser looks, on Windows through `%COMSPEC% /d /s /c` with redirected pipes as Chromium does; turning off restores a planted KeePassXC manifest and Windows value byte for byte and leaves one KeePassXC rewrote; and the status names KeePassXC, another program, a missing path and none.
- [ ] **8.3a2 — Fill a login on the person's answer or allowance.** Needs: 8.1d, 8.3a1.
  - `get-logins` is served on a bound attachment. Candidates are the items whose URL or a `KP2A_URL` field matches by 8.3a1's rules, leaving out recycled items, keypaste's reserved groups, items KeePassXC's `BrowserHideEntry` hides on the item or its group, and expired items, refused as a run refuses them (D-0337); KeePassXC's other narrowing settings are honoured and its allow lists are not.
  - A candidate with an allowance for the exact origin is released under it; any other is asked about. The prompt names the browser and pairing, the origin in ASCII with its registrable domain emphasised and the form's origin when it differs, and lists candidates by title and username, never a password; the person fills one, optionally ticking "Always fill this item on" that origin, or denies. One prompt per pairing is open at a time, a request meanwhile is answered with no logins, the recent-refusal cooldown applies, and a lock withdraws a waiting prompt (D-0313). Allowances are kept in `.keypaste/browsers`, made only in the app, and listed and removable in Settings.
  - The reply carries, for each released item, its login and password filled in as D-0465 fills them, its name and its UUID, never its group, a TOTP code, string fields or `KPH:` or `KPXC_` fields. Each released item gets its own line before the reply, and a line that cannot be written refuses the release (D-0439); lines name the identity, the pairing, the origin as `scheme://host:port` and the item's path, never a page's path, query or fragment, a username, a password or a key, and also record each matched item refused, with its reason, and each allowance added or removed.
  - THREATS gains a page that is not the item's site, keypaste believing the extension about which page asks, the extension as someone else's code receiving passwords, and an allowance as a rule kept in the vault.
  - Accepted when, in `verify-browser-host.sh` and the owner tests, a fill follows the person's Fill; an allowance releases on its exact origin only; every refusal of 8.3a1 holds; only login, password, name and UUID leave; one line per released item comes before the reply; and no line is written for a lookup that matched nothing.
- [ ] **8.3a3 — Prove fill in real browsers.** Needs: 8.1e, 8.3a2.
  - `scripts/verify-browser-fill.sh`, in one new app job on `ubuntu-24.04`, runs Chromium and Firefox under Xvfb with KeePassXC-Browser 1.10.4.1 from its release zips pinned by SHA-256. Chromium's copy carries the Web Store's public key as its manifest key, so its ID is the store's, and Firefox loads it as a temporary add-on under its own ID. A local HTTPS server with a throwaway CA serves pages for 8.3a1's rules, a cross-origin frame and a cross-site form, its names mapped by Chromium's `--host-resolver-rules` and `/etc/hosts`; Playwright drives Chromium and geckodriver Firefox, both pinned by lockfile.
  - SECURITY gains its Browser fill subsection and an Audit limits sentence saying browser fill lines name the sites where items were filled.
  - Accepted when the gate passes in that job, showing filled and refused pages and the extension refusing a cross-origin frame, and the founder's hand check of the store-installed extension in branded Chrome, Edge and Firefox on each platform is recorded.
- [ ] **1.4b4 — `keypaste approve` merges another program's save.** Needs: 1.4b2.
  - The folder watch and poll moved into Core for both owners, and `keypaste approve`'s terminal line, hardware-key touch and refusal text (D-0546).
  - Accepted when `ApproveCommandTests` shows another program's save merged without a restart, and `verify-keepassxc-merge.sh` gains, passing on the three runners, G9: `keypaste approve` narrows an agent and an older KeePassXC copy is moved over the file, and the narrowing holds with the older record in history; and G10: KeePassXC saves while `keypaste approve` serves a bridge, and the next request is refused as `vault-changed` and then served from the merged copy in the same session, with no restart.

## T7 — Agents without values

PRODUCT v1.9 (D-0400) adds T7–T10, v1.10 (D-0408) adds the cloud vault to T8 and adds T11, v1.12 (D-0430) adds X.17 to T7, v1.15 (D-0530) put T7 in 1.0.0, and v1.19 (D-0669) moves it after 1.0.0, which T8–T11 follow too. Each row carries its purpose until it is selected and detailed.

- [ ] **X.1 — Attach approved credentials on the wire.** Needs: none.
  - The process holding the vault proxies an agent's HTTPS requests to the hosts an entry names and attaches the entry's credential under a grant the person approved; the agent holds only a placeholder (PRODUCT §2, §3.2).
  - The agent asks by host; the owner matches it to an entry's service, the person picks a personal or team entry, and the agent learns only that service, its scope and its grant (D-0405).
  - The owner writes the audit record of every request it serves.
- [ ] **X.17 — Let an agent organize the vault without seeing values.** Needs: none.
  - By default the agent sees names and structure only, never a value, and note text counts as a value; the person may allow it to read a vault's note text, told that the text then reaches the agent's provider (PRODUCT §2).
  - It proposes changes such as classifying notes, moving keys into fields or tagging entries for projects; each change is applied only once the person approves it in the app, and one that moves an item into or out of an agent's reach, such as tagging it into a project an agent can see, is flagged as such before the person approves it.
  - Every name the agent reads reaches its provider, and a planted entry name can steer it, for example into proposing a move into a project an agent is Automatic for (T-13).

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

The [current release matrix](RELEASE.md#current-distribution--2026-09-07) owns public availability: a completed source step does not mean the behavior is in the current download.
