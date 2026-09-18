# Product UI direction and source findings

This document records the accepted interface direction and defects found in the desktop source. [PRODUCT](PRODUCT.md) owns product scope; [STEPS](STEPS.md) owns implementation and release status.

Retain the existing lowercase keypaste wordmark across desktop and narrow layouts.

The default screen should show search, Add item and a simple item list. Opening an item shows the fields relevant to its type and an obvious way back. Avoid a permanent preview, empty sections and always-visible Notes/Fields/History tabs. History remains reachable from the item menu. Long notes expand into a working area when opened; they do not determine the layout of every item.

Use familiar templates such as Login, Secure note and API credential within one consistent editor. A login starts with website, username and password. A note opens as a document. A service credential can keep named protected fields and authentication instructions together. Existing notes remain intact, and moving tokens into separate fields is optional. Additional fields appear when added or already populated. These are proposed presentations of KDBX content, not a new vault format.

Ownership and access must be understandable before sharing. Keep personal storage distinct from organization-owned storage, show the selected vault on each item, and expose who can access shared content. Personal users should not encounter organization policy or provisioning controls during routine use.

| Context | Everyday experience | Additional controls |
|---|---|---|
| Personal | Save, find, fill, edit and recover items | Sharing appears when available and requested; local use needs no account |
| Shared work | The same item editor within an explicitly shared vault | Access and membership are visible; permissions govern available actions |
| Organization administrator | The same item experience for credentials they may access | A separate administration area for people, policy, audit and offboarding |

Environment mapping and agent access belong to relevant item or project actions. Introduce them when a user connects that workflow. The product's opportunity is consistent credential use across people, applications and controlled automation, with local access and KDBX portability. A familiar form or sharing pattern alone does not establish an advantage over an existing product.

Keypaste currently opens existing vaults and provides search, generated or blank entries, username/URL/notes editing, copying and permanent deletion. [Env Sets](../src/Keypaste.App/Views/EnvSetsView.axaml) offers generated variables, reveal/copy/remove and a copied run command. [Agent Activity](../src/Keypaste.App/ViewModels/AgentActivityViewModel.cs) reports terminal-approver availability. Desktop vault creation, existing-password editing, custom-field controls, recovery, native approvals and browser filling remain planned. The current [entry view](../src/Keypaste.App/Views/EntriesView.axaml) has a narrow right-hand inspector.

## Window and text behavior

The [window](../src/Keypaste.App/Views/MainWindow.axaml) defaults to 1000 × 680 logical units and allows 720 × 520. The [shell](../src/Keypaste.App/Views/ShellView.axaml) reserves 220 for navigation and 48 for content margins. Entries reserves 180 for groups, 320 for details and two one-unit separators. Its nominal list budget is `window width − 770`, before controls and native window effects. These are source-derived budgets, not measured Avalonia layout.

| Window size | Nominal entry-list width | Design consequence to verify |
|---|---:|---|
| 720 × 520 minimum | −50 | Fixed columns already exceed the available width |
| 1000 × 680 default | 230 | Search, Add and Delete compete with a narrow list |
| 1280 × 800 | 510 | More room for title and group together |
| 1440 × 900 | 670 | Better comparison space; details remain fixed at 320 |

The current views have no responsive breakpoints or draggable splitters. Future layout work should preserve readable text and full-content access as the window narrows.

The [detail model](../src/Keypaste.App/ViewModels/EntryDetailViewModel.cs) applied [name sanitization](../src/Keypaste.Core/EntryNameSanitizer.cs) to the username, URL and notes, so `https://example.test/path` displayed as `https: example.test path` and newlines, brackets and other structural characters became spaces, damaging authentication instructions and configuration examples. Those three fields now use [display sanitization](../src/Keypaste.Core/DisplayTextSanitizer.cs) instead, which keeps ordinary punctuation and line breaks and still replaces bidi overrides, zero-width and tag characters and other controls. Titles and group paths keep the name rule, because they address an entry rather than being read. Stored values and edit drafts remain unchanged.

Display limits are 128 UTF-16 units for names, 512 for username/URL and 8192 for notes, before visual clipping. Disclose shortening and allow complete reading/editing. Entry passwords show at most 24 dots; the [env reveal control](../src/Keypaste.App/Controls/RevealedValue.cs) has no explicit text-width constraint. Long tokens need bounded layout without changing their copied value.

## Scenario comparison

KeePassXC remains the declared capability baseline; it does not prescribe the interface. Its table/preview structure is unsuitable as the universal default for this product. [1Password's vault model](https://support.1password.com/explore/get-started/) demonstrates familiar personal/shared organization, and its [business controls](https://support.1password.com/create-share-vaults-teams/) distinguish access from administration. These references inform patterns rather than establish keypaste feature parity.

| Scenario | Keypaste today | Comparison and proposed direction |
|---|---|---|
| Small window or many entries | Fixed panes and roomy navigation | A simple searchable list with clear item identity; open details when needed |
| Authentication notes and service tokens | Narrow notes pane; sanitization changes content | Expandable multiline notes and optional protected fields within the relevant item |
| Empty vault or no search matches | Blank list and details | Provide a specific explanation and next action |
| Mistaken edit or deletion | No desktop recovery | Complete history/trash tasks. [KeePassXC recovery](https://keepassxc.org/docs/KeePassXC_GettingStarted), [KeePass history](https://keepass.info/help/v2/entry.html) |
| Daily login use | Password editing and browser filling unfinished | Complete the journey already supported by [KeePassXC browser integration](https://keepassxc.org/docs/KeePassXC_GettingStarted#_browser_integration) and [KeePass Auto-Type](https://keepass.info/help/base/autotype.html) |

## Build and acceptance

Keep F.11/F.10 repair ahead of UI delivery under the existing plan. Complete Create/Open and credential editing in 4.8/4.9, including faithful Notes rendering. V.2b/V.3b own recovery, V.5b organization/search and V.7 named custom fields with protected flags. Use 4.5 for numeric task thresholds, 4.6 for native rendering and 4.7b for installed candidates. Native approvals, E.1 and browser work retain their prerequisites; P.0 owns the comparison contract.

Cross the four window sizes with light/dark themes and 100%, 150% and 200% scaling where supported on each advertised platform.

| Fixture | Acceptance evidence to retain |
|---|---|
| 0, 20, 500 and 10,000 entries | Search/selection timings and memory measurements against 4.5 thresholds |
| 20/100/300-unit titles; duplicate titles; deep groups | Selection remains distinguishable and fully inspectable |
| Synthetic Cloudflare/Akamai notes mixing URLs, credentials, headings and configuration | Punctuation, line breaks and whitespace survive display, edit, save and reopen |
| 200/2000/10,000-unit notes; 64/512/4096-unit service tokens | Full notes remain accessible; tokens stay intact without overlapping controls |
| Several protected custom fields | Reveal/copy affects only the chosen field; other values remain masked and absent from automation output |
| Empty results, conflicts, keyboard navigation and lock during editing/reveal | Controls remain reachable; lock clears notes, drafts and revealed values |

Verify these behaviors in the native app, including field protection, persistence and interoperability.
