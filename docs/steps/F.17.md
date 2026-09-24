# F.17 — Diagnose the intermittent Linux exposure in `DrawnMaskTests`

Completed 2026-09-24 on `8f1f157`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Build:** on `ubuntu-24.04`, app run 35891535300 at `0a6b05e` failed `A_shell_field_never_draws_what_is_typed(name: "ReplacementEnvValue")` with "TextBlockAutomationPeer.GetHelpText exposes the fixture password"; runs 35891973017 at `4f1b3a9` and 35892763909 at `0a6b05e` passed, and a Windows run with `Q` in the temp path did not reproduce it. App run 35953567143 at `530b6b8` failed the `AccessConfirmPassword` case in attempt 1 and the `ReplacementPassword` case in attempt 2 with the same message; the peer is the shell's vault-path tooltip (`ShellView.axaml`). The one-character value is always `Q`, and `Directory.CreateTempSubdirectory` names the fixture's directory with six characters from `[A-Za-z0-9]` on Linux, so about one fixture path in ten contains it; the probe tests that first. A probe on a branch, dispatched with `gh workflow run --ref`, repeats that case with the identical desktop test command on `ubuntu-24.04`. On a failure it records which element's peer answered, the whole help text and where it was bound from, and whether the matched text is the typed value or a `Q` that was already on screen, such as in a path or a label. A short preflight first runs the case a handful of times to size the repetitions. The probe is deleted once the diagnosis closes and its result kept in the record, per [diagnostics](../diagnostics.md).

**Verify (V-F.17):** the record names the source SHA, runner, command, repetitions and failure count. For the observed failure it names the element, property and text, and says whether a value typed into a secret field reached the automation tree. If one did, a repair row with a regression that fails on the recorded input is added to STEPS. If the sweep matched text that was never typed, the sweep's sentinel is changed so it cannot occur in the rest of the window, with a test that fails on the recorded text. A run that simply passes, or a conclusion with no recorded failing observation, leaves the row open with its next experiment named.

No amendment. On the founder's choice, the probe ran as a cut-down `app.yml` on the `f17-probe` branch, which put nothing on `main`.

## What changed for users

Nothing. No typed value reached the automation tree. The test had matched the vault's path, which the app shows by design, and only the test changed.

## Evidence

**Mechanism.** `NeverDrawn` types a one-character value, the first character of its pool, `Q`, and then sweeps the whole window's automation surface for it. The shell's sidebar TextBlock that shows the vault's file name binds `ToolTip.Tip="{Binding VaultPath}"` (`ShellView.axaml`), and Avalonia answers that TextBlock peer's `GetHelpText` with the tooltip. `RenderedShell` keeps its vault in `Directory.CreateTempSubdirectory("keypaste-drawn-")`. On Linux that name ends in six characters from `[A-Za-z0-9]`, which contain `Q` about 9.3% of the time. Across the theory's seven cases, a run has about a 50% chance of at least one failure, which matches the five passes in ten Linux runs that 2.6a recorded. On Windows, .NET names the directory in lowercase base32, which never contains `Q`.

**Instrument.** Enabled by `KEYPASTE_F17_TRACE`, on the probe branch only. For each case it recorded the fixture directory and the outcome. It swept the window for `Q` before the first keystroke. On a match it recorded the answering peer's owner, its text, the full help text, its `ToolTip.Tip` and `AutomationProperties.HelpText`, and whether the tip equals `ShellViewModel.VaultPath`.

**Preflight, local, Windows 10 Pro 19045, SDK 10.0.302:** `dotnet test --project tests/Keypaste.App.Tests/Keypaste.App.Tests.csproj --no-build -c Release -- --filter-method '*A_shell_field_never_draws_what_is_typed'`.

| Temp setting | Failed |
|---|---|
| `TMP` and `TEMP` set to a directory containing `Q` | 7 of 7 |
| `TMP` only | 7 of 7 |
| `TEMP` only | 0 of 7 |

.NET on Windows reads `TMP` first, so setting only `TEMP` leaves `Q` out of the path. 9.4's 29-of-29 Windows pass is consistent with that. 9.4's record does not say which variable it set.

**Preflight, hosted:** run 36033202756 at `c149541` on `ubuntu-24.04`, image `ubuntu24 20260920.314.1`, SDK 10.0.302. The loop stopped after its first invocation, because the runner's `bash -e` ended it at the first failing case. That invocation's seven cases include one fixture with `Q`, `/tmp/keypaste-drawn-v7uQny`, in `ReplacementEnvValue`, and that case failed. The sidebar TextBlock (text `vault.kdbx`) answered `TextBlockAutomationPeer.GetHelpText` with `/tmp/keypaste-drawn-v7uQny/vault.kdbx`, from `ToolTip.Tip` bound to `VaultPath`, with `AutomationProperties.HelpText` unset. The same text was on the surface before anything was typed.

**Main run:** run 36033421868 at `2626836` (the loop fixed with `set +e`), same runner image and SDK, with the same filtered command.

| Arm | Invocations | Cases | Fixtures with `Q` | Failed | Failed without `Q` | `Q` fixture that passed | Failed with `Q` not on screen before typing |
|---|---|---|---|---|---|---|---|
| as-found | 30 | 210 | 17 | 17 | 0 | 0 | 0 |
| forced-Q (`TMPDIR=/tmp/f17Q`) | 5 | 35 | 35 | 35 | 0 | 0 | 0 |

In as-found, 15 of the 30 invocations failed. Every failure had the same element, property and binding as the preflight's. The confirming arm ran the full `dotnet test keypaste.app.slnx --no-build -c Release` once under `TMPDIR=/tmp/f17Q`: 531 tests, 8 failed. Seven were this theory's cases. The eighth was `An_unlock_field_never_draws_what_is_typed(name: "Password")`, through `NoneAutomationPeer.GetHelpText`, which the trace did not instrument. It reproduced locally with `TMP` containing `Q`. There, the precondition below, run with the old sentinel, named the text as the offered vault path, `…\f17Q\keypaste-app-tests\<hex>\test.kdbx`, present before typing. The one tooltip bound to a vault path on that screen is the recent-list row's StackPanel, `ToolTip.Tip="{Binding Path}"` in `UnlockView.axaml`, and a StackPanel's peer is a `NoneAutomationPeer`. `TempVault` names that directory in lowercase hex, so it can only contain `Q` when the temp root does.

**Conclusion.** No value typed into a secret field reached the automation tree. In all 17 + 35 + 8 failures, the sweep matched a vault path that held `Q` before anything was typed.

**Repair.** In [DrawnMaskTests](../../tests/Keypaste.App.Tests/Rendering/DrawnMaskTests.cs), the pool's first character, and so the whole one-character value, is now `¤`, which no path or label in these fixtures contains. `NeverDrawn` now first asserts that the one-character value is on no surface of the untyped window. Text already on screen now fails as that precondition, naming the surface and the text, instead of as an exposure. [RenderedShell](../../tests/Keypaste.App.Tests/Rendering/RenderedShell.cs) takes a directory prefix. `The_one_character_value_is_on_no_surface_before_typing` opens the shell under the recorded directory name `keypaste-drawn-v7uQny` and asserts that name is on the surface, then runs `NeverDrawn`. With the sentinel reverted to `Q`, that test alone fails: "TextBlockAutomationPeer.GetHelpText holds the one-character value before anything is typed", followed by the path. Restored, it passes.

Locally the class passed 30 of 30, both with the default temp and with `TMP` containing `Q`. Run 36035074198 checked the repair on `ubuntu-24.04`, at `77ff8ec` on the `f17-repair-check` branch: `main` with the repair and the probe workflow, without the instrument, the arms widened to the whole `DrawnMaskTests` class. The as-found arm ran 20 invocations, 600 tests, and none failed. Forced-Q ran 5 invocations, 150 tests, and none failed. The full `dotnet test keypaste.app.slnx --no-build -c Release` under `TMPDIR=/tmp/f17Q` passed 531 tests of 532, with 1 skipped. The job reported failure only because its trace-summary step found no traces on a branch without the instrument.

Verification: `bash scripts/verify.sh` on the finished tree, Windows 10 Pro 19045, ran workflows and desktop and passed both. Desktop ran 532 app tests and 41 consistency tests, and none failed. Scripts, backend and integration were skipped because no changed path maps to them, and `compat` was not run because this step writes no vault content.

The probe branches `f17-probe` and `f17-repair-check` were deleted after their evidence was retained. The runs' downloads are in `artifacts/diagnostics/`, which is not committed.

## Decisions

None. The sentinel binds only this test.

## Limits and follow-ups

The trace instrumented only the shell theory. The unlock case's owning element is inferred from the one tooltip on that screen bound to a vault path, not traced. The app tests' other sweeps look for `SENTINEL-…` strings, or for `MaskedInputAutomationTests.Rare`, `Ж`. No generated temp name contains either, so none of them was changed. The Approve timeout in `DesktopApprovalTests` that 2.6a observed is a separate observation, and this step did not examine it.
