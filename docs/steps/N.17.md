# N.17 — Redesign the desktop, prompts, browser fill and site as native

Completed 2026-11-17 on `native-redesign`, source only. Selected by the founder's direction of 2026-11-17 without a STEPS row, with one record for the whole redesign.

## Amendments

The founder chose each part from rendered options and approved a drawn target before any code; the decisions of 2026-11-17:

- **Look.** The app follows the system's light or dark setting and accent colour, with native palettes, the system font and system mono, grouped rows, one icon per meaning and a monogram or a bundled service icon per item; amber stays on the marks alone. macOS draws Helvetica Neue and Menlo until Avalonia draws SF's weights.
- **Window.** A rail of places (Items, Agents, Security, Access marked later, Settings), a New button, Favorites as a `favorite` tag, and places per vault with counts; with one vault the sidebar shows its places alone, and an account, "On this Mac", holding several vaults appears with a second. Rows show the tile, title and username in Today, This week and Older; an item shows Website and One-time code. Recommendations became Security.
- **Several vaults.** Each vault has its own unlock session, pipe, agent rules and file watch; idle, ⌘L and quitting lock them together, and a vault locks alone from its menu. PRODUCT re-ratified Track 2 as v1.30.
- **Agents across vaults.** Connect scopes each AI tool to the vaults chosen, and one keypaste connection serves them, each vault's rules and prompts deciding.
- **New item.** Templates without Passkey open their form at once, with Save in, custom fields on every template and labels left of fields in grouped rows (DESIGN rule 5 amended); a `Credit Card` or `Identity` tag makes a card or an identity. The + beside the list's title adds in place.
- **Prompts.** Every prompt is a claim card headed by the wordmark at 64px, naming the agent and the item by title, quoting the agent, with Details and Deny, Always allow where PRODUCT allows it and Allow once; Always block left the prompts. Verified is shown only when keypaste identified the program on its pipe and its parent app, Node running the tool's installed package included. Return presses the primary after the arming second (DESIGN rule 8 amended).
- **Environments.** The place and the `.env` template are Environments, and dev, staging and prod are stages in the app, the CLI, the documents and PRODUCT.
- **Browser.** Connect in the extension pairs by a six-digit code shown on both sides, with no arming; one pairing per browser serves every open vault, sealed by each. A click on keypaste's key lists names from every open vault; a pick fills at once where that login was filled on that site, and a first fill on a site asks in keypaste. Subdomains match as before; http, single-label hosts and forms posting elsewhere warn and ask every time. PRODUCT §3 re-ratified this in v1.30.
- **Site and README.** keypaste.com takes the native look and tells what keypaste solves as one scroll story under "Your centralized access manager", each chapter labelled by its state; the README became a short guide.
- **Organizations.** Company-owned work vaults, the recommendation to move company-email logins into them and the hand-over of a revoked member's work access joined Track 10 as X.18; server access for organizations went to BACKLOG.

## Evidence

- Screen renderer: every palette, 150% and 200%, the long and right-to-left pseudo-locales, every control within reach, one accent per frame and security prompts drawn the same right to left (`Keypaste.App.Tests.Rendering.ScreenRenderer`).
- Several vaults: `OpenVaultsTests`, `SeveralVaultsWindowTests`, the per-vault watch journeys, `SealedAgentRulesTests`, `AuditCompactionTests`, `BridgeAcrossVaultsTests`.
- Prompts and verification: the desktop approval, run, env, save, item-change and picker classes, `RequesterIdentityTests`, the right-to-left prompt test.
- Browser: `SessionBrowsersTests`, `SessionAuthorityBrowserTests`, `ApproverBrowserProtocolTests`, `LoginCandidatesTests`, `LoginLookupTests`, `TerminalApprovalChannelTests`, `BrowserPairingTests`, the extension's content-script specs; the in-page list's clickjacking cases checked in Playwright.
- Tag reads across threads: `VaultTagTests.Tags_read_on_another_thread_while_this_one_reads_and_writes_them_never_fault`.
- CI: ci.yml green on all three runners; app.yml green on all three apart from one Windows run of `SecondStartTests`, whose code the redesign did not change (BACKLOG), with the extension, pairing and fill gates and the desktop gates passing.

## Limits and follow-ups

- Verified, and reading a parent's command line, ran on macOS only; Linux and Windows compile and pass CI's tests but no real AI tool has shown Verified yet.
- The relay's tests and the real-browser fill and pairing gates run only in CI; the extension is in no store yet (8.1g).
- `third_party/KeePassLib` carries one guarded change so reading tags no longer reorders them.
- The relay reaches one owner process at a time (BACKLOG); several vaults per tool are refused under a non-default `KEYPASTE_HOME`.
- The browser extension's own pages and the site's diagrams keep their old styling; `docs/screenshots/` is unused (BACKLOG).
- `scripts/verify-site-endpoint.sh` still expects the home page to load no scripts, which it has not met since the hero animation; a founder decision is pending.
