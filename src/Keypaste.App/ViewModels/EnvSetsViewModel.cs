using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>
/// The Env Sets screen: projects as cards, one of them open, its variables masked.
/// </summary>
/// <remarks>
/// <para>
/// A project is a group under <c>env/</c> and a variable is one entry inside it — D-0014's
/// convention, read and written through <see cref="EnvStore"/>. Nothing about where a variable lives
/// is decided here, which is what lets <c>keypaste env ls</c> and <c>keypaste run</c> see what this
/// screen writes the moment it is written.
/// </para>
/// <para>
/// <b>Only the open project's variables are ever read.</b> A card knows a project's name and how
/// many variables it holds; the values behind a card nobody opened are never fetched, so a screen
/// showing eight projects has read one project's worth of anything.
/// </para>
/// </remarks>
internal sealed class EnvSetsViewModel : ObservableObject, IDisposable
{
    private readonly AppVaultSession _session;
    private readonly ClipboardCountdown _clipboard;

    private IReadOnlyList<string> _projectNames = [];
    private EnvProjectViewModel? _open;
    private string? _error;
    private bool _isAdding;
    private string _newProject = string.Empty;

    internal EnvSetsViewModel(AppVaultSession session, ClipboardCountdown clipboard)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clipboard);

        _session = session;
        _clipboard = clipboard;

        OpenCommand = new RelayCommand<string>(Open);
        CopyRunCommandCommand = new RelayCommand<string>(
            project => _ = CopyRunCommandAsync(project ?? string.Empty));
        CloseCommand = new RelayCommand(() => Open(null));
        BeginAddCommand = new RelayCommand(BeginAdd, () => !IsAdding);
        CancelAddCommand = new RelayCommand(CancelAdd, () => IsAdding);
        ConfirmAddCommand = new RelayCommand(ConfirmAdd, () => IsAdding);

        Reload();
    }

    /// <summary>Every project in the vault, by name.</summary>
    internal IReadOnlyList<string> Projects
    {
        get => _projectNames;
        private set
        {
            if (Set(ref _projectNames, value))
            {
                Raise(nameof(HasProjects));
            }
        }
    }

    internal bool HasProjects => _projectNames.Count > 0;

    /// <summary>The project whose table is showing, or null.</summary>
    internal EnvProjectViewModel? OpenProject
    {
        get => _open;
        private set
        {
            var previous = _open;

            if (Set(ref _open, value))
            {
                // Disposed on the way out: it holds a reveal slot and a table read from an open
                // vault, and leaving it alive would keep both after the card was closed.
                previous?.Dispose();
                Raise(nameof(HasOpenProject));
            }
        }
    }

    internal bool HasOpenProject => _open is not null;

    /// <summary>A calm sentence when something did not work, or null.</summary>
    internal string? Error
    {
        get => _error;
        private set
        {
            if (Set(ref _error, value))
            {
                Raise(nameof(HasError));
            }
        }
    }

    internal bool HasError => _error is not null;

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

    /// <summary>The name of the project being created.</summary>
    internal string NewProject
    {
        get => _newProject;
        set => Set(ref _newProject, value);
    }

    internal RelayCommand<string> OpenCommand { get; }

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
            OpenProject = null;
            return;
        }

        var wanted = OpenProject?.Name;

        Projects = [.. new EnvStore(vault).Projects()];

        if (wanted is not null && Projects.Contains(wanted, StringComparer.Ordinal))
        {
            OpenProject?.Reload();
        }
        else
        {
            OpenProject = null;
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
            return new EnvStore(vault).Read(project).Count;
        }
        catch (VaultException)
        {
            return 0;
        }
    }

    /// <summary>The command that injects a project, for the card's copy helper.</summary>
    internal static string RunCommandFor(string project) => $"keypaste run {project} -- ";

    /// <summary>Copies a project's run command, plainly and with no countdown.</summary>
    internal async Task CopyRunCommandAsync(string project) =>
        await _clipboard.CopyPlainAsync(RunCommandFor(project), "Run command").ConfigureAwait(true);

    /// <summary>Nothing derived from the vault outlives this.</summary>
    public void Dispose()
    {
        OpenProject = null;
        Projects = [];
    }

    private void Open(string? project)
    {
        Error = null;

        OpenProject = project is null
            ? null
            : new EnvProjectViewModel(_session, _clipboard, project, message => Error = message);
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
            Error = "The vault is locked.";
            return;
        }

        var project = NewProject.Trim();

        // Core's rule again, for the same reason: `keypaste env set` refuses the same names.
        if (!EnvConvention.IsValidProject(project, out var invalid))
        {
            Error = invalid;
            return;
        }

        if (new EnvStore(vault).ProjectExists(project))
        {
            Error = $"'{project}' already exists.";
            return;
        }

        // A project is a group, and core creates a group when something is put in it. There is
        // nothing to write yet, so the card appears the moment its first variable does — which is
        // also true of `keypaste env set`, and saying otherwise would be a second convention.
        IsAdding = false;
        Error = null;
        Open(project);
    }
}
