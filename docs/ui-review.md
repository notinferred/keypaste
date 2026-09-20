# Desktop UI direction

[PRODUCT](PRODUCT.md) defines the local password manager; [STEPS](STEPS.md) owns the active work. This is interface guidance for that scope, not a separate feature roadmap.

## Everyday use

Keep the lowercase keypaste wordmark, a clear vault identity and an obvious locked or unlocked state. Opening the app should lead to creating or opening a vault, then finding, adding, editing or copying a credential. Search, Add and the selected item's details must remain usable in a small window. Notes should have enough room to read and edit without forcing every item into a large editor.

Recovery belongs beside the action it reverses. Entry history is implemented in source; accidental deletion still needs recovery. Preserve familiar KeePass groups and ordinary KDBX data. Custom item templates, shared ownership, organization administration and browser integration are optional ideas in [BACKLOG](BACKLOG.md).

## One session for credentials, agents and projects

The target is one unlock session. An agent request should appear in the app with the requested entry and field, requester, agent-written reason and approval lifetime. Approval remains explicit unless a person configured a matching policy. Denial is the default. Agent traffic must not extend the person's idle session.

Project actions should identify the env set and command before launch. The app should make clear that the child receives a snapshot, and that subsequent vault edits or locks cannot erase values already delivered. Lock must stop new session-backed launches and credential releases.

These are target behaviors. Today the desktop and terminal approver unlock independently, approvals appear in the terminal, and Env Sets copies a `keypaste run` command. The Agent Activity screen reports whether a terminal approver is running. [Desktop guidance](desktop.md) owns the current user instructions.

## Existing source and known gaps

The app creates and opens vaults, adds generated or existing credentials, edits passwords, usernames, URLs and notes, generates passwords or passphrases, copies secrets and restores prior entry values. Env values can be added, replaced, copied and revealed while held. These source features do not establish public desktop availability.

Deletion is permanent. Custom-field editing, encrypted backup/restore, native approvals and launching projects from the app are not implemented. The current interface uses fixed panes; narrower layouts and long text still need review. The earlier layout measurements predate the expanded history pane and are not current rendering evidence.

Username, URL and notes display now preserves ordinary punctuation and line breaks while sanitizing control characters that could misrepresent text. Titles and group paths retain stricter name sanitization. Display limits, clipping and shortening must remain distinguishable from the actual stored or copied value.

## Review scenarios

Review the relevant interface changes on Windows and Linux at minimum and normal window sizes, in both themes and at supported scaling levels. Retain evidence for behavior exercised rather than inferring it from a successful headless build.

| Scenario | What to verify |
|---|---|
| Empty vault and no search matches | A clear next action, with no misleading blank detail pane |
| Many entries, duplicate titles and deep groups | Search and selection keep the chosen item distinguishable |
| Long titles, URLs, multiline notes and tokens | Text stays accessible; values are not altered by display or copying |
| Existing and generated credentials | Clear choices, masked input and no plaintext in accessibility output |
| Mistaken edit or deletion | Available recovery is visible and permanent actions are explicit |
| Agent approval, denial, timeout and dismissal | The requested scope is clear; errors and non-approval paths deny |
| Lock during editing, reveal, approval or launch | Drafts and displays clear; new session-backed secret delivery is refused |
| Project launch and later vault changes | The env snapshot and limits of later locking are understandable |

T1 covers daily vault use and recovery, T2 the shared session, T3 native MCP approval, T4 project environments and T5 installation and delivery. Backend compatibility, secret-display protections and installed-app verification remain required for the behaviors they establish.
