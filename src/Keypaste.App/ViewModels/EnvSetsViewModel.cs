using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>
/// The Environments place: the project the sidebar chose, its stages and its variables masked.
/// </summary>
/// <remarks>
/// <para>
/// A project is the entries whose own tags name it, and a variable is an env-named field of one of
/// them, read through <see cref="ProjectCatalog"/> and <see cref="EnvResolution"/> and
/// written through <see cref="EnvStore"/>. Nothing about where a variable lives is decided here,
/// which is what lets <c>keypaste env ls</c> and <c>keypaste run</c> see what this screen writes the
/// moment it is written.
/// </para>
/// <para>
/// <b>Only the open project's variables are ever read.</b> A card knows a project's name and how
/// many variables it holds; the values behind a card nobody opened are never fetched, so a screen
/// showing eight projects has read one project's worth of anything.
/// </para>
/// </remarks>
internal sealed partial class EnvSetsViewModel : ObservableObject, IDisposable, IHoldsDraft
{
    private readonly AppVaultSession _session;
    private string? _newProjectRefusal;
    private readonly ClipboardCountdown _clipboard;
    private readonly IVaultFilePicker? _picker;
    private readonly ProjectLaunching _launching;
    private readonly Action<string, ToastTone>? _toast;
    private readonly Action<EntryName>? _openItem;

    private EnvProjectViewModel? _open;
    private bool _importsOnceCreated;

    internal EnvSetsViewModel(
        AppVaultSession session,
        ClipboardCountdown clipboard,
        IVaultFilePicker? picker = null,
        ProjectLaunching? launching = null,
        Action<string, ToastTone>? toast = null,
        Action<EntryName>? openItem = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clipboard);

        _session = session;
        _clipboard = clipboard;
        _picker = picker;
        _launching = launching ?? ProjectLaunching.ForThisMachine(authority: null);
        _toast = toast;
        _openItem = openItem;

        CopyRunCommandCommand = new RelayCommand<string>(
            project => _ = CopyRunCommandAsync(project ?? string.Empty));
        CloseCommand = new RelayCommand(() => Open(null));
        BeginAddCommand = new RelayCommand(BeginAdd, () => !IsAdding);
        CancelAddCommand = new RelayCommand(CancelAdd, () => IsAdding);
        ConfirmAddCommand = new RelayCommand(ConfirmAdd, () => IsAdding);
        BeginImportCommand = new RelayCommand(BeginImport, () => !IsAdding);
        ConfirmFixCommand = new RelayCommand(ConfirmFix, () => PendingFix is not null);
        CancelFixCommand = new RelayCommand(() => PendingFix = null, () => PendingFix is not null);

        Reload();
    }

    /// <inheritdoc/>
    public string? UnsavedDraft => NewProjectDraft() ?? OpenProject?.UnsavedDraft;

    internal Func<string?> NewProjectDraft => () => IsAdding && NewProject.Trim().Length > 0 ? "this new project" : null;

    /// <summary>Every project in the vault, by name.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProjects), nameof(IsEmpty))]
    internal partial IReadOnlyList<string> Projects { get; private set; } = [];

    internal bool HasProjects => Projects.Count > 0;

    /// <summary>Every tag in the vault that starts like a project tag and puts its entry in no project, as <c>keypaste env ls</c> warns of them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMalformedTags))]
    internal partial IReadOnlyList<EnvTagProblemRow> MalformedTags { get; private set; } = [];

    internal bool HasMalformedTags => MalformedTags.Count > 0;

    /// <summary>The tag whose change to lower case is being asked about, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingFix))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmFixCommand), nameof(CancelFixCommand))]
    internal partial EnvTagProblemRow? PendingFix { get; private set; }

    internal bool IsConfirmingFix => PendingFix is not null;

    /// <summary>What changing the tag reaches, shown before it is written.</summary>
    [ObservableProperty]
    internal partial IReadOnlyList<string> FixLines { get; private set; } = [];

    internal RelayCommand ConfirmFixCommand { get; }

    internal RelayCommand CancelFixCommand { get; }

    /// <summary>Import .env… with no project yet: names the new project first, then chooses the file.</summary>
    internal RelayCommand BeginImportCommand { get; }

    /// <summary>The project whose table is showing, or null.</summary>
    internal EnvProjectViewModel? OpenProject
    {
        get => _open;
        private set
        {
            var previous = _open;

            if (SetProperty(ref _open, value))
            {
                OnPropertyChanged(nameof(HasOpenProject));
                OnPropertyChanged(nameof(IsEmpty));
                // Disposed on the way out: it holds a reveal slot and a table read from an open
                // vault, and leaving it alive would keep both after the card was closed.
                previous?.Dispose();
            }
        }
    }

    internal bool HasOpenProject => _open is not null;

    /// <summary>Whether there is no project to show, open or in the vault.</summary>
    internal bool IsEmpty => !HasProjects && !HasOpenProject;

    /// <summary>A calm sentence when something did not work, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    internal partial string? Error { get; private set; }

    internal bool HasError => Error is not null;

    /// <summary>What the last action did, or null.</summary>
    /// <remarks>
    /// Separate from <see cref="Error"/> because a removal that worked is not a failure, and a
    /// person needs to be told where the variable went. The screen draws them the same way.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    internal partial string? Notice { get; private set; }

    internal bool HasNotice => Notice is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BeginAddCommand), nameof(CancelAddCommand), nameof(ConfirmAddCommand), nameof(BeginImportCommand))]
    internal partial bool IsAdding { get; private set; }

    /// <summary>The name of the project being created.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewProjectProblem), nameof(HasNewProjectProblem))]
    internal partial string NewProject { get; set; } = string.Empty;

    /// <summary>Why the project name typed cannot be used, or null: what Create refused it for, then core's rule, an empty name only once Create is pressed on it.</summary>
    internal string? NewProjectProblem =>
        _newProjectRefusal ?? (NewProject.Trim() is { Length: > 0 } project && !EnvConvention.IsValidProject(project, out var invalid) ? invalid : null);

    internal bool HasNewProjectProblem => NewProjectProblem is not null;

    partial void OnNewProjectChanged(string value) => _newProjectRefusal = null;

    private void RefuseNewProject(string? refusal)
    {
        _newProjectRefusal = refusal;
        OnPropertyChanged(nameof(NewProjectProblem));
        OnPropertyChanged(nameof(HasNewProjectProblem));
    }

    /// <summary>Copies a card's run command, plainly and with no countdown.</summary>
    internal RelayCommand<string> CopyRunCommandCommand { get; }

    /// <summary>Whether another open vault has a project of this name, which a copied run command then names its vault for.</summary>
    internal Func<string, bool> ProjectElsewhere { get; init; } = static _ => false;

    /// <summary>Told once a run command is copied, so the window can offer to link the <c>keypaste</c> it runs; none tells nobody.</summary>
    internal Action? RunCommandCopied { get; init; }

    internal RelayCommand CloseCommand { get; }

    internal RelayCommand BeginAddCommand { get; }

    internal RelayCommand CancelAddCommand { get; }

    internal RelayCommand ConfirmAddCommand { get; }

    /// <summary>Reads the project list again, keeping the open card if it survived.</summary>
    internal void Reload()
    {
        if (_session.Unlocked is not { } vault)
        {
            Projects = [];
            MalformedTags = [];
            OpenProject = null;
            return;
        }

        var wanted = OpenProject?.Name;

        ReadCatalog(vault);

        if (wanted is not null && Projects.Contains(wanted, StringComparer.Ordinal))
        {
            OpenProject?.Reload();
        }
        else
        {
            OpenProject = null;
        }

        // The screen always shows a project, as the design does; the first one until another is chosen.
        if (OpenProject is null && Projects.Count > 0)
        {
            Open(Projects[0]);
        }
    }

    /// <summary>Reads the project list and the malformed tags again, leaving the open project as it is.</summary>
    /// <remarks>
    /// A tag change on the open project can make a project or take its last entry away; the
    /// project stays open either way, as a new one does before its first entry.
    /// </remarks>
    internal void RefreshProjects()
    {
        if (_session.Unlocked is { } vault)
        {
            ReadCatalog(vault);
        }
    }

    private void ReadCatalog(Vault vault)
    {
        var catalog = ProjectCatalog.Read(vault);
        Projects = [.. catalog.Projects.Select(listing => listing.Name)];
        PendingFix = null;
        MalformedTags = [.. catalog.Problems.Select(Row)];

        EnvTagProblemRow Row(ProjectTagProblem problem)
        {
            EnvTagProblemRow? row = null;
            row = new EnvTagProblemRow(problem, new RelayCommand(() => _openItem?.Invoke(problem.Entry)), new RelayCommand(() => AskFix(row!)));
            return row;
        }
    }

    /// <summary>How many variables a project holds, without opening it.</summary>
    /// <remarks>
    /// A count rather than a table: a card says how much is behind it, and reading eight projects'
    /// values to draw eight cards would be reading seven projects nobody asked about.
    /// </remarks>
    internal int CountIn(string project)
    {
        if (_session.Unlocked is not { } vault)
        {
            return 0;
        }

        try
        {
            return EnvResolution.List(vault, project, EnvProfileNames.Default).Variables.Count;
        }
        catch (VaultException)
        {
            return 0;
        }
    }

    /// <summary>The command that injects a project of this vault, for the card's copy helper, for the person to finish, or null when the vault's path or the name gets none.</summary>
    internal string? RunCommandFor(string project) =>
        _session.VaultPath is { } vault
            ? CopiedRunCommand.For(CopiedRunCommand.NamesVault(vault, _session.Home, ProjectElsewhere(project)) ? vault : null, project, environment: null, command: string.Empty)
            : null;

    /// <summary>Copies a project's run command, plainly and with no countdown, when it has one.</summary>
    internal async Task CopyRunCommandAsync(string project)
    {
        if (RunCommandFor(project) is { } command)
        {
            await _clipboard.CopyPlainAsync(command, "Run command").ConfigureAwait(true);
            RunCommandCopied?.Invoke();
        }
    }

    /// <summary>Nothing derived from the vault outlives this.</summary>
    public void Dispose()
    {
        OpenProject = null;
        Projects = [];
        MalformedTags = [];
    }

    [RelayCommand]
    private void Open(string? project)
    {
        Error = null;
        Notice = null;

        OpenProject = project is null
            ? null
            : new EnvProjectViewModel(
                _session,
                _clipboard,
                project,
                message => Error = message,
                Announce,
                _picker,
                _launching,
                Reload,
                RefreshProjects)
            {
                ProjectElsewhere = ProjectElsewhere,
                RunCommandCopied = RunCommandCopied,
            };
    }

    private void Announce(string? outcome, ToastTone tone)
    {
        if (_toast is null)
        {
            Notice = outcome;
        }
        else if (!string.IsNullOrWhiteSpace(outcome))
        {
            _toast(outcome, tone);
        }
    }

    private void AskFix(EnvTagProblemRow row)
    {
        if (_session.Unlocked is not { } vault || row.Fixed is not { } tag)
        {
            return;
        }

        try
        {
            FixLines = ProjectTagChange.Preview(vault, row.Problem.Entry, [tag], adding: true)?.Describe() ?? [];
            PendingFix = row;
            Error = null;
        }
        catch (VaultException e)
        {
            Error = e.Message;
        }
    }

    /// <summary>Takes the cased-wrong tag off its item and puts the lower-case one on, and saves.</summary>
    private void ConfirmFix()
    {
        if (PendingFix is not { Fixed: { } tag } row)
        {
            return;
        }

        PendingFix = null;
        var entry = row.Problem.Entry;
        var write = _session.Write(vault => vault.RemoveTags(entry, [row.Problem.Tag]) | vault.AddTag(entry, tag), changed => changed);

        if (write.Problem("change the tag again") is { } problem)
        {
            Error = problem;
            return;
        }

        Error = null;
        Reload();
    }

    private void BeginImport()
    {
        BeginAdd();
        _importsOnceCreated = true;
    }

    private void BeginAdd()
    {
        NewProject = string.Empty;
        RefuseNewProject(null);
        IsAdding = true;
        Error = null;
    }

    private void CancelAdd()
    {
        _importsOnceCreated = false;
        IsAdding = false;
        NewProject = string.Empty;
        RefuseNewProject(null);
        Error = null;
    }

    private void ConfirmAdd()
    {
        if (_session.Unlocked is not { } vault)
        {
            Error = "The vault is locked. Unlock it, then try again.";
            return;
        }

        var project = NewProject.Trim();

        // Core's rule, so this screen and `keypaste env set` refuse the same names and cannot drift.
        if (!EnvConvention.IsValidProject(project, out var invalid))
        {
            RefuseNewProject(invalid);
            return;
        }

        if (ProjectCatalog.Read(vault).Projects.Any(listing => string.Equals(listing.Name, project, StringComparison.Ordinal)))
        {
            RefuseNewProject($"'{project}' already exists. Choose another name, or open it from the sidebar.");
            return;
        }

        // A project is its tagged entries, and its first key creates the tagged home entry. There is
        // nothing to write yet, so the card appears the moment its first variable does — which is
        // also true of `keypaste env set`, and saying otherwise would be a second convention.
        IsAdding = false;
        Error = null;
        Open(project);

        if (_importsOnceCreated)
        {
            _importsOnceCreated = false;
            OpenProject?.Import.ChooseCommand.Press();
        }
    }
}
