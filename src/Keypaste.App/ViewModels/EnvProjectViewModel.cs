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
    private readonly IVaultFilePicker? _picker;

    private IReadOnlyList<EnvVariableRow> _variables = [];
    private IReadOnlyList<EnvProfileColumn> _columns = [];
    private IReadOnlyList<EnvKeyRow> _rows = [];
    private IReadOnlyList<EnvProfileInfo> _profiles = [];
    private IReadOnlyList<string> _profileProblems = [];
    private EnvMatrix? _matrix;
    private string _selectedProfile = EnvProfileNames.Default;
    private EnvVariableRow? _revealed;
    private EnvVariableRow? _removing;
    private EnvVariableRow? _replacing;
    private bool _isAdding;
    private bool _generateValue = true;
    private string _newKey = string.Empty;
    private bool _isAddingProfile;
    private string _newProfile = string.Empty;

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
        _picker = picker;
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
        ExportReferencesCommand = new AsyncRelayCommand(ExportReferencesAsync, () => _picker is not null);
        BeginAddProfileCommand = new RelayCommand(BeginAddProfile, () => !IsAddingProfile);
        ConfirmAddProfileCommand = new RelayCommand(ConfirmAddProfile, () => IsAddingProfile);
        CancelAddProfileCommand = new RelayCommand(() => IsAddingProfile = false, () => IsAddingProfile);

        Import = new EnvImportViewModel(session, name, picker, report, _announce, imported ?? Reload);
        Launch = new ProjectLaunchViewModel(session, name, picker, launching ?? ProjectLaunching.ForThisMachine(), report, _announce);
        Launch.PropertyChanged += OnLaunchChanged;

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

    /// <summary>The command that injects the selected profile, for the copy helper and the card.</summary>
    /// <remarks>
    /// It names the project as well as the profile, so it runs from any directory and not only the
    /// one <c>projects.json</c> maps; the command is the mapped one, or <c>npm start</c> to edit.
    /// </remarks>
    internal string RunCommand => $"keypaste run -p {SelectedProfile} {Name} -- {Launch.Mapping?.Command ?? "npm start"}";

    /// <summary>The project's profiles: <c>dev</c> first, protected ones last.</summary>
    internal IReadOnlyList<EnvProfileInfo> Profiles
    {
        get => _profiles;
        private set => Set(ref _profiles, value);
    }

    /// <summary>
    /// The matrix columns and the preview's toggle: <see cref="Profiles"/>, plus the selected
    /// profile while it has no key yet, placed before any protected one.
    /// </summary>
    internal IReadOnlyList<EnvProfileColumn> Columns
    {
        get => _columns;
        private set => Set(ref _columns, value);
    }

    /// <summary>Every key against <see cref="Columns"/>. Holds no value.</summary>
    internal IReadOnlyList<EnvKeyRow> Rows
    {
        get => _rows;
        private set
        {
            if (Set(ref _rows, value))
            {
                Raise(nameof(HasRows));
            }
        }
    }

    internal bool HasRows => _rows.Count > 0;

    /// <summary>Whether the selected profile is protected, so every release of it is asked live (D-0348).</summary>
    internal bool SelectedIsProtected => EnvProfileNames.IsProtected(SelectedProfile);

    /// <summary>Whether the new-profile form is open.</summary>
    internal bool IsAddingProfile
    {
        get => _isAddingProfile;
        private set
        {
            if (Set(ref _isAddingProfile, value))
            {
                BeginAddProfileCommand.RaiseCanExecuteChanged();
                ConfirmAddProfileCommand.RaiseCanExecuteChanged();
                CancelAddProfileCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>The name of the profile being started.</summary>
    internal string NewProfile
    {
        get => _newProfile;
        set => Set(ref _newProfile, value);
    }

    internal RelayCommand BeginAddProfileCommand { get; }

    /// <summary>Selects the new profile and opens the add form, since its first key is what creates it.</summary>
    internal RelayCommand ConfirmAddProfileCommand { get; }

    internal RelayCommand CancelAddProfileCommand { get; }

    /// <summary>Asks where to save <see cref="ReferencePreview"/> and writes it there.</summary>
    internal AsyncRelayCommand ExportReferencesCommand { get; }

    /// <summary>The subgroups that are never read, in keypaste's words.</summary>
    internal IReadOnlyList<string> ProfileProblems
    {
        get => _profileProblems;
        private set => Set(ref _profileProblems, value);
    }

    /// <summary>Every key against every profile. Holds no value.</summary>
    internal EnvMatrix? Matrix
    {
        get => _matrix;
        private set => Set(ref _matrix, value);
    }

    /// <summary>The profile the table shows and every add, replace, remove, import and launch goes to.</summary>
    /// <remarks>A profile that does not exist yet may be chosen: the first variable added to it creates it, as <c>env set -p</c> does.</remarks>
    internal string SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!EnvProfileNames.IsValid(value, out var invalid))
            {
                _report(invalid);
                return;
            }

            if (Set(ref _selectedProfile, value))
            {
                Raise(nameof(SelectedIsProtected));
                Import.Profile = value;
                Launch.Profile = value;
                _revealed = null;
                Raise(nameof(RevealedKey));
                Removing = null;
                CancelReplace();
                Reload();
            }
        }
    }

    /// <summary>The <c>.env.keypaste</c> the selected profile exports: references only, safe to commit.</summary>
    internal string ReferencePreview => EnvReferenceFile.Format(Name, SelectedProfile, [.. Variables.Select(row => row.Key)]);

    /// <summary>The preview a line at a time, as the terminal panel draws it.</summary>
    internal IReadOnlyList<EnvPreviewLine> ReferenceLines =>
        [.. ReferencePreview.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => new EnvPreviewLine(line))];

    /// <summary>Writes <see cref="ReferencePreview"/> to a file, never a value.</summary>
    /// <param name="path">The file.</param>
    /// <param name="replace">Whether an existing file may be replaced.</param>
    /// <returns>What happened, in a sentence the screen shows.</returns>
    internal string ExportReferences(string path, bool replace = false) => Export(path, replace, out _);

    private string Export(string path, bool replace, out bool written)
    {
        ArgumentNullException.ThrowIfNull(path);
        written = false;

        if (_session.Unlocked is not { } vault)
        {
            return "The vault is locked.";
        }

        var target = Path.GetFullPath(path);

        if ((_session.VaultPath is { } vaultPath && PathIdentity.SameFile(vaultPath, target))
            || (File.Exists(target) && KdbxHeader.IsVaultFile(target)))
        {
            return $"{target} is a vault, so nothing was written. Choose another file.";
        }

        if (File.Exists(target) && !replace)
        {
            return $"{target} already exists, so nothing was written. Replace it to overwrite it.";
        }

        var variables = new EnvStore(vault).Read(Name, SelectedProfile);

        if (!EnvNameRules.TryCheck(variables, out var names))
        {
            return $"{EnvProfileNames.GroupPath(Name, SelectedProfile)} {names}";
        }

        var text = EnvReferenceFile.Format(Name, SelectedProfile, [.. variables.Select(variable => variable.Key)], Path.GetFileName(target));

        if (!EnvReferenceFile.TryWrite(target, text, replace, out var writeError))
        {
            return $"{target} could not be written: {writeError}";
        }

        written = true;
        return $"Wrote {(variables.Count == 1 ? "1 reference" : $"{variables.Count} references")} to {target}. It holds no value and is safe to commit.";
    }

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
            Profiles = [];
            ProfileProblems = [];
            Matrix = null;
            Columns = [];
            Rows = [];

            // A half-entered value is as much a secret as a stored one, and the screen is about to
            // be disposed anyway. Clearing here means the lock holds on whichever path runs.
            NewValue.Clear();
            ReplacementValue.Clear();
            IsAdding = false;
            Replacing = null;
            RaiseProfileText();
            return;
        }

        var store = new EnvStore(vault);
        Profiles = store.Profiles(Name);
        ProfileProblems = store.ProfileProblems(Name);
        Matrix = EnvMatrix.Build(vault, Name, _session.Clock);

        try
        {
            Variables =
            [
                .. store
                    .Read(Name, SelectedProfile)
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

        Layout();
        RaiseProfileText();
    }

    private void Layout()
    {
        var profiles = Profiles.ToList();

        if (!profiles.Any(profile => string.Equals(profile.Name, SelectedProfile, StringComparison.Ordinal)))
        {
            var pending = new EnvProfileInfo(SelectedProfile, EnvProfileNames.IsProtected(SelectedProfile));
            var firstProtected = profiles.FindIndex(profile => profile.IsProtected);
            profiles.Insert(pending.IsProtected || firstProtected < 0 ? profiles.Count : firstProtected, pending);
        }

        Columns =
        [
            .. profiles.Select(profile => new EnvProfileColumn(
                profile.Name,
                profile.IsProtected,
                string.Equals(profile.Name, SelectedProfile, StringComparison.Ordinal),
                new RelayCommand(() => SelectedProfile = profile.Name))),
        ];

        var selected = Variables.ToDictionary(row => row.Key, StringComparer.Ordinal);
        var keys = (Matrix?.Rows.Select(row => row.Key) ?? []).Union(selected.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal);

        Rows =
        [
            .. keys.Select(key => new EnvKeyRow(key, [.. profiles.Select(profile => Cell(key, profile.Name))])),
        ];

        EnvProfileCell Cell(string key, string profile)
        {
            var known = Matrix?.Row(key)?.Cells.FirstOrDefault(cell => string.Equals(cell.Profile, profile, StringComparison.Ordinal));
            var isSelected = string.Equals(profile, SelectedProfile, StringComparison.Ordinal);
            var variable = isSelected ? selected.GetValueOrDefault(key) : null;
            var state = known?.State ?? (variable is null ? EnvCellState.Missing : EnvCellState.Set);

            return new EnvProfileCell(
                profile,
                state,
                known?.Problem,
                known?.SameValueAs ?? [],
                variable,
                new RelayCommand(() => Act(key, profile, state)));
        }
    }

    private void Act(string key, string profile, EnvCellState state)
    {
        SelectedProfile = profile;

        if (state == EnvCellState.Missing && string.Equals(SelectedProfile, profile, StringComparison.Ordinal))
        {
            BeginAdd();
            NewKey = key;
        }
    }

    private void BeginAddProfile()
    {
        NewProfile = string.Empty;
        IsAddingProfile = true;
        _report(null);
    }

    private void ConfirmAddProfile()
    {
        var profile = NewProfile.Trim();

        if (!EnvProfileNames.IsValid(profile, out var invalid))
        {
            _report(invalid);
            return;
        }

        IsAddingProfile = false;
        NewProfile = string.Empty;
        SelectedProfile = profile;
        BeginAdd();
    }

    private async Task ExportReferencesAsync()
    {
        if (_picker is null || await _picker.PickReferenceFileAsync(EnvReferenceFile.FileName, Launch.Mapping?.Directory).ConfigureAwait(true) is not { } path)
        {
            return;
        }

        // The save dialog has already asked before replacing a file, so its answer is the person's.
        var message = Export(path, replace: true, out var written);

        if (written)
        {
            _report(null);
            _announce(message);
        }
        else
        {
            _report(message);
        }
    }

    private void OnLaunchChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectLaunchViewModel.Mapping))
        {
            Raise(nameof(RunCommand));
        }
    }

    private void RaiseProfileText()
    {
        Raise(nameof(ReferencePreview));
        Raise(nameof(ReferenceLines));
        Raise(nameof(RunCommand));
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

        foreach (var variable in new EnvStore(vault).Read(Name, SelectedProfile))
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
        Profiles = [];
        ProfileProblems = [];
        Matrix = null;
        Columns = [];
        Rows = [];
        Removing = null;
        Replacing = null;
        Launch.PropertyChanged -= OnLaunchChanged;
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

            if (store.TrySet(Name, SelectedProfile, key, value, out var rejection) == EnvSetOutcome.Rejected)
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
    /// Straight through <see cref="EnvStore.TrySet(string, string, string, out string)"/>, whose update branch goes to
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

            switch (store.TrySet(Name, SelectedProfile, row.Key, value, out var rejection))
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

        if (_session.Unlocked is { } current)
        {
            Matrix = EnvMatrix.Build(current, Name, _session.Clock);
            Layout();
        }

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
            outcome = new EnvStore(vault).Remove(Name, SelectedProfile, row.Key);

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
