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

    private IReadOnlyList<EnvVariableRow> _variables = [];
    private EnvVariableRow? _revealed;
    private EnvVariableRow? _removing;
    private bool _isAdding;
    private string _newKey = string.Empty;

    internal EnvProjectViewModel(
        AppVaultSession session,
        ClipboardCountdown clipboard,
        string name,
        Action<string?> report)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(report);

        _session = session;
        _report = report;
        Clipboard = clipboard;
        Name = name;

        CopyRunCommandCommand = new AsyncRelayCommand(CopyRunCommandAsync);
        BeginAddCommand = new RelayCommand(BeginAdd, () => !IsAdding);
        CancelAddCommand = new RelayCommand(CancelAdd, () => IsAdding);
        ConfirmAddCommand = new RelayCommand(ConfirmAdd, () => IsAdding);
        ConfirmRemoveCommand = new RelayCommand(ConfirmRemove, () => Removing is not null);
        CancelRemoveCommand = new RelayCommand(() => Removing = null, () => Removing is not null);

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

    /// <summary>What the confirmation asks.</summary>
    internal string RemovePrompt => _removing is { } row
        ? $"Remove {row.DisplayKey} from {DisplayName}? There is no undo."
        : string.Empty;

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
    }

    private async Task CopyRunCommandAsync() =>
        await Clipboard.CopyPlainAsync(RunCommand, "Run command").ConfigureAwait(true);

    private void BeginAdd()
    {
        NewKey = string.Empty;
        IsAdding = true;
        _report(null);
    }

    private void CancelAdd()
    {
        IsAdding = false;
        NewKey = string.Empty;
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

        // Generated, because typing a secret needs a field that accumulates one and this app does
        // not have one. `keypaste env set` prompts for a value without putting it in a window.
        var value = string.Empty;

        try
        {
            using (var buffer = new SecretBuffer())
            {
                PasswordGenerator.Append(PasswordRecipe.Default, buffer);
                value = new string(buffer.Value);
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
        _report(null);
        Reload();
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

        try
        {
            new EnvStore(vault).Remove(Name, row.Key);
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
        Reload();
    }
}
