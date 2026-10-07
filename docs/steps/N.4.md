# N.4 — Create an item from a template

Completed 2026-09-29 on `main` above `40acb68`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

The founder selected N.4 with N.2 and N.5 on 2026-09-29 and settled what the row left open:

- **A host** is a plain custom field named `Host`, since KeePass has no standard field for one and the URL field is a web address the pane opens (N.5).
- **"No typed value"** on the form's automation tree is read as no typed secret. A plain text box publishes its text to assistive technology, which is how a screen reader reads back a title or a username, so the title, username, host, key name and notes are on the tree, as every plain field in the app is. The password and the key's value, both masked inputs, are not.

## Evidence

Local, Windows 10 Pro 19045, KeePassXC 2.7.10, on `main` above `40acb68`.

- **Core.** [VaultCreateEntryTests](../../tests/Keypaste.Core.Tests/VaultCreateEntryTests.cs): an item with a protected and a plain field and two tags is one edit naming it, reads back whole after a save and has no revision; a login's password is protected as KeePass keeps it; a taken title, an empty or slashed title, a missing group, `env/…`, `env` itself, `.keypaste`, the names `otp` and `Password`, a tag with a comma, and a field or tag named twice are each refused with no edit and the file's bytes unchanged.
- **The form.** [NewItemTests](../../tests/Keypaste.App.Tests/ViewModels/NewItemTests.cs): the folder picker lists the top level and the vault's groups and never `env`; a login, an API key, a database, a server and a secure note each write exactly their own fields, the key protected and the host plain, with no revision; switching templates keeps the title, folder, tags and notes; an empty title, a taken title and the key names `otp`, `api key` and `KPXC_KEY` leave the form open and the file byte for byte; cancelling zeroes what was typed. `SecretFieldAutomationTests` types into the API key's value field and finds its surface depends on the length and not the characters, and nothing on the screen carries what was typed, beside the password field it already covered; `DrawnMaskTests` finds no typed key value drawn at 1, 5, 24 or 64 characters; `SecretHygieneTests` still finds no secret on any view model.
- **Real KeePassXC.** [verify-keepassxc-workflows.sh](../../scripts/verify-keepassxc-workflows.sh) gained a fourth vault KeePassXC imports from the same seed, on which [Keypaste.AppDriver](../../tests/Keypaste.AppDriver/Program.cs)'s new `item-new` act presses New item, picks each template, types the title and the plain fields, picks the folder from the picker, adds tags through the chips and types the secret. KeePassXC reads the login's username, password, web address, notes and tag, the API key's `N4_API_KEY` as protected with its project tag, the database's and the server's `Host` as plain with their username and password, and the secure note's notes, and counts no revision for any of the five. A taken title, the key names `otp` and `api key`, and an empty title are each refused by the driver with exit 1, the vault's bytes identical and no backup kept. The vault stays KDBX 4.0 and keeps every marker the seed carries. A check of the gate's revision count on the seed's `servers/database`, which carries KeePassXC's own revisions, reads three, so the count of none for the new items is one that can fail.
- **Updated tests.** Every test that made an item through the old typed path now picks its folder and title through `NewItemForm`, in the app's tests and the Consistency project, which reads what the form wrote with the CLI.
- **The command.** `bash scripts/verify.sh` in the step's worktree selected workflows, scripts, backend, integration and desktop. The backend suites ran 3,101 tests, 3,091 passed and 10 skipped. Desktop first failed on `dotnet format`'s import order in four test files the step had given a `using`, and, resumed with `--from desktop` once they were ordered, ran 838 tests, 835 passed and 3 skipped (the renderer's draws); Consistency 43 of 43. `bash scripts/verify.sh compat` then passed all thirteen KeePassXC gates against KeePassXC 2.7.10 on Windows, the workflows gate with its templates step among them. `ScreenRenderer` drew the New item form in both palettes with at most one amber element.

## Decisions

- D-0379: `Vault.CreateEntry` makes a new entry whole, in one change and with no history item, and a host is a plain `Host` field. It amends D-0369, which named `SetFields` and `RemoveField` as the only writers of custom fields.

## Limits and follow-ups

- No template has more fields than the row names: a database's name or port, for example, goes in the host or the notes.
- The CLI has no template verb; `keypaste add` is unchanged.
- The form cannot create a group; New group in Items' "+" does.
- Source only.
