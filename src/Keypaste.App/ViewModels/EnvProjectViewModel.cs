using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>
/// One project's card: its name, how many variables it holds, and its masked table.
/// </summary>
/// <remarks>
/// <para>
/// <b>One reveal at a time, and it lives here.</b> A row asks this object to reveal it, and this
/// object conceals whatever was revealed before. Putting the rule in the view model rather than in
/// the control means "only one value is ever on screen" is assertable with no display — and it means
/// a row cannot reveal itself behind the screen's back.
/// </para>
/// <para>
/// <b>Reading and writing go through <see cref="EnvStore"/>, which is what makes the CLI agree.</b>
/// The group a variable lands in, the rule for a name, the outcome of a set — all of it is core's
/// (D-0014). A screen that wrote to <c>envs/&lt;project&gt;</c> would round-trip through
/// <c>Vault.Open</c> perfectly and be invisible to <c>keypaste env ls</c>, which is exactly the
/// mutation the consistency tests exist to catch.
/// </para>
/// </remarks>
internal sealed class EnvProjectViewModel : ObservableObject, IDisposable
{
    private readonly AppVaultSession _session;
    private readonly Action<string?> _report;
    private readonly Action<string?> _announce;

    private IReadOnlyList<EnvVariableRow> _variables = [];
    private EnvVariableRow? _revealed;
    private EnvVariableRow? _removing;
    private EnvVariableRow? _replacing;
    private bool _isAdding;
    private bool _generateValue = true;
    private string _newKey = string.Empty;

    internal EnvProjectViewModel(
        AppVaultSession session,
        ClipboardCountdown clipboard,
        string name,
        Action<string?> report,
        Action<string?>? announce = null,
        IVaultFilePicker? picker = null,
        ProjectLaunching? launching = null,
        Action? imported = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(report);

        _session = session;
        _report = report;
        _announce = announce ?? (_ => { });
        Clipboard = clipboard;
        Name = name;

        CopyRunCommandCommand = new AsyncRelayCommand(CopyRunCommandAsync);
        BeginAddCommand = new RelayCommand(BeginAdd, () => !IsAdding);
        CancelAddCommand = new RelayCommand(CancelAdd, () => IsAdding);
        ConfirmAddCommand = new RelayCommand(ConfirmAdd, () => IsAdding);
        ConfirmRemoveCommand = new RelayCommand(ConfirmRemove, () => Removing is not null);
        CancelRemoveCommand = new RelayCommand(() => Removing = null, () => Removing is not null);

        NewValue = new SecretField(clipboard);
        ReplacementValue = new SecretField(clipboard);
        ConfirmReplaceCommand = new RelayCommand(ConfirmReplace, () => Replacing is not null);
        CancelReplaceCommand = new RelayCommand(CancelReplace, () => Replacing is not null);

        Import = new EnvImportViewModel(session, name, picker, report, _announce, imported ?? Reload);
        Launch = new ProjectLaunchViewModel(session, name, picker, launching ?? ProjectLaunching.ForThisMachine(), report, _announce);

        Reload();
    }

    /// <summary>The project's name, as the vault holds it. Addresses the set, and must stay
    /// executable in <see cref="RunCommand"/>.</summary>
    internal string Name { get; }

    /// <summary>The name as the card draws it.</summary>
    /// <remarks>
    /// <see cref="RunCommand"/> deliberately keeps <see cref="Name"/> instead: it is a line somebody
    /// pastes into a shell, and a scrubbed one would not run. A group whose name needs scrubbing can
    /// only have been made outside keypaste, and the card above the command shows the drawn form.
    /// </remarks>
    internal string DisplayName => EntryNameSanitizer.Sanitize(Name).Text;

    /// <summary>Imports a <c>.env</c> into this project.</summary>
    internal EnvImportViewModel Import { get; }

    /// <summary>Where this project runs on this machine, and its Run and Open terminal.</summary>
    internal ProjectLaunchViewModel Launch { get; }

    /// <summary>The clipboard a row copies through.</summary>
    internal ClipboardCountdown Clipboard { get; }

    /// <summary>The variables, masked.</summary>
    internal IReadOnlyList<EnvVariableRow> Variables
    {
        get => _variables;
        private set
        {
            if (Set(ref _variables, value))
            {
                Raise(nameof(Count));
                Raise(nameof(Summary));
            }
        }
    }

    /// <summary>How many variables this project holds.</summary>
    internal int Count => _variables.Count;

    /// <summary>What the card says under its name.</summary>
    internal string Summary => Count == 1 ? "1 variable" : $"{Count} variables";

    /// <summary>The command that injects this project, for the copy helper and the card.</summary>
    /// <remarks>
    /// The trailing space is deliberate: it is a line somebody finishes typing, not one they run.
    /// The prompt for 4.2 spells it exactly this way, and <c>docs/demo.md</c> shows the same shape.
    /// </remarks>
    internal string RunCommand => $"keypaste run {Name} -- ";

    /// <summary>Which variable is revealed right now, by name. Never its value.</summary>
    /// <remarks>
    /// Exposed so a test can assert that revealing is single and transient without reaching into a
    /// control. A key is already on screen; a value is what must not be.
    /// </remarks>
    internal string? RevealedKey => _revealed?.Key;

    /// <summary>The variable a confirmation is pending for, or null.</summary>
    internal EnvVariableRow? Removing
    {
        get => _removing;
        private set
        {
            if (Set(ref _removing, value))
            {
                Raise(nameof(RemovePrompt));
                Raise(nameof(IsRemoving));
                ConfirmRemoveCommand.RaiseCanExecuteChanged();
                CancelRemoveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    internal bool IsRemoving => _removing is not null;

    /// <summary>The variable whose value is being replaced, or null.</summary>
    /// <remarks>
    /// One at a time, on the project rather than on each row, for the reason
    /// <see cref="Removing"/> is shaped this way and <see cref="RevealedKey"/> before it: a form
    /// per row would mean one live <see cref="SecretField"/> per variable, and a screen holding
    /// thirty buffers in order to use one of them.
    /// </remarks>
    internal EnvVariableRow? Replacing
    {
        get => _replacing;
        private set
        {
            if (Set(ref _replacing, value))
            {
                Raise(nameof(ReplacePrompt));
                Raise(nameof(IsReplacing));
                ConfirmReplaceCommand.RaiseCanExecuteChanged();
                CancelReplaceCommand.RaiseCanExecuteChanged();
            }
        }
    }

    internal bool IsReplacing => _replacing is not null;

    /// <summary>What the replace form is headed with.</summary>
    internal string ReplacePrompt => _replacing is { } row
        ? $"New value for {row.DisplayKey}. The old one stays in this entry history."
        : string.Empty;

    /// <summary>The value being entered for a new variable, when it is not being generated.</summary>
    internal SecretField NewValue { get; }

    /// <summary>The value replacing an existing variable value.</summary>
    internal SecretField ReplacementValue { get; }

    /// <summary>Whether to generate the new variable value rather than take one already in hand.</summary>
    /// <remarks>
    /// On by default, so adding a variable behaves exactly as it did before 4.9. Turning it off
    /// shows <see cref="NewValue"/>, which is how somebody stores a key a provider gave them.
    /// </remarks>
    internal bool GenerateValue
    {
        get => _generateValue;
        set
        {
            if (Set(ref _generateValue, value) && value)
            {
                NewValue.Clear();
            }
        }
    }

    /// <summary>What to generate, while <see cref="GenerateValue"/> is on.</summary>
    internal GeneratorViewModel Generator { get; } = new();

    /// <summary>Replaces the value, having been given one.</summary>
    internal RelayCommand ConfirmReplaceCommand { get; }

    internal RelayCommand CancelReplaceCommand { get; }

    /// <summary>Opens the replace form for one row.</summary>
    /// <param name="row">The variable whose value is being replaced.</param>
    internal void BeginReplace(EnvVariableRow row)
    {
        ReplacementValue.Clear();
        Replacing = row;
        _report(null);
    }

    /// <summary>What the confirmation asks, and whether the variable can come back.</summary>
    /// <remarks>
    /// The vault's recycle-bin setting decides, not this screen: the same question has two honest
    /// answers depending on the file. A recycled variable is recovered on the Trash screen.
    /// </remarks>
    internal string RemovePrompt
    {
        get
        {
            if (_removing is not { } row)
            {
                return string.Empty;
            }

            return _session.Unlocked?.RecyclesDeletedEntries == true
                ? $"Remove {row.DisplayKey} from {DisplayName}? It goes to the vault's recycle bin."
                : $"Remove {row.DisplayKey} from {DisplayName}? There is no undo.";
        }
    }

    internal bool IsAdding
    {
        get => _isAdding;
        private set
        {
            if (Set(ref _isAdding, value))
            {
                BeginAddCommand.RaiseCanExecuteChanged();
                CancelAddCommand.RaiseCanExecuteChanged();
                ConfirmAddCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>The name of the variable being added.</summary>
    internal string NewKey
    {
        get => _newKey;
        set => Set(ref _newKey, value);
    }

    internal AsyncRelayCommand CopyRunCommandCommand { get; }

    internal RelayCommand BeginAddCommand { get; }

    internal RelayCommand CancelAddCommand { get; }

    internal RelayCommand ConfirmAddCommand { get; }

    internal RelayCommand ConfirmRemoveCommand { get; }

    internal RelayCommand CancelRemoveCommand { get; }

    /// <summary>Reads the project again.</summary>
    internal void Reload()
    {
        if (_session.Unlocked is not { } vault)
        {
            Variables = [];

            // A half-entered value is as much a secret as a stored one, and the screen is about to
            // be disposed anyway. Clearing here means the lock holds on whichever path runs.
            NewValue.Clear();
            ReplacementValue.Clear();
            IsAdding = false;
            Replacing = null;
            return;
        }

        try
        {
            Variables =
            [
                .. new EnvStore(vault)
                    .Read(Name)
                    .Select(variable => new EnvVariableRow(
                        this,
                        variable.Key,
                        variable.Value.Length,
                        variable.IsUsableName))
            ];
        }
        catch (VaultException e)
        {
            // A project holding two variables of the same name is a file KeePassXC can make and
            // keypaste will not guess about. Core says so; this repeats it rather than hiding it.
            Variables = [];
            _report(e.Message);
        }
    }

    /// <summary>Hands a row its value, and takes it away from whichever row had it.</summary>
    internal string? Reveal(EnvVariableRow row)
    {
        if (!ReferenceEquals(_revealed, row))
        {
            _revealed = row;
            Raise(nameof(RevealedKey));
        }

        return Read(row.Key);
    }

    /// <summary>Notes that a row's hold ended.</summary>
    internal void Conceal(EnvVariableRow row)
    {
        if (ReferenceEquals(_revealed, row))
        {
            _revealed = null;
            Raise(nameof(RevealedKey));
        }
    }

    /// <summary>Reads one value out of the open vault, for a copy or a hold.</summary>
    internal string? Read(string key)
    {
        if (_session.Unlocked is not { } vault)
        {
            return null;
        }

        foreach (var variable in new EnvStore(vault).Read(Name))
        {
            if (string.Equals(variable.Key, key, StringComparison.Ordinal))
            {
                return variable.Value;
            }
        }

        return null;
    }

    /// <summary>Passes a message up to the screen, which draws the banner.</summary>
    internal void Report(string? message) => _report(message);

    /// <summary>Asks to remove a variable.</summary>
    internal void BeginRemove(EnvVariableRow row) => Removing = row;

    /// <summary>Nothing read out of the vault outlives this.</summary>
    public void Dispose()
    {
        _revealed = null;
        Variables = [];
        Removing = null;
        Replacing = null;
        NewValue.Dispose();
        ReplacementValue.Dispose();
        Import.Dispose();
        Launch.Dispose();
    }

    private async Task CopyRunCommandAsync() =>
        await Clipboard.CopyPlainAsync(RunCommand, "Run command").ConfigureAwait(true);

    private void BeginAdd()
    {
        NewKey = string.Empty;
        NewValue.Clear();
        IsAdding = true;
        _report(null);
    }

    private void CancelAdd()
    {
        IsAdding = false;
        NewKey = string.Empty;
        NewValue.Clear();
        _report(null);
    }

    private void CancelReplace()
    {
        Replacing = null;
        ReplacementValue.Clear();
        _report(null);
    }

    private void ConfirmAdd()
    {
        if (_session.Unlocked is not { } vault)
        {
            _report("The vault is locked.");
            return;
        }

        var key = NewKey.Trim();

        // Core's rule, not one written next to this error message. keypaste env set refuses the
        // same names for the same reasons, and the two must not drift.
        if (!EnvConvention.IsValidKey(key, out var invalid))
        {
            _report(invalid);
            return;
        }

        var value = string.Empty;

        try
        {
            if (GenerateValue)
            {
                if (Generator.Recipe is not { } recipe)
                {
                    _report(Generator.Error ?? string.Empty);
                    return;
                }

                using var buffer = new SecretBuffer();
                PasswordGenerator.Append(recipe, buffer);
                value = new string(buffer.Value);
            }
            else
            {
                value = NewValue.Compose();
            }

            var store = new EnvStore(vault);

            if (store.TrySet(Name, key, value, out var rejection) == EnvSetOutcome.Rejected)
            {
                _report(rejection);
                return;
            }

            vault.Save();
        }
        catch (VaultChangedOnDiskException)
        {
            _report("Something else changed this vault since you opened it. Lock and unlock to see it, then add this again.");
            return;
        }
        catch (VaultException e)
        {
            _report(e.Message);
            return;
        }

        IsAdding = false;
        NewKey = string.Empty;
        NewValue.Clear();
        _report(null);
        Reload();
    }

    /// <summary>
    /// Writes a new value over an existing variable, keeping the old one in history.
    /// </summary>
    /// <remarks>
    /// Straight through <see cref="EnvStore.TrySet"/>, whose update branch goes to
    /// <c>Vault.UpdateEntry</c> and therefore to <c>CreateBackup</c> — which is what keeps the
    /// replaced value in KeePass history (D-0014) without this screen knowing anything about it.
    /// </remarks>
    private void ConfirmReplace()
    {
        if (Replacing is not { } row)
        {
            return;
        }

        if (_session.Unlocked is not { } vault)
        {
            _report("The vault is locked.");
            return;
        }

        var value = ReplacementValue.Compose();

        try
        {
            var store = new EnvStore(vault);

            switch (store.TrySet(Name, row.Key, value, out var rejection))
            {
                case EnvSetOutcome.Rejected:
                    _report(rejection);
                    return;

                case EnvSetOutcome.Created:
                    // TrySet created it, so it was not there to replace: something removed it
                    // while this form was open. Say so rather than report a replacement.
                    _report($"{row.DisplayKey} was not in {DisplayName} any more, so it was added.");
                    break;

                default:
                    break;
            }

            vault.Save();
        }
        catch (VaultChangedOnDiskException)
        {
            _report("Something else changed this vault since you opened it. Lock and unlock to see it, then replace this again.");
            return;
        }
        catch (VaultException e)
        {
            _report(e.Message);
            return;
        }

        // The one length that changed, rather than Reload, which reads every value in the project
        // back out of the vault to recompute lengths it already knows.
        row.Resize(value.Length);

        Replacing = null;
        ReplacementValue.Clear();
    }

    private void ConfirmRemove()
    {
        if (Removing is not { } row)
        {
            return;
        }

        if (_session.Unlocked is not { } vault)
        {
            _report("The vault is locked.");
            return;
        }

        DeletionOutcome outcome;

        try
        {
            outcome = new EnvStore(vault).Remove(Name, row.Key);

            if (outcome == DeletionOutcome.NothingMatched)
            {
                _report($"{row.DisplayKey} is not in {DisplayName} any more.");
                Removing = null;
                Reload();
                return;
            }

            vault.Save();
        }
        catch (VaultChangedOnDiskException)
        {
            _report("Something else changed this vault since you opened it. Lock and unlock to see it, then remove this again.");
            return;
        }
        catch (VaultException e)
        {
            _report(e.Message);
            return;
        }

        Removing = null;
        _report(null);

        // Reported after the act, from the outcome core returned, as the CLI reports it.
        _announce(outcome == DeletionOutcome.Recycled
            ? $"Moved {row.DisplayKey} to the trash. Restore it there."
            : $"Removed {row.DisplayKey}. This vault has no recycle bin, so nothing can put it back.");

        Reload();
    }
}
