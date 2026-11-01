# V.12 — A password generated where it goes, or on its own

Completed 2026-11-01 on `v12-generator`, source only.

## What changed

- **A generated value goes into the field it is for** (`SecretField.Generate`), drawn from the generator straight into the field's buffer and never a string, and a key typed or pasted afterwards replaces it whole rather than adding to it.
- **Generate beside every password and protected field in a form** (Lucide `wand-2`): New item, Edit fields, Add a field, Replace, and Projects' new and replaced values. While the field holds a generated value its choices show under it, one `GeneratorOptions` view for every form and the dialog, and changing a choice generates again.
- **New item and a new variable open with a generated value in their field**, where it can be copied before Create, replacing the "Generate a password" and "Generate a value" boxes, which generated only at Create and showed nothing; typing or pasting puts the person's own in its place. `GeneratePassword` and `GenerateValue` keep their meaning for the forms' callers: on, the field holds a generated value.
- **On the item pane**, Generate beside Reveal on the password opens Edit fields with a generated password, and a protected field's row action opens its Replace with a generated value; each form's Save is the question, and the item's history keeps the old value.
- **Generate password…**, in File and Items' New menu, with no chord because ⌘G is Find next, is a dialog over the screen: the choices, the masked value with Reveal held and Generate another, Copy through the clearing countdown, New item with it as the password, and Add to the chosen item as a protected field through Add a field's own write; Cancel and Escape close it, and closing or locking wipes it. It holds the shell still as the other dialogs do.
- The design handoff and the desktop guide say the same; the guide's checklist step 23 gains the new routes.

Choices settled in the step, each reversible: Rotate stays as it was, a replacement nobody sees; plain fields such as `Host` get no Generate; New item's characters keep the generator's default 20.

## Evidence

- `GeneratePasswordTests` (6): a generated value fills its field and what is typed replaces it whole; Generate on the saved password opens Edit with one and Save writes it with the old one in history; Generate on a protected field fills its Replace, follows a change to Words and writes on Replace, and a KeePassXC attribute offers none; Generate password copies through the countdown as a secret and starts New item with exactly that value, wiping its own; it adds its value to the chosen item as a protected field; and while open it holds the shell's commands off and puts no value in the automation tree.
- `ExistingSecretTests`' turning generation back on now replaces what was typed with a generated password.
- On the founder's Mac (macOS 26.6, arm64): `verify.sh test` passed those classes with `AppCommandsTests`, `AppMenuTests`, `NewItemTests`, `EntryFieldsTests`, `SecretFieldAutomationTests`, `DrawnMaskTests`, `SecretHygieneTests`, `GeneratedPassphraseTests`, `EnvProfilesTests`, `ShellViewModelTests`, `TrashTests` and the Projects, plain-words, accessible-drive and launch journeys, and `Keypaste.Consistency.Tests.ExistingSecretIsVisibleToTheCliTests`; `verify.sh compile` passed.

## Limits and follow-ups

- The CLI's `rotate` and `set` are unchanged; generating from a terminal is as before.
