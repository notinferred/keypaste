# V.10 — Complete current-password use and accessible input

Completed 2026-09-23 on `49e055c`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

The founder decided one question during planning on 2026-09-23. A history revision could be revealed but not copied, and KeePassXC's History tab (Show, Restore, Delete, Delete all) copies nothing either. The founder chose to add Copy to revisions anyway, so all three secret surfaces copy and clear alike (D-0300).

## Evidence

Tests, all passing on Windows 10 at the source above:

- [Current-password reveal](../../tests/Keypaste.App.Tests/Controls/CurrentPasswordRevealAutomationTests.cs) has 8 tests on the rendered Entries view. Pointer press and release events are raised on the drawn `CurrentPassword` cell rather than calling its methods, and `RevealedValue.Rendered` shows the value and then dots. Capture loss, selecting another entry, switching screens and locking each end the hold, and a press on a locked vault draws only dots. The whole window's automation surface while held equals the surface at rest, is non-empty, names the entry and carries neither the current nor the superseded password (D-0232).
- [Copy parity](../../tests/Keypaste.App.Tests/Clipboard/SecretCopyParityTests.cs) has 11 cases over the rendered shell with `AvaloniaClipboard` on the headless platform's clipboard. A preflight shows that clipboard round-trips and clears a secret. For each of the current password, an env value and a revision, the drawn Copy button is pressed with Enter. The clipboard then holds the value's hash, and the countdown's expiry, the shell's Clear now button and a lock (the session locks and the shell is disposed, as `App.ShowUnlock` does) each clear it. A stale revision puts nothing on the clipboard, reports the change and rereads the list.
- [Hygiene](../../tests/Keypaste.App.Tests/SecretHygieneTests.cs) gains `The_entry_pane_hands_its_current_password_only_to_a_hold`. No property of the Entries screen or the pane carries the current password before or during a hold, and a locked session reveals nothing.
- The 11 D-0099 differentials in the [unlock](../../tests/Keypaste.App.Tests/Controls/MaskedInputAutomationTests.cs), [Settings](../../tests/Keypaste.App.Tests/Controls/VaultAccessAutomationTests.cs) and [entry and env](../../tests/Keypaste.App.Tests/Controls/SecretFieldAutomationTests.cs) classes each check that the field's peer name is non-empty and equal to its placeholder at rest, after the first secret and after the second.
- The twelve affected classes, the existing reveal, history, clipboard and secret-field classes included, pass together: 160.

Mutations, each restored afterwards:

- Returning the base peer name fails all 11 differentials.
- Dropping `EndReveal` from `OnPointerCaptureLost` fails `Losing_the_pointer_ends_the_hold`.
- Copying a revision by index without the re-check fails `A_stale_revision_is_refused_and_the_list_read_again`.

Verification: `./scripts/verify.ps1` on the finished tree ran workflows, scripts and desktop, and passed all three; desktop ran 405 app tests and 40 consistency tests, none failing. Backend and integration were skipped because no changed path maps to them. `compat` was not run and no KeePassXC run was made, because this step writes no new vault content.

## Decisions

Ledger rows from this step, which constrain later work, stay in [DECISIONS](../../DECISIONS.md): D-0300 and D-0301.

## Limits and follow-ups

A value can be revealed only with a pointer. The cells take no focus and no keyboard gesture holds them, which is unchanged for env values and revisions and now applies to the current password too. The hold is driven by events raised on the drawn cell, not by a click the platform resolved through a rendered frame, because the headless session draws none. Reading what a frame drew is 4.6. Each masked field still publishes its length through the mask, as D-0099 allows. Naming by placeholder means a field's announcement is its placeholder's wording, and Env Sets' "New value" does not name the variable being replaced; the prompt above it does. No person has used the reveal or the revision Copy on a real desktop, and the guide's checks 46 and 47 record what to observe.
