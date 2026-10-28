# Changelog

Published versions are available at `https://dl.keypaste.com/v<version>/` with checksums and corresponding source. CLI/MCP binaries are unsigned and un-notarized; the desktop has no public release. [RELEASE.md](docs/RELEASE.md) records platform support and verification requirements. The release workflow requires a section matching each tag.

## Unreleased

`keypaste import <file>` now also copies KeePassXC's CSV export into your vault, under a group named after the file, each item in its KeePassXC group below the root, with its username, password, URL, notes and TOTP, which lands in KeePassXC's `otp` field, and when it was created and last changed. The items KeePassXC exports from its recycle bin are left behind, as a KeePass file's import leaves them, and named as not imported, as is an icon other than the key. The CSV leaves out custom fields, attachments, history, tags and expiry, which importing the `.kdbx` itself keeps ([9.1e](docs/steps/9.1e.md)).

`keypaste import <file>` now also copies 1Password's CSV export into your vault: 1Password 8's, of its Login and Password items, or an earlier version's common fields exported with column labels. Each item lands under a group named after the file with its username, password, web address, notes and TOTP, which lands in KeePassXC's `otp` field, and its tags; a favorite gets the tag `Favorite` and an archived item `Archived`, as KeePassXC's 1Password import gives them, and an earlier version's item type becomes a tag. 1Password's CSV leaves out vaults, folders, other item types, custom fields and attachments ([9.1d2](docs/steps/9.1d2.md)).

`keypaste import <file>` now copies LastPass's CSV export, from LastPass's vault or `lpass export`, into your vault, all of it or nothing, under a group named after the file and each item in its LastPass folder: a site with its web address, notes and TOTP, which lands in KeePassXC's `otp` field, a secure note as its notes, and a typed note, such as a card, a server or an SSH key, with each value a protected field and its type a tag. A favorite gets the tag `Favorite`, as KeePassXC's importers give one. `--dry-run` shows where each item lands and names everything that does not, with why, and `--skip-existing` skips an item your vault already has by name. LastPass's export carries no attachments ([9.1c](docs/steps/9.1c.md)).

`keypaste import` now copies in Bitwarden's `.json` export, of your own vault or an organization's, under a group named after the file, each item in its folder or first collection: a login's username, password, web addresses, TOTP and passkey, in the fields KeePassXC reads them from, and the details of cards, identities, SSH keys, bank accounts, driver's licenses and passports as fields, protected where Bitwarden hides them, with earlier passwords in history, favorites tagged `Favorite` and archived items `Archived`. `--dry-run` shows where each item would land, and both say what is not copied and why, such as a second passkey on one item, linked fields and the attachments the export leaves out; `--skip-existing` leaves out each item your vault already has by name. An encrypted or password-protected export is refused, saying how to export one keypaste reads ([9.1b](docs/steps/9.1b.md)).

The app's Import KeePass database dialog now opens a file that also needs a YubiKey: Use a YubiKey asks slot 2 unless you choose slot 1, the dialog says Touch your YubiKey while the key waits, with a Cancel, and names a missing key, an empty slot and a touch that never came. Keeping the file in place hands its slot to the unlock screen. `keypaste import` still cannot ask a hardware key ([P.1a](docs/steps/P.1a.md)).

On Windows, the app now unlocks a vault again with Windows Hello after you lock it, it locks when idle or minimized, or you close it into the tray, as KeePassXC 2.8 does. After each unlock with the master password, Windows Hello asks to keep it, and the unlock screen then offers Unlock with Windows Hello above the master password, which stays. `keypaste lock`, opening another vault, a changed password or keyfile and quitting still need the master password, nothing is written to disk, and a vault that needs a YubiKey asks for its touch again ([4.10a](docs/steps/4.10a.md)).

On Windows, an agent asking just after another while the app was open, locked and busy could be told that no keypaste app was open; the app now keeps its pipe ready for the next agent. A bridge whose connection to the app ends before it has checked who made the pipe now tells its agent that no app was found, rather than failing the request ([F.70](docs/steps/F.70.md)).

An item in the app now lists its attachments by name and size, with Add attachment, which attaches a file of up to 5 MiB under the file's name, and Replace, Remove and Save on each one. Save writes a copy, unencrypted, only to a new file you choose and never over one, and the app never opens or runs an attachment. When the vault's history size limit means history cannot keep the item as it was, the item says what is gone ([V.8b](docs/steps/V.8b.md)).

`keypaste attachment` lists an item's attachments by name and size, attaches a file of up to 5 MiB, replaces or removes one, keeping what it held in the item's history, and saves one to a new file you name; KeePassXC reads every change, and the attachments you do not touch stay byte for byte. While the app or `keypaste approve` holds the vault, adding, replacing and removing are refused ([V.8a](docs/steps/V.8a.md)).

A value holding a `{S:…}` whose name holds braces, such as `{S:a{PASSWORD}b}`, is now refused and named, as `{REF:…}` is: KeePassXC reads it as one field named `a{PASSWORD}b`, and keypaste releases no field so named, where it had filled in the `{PASSWORD}` inside the name ([F.69](docs/steps/F.69.md)).

The idle warning now gives a screen reader its first line, "Locking in 30 seconds.", once per warning, as an earlier entry said it did; Avalonia had named it by its ticking text, a new name each second ([F.68](docs/steps/F.68.md)).

A value with a KeePass placeholder or escape inside braces that name no placeholder, such as `{x{PASSWORD}y}` or `{\{PASSWORD\}}`, is now refused and named, as `{REF:…}` is, because KeePassXC 2.7.11 fills in that inside and 2.8 does not ([F.68](docs/steps/F.68.md)).

On Linux, the first run now also offers the databases KeePassXC 2.8 last opened, which it keeps in `~/.local/state/keepassxc` rather than `~/.cache/keepassxc` ([F.68](docs/steps/F.68.md)).

`keypaste doctor` now says which Argon2 derives your vault keys: the `argon2.dll` or `libargon2.1.dylib` beside `keypaste` on Windows and macOS, or the system's libargon2 on Linux. When the file is missing or does not work, or Linux has none, it says that unlocking and saving take longer and what to do ([F.67](docs/steps/F.67.md)).

A screen reader now names each revision in an item's History by when it was current and its title, rather than by an internal type name ([R.1a](docs/steps/R.1a.md)).

`keypaste approve`'s credential prompt now names the agent with its bridge's `--client-label` in brackets, as its run prompt and the app's prompt do, so you can tell which configured connection is asking.

`keypaste mcp` now withdraws a request still waiting for your answer when its client closes the bridge's input, as when the client cancels it, so the prompt comes down rather than staying up until it times out; it also stops waiting for the app or `keypaste approve` after 70 seconds, 125 for a credential request, and it never sends a credential request a second time after losing the reply, so you are not asked twice about one you already answered.

A request for more access or a terminal's edit that Reload took off the screen no longer holds the next one back for a minute, as one nobody answered does: it can be asked again at once, as after a lock.

A change to an agent's settings is now saved only in the unlock it was asked in, never in the vault unlocked after a lock, and a lock or Reload just as its confirmation is about to appear keeps the window from appearing.

A timed allow given in the prompt while End all, `keypaste grants revoke --all`, `keypaste grants revoke <agent>`, a stricter setting for an agent in the app or an edit ended allowances now releases that one request and keeps nothing, so the agent's next request is asked about again rather than served under an allowance given before the end; History records it as allowed once. One answered as Reload ends them keeps nothing either.
Locking the app with Lock, `Ctrl/Cmd+L`, the tray or menu bar or `keypaste lock`, or quitting it with a vault open, no longer leaves its window unresponsive while keypaste finishes answering agents. A change a terminal asked the app to save that is still waiting behind another save when the vault locks is now refused as locked and writes nothing, rather than holding up the lock.

The unlock screen's line naming the program that holds a vault, such as `keypaste approve`, now goes within a few seconds of that program letting the vault go, without anything typed or pressed.

The notice that an agent asked for a vault while the app was locked now opens at the top-right of the screen, where it doesn't cover the unlock form.
The tag box on an item and in New item now suggests the vault's project tags as you type, such as `env:billing:prod`, leaving out the ones the item already has. Choosing one only fills the box, and on an item Add tag still says what a project tag reaches before it writes anything.

An item's preview in the app now shows its expiry, set in KeePassXC, beside Created, and reads Expired in red once it has passed, so you see why a run refuses it before you start one.

A value wrapped whole in `\{` and `\}`, KeePassXC's escape for text shaped like a placeholder, now leaves keypaste as KeePassXC 2.7.11 gives it: `\{PASSWORD\}` becomes `{PASSWORD}` with nothing filled in, where keypaste passed the backslashes on or, for one such as `\{S:API_KEY\}`, refused the value.
Settings › Recommendations now checks the vault on a background thread after each unlock and save, rather than on the window's own, and updates its list and the count beside Settings when the check finishes.

After Import .env on the Projects screen, the app now offers to delete the file, as `keypaste env pull` does, saying that deleting does not overwrite the disk blocks it used and, for a file inside a git repository, does not remove it from the repository's history. The file stays unless you press Delete, and a file that changed after keypaste read it is kept.

When no agent has received an item, holds an allowance for it or is asking for it, the item's Agent access card now names the connected agents that can see it, such as "claude-code can see it · nothing released", rather than None active.
Saving Settings in the app, choosing a vault with `keypaste use`, and saving a project's directory and command now keep what a newer version of keypaste wrote into `app.toml` and `projects.json`, rather than dropping every key, section or project mapping this version does not read.

keypaste now opens and saves a vault encrypted with Twofish, as KeePassXC can make one, keeping Twofish when it saves; before, such a vault did not open at all. keypaste still makes new vaults with AES-256 and never changes a vault's cipher. The Twofish code is BouncyCastle's, vendored and pinned as [third_party/bouncycastle-twofish/UPSTREAM.md](third_party/bouncycastle-twofish/UPSTREAM.md) records.
Unlocking a vault and saving it in the app are faster on Windows and macOS, by about 2.3 times on the test runners: the app now derives the vault's key with the Argon2 reference library, which it carries as `argon2.dll` or `libargon2.1.dylib` beside its binary, rather than with slower .NET code. `keypaste` carries the same file and derives with it too; keep the file beside `keypaste` when you move it out of the archive, since without it the key is the same and takes longer. Linux uses the system's libargon2 as before.

Tagging an item into a project, in the app or with `keypaste env tag`, now names each of its variables that holds a KeePass placeholder such as `{PASSWORD}`, with the placeholder, before anything is written, because that variable carries the item's own field to whoever gets it.

In the app's History, the caption under Restore this now says a restore drops the attachments, custom fields and custom data added since that revision. When the revision's project tags differ from the item's, Restore this first says which environments the item leaves and joins, as adding a project tag does, and restores only when you confirm.

`keypaste env export --dotenv` now refuses an environment holding an expired item, naming it and writing nothing, as `keypaste run` does.

The run command the Projects screen copies now carries `--session`, so pasting it in a terminal asks the app for the variables rather than for the master password, and nothing starts while the app is locked.
Trash in the app now has Empty Trash, which erases everything in the vault's recycle bin, with its history, once you confirm a second time; the confirmation says how many items go, and the button is greyed out while Trash is empty. The `keypaste` CLI has no command for it.

When restoring an item from Trash finds another item with its name, the app now offers Restore as `<title> (restored)`, or `(restored 2)` and onwards when that is taken too, which puts the item back with its history under that title. Nothing changes until you choose it, and the other item keeps its name.

The import dialog's password field now takes `Ctrl/Cmd+V` too, as the master-password fields do: one trailing line break is dropped and anything a keyboard can't type is refused with a message.
When `keypaste approve` or a command such as `keypaste get` cannot open a vault the app last opened with a YubiKey, it now says that such a vault opens only in the desktop app, and that `keypaste run --session` and edits such as `keypaste set` reach it there while the app holds it, rather than only that the master password is wrong.

Search now also finds an item by any part of one of its own tags, so `prod` finds an item tagged `env:billing:prod`, in the app's search, which names the tag beside the item, and in `keypaste search`, which names the tag it matched on the item's line and in `--json`.

`keypaste field mv <item> <name> <new-name>` renames a custom field, keeping its value and its protection, and the old name stays in the item's history. It refuses a name the item already has in any case, a standard name, KeePassXC's own attributes and the fields a TOTP is read from, and is refused while the app or `keypaste approve` holds the vault, naming the holder.

In the app, Rename on a custom field's row does the same. For a field a project reads as a variable, or one the item's own `{S:…}` names, it first lists the environments that stop giving the variable, the `kp://` references and the fields the rename leaves naming nothing, and renames only when you press Rename again.

In the app, right-clicking a group in the sidebar, or pressing the menu key or Shift+F10 on it, opens a menu with New group, Rename, Move and Delete. Move takes the group and everything in it into another group. Delete asks first, saying how many items go with it, and puts the group in the vault's recycle bin, where Trash lists each of its items and puts each back at the path it had; in a vault whose recycle bin is switched off, only an empty group can be deleted, for good. The sidebar now lists groups in the order the vault holds them, as KeePassXC does.

Duplicate in an item's ⋯ menu makes a copy beside it, titled `<title> (copy)`, with its fields and attachments but none of its history, and selects it. The copy leaves out the item's project tags, because a project takes one item for each variable, and the app says which tags it left out.

Items' list now shows each item's username and web address beside its title, as KeePassXC's table does. A password, a note or a custom field's value is never a column.

An item in a project now shows an Environments card in its preview, listing each environment its tags put it in and saying approval required where no agent gets the item without your answer.

History in the app now compares a revision's custom fields, each marked protected or not, and its tags with the item's own, and never shows a field's value.

When another program saves the vault while a change of yours is unsaved, the notice now names the items that change touched, never a value, so you know what Reload discards.
You can now paste a master password with `Ctrl/Cmd+V` on the unlock screen, the create and restore forms and in Settings' password change, so a password kept in another manager needs no retyping. One trailing line break is dropped and anything a keyboard can't type is refused with a message, as in the other secret fields. keypaste doesn't clear a master password you copied, so copy something else afterwards.

When another program such as KeePassXC or a sync tool saves the open vault, the app now says so with Reload almost at once rather than up to five seconds later, by watching the vault's folder; where the folder can't be watched it still checks every five seconds.

When an agent's request finds the app's audit log won't open, the Agents screen now says that keypaste refuses every agent request until it does, with the reason, and the tooltip on the sidebar's Agents row says Refusing agents. Fixing the log file clears both at the next request; unlocking alone still creates no log.

While the app refuses every agent request because its audit log won't open, the dot on the sidebar's Agents row is now red instead of green.

Reload in the app now also withdraws every agent request still waiting for your answer, as locking does, so pressing Allow on a prompt from before the reload releases nothing from what the reload read in, and the agent can ask again ([N.16](docs/steps/N.16.md)).

The prompts for an agent's run and for `keypaste run --session` now show the environment on a row of its own beneath the heading, with its whole name, so a long one is never cut short ([N.6](docs/steps/N.6.md)).

On an agent's settings panel, a choice made from the keyboard keeps the keyboard on its chip once the panel shows the change, or on the first chip of its group when that choice is no longer offered, so Tab and Space carry on from where you were.

A toast in the app no longer blocks what it covers: a click where it is drawn reaches the button under it, such as Run in a terminal at the foot of a project. The clipboard's toast still takes a press on its Clear now.

The app's idle warning now counts down: "Locking in 30 seconds." drops by one each second until the lock, any key, click or pointer move still takes it away, and the name it gives a screen reader is its first line, set once per warning rather than every second.
On Windows and Linux, Run in a terminal and Open terminal on the Projects screen now write a History line too, the same one a macOS Run writes, naming the project, environment and items but no value, so each Run also shows in its items' activity. As on macOS, a Run is refused before its confirmation while the app cannot open its audit log, or when its variables come from more items than one History line can name, and the refusal gives only that reason, since the copied run command meets the same refusal.

Settings › Recommendations now calls a password weak when it is one of the 10,000 most common passwords, or made of them, however long it is, checked against a list built into keypaste with no network request. Each password it lists has Open item, which shows that item selected in Items, even after you renamed it. Custom fields are still not rated, since they mostly hold API keys a password-strength estimate misjudges.

`keypaste get <item> totp` gives an item's current TOTP code, on the clipboard with the usual clearing countdown or printed with `--reveal`, and never its seed; `keypaste show` lists `totp` for such an item without a value and says how to get the code. A TOTP code you hold on screen in the app now changes to the next code when its period ends, rather than showing the old one until you let go.

keypaste now reads the TOTP of an item KeePassXC set up in its older way, a `TOTP Seed` with `TOTP Settings` such as `30;6` or `30;S` for Steam, and of one holding KeeOTP's `key=…` form, in the order KeePassXC reads them, and still writes none of them. An `otp` link is now read exactly as KeePassXC reads it, so a parameter such as `Digits` in another case is ignored as KeePassXC ignores it; where KeePassXC would compute a code from a link keypaste cannot read as written, such as Steam without `digits=5`, keypaste says which field to correct rather than showing a different code.

Each value on the app's Projects screen now has a Reveal button beside Copy, and a new scoped token has one beside it, so holding Space or Enter on the button shows the value until you let go, as the Items pane's Reveal already did.
Settings › Advanced is now a screen of its own. One Advanced row on Settings opens it, and it leads to the activity log, share links, scoped tokens and diagnostics; Back on each of those returns to Advanced, and Back on Advanced to Settings ([N.1b](docs/steps/N.1b.md)).
The app can now make a keyfile for you: Create a key file on the create form, and Create keyfile… in Settings when changing a vault's keyfile, write a new KeePass XML keyfile where you choose, never over a file already there, and remind you to keep a copy somewhere other than this computer, saying so too when you saved it in the vault's folder. KeePassXC opens a vault protected by it. The `keypaste` CLI still makes no keyfile ([V.1a2](docs/steps/V.1a2.md)).

The app's create form can now protect a new vault with a YubiKey as well as its master password: Add a YubiKey chooses the slot, Create vault asks the key once, and a key that is missing, has no such slot or is not touched in time is named with no vault written ([Creating a vault](docs/desktop.md#creating-a-vault)).

On macOS, keypaste's menu bar icon is now a monochrome image that follows the menu bar's light or dark look, and the Dock icon shows only while the window is open: closing the window into the menu bar takes it away, and Open keypaste, or a Dock click while it shows, brings the window back. Open keypaste at login now reads as off while macOS lists keypaste's login agent as disabled, as `launchctl print-disabled` shows, and turning it on in Settings enables the agent again ([G.4a](docs/steps/G.4a.md)).

Uninstalling keypaste on Windows now also removes Open keypaste at login, so Windows no longer tries to start an app that is gone; upgrading to a newer version keeps it on ([G.4a](docs/steps/G.4a.md)).

With no recent vault, the app's first screen now also lists the databases a portable KeePassXC opened, while that KeePassXC is running: keypaste finds it through the running program's own folder and reads its `config\keepassxc_local.ini`, never searching your disks and never writing the file ([N.2](docs/steps/N.2.md)).

An item with a TOTP now shows its code in the app, masked until you hold Reveal, with the seconds until the next code beside it, and Copy puts the code on the clipboard with the usual clearing countdown. An agent can ask for an item's current code with `request_credential` and `field` `totp`, which `list_entry_names` offers for items that have one: you allow each code once, with no timed allowance since the code changes, or the agent's Automatic mode releases it, and the log records a `totp` release. No agent, `kp://` reference or share is ever given the seed ([9.2b](docs/steps/9.2b.md)).

`keypaste` on its own in a terminal now shows where things stand: its version, the vault it uses and what chose it, whether the keypaste app or `keypaste approve` holds it unlocked, which AI tools are connected, and one next step, such as `keypaste setup` or opening the app. It asks for no password, starts nothing and changes nothing. In a script or a pipe, `keypaste` alone still prints the help on stderr and exits 1 ([N.8](docs/steps/N.8.md)).
`keypaste setup --link` prints a link for Cursor and one for VS Code that has the tool add keypaste itself, holding the same command and flags `setup` would write to its file, and changes nothing. `keypaste setup` also prints a tool's link when it cannot write that tool's file, and the app's Connect pane for Cursor and VS Code shows the link beside its plan, with Open in Cursor or Open in VS Code and Copy link. A link names the paths `setup` would write, your vault's among them, but never a value ([G.6e](docs/steps/G.6e.md)).

`keypaste agent` is now `keypaste approve`, and `keypaste mcp policy` is now `keypaste clients`, each with the same options; `keypaste mcp serve` and `keypaste mcp setup` are gone in favour of `keypaste approve` and `keypaste setup`, and `keypaste get --show` in favour of `--reveal`. An old spelling runs nothing: it exits 1 with one line naming its replacement, so a script that uses one needs updating. `keypaste mcp` is now only the bridge your AI tools start. A change made through `keypaste clients` is recorded in the log under that name, and earlier lines keep `keypaste mcp policy` ([N.9](docs/steps/N.9.md)).

`keypaste doctor` checks, reading only, that the copies of keypaste are one version, the chosen vault is a KeePass database it can read, each AI tool's registration starts keypaste and survives an upgrade, the app or `keypaste approve` answers for the vault, keypaste's settings files read cleanly and `keypaste` is on PATH, and gives a fix for each problem. It opens no vault, asks for no password, changes nothing and starts no AI tool: it runs only the `keypaste` each tool names, with `--version` and `mcp --help`, and the one on PATH with `--version`. It exits 7 when something needs fixing, and `--json` gives one object per finding ([G.3](docs/steps/G.3.md)).
A new guide, [Running agents safely](docs/safe-agents.md), says how to set Claude Code, Codex and Cursor on macOS, Linux and Windows so the commands their agent runs cannot write `~/.keypaste`, the tool's own MCP configuration, the vault or the `keypaste` the tool starts, while the bridge keeps the access it needs, and what each tool's documentation leaves unsaid and keypaste still cannot stop ([L.3](docs/steps/L.3.md)).
Settings › Recommendations now also lists an item whose password is weak or used on another item, and an item whose expiry date has passed, checked on this machine when you unlock and after each save, with no network request. Each row names the item and the problem, never the password, and Dismiss remembers your choice on this machine; an item you excluded from KeePassXC's reports is not listed ([V.9](docs/steps/V.9.md)).

When an agent asks while the desktop app is open but locked, the app now shows one notice naming the vault, without taking the keyboard: Close dismisses it, and Open keypaste, which answers after a second, puts that vault on the unlock screen. The request is still refused, and the agent is told the app is open but locked and to ask you to unlock it. The app listens for the vault on its unlock screen and for the chosen vault, including one chosen with `keypaste use` while it is locked, and shows one notice per lock. An approver name starting `keypaste-locked-` is now refused ([G.4b](docs/steps/G.4b.md)).

A value can now carry its own item's fields through KeePass placeholders: `{TITLE}`, `{USERNAME}`, `{PASSWORD}`, `{URL}`, `{NOTES}` and `{S:<field>}`, in any case, fill in from that item wherever the value leaves keypaste, so `DATABASE_URL=postgres://{USERNAME}:{PASSWORD}@db` needs no copy of the password. That covers a credential an agent receives, `keypaste run` in every form and the MCP `run` tool, tokens, the app's Run, `kp://` lines, `get`, `share`, `env export --dotenv`, and the app's copy buttons and its opening of a web address; `show --reveal`, the app's reveal and its edit forms still show the value as stored. `{S:<field>}` reads only a field that can leave by name. A value is refused whole, naming the placeholder and never a value, when it holds any other placeholder such as `{REF:…}` or `{TOTP}`, names a field keypaste never releases or one the item lacks, leads back to itself or nests more than 10 deep; an agent asking for such a value is told so, and that field cools down for a minute. The MCP `run` tool also scrubs each filled-in part from a command's output, and a terminal edit's prompt names any placeholder its value plants. The Agent Credential Access Format is now version 1.1, at keypaste.com/acaf/v1.1/, which allows this ([P.3b1](docs/steps/P.3b1.md)).

`keypaste search <text>` finds the items whose title, group, username or URL holds the text and lists each by path, saying when it matched in the username or URL but never printing either; passwords, notes and custom fields are not searched. `keypaste show <item>` lists an item's tags and fields, each custom field with its protection, and masks every value, the username and URL included, until you add `--reveal`. Both take `--json` ([N.13](docs/steps/N.13.md)).

The app, `keypaste`'s help and messages, the terminal's approval prompts and what agents are told now say item, environment, allowance, what an agent can see, KeePass database, agent and AI tool where they said entry, profile, grant, exposure, KDBX and client, say while the app or the vault is unlocked where they said session, and no longer mention stdio. A credential prompt's rows read `agent` and `item`, and `keypaste log`'s table heads its columns the same way while its rows keep the words the log records. The app's prompt for an agent's run names the environment before the directory, so a long directory no longer hides it. Commands, flags, file names, MCP tool and argument names, `--json` keys and audit records keep their spellings ([N.6](docs/steps/N.6.md)).

The app's Agents screen now lists the AI tools found on this computer, looked for each time it opens: a tool whose settings hold keypaste says Connected, and any other has one Connect, which shows exactly what it will change and changes it only when you press Run it. Every agent that asked in the last 30 days or has settings has its own row, with its settings under Settings on that row, and Every other agent comes last; a connected tool's Connect again and Disconnect are there too. Connect no longer asks for a label or what the agent can see, which its settings now decide; `keypaste setup --label` and `--expose` keep both. A request waiting for you is listed without its reason, and Show the prompt brings its window forward with Deny focused and its Allow buttons held back for a second; it is still answered only in that window. Allowances show their time left with End and End all, and the sidebar counts a settings change waiting for your confirmation ([N.3](docs/steps/N.3.md)).

On macOS, Run in a terminal and Open terminal on the Projects screen now open Terminal in the project's directory with its variables, after the same confirmation as on Windows and Linux. Terminal is handed no value and none of the app's `KEYPASTE_` settings: its window runs a launcher inside the app, whose `keypaste run --session` takes the set from the unlocked app without asking again, and a window that asks more than two minutes after Start, after a lock, or after the project's variables changed starts nothing, says why and waits for Return, so the reason stays on screen even when Terminal closes finished windows. Each Run the app releases on macOS appears in History, naming the project, environment and items but no value. A copy of keypaste not running as `keypaste.app` still offers only the copied run command. On every platform the copied run command now quotes a project's name for the platform's shells, and on Windows a project whose name holds `$`, a backtick, `!`, `%` or a double quote gets no command to copy, the screen saying why. `keypaste run` also takes its project as `--project <name>`, which accepts a name starting with `-`, and `run --session` and `--token` now carry a set of up to 8 MiB, its History line naming up to about a thousand items; once such a line passes 16 KiB, an earlier keypaste reports the log's chain as broken and refuses to add to it until a newer one writes the next line. `keypaste run --token` refuses a set whose items one History line cannot name before releasing anything, says so, and History records the refusal ([E.1d](docs/steps/E.1d.md)).

Each agent now has settings, on its card in the app's Agents screen and through `keypaste clients`, that say what it can see, whether it is given values, whether it may run commands and how its requests are answered: Ask every time, Ask, allow for a while, the default, or Automatic, which releases what it can see without asking you and records each release, with optional rows for a project or environment, an hourly cap and an end time. Commands, protected environments, `*` and an agent with no name are never Automatic. A change that gives an agent more takes effect only once you confirm it in the app or at `keypaste approve`'s terminal, whether it comes from the settings, the credential prompt's new Always for, `keypaste clients` or the agent's own `request_access_change` tool; one that gives less applies at once, and a refused or unanswered request for more holds back the next from any terminal or agent for a minute. The settings are kept in the vault, one hidden entry per agent in `.keypaste/agents` in the Agent Credential Access Format (ACAF v1, published at keypaste.com/acaf/v1/), so they travel with it and survive KeePassXC's saves and merges, and `~/.keypaste/clients.toml` now holds only this computer's ceilings, which can only take access away. `policy.toml` is no longer read, and `keypaste policy` and `keypaste agent --policy` are gone. `keypaste setup` and Connect now register every bridge with `run` offered and every item in reach, and each agent's settings narrow that: a new agent still sees every project's variables, gets values you allow and runs nothing, and an `--expose` on a bridge only narrows what its settings allow. Tagging an item into a project an agent is Automatic for names that agent first ([G.8](docs/steps/G.8.md)).

`keypaste log` and the app's History now hold one line for every agent request the app or `keypaste approve` answers. The holder writes it in its own log before replying, so a request is recorded even when a tool's configuration points the bridge's `--audit-log` elsewhere, when a program talks to the holder directly, when an agent gives up while you are asked, and when a `run --session` or token request is refused before it is considered. The bridge records only a call that nothing answered or that it refused itself. `keypaste approve` now refuses to start, before asking for the master password, when it cannot open its log. While the app cannot open its log, it refuses every agent request without asking you ([G.9](docs/steps/G.9.md)).

`keypaste setup` and the app's Connect now also connect Gemini CLI and Devin through their own commands, and connect VS Code, Cursor and, on macOS and Windows, Claude Desktop by changing only keypaste's entry in the tool's configuration file, where Cursor and Claude Desktop used to get a block to paste. Before its first change to such a file keypaste keeps the original beside it as `<file>.keypaste-backup`, and it leaves alone a file it cannot read safely. One question, or `--yes`, covers a whole `setup` run, and without a terminal nothing changes unless you pass `--yes`. Removing first reads each tool's configuration and reports a tool without keypaste as not connected, and `setup` exits 3 when it finds no tool and 6 when every tool it found refused. A tool's command is found only on `PATH`, never in the folder you run `setup` from, and on Windows a tool installed with pnpm or yarn is refused. Under a `KEYPASTE_HOME` other than `~/.keypaste`, each registration also names the vault, audit log and approver. From an AppImage, tools start keypaste through `~/.local/bin/keypaste`, which setup creates when it is missing and a newer image re-points when it starts ([G.2](docs/steps/G.2.md)).

`keypaste lock` no longer reports that the app or `keypaste approve` holding the vault did not lock when it had: the holder now answers once it has locked, and that answer is the confirmation ([F.62](docs/steps/F.62.md)).

A new terminal now runs the `keypaste` a desktop install carries. The Windows MSI adds its folder to your user PATH and removes it on uninstall. On Linux and macOS, Settings › keypaste in a terminal links it at `~/.local/bin/keypaste` or `/usr/local/bin/keypaste`, where macOS asks for an administrator's password, and on Linux each start of a newer app moves the link to it. A file keypaste did not write there is replaced only after you confirm, and kept beside it as `keypaste.keypaste-backup`. Started with `cli`, the Linux AppImage runs the CLI it carries ([G.5](docs/steps/G.5.md)).

An agent request that arrives while its client is still introducing itself is now prompted for and logged under the name the client gave, not as "an unnamed client" ([F.61](docs/steps/F.61.md)).

A save that fails partway, as on a full disk, is now reported and leaves the vault as it was; before, keypaste could report it saved and replace the vault with a truncated file. A save is now on disk before it replaces the vault. After you refuse a run, an agent asking again with one variable more or fewer, or asking for one of its fields alone, is refused for the minute too, as is a run reaching a field you just refused ([F.60](docs/steps/F.60.md)).

A crash while the app saves project mappings or dismissed recommendations no longer leaves a torn file, a vault `keypaste import` adds while the unlock screen is open is no longer dropped from Recent, and a policy rule's hourly limit is no longer reset by moving the clock forward ([F.59](docs/steps/F.59.md)).

`keypaste add` and `set` now refuse the entry titles the app refuses, `env pull` refuses a file over its size limit before reading it, and `keypaste approve` withdraws its grants when the vault is edited, as the app does. A remembered vault path `recent.toml` or `app.toml` cannot hold is now left out instead of making the whole file unreadable, and the app refuses to export a profile with no variables, as the CLI does ([F.55](docs/steps/F.55.md)).

When you refuse a request, the same field, or the same secrets for a run, is now refused for a minute whichever agent or connection asks, so an agent cannot reconnect to ask you again. Each `keypaste run --session` release, and each refusal of a valid request, now appears in `keypaste log`, and a run is refused if its line cannot be written ([F.54](docs/steps/F.54.md)).

On macOS and Linux a save now replaces the vault in one step, so a crash or power loss while saving can no longer leave nothing at the vault's path. keypaste also notices another program's save that lands while a vault is being opened or just after keypaste saves it, instead of quietly writing over it on its next save ([F.53](docs/steps/F.53.md)).

Upgrade the bridge and `keypaste approve` together: they must be the same version. Approval prompts answer `d`, `o` or `h` instead of `y` or `n`. `env export` now writes `kp://` references by default and values only with `--dotenv`. `rm` moves entries to the vault's recycle bin, which saves the vault as KDBX 4.1; KeePass 2.48 and KeePassXC 2.7 or later open it. The desktop entries below ship with the first public desktop release, which is published together with this version ([RELEASE](docs/RELEASE.md)). Each entry that names a step links its record, which holds the full account.

An MCP bridge started without `--expose` now reaches the project variables of entries tagged into a project, wherever they sit, and not an untagged entry under `env/`; `--expose` and policy rules take `tag:env:<project>` and `tag:env:<project>:<environment>`, which reach only those variables, never a password. `list_entry_names` names each entry's fields and project tags, and a bridge configured for the old default needs `--expose 'env/**'` to keep it ([C.5b](docs/steps/C.5b.md)).

Settings › Recommendations now also lists a project tag another app dropped while saving an edit, such as a phone app that does not keep tags taking an entry out of `env:billing:prod`, and not one you removed on its own. Restore tag names what the tag puts back before writing it, and Dismiss remembers your choice on this machine ([V.11](docs/steps/V.11.md)).

The MCP bridge is now part of the `keypaste` binary: `keypaste mcp` starts it, `keypaste mcp help`, `-h` and `--help` print its one help, and `keypaste-mcp` is gone from the package. MCP client configurations that named `keypaste-mcp` need updating to `keypaste` with `["mcp"]` as arguments. `keypaste setup` registers the `keypaste` that runs it, so its `--server-path` is gone ([B.4b](docs/steps/B.4b.md), [F.48](docs/steps/F.48.md)).

The desktop's Env profiles screen is now Projects, titled by its project and speaking of environments: each environment lists the entries tagged into it, Add entry tags another and Remove takes the tag off, each after naming the fields that join or leave, and every value shows the entry it lives on and the other environments that entry serves. Replacing or removing a key names every environment that reads its entry, Run's confirmation names the entries its values come from, and a tag that puts its entry in no project, such as `env:billing:Prod`, is listed ([C.4](docs/steps/C.4.md)).

keypaste no longer reads the `env/<project>` layout of 0.3, one entry per variable: such an entry stays an ordinary entry you can open, copy and request by its path, and only an `env:<project>` or `env:<project>:<environment>` tag puts an entry's fields in a project. A group named `prod` no longer makes its entries ask every time; an `env:<project>:prod` tag does. `keypaste import` copies an `env` group like any other, and `keypaste log verify` reports a line from before the hash chain as one keypaste did not write ([C.6](docs/steps/C.6.md)).

`keypaste env set`, `env pull` and the app's Env profiles now keep a new key as a protected field, on the entry you name with `--entry` or choose in the app, or else on the project's entry `env/<project>/.env` (`.env.<profile>` for another profile), created tagged on first use; a key already in the vault is updated where it is, and `env rm` leaves a field's value in its entry's history. A new key is named with capitals, digits and `_`, as a project's variable fields are. `env tag`, `env untag` and the entry pane's tag chips now say which environment and fields a project tag reaches and ask first, and without a terminal `env tag` and `env untag` need `--yes` ([C.1c](docs/steps/C.1c.md)).

When another program saves the vault the desktop has open, such as KeePassXC or a sync from your phone, the app now says so within five seconds, and Reload loads the file without asking for your password again. Agents are refused until you reload, then stay connected and are asked again. An edit the app could not save because of the other save is named in the notice and discarded by Reload ([N.16](docs/steps/N.16.md)).

An agent can now ask for one custom field of an entry it may see, such as `OPENAI_API_KEY`: the prompt names the field, and a grant for it serves none of the entry's other fields. The same field can be named in `kp:///<group>/<title>#OPENAI_API_KEY` for `keypaste run --env-file` and the `run` tool, in `policy.toml`'s `fields` and in `keypaste share --field`. Only a field named like an environment variable leaves; any other, such as `Recovery codes` or `otp`, is refused before anyone is asked or anything is read ([C.5a1](docs/steps/C.5a1.md)).

keypaste has a new brand: an icon "k." and a wordmark "keypaste." in Hepta Slab with an amber dot, amber on ink, Instrument Sans and Fragment Mono ([BRAND](docs/BRAND.md)). The desktop app is rebuilt on it, with restyled screens, a new lock screen and restyled prompt windows, and keypaste.com and the README use it too.

Closing the desktop app's window on Windows or macOS now locks it and leaves it in the tray or menu bar, whose icon offers Open, Lock and Quit; on Linux that is a choice in Settings › Startup. Settings › Startup can also open keypaste at login, locked, and with no window while it stays in the tray ([G.4a](docs/steps/G.4a.md)).

Opening keypaste while it is already running, from the Start menu, a shortcut or a launcher, now shows the running app's window instead of starting a second copy with its own tray icon, and a start at login while it runs does nothing. One app runs for each user and `KEYPASTE_HOME` ([F.38](docs/steps/F.38.md)).

When KeePassXC merges a vault with another copy, it no longer drops a value keypaste saved in the same second as the save before it. Each save of an entry now takes a later second than the version it replaced, so a burst of saves can be stamped a few seconds ahead of the clock ([F.27](docs/steps/F.27.md)).

Agents and the CLI now use the vault you chose when nothing names one: the app chooses the first vault you create or unlock and says so, Settings › Agents and the CLI changes it, and `keypaste use <path>` does the same from a terminal. `--vault` and `KEYPASTE_VAULT` still win, `keypaste setup` and the app's Connect no longer write `--vault` for the chosen vault, and an existing client entry keeps the vault it names. With a vault chosen and none named, `keypaste import <file>` copies into it; `--in-place` keeps the file where it is ([G.1](docs/steps/G.1.md)).

A project's variables are now the env-named fields of entries tagged `env:<project>` or `env:<project>:<environment>`: `keypaste run` in every form, `env ls`, `env export`, `env diff` and the app's Env profiles use them, and each run prompt names the entry every value comes from. A set is refused whole, naming the entries, when a key is on two entries, two keys differ only in case, an entry has expired, a custom field is named like a standard one such as `PASSWORD`, or a value holds a KeePass placeholder keypaste does not fill in. A set holding any entry tagged into a protected environment is asked about every time with Allow once only, needs a token that allows prod, and cannot go into a token bundle ([C.1b](docs/steps/C.1b.md)).

`keypaste access` and `import` are now refused while the desktop app or `keypaste approve` holds the vault, before asking for a password, as `token` and `share` already were. The message names what holds it and says to make the change there or run `keypaste lock` ([N.10](docs/steps/N.10.md)). `add`, `set`, `rm`, `rotate`, `field rm`, `env set`, `env rm`, `env pull`, `env tag` and `env untag` instead send their change to whatever holds the vault, which asks you, in the app's prompt window or at `keypaste approve`'s terminal, and saves it once you allow it. They ask for no master password, and the prompt names the item and what changes, never a value. Piped, they read only their values: a script that pipes the master password first is refused with nothing sent ([N.11](docs/steps/N.11.md)).

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

An entry's own KeePass tag now puts it in a project: `env:<project>` in its dev environment, `env:<project>:<environment>` in another. `keypaste env tag` and `env untag` write and remove the tag, naming the fields that join or leave, and `env ls` lists each project's environments, protected ones marked, and the entries tagged into each. A tag naming a protected environment, such as `env:billing:prod`, makes every agent request for that entry ask and offer Allow once only. Tagged fields do not reach `run` yet ([C.1a](docs/steps/C.1a.md)).

`keypaste set <entry> --field <name>` sets a custom field on an existing entry, protected unless `--plain`, and several `--field` make one revision. `get --field` copies or prints one, `field ls` names an entry's fields and their protection without a value, and `field rm` removes one, which stays in history. KeePassXC's own attributes such as `otp` are read and never written ([V.7a](docs/steps/V.7a.md)).

Every approval prompt, in the desktop and in `keypaste approve`, offers Deny, Allow once, which keeps nothing, and Allow for 1 hour (`d`, `o` and `h` at the terminal), whatever lifetime the agent asked for. `--max-ttl` shortens the hour. An entry in a protected profile such as `prod` offers Allow once only.

A project holds profiles: `env/<project>` is dev and `env/<project>/<profile>` any other. `-p` picks one in `env` and `run`, `env diff` compares key names across profiles without printing a value, and profiles named `prod` or `production` are protected. `kp://<project>/<profile>/<KEY>` and `kp:///<group>/<title>#<field>` name a value without holding it.

`keypaste env export` now writes a `.env.keypaste` of `kp://` references and no values, safe to commit; `--dotenv` still writes plaintext. `keypaste run --env-file .env.keypaste` resolves every reference or starts nothing.

`keypaste token create` makes a scoped, inject-only token for CI, such as `read:acme-api/staging/*`, printed once and expiring after 30 days by default. `keypaste run --token` injects what its scope covers through the process holding the vault, which audits it, and `token bundle` seals the sets into a file for `run --bundle`. A protected profile needs `--allow-prod` and still asks you live. The Agents screen creates, lists and revokes the same tokens.

`keypaste share` encrypts one field, or an entry's username, password and URL together (`--field login`), on this machine and uploads only ciphertext. The link holds the key, opens 1 to 10 times, expires after 5 minutes to 7 days, can require a passphrase and can be revoked; `share ls` lists your links. keypaste.com serves them from 2026-09-25.

`keypaste import <file.kdbx>` copies another KeePass file into the vault with its fields, attachments and history, leaving its recycle bin behind and never writing the file. `--dry-run` shows the plan and `--in-place` keeps editing the file itself. The desktop has the same as its Import .kdbx dialog.

`keypaste mcp --allow-run` offers agents a `run` tool. After you approve the exact command, directory, variable names and reason, or under a grant of at most 15 minutes for that command line, it runs the command with the values in its environment and returns its output with each value replaced; a command the agent can edit can still reveal a value (T-35). Nothing adds `--allow-run` for you.

`~/.keypaste/clients.toml` narrows one MCP client at a time: Ask every time, Session grants up to 1 hour, or Inject only. Set it with `keypaste clients` or on the desktop's Agents screen.

The desktop app unlocks a vault that also needs a YubiKey, as KeePassXC protects one with HMAC-SHA1 challenge-response, and Settings adds or removes the key after saying what a lost one costs. It shows Touch your YubiKey while the key waits, on unlocking and on every save. It has not yet been tried with a physical key, and the CLI cannot open such a vault (T-37).

`keypaste rotate <entry>` and the entry pane's Rotate replace a password with a generated one, keeping the old value in history and never showing the new one.

`keypaste grants` lists the grants the process holding the vault has given, never a value; `grants revoke <id|agent>` and `--all` end them. `keypaste lock` locks the desktop app or `keypaste approve` holding the vault.

The CLI shows what needs you in amber, what is done in green and what was refused in red, and groups its help into secrets, agents and vault commands. `--json` gives machine-readable output from `ls`, `env ls`, `log`, `grants`, `token ls`, `share ls` and `clients`. `set` joins `add`. Audit lines now name the vault, the entries used and a run's command.

The desktop's Secrets screen no longer overlaps with an entry open: the search box, New, Rename or move and Delete run across the list and the entry's pane, and the dividers sit between the panes instead of over them. The main window can no longer be made narrower than 960 px, the least width at which the screen fits ([F.22](docs/steps/F.22.md)).

Closing the desktop's main window now ends the session even while an agent's request waits in its prompt window: the request is refused as `vault-locked` and the prompt closes. Before, the prompt kept the app running with the vault unlocked, and approving on it still released the value ([F.21](docs/steps/F.21.md)).

`keypaste run --session <project> -- <command>` now takes the project's variables from the desktop app or `keypaste approve` holding the vault unlocked, and asks for no password. The app shows the project, the variable names, the command and the directory in a prompt window, and `keypaste approve` shows them in its terminal. The command starts only on Allow once or Allow this command for 15 minutes (`o` or `h` in `keypaste approve`). Deny, a lock, the timeout or nothing holding the vault ends the run with a reason and starts nothing ([E.1c](docs/steps/E.1c.md)).

The desktop's Env profiles screen can now import a `.env` into a project, listing each variable as new, replacing a stored value or unchanged and writing only when you press Import. A project can be given a directory and a command on this machine: Run opens a terminal there running the command, and Open terminal opens one at a prompt, each with the project's variables from the unlocked app and only after a card naming the command, the directory and the variable names. A set with an expired entry or a bad name, a lock or Cancel starts nothing. Windows and Linux only ([E.1b](docs/steps/E.1b.md)).

`keypaste run` now refuses a project whose set holds an expired entry, as it already refused names that cannot be exported, and names every entry it refuses and why without printing a value. An untitled entry, which used to be skipped, now refuses the set too, and a duplicated name is listed with the others rather than reported alone. An entry in the recycle bin is still left out. The desktop's session can resolve a set the same way, reading the vault as saved and releasing nothing once it locks, ready for launching projects from the app ([E.1a](docs/steps/E.1a.md)).

The Agents screen can now connect Claude Code or Codex to the vault the desktop has unlocked. It shows the exact commands, with the vault, the label and the exposure, and runs them only when you press Run it. For Cursor and Claude Desktop it shows the block to paste. Check the connection asks once through the bridge the client will start, in the app's own prompt, and Remove takes keypaste out of the client again. The internal desktop packages now include the bridge, and `keypaste setup --dry-run` prints the same commands the app shows, quoting any argument with a space in it ([2.6a](docs/steps/2.6a.md)).

The Agents screen now lists the request waiting in the desktop's prompt and the grants in force, with the client, label, entry, field and time left, and Revoke or Revoke all makes the next request ask again. Below them is this session's audit history, which says when the log is unreadable or edited instead of looking empty ([4.3b](docs/steps/4.3b.md)).

The desktop now asks about an agent's credential request in its own prompt window, showing the client, its label, the entry, the field, the lifetime and the agent's reason, and releases only when you press Allow once or Allow for 1 hour, which work a second after the window appears. Deny, closing it, the timeout, locking and the client giving up each refuse. At the app and at `keypaste approve`, a request whose client cancels or whose `keypaste mcp` goes away is now withdrawn at once. The app does not read `policy.toml` ([4.4](docs/steps/4.4.md)).

The desktop and `keypaste approve` now apply every limit on an agent's request themselves: entry and reason length, the field, a lifetime of 1 to 3600 seconds and the exposure. A process that talks to them without the bridge is refused the same way, and an out-of-range lifetime is refused rather than shortened. Agents using the bridge see no difference ([4.3a](docs/steps/4.3a.md)).

The Agents screen now says what agents meet as the app's session reports it, naming this app's process and session while it serves the vault. When `keypaste approve` already holds the vault, the unlock screen names it as the owner and leaves it running. A desktop app that crashed leaves nothing answering agents; relaunched, it starts locked and serves again once unlocked ([4.4b](docs/steps/4.4b.md)).

Agents now get the vault as it is saved. A password changed in the desktop is the next value an agent receives, and editing, moving, renaming or deleting an entry, or changing the vault's access, makes the next request for it ask again instead of reusing an earlier approval. When another program saves the vault file, the desktop and `keypaste approve` refuse every agent request as `vault-changed` until you lock and unlock, or restart the agent, and they never write over that save ([U.3](docs/steps/U.3.md)).

Locking is now one step for agents. Every desktop lock, and quitting, denies a request still waiting at the app as `vault-locked` in the audit log and clears the grants agents were given; so does stopping `keypaste approve` with Ctrl+C, SIGTERM or by closing its terminal. An agent's requests never keep the app unlocked, and one that arrives after the machine slept past the timeout locks the app and is refused. Values already delivered are not recalled ([U.2](docs/steps/U.2.md)).

The desktop now owns the vault it unlocks. A `keypaste mcp` configured for that vault reaches the app, which answers `list_entry_names` and refuses every credential request until approving in the app exists, and each audit line names the unlocked session that answered. `keypaste approve` on a vault the app holds, or a second app on it, is refused naming the holder before any password is asked for. The bridge now needs `--vault`, and the bridge and the approver must both be this version ([U.1](docs/steps/U.1.md)).

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

The desktop's Trash screen restores one recycled entry, or erases one behind its own confirmation, and Delete offers an immediate Restore ([V.3b](docs/steps/V.3b.md)).

`keypaste rm` and the desktop's Delete move an entry to the vault's KDBX recycle bin with its fields and history, unless the vault's recycle bin is switched off; a recycled entry is hidden from listing, `keypaste run` and agents. A vault that has recycled anything is written as KDBX 4.1, which KeePassXC 2.7 and KeePass 2.48 and later read ([V.3a](docs/steps/V.3a.md)).

Passphrases: `--generate --words N` on `add` and `env set`, the desktop's Generate boxes, and `keypaste generate --words N`, which prints one to stdout and stores nothing. Words come from EFF's long list, pinned by SHA-256; six words, about 78 bits, is the minimum. The separator defaults to `.` and a hyphen is refused, because four list words contain one.

The desktop shows an entry's history and restores a revision; the replaced value stays in history, so a restore can be undone by another.

The desktop stores an existing secret typed or pasted into a masked field. Paste drops one trailing line break and refuses anything a keyboard could not type.

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

## 0.5.0-rc.1

Unadvertised candidate built from the source listed under Unreleased. It exists to install the internal, unsigned Windows MSI and Linux AppImage on fresh runners and check that a new terminal runs the `keypaste` they carry. The desktop packages stay workflow artifacts and are not published.

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
