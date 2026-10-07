# F.25 — Point the install exercise at controls the app has

Completed 2026-09-29 on `main` above `6972419`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

The stale exercise was found during N.1a1 and filed then. The founder directed on 2026-09-29 that work found in these steps is finished rather than left on new rows, so it was built in the same session as N.7.

## Evidence

Local, Windows 10 Pro 19045, on `main` above `6972419`.

**The exercise was staler than the row said.** Reading each act against the app found four more places where it no longer matched:

- `unlock` waits for an element named "Lock now", but the lock button carried only a tooltip, and a tooltip is not an accessible name;
- "Edit" is now "Edit fields", inside the ⋯ menu, which is a toggle;
- "Add variable" is now "Add a key to dev", a button whose content is an icon and text and so had no name;
- `set-only` expects exactly one field in the window, but since N.1a1 the title bar's search is always a second.

Since U.1–U.3, the terminal agent holds the vault. So the app's second unlock is refused, and its unlock screen says "Keypaste agent (process N) holds this vault, and agents reach it there." That sentence replaces "A keypaste agent is running" as the evidence the approval check records.

**The changes:**

- the names above: 53 buttons, the new key's field and the item rows;
- a `toggle` act in both drivers (UI Automation's TogglePattern on Windows, the AT-SPI action on Linux);
- `set-named` in place of `set-only`, finding a field by its accessible name;
- the exercise's acts rewritten to those names.

**The check.** [AccessibleDriveTests](../../tests/Keypaste.App.Tests/Session/AccessibleDriveTests.cs) does the exercise's edit, variable and lock acts through Avalonia's automation peers, which are what UI Automation and AT-SPI read. Each act finds the first visible element with that name and control type, as the drivers do, and acts on its pattern:

1. select "github";
2. toggle "More actions";
3. invoke "Edit fields";
4. set the field after "Username";
5. invoke "Save".

The vault then holds the new username. Selecting "billing", invoking "Add a key to dev", setting "New key" and invoking "Add" puts the key in the vault, and invoking "Lock now" locks the app. With the lock button's name removed, the test fails with "nothing visible is a Button named 'Lock now'". That is the failure the old exercise would have met.

The second test in that class visits Items with an item and both menus open, then Agents, History, Trash, Settings, the activity log, share links, a project and the lock screen. It fails on any visible name that is a type rather than words. Run as a probe after a first pass had named 31 buttons, it still found nine buttons and every item row; it passes now.

**The unlock screen's guard.** `MaskedInputAutomationTests` held that nothing on the unlock screen attaches an automation name, because a bound name publishes whatever it is bound to. The first verifier run failed it on the new "Unlock" label. The guard stays as strict about everything else:

- it allows only the thirteen literal labels the view writes, and only on buttons;
- it still fails on any other name, and on any help text, item status, item type or automation id;
- it checks all of this with the fixture password typed.

`bash scripts/exercise-desktop-install.sh --selftest` passes its 33 classification cases with the new evidence strings.

**The command.** `bash scripts/verify.sh` selected workflows, scripts, backend, integration and desktop, since the drivers are paths no narrower profile claims. The first four passed; desktop failed only the unlock-screen guard above, and after the guard's change `--from desktop` passed: 809 tests, 806 passed and 3 skipped (the renderer's draws), and Consistency 43 of 43.

## Decisions

None.

## Limits and follow-ups

- **Not run against an installed candidate.** The exercise runs on the internal-candidate workflow; its next run is the evidence that the whole journey passes on Windows and Linux.
- **Only the states the sweep visits are held to it.** A form the sweep does not open could still hold a control named by its type, though every button element in the views now carries a name.
