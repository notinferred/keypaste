# K.12 — The view models on CommunityToolkit.Mvvm

Completed 2026-10-31 at `aa51f79c` on `k12`; runs ci 41251799496 and app 41251806377 at that commit. The integrated commit differs from `aa51f79c` only in this record.

## Amendments

- With the plan (founder, 2026-10-31, D-0680):
  - **Dependents are declared as the row says.** Each stored property names every property and command that reads it, directly or through another computed property.
  - **The 15 tests that check what a change notifies stay.** They are what catches a missing declaration.
  - **The whole `Keypaste.App.Tests` project runs on the founder's Mac**, in a few bursts.

## What changed

- **The package.**
  - CommunityToolkit.Mvvm 8.4.2 (MIT, .NET Foundation, no dependencies on net8.0 and later) is referenced by the App alone, with `PropertyChanging` support off.
  - `src/Keypaste.Mvvm` is gone, with its reference rule, its ban list, its solution entry, `app.yml`'s path and signature-list entries, `sign-windows.sh`' first-party list entry and the publisher check's library.
- **Properties.**
  - The 219 plain properties are `[ObservableProperty]` partial properties carrying their initializers.
  - Their `NotifyPropertyChangedFor` and `NotifyCanExecuteChangedFor` lists are the transitive closure of the `DependsOn` graph they replace, generated from it rather than copied. The 481 `DependsOn` lines are gone.
  - A setter with logic stays hand-written with `SetProperty` and notifies its readers inside its `if`, in the order `Raise` sent them. A property raised with `OnPropertyChanged` notifies its readers beside that call.
  - `SkipHeld` became generated with `OnSkipHeldChanged`, and the import row's `Problem` and `Blocks` became generated too.
  - The approval and pairing prompts' own allow commands read the base's `IsArmed` and `IsAnswered`, and are asked again through `PromptViewModel.OnAllowsChanged`.
- **Commands.**
  - The 33 commands that run a method of their own name are `[RelayCommand]`. The rest are still built in their constructors, now from the toolkit's classes.
  - **The toolkit's `Execute` and `ExecuteAsync` run whatever `CanExecute` says**, where `Keypaste.Mvvm`'s refused. `Commands.Press` runs a command only while it can. The menu, the tray, the shell's and sharing's chains and the views' Enter and Escape handlers now press through it. The tests press the same way, with `Pressing.PressAsync` for an awaited command.
  - An async command still runs once at a time, because `CanExecute` is false while it runs.
- **Constructors.** `EntryDetailViewModel`, `SharingViewModel` and `RestoreBackupViewModel` set a property before the command it notifies existed, and the generated setter calls that command. They now build their commands first.
- **Deleted tests.** These restated the library:
  - `ObservableObjectTests` and `RelayCommandTests`;
  - `PublisherMetadataTests.Its_bindings_are_published_as_the_project`;
  - `CliLinkSettingsTests.A_link_under_way_cannot_be_started_again`.
- **C# line counts against `main`:**

  | Area | Added | Removed | Net |
  |---|---|---|---|
  | `src/Keypaste.App` | 1,631 | 2,565 | −934 |
  | `src/Keypaste.Mvvm` | 0 | 269 | −269 |
  | `tests` | 686 | 1,006 | −320 |
  | In total | 2,317 | 3,840 | −1,523 |

  The plan's estimate was −1,800. The explicit notifications came to about 250 lines against the 60 to 100 estimated.

## Evidence

- **Parity.** A scratchpad script compared every (source → reader) pair of `main`'s `DependsOn` closure with what the new code notifies:
  - the attributes;
  - the `On<Name>Changed` bodies;
  - each hand-written setter;
  - the calls beside each `OnPropertyChanged` of the source or `SetProperty` naming it.

  Every pair is notified except 8 it cannot see, each read by hand:
  - `AgentSettingsViewModel.HeldBack` reaches `UseVaultCommand` through `Read`'s list of everything it sets, which now also asks the commands those properties reach again.
  - `IsArmed` and `IsAnswered` reach `AlwaysForCommand` and the pairing prompt's `AllowCommand` through `OnAllowsChanged`.
  - `EntriesViewModel.VaultName` was raised by neither version.
- **On the founder's Mac** (macOS 26.6, arm64, SDK 10.0.302):
  - `verify.sh compile` passed.
  - `verify.sh test tests/Keypaste.App.Tests` took three runs:
    1. Stopped after 10 minutes with 262 failures, 259 of them the constructors' `NullReferenceException`, and a test waiting for a second replacement its gate held.
    2. Stopped after 9 minutes on another such wait, with one failure: a sharing test whose create only the old command refused.
    3. Once the tests pressed commands, passed 1,317 tests, 3 skipped, in 1 min 23 s.

    Nothing was left running after any of them.
- **The package.** Its `.nupkg` hashes as nuget.org serves it, and the lock files hold its content hash.
- **CI at `aa51f79c`:**
  - ci 41251799496 passed every job.
  - app 41251806377 passed the desktop tests, the desktop gates, which drive the app through the driver, and publish and selftest on all three runners. It failed only `keepassxc first run (macos-15)`, as F.74 does: KeePassXC exited before opening the vaults after the Qt font-alias warning.

## Decisions

D-0680. D-0427's approver-frame encoding stands.

## Limits and follow-ups

- **A computed property added later** must be named on every stored property it reads, directly or not. The 15 notification tests check that.
- **A wrong-typed parameter now throws.** The toolkit's `RelayCommand<T>` throws on a parameter of the wrong type, where `Keypaste.Mvvm` passed null. No test met one, the headless view tests included.
