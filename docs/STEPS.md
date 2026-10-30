# Build plan

This plan owns the committed tasks, grouped by product track: each task's dependencies, detail and acceptance evidence for [PRODUCT](PRODUCT.md) v1.17 (D-0556). [ROADMAP](../ROADMAP.md) owns direction, track order and which tasks each milestone needs. [FEATURES](FEATURES.md) owns the capability inventory, [RELEASE](RELEASE.md) distribution evidence, [BACKLOG](BACKLOG.md) optional work, and the [step records](steps/README.md) what each completed task did. The license remains AGPL-3.0.

This file holds open work only. Needs are build dependencies; time and human acts are never Needs, and a Human row is its own track. Ships after names publication gates. External signing identities are inputs, not a queue of enrollment code. A ready row does not authorize publication, account changes or messages. Preserve the secret-path tests, real KeePassXC compatibility, stale-write refusals and release integrity checks while changing product scope.

## K and B — Build infrastructure

These rows serve every track: a development machine builds only the compile check (D-0468), and CI runs every check in parallel on every run (D-0423). ROADMAP places them in 1.0.0, the milestone that was 0.5.0 when the founder placed them there on 2026-09-30.

## T3 — AI requests in the app

Every release still needs a person's answer or a rule they set, and the bridge stays vault-free (PRODUCT §§2 and 3.2). The bridge, the in-app prompt (4.4), the Agents screen (4.3b) and connecting from the app (2.6a) are reused.

- [ ] **F.74 — Find why KeePassXC 2.8.0 exits at the macOS first-run gate.** Needs: none.
  - In app 40984082285 at `28a95a73`, keepassxc first run (macos-15) failed with "FIRST-RUN GATE FAILED: KeePassXC exited before opening the vaults", its last output a Qt font-alias warning; the rerun of that job passed, which does not close it. Nothing that run's batch changed touches first run or how the gate starts KeePassXC. [F.68](steps/F.68.md) recorded 2.8.0 not crashing in 60 starts as evidence, not KeePassXC's diagnosis.
  - Capture KeePassXC's exit status and crash report on the runner when it exits early, so the next occurrence names the cause, then repair the gate or report upstream.
- [ ] **F.73 — Find why a masked field's window surface differed on macOS.** Needs: none.
  - In app 40909966048 at `34564a1a`, desktop tests (macos-15), `DrawnMaskTests.A_shell_field_leaves_the_window_surface_the_same_for_any_value(name: "AccessConfirmPassword")` failed once: the window's property dump for two typed values differed at position 11822, past an `ItemsControl`'s `ItemCount` and `ItemsPanel`, around its `ItemsSource` (`DrawnMaskTests.cs:168`); the rerun of that job passed, which does not close it. Nothing that run's batch changed is in the app. [F.17](steps/F.17.md) closed an earlier exposure in the same field.
  - Diagnose what changes in the surface between the two values, keep its regression and repair the cause, whether the test reads a surface still settling or the field leaks its value's shape.
- [ ] **F.72 — Find what hangs the macOS desktop gates since `t6`'s second batch.** Needs: none.
  - At `dfe09d12`, desktop gates (macos-15) was cancelled at its timeout twice in a row with no log kept, app 40749086048's first attempt at 1053 s and dev run 40751958140 at 901 s, after about 40 runs that never hung; the gates had passed in 457 s at `42ef9de5`, which differs only in a label and a path list. Three probes on a branch then passed, in 440 s, 393 s and 422 s: 40754396801 streamed each gate through `tee` under `bash -x` with three-minute caps, 40757177371 kept the files with the caps and a heartbeat, and 40758266185 ran the gates as they are with timestamps and a watchdog that would have printed the processes and logs; no gate took over 65 s.
  - Candidates are what that batch changed on macOS: 4.10a's unlock and lock paths, P.1a's import, and F.70's accept loop, which on Unix now hangs up after each answer. Catch a hang with the watchdog probe, keep its regression and repair the cause.
- [ ] **F.71 — Explain the knocks a Windows locked app still misses with a spare pipe waiting.** Needs: none.
  - After F.70's repair `LockedAppEndpointTests.BridgesInQuickSuccession_AreEachHeard` still timed out on windows-2025, twice in about 19,000 knocks, once at bridge 187 in dev run 40190674348; F.70's probes ruled out a spare failing to accept and a stopped listener ([F.70](steps/F.70.md)).
  - Diagnose the mechanism, keep its regression and repair the cause.


## T5 — Desktop delivery

The Windows MSI, the Linux AppImage and the macOS DMG are internal candidates. Existing install and upgrade observations remain useful, but they are repeated for the product that [ROADMAP](../ROADMAP.md) puts in 1.0.0.

Package managers and agent marketplaces carry a version only after it is published. keypaste.com keeps its "no `curl | sh`" stance: the one-command routes are the package managers and the signed installers.

- [ ] **3.5a — Enable the macOS signing identity (H-0015).** Human. Needs: none.
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
- [ ] **4.7c2 — Publish and verify the first desktop release as 1.0.0.** Needs: 4.7c1, 3.5b, 4.7a2, 4.7e, G.5. Ships after: R.1a, every T6 and T7 task but a person's check on real hardware (D-0557), P.4a, P.4b, V.8a and V.8b, 4.11a to 4.11c, and the fixes marked for 1.0.0 (D-0530). Inputs: a verified Microsoft Artifact Signing identity as keypaste (H-0017), or the recorded cloud-HSM fallback, 3.5a's Apple credentials, the approved terms of use (H-0027) and the crash-report database switched on (H-0028).
  - Prove Windows signing with changed-byte refusal, and macOS notarization.
  - `osx-arm64` publishes the DMG alone; the `.app.zip` it is built from is a build intermediate, not a declared package (D-0513).
  - Then publish, through `release.yml`, immutable Windows, macOS and Linux desktop packages and the matching CLI/MCP archives as 1.0.0. This renames CHANGELOG's Unreleased section and sets the version: `VersionPrefix`, release-targets.json, the site's labels and every other place that still names 0.5.0.
  - Verify from anonymous downloads: public hashes, provenance, signatures and notarization, and an upgrade that keeps the vault, `~/.keypaste` and the recent list.
  - Rehearsal certificates, unpublished artifacts and source builds do not pass.
- [ ] **R.1b — Round-trip a tagged entry through the phone apps (H-0024).** Human. Needs: none.
  - Tag an entry and give it a protected env field in keypaste, edit that entry in KeePassium, Strongbox and KeePassDX, and confirm keypaste still reads the tag, the field and its protection; record each app's version.
- [ ] **L.1 — Make the released app understandable and reachable.** Needs: 4.7c2, L.3.
  - README, keypaste.com and the guides lead with the v1.8 positioning and name 1.0.0.
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
  - It is kept on a branch until then, because a push to `main` publishes it.
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

Each row keeps its historical meaning and is expanded when selected. Designs the founder approved on 2026-10-24 split 8.1 and 8.3a (D-0548), 1.4b (D-0537 to D-0546), and P.4a and P.4b (D-0547) into the children below, which keep their parents' IDs; a parent closes when all its children have.

- [ ] **4.10c — Check Windows quick unlock on a machine with Windows Hello (H-0025).** Human. Needs: none.
  - On a Windows 10 or 11 machine with Windows Hello set up, run the app's Windows package built from 4.10a: unlock with the master password and answer Windows Hello, the first time making keypaste's key; lock with `Ctrl/Cmd+L` and resume with Unlock with Windows Hello.
  - Check the prompt comes in front of the app each time, a cancelled prompt opens nothing and says nothing, the PIN instead of a face or finger opens it, `keypaste lock` then asks for the master password, and a vault that needs a YubiKey resumed with Windows Hello asks for its touch.
  - Record the Windows version, the Windows Hello method and the app's commit in [4.10a](steps/4.10a.md)'s limits. No CI runner has Windows Hello set up, so only a person can make these acts (T-44).
- [ ] **4.10b — Add macOS quick unlock.** Needs: 3.5b.
  - Touch ID, under the same session policy, using the signed app's entitlements.
- [ ] **8.1d — Pair a browser with the vault's owner.** Needs: 8.1c.
  - Pair a browser, in Settings › Browser fill and in `keypaste approve`, arms pairing for two minutes and one pairing. An `associate` outside that window is refused unasked with error 6 and recorded, and the app says "A browser asked to pair. To pair it, press Pair a browser." Armed, a confirmation names the identity the browser started keypaste with, such as "KeePassXC-Browser from the Chrome Web Store (oboonakemofpalcgghocfoadofidjkkk)", with an editable name that becomes the pairing's id, its allow disarmed for a second as approval windows are (D-0456).
  - The owner writes the pairing in `.keypaste/browsers`: its id, the identity, the SHA-256 of the extension's identification key and the creation time, never the key and no `KPXC_` data. `test-associate` succeeds only for a record whose identity equals the attachment's and binds the attachment to that pairing, and a request on an unbound attachment is refused with error 8. `get-databasehash` answers the SHA-256 of `keypaste`, a line feed and the root group's UUID, so the extension keeps keypaste's pairing in a slot of its own beside KeePassXC's. Settings lists pairings with their last use, and Remove deletes one.
  - The audit gains a browser kind, here recording a pairing granted, denied or refused while unarmed, a pairing check refused and a pairing removed, naming the identity and pairing and never a key. THREATS gains two programs answering one host name, a pairing as a bearer key, and something other than the extension asking to pair.
  - Accepted when `scripts/verify-browser-host.sh`, a desktop-gates gate driving the fake browser against the shipped binary and the app driver on the three runners, shows pairing only while armed, an unarmed request refused unasked and noticed and an identity mismatch refused; and, from [9.4](steps/9.4.md)'s limit, a vault made in KeePassXC's own window holding KeePassXC-Browser's pairings and per-entry settings, committed with a fixed test password, passes `verify-keepassxc-workflows.sh`'s after-write checks with its container unchanged and that data kept byte for byte, keypaste neither reads it for a grant nor changes it, the vault opens in KeePassXC (PRODUCT §4.6), KeePassXC's own pairing still answers, and FEATURES lists the data as exercised.
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
- [ ] **1.4b3 — The desktop merges another program's save.** Needs: 1.4b2.
  - The app merges on the watch, the poll and a refused save, an edit waiting for the merge; the title bar and the notice of D-0542; Reload only after a refused merge.
  - Accepted when App tests show another program's save merged with the edit it refused and named in the notice, a conflict staying in the notice until dismissed, a merge keeping the session with the next request asked, and an item open for editing left as typed; and when `scripts/verify-keepassxc-merge.sh`, a new compat gate sourcing `scripts/lib/common.sh` and `scripts/lib/kpxc.sh` and run by ci's compat jobs on the three runners, passes these cases, driving the shipped `keypaste` and `Keypaste.AppDriver hold`, making KeePassXC's changes with keepassxc-cli and the existing export, edit and import pattern, and having KeePassXC export the file and check its header after every merge:
    - G1: a KeePassXC save to another entry refuses the app's edit; keypaste saves once, both changes are in KeePassXC's export, and the notice names the edit as saved.
    - G2: KeePassXC saves and then the app edits; and the app edits and saves before a KeePassXC copy taken earlier is moved over the file, as a sync service does, after which the app's saved change survives and KeePassXC's is current where newer.
    - G3: KeePassXC and the app change one entry, in both orders of time; the newer is current and the older in history, and finding the base's version in KeePassXC's history measures whether its round trip keeps a version intact.
    - G4: the open copy, written to a temporary encrypted file and merged into a copy of the file with `keepassxc-cli merge -s`, gives the current versions, places and history keypaste's result has, except where D-0537 to D-0541 differ on purpose, each asserted: tombstones applied, a deletion against an unseen change kept in the bin, a tie keeping both, versions sharing a second all kept, a change only the file made taken though stamped earlier, `LocationChanged` kept with the place, group custom data, tags and flags taken with the newer group, the displaced version kept past the limit, and two recycle bins reconciled.
    - G5: KeePassXC purges an entry the app did not change, which goes with its tombstone, and one it did, which is in the bin with the app's change.
    - G6: KeePassXC recycles an entry the app edits, which ends in the bin with the edit, and the app restores one KeePassXC purged, which survives.
    - G7: KeePassXC attaches a file to one entry while the app edits another, and a conflict puts an attachment in history; exported attachments' SHA-256 equal the originals', current and in history.
    - G8, from [1.4b5](steps/1.4b5.md): a token is revoked, KeePassXC merges in a copy still holding it, and after keypaste's merge `keypaste run --token` is refused and recorded.
    - G11: KeePassXC changes the password, and the app locks with `ReloadRefused`; the file is replaced by another vault, which is not merged and the notice names; a truncated file is refused and the complete one then merged.
    - G12: KeePassXC opens every result, which is KDBX 4.0 unless 4.1 content is present.
  - `verify-current-state.sh` step 5 ends with the merge and keeps Reload for a refused merge; THREATS' opening, T-29's last paragraph, T-15's merge text and T-32's limit while no owner merges say what the desktop now does; and D-0317's "until a person reloads" is superseded.
- [ ] **1.4b4 — `keypaste approve` merges another program's save.** Needs: 1.4b2.
  - The folder watch and poll moved into Core for both owners, and `keypaste approve`'s terminal line, hardware-key touch and refusal text (D-0546).
  - Accepted when `ApproveCommandTests` shows another program's save merged without a restart, and `verify-keepassxc-merge.sh` gains, passing on the three runners, G9: `keypaste approve` narrows an agent and an older KeePassXC copy is moved over the file, and the narrowing holds with the older record in history; and G10: KeePassXC saves while `keypaste approve` serves a bridge, and the next request is refused as `vault-changed` and then served from the merged copy in the same session, with no restart.
- [ ] **P.4a2 — Type into the previous window on Windows.** Needs: P.4a1.
  - The app's typing seam, a keyboard, the foreground window and its identity, in `App/AutoType/`, and the Windows engine: `SendInput` with characters as `KEYEVENTF_UNICODE`, both halves of a surrogate pair, named keys and modifiers as scan codes with the extended flag where it applies, and each key's press and release in one call; the target's root window from `GetForegroundWindow`, its process through `GetWindowThreadProcessId` and `OpenProcess` with `PROCESS_QUERY_LIMITED_INFORMATION` for `QueryFullProcessImageNameW` and the token's integrity level, and its title from `GetWindowTextW`, drawn through `DisplayTextSanitizer` wherever it shows. keypaste asks for no permission, UIAccess or elevation, and does not block the person's input while it types.
  - The app records the last window in front that was not its own through `SetWinEventHook` for `EVENT_SYSTEM_FOREGROUND`, out of context. Items gain Type into ‹app› beside the copy buttons, with that window's title beneath, enabled while the app knows the window; pressing it counts as activity for the idle lock, brings the window forward with `SetForegroundWindow`, waits briefly for it to be in front and reads its identity again before the first key, so a window that closed or was covered gets nothing.
  - D-0547's refusals on Windows: the integrity read through `GetTokenInformation(TokenIntegrityLevel)`, a window whose integrity cannot be read refused too, since UIPI drops keys without saying so; the secure desktop and the lock screen refused as having no foreground window; held keys read through `GetAsyncKeyState`, waited on for up to a second and never given a key-up by keypaste. Before each key the foreground window handle is compared with the target, and a change stops typing, naming the app that took focus; however it ends, every key keypaste pressed is released and the plan dropped.
  - Accepted when the app rules pass headless on the three runners over a recording keyboard and a scripted foreground: a focus change after n keys stops with no further key, every pressed key is released on a stop and on an exception, a lock mid-sequence stops it, and the own-window, integrity and held-key refusals hold. Rule tests hold that each platform's key-sending call appears in one file and that `Keypaste.Cli` and `Keypaste.Mcp` reach no Auto-Type type. A new app.yml job beside clipboard markers, on `windows-2025`, has the real app, started through the D-0199 observer path, type into an unshipped `tests/Keypaste.TypeTarget`, whose two fields and submit report what arrived over a pipe, and into Notepad read through UI Automation: the default sequence submitted, a delay, a password with accented and astral characters, the target opening a second window mid-sequence, keypaste's own window and a lock mid-sequence. An act in `exercise-desktop-acts.sh` has the installed MSI type into Notepad. The integrity probe's result is recorded, the refusal seen or left to a person, and a person's checks of a browser sign-in form, a UAC-elevated window and a Remote Desktop or virtual machine window are recorded or kept as named limits. The rows for who starts Auto-Type and for the target and its refusals, THREATS' entries for typing into a window keypaste does not control and for something other than the person driving Auto-Type, SECURITY's Auto-Type subsection and the desktop guide's Windows part are written.
- [ ] **P.4a3 — Type into the previous app on macOS.** Needs: P.4a2.
  - The macOS engine: `CGEventCreateKeyboardEvent` with `CGEventKeyboardSetUnicodeString` for characters, virtual key codes for named keys and event flags for modifiers, posted with `CGEventPost`; the target from NSWorkspace's frontmost app, its process, bundle identifier and path, and through `AXUIElementCreateApplication` and `kAXFocusedWindowAttribute` its focused window and that window's title, the process and window compared before each key; the previous app recorded from NSWorkspace's activation notifications and brought forward by macOS 14's cooperative activation (`yieldActivation(to:)`).
  - Accessibility is the one permission, asked at the first Auto-Type through `AXIsProcessTrustedWithOptions` with its prompt, which types nothing that time. Titles come from the Accessibility API rather than `CGWindowListCopyWindowInfo`, so Screen Recording is not asked, nor Input Monitoring, since keypaste installs no event tap. A target with no focused window the Accessibility API names is refused, and held keys are read from `CGEventSourceFlagsState` of the HID state. Until the probe settles Secure Input (`IsSecureEventInputEnabled`), keypaste types when the target app holds it, as a focused password field does, and refuses when another process does.
  - Accepted when the probes on `macos-15` are recorded: whether `keypaste-app` started from the job is trusted for Accessibility through the image's TCC grants or needs its own row, whether the Accessibility title read works without Screen Recording, and what typing into osascript's hidden-answer dialog gives with Secure Input on; the native typing job passes on `macos-15` into the type target and that dialog with P.4a2's cases; an act has the installed DMG type into the dialog; a person's check of a Safari or Chrome password field with Secure Input on is recorded or kept as a named limit; and the rows for Secure Input and titles, THREATS' entry for the permissions Auto-Type needs and the guide's macOS part, saying an unsigned build's grant lasts for that build only, are written.
- [ ] **P.4a4 — The global shortcut and the chooser.** Needs: P.4a2, P.4a3.
  - Settings sets the shortcut, off and unregistered until the person sets one, registered with `RegisterHotKey` on Windows and `RegisterEventHotKey` on macOS, which see no other keystrokes and need no permission. Pressed with another program in front, it fixes that window as the target and opens keypaste's chooser, which names the app and the title and lists the items whose associations, title or web address match, each with the sequence it would type, or opens on a search of every item when none does, and shows even for a single match; Enter or a click types the chosen item and Escape types nothing. With keypaste in front it does nothing, and with the vault locked it opens the unlock screen and types nothing.
  - BACKLOG gains KeePassXC's remembering of the last item for a second press.
  - Accepted when, on `windows-2025` and `macos-15`, the native job presses the shortcut over a target and picks in the chooser, with no shortcut set nothing is registered, and the row for the shortcut is written.
- [ ] **P.4a5 — Keep the macOS grant across signed upgrades.** Needs: P.4a3, 3.5b.
  - Accepted when `upgrade-desktop.yml`'s macOS leg, or a person, sees Auto-Type work after an upgrade from one Developer ID build to the next with no new grant, under the hardened runtime with no entitlement or the one found needed, and the guide's macOS steps are checked against it.
- [ ] **P.4b1 — Type on X11.** Needs: P.4a2.
  - keypaste is an X11 client in every Linux session, since Avalonia 12.1's `UsePlatformDetect` does not pick its experimental Wayland backend, and types only where XTest and EWMH's `_NET_ACTIVE_WINDOW` hold. It refuses when `WAYLAND_DISPLAY` is set, `XDG_SESSION_TYPE` is `wayland` or the server has the `XWAYLAND` extension, saying "Auto-Type needs an X11 session. On Wayland, keypaste can't see which window would receive the keys."; when `_NET_SUPPORTED` lacks `_NET_ACTIVE_WINDOW` or the property is empty; when the server lacks XTEST; and when `libXtst.so.6` does not load, naming it. These checks, not `XDG_SESSION_TYPE` alone, decide an X11 session started from a console, which can report `tty`.
  - `XTestFakeKeyEvent` types keysyms found on the XKB map, and a character the layout has no key for through a spare key code keypaste borrows and restores on every end. The target and the previous window come from `_NET_ACTIVE_WINDOW`, watched through PropertyNotify on the root window, the title from `_NET_WM_NAME` or `WM_NAME`, and the PID from X-Resource where the server offers it, else the window's own claim; held modifiers and Caps Lock are read from `XQueryPointer`'s mask, and Caps Lock is refused.
  - BACKLOG gains typing on Wayland through the RemoteDesktop portal, to reconsider once a compositor tells a client which window has focus.
  - Accepted when the native typing job passes on `ubuntu-24.04` under Xvfb and openbox, typing into the type target and `zenity --password` with P.4a2's cases and refusing a dummy `WAYLAND_DISPLAY`; an act has the installed AppImage type into zenity; the row for Linux sessions is written; and a person's checks, a non-US layout on a real Xorg desktop and the refusal in a real GNOME or KDE Wayland session, are recorded or kept as named limits.
- [ ] **P.4b2 — The global shortcut on X11.** Needs: P.4a4, P.4b1.
  - `XGrabKey` registers P.4a4's shortcut.
  - Accepted when the Linux job presses it with xdotool and types through the chooser.
- [ ] **P.1b — Check hardware-key vaults with a physical YubiKey (H-0026).** Human. Needs: none.
  - [P.1a](steps/P.1a.md) built what code can do: create, unlock, save, change and import such a vault in the app, and reach it from the CLI only through the app (D-0366, D-0514), all against a software key.
  - On Windows, macOS and Linux, with the app built from P.1a, make the [desktop guide](desktop.md)'s check 52 with a YubiKey 5 and a second key holding the same secret, then create a vault with the key, and import a KeePassXC database that needs it.
  - Check the refusals with the physical key: another key, the other slot, no key and two keys plugged in are each named, and the spare-key and lost-key limits hold as Settings states them, which PRODUCT §2 asks to be verified before support is advertised.
  - Record the platform, the key's model and firmware, the KeePassXC version and the app's commit in P.1a's limits. No CI runner has a YubiKey, so only a person can make these acts (T-37). OS quick unlock and browser passkeys are separate features.

## T7 — Agents without values

PRODUCT v1.9 (D-0400) adds T7–T10, v1.10 (D-0408) adds the cloud vault to T8 and adds T11, v1.12 (D-0430) adds X.17 to T7, and v1.15 (D-0530) puts T7 in 1.0.0, which T8–T11 follow. Each row carries its purpose until it is selected and detailed.

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
