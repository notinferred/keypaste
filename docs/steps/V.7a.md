# V.7a — Manage custom fields in core and the CLI

Completed 2026-09-28 on `main` above `c23574f`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

The founder selected V.7a together with C.1a and V.7b on 2026-09-28 and approved a plan that settled what the row left open:

- "Stay readable" means `get --field` reads any custom field that exists, KeePassXC's attributes included; `set --field` and `field rm` refuse every listed name. `get --field` refuses a standard name, which plain `get` reads.
- KeePassXC's attribute names are matched ignoring case, as standard names are.
- `--field` may be given several times on `set`, and only there, so three fields are one command and one revision.
- `set --field` writes to an entry that exists and does not create one; `--plain` naming a field that already exists is refused rather than ignored; `--field` with the generate flags is refused.

## Evidence

Local, Windows 10 Pro 19045, KeePassXC 2.7.10, on `main` above `c23574f` with the step's changes uncommitted.

**Core.** [VaultFieldTests](../../tests/Keypaste.Core.Tests/VaultFieldTests.cs), 32 cases: a new field is protected and a `Protect: false` one plain across a save; an existing field keeps its flag unless a write names one; three fields are one revision and one `Edited` naming the entry; removing is one revision with the value in history; 20 refused names each change nothing, leave `ReadSaved` current and raise no `Edited`; a name given twice, a kept value of a missing field and an empty write change nothing; a standard name is never read as a custom one; fields another client wrote are listed and readable, `otp` read-only; a field write leaves the other fields, the attachment and the tag; `UpdateEntry` writes no custom field or flag. `VaultSearchTests.AProtectedCustomFieldIsNotMatched` now writes its field through `SetFields`. The foreign-vault fixture gained KeePassXC's `otp` attribute, and `KdbxImportTests` still pass with it.

**CLI.** [FieldVerbTests](../../tests/Keypaste.Cli.Tests/FieldVerbTests.cs), 30 cases, over the verbs' prompts, exit codes, one revision for three fields, the `--plain` rules and refusals before the vault opens. `CommandLineTests` gained a repeating option; `CliAppTests` pins the new help lines and 20 verbs; `KeyfileOptionTests` gained four field command lines. `SecretHygieneTests` seeds a protected and a plain field through `set --field` and sweeps `field ls` in text and JSON, `field rm`, `get --field` and `set --field` for every sentinel.

**KeePassXC.** [verify-keepassxc-fields.sh](../../scripts/verify-keepassxc-fields.sh) passed against the release build. KeePassXC imports XML for an entry with a plain and a protected field, a tag, custom data, `otp` and a revision, and attaches a file. The shipped CLI sets `STRIPE_SECRET_KEY` and changes `Region`. KeePassXC reads both with `show -a`, its export marks `STRIPE_SECRET_KEY` and the untouched `Recovery` protected and `Region` plain, and the tag, custom data, `otp`, attachment (byte for byte) and KDBX 4.0 header remain. Three `--field` make exactly one more `<History>` item. `set --field otp`, `--field Password`, `--field password` and `field rm … otp` each exit non-zero with identical bytes and no new backup. `field ls`, `field ls --json`, `ls` and `ls --json` print none of nine values and name the new field protected and `otp` read-only. keypaste sets `MERGED`; an XML copy of the entry with a new value and a 2037 modification time is imported and merged with `keepassxc-cli merge -s`, after which `get --field MERGED --show` prints KeePassXC's value and the export's history holds keypaste's. The negative controls fail a corrupted expectation, a plain field read as protected and a changed attachment byte.

The first run failed its first check: KeePassXC wrote KDBX 3.1, because under its default AES-KDF it writes 4.x only when something needs it. The seed gained entry custom data, as the workflows gate's seed already has, and KeePassXC then writes 4.0.

[verify-keepassxc-workflows.sh](../../scripts/verify-keepassxc-workflows.sh) gained a field step after the history step. On the password, password-and-keyfile and keyfile-only vaults the CLI sets a protected and a plain field on the entry carrying the unmodelled data and removes the plain one, KeePassXC reads the values and no longer finds the removed field, a field named `Password` is refused byte-identical, and every marker, both attachments, the cipher and KDF and KDBX 4 remain. The whole gate passed on 2.7.10.

**The command.** `./scripts/verify.ps1` selected workflows, scripts, backend, integration and desktop and passed in about eleven minutes: the backend suites ran 2,940 tests, 2,930 passed and 10 skipped; the desktop suite 719, 717 passed and 2 skipped; Consistency 42 of 42. `bash scripts/verify.sh compat`, with `KPXC_CLI` naming the installed 2.7.10, passed every gate: compat, write-back, history, recycle bin, run, backup, organize, import, fields, keyfile, XML attach and workflows.

Hosted CI has not run. When `main` is pushed, the `ci.yml` compatibility job runs the fields gate on three operating systems and `app.yml` runs the workflows gate on Ubuntu and Windows.

## Decisions

D-0369 in [DECISIONS](../../DECISIONS.md).

## Limits and follow-ups

- A field KeePassXC made under a standard name in another case, such as `password`, is listed read-only and cannot be read with `--field`, which refuses standard names in any case.
- `set --field` cannot create an entry, and `--field` values cannot be generated.
- `EntryRevision` still carries only the standard fields, so no surface shows a revision's custom fields.
- The desktop shows no custom fields until V.7b, and no custom field reaches an agent until C.5a.
- Source only; the fields gate has run only locally on Windows.
