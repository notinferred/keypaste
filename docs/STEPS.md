# Build plan

This plan owns the committed tasks, grouped by product track: each task's dependencies, detail and acceptance evidence for [PRODUCT](PRODUCT.md) v1.20 (D-0681). [ROADMAP](../ROADMAP.md) owns direction, track order and which tasks each milestone needs. [FEATURES](FEATURES.md) owns the capability inventory, [RELEASE](RELEASE.md) distribution evidence, [BACKLOG](BACKLOG.md) optional work, and the [step records](steps/README.md) what each completed task did. The license remains AGPL-3.0.

This file holds open work only. Needs are build dependencies; time and human acts are never Needs, and a Human row is its own track. Ships after names publication gates. External signing identities are inputs, not a queue of enrollment code. A ready row does not authorize publication, account changes or messages. Preserve the secret-path tests, real KeePassXC compatibility, stale-write refusals and release integrity checks while changing product scope.

## K and B — Build infrastructure

These rows serve every track, and ROADMAP has them worked first in 1.0.0 (D-0672): they move architecture rules into the compiler, tests onto the founder's machine and hand-written code onto established libraries in the order PRODUCT §4.9 sets (D-0670, D-0671).

- [ ] **B.5 — Cache NuGet restores in CI.** Needs: none.
  - Locked-mode restores are cached by their lock files' hash, an exception to D-0423's "nothing cached" for restores only (D-0671).
  - Accepted when a second run restores from the cache, a changed lock file restores fresh, and both runs' job times are recorded.
- [ ] **B.6 — Repeat no green branch run on `main`.** Needs: none.
  - A push run on `main` whose commit already has green dispatched `ci` and `app` runs ends at once, naming them, while `require-green-gates.sh` still finds a full green run for a release tag (D-0403).
  - Accepted when an integration push of a verified commit spends under a minute per workflow and an unverified commit still runs every job.

## Q — At home on every platform

The audit of 2026-10-31 checked the desktop against Apple's Human Interface Guidelines (PRODUCT §7.8, D-0682). Q.1 to Q.12 gate 1.0.0 and Q.13 to Q.17 follow it. Each row keeps every control name the tests use and BRAND's one amber per view, keeps the prompts' and masked input's security behaviour (D-0232, D-0301, D-0326, D-0456, D-0634), says Ctrl where macOS says ⌘, and updates [the design handoff](design/README.md) and [the desktop guide](desktop.md) where it changes what they describe. A row that reverses a decision writes the row that supersedes it.

- [ ] **Q.2 — Give the app its menus and the standard shortcuts.** Needs: Q.1.
  - The macOS menu bar holds only File › Import from another app… (`AppMenu.cs`), set on the window, so the app menu is Avalonia's default, with "About Avalonia", an unnamed Quit and Hide Others on ⌥⌘Q, and Windows and Linux have no menus.
  - An application-level menu: keypaste (About keypaste with the version and copyright, Settings… ⌘,, Hide keypaste, Hide others ⌥⌘H, Quit keypaste), File (New item ⌘N, New group, Open vault… ⌘O, also while unlocked, Open recent with Clear menu, Close window ⌘W, Lock ⌘L, the imports and Export…), Edit (Undo, Cut, Copy, Paste reaching a masked field through its own paste path, Select all, Delete, Find on ⌘F and ⌥⌘F beside ⌘K), View (the four places on ⌘1 to ⌘4, Enter full screen ⌃⌘F), Window (Minimize ⌘M, Zoom, Bring all to front, a prompt only through `WindowApprovalChannel.BringForward`) and Help; and a Dock menu with Lock and Open vault…. Windows and Linux reach the same menus from a menu button in the titlebar's empty first column (D-0508). Labels are in sentence case (BRAND). While locked, Settings… is off.
  - On a focused Items row ⌘C copies the password, Delete and ⌘Delete delete, and ⌘Z undoes (Q.5); text fields keep their own.
  - Shortcut text names the platform's key, including Settings' "Ctrl+L locks now." and the masked fields' "Ctrl/Cmd+V to paste.", and `packaging/macos/Info.plist` gains `NSHumanReadableCopyright`.
  - Accepted when headless tests run each menu item's command and each chord under both modifiers, the About item names keypaste, and the desktop guide's Keyboard table lists them.
- [ ] **Q.3 — Add context menus.** Needs: Q.2.
  - Only a sidebar group row opens a menu, on a right-button release (`Views/ShellView.axaml.cs`); item, Trash, project, agent and revision rows have none, and an item's actions sit only in its preview's ⋯ menu.
  - Each row gets a context flyout of its existing commands: an item's Copy username, Copy password, Open URL, Edit fields, Rename or move, Duplicate, Share… and Delete; Trash's Restore and Delete for good; a project's Open; an agent's Settings and Disconnect. The group menu moves onto the context-request event, so right-click, Control-click, Shift+F10 and the menu key share one path, and every entry is also in the menu bar.
  - Accepted when headless tests open each flyout from the pointer and the keyboard and run a command from it.
- [ ] **Q.4 — Keep unsaved work.** Needs: none.
  - Changing place disposes the screen (`ShellViewModel.Show`), which drops the New item form, Edit fields' drafts and agent limits not yet applied, asking nothing. A screen holding a draft asks "Discard …?" with Cancel and Discard before the place changes, from the sidebar, a shortcut or a search typed elsewhere. A lock still discards drafts.
  - In-pane forms answer Return with their confirm, never a danger one, and Escape with Cancel, as the import dialogs do (`ExportImportView.OnRootEscape`); the Share dialog gains Escape and a Cancel button. Escape in a masked field clears it and then reaches the form (`MaskedInput.OnKeyDown`).
  - An error appears under the field it concerns as the field is typed, as the pairing prompt's name does: New item's title, key name and tags, Rename or move's name and Projects' key name. Create waits for a title.
  - Accepted when tests show a draft kept after Cancel and gone after Discard, Return and Escape on each form, and each error under its field.
- [ ] **Q.5 — Make destructive actions consistent and undoable.** Needs: Q.2.
  - A terminal's `keypaste rm` on a vault with no recycle bin is approved with the ordinary amber Allow (`Views/TerminalEditWindow.axaml`). The prompt carries `EditPreview.Recycles`, and when it is false the Allow is drawn as danger and reads "Delete for good", the layout, focus on Deny and arming unchanged.
  - Edit › Undo and ⌘Z reverse the last delete, rename or move, duplicate, and group create, rename, move or delete, each through the inverse write; the Restore link stays. Trash restores several selected rows at once.
  - Every confirmation puts Cancel first and the action last, and its cancel button reads Cancel: item and group delete, the in-pane forms, token revoke, Projects' Remove, Settings' access change and key file replace, Connect and Restore backup.
  - Settings' "Forget them all" and a pairing's Remove confirm in place.
  - Accepted when tests show the danger Allow for an edit that does not recycle, each undo returning the vault to its state before, and the button order of every confirmation.
- [ ] **Q.6 — Say what is happening.** Needs: none.
  - The toast (`Views/ShellView.axaml`) shows a green check for every message, a cancellation or a failure included, lasts 2.8 seconds, cannot be pointed at and is replaced by the next. It carries its tone (success, information or warning), lasts at least four seconds and longer for longer text, waits while the pointer is on it and closes with ×.
  - A moving indicator sits beside each wait, with its own label: Unlocking…, Create vault while its key is derived, Looking for AI tools…, Run it, Create and copy link and Check status.
  - Every error ends with what to do next (BRAND § Voice), among them "That password didn't open this vault.", "The vault is locked.", "The link could not be copied, so it was withdrawn." and "That preference could not be saved, so it will not survive a restart."
  - An empty list offers its next step: a search with no match Search all items and Clear search, an empty log filter Show all, no tokens New token.
  - Accepted when tests show each tone, each busy label and the next step of each error named here.
- [ ] **Q.7 — Work with assistive technology.** Needs: none.
  - About 32 fields and four choice lists sit under a visible label with no accessible name (New item, Settings' idle lock and theme, the Share dialog, Projects), and Avalonia 12.1 does not implement `LabeledBy`: each takes its label as `AutomationProperties.Name`. A field labelled only by its placeholder (Settings' three master-password fields, an agent's limits, Projects' new project, environment and key, New group, the generator's separator) gains a visible label.
  - Only the idle warning is announced (D-0527). The unlock and form errors, the banner saying another program saved the vault, each toast and a prompt's opening with its time limit are announced once, through the same hidden live text.
  - Headings carry `AutomationProperties.HeadingLevel` from their Typography class; Items, Trash and log rows are named from their columns, never a secret or a field D-0488 keeps out of the columns; fold buttons say Show or Hide and tree rows their level; repeated row buttons name their field ("Copy DATABASE_URL"); and the Agents dot and a tag chip's protected or malformed state gain an icon or words beside their colour.
  - Accepted when the headless automation tree names every field and row, and a screen reader on each platform reads an unlock error, a toast and a prompt (the desktop guide's checklist).
- [ ] **Q.8 — Make focus, edges and targets visible.** Needs: none.
  - In the light palette the amber focus ring, focused field, selection bar and warn dot are 1.5 to 1.8:1 against their grounds: they take `#8A5A00`, BRAND's amber on light, in the theme dictionaries, and dark keeps its values. Muted text on hover and selected rows takes the secondary colour where it falls under 4.5:1 (sidebar counts, the toast's countdown, the keycap), and unchecked checkbox and radio edges and field borders reach 3:1.
  - A high-contrast dark and light variant follows macOS's Increase Contrast and Windows' contrast themes through the platform's contrast preference.
  - Fold, chip-remove and clear-scope buttons, drawn 16×16, take at least 20×20 without moving the layout, and row action buttons sit 8 apart.
  - Accepted when a test computes every text and edge pair of `Theme/Tokens.axaml` on its grounds in each variant, against 4.5:1 for text and 3:1 for edges, and the screen renderer draws every screen in each variant.
- [ ] **Q.9 — Let people enlarge text.** Needs: Q.2.
  - Nothing enlarges text, font sizes are fixed on about 180 lines of the views, and the 960×520 minimum window does not fit a 1366×768 screen at 150% Windows scaling. View › Zoom in, Zoom out and Actual size (⌘+, ⌘− and ⌘0) scale the interface up to 200%, kept in `app.toml`; view-level sizes move onto the Typography classes; and the minimum window and the prompt windows scale with it.
  - Accepted when the screen renderer draws every screen at 100%, 150% and 200% with nothing cut off, masked text staying at 14px or larger.
- [ ] **Q.10 — Pick up where the person left off.** Needs: none.
  - The window opens at 1000×680 in the middle of the screen every time, and the split between the list and the preview resets at each unlock. The window's bounds, its maximized or full-screen state and the split are kept in `app.toml` on close, lock and quit, and restored within the screen's working area.
  - The 960 minimum width (D-0344) rules out two windows side by side on most laptops. It falls to about 720, where Items drops its Username and URL columns and the sidebar collapses, and View › Hide sidebar hides it at any width.
  - Accepted when tests restore saved bounds, bring bounds off the screen back onto it, and lay out Items at 720 wide.
- [ ] **Q.11 — Open on a first run anyone understands.** Needs: none. Input: H-0027 for where the terms appear.
  - With no vault and no KeePassXC database found, the welcome's first action is Create a vault, with one plain sentence on what a vault and its master password are; someone with KeePassXC databases still sees them first (D-0681).
  - The terms of use leave the first launch (D-0594), whose own copy says running keypaste needs no agreement, and appear where their two services start: the Share… dialog, and the crash-report choice, asked once on the unlock screen with Send crash reports and Don't send as two equal buttons and no default.
  - One word list beside BRAND § Voice: Trash, Group, Title, key file, top level and Disconnect, replacing recycle bin, Folder, Name, keyfile and the root where the copy uses them for these; and the words a new person cannot know are rewritten: `--client-label`, ceilings, selectors as `--expose` takes them, unlock ID and the relay.
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
  - Items, Trash and the activity log sort by a column head, again to reverse, and resize their columns; heads stay in capitals (BRAND).
- [ ] **Q.16 — Read in any language and region.** Needs: none.
  - Dates and times people read follow the current culture and machine text stays invariant; strings move to resources; right-to-left layout follows the culture, with a chevron-left icon for Back and secrets kept left to right.
- [ ] **Q.17 — Finish the polish.** Needs: Q.5.
  - Window titles name the place; the pointing hand appears only on links; a prompt is drawn inactive while another window has focus and is disarmed again on activation; toasts sit under the titlebar; scroll bars follow the system's setting; End is drawn plain and End all confirms; a delete into Trash asks nothing once undo exists, while a delete for good still asks; buttons are named by their result ("Connect Claude Code", "Reset clients.toml") and every command that opens a form ends in an ellipsis; tooltips say what a control does, with Learn more beside selectors and token scopes; and Appearance's Theme gives way to the system's setting, kept only where a Linux desktop reports none.

## T3 — AI requests in the app

Every release still needs a person's answer or a rule they set, and the bridge stays vault-free (PRODUCT §§2 and 3.2). The bridge, the in-app prompt (4.4), the Agents screen (4.3b) and connecting from the app (2.6a) are reused.

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
- [ ] **4.7c2 — Publish and verify the first desktop release as 1.0.0.** Needs: 4.7c1, 3.5b, 4.7a2, 4.7e, G.5. Ships after: R.1a, the engineering rows K.7 to K.12, B.5 and B.6 (D-0672), every T6 task but 1.4b4 (D-0669), V.8a and V.8b, 4.11a to 4.11c, Q.1 to Q.12 (D-0682), and the fixes still marked for 1.0.0 (D-0669). Inputs: a verified Microsoft Artifact Signing identity as keypaste (H-0017), or the recorded cloud-HSM fallback, 3.5a's Apple credentials, the approved terms of use (H-0027) and the crash-report database switched on (H-0028).
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
