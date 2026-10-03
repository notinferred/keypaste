# Changelog

Published versions are available at `https://dl.keypaste.com/v<version>/` with checksums and corresponding source. CLI/MCP binaries are unsigned and un-notarized; the desktop has no public release. [RELEASE.md](docs/RELEASE.md) records platform support and verification requirements. The release workflow requires a section matching each tag.

## Unreleased

Upgrade the bridge and `keypaste agent` together: they must be the same version. Approval prompts answer `d`, `o` or `h` instead of `y` or `n`. `env export` now writes `kp://` references by default and values only with `--dotenv`. `rm` moves entries to the vault's recycle bin, which saves the vault as KDBX 4.1; KeePass 2.48 and KeePassXC 2.7 or later open it. The desktop entries below ship with the first public desktop release, which is published together with this version ([RELEASE](docs/RELEASE.md)). Each entry that names a step links its record, which holds the full account.

`keypaste env set`, `env pull` and the app's Env profiles now keep a new key as a protected field, on the entry you name with `--entry` or choose in the app, or else on the project's entry `env/<project>/.env` (`.env.<profile>` for another profile), created tagged on first use; a key already in the vault is updated where it is, and `env rm` leaves a field's value in its entry's history. A new key is named with capitals, digits and `_`, as a project's variable fields are. `env tag`, `env untag` and the entry pane's tag chips now say which environment and fields a project tag reaches and ask first, and without a terminal `env tag` and `env untag` need `--yes` ([C.1c](docs/steps/C.1c.md)).

When another program saves the vault the desktop has open, such as KeePassXC or a sync from your phone, the app now says so within five seconds, and Reload loads the file without asking for your password again. Agents are refused until you reload, then stay connected and are asked again. An edit the app could not save because of the other save is named in the notice and discarded by Reload ([N.16](docs/steps/N.16.md)).

An agent can now ask for one custom field of an entry it may see, such as `OPENAI_API_KEY`: the prompt names the field, and a grant for it serves none of the entry's other fields. The same field can be named in `kp:///<group>/<title>#OPENAI_API_KEY` for `keypaste run --env-file` and the `run` tool, in `policy.toml`'s `fields` and in `keypaste share --field`. Only a field named like an environment variable leaves; any other, such as `Recovery codes` or `otp`, is refused before anyone is asked or anything is read ([C.5a1](docs/steps/C.5a1.md)).

keypaste has a new brand: an icon "k." and a wordmark "keypaste." in Hepta Slab with an amber dot, amber on ink, Instrument Sans and Fragment Mono ([BRAND](docs/BRAND.md)). The desktop app is rebuilt on it, with restyled screens, a new lock screen and restyled prompt windows, and keypaste.com and the README use it too.

Closing the desktop app's window on Windows or macOS now locks it and leaves it in the tray or menu bar, whose icon offers Open, Lock and Quit; on Linux that is a choice in Settings › Startup. Settings › Startup can also open keypaste at login, locked, and with no window while it stays in the tray ([G.4a](docs/steps/G.4a.md)).

Opening keypaste while it is already running, from the Start menu, a shortcut or a launcher, now shows the running app's window instead of starting a second copy with its own tray icon, and a start at login while it runs does nothing. One app runs for each user and `KEYPASTE_HOME` ([F.38](docs/steps/F.38.md)).

When KeePassXC merges a vault with another copy, it no longer drops a value keypaste saved in the same second as the save before it. Each save of an entry now takes a later second than the version it replaced, so a burst of saves can be stamped a few seconds ahead of the clock ([F.27](docs/steps/F.27.md)).

Agents and the CLI now use the vault you chose when nothing names one: the app chooses the first vault you create or unlock and says so, Settings › Agents and the CLI changes it, and `keypaste use <path>` does the same from a terminal. `--vault` and `KEYPASTE_VAULT` still win, `keypaste setup` and the app's Connect no longer write `--vault` for the chosen vault, and an existing client entry keeps the vault it names. With a vault chosen and none named, `keypaste import <file>` copies into it; `--in-place` keeps the file where it is ([G.1](docs/steps/G.1.md)).

A project's variables now include the env-named fields of entries tagged `env:<project>` or `env:<project>:<environment>`, beside its `env/<project>` variables: `keypaste run` in every form, `env ls`, `env export`, `env diff` and the app's Env profiles use them, and each run prompt names the entry every value comes from. A set is refused whole, naming the entries, when a key is on two entries, two keys differ only in case, an entry has expired, a custom field is named like a standard one such as `PASSWORD`, or a value holds a KeePass placeholder such as `{PASSWORD}`, which keypaste does not fill in yet. A set holding any entry of a protected environment, by its group or any of its tags, is asked about every time with Allow once only, needs a token that allows prod, and cannot go into a token bundle ([C.1b](docs/steps/C.1b.md)).

`keypaste add`, `rm`, `access`, `env set`, `env rm`, `env pull` and `import` are now refused while the desktop app or `keypaste agent` holds the vault, before asking for a password, as `set` and `rotate` already were. The message names what holds it and says to make the change there or run `keypaste lock`. Scripts that saved through the CLI while the app was unlocked now stop there ([N.10](docs/steps/N.10.md)).

A secret the desktop copies now also asks macOS pasteboard managers such as Maccy, and KDE's Klipper, not to keep it, as it already asked Windows' Clipboard History; a copied run command is left for them to keep ([N.12](docs/steps/N.12.md)).

Scoped tokens and Diagnostics moved to Settings › Advanced, beside the activity log and share links. A client's policy is now chosen from its card's ⋯ menu on Agents, and History shows its records without Verify chain, which is on the activity log ([N.1b](docs/steps/N.1b.md)).

The wordmark and the icon are a lighter cut: the wordmark in Hepta Slab at weight 500 and the icon at 580, each with a square dot sized from its letters, in the app, its icons, keypaste.com and the README ([N.7](docs/steps/N.7.md)).

New item now starts from a template, Login, API key, Database, Server or Secure note, picks its folder from your vault's groups instead of a typed path, and takes tags and notes; an API key is kept as a protected field named as a project reads it, and a database's or server's host as a plain `Host` field. The item is written whole, with no history item ([N.4](docs/steps/N.4.md)).

An item's URL is now a link that opens in your browser when it is a web address, with its own Copy; any other scheme, such as `javascript:` or `file:`, is shown as text and never opened. The item's `kp://` reference and KDBX identifier moved into its ⋯ menu, and the Agent access card shows only for items agents can see ([N.5](docs/steps/N.5.md)).

The first time you open the desktop, it lists the databases KeePassXC last opened on this machine, the one it had in front first, beside Open another file… and Create a new vault…, and choosing one takes you to its unlock. keypaste reads KeePassXC's list of recent databases and never writes KeePassXC's files. The lock screen no longer talks about agents, and Unlock with a YubiKey too is under More options unless the vault last opened with a key ([N.2](docs/steps/N.2.md)).

Items is laid out as KeePassXC's main window: the vault's groups fold under Items in the sidebar, with your projects under their own heading; the list is a table of titles, kinds and groups above the chosen item's preview, whose fields read one line each; and a new item, an edit or a comparison of two revisions takes the whole view ([N.14](docs/steps/N.14.md)).

Screen readers now announce the desktop's icon-and-text buttons by their labels, the three Copy buttons by what they copy, and item rows by their titles, instead of by control type names ([F.25](docs/steps/F.25.md)).

A SIGTERM that reaches `keypaste run` while it is starting your command, such as `docker stop` or `timeout` right after launch, now reaches the command, or stops it from starting with 128 plus the signal's number and a line saying so. keypaste used to die of it and could leave the command running without it, most often on macOS ([F.37](docs/steps/F.37.md)).

The desktop follows your system's light or dark setting unless Settings chooses one. Item titles are set in Instrument Sans, status text is darker and readable on the light palette, and every screen in both palettes keeps amber for its one main action or live signal. The icon and wordmark replace the monogram in the app, its icons, keypaste.com and the README, and `keypaste --help` opens with what keypaste is ([N.7](docs/steps/N.7.md)).

Amber now marks one thing on a screen, its main action or something live such as a waiting request: selected rows and icons, links, countdown bars and an entry's "in use" dot are drawn in the ordinary colours ([N.1a2](docs/steps/N.1a2.md)).

The desktop has four places: Items, with each project's row beneath it, Agents, Trash and Settings, on `Ctrl/Cmd+1` to `4`. History moved under Agents, and the whole activity log and your share links under Settings › Advanced; Share… is in an item's ⋯ menu, New project, Import .env and Import .kdbx in Items' "+" (and Import under File on macOS), and the titlebar's search is the only one and says where it searches. The MCP server card is gone: the Agents row shows a count and a dot ([N.1a1](docs/steps/N.1a1.md)).

The desktop now looks for keys left in entry notes, such as `STRIPE_SECRET_KEY=…` or a GitHub token on a line of its own, when you unlock and after each save. Settings › Recommendations lists each by entry and key, never the value, with a quiet count on Settings; Move to a field makes each value a protected field and takes its line out of the notes in one revision, and Dismiss remembers your choice on this machine ([C.2](docs/steps/C.2.md)).

The desktop's entry pane now shows an entry's custom fields by name, each value masked until you hold it, plain ones too, and copied with the same clear as a password. Add field, Replace, the shield and Remove add a field (protected unless you switch it off), change a value, switch protection and remove one, each as one revision; KeePassXC's own attributes such as `otp` can be held and copied but not changed. Tags are chips you can add and remove, and a project tag shows its environment and whether it is protected ([V.7b](docs/steps/V.7b.md)).

An entry's own KeePass tag now puts it in a project: `env:<project>` in its dev environment, `env:<project>:<environment>` in another. `keypaste env tag` and `env untag` write and remove the tag, naming the fields that join or leave, and `env ls` lists each project's environments, protected ones marked, and the entries tagged into each, marking a project that has an `env/` group as legacy. A tag naming a protected environment, such as `env:billing:prod`, makes every agent request for that entry ask and offer Allow once only. Tagged fields do not reach `run` yet ([C.1a](docs/steps/C.1a.md)).

`keypaste set <entry> --field <name>` sets a custom field on an existing entry, protected unless `--plain`, and several `--field` make one revision. `get --field` copies or prints one, `field ls` names an entry's fields and their protection without a value, and `field rm` removes one, which stays in history. KeePassXC's own attributes such as `otp` are read and never written ([V.7a](docs/steps/V.7a.md)).

Every approval prompt, in the desktop and in `keypaste agent`, offers Deny, Allow once, which keeps nothing, and Allow for 1 hour (`d`, `o` and `h` at the terminal), whatever lifetime the agent asked for. `--max-ttl` shortens the hour. An entry in a protected profile such as `prod` offers Allow once only.

A project holds profiles: `env/<project>` is dev and `env/<project>/<profile>` any other. `-p` picks one in `env` and `run`, `env diff` compares key names across profiles without printing a value, and profiles named `prod` or `production` are protected. `kp://<project>/<profile>/<KEY>` and `kp:///<group>/<title>#<field>` name a value without holding it.

`keypaste env export` now writes a `.env.keypaste` of `kp://` references and no values, safe to commit; `--dotenv` still writes plaintext. `keypaste run --env-file .env.keypaste` resolves every reference or starts nothing.

`keypaste token create` makes a scoped, inject-only token for CI, such as `read:acme-api/staging/*`, printed once and expiring after 30 days by default. `keypaste run --token` injects what its scope covers through the process holding the vault, which audits it, and `token bundle` seals the sets into a file for `run --bundle`. A protected profile needs `--allow-prod` and still asks you live. The Agents screen creates, lists and revokes the same tokens.

`keypaste share` encrypts one field, or an entry's username, password and URL together (`--field login`), on this machine and uploads only ciphertext. The link holds the key, opens 1 to 10 times, expires after 5 minutes to 7 days, can require a passphrase and can be revoked; `share ls` lists your links. keypaste.com serves them from 2026-09-25.

`keypaste import <file.kdbx>` copies another KeePass file into the vault with its fields, attachments and history, leaving its recycle bin behind and never writing the file. `--dry-run` shows the plan and `--in-place` keeps editing the file itself. The desktop has the same as its Import .kdbx dialog.

`keypaste-mcp --allow-run` offers agents a `run` tool. After you approve the exact command, directory, variable names and reason, or under a grant of at most 15 minutes for that command line, it runs the command with the values in its environment and returns its output with each value replaced; a command the agent can edit can still reveal a value (T-35). Nothing adds `--allow-run` for you.

`~/.keypaste/clients.toml` narrows one MCP client at a time: Ask every time, Session grants up to 1 hour, or Inject only. Set it with `keypaste mcp policy` or on the desktop's Agents screen.

The desktop app unlocks a vault that also needs a YubiKey, as KeePassXC protects one with HMAC-SHA1 challenge-response, and Settings adds or removes the key after saying what a lost one costs. It shows Touch your YubiKey while the key waits, on unlocking and on every save. It has not yet been tried with a physical key, and the CLI cannot open such a vault (T-37).

`keypaste rotate <entry>` and the entry pane's Rotate replace a password with a generated one, keeping the old value in history and never showing the new one.

`keypaste grants` lists the grants the process holding the vault has given, never a value; `grants revoke <id|agent>` and `--all` end them. `keypaste lock` locks the desktop app or `keypaste agent` holding the vault.

The CLI shows what needs you in amber, what is done in green and what was refused in red, and groups its help into secrets, agents and vault commands. `--json` gives machine-readable output from `ls`, `env ls`, `log`, `grants`, `token ls`, `share ls` and `mcp policy`. `set`, `get --reveal`, `mcp serve` and `mcp setup` join `add`, `get --show`, `agent` and `setup`. Audit lines now name the vault, the entries used and a run's command.

The desktop's Secrets screen no longer overlaps with an entry open: the search box, New, Rename or move and Delete run across the list and the entry's pane, and the dividers sit between the panes instead of over them. The main window can no longer be made narrower than 960 px, the least width at which the screen fits ([F.22](docs/steps/F.22.md)).

Closing the desktop's main window now ends the session even while an agent's request waits in its prompt window: the request is refused as `vault-locked` and the prompt closes. Before, the prompt kept the app running with the vault unlocked, and approving on it still released the value ([F.21](docs/steps/F.21.md)).

`keypaste run --session <project> -- <command>` now takes the project's variables from the desktop app or `keypaste agent` holding the vault unlocked, and asks for no password. The app shows the project, the variable names, the command and the directory in a prompt window, and `keypaste agent` shows them in its terminal. The command starts only on Allow once or Allow this command for 15 minutes (`o` or `h` in `keypaste agent`). Deny, a lock, the timeout or nothing holding the vault ends the run with a reason and starts nothing ([E.1c](docs/steps/E.1c.md)).

The desktop's Env profiles screen can now import a `.env` into a project, listing each variable as new, replacing a stored value or unchanged and writing only when you press Import. A project can be given a directory and a command on this machine: Run opens a terminal there running the command, and Open terminal opens one at a prompt, each with the project's variables from the unlocked app and only after a card naming the command, the directory and the variable names. A set with an expired entry or a bad name, a lock or Cancel starts nothing. Windows and Linux only ([E.1b](docs/steps/E.1b.md)).

`keypaste run` now refuses a project whose set holds an expired entry, as it already refused names that cannot be exported, and names every entry it refuses and why without printing a value. An untitled entry, which used to be skipped, now refuses the set too, and a duplicated name is listed with the others rather than reported alone. An entry in the recycle bin is still left out. The desktop's session can resolve a set the same way, reading the vault as saved and releasing nothing once it locks, ready for launching projects from the app ([E.1a](docs/steps/E.1a.md)).

The Agents screen can now connect Claude Code or Codex to the vault the desktop has unlocked. It shows the exact commands, with the vault, the label and the exposure, and runs them only when you press Run it. For Cursor and Claude Desktop it shows the block to paste. Check the connection asks once through the bridge the client will start, in the app's own prompt, and Remove takes keypaste out of the client again. The internal desktop packages now include `keypaste-mcp`, and `keypaste setup --dry-run` prints the same commands the app shows, quoting any argument with a space in it ([2.6a](docs/steps/2.6a.md)).

The Agents screen now lists the request waiting in the desktop's prompt and the grants in force, with the client, label, entry, field and time left, and Revoke or Revoke all makes the next request ask again. Below them is this session's audit history, which says when the log is unreadable or edited instead of looking empty ([4.3b](docs/steps/4.3b.md)).

The desktop now asks about an agent's credential request in its own prompt window, showing the client, its label, the entry, the field, the lifetime and the agent's reason, and releases only when you press Allow once or Allow for 1 hour, which work a second after the window appears. Deny, closing it, the timeout, locking and the client giving up each refuse. At the app and at `keypaste agent`, a request whose client cancels or whose `keypaste-mcp` goes away is now withdrawn at once. The app does not read `policy.toml` ([4.4](docs/steps/4.4.md)).

The desktop and `keypaste agent` now apply every limit on an agent's request themselves: entry and reason length, the field, a lifetime of 1 to 3600 seconds and the exposure. A process that talks to them without `keypaste-mcp` is refused the same way, and an out-of-range lifetime is refused rather than shortened. Agents using the bridge see no difference ([4.3a](docs/steps/4.3a.md)).

The Agents screen now says what agents meet as the app's session reports it, naming this app's process and session while it serves the vault. When `keypaste agent` already holds the vault, the unlock screen names it as the owner and leaves it running. A desktop app that crashed leaves nothing answering agents; relaunched, it starts locked and serves again once unlocked ([4.4b](docs/steps/4.4b.md)).

Agents now get the vault as it is saved. A password changed in the desktop is the next value an agent receives, and editing, moving, renaming or deleting an entry, or changing the vault's access, makes the next request for it ask again instead of reusing an earlier approval. When another program saves the vault file, the desktop and `keypaste agent` refuse every agent request as `vault-changed` until you lock and unlock, or restart the agent, and they never write over that save ([U.3](docs/steps/U.3.md)).

Locking is now one step for agents. Every desktop lock, and quitting, denies a request still waiting at the app as `vault-locked` in the audit log and clears the grants agents were given; so does stopping `keypaste agent` with Ctrl+C, SIGTERM or by closing its terminal. An agent's requests never keep the app unlocked, and one that arrives after the machine slept past the timeout locks the app and is refused. Values already delivered are not recalled ([U.2](docs/steps/U.2.md)).

The desktop now owns the vault it unlocks. A `keypaste-mcp` configured for that vault reaches the app, which answers `list_entry_names` and refuses every credential request until approving in the app exists, and each audit line names the unlocked session that answered. `keypaste agent` on a vault the app holds, or a second app on it, is refused naming the holder before any password is asked for. The bridge now needs `--vault`, and the bridge and the approver must both be this version ([U.1](docs/steps/U.1.md)).

FEATURES now lists which vault workflows work on a vault KeePassXC made, through the CLI and the desktop, what KeePassXC still reads afterwards and what is refused without touching the file. Restoring a history revision makes the entry that revision whole, attachments and custom fields included, as KeePassXC does ([9.4](docs/steps/9.4.md)).

`Ctrl/Cmd+O` on the desktop's unlock screen opens the vault picker, as Browse does ([F.15](docs/steps/F.15.md)).

The desktop entry pane reveals the current password while it is held, and a history revision now has Copy with the same twenty-second clear as every other secret. Masked fields are announced by their purpose to screen readers ([V.10](docs/steps/V.10.md)).

The desktop opens, creates and restores vaults that need a keyfile, and remembers where each vault's keyfile is, as KeePassXC does. Settings changes the master password and adds, replaces or removes a keyfile after asking for the current password. It says that existing backups still open with the old credentials, and the app stays unlocked afterwards ([V.1b](docs/steps/V.1b.md)).

`keypaste access` changes a vault's master password and attaches, replaces or removes an existing KeePass XML, 32-byte or 64-character hex keyfile. It never creates a keyfile and never removes a password, opens the new bytes with the new credentials before and after replacing the file, and keeps the replaced file as a backup; that copy and every earlier one still open with the old credentials, so delete them if those were exposed. The native-compiled CLI no longer reads an XML keyfile as the hash of the whole file ([V.1a2](docs/steps/V.1a2.md)).

Every command that opens a vault takes `--keyfile <path>` or `KEYPASTE_KEYFILE`, opening all four KeePass keyfile forms and a vault a keyfile alone protects. A vault keyed to an arbitrary file is named on stderr each time it opens, because editing that file loses the vault. A missing, empty or unreadable keyfile is refused before the password prompt. `keypaste setup` takes no keyfile, nothing records which keyfile a vault uses, and the desktop cannot open a keyfile vault yet ([V.1a1](docs/steps/V.1a1.md)).

The desktop app creates and renames groups, and renames and moves an entry in one write that keeps its identity and history; a refusal writes nothing and says why. The forms state when a rename changes which policy rule or agent exposure applies, and what renaming a project costs `keypaste run`. Search also covers usernames and URLs and never reads a password, a note or a protected field ([V.5b](docs/steps/V.5b.md)).

Core renames and moves groups and entries, keeping identity, history and data keypaste does not model; renaming the group `env/billing` renames the project. `env` at the root and the recycle bin are reserved names. An organized vault stays KDBX 4.0, and restoring a revision no longer renames the entry back ([V.5a](docs/steps/V.5a.md)).

The desktop's locked unlock screen restores a backup, opened with the master password it was made under, over a vault that is healthy, damaged or missing, keeping the file it replaces. A restored vault opens with the backup's password. Settings exports an exact encrypted copy to a new file ([V.4b](docs/steps/V.4b.md)).

A save over an existing vault first copies it into `<vault>.backups` beside it, keeping five, once per unlock and at most every fifteen minutes. A save whose copy cannot be written does not happen, and nothing turns that off. Each copy opens with the master password it was made under and is another offline guessing target; five copies beside the file do not survive losing the disk ([V.4a](docs/steps/V.4a.md)).

The desktop's Trash screen restores one recycled entry, or erases one behind its own confirmation, and Delete offers an immediate Restore; emptying the whole bin stays in KeePassXC ([V.3b](docs/steps/V.3b.md)).

`keypaste rm` and the desktop's Delete move an entry to the vault's KDBX recycle bin with its fields and history, unless the vault's recycle bin is switched off; a recycled entry is hidden from listing, `keypaste run` and agents. A vault that has recycled anything is written as KDBX 4.1, which KeePassXC 2.7 and KeePass 2.48 and later read ([V.3a](docs/steps/V.3a.md)).

Passphrases: `--generate --words N` on `add` and `env set`, the desktop's Generate boxes, and `keypaste generate --words N`, which prints one to stdout and stores nothing. Words come from EFF's long list, pinned by SHA-256; six words, about 78 bits, is the minimum. The separator defaults to `.` and a hyphen is refused, because four list words contain one.

The desktop shows an entry's history and restores a revision; the replaced value stays in history, so a restore can be undone by another.

The desktop stores an existing secret typed or pasted into a masked field. Paste drops one trailing line break and refuses anything a keyboard could not type; the master-password fields still ignore paste.

The entry pane shows usernames, URLs and notes with their punctuation and line breaks intact, while still replacing bidi overrides, zero-width characters and other text that can misrepresent itself.

The desktop creates vaults under the same rules as `keypaste init`, whose prompts, messages and exit codes are unchanged.

The release workflow can publish the Windows MSI and Linux AppImage beside the CLI at the same version prefix, each with its own manifest and attestation, and refuses them while they are unsigned.

## 0.3.0

Upgrade from `v0.2.0` to receive the save and approval-bridge repairs below. Two of them preserve data that `v0.2.0` can lose or refuse to write. Old archives remain available and immutable.

Saves re-read the vault between retries and refuse to overwrite a change made by another program while waiting. The earlier behavior, present since `0.1.0`, could replace that change with an older copy without retaining it in history. Reload the vault after a refusal.

On Windows 11 24H2 and Windows Server 2025, temporary files now use a private process directory. Shared 8.3 temporary-name aliases previously allowed unrelated Keypaste or KeePass saves to exhaust the retry budget, after which the save reported failure without committing. Older Windows builds were unaffected.

The bridge now tries the approver connection before reporting that `keypaste agent` is absent. Previously, worker-pool contention could exhaust the deadline before the first attempt, so a listing on a loaded machine could be told to start an approver that was already running. The connection budget is unchanged.

Saving a vault file that does not exist yet no longer waits behind other saves, and a save sleeping between retries no longer holds the in-process lock that orders them. Only an attempt that can transact takes it. A save that queued behind another save committing in the same process is now refused as changed on disk rather than overwriting it.

Windows saves now retry transient rename collisions. A failed save's stranded `vault.kdbx.tmp` is removed only when it can be opened exclusively.

Releases now publish a manifest and a Sigstore build attestation covering every asset. `gh attestation verify` with the published bundle confirms, without a GitHub account, that an asset was built by this repository's release workflow for its tag; SECURITY.md has the procedure. Attestation authenticates origin; the binaries remain unsigned and builds are not reproducible.

Fixed a race in the KeePassLib KDF registry that could throw or corrupt the engine list when several vaults were created concurrently on first use. KeePassInterop now forces registry initialization from its type initializer. Not reachable from the CLI, agent or desktop, which each open their first vault on a single thread.

In the desktop source, which this release does not publish, restoring the window no longer postpones the idle lock when the pointer has not moved, and input arriving after the idle deadline locks the vault instead of extending it. On Windows a restore delivers a pointer move at the resting cursor, which previously counted as somebody being there.

## 0.3.1-rc.2

Unadvertised candidate whose CLI/MCP behavior matches `0.3.0`. It exists to install the internal, unsigned Windows MSI and Linux AppImage on fresh runners and exercise the installed app, and it is the first tag through the Windows signing steps, which sign nothing while signing is not enabled. The desktop packages stay workflow artifacts and are not published.

## 0.3.1-rc.1

Unadvertised candidate whose CLI/MCP behavior matches `0.3.0`. It exists to run the desktop packaging tag path, which now also builds an internal, unsigned Linux AppImage kept as a workflow artifact and not published.

## 0.2.1-rc.3

Unadvertised candidate whose CLI/MCP behavior matches `0.3.0` and `0.2.1-rc.2`. It exists to run the desktop packaging tag path, which now builds an internal, unsigned per-user Windows MSI kept as a workflow artifact and not published.

## 0.2.1-rc.2

Unadvertised candidate whose CLI/MCP behavior matches `0.3.0`. It is the first release published with a manifest and build attestation, and exists to verify them against public bytes before an advertised release relies on them. The `v0.2.1-rc.1` tag published nothing: its release guard stopped before any build.

## 0.2.0

Upgrade from `v0.1.0` to receive the data-preservation and approval repairs below. Old archives remain available and immutable.

This release uses the source verified by `v0.2.0-rc.1`. The unadvertised candidate was downloaded anonymously, checked against recorded asset hashes, and installed on each advertised target to exercise vault creation and environment injection.

MCP requests arriving immediately after initialization now wait for the pending identity handshake. This fixes a macOS race that refused clients which had already sent initialization. Clients that never initialize remain refused.

Download pages now state OS floors and their evidence: glibc 2.35, macOS 13 and Windows 10 1809. Linux x64 is checked on Debian 12 with an Alpine rejection control. The macOS and Windows values are cited .NET 10 floors without a Keypaste installation on those minimum versions; Linux ARM64 has no equivalent container check.

An approved field too large for one response now returns an explicit delivery refusal and audit result while retaining the connection and other grants. The secret is never truncated. Repeated requests use the recorded outcome without prompting again; large values require manual copying.

Large entry listings now fit the response budget and disclose when names were omitted. They expose the same authorized subtree, offer no paging operation, and preserve cached approvals. Concurrent requests on one connection are refused immediately with `BUSY` while an approval is pending, including listing requests. Busy responses describe the possible human wait without identifying the blocking request, and each refusal is audited.

Approval expiry now uses both wall and monotonic clocks, preventing rollback or suspension from extending access. Denial cooldowns also resist clock changes. Previously, moving the wall clock could revive an expired grant or shorten a refusal's cooldown.

Ambiguous entry paths now fail across CLI reads, desktop copy and edits. Group and title identify an entry separately, so slash-containing titles cannot redirect a read or write. `env rm` and `env set` receive the same protection. Duplicate identities are refused without rewriting the vault; listings retain duplicates, and removals with no match return exit 3. `add` permits a distinct identity that creates a path collision, reports that ambiguity and refuses further additions through an already ambiguous path.

`env pull` preserves a source changed during prompting or replaced before deletion. It records path and content, then moves and verifies the imported file during removal. Missing sources no longer report successful deletion. `env export` refuses to overwrite its source vault or any other KeePass vault, including with `--force`; checks resolve symbolic links, junctions and ancestor links.

Approval reasons retain ordinary path separators while hostile markup remains sanitized. Audit records use the resolved entry path instead of opaque handles.

`keypaste setup` configures Claude Code and Codex through their `mcp add` commands and prints configuration for Cursor and Claude Desktop. `--dry-run` previews commands, `--remove` removes only Keypaste, and repeated setup updates the configuration idempotently.

The pre-publication review added regressions for hostile-name rendering, scrubbed approval text and exception-path auditing. Sanitization now covers CLI listings, import refusals and desktop display while preserving exact values for addressing, editing and copying. The prompt marks scrubbed entries or reasons. Vault failures are audited, and an accept failure ends the approver instead of leaving its vault unlocked.

Desktop source changes include accessibility checks for the master-password mask, which exposes only placeholder and length. Paste and a screen-reader name remain missing. Clipboard writes pending at lock, quit or Clear now are cleared when they complete; quitting waits for them, and an unreadable clipboard no longer prevents scheduled clearing. Non-secret run commands remain on the clipboard.

Minimize-lock now uses the normal lock path and responds to setting changes immediately. It was observed on Windows; macOS and Linux still require manual observation. Focus loss and macOS Cmd+H are separate events. Startup now applies stored idle and theme settings before rendering, honors custom timeout values, and preserves unreadable settings files while retaining safe defaults.

First-party binaries now report the Keypaste publisher and copyright explicitly. The bundled KeePassLib carries its upstream attribution. Published `v0.1.0` metadata remains unchanged.

## 0.2.0-rc.1

Unadvertised candidate using the same source as `0.2.0`. Its published URL exercised tag-only behavior, including desktop prerelease versions, release matrices and publication. Its changes are listed under `0.2.0`.

## 0.1.0

The first advertised release uses the same behavior as `0.1.0-rc.1`. The pipeline completed, a public asset was downloaded and checked manually, and install pages moved to this version. The candidate remains at its unadvertised URL.

This version provides a local KDBX4 vault, environment injection without an environment file, and an MCP bridge for human-approved access to one field for a stated lifetime. Calls enter a local hash-chained audit log. Vault and bridge use requires no account or hosted service.

Binaries are unsigned and un-notarized; checksums do not authenticate their publisher. Approval uses the terminal, and the desktop is source-only. Linux requires glibc 2.35; musl is unsupported by these binaries. Intel Macs and Windows ARM64 use source builds. [THREATS.md](THREATS.md) T-21 describes download trust.

## 0.1.0-rc.1

The first published tag exercised the release pipeline at an unadvertised URL.

NativeAOT binaries cover `linux-x64`, `linux-arm64`, `osx-arm64` and `win-x64` without requiring a .NET runtime. The recorded binaries were about 10 MB each and started in a little under half the framework-dependent build's time. Intel macOS has no published target; [runner availability](https://docs.github.com/en/actions/reference/runners/github-hosted-runners) does not replace the project's missing build and verification work.

Release checks run against the binaries being uploaded, covering real-process approval and refusal, MCP pipes, audit tampering, injection, demo transcripts and KeePassXC compatibility on all four targets.

Linux binaries require glibc 2.35 or newer. Linux x64 is checked on Debian 12; ARM64 has no equivalent container check. Alpine and other musl distributions are unsupported. Vault format, approval behavior and audit format were unchanged.
