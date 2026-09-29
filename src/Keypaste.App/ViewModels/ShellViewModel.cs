using System.Globalization;
using Keypaste.App.Clipboard;
using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Sharing;

namespace Keypaste.App.ViewModels;

/// <summary>
/// The main window once a vault is open: a titlebar, a sidebar, a content region and a toast.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything here is disposed on lock.</b> The whole shell leaves the visual tree rather than
/// being hidden, so "locked" has exactly one meaning and nothing that was derived from an open
/// vault can survive it. The sidebar's counts and project names are derived from the vault too,
/// and go with it.
/// </para>
/// <para>
/// <b>The sidebar reads names and counts, never values.</b> A count comes from the same entry list
/// the Secrets screen reads, and is dropped the moment it is counted.
/// </para>
/// </remarks>
/// <summary>How a status dot is drawn.</summary>
internal enum StatusTone
{
    /// <summary>All is as it should be.</summary>
    Ok = 0,

    /// <summary>Needs attention soon.</summary>
    Accent = 1,

    /// <summary>Something is wrong.</summary>
    Danger = 2,

    /// <summary>Nothing to report.</summary>
    Muted = 3,
}

internal sealed class ShellViewModel : ObservableObject, IDisposable
{
    /// <summary>How long a toast stays up.</summary>
    internal static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(2.8);

    private static readonly TimeSpan _statusTick = TimeSpan.FromSeconds(1);

    private readonly AppVaultSession _session;
    private readonly IVaultFilePicker? _picker;
    private readonly TimeProvider _clock;
    private readonly Action<Action>? _post;
    private readonly ITimer? _statusTimer;
    private ITimer? _toastTimer;
    private string? _notice;
    private Destination _current;
    private object? _content;
    private string _countdown = string.Empty;
    private string _search = string.Empty;
    private string? _toast;
    private IReadOnlyList<ProjectRow> _projects = [];
    private IReadOnlyList<object> _sidebarRows = [];
    private string? _openProject;
    private bool _rebuildingSidebar;
    private bool _agentsServing;
    private string _agentsDetail = string.Empty;
    private SharingViewModel? _share;
    private string _vaultStatus = string.Empty;
    private StatusTone _vaultStatusTone;
    private string _vaultStatusDetail = string.Empty;
    private int _ticks;
    private readonly Action<string, string?>? _openInPlace;
    private KdbxImportViewModel? _import;
    private bool _waitingForTouch;
    private bool _disposed;

    internal ShellViewModel(
        AppVaultSession session,
        string? home,
        AppAuthority? authority,
        Action<Core.Settings.AppTheme>? applyTheme = null,
        IAppClipboard? clipboard = null,
        TimeProvider? clock = null,
        Action<Action>? post = null,
        DesktopPreferences? preferences = null,
        string? notice = null,
        IVaultFilePicker? picker = null,
        Action<string, string?>? openInPlace = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _notice = notice;
        _picker = picker;
        _openInPlace = openInPlace;
        Home = home;
        Authority = authority;
        ApplyTheme = applyTheme ?? (_ => { });
        Preferences = preferences ?? new DesktopPreferences(home);
        _clock = clock ?? TimeProvider.System;
        _post = post;

        // Owned here rather than by each screen, so a copy made on Secrets is still counting down
        // after a move to Env profiles — and is cleared by the lock, because this is disposed with
        // everything else the shell built.
        Clipboard = new ClipboardCountdown(
            clipboard ?? NoClipboard.Instance,
            _clock,
            post);

        LockCommand = new RelayCommand(() => _session.Lock(VaultLockReason.Manual));
        DismissNoticeCommand = new RelayCommand(() => Notice = null);
        DismissToastCommand = new RelayCommand(() => Toast = null);
        OpenProjectCommand = new RelayCommand<string>(OpenProject);
        BackCommand = new RelayCommand(() => Current = Destinations.PlaceOf(Current), () => HasBack);
        NewProjectCommand = new RelayCommand(NewProject);
        ImportEnvCommand = new RelayCommand(ImportEnv);
        ShareCommand = new RelayCommand<string>(OpenShare, path => path is not null && _share is null);
        CloseShareCommand = new RelayCommand(CloseShare);
        ClearScopeCommand = new RelayCommand(() => (Content as EntriesViewModel)?.ClearScopeCommand.Execute(null));
        ImportCommand = new AsyncRelayCommand(PickImportAsync, () => _picker is not null && _import is null);
        CancelTouchCommand = new RelayCommand(_session.CancelHardwareKeyWait, () => _waitingForTouch);

        MainNav = [.. Destinations.Main.Select(d => new NavItem(d) { HasDot = d.Kind == DestinationKind.AgentActivity })];
        FooterNav = [.. Destinations.Footer.Select(d => new NavItem(d))];
        _sidebarRows = SidebarOf([]);

        _current = Destinations.Places[0];
        _session.LockingSoon += OnLockingSoon;
        _session.Edited += OnEdited;
        _session.WaitingForTouch += OnWaitingForTouch;
        _session.Saved += OnSaved;

        if (authority is not null && _session.Identity is { } identity)
        {
            EntryActivity = new EntryActivitySource(authority, Core.Audit.KeypasteHome.AuditPath(home), identity.Key, _clock, post);
        }

        Recommendations = new RecommendationsViewModel(session, home);
        Recommendations.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RecommendationsViewModel.NeedsReview))
            {
                SetCount(DestinationKind.Settings, Recommendations.NeedsReview);
            }
        };

        // Built here rather than left to the first navigation. Assigning Current to the destination
        // it already holds changes nothing, so Show never ran and the shell opened on a blank pane.
        Show(_current);
        SetCount(DestinationKind.Settings, Recommendations.NeedsReview);
        ReadAuthority();
        ReadVaultStatus();

        _statusTimer = _clock.CreateTimer(_ => Post(OnStatusTick), null, _statusTick, _statusTick);
    }

    /// <summary>What the restore that opened this vault did, until it is dismissed or the vault locks.</summary>
    /// <remarks>
    /// Said after the fact as well as before it, because only afterwards is it known whether the
    /// replaced file was kept, was already kept, or was never there.
    /// </remarks>
    internal string? Notice
    {
        get => _notice;
        private set
        {
            if (Set(ref _notice, value))
            {
                Raise(nameof(HasNotice));
            }
        }
    }

    internal bool HasNotice => _notice is not null;

    internal RelayCommand DismissNoticeCommand { get; }

    /// <summary>Whether a save is waiting for the vault's YubiKey to be touched.</summary>
    internal bool IsWaitingForTouch
    {
        get => _waitingForTouch;
        private set
        {
            if (Set(ref _waitingForTouch, value))
            {
                CancelTouchCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Stops waiting for the YubiKey, so the save fails and writes nothing.</summary>
    internal RelayCommand CancelTouchCommand { get; }

    /// <summary>Keys left in notes, checked on unlock and after each save, and listed only in Settings.</summary>
    internal RecommendationsViewModel Recommendations { get; }

    /// <summary>The auto-clearing clipboard, and the toast that counts it down.</summary>
    internal ClipboardCountdown Clipboard { get; }

    /// <summary>The value of <c>KEYPASTE_HOME</c>, or null.</summary>
    internal string? Home { get; }

    /// <summary>What serves this vault to agents, or null where nothing does.</summary>
    internal AppAuthority? Authority { get; }

    /// <summary>What agents did with this vault's entries, read every few seconds; null where nothing serves agents.</summary>
    internal EntryActivitySource? EntryActivity { get; }

    /// <summary>How a theme choice reaches the application object.</summary>
    /// <remarks>
    /// Passed in rather than reached for, so this class still names no Avalonia type and its tests
    /// still need no application.
    /// </remarks>
    internal Action<Core.Settings.AppTheme> ApplyTheme { get; }

    /// <summary>The preferences the app was composed from.</summary>
    /// <remarks>
    /// Held here rather than re-read by the Settings screen, so the screen shows the timeout and
    /// theme that are actually in force. Two independent reads of one file agree only until they
    /// do not, and the moment they disagree is the moment a person is looking at a security
    /// setting that is not the one protecting them.
    /// </remarks>
    internal DesktopPreferences Preferences { get; }

    /// <summary>Every destination, in sidebar order.</summary>
    /// <remarks>The trailing underscore keeps it from colliding with the <see cref="Navigation.Destinations"/> class.</remarks>
#pragma warning disable CA1822
    internal IReadOnlyList<Destination> Destinations_ => Navigation.Destinations.All;
#pragma warning restore CA1822

    /// <summary>The sidebar's main rows.</summary>
    internal IReadOnlyList<NavItem> MainNav { get; }

    /// <summary>The sidebar's quieter rows at the bottom: Settings and Trash.</summary>
    internal IReadOnlyList<NavItem> FooterNav { get; }

    /// <summary>
    /// The sidebar's main list, in reading order: Items, the project rows beneath it, then Agents (D-0374).
    /// </summary>
    /// <remarks>
    /// One list rather than three, so the arrow keys walk the sidebar as a person reads it and its
    /// automation tree names exactly the places and the projects.
    /// </remarks>
    internal IReadOnlyList<object> SidebarRows
    {
        get => _sidebarRows;
        private set => Set(ref _sidebarRows, value);
    }

    /// <summary>
    /// The sidebar row for where the shell is: the open project's row on its Env profiles, otherwise
    /// the place the screen is, or is under. Choosing a row goes there.
    /// </summary>
    internal object? SelectedSidebarRow
    {
        get
        {
            if (_current.Kind == DestinationKind.EnvSets && _openProject is { } open
                && _projects.FirstOrDefault(row => row.Name == open) is { } project)
            {
                return project;
            }

            return SelectedMain;
        }
        set
        {
            // Replacing the rows makes the list re-select, and write back, whichever row it had.
            if (_rebuildingSidebar)
            {
                return;
            }

            switch (value)
            {
                case NavItem item:
                    Current = item.Destination;
                    break;
                case ProjectRow project:
                    OpenProject(project.Name);
                    break;
            }
        }
    }

    /// <summary>The main row for the place <see cref="Current"/> is, or is under; null while it is a footer place.</summary>
    internal NavItem? SelectedMain
    {
        get => MainNav.FirstOrDefault(item => item.Destination == Destinations.PlaceOf(_current));
        set
        {
            if (value is not null)
            {
                Current = value.Destination;
            }
        }
    }

    /// <summary>The footer row for the place <see cref="Current"/> is, or is under; null while it is a main place.</summary>
    internal NavItem? SelectedFooter
    {
        get => FooterNav.FirstOrDefault(item => item.Destination == Destinations.PlaceOf(_current));
        set
        {
            if (value is not null)
            {
                Current = value.Destination;
            }
        }
    }

    /// <summary>The vault's env projects, by name, with their variable counts.</summary>
    internal IReadOnlyList<ProjectRow> Projects
    {
        get => _projects;
        private set
        {
            if (_projects.SequenceEqual(value))
            {
                return;
            }

            _projects = value;
            Raise();
            Raise(nameof(HasProjects));
            _rebuildingSidebar = true;

            try
            {
                SidebarRows = SidebarOf(value);
            }
            finally
            {
                _rebuildingSidebar = false;
            }

            Raise(nameof(SelectedSidebarRow));
        }
    }

    internal bool HasProjects => _projects.Count > 0;

    /// <summary>Opens Env profiles on a project.</summary>
    internal RelayCommand<string> OpenProjectCommand { get; }

    /// <summary>Goes up from a screen under a place to the place.</summary>
    internal RelayCommand BackCommand { get; }

    /// <summary>Whether the shell shows a screen under a place, which offers Back.</summary>
    internal bool HasBack => !_current.IsPlace;

    /// <summary>What Back says: the place it returns to.</summary>
    internal string BackTitle => Destinations.PlaceOf(_current).Title;

    /// <summary>Items' "+" menu: New project, on Env profiles.</summary>
    internal RelayCommand NewProjectCommand { get; }

    /// <summary>Items' "+" menu: Import .env, into the project in view or the first one.</summary>
    internal RelayCommand ImportEnvCommand { get; }

    /// <summary>An item's ⋯ menu: Share…, as a dialog over the screen.</summary>
    internal RelayCommand<string> ShareCommand { get; }

    internal RelayCommand CloseShareCommand { get; }

    /// <summary>The share dialog while it is open, or null.</summary>
    internal SharingViewModel? Share
    {
        get => _share;
        private set
        {
            if (Set(ref _share, value))
            {
                Raise(nameof(HasShare));
                ShareCommand.RaiseCanExecuteChanged();
            }
        }
    }

    internal bool HasShare => _share is not null;

    /// <summary>Asks which KDBX file to import, then shows the import dialog over the shell.</summary>
    internal AsyncRelayCommand ImportCommand { get; }

    /// <summary>The KDBX import dialog while it is open, or null.</summary>
    internal KdbxImportViewModel? Import
    {
        get => _import;
        private set
        {
            if (Set(ref _import, value))
            {
                Raise(nameof(HasImport));
                ImportCommand.RaiseCanExecuteChanged();
            }
        }
    }

    internal bool HasImport => _import is not null;

    /// <summary>Shows the import dialog for <paramref name="path"/>, replacing one already open.</summary>
    internal void OpenImport(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (_disposed)
        {
            return;
        }

        _import?.Dispose();

        var import = new KdbxImportViewModel(
            _session,
            path,
            _openInPlace ?? ((_, _) => _session.Lock(VaultLockReason.Manual)),
            Imported,
            _picker is null ? null : _picker.PickKeyfileAsync,
            _picker is null ? null : PickImportAsync);

        import.Closed += (_, _) =>
        {
            if (ReferenceEquals(Import, import))
            {
                Import = null;
            }
        };

        Import = import;
    }

    /// <summary>Says what an import copied, and rebuilds the screen so it lists the new entries.</summary>
    private void Imported(string message)
    {
        ShowToast(message);
        Show(_current);
    }

    private async Task PickImportAsync()
    {
        if (_picker is not null && await _picker.PickExistingAsync().ConfigureAwait(true) is { } path)
        {
            OpenImport(path);
        }
    }

    /// <summary>The open vault's file name. The full path is a tooltip, never a heading.</summary>
    internal string VaultName =>
        _session.VaultPath is { } path ? Path.GetFileName(path) : string.Empty;

    /// <summary>The open vault's full path, for the tooltip.</summary>
    internal string VaultPath => _session.VaultPath ?? string.Empty;

    /// <summary>The titlebar's status line: whether the file holds what is open.</summary>
    internal string VaultStatus
    {
        get => _vaultStatus;
        private set => Set(ref _vaultStatus, value);
    }

    /// <summary>The colour of the titlebar's dot.</summary>
    internal StatusTone VaultStatusTone
    {
        get => _vaultStatusTone;
        private set
        {
            if (Set(ref _vaultStatusTone, value))
            {
                Raise(nameof(VaultStatusOk));
                Raise(nameof(VaultStatusAccent));
                Raise(nameof(VaultStatusDanger));
            }
        }
    }

    internal bool VaultStatusOk => _vaultStatusTone == StatusTone.Ok;

    internal bool VaultStatusAccent => _vaultStatusTone == StatusTone.Accent;

    internal bool VaultStatusDanger => _vaultStatusTone == StatusTone.Danger;

    /// <summary>The titlebar's tooltip: when the file was saved, and where it is.</summary>
    internal string VaultStatusDetail
    {
        get => _vaultStatusDetail;
        private set => Set(ref _vaultStatusDetail, value);
    }

    /// <summary>Locks now.</summary>
    internal RelayCommand LockCommand { get; }

    /// <summary>Whether this app is answering agents for the vault, as its authority says; the Agents row's dot.</summary>
    internal bool AgentsServing
    {
        get => _agentsServing;
        private set
        {
            if (Set(ref _agentsServing, value))
            {
                MainNav.Single(item => item.Destination.Kind == DestinationKind.AgentActivity).DotLive = value;
            }
        }
    }

    /// <summary>The Agents row's tooltip: what the authority knows, and nothing it does not.</summary>
    internal string AgentsDetail
    {
        get => _agentsDetail;
        private set
        {
            if (Set(ref _agentsDetail, value))
            {
                MainNav.Single(item => item.Destination.Kind == DestinationKind.AgentActivity).Detail = value;
            }
        }
    }

    /// <summary>The titlebar search, the app's only one. It filters Items, and moves there to do it.</summary>
    internal string Search
    {
        get => _search;
        set
        {
            if (!Set(ref _search, value ?? string.Empty))
            {
                return;
            }

            if (_search.Length > 0 && _current.Kind != DestinationKind.Entries)
            {
                Current = Destinations.Of(DestinationKind.Entries);
            }
            else if (Content is EntriesViewModel entries)
            {
                entries.Search = _search;
            }
        }
    }

    /// <summary>The group the search is limited to, or null when it searches every item.</summary>
    internal string? SearchScope => Content is EntriesViewModel entries ? entries.SearchScope : null;

    internal bool HasSearchScope => SearchScope is not null;

    /// <summary>What the search box says while empty: where it searches.</summary>
    internal string SearchPlaceholder => SearchScope is null ? "Search all items" : "Search in this group";

    /// <summary>Widens the search to every item.</summary>
    internal RelayCommand ClearScopeCommand { get; }

    /// <summary>Raised when <c>Ctrl/Cmd+K</c> asks for the titlebar search.</summary>
    internal event EventHandler? SearchFocusRequested;

    internal void FocusSearch() => SearchFocusRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>The shortcut hint on the search field, in the platform's own spelling.</summary>
    public static string SearchShortcut => OperatingSystem.IsMacOS() ? "⌘K" : "Ctrl K";

    /// <summary>A short confirmation in the bottom-right corner, or null.</summary>
    internal string? Toast
    {
        get => _toast;
        private set
        {
            if (Set(ref _toast, value))
            {
                Raise(nameof(HasToast));
            }
        }
    }

    internal bool HasToast => _toast is not null;

    internal RelayCommand DismissToastCommand { get; }

    /// <summary>
    /// Says <paramref name="message"/> in a toast for <see cref="ToastDuration"/>. A second call
    /// replaces the first and restarts the time.
    /// </summary>
    /// <remarks>Never put a secret in it: a toast is a line of plain text on screen.</remarks>
    internal void ShowToast(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (_disposed)
        {
            return;
        }

        _toastTimer?.Dispose();
        Toast = message;
        _toastTimer = _clock.CreateTimer(_ => Post(() => Toast = null), null, ToastDuration, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Where the sidebar is.</summary>
    internal Destination Current
    {
        get => _current;
        set
        {
            if (value is not null && Set(ref _current, value))
            {
                if (value.Kind != DestinationKind.EnvSets)
                {
                    _openProject = null;
                }

                Raise(nameof(CurrentTitle));
                Raise(nameof(ShowsHeader));
                Raise(nameof(HasBack));
                Raise(nameof(BackTitle));
                BackCommand.RaiseCanExecuteChanged();
                Show(value);
                Raise(nameof(SelectedMain));
                Raise(nameof(SelectedFooter));
                Raise(nameof(SelectedSidebarRow));
            }
        }
    }

    /// <summary>The current destination's title, for the content header.</summary>
    internal string CurrentTitle => _current.Title;

    /// <summary>Whether the shell draws the title above a screen that does not draw its own.</summary>
    internal bool ShowsHeader => !_current.OwnsHeader;

    /// <summary>Whatever the current destination shows.</summary>
    internal object? Content
    {
        get => _content;
        private set => Set(ref _content, value);
    }

    /// <summary>
    /// A quiet line that appears shortly before the vault locks, and disappears on any input.
    /// </summary>
    /// <remarks>
    /// Muted, not red, and not a dialog: an auto-lock is the most normal thing this app does.
    /// </remarks>
    internal string Countdown
    {
        get => _countdown;
        private set
        {
            if (Set(ref _countdown, value))
            {
                Raise(nameof(HasCountdown));
            }
        }
    }

    internal bool HasCountdown => _countdown.Length > 0;

    /// <summary>Clears the countdown, because somebody is evidently still here.</summary>
    internal void ClearCountdown() => Countdown = string.Empty;

    /// <summary>Moves to a place by its shortcut digit.</summary>
    /// <param name="digit">A place's position in the sidebar, from 1.</param>
    /// <returns><see langword="true"/> when a place matched.</returns>
    internal bool GoTo(int digit)
    {
        foreach (var destination in Navigation.Destinations.Places)
        {
            if (destination.Shortcut == digit)
            {
                Current = destination;
                return true;
            }
        }

        return false;
    }

    /// <summary>Builds the current destination's content.</summary>
    private void Show(Destination destination)
    {
        if (Content is EntriesViewModel previous)
        {
            previous.PropertyChanged -= OnEntriesChanged;
        }

        (Content as IDisposable)?.Dispose();

        Content = destination.Kind switch
        {
            // The audit log is machine state, which is why `keypaste log` reads it without a vault.
            DestinationKind.Log => new LogViewModel(Home, _clock, Clipboard),
            DestinationKind.AgentHistory => new LogViewModel(Home, _clock, Clipboard, agentsOnly: true),
            DestinationKind.Settings => new SettingsViewModel(_session, Home, Preferences, ApplyTheme, _picker, Recommendations, kind => Current = Destinations.Of(kind)),
            DestinationKind.AgentActivity => Activity(),
            DestinationKind.Entries => Entries(),
            DestinationKind.EnvSets => new EnvSetsViewModel(_session, Clipboard, _picker, toast: ShowToast),
            DestinationKind.Trash => new TrashViewModel(_session),
            DestinationKind.Sharing => Sharing(),
            _ => null,
        };

        if (Content is EntriesViewModel entries)
        {
            entries.PropertyChanged += OnEntriesChanged;
        }

        RaiseScope();
        Count();
    }

    private EntriesViewModel Entries()
    {
        var entries = new EntriesViewModel(_session, Clipboard, EntryActivity);

        if (_search.Length > 0)
        {
            entries.Search = _search;
        }

        return entries;
    }

    private AgentActivityViewModel Activity() =>
        new(Authority, Home, _clock, _post, toast: ShowToast, clipboard: Clipboard, openHistory: () => Current = Destinations.Of(DestinationKind.AgentHistory));

    private void OnEntriesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EntriesViewModel.SearchScope))
        {
            RaiseScope();
        }
    }

    private void RaiseScope()
    {
        Raise(nameof(SearchScope));
        Raise(nameof(HasSearchScope));
        Raise(nameof(SearchPlaceholder));
    }

    /// <summary>How share links reach their server; the shell makes one when none is given.</summary>
    /// <remarks>Redirects are never followed, so an envelope reaches only the origin the link names.</remarks>
    internal HttpMessageHandler? ShareTransport { get; init; }

    private SocketsHttpHandler? _ownShareTransport;

    private SharingViewModel Sharing(string? what = null, Action? cancel = null)
    {
        var resolved = ShareEndpoint.TryResolve(null, Environment.GetEnvironmentVariable(ShareEndpoint.EnvironmentVariable), out var endpoint, out _);
        var transport = ShareTransport ?? (_ownShareTransport ??= new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = System.Net.DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(10),
        });
        var auditPath = Core.Audit.KeypasteHome.AuditPath(Home);
        var service = new ShareService(
            new ShareClient(transport, endpoint ?? ShareEndpoint.Default),
            _clock,
            () => Core.Audit.AuditLog.TryOpen(auditPath, _clock, out var log, out _) ? log : null);

        return new SharingViewModel(
            _session,
            Clipboard,
            service,
            ShowToast,
            resolved ? null : $"Sharing is off: {ShareEndpoint.EnvironmentVariable} may only name a local development server.",
            what,
            cancel);
    }

    /// <summary>Opens the share dialog on one entry, over whatever screen is showing.</summary>
    internal void OpenShare(string? path)
    {
        if (_disposed || path is null || _share is not null)
        {
            return;
        }

        var share = Sharing(path, CloseShare);
        share.Created += (_, _) => CloseShare();
        Share = share;
    }

    private void CloseShare()
    {
        var share = _share;
        Share = null;
        share?.Dispose();
    }

    /// <summary>New project, from Items' "+" menu: Env profiles, with its create form open.</summary>
    private void NewProject()
    {
        Current = Destinations.Of(DestinationKind.EnvSets);

        if (Content is EnvSetsViewModel env)
        {
            env.BeginAddCommand.Execute(null);
        }
    }

    /// <summary>Import .env, from Items' "+" menu: into the project in view, else the first; with none, create one first.</summary>
    private void ImportEnv()
    {
        var inView = (Content as EntriesViewModel)?.SelectedGroup is { IsEverything: false } group
            ? EnvPlace.OfGroup(group.Path)?.Project
            : null;
        var project = inView ?? (_projects.Count > 0 ? _projects[0].Name : null);

        if (project is null)
        {
            NewProject();
            return;
        }

        OpenProject(project);

        if ((Content as EnvSetsViewModel)?.OpenProject is { } open)
        {
            open.Import.ChooseCommand.Execute(null);
        }
    }

    private void OpenProject(string? project)
    {
        if (project is null)
        {
            return;
        }

        _openProject = project;
        Current = Destinations.Of(DestinationKind.EnvSets);

        if (Content is EnvSetsViewModel env)
        {
            env.OpenCommand.Execute(project);
        }

        Raise(nameof(SelectedSidebarRow));
    }

    /// <summary>The sidebar's main list: Items, then each project, then Agents.</summary>
    private IReadOnlyList<object> SidebarOf(IReadOnlyList<ProjectRow> projects) =>
        [MainNav[0], .. projects, .. MainNav.Skip(1)];

    /// <summary>Counts entries and env projects for the sidebar, reading names only.</summary>
    private void Count()
    {
        if (_disposed || _session.Unlocked is not { } vault)
        {
            Projects = [];
            return;
        }

        IReadOnlyList<string> names;
        Dictionary<string, int> perGroup = new(StringComparer.Ordinal);
        int total;

        try
        {
            // keypaste's own records, share links among them, are not secrets the Secrets list shows.
            var entries = vault.ReadEntries().Where(entry => !ReservedGroups.IsReserved(entry.GroupPath)).ToList();
            total = entries.Count;

            foreach (var entry in entries)
            {
                if (entry.Title.Length > 0)
                {
                    perGroup[entry.GroupPath] = perGroup.GetValueOrDefault(entry.GroupPath) + 1;
                }
            }

            names = new EnvStore(vault).Projects();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        Projects = [.. names.Select(name => new ProjectRow(name, perGroup.GetValueOrDefault(EnvConvention.GroupPath(name))))];
        SetCount(DestinationKind.Entries, total);
    }

    private void SetCount(DestinationKind kind, int count)
    {
        foreach (var item in MainNav.Concat(FooterNav))
        {
            if (item.Destination.Kind == kind)
            {
                item.Count = count > 0 ? count.ToString(CultureInfo.InvariantCulture) : string.Empty;
            }
        }
    }

    private void ReadAuthority()
    {
        if (_disposed)
        {
            return;
        }

        var status = Authority?.Status ?? new AuthorityStatus.Locked();

        if (status is AuthorityStatus.Serving)
        {
            var activity = Authority!.Activity;
            var grants = activity.Grants.Count + activity.EnvGrants.Count;
            var waiting = activity.Waiting.Count + activity.WaitingRuns.Count + activity.WaitingEnvs.Count;
            var clients = Core.Clients.McpClientCards.Count(Authority.Clients);

            AgentsServing = true;
            AgentsDetail = waiting > 0
                ? string.Create(CultureInfo.InvariantCulture, $"Answering agents · {waiting} waiting · {ClientCount(clients)}")
                : "Answering agents · " + ClientCount(clients);
            SetCount(DestinationKind.AgentActivity, grants + waiting);
            return;
        }

        AgentsServing = false;
        AgentsDetail = status switch
        {
            AuthorityStatus.HeldBy => "another keypaste holds this vault",
            AuthorityStatus.NotServing => "agents cannot reach this vault",
            _ => "not serving agents",
        };
        SetCount(DestinationKind.AgentActivity, 0);
    }

    private static string ClientCount(int count) => count switch
    {
        0 => "no clients",
        1 => "1 client",
        _ => string.Create(CultureInfo.InvariantCulture, $"{count} clients"),
    };

    private void OnStatusTick()
    {
        ReadAuthority();

        // The file is hashed at most every five seconds; an edit or a save says so at once.
        if (++_ticks % 5 == 0)
        {
            ReadVaultStatus();
        }
    }

    private void ReadVaultStatus()
    {
        if (_disposed)
        {
            return;
        }

        if (_session.Unlocked is not { } vault || VaultName.Length == 0)
        {
            VaultStatus = string.Empty;
            VaultStatusDetail = string.Empty;
            VaultStatusTone = StatusTone.Ok;
            return;
        }

        VaultSaveState state;

        try
        {
            state = vault.SaveState();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        (VaultStatus, VaultStatusTone) = state.Status switch
        {
            VaultSaveStatus.Saved => ($"{VaultName} · saved", StatusTone.Ok),
            VaultSaveStatus.Unsaved => ($"{VaultName} · unsaved changes", StatusTone.Accent),
            VaultSaveStatus.ChangedOnDisk => ($"{VaultName} · changed on disk", StatusTone.Danger),
            _ => ($"{VaultName} · file unreadable", StatusTone.Danger),
        };

        VaultStatusDetail = state.SavedAt is { } saved
            ? $"Saved {saved.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)} · {VaultPath}"
            : VaultPath;
    }

    private void OnSaved(object? sender, EventArgs e) => Post(() =>
    {
        if (!_disposed)
        {
            ReadVaultStatus();
            Recommendations.Check();
        }
    });

    private void OnEdited(object? sender, VaultEdit edit) => Post(() =>
    {
        Count();
        ReadVaultStatus();
    });

    private void Post(Action action)
    {
        if (_post is { } post)
        {
            post(action);
        }
        else
        {
            action();
        }
    }

    private void OnWaitingForTouch(object? sender, bool waiting) => Post(() =>
    {
        if (!_disposed)
        {
            IsWaitingForTouch = waiting;
        }
    });

    private void OnLockingSoon(object? sender, TimeSpan remaining) =>
        Countdown = $"Locking in {Math.Max(1, (int)remaining.TotalSeconds)} seconds.";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.LockingSoon -= OnLockingSoon;
        _session.Edited -= OnEdited;
        _session.WaitingForTouch -= OnWaitingForTouch;
        _session.Saved -= OnSaved;
        Recommendations.Dispose();
        CloseShare();

        _statusTimer?.Dispose();
        _toastTimer?.Dispose();
        EntryActivity?.Dispose();
        _import?.Dispose();
        Import = null;
        Notice = null;
        Toast = null;
        Projects = [];
        (Content as IDisposable)?.Dispose();
        Content = null;
        _ownShareTransport?.Dispose();

        // A secret on the clipboard is derived from an open vault, so it does not survive the lock
        // either. Disposing clears it, conditionally — a clipboard the user has changed since is
        // left alone.
        Clipboard.Dispose();
    }
}
