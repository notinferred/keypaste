using System.Windows.Input;
using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Approval;

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
/// <b>Reading goes through <see cref="EnvResolution"/> and writing through <see cref="EnvStore"/>,
/// which is what makes the CLI agree.</b> Where a key lives, where a new one lands, the rule for a
/// name and the outcome of a write are all core's (D-0413). A screen that wrote anywhere else would
/// round-trip through <c>Vault.Open</c> perfectly and be invisible to <c>keypaste run</c>, which is
/// exactly the mutation the consistency tests exist to catch.
/// </para>
/// </remarks>
internal sealed partial class EnvProjectViewModel : ObservableObject, IDisposable
{
    private const int _shownCandidates = 50;

    private readonly AppVaultSession _session;
    private readonly Action<string?> _report;
    private readonly Action<string?, ToastTone> _announce;
    private readonly IVaultFilePicker? _picker;
    private readonly Action _projectsChanged;

    private ProjectCatalog? _catalog;
    private EnvEntryCandidate? _chosenEntry;
    private IReadOnlyList<string> _referenceKeys = [];
    private string _selectedProfile = EnvProfileNames.Default;
    private bool _generateValue = true;

    internal EnvProjectViewModel(
        AppVaultSession session,
        ClipboardCountdown clipboard,
        string name,
        Action<string?> report,
        Action<string?, ToastTone>? announce = null,
        IVaultFilePicker? picker = null,
        ProjectLaunching? launching = null,
        Action? imported = null,
        Action? projectsChanged = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(report);

        _session = session;
        _report = report;
        _announce = announce ?? ((_, _) => { });
        _picker = picker;
        _projectsChanged = projectsChanged ?? (() => { });
        Clipboard = clipboard;
        Name = name;

        BeginAddCommand = new RelayCommand(BeginAdd, () => !IsAdding);
        CancelAddCommand = new RelayCommand(CancelAdd, () => IsAdding);
        ConfirmAddCommand = new RelayCommand(ConfirmAdd, () => IsAdding);
        ConfirmRemoveCommand = new RelayCommand(ConfirmRemove, () => Removing is not null);
        CancelRemoveCommand = new RelayCommand(() => Removing = null, () => Removing is not null);

        NewValue = new SecretField(clipboard);
        ReplacementValue = new SecretField(clipboard);
        GenerateCommand = new RelayCommand(() => GenerateValue = true);
        GenerateIntoCommand = new RelayCommand<SecretField>(GenerateInto);

        // A generated value is in its field, where it can be seen and replaced, and follows the choices (V.12).
        NewValue.GeneratedValueReplaced += (_, _) => SetProperty(ref _generateValue, false, nameof(GenerateValue));
        Generator.PropertyChanged += (_, _) =>
        {
            if (_generateValue && IsAdding)
            {
                GenerateInto(NewValue);
            }
            else if (ReplacementValue.IsGenerated)
            {
                GenerateInto(ReplacementValue);
            }
        };
        ConfirmReplaceCommand = new RelayCommand(ConfirmReplace, () => Replacing is not null);
        CancelReplaceCommand = new RelayCommand(CancelReplace, () => Replacing is not null);
        ExportReferencesCommand = new AsyncRelayCommand(ExportReferencesAsync, () => _picker is not null);
        BeginAddProfileCommand = new RelayCommand(BeginAddProfile, () => !IsAddingProfile);
        ConfirmAddProfileCommand = new RelayCommand(ConfirmAddProfile, () => IsAddingProfile);
        CancelAddProfileCommand = new RelayCommand(() => IsAddingProfile = false, () => IsAddingProfile);
        ConfirmTagChangeCommand = new RelayCommand(ConfirmTagChange, () => PendingTagChange is not null);
        CancelTagChangeCommand = new RelayCommand(CancelTagChange, () => PendingTagChange is not null);

        Import = new EnvImportViewModel(session, name, picker, report, _announce, imported ?? Reload);
        Launch = new ProjectLaunchViewModel(session, name, picker, launching ?? ProjectLaunching.ForThisMachine(authority: null), report, _announce);
        Launch.PropertyChanged += OnLaunchChanged;

        Reload();
    }

    /// <summary>The project's name, as the vault holds it. Addresses the set, and must stay
    /// executable in <see cref="RunCommand"/>.</summary>
    internal string Name { get; }

    /// <summary>The name as the card draws it.</summary>
    /// <remarks>
    /// <see cref="RunCommand"/> deliberately keeps <see cref="Name"/> instead: it is a line somebody
    /// pastes into a shell, and a scrubbed one would not run. A project whose name needs scrubbing can
    /// only have been tagged outside keypaste, and the card above the command shows the drawn form.
    /// </remarks>
    internal string DisplayName => EntryNameSanitizer.Sanitize(Name).Text;

    /// <summary>Imports a <c>.env</c> into this project.</summary>
    internal EnvImportViewModel Import { get; }

    /// <summary>Where this project runs on this machine, and its Run and Open terminal.</summary>
    internal ProjectLaunchViewModel Launch { get; }

    /// <summary>The clipboard a row copies through.</summary>
    internal ClipboardCountdown Clipboard { get; }

    /// <summary>The variables, masked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Count), nameof(Summary))]
    internal partial IReadOnlyList<EnvVariableRow> Variables { get; private set; } = [];

    /// <summary>How many variables this project holds.</summary>
    internal int Count => Variables.Count;

    /// <summary>What the card says under its name.</summary>
    internal string Summary => Count == 1 ? "1 variable" : $"{Count} variables";

    /// <summary>The command that injects the selected profile, for the copy helper and the card, or null when the project's name gets none.</summary>
    /// <remarks>
    /// It names the project as well as the profile, so it runs from any directory and not only the
    /// one <c>projects.json</c> maps; the command is the mapped one, or <c>npm start</c> to edit.
    /// </remarks>
    internal string? RunCommand => CopiedRunCommand.For(Name, SelectedProfile, Launch.Mapping?.Command ?? "npm start");

    /// <summary>Whether the card offers <see cref="RunCommand"/>, which a name a terminal could run never gets (D-0453).</summary>
    internal bool HasRunCommand => CopiedRunCommand.IsCopyable(Name, OperatingSystem.IsWindows());

    /// <summary>What the card says in place of <see cref="RunCommand"/> when it offers none, or empty.</summary>
    internal string RunCommandNote => HasRunCommand ? string.Empty : CopiedRunCommand.NoCommandNote;

    /// <summary>The project's profiles: <c>dev</c> first, protected ones last.</summary>
    [ObservableProperty]
    internal partial IReadOnlyList<EnvProfileInfo> Profiles { get; private set; } = [];

    /// <summary>
    /// The matrix columns and the preview's toggle: <see cref="Profiles"/>, plus the selected
    /// profile while it has no key yet, placed before any protected one.
    /// </summary>
    [ObservableProperty]
    internal partial IReadOnlyList<EnvProfileColumn> Columns { get; private set; } = [];

    /// <summary>Every key against <see cref="Columns"/>. Holds no value.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRows))]
    internal partial IReadOnlyList<EnvKeyRow> Rows { get; private set; } = [];

    internal bool HasRows => Rows.Count > 0;

    /// <summary>Whether the selected profile is protected, so every release of it is asked live (D-0348).</summary>
    internal bool SelectedIsProtected => EnvProfileNames.IsProtected(SelectedProfile);

    /// <summary>Whether the new-profile form is open.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BeginAddProfileCommand), nameof(ConfirmAddProfileCommand), nameof(CancelAddProfileCommand))]
    internal partial bool IsAddingProfile { get; private set; }

    /// <summary>The name of the profile being started.</summary>
    [ObservableProperty]
    internal partial string NewProfile { get; set; } = string.Empty;

    internal RelayCommand BeginAddProfileCommand { get; }

    /// <summary>Selects the new profile and opens the add form, since its first key is what creates it.</summary>
    internal RelayCommand ConfirmAddProfileCommand { get; }

    internal RelayCommand CancelAddProfileCommand { get; }

    /// <summary>Asks where to save <see cref="ReferencePreview"/> and writes it there.</summary>
    internal AsyncRelayCommand ExportReferencesCommand { get; }

    /// <summary>Every key against every profile. Holds no value.</summary>
    [ObservableProperty]
    internal partial EnvMatrix? Matrix { get; private set; }

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

            if (SetProperty(ref _selectedProfile, value))
            {
                OnPropertyChanged(nameof(RunCommand));
                OnPropertyChanged(nameof(SelectedIsProtected));
                OnPropertyChanged(nameof(ReferencePreview));
                OnPropertyChanged(nameof(ReferenceLines));
                Import.Profile = value;
                Launch.Profile = value;
                Revealed = null;
                Removing = null;
                CancelReplace();
                CloseEntryForms();
                Reload();
            }
        }
    }

    /// <summary>The <c>.env.keypaste</c> the selected profile exports: references only, safe to commit.</summary>
    internal string ReferencePreview => EnvReferenceFile.Format(Name, SelectedProfile, _referenceKeys);

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
            return "The vault is locked. Unlock it, then try again.";
        }

        var target = Path.GetFullPath(path);
        var overVault = $"{target} is a vault, so nothing was written. Choose another file.";

        if (VaultOverwriteRule.Check(vault.Path, target) != VaultOverwrite.None)
        {
            return overVault;
        }

        if (File.Exists(target) && !replace)
        {
            return $"{target} already exists, so nothing was written. Replace it to overwrite it.";
        }

        var export = EnvReferenceExport.Run(vault, Name, SelectedProfile, target, replace);
        written = export.Outcome == EnvReferenceExportOutcome.Written;

        return export.Outcome switch
        {
            EnvReferenceExportOutcome.Written =>
                $"Wrote {(export.Count == 1 ? "1 reference" : $"{export.Count} references")} to {target}. It holds no value and is safe to commit.",
            EnvReferenceExportOutcome.NoProject or EnvReferenceExportOutcome.NoProfile =>
                $"{DisplayName}/{SelectedProfile} has no variables yet, so nothing was written.",
            EnvReferenceExportOutcome.Refused =>
                $"{EntryNameSanitizer.SanitizeProse(export.Problem, 1024).Text.TrimEnd('.')}. Nothing was written. Correct that, then export again.",
            EnvReferenceExportOutcome.OverVault => overVault,
            _ => $"{target} could not be written: {export.Problem?.TrimEnd('.')}. Choose another place.",
        };
    }

    /// <summary>Which variable is revealed right now, by name. Never its value.</summary>
    /// <remarks>
    /// Exposed so a test can assert that revealing is single and transient without reaching into a
    /// control. A key is already on screen; a value is what must not be.
    /// </remarks>
    internal string? RevealedKey => Revealed?.Key;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RevealedKey))]
    private partial EnvVariableRow? Revealed { get; set; }

    /// <summary>The variable a confirmation is pending for, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemovePrompt), nameof(IsRemoving))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmRemoveCommand), nameof(CancelRemoveCommand))]
    internal partial EnvVariableRow? Removing { get; private set; }

    internal bool IsRemoving => Removing is not null;

    /// <summary>The variable whose value is being replaced, or null.</summary>
    /// <remarks>
    /// One at a time, on the project rather than on each row, for the reason
    /// <see cref="Removing"/> is shaped this way and <see cref="RevealedKey"/> before it: a form
    /// per row would mean one live <see cref="SecretField"/> per variable, and a screen holding
    /// thirty buffers in order to use one of them.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReplacePrompt), nameof(IsReplacing))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmReplaceCommand), nameof(CancelReplaceCommand))]
    internal partial EnvVariableRow? Replacing { get; private set; }

    internal bool IsReplacing => Replacing is not null;

    /// <summary>What the replace form is headed with.</summary>
    /// <remarks>An entry several environments read is named with each of them, since every one gets the new value.</remarks>
    internal string ReplacePrompt => Replacing is { } row
        ? $"New value for {row.DisplayKey} on {ApprovalPrompt.Shown(row.Source.Entry)}{ReadBy(row.Source.Entry)}. The old one stays in the item's history."
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
            var changed = SetProperty(ref _generateValue, value);

            if (value && IsAdding)
            {
                GenerateInto(NewValue);
            }
            else if (!value && changed)
            {
                NewValue.Clear();
            }
        }
    }

    /// <summary>Generate, beside the new value: a generated value in place of what it holds.</summary>
    internal RelayCommand GenerateCommand { get; }

    /// <summary>Generate, beside a form's value: a generated value in place of what the field holds.</summary>
    internal RelayCommand<SecretField> GenerateIntoCommand { get; }

    private void GenerateInto(SecretField? field)
    {
        if (field is null)
        {
            return;
        }

        if (Generator.Recipe is { } recipe)
        {
            field.Generate(recipe);
        }
        else
        {
            field.Clear();
            _report(Generator.Error);
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
        CloseEntryForms();
        ReplacementValue.Clear();
        Replacing = row;
        _report(null);
    }

    /// <summary>What the confirmation asks.</summary>
    internal string RemovePrompt => Removing is { } row
        ? $"Remove {row.DisplayKey} from {ApprovalPrompt.Shown(row.Source.Entry)}{ReadBy(row.Source.Entry)}? Its value stays in the item's history."
        : string.Empty;

    /// <summary>The other environments an entry serves, the open project's by name and another project's as <c>project/environment</c>.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="environment">The environment it is being shown in, left out.</param>
    internal IReadOnlyList<string> Elsewhere(EntryName entry, string environment) =>
    [
        .. (_catalog?.EnvironmentsOf(entry) ?? [])
            .Where(membership => !(string.Equals(membership.Project, Name, StringComparison.Ordinal)
                && string.Equals(membership.Environment, environment, StringComparison.Ordinal)))
            .Select(membership => string.Equals(membership.Project, Name, StringComparison.Ordinal)
                ? EntryNameSanitizer.Sanitize(membership.Environment).Text
                : EntryNameSanitizer.SanitizePath(membership.ToString()).Text),
    ];

    /// <summary>", which billing/dev and billing/staging read" for an entry more than one environment reads; empty otherwise.</summary>
    private string ReadBy(EntryName entry)
    {
        var environments = (_catalog?.EnvironmentsOf(entry) ?? []).Select(membership => EntryNameSanitizer.SanitizePath(membership.ToString()).Text).ToArray();

        return environments.Length < 2
            ? string.Empty
            : $", which {string.Join(", ", environments[..^1])} and {environments[^1]} read";
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BeginAddCommand), nameof(CancelAddCommand), nameof(ConfirmAddCommand))]
    internal partial bool IsAdding { get; private set; }

    /// <summary>The name of the variable being added.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewKeyProblem), nameof(HasNewKeyProblem))]
    internal partial string NewKey { get; set; } = string.Empty;

    /// <summary>Why the name typed cannot be added, or null: core's rule for a new key, which an existing key of this environment already passed.</summary>
    internal string? NewKeyProblem =>
        NewKey.Trim() is { Length: > 0 } key
        && !Variables.Any(variable => string.Equals(variable.Key, key, StringComparison.Ordinal))
        && !EnvStore.IsNewFieldName(key, out var error)
            ? EntryNameSanitizer.SanitizeProse(error, 1024).Text
            : null;

    internal bool HasNewKeyProblem => NewKeyProblem is not null;

    /// <summary>What leaving would lose: the first form here holding what was typed, or null.</summary>
    internal string? UnsavedDraft => ProfileDraft() ?? KeyDraft() ?? ReplaceDraft() ?? Launch.UnsavedDraft;

    internal Func<string?> ProfileDraft => () => IsAddingProfile && NewProfile.Trim().Length > 0 ? "this new environment" : null;

    internal Func<string?> KeyDraft => () =>
        IsAdding && (NewKey.Trim().Length > 0 || (NewValue.HasValue && !NewValue.IsGenerated)) ? "this new key" : null;

    internal Func<string?> ReplaceDraft => () =>
        IsReplacing && ReplacementValue.HasValue && !ReplacementValue.IsGenerated ? "this new value" : null;

    /// <summary>What Return does on the tag change's confirmation: nothing when the change takes keys away, which is its danger form.</summary>
    internal ICommand? TagChangeReturnCommand => TagChangeAdds ? ConfirmTagChangeCommand : null;

    /// <summary>The entries a new key can go on: the selected profile's home entry first, then the entries tagged into it.</summary>
    [ObservableProperty]
    internal partial IReadOnlyList<EnvEntryChoice> EntryChoices { get; private set; } = [];

    /// <summary>The entry the new key goes on.</summary>
    [ObservableProperty]
    internal partial EnvEntryChoice? NewEntry { get; set; }

    internal RelayCommand BeginAddCommand { get; }

    internal RelayCommand CancelAddCommand { get; }

    internal RelayCommand ConfirmAddCommand { get; }

    internal RelayCommand ConfirmRemoveCommand { get; }

    internal RelayCommand CancelRemoveCommand { get; }

    /// <summary>Each environment of <see cref="Columns"/> with the entries tagged into it.</summary>
    [ObservableProperty]
    internal partial IReadOnlyList<EnvEnvironmentEntries> Environments { get; private set; } = [];

    /// <summary>Whether the form that tags an entry into an environment is open.</summary>
    internal bool IsAddingEntry => AddingEntryTo is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAddingEntry), nameof(AddEntryTitle))]
    [NotifyCanExecuteChangedFor(nameof(CancelAddEntryCommand))]
    private partial string? AddingEntryTo { get; set; }

    internal string AddEntryTitle => AddingEntryTo is { } environment
        ? $"Add an item to {EntryNameSanitizer.Sanitize(environment).Text}"
        : string.Empty;

    /// <summary>What the add form's list is narrowed to: part of an entry's path, in any case.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EntryCandidates), nameof(CandidateNote))]
    internal partial string EntryFilter { get; set; } = string.Empty;

    /// <summary>
    /// The entries the add form offers for <see cref="EntryFilter"/>: never one already in the
    /// environment, one in the recycle bin or one of keypaste's own.
    /// </summary>
    internal IReadOnlyList<EnvEntryCandidate> EntryCandidates => [.. Matching().Take(_shownCandidates)];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EntryCandidates), nameof(CandidateNote))]
    private partial IReadOnlyList<EnvEntryCandidate> Candidates { get; set; } = [];

    /// <summary>What the add form says under its list: that nothing matches, or how many more the filter hides.</summary>
    internal string CandidateNote
    {
        get
        {
            var count = Matching().Count();

            return count == 0 ? "No other item matches."
                : count > _shownCandidates ? Invariant($"{count - _shownCandidates} more. Type part of a path to narrow the list.")
                : string.Empty;
        }
    }

    /// <summary>The entry chosen in the add form; choosing one says what tagging it does, before anything is written.</summary>
    internal EnvEntryCandidate? ChosenEntry
    {
        get => _chosenEntry;
        set
        {
            if (SetProperty(ref _chosenEntry, value))
            {
                PreviewAdd(value);
            }
        }
    }

    /// <summary>The tag change waiting for the person's answer, as the entry pane asks it (D-0415).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TagChangeReturnCommand))]
    [NotifyPropertyChangedFor(nameof(IsConfirmingTagChange), nameof(TagChangeLines), nameof(TagChangeAdds), nameof(TagChangeAction))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmTagChangeCommand), nameof(CancelTagChangeCommand))]
    internal partial ProjectTagChange? PendingTagChange { get; private set; }

    internal bool IsConfirmingTagChange => PendingTagChange is not null;

    /// <summary>What the change does: the environment it reaches and the fields that join or leave it, never a value.</summary>
    internal IReadOnlyList<string> TagChangeLines => PendingTagChange?.Describe() ?? [];

    internal bool TagChangeAdds => PendingTagChange?.Adding ?? false;

    internal string TagChangeAction => PendingTagChange is { Read.Environment: { } environment } change
        ? change.Adding
            ? $"Add to {EntryNameSanitizer.Sanitize(environment).Text}"
            : $"Remove from {EntryNameSanitizer.Sanitize(environment).Text}"
        : string.Empty;

    /// <summary>Writes the pending tag change.</summary>
    internal RelayCommand ConfirmTagChangeCommand { get; }

    internal RelayCommand CancelTagChangeCommand { get; }

    /// <summary>Reads the project again.</summary>
    internal void Reload()
    {
        if (_session.Unlocked is not { } vault)
        {
            Variables = [];
            EntryChoices = [];
            NewEntry = null;
            _catalog = null;
            Environments = [];
            CloseEntryForms();
            _referenceKeys = [];
            Profiles = [];
            Matrix = null;
            Columns = [];
            Rows = [];

            // A half-entered value is as much a secret as a stored one, and the screen is about to
            // be disposed anyway. Clearing here means the lock holds on whichever path runs.
            NewValue.Clear();
            ReplacementValue.Clear();
            IsAdding = false;
            Replacing = null;
            OnPropertyChanged(nameof(ReferencePreview));
            OnPropertyChanged(nameof(ReferenceLines));
            return;
        }

        var matrix = EnvMatrix.Build(vault, Name, _session.Clock);
        Matrix = matrix;
        Profiles = matrix.Profiles;

        var listing = EnvResolution.List(vault, Name, SelectedProfile);
        _referenceKeys = [.. listing.Variables.Select(variable => variable.Key).Distinct(StringComparer.Ordinal)];

        // A key two entries hold has no one value to copy, replace or remove; its cell says so,
        // and the entry pane edits each copy.
        var single = listing.Sources.GroupBy(source => source.Key, StringComparer.Ordinal)
            .Where(key => key.Count() == 1)
            .Select(key => key.Key)
            .ToHashSet(StringComparer.Ordinal);

        Variables =
        [
            .. listing.Variables
                .Zip(listing.Sources)
                .Where(pair => single.Contains(pair.Second.Key))
                .Select(pair => new EnvVariableRow(this, pair.Second, pair.First.Value.Length)),
        ];

        var home = EnvStore.HomeEntry(Name, SelectedProfile);
        _catalog = ProjectCatalog.Read(vault);
        var tagged = MembersOf(SelectedProfile);

        var homeExists = vault.ReadEntries().Any(entry => EntryName.Of(entry) == home);

        EntryChoices =
        [
            new EnvEntryChoice(
                tagged.Contains(home) ? home : null,
                homeExists ? ApprovalPrompt.Shown(home) : $"{ApprovalPrompt.Shown(home)}, created on first use"),
            .. tagged.Where(entry => entry != home).Select(entry => new EnvEntryChoice(entry, ApprovalPrompt.Shown(entry))),
        ];
        NewEntry = EntryChoices[0];

        Layout();
        ListEnvironments(vault);
        OnPropertyChanged(nameof(ReferencePreview));
        OnPropertyChanged(nameof(ReferenceLines));
    }

    /// <summary>The entries tagged into one of this project's environments.</summary>
    private IReadOnlyList<EntryName> MembersOf(string environment) =>
        _catalog?.Projects
            .FirstOrDefault(project => string.Equals(project.Name, Name, StringComparison.Ordinal))?.Environments
            .FirstOrDefault(listed => string.Equals(listed.Name, environment, StringComparison.Ordinal))?.Members ?? [];

    private void ListEnvironments(Vault vault)
    {
        Environments =
        [
            .. Columns.Select(column => new EnvEnvironmentEntries(
                column.Name,
                column.IsProtected,
                [.. MembersOf(column.Name).Select(entry => new EnvMemberRow(
                    entry,
                    column.Name,
                    KeysOn(vault, entry),
                    Elsewhere(entry, column.Name),
                    new RelayCommand(() => BeginRemoveEntry(entry, column.Name))))],
                new RelayCommand(() => BeginAddEntry(column.Name)))),
        ];
    }

    /// <summary>The names of an entry's fields a project releases; no value is read.</summary>
    private static IReadOnlyList<string> KeysOn(Vault vault, EntryName entry)
    {
        try
        {
            return [.. (vault.Fields(entry) ?? []).Select(field => field.Name).Where(EnvConvention.IsEnvNamedField).Order(StringComparer.Ordinal)];
        }
        catch (VaultException)
        {
            return [];
        }
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

        var usual = profiles.ToDictionary(
            profile => profile.Name,
            profile => EnvProfileCell.UsualOf(
                (Matrix?.Rows ?? [])
                    .SelectMany(row => row.Cells.Where(cell => string.Equals(cell.Profile, profile.Name, StringComparison.Ordinal)))
                    .Select(cell => (IReadOnlyList<string>)[.. cell.Sources.Select(ApprovalPrompt.Shown)])),
            StringComparer.Ordinal);

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
                new RelayCommand(() => Act(key, profile, state)))
            {
                Sources = [.. (known?.Sources ?? []).Select(ApprovalPrompt.Shown)],
                Also = known?.Sources is [var only] ? Elsewhere(only, profile) : [],
                UsualSource = usual.GetValueOrDefault(profile),
            };
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
        CloseEntryForms();
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
            _announce(message, ToastTone.Success);
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
            OnPropertyChanged(nameof(RunCommand));
        }
    }

    /// <summary>Hands a row its value, and takes it away from whichever row had it.</summary>
    internal string? Reveal(EnvVariableRow row)
    {
        Revealed = row;
        return Read(row);
    }

    /// <summary>Notes that a row's value was hidden.</summary>
    internal void Conceal(EnvVariableRow row)
    {
        if (ReferenceEquals(Revealed, row))
        {
            Revealed = null;
        }
    }

    /// <summary>Reads one value out of the open vault as it holds it, from the entry and field holding it, for a hold.</summary>
    internal string? Read(EnvVariableRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (_session.Unlocked is not { } vault)
        {
            return null;
        }

        try
        {
            return vault.ReadField(row.Source.Entry, row.Source.Field);
        }
        catch (VaultException e)
        {
            _report(e.Message);
            return null;
        }
    }

    /// <summary>
    /// Reads one value out of the open vault as it leaves keypaste, for a copy: its placeholders filled in
    /// from its entry's own fields, or why it cannot leave reported (D-0465).
    /// </summary>
    /// <returns>The value, or null once what went wrong was reported.</returns>
    internal string? ReadFilled(EnvVariableRow row)
    {
        if (Read(row) is not { } value)
        {
            _report("That value could not be read. The vault may have locked: unlock it, then try again.");
            return null;
        }

        PlaceholderFill? fill;
        try
        {
            fill = _session.Unlocked?.FillPlaceholders(row.Source.Entry, value);
        }
        catch (VaultException e)
        {
            _report(e.Message);
            return null;
        }

        if (fill?.Value is { } filled)
        {
            return filled;
        }

        _report(fill?.Refusal is { } refusal
            ? DisplayTextSanitizer.Sanitize($"{row.DisplayKey} {refusal}, so it was not copied.").Text + " Edit the item so keypaste can fill it in."
            : "That value could not be read. The vault may have locked: unlock it, then try again.");
        return null;
    }

    /// <summary>Asks to remove a variable.</summary>
    internal void BeginRemove(EnvVariableRow row)
    {
        CloseEntryForms();
        Removing = row;
    }

    /// <summary>Nothing read out of the vault outlives this.</summary>
    public void Dispose()
    {
        Revealed = null;
        Variables = [];
        Profiles = [];
        Matrix = null;
        Columns = [];
        Rows = [];
        Removing = null;
        Replacing = null;
        CloseEntryForms();
        Environments = [];
        _catalog = null;
        Launch.PropertyChanged -= OnLaunchChanged;
        NewValue.Dispose();
        ReplacementValue.Dispose();
        Import.Dispose();
        Launch.Dispose();
    }

    [RelayCommand(CanExecute = nameof(HasRunCommand))]
    private async Task CopyRunCommandAsync()
    {
        if (RunCommand is { } command)
        {
            await Clipboard.CopyPlainAsync(command, "Run command").ConfigureAwait(true);
        }
    }

    private void BeginAdd()
    {
        CloseEntryForms();
        NewKey = string.Empty;
        NewEntry = EntryChoices.Count > 0 ? EntryChoices[0] : null;
        NewValue.Clear();
        IsAdding = true;
        _report(null);

        if (_generateValue)
        {
            GenerateInto(NewValue);
        }
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
        CancelReplaceQuietly();
        _report(null);
    }

    private void CancelReplaceQuietly()
    {
        Replacing = null;
        ReplacementValue.Clear();
    }

    private void ConfirmAdd()
    {
        if (!_session.IsUnlocked)
        {
            _report("The vault is locked. Unlock it, then try again.");
            return;
        }

        var key = NewKey.Trim();

        if (key.Length == 0)
        {
            _report("The key needs a name.");
            return;
        }

        if (GenerateValue && Generator.Recipe is null)
        {
            _report(Generator.Error ?? string.Empty);
            return;
        }

        var value = NewValue.Compose();

        // Core's rules, not ones written next to this message: keypaste env set refuses the same
        // names and entries for the same reasons, and the two must not drift.
        var write = _session.Write(
            vault => new EnvStore(vault).Set(Name, SelectedProfile, key, value, NewEntry?.Entry),
            plan => plan.Refusal is null && plan.WritesAnything);

        if (write.Value?.Refusal is { } refusal)
        {
            _report(EntryNameSanitizer.SanitizeProse(refusal, 1024).Text);
            return;
        }

        if (write.Problem("add this again") is { } problem)
        {
            _report(problem);
            return;
        }

        IsAdding = false;
        NewKey = string.Empty;
        NewValue.Clear();
        _report(null);
        Reload();
    }

    /// <summary>
    /// Writes a new value over an existing variable on the entry holding it, keeping the old one in history.
    /// </summary>
    private void ConfirmReplace()
    {
        if (Replacing is not { } row)
        {
            return;
        }

        if (_session.Unlocked is not { } open)
        {
            _report("The vault is locked. Unlock it, then try again.");
            return;
        }

        // Something may have removed it while this form was open; writing it elsewhere would be a
        // new key the person did not ask for.
        if (!EnvResolution.List(open, Name, SelectedProfile).Sources.Contains(row.Source))
        {
            _report($"{row.DisplayKey} is no longer on {ApprovalPrompt.Shown(row.Source.Entry)}, so nothing was written. Check the list, then try again.");
            CancelReplaceQuietly();
            Reload();
            return;
        }

        var value = ReplacementValue.Compose();
        var write = _session.Write(
            vault => new EnvStore(vault).Set(Name, SelectedProfile, row.Key, value, row.Source.Entry),
            plan => plan.Refusal is null && plan.WritesAnything);

        if (write.Value?.Refusal is { } refusal)
        {
            _report(EntryNameSanitizer.SanitizeProse(refusal, 1024).Text);
            return;
        }

        if (write.Problem("replace this again") is { } problem)
        {
            _report(problem);
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

        var write = _session.Write(
            vault => new EnvStore(vault).Remove(Name, SelectedProfile, row.Key, row.Source.Entry),
            removal => removal.Outcome is not (EnvRemoveOutcome.NothingMatched or EnvRemoveOutcome.Ambiguous or EnvRemoveOutcome.Refused));

        if (write.Value is { Outcome: EnvRemoveOutcome.NothingMatched })
        {
            _report($"{row.DisplayKey} is not in {DisplayName} any more. Check the list, then try again.");
            Removing = null;
            Reload();
            return;
        }

        if (write.Value is { Outcome: EnvRemoveOutcome.Ambiguous or EnvRemoveOutcome.Refused } refused)
        {
            _report(EntryNameSanitizer.SanitizeProse(refused.Refusal, 1024).Text);
            return;
        }

        if (write.Problem("remove this again") is { } problem)
        {
            _report(problem);
            return;
        }

        Removing = null;
        _report(null);
        _announce($"Removed {row.DisplayKey} from {ApprovalPrompt.Shown(row.Source.Entry)}. Its value stays in the item's history.", ToastTone.Success);

        Reload();
    }

    /// <summary>Opens the form that tags another entry into an environment.</summary>
    private void BeginAddEntry(string environment)
    {
        if (_session.Unlocked is not { } vault)
        {
            _report("The vault is locked. Unlock it, then try again.");
            return;
        }

        IsAdding = false;
        IsAddingProfile = false;
        Removing = null;
        CancelReplaceQuietly();
        PendingTagChange = null;

        var members = MembersOf(environment).ToHashSet();
        Candidates =
        [
            .. vault.ReadEntries()
                .Select(EntryName.Of)
                .Distinct()
                .Where(entry => !members.Contains(entry))
                .Select(entry => new EnvEntryCandidate(entry, ApprovalPrompt.Shown(entry)))
                .OrderBy(candidate => candidate.Display, StringComparer.Ordinal),
        ];
        ChosenEntry = null;
        EntryFilter = string.Empty;
        AddingEntryTo = environment;
        _report(null);
    }

    [RelayCommand(CanExecute = nameof(IsAddingEntry))]
    private void CancelAddEntry()
    {
        CloseEntryForms();
        _report(null);
    }

    private void CloseEntryForms()
    {
        PendingTagChange = null;

        if (AddingEntryTo is null)
        {
            return;
        }

        AddingEntryTo = null;
        Candidates = [];
        ChosenEntry = null;
        EntryFilter = string.Empty;
    }

    private IEnumerable<EnvEntryCandidate> Matching()
    {
        var filter = EntryFilter.Trim();

        return filter.Length == 0
            ? Candidates
            : Candidates.Where(candidate => candidate.Display.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Says what tagging the chosen entry into the environment does; nothing is written until it is confirmed.</summary>
    private void PreviewAdd(EnvEntryCandidate? chosen)
    {
        if (chosen is null || AddingEntryTo is not { } environment || _session.Unlocked is not { } vault)
        {
            PendingTagChange = null;
            return;
        }

        if (!ProjectTag.TryFor(Name, environment, out var tag, out var invalid))
        {
            PendingTagChange = null;
            _report(invalid);
            return;
        }

        Preview(vault, chosen.Entry, [tag], adding: true);
    }

    /// <summary>Asks to take an entry out of an environment by every tag of its own that puts it there; the entry and its fields stay.</summary>
    private void BeginRemoveEntry(EntryName entry, string environment)
    {
        if (_session.Unlocked is not { } vault)
        {
            _report("The vault is locked. Unlock it, then try again.");
            return;
        }

        IsAdding = false;
        IsAddingProfile = false;
        Removing = null;
        CancelReplaceQuietly();
        CloseEntryForms();

        IReadOnlyList<string> tags;

        try
        {
            tags =
            [
                .. (vault.Tags(entry) ?? []).Where(tag => ProjectTag.Read(tag) is { Kind: ProjectTagKind.Member } read
                    && string.Equals(read.Project, Name, StringComparison.Ordinal)
                    && string.Equals(read.Environment, environment, StringComparison.Ordinal)),
            ];
        }
        catch (VaultException e)
        {
            _report(e.Message);
            return;
        }

        if (tags.Count == 0)
        {
            _report($"{ApprovalPrompt.Shown(entry)} is no longer in {DisplayName}/{EntryNameSanitizer.Sanitize(environment).Text}. Check the list, then try again.");
            Reload();
            return;
        }

        Preview(vault, entry, tags, adding: false);
    }

    private void Preview(Vault vault, EntryName entry, IReadOnlyList<string> tags, bool adding)
    {
        try
        {
            PendingTagChange = _session.PreviewTagChange(vault, entry, tags, adding);
            _report(null);
        }
        catch (VaultException e)
        {
            PendingTagChange = null;
            _report(e.Message);
        }
    }

    private void CancelTagChange()
    {
        PendingTagChange = null;
        ChosenEntry = null;
        _report(null);
    }

    private void ConfirmTagChange()
    {
        if (PendingTagChange is not { } change)
        {
            return;
        }

        var entry = ApprovalPrompt.Shown(change.Entry);
        var environment = EntryNameSanitizer.SanitizePath(change.Environment ?? string.Empty).Text;
        var write = _session.Write(
            vault => change.Adding ? vault.AddTag(change.Entry, change.Tags[0]) : vault.RemoveTags(change.Entry, change.Tags),
            changed => changed);

        if (write.Outcome == WriteOutcome.NothingToSave)
        {
            _report(change.Adding
                ? $"{entry} is already in {environment} or no longer in this vault, so nothing was written. Check the list, then try again."
                : $"{entry} is no longer in {environment}, so nothing was written. Check the list, then try again.");
            CloseEntryForms();
            Reload();
            return;
        }

        if (write.Problem("make this change again") is { } problem)
        {
            _report(problem);
            return;
        }

        CloseEntryForms();
        _report(null);
        _announce(change.Adding
            ? $"Added {entry} to {environment}."
            : $"Removed {entry} from {environment}. The item and its fields are kept.",
            ToastTone.Success);
        Reload();
        _projectsChanged();
    }
}

/// <summary>An entry a new key can be written on, as the add form offers it.</summary>
/// <param name="Entry">The entry, or null for the home entry not yet created.</param>
/// <param name="Display">What the form shows.</param>
internal sealed record EnvEntryChoice(EntryName? Entry, string Display)
{
    /// <inheritdoc/>
    public override string ToString() => Display;
}
