# KeePassXC-Browser provenance

| Field | Value |
|---|---|
| Upstream repository | <https://github.com/keepassxreboot/keepassxc-browser> |
| Tag | `1.10.4.1` |
| Commit | `8b0b2c4347126f4983f59ea7dd6ca2a2a667cf48` |
| Archive | `https://codeload.github.com/keepassxreboot/keepassxc-browser/tar.gz/8b0b2c4347126f4983f59ea7dd6ca2a2a667cf48`, SHA-256 `9db127b0f2a63885091935d90b4cff58198aa4b2cce243e61cdb6459b2994a5b` |
| Forked on | 2026-11-14 |
| Original work | KeePassXC-Browser by the KeePassXC Team and its contributors |
| Licence | GPL-3.0; see `LICENSE` |

keypaste's browser extension is this fork. `src/` is upstream's `keepassxc-browser/`; `dist/`, `build.js`, `package.json`, `package-lock.json`, `eslint.config.mjs`, `keepassxc-protocol.md`, `tests/` and `playwright.config.ts` come from the repository's root. Upstream's [CHANGELOG](https://github.com/keepassxreboot/keepassxc-browser/blob/1.10.4.1/CHANGELOG) records its history.

## Licence

The extension is GPL-3.0 and a separate program from keypaste's AGPL-3.0-only code: it runs in the browser and reaches keypaste only through native messaging, as the host `com.keypaste.browser`, never by linking. Lucide's icons (ISC) and the Instrument Sans typeface (OFL-1.1), which keypaste adds, keep their licences on the options page; TweetNaCl.js and webextension-polyfill come from upstream unchanged.

## Local modifications

The wire protocol is upstream's, with the same NaCl box, so keypaste's relay answers `change-public-keys`, `get-databasehash`, `associate`, `test-associate`, `lock-database`, and the `database-locked` and `database-unlocked` signals as KeePassXC does. Three things are keypaste's own, which [keepassxc-protocol.md](keepassxc-protocol.md#keypastes-changes) records: `get-logins` answers each entry with an empty password, its vault, the open vaults that are locked, the page's site and why every fill there is asked, and a list matching nothing is an answer rather than error 15; `get-login` asks for the one login the person picked by its `uuid`; and `open-keypaste` brings keypaste forward.

### Removed

| What | Files and paths |
|---|---|
| Saving and updating a login from a page | `content/banner.js`, `css/banner.css`, `background/credentials.js`; `set-login`, the Save context menu item and command, form-submit detection in `content/form.js`, which keeps only the submit button Auto-Submit presses |
| Password generator | `content/pwgen.js`, `css/pwgen.css`, `icons/key.svg`; `generate-password`, its command and context menu item |
| TOTP | `content/totp-field.js`, `content/totp-autocomplete.js`, `css/totp.css`, `icons/otp.svg`; `get-totp`, segmented code fields and their site exceptions, the Fill TOTP command and context menu item |
| Custom login fields and string fields | `content/custom-fields-banner.js`, `css/define.css`, `css/button.css`, `icons/custom_login_fields.svg`; filling `KPH:` string fields and the Fill attribute context menu; a field whose `autocomplete` is `one-time-code` is never taken for a username |
| Passkeys | `content/passkeys*.js`; `passkeys-register`, `passkeys-get` |
| HTTP authentication | `background/httpauth.js`, `popups/popup_httpauth.*`, `options/http-auth-dialog.png`; `httpAuth` in `get-logins` |
| Other requests keypaste does not answer | `get-database-groups`, `create-new-group`, `request-autotype` and its context menu item, Fill password and its command, the WebSocket connection |
| Several databases | The keyring holds one pairing, which a new pairing replaces |
| Settings | `options/` (settings, shortcuts and getting-started pages), `managed_storage.json` and the `storage.managed_schema` key, site preferences (ignored sites, allowed cross-origin frames, username-only pages), the colour and monochrome toolbar icon choices with `offscreen/`; the remaining settings are fixed in `background/page.js`, and `options/` is one page with the pairing, how to pair and where keypaste's settings are |
| Getting started and update checks | The first-install page, the popup's getting-started and troubleshooting alerts (one line in the popup instead), the KeePassXC update check against GitHub, its new-version toolbar icons, and every reading of the KeePassXC version |
| Locales | Every `_locales` folder but `en`, whose strings are cut to those still used |
| Bootstrap and Fork Awesome | `bootstrap/`, `fonts/fork-awesome.min.css`, `fonts/forkawesome-webfont.woff2` and their licence texts; every Bootstrap class and Fork Awesome glyph in the pages |
| KeePassXC's artwork | `icons/keepassxc*.png`, `icons/keepassxc.svg`, `icons/locked.svg`, `icons/disconnected.svg`, `icons/toolbar/` |
| Colour theme | `get_color_theme`, the `colorTheme` setting and the `data-bs-theme` attribute; the pages and the content scripts follow `prefers-color-scheme` |
| Assets no longer referenced | `bootstrap/bootstrap.min.js`, `icons/help.svg`, `icons/keepassxc-dark_*.png`, the dark and light toolbar icon sets |
| Repository files | `.github`, `.tx`, `.vscode`, `dev-resources`, `CHANGELOG`, `README.md`, `jsconfig.json`, the Prettier files and `src/manifest.json`; `tests/page.spec.ts` and the TOTP, password-change, version, placeholder, URL-trimming and slash tests, whose code is removed; `@npmcli/fs` |

Permissions go from `activeTab`, `clipboardWrite`, `contextMenus`, `cookies`, `nativeMessaging`, `notifications`, `offscreen`, `storage`, `tabs`, `webNavigation`, `webRequest`, `webRequestAuthProvider` (Firefox `webRequestBlocking` and `https://api.github.com/`) and optional `privacy`, to `contextMenus`, `nativeMessaging`, `storage`, `tabs` and `webNavigation`, with the same host permissions.

### Changed

- `background/keepass.js` makes the identification key before `associate` sends it (`beginPairing`, the `begin_pairing` message) and derives six digits from it, the first four bytes of its SHA-256, big-endian, modulo a million; the popup's and the options page's Connect show them while keypaste asks, as "Check keypaste", and keypaste's confirmation shows the same digits from the key it receives, so the messages carry nothing new.
- Nothing asks keypaste for logins as a page loads: `content/keypaste.js` and `content/observer-helper.js` only draw the key icon, and `content/username-field.js` asks for the page's logins, by name, when the person clicks it. The extension holds no password but while it fills the one picked, and `content/fill.js` fills only that one, from `get-login`; the auto-fill of a single login, the reuse of a tab's logins and filling from the page's load are gone.
- `background/page.js` sends keypaste the tab's address as the browser reports it, never the page's own, and refuses a frame whose origin is not the tab's, as `content/keypaste.js` already did in the frame.
- `background/init.js` adds its message listener in the worker's first turn and has it wait for the worker's start, since the browser delivers the message that starts a stopped worker only to listeners added then; upstream adds it after reading storage, so a press of Connect, or any page's message, sent while the worker was starting was dropped.
- `background/client.js` times a request from when every request sent before it on the native port is answered, since keypaste answers one at a time: a status check queued behind a pairing the person is still confirming is no longer taken for keypaste being unreachable, which cleared the database hash the pairing's answer is verified against.
- `background/client.js` answers a request still waiting on the native port a reconnect closes as not connected at once, rather than at its timeout: the worker's first connect, overtaken by a Connect pressed while it starts, otherwise resynced every page five seconds later and closed a login list the person had open.

### Restyled

The popup, the login popup, the options page, the field icon, the drop-down and the page notifications follow keypaste's [design system](../docs/DESIGN.md), light and dark, in hand-written CSS: `css/colors.css` holds the palette as variables for the pages and the shadow roots, and `css/page.css` the pages' type, buttons, fields, focus ring and notes. The content scripts' UI stays in closed shadow roots, as upstream's does.

- Icons are Lucide's, from `third_party/lucide/icons/`, inline in the pages and drawn by `kpxcUI.createIcon` in the content scripts.
- The pages are set in Instrument Sans, copied unmodified into `fonts/` with its OFL text; the content scripts use it only where the system has it.
- `render-icons.js` draws `icons/` from `assets/brand/`'s two tiles, the mark as SVG and PNG at 16, 32, 48, 96 and 128 px, and the locked, disconnected and unpaired toolbar states as the favicon cut with a badge whose glyph says the state.
- The login popup's logins are buttons with the title and username on two lines, filtered by plain text. The drop-down is keypaste's list (`content/autocomplete.js`, `content/credential-autocomplete.js`, `css/autocomplete.css`): opened only by the key icon, in a closed shadow root in the browser's top layer, it heads with the site, lists each login as a `listbox` `option` with a monogram, its title, its username and, while several vaults are open, its vault, then a row for each locked vault with Unlock…, and New login… and Open keypaste; it says "No logins for" the site, or "keypaste is locked" with Unlock…, when that is so. A status line announces the login the arrow keys choose, since focus stays in the page's field. A pick counts only from a trusted click or key, half a second after the list appears, while the list is the topmost element under the pointer and neither it nor its ancestors is faded, filtered, masked or clipped. A notification names keypaste, carries a status icon and a Dismiss button, and is announced as a status or, for an error or warning, an alert.
- Button names and words follow DESIGN's voice: Reload is Try again, and no message carries an exclamation mark.

### Renamed

- The native host `org.keepassxc.keepassxc_browser` is `com.keypaste.browser`.
- The extension is `keypaste`, described as "Fill your logins from keypaste", version `1.0.0`, homepage `https://keypaste.com`; Firefox's add-on ID is `browser@keypaste.invalid` and the Chromium manifest's `key` is `test-keys/chrome.pub`, whose ID `BrowserIdentity.cs` names, until H-0029 supplies the stores'.
- Strings that named KeePassXC as the app name keypaste, in keypaste's words: vault for database, and sentence case.
- `content/keepassxc-browser.js` is `content/keypaste.js`, and `isKeePassXCAvailable` and the status's `keePassXCAvailable` are `isKeypasteAvailable` and `keypasteAvailable`.
- `build.js` writes `artifacts/extension/keypaste-chromium.zip` and `keypaste-firefox.zip`, and an unpacked copy of each carrying `LICENSE`, byte for byte the same on every run, without Transifex and without changing `src/`. `tests/pair-in-browsers.js` is keypaste's, driven by `scripts/verify-extension-load.sh`.

Other identifiers, such as the `kpxc` prefixes, the `keepass` object and `keepassxc-protocol.md`, keep upstream's names.
