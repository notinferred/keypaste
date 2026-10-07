# V.7b — Edit custom fields and tags in the app

Completed 2026-09-28 on `main` above `a398212`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

The founder selected V.7b together with V.7a and C.1a on 2026-09-28 and approved a plan that settled what the row left open:

- Every custom-field value is masked at rest, plain ones included, because the row's Verify allows no field value on the automation surface at rest, and KeePassXC leaves new attributes unprotected by default. Every value copies through the countdown.
- KeePassXC's own attributes, such as `otp`, can be held and copied, as `get --field` reads them (V.7a); they offer nothing that changes them.

## Evidence

Local, Windows 10 Pro 19045, KeePassXC 2.7.10, on `main` above `a398212` with the step's changes uncommitted.

**View models.** [EntryFieldsTests](../../tests/Keypaste.App.Tests/ViewModels/EntryFieldsTests.cs), on a vault another KeePass application wrote: the pane lists `PIN` protected and `otp` as KeePassXC's with a fixed mask and nothing to change it; adding a protected and a plain field is two revisions with the flags core then reads; four refused names (`otp`, `Password`, `KPXC_X` and an existing `PIN`) write nothing and leave the form open with a sentence; replacing a value keeps its protection and the shield keeps its value, one revision each; Remove asks first and is one revision; tag chips show a project tag's environment and protection, flag a malformed one that still protects, and each add and remove is a revision; a refused or duplicate tag writes nothing; a change over a file another program saved is refused as a changed vault; copying goes through the countdown; disposing the pane empties its fields, tags and forms.

**Automation and frames.** `DrawnRevealTests` gained a protected and a plain custom field as surfaces: in a frame Skia drew, each value is drawn in its cell only while pressed, dots at rest and after release, no other secret anywhere, and the window's automation surface while held equals the surface at rest and carries no secret. `DrawnMaskTests` types values of 1, 5, 24 and 64 characters into the two new masked inputs and finds none drawn. `SecretFieldAutomationTests` gained the two differentials (two same-length values with no shared character give the same surface), the mask reaching the tree and nothing on the screen exposing what was typed. `SecretCopyParityTests` copies both custom fields through the countdown and sees it clear the platform clipboard. `SecretHygieneTests` gained a protected and a plain field value on the selected entry, both on the never-anywhere list, so no property of any view model on any screen, before or after a lock, may hold either. `EntriesViewLayoutTests` still holds at 1000×680 and 960×520. The first run of `DrawnRevealTests` failed for both field surfaces: the value cell took its content's width, twelve dots at rest and the whole value while held, so a hold changed a size the automation surface reports, which D-0232 forbids and which would tell a reader the value's length. It also showed a 27-character value clipped in a narrow column. Each row now puts its value on its own line under the name and buttons, and the cell takes the row's width, as the env matrix's cell takes its column's. `ScreenRenderer`'s demo entry gained two fields and two tags, and its renders at 1280 and 960 px were checked against the design. A Consistency test adds a field and a tag in the pane and reads them with `keypaste get --field` and `keypaste env ls`.

**KeePassXC.** [verify-keepassxc-workflows.sh](../../scripts/verify-keepassxc-workflows.sh)'s seed gained two custom fields and KeePassXC's `otp`, whose secret joined the markers every check after a write looks for outside `<History>`. A new app step on each of the three vaults runs [Keypaste.AppDriver](../../tests/Keypaste.AppDriver/Program.cs)'s new acts, which press the pane's commands and type into its fields: it adds `kp7b-added` protected, replaces `kp7b-change`'s value, which stays plain, then protects it, removes `kp7b-remove`, and adds and removes the tag `env:kp94:prod`. KeePassXC reads both values, marks both fields protected in its export, no longer finds the removed one, reads the tag and then does not, and still finds every marker, `otp` among them, both attachments byte for byte, the same cipher and KDF and a KDBX 4.0 header. The app adding a field named `otp` is refused with exit 1, identical bytes and no backup. The whole gate passed on 2.7.10.

**The command.** `./scripts/verify.ps1` selected workflows, scripts, backend, integration and desktop and passed in about eleven minutes: the backend suites ran 3,023 tests, 3,013 passed and 10 skipped; the desktop suite 751, 749 passed and 2 skipped; Consistency 43 of 43. The `compat` profile was not run again after C.1a's: of its gates only the workflows gate reaches the app, and it passed on this step's final code, run directly with the release builds.

Hosted CI has not run; `app.yml` runs the workflows gate on Ubuntu and Windows when `main` is pushed.

## Decisions

None.

## Limits and follow-ups

- The pane cannot rename a custom field; KeePassXC can.
- History rows still show only the password, not a revision's custom fields or tags.
- Reveal needs a pointer, as for the password: no keyboard gesture holds a field value.
- The tag box takes a tag as typed; it offers no list of projects to pick from, which C.4's Projects screen may.
- Source only; the app step has run only locally on Windows.
