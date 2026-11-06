using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>
/// The Env Sets screen: projects as cards, one of them open, its variables masked.
/// </summary>
/// <remarks>
/// <para>
/// A project is the entries whose own tags name it, and a variable is an env-named field of one of
/// them (D-0416), read through <see cref="ProjectCatalog"/> and <see cref="EnvResolution"/> and
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
    private readonly ClipboardCountdown _clipboard;
    private readonly IVaultFilePicker? _picker;
    private readonly ProjectLaunching _launching;
    private readonly Action<string, ToastTone>? _toast;

    private EnvProjectViewModel? _open;
    private string? _askedProject;

    internal EnvSetsViewModel(
        AppVaultSession session,
        ClipboardCountdown clipboard,
        IVaultFilePicker? picker = null,
        ProjectLaunching? launching = null,
        Action<string, ToastTone>? toast = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clipboard);

        _session = session;
        _clipboard = clipboard;
        _picker = picker;
        _launching = launching ?? ProjectLaunching.ForThisMachine(authority: null);
        _toast = toast;

        CopyRunCommandCommand = new RelayCommand<string>(
            project => _ = CopyRunCommandAsync(project ?? string.Empty));
        CloseCommand = new RelayCommand(() => Open(null));
        BeginAddCommand = new RelayCommand(BeginAdd, () => !IsAdding);
        CancelAddCommand = new RelayCommand(CancelAdd, () => IsAdding);
        ConfirmAddCommand = new RelayCommand(ConfirmAdd, () => IsAdding);

        Reload();
    }

    /// <inheritdoc/>
    public string? UnsavedDraft => NewProjectDraft() ?? OpenProject?.UnsavedDraft;

    /// <summary>Asks about unsaved work before the header's project picker replaces it, then discards or keeps it.</summary>
    internal Action<string, Action, Action?>? AskDiscard { get; init; }

    internal Func<string?> NewProjectDraft => () => IsAdding && NewProject.Trim().Length > 0 ? "this new project" : null;

    /// <summary>Every project in the vault, by name.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProjects), nameof(ProjectChoices), nameof(IsEmpty))]
    internal partial IReadOnlyList<string> Projects { get; private set; } = [];

    internal bool HasProjects => Projects.Count > 0;

    /// <summary>Every tag in the vault that starts like a project tag and puts its entry in no project, as <c>keypaste env ls</c> warns of them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMalformedTags))]
    internal partial IReadOnlyList<EnvTagProblemRow> MalformedTags { get; private set; } = [];

    internal bool HasMalformedTags => MalformedTags.Count > 0;

    /// <summary>What the header's project picker offers: every project, and the open one while it has no variable yet.</summary>
    internal IReadOnlyList<string> ProjectChoices =>
        _open is { } open && !Projects.Contains(open.Name, StringComparer.Ordinal)
            ? [.. Projects, open.Name]
            : Projects;

    /// <summary>The open project's name; setting it opens that project.</summary>
    internal string? SelectedProject
    {
        get => _askedProject ?? _open?.Name;
        set
        {
            if (value is null || string.Equals(value, _open?.Name, StringComparison.Ordinal))
            {
                return;
            }

            if (AskDiscard is { } ask && UnsavedDraft is { } draft)
            {
                // The picker holds the name asked about until the answer, as a binding puts back only a value that changed.
                _askedProject = value;
                OnPropertyChanged();
                ask(draft, () => AnsweredAsked(() => Open(value)), () => AnsweredAsked(() => { }));
                return;
            }

            Open(value);
        }
    }

    private void AnsweredAsked(Action answer)
    {
        _askedProject = null;
        answer();
        OnPropertyChanged(nameof(SelectedProject));
    }

    /// <summary>The project whose table is showing, or null.</summary>
    internal EnvProjectViewModel? OpenProject
    {
        get => _open;
        private set
        {
            var previous = _open;

            if (SetProperty(ref _open, value))
            {
                OnPropertyChanged(nameof(ProjectChoices));
                OnPropertyChanged(nameof(SelectedProject));
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
    [NotifyCanExecuteChangedFor(nameof(BeginAddCommand), nameof(CancelAddCommand), nameof(ConfirmAddCommand))]
    internal partial bool IsAdding { get; private set; }

    /// <summary>The name of the project being created.</summary>
    [ObservableProperty]
    internal partial string NewProject { get; set; } = string.Empty;

    /// <summary>Copies a card's run command, plainly and with no countdown.</summary>
    internal RelayCommand<string> CopyRunCommandCommand { get; }

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
        MalformedTags = [.. catalog.Problems.Select(problem => new EnvTagProblemRow(problem))];
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

    /// <summary>The command that injects a project, for the card's copy helper, for the person to finish, or null when the name gets none (D-0453).</summary>
    internal static string? RunCommandFor(string project) => CopiedRunCommand.For(project, environment: null, command: string.Empty);

    /// <summary>Copies a project's run command, plainly and with no countdown, when it has one.</summary>
    internal async Task CopyRunCommandAsync(string project)
    {
        if (RunCommandFor(project) is { } command)
        {
            await _clipboard.CopyPlainAsync(command, "Run command").ConfigureAwait(true);
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
                RefreshProjects);
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

    private void BeginAdd()
    {
        NewProject = string.Empty;
        IsAdding = true;
        Error = null;
    }

    private void CancelAdd()
    {
        IsAdding = false;
        NewProject = string.Empty;
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

        // Core's rule again, for the same reason: `keypaste env set` refuses the same names.
        if (!EnvConvention.IsValidProject(project, out var invalid))
        {
            Error = invalid;
            return;
        }

        if (ProjectCatalog.Read(vault).Projects.Any(listing => string.Equals(listing.Name, project, StringComparison.Ordinal)))
        {
            Error = $"'{project}' already exists.";
            return;
        }

        // A project is its tagged entries, and its first key creates the tagged home entry. There is
        // nothing to write yet, so the card appears the moment its first variable does — which is
        // also true of `keypaste env set`, and saying otherwise would be a second convention.
        IsAdding = false;
        Error = null;
        Open(project);
    }
}
