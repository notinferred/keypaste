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

The wire protocol is upstream's unchanged, so keypaste's relay answers it: `change-public-keys`, `get-databasehash`, `associate`, `test-associate`, `get-logins`, `lock-database`, and the `database-locked` and `database-unlocked` signals, with the same fields and NaCl box.

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

### Restyled

The popup, the login popup, the options page, the field icon, the drop-down and the page notifications follow keypaste's [design system](../docs/DESIGN.md), light and dark, in hand-written CSS: `css/colors.css` holds the palette as variables for the pages and the shadow roots, and `css/page.css` the pages' type, buttons, fields, focus ring and notes. The content scripts' UI stays in closed shadow roots, as upstream's does.

- Icons are Lucide's, from `third_party/lucide/icons/`, inline in the pages and drawn by `kpxcUI.createIcon` in the content scripts.
- The pages are set in Instrument Sans, copied unmodified into `fonts/` from the desktop app's with its OFL text; the content scripts use it only where the system has it.
- `render-icons.js` draws `icons/` from `assets/brand/`'s two tiles, the mark as SVG and PNG at 16, 32, 48, 96 and 128 px, and the locked, disconnected and unpaired toolbar states as the favicon cut with a badge whose glyph says the state.
- The login popup's logins are buttons with the title and username on two lines, filtered by plain text; the drop-down is a `listbox` of `option`s named by title and username, and a status line announces the login the arrow keys choose, since focus stays in the page's field. A notification names keypaste, carries a status icon and a Dismiss button, and is announced as a status or, for an error or warning, an alert.
- Button names and words follow DESIGN's voice: Reload is Try again, and no message carries an exclamation mark.

### Renamed

- The native host `org.keepassxc.keepassxc_browser` is `com.keypaste.browser`.
- The extension is `keypaste`, described as "Fill your logins from keypaste", version `1.0.0`, homepage `https://keypaste.com`; Firefox's add-on ID is `browser@keypaste.invalid` and the Chromium manifest's `key` is `test-keys/chrome.pub`, whose ID `BrowserIdentity.cs` names, until H-0029 supplies the stores'.
- Strings that named KeePassXC as the app name keypaste, in keypaste's words: vault for database, and sentence case.
- `content/keepassxc-browser.js` is `content/keypaste.js`, and `isKeePassXCAvailable` and the status's `keePassXCAvailable` are `isKeypasteAvailable` and `keypasteAvailable`.
- `build.js` writes `artifacts/extension/keypaste-chromium.zip` and `keypaste-firefox.zip`, and an unpacked copy of each carrying `LICENSE`, byte for byte the same on every run, without Transifex and without changing `src/`. `tests/pair-in-browsers.js` is keypaste's, driven by `scripts/verify-extension-load.sh`.

Other identifiers, such as the `kpxc` prefixes, the `keepass` object and `keepassxc-protocol.md`, keep upstream's names.
