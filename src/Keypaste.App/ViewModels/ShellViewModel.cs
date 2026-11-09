using System.Globalization;
using Keypaste.App.Clipboard;
using Keypaste.App.Globalization;
using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Editing;
using Keypaste.Core.Linking;
using Keypaste.Core.Login;
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
/// <b>The sidebar reads names and counts, never values.</b> A count comes from the entry list the
/// Secrets screen reads or from the project catalog, and is dropped the moment it is counted.
/// </para>
/// </remarks>
/// <summary>How a status dot is drawn.</summary>
internal enum StatusTone
{
    /// <summary>All is as it should be.</summary>
    Ok = 0,

    /// <summary>Saving or merging is still to come: drawn idle, since the view's one amber is its primary action.</summary>
    Pending = 1,

    /// <summary>Something is wrong.</summary>
    Danger = 2,

    /// <summary>Nothing to report.</summary>
    Muted = 3,
}

internal sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan _statusTick = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _countdownTick = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _fileSettle = TimeSpan.FromMilliseconds(250);

    private readonly AppVaultSession _session;
    private readonly IVaultFilePicker? _picker;
    private readonly IWebLauncher? _web;
    private readonly TimeProvider _clock;
    private readonly Action<Action>? _post;
    private readonly ITimer? _statusTimer;
    private readonly ITimer? _fileTimer;
    private readonly FileSystemWatcher? _fileWatch;
    private ITimer? _countdownTimer;
    private TimeSpan _lockWindow;
    private DateTimeOffset _warnedWall;
    private long _warnedStamp;
    private Destination _current;
    private string _search = string.Empty;
    private static readonly SidebarHeading _projectsHeading = new("Projects");

    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private IReadOnlyList<ProjectRow> _projects = [];
    private IReadOnlyList<GroupNode> _groups = [];
    private string? _openProject;
    private bool _rebuildingSidebar;
    private int _conflictedCopiesSaid;
    private bool _agentsServing;
    private bool _agentsRefusing;
    private string _agentsDetail = string.Empty;
    private string? _reloadFailure;
    private Task _merging = Task.CompletedTask;
    private bool _isMerging;
    private DateTimeOffset? _updatedAt;
    private bool _noticeNamesAConflict;
    private int _ticks;
    private readonly Action<string, string?, int?>? _openInPlace;
    private Action? _discard;
    private Action? _keep;
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
        Action<string, string?, int?>? openInPlace = null,
        IWebLauncher? web = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _web = web;
        Notice = notice;
        _picker = picker;
        _openInPlace = openInPlace;
        Home = home;
        Authority = authority;
        ApplyTheme = applyTheme ?? (_ => { });
        Preferences = preferences ?? new DesktopPreferences(home);
        SidebarHidden = Preferences.Current.Window?.SidebarHidden ?? false;
        ListShare = Preferences.Current.Window?.ListShare;
        Tables = TableStates.Read(Preferences.Current.Window?.TableSorts, Preferences.Current.Window?.TableWidths);
        _clock = clock ?? TimeProvider.System;
        _post = post;

        // Owned here rather than by each screen, so a copy made on Secrets is still counting down
        // after a move to Env profiles — and is cleared by the lock, because this is disposed with
        // everything else the shell built.
        Clipboard = new ClipboardCountdown(
            clipboard ?? NoClipboard.Instance,
            _clock,
            post);

        LockCommand = new AsyncRelayCommand(() => _session.LockAsync(VaultLockReason.Manual));
        Toasts = new ToastStack(_clock, Post);
        BackCommand = new RelayCommand(() => Current = Destinations.UpOf(Current), () => HasBack);
        ShareCommand = new RelayCommand<EntryName>(OpenShare, item => item is not null && Share is null);
        ImportCommand = new AsyncRelayCommand(PickImportAsync, () => _picker is not null && !HasImport);
        CancelTouchCommand = new RelayCommand(_session.CancelHardwareKeyWait, () => IsWaitingForTouch);

        FoldCommand = new RelayCommand<object>(row => Fold(row, open: null));
        MainNav = [.. Destinations.Main.Select(d => new NavItem(d) { HasDot = d.Kind == DestinationKind.AgentActivity, IsExpandable = d.Kind == DestinationKind.Entries })];
        FooterNav = [.. Destinations.Footer.Select(d => new NavItem(d))];
        RailPlaces = [.. MainNav, .. FooterNav];
        SidebarRows = SidebarOf();

        _current = Destinations.Places[0];
        _session.LockingSoon += OnLockingSoon;
        _session.Edited += OnEdited;
        _session.WaitingForTouch += OnWaitingForTouch;
        _session.Saved += OnSaved;
        _session.Merged += OnMerged;

        if (authority is not null)
        {
            authority.TerminalEdited += OnTerminalEdited;
        }

        if (authority is not null && _session.Identity is { } identity)
        {
            EntryActivity = new EntryActivitySource(
                authority, Core.Audit.KeypasteHome.AuditPath(home), identity.Key, _clock, post);
        }

        Recommendations = new RecommendationsViewModel(session, home, OpenEntry, post);
        Recommendations.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(RecommendationsViewModel.NeedsReview) or nameof(RecommendationsViewModel.IsEmpty))
            {
                ShowRecommendationsRow();
            }

            if (e.PropertyName == nameof(RecommendationsViewModel.ConflictedCopies))
            {
                SayWhenACopyArrives();
            }
        };

        // Built here rather than left to the first navigation. Assigning Current to the destination
        // it already holds changes nothing, so Show never ran and the shell opened on a blank pane.
        Show(_current);
        ShowRecommendationsRow();
        ReadAuthority();
        ReadVaultStatus();

        _statusTimer = _clock.CreateTimer(_ => Post(OnStatusTick), null, _statusTick, _statusTick);
        _fileTimer = _clock.CreateTimer(_ => Post(ReadVaultStatus), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _fileWatch = WatchVaultFile();
    }

    /// <summary>
    /// What the restore that opened this vault did, or what a merge of another program's save carried and named, until it is
    /// dismissed or the vault locks; a later merge's notice follows one naming a conflict rather than replacing it.
    /// </summary>
    /// <remarks>
    /// Said after the fact as well as before it, because only afterwards is it known whether the
    /// replaced file was kept, was already kept, or was never there.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    internal partial string? Notice { get; private set; }

    internal bool HasNotice => Notice is not null;

    /// <summary>Whether the person hid the sidebar with View › Hide sidebar; it stays hidden at every width and across locks.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsSidebar))]
    [NotifyPropertyChangedFor(nameof(ShowsRail), nameof(ContentIsTight))]
    internal partial bool SidebarHidden { get; private set; }

    /// <summary>Whether the window is under <see cref="Views.WindowChrome.NarrowBelow"/>, where the sidebar collapses to the rail and Items drops two columns.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsSidebar))]
    [NotifyPropertyChangedFor(nameof(SidebarOverlays))]
    [NotifyPropertyChangedFor(nameof(ShowsRail), nameof(ContentIsTight))]
    [NotifyPropertyChangedFor(nameof(SidebarWayBack))]
    internal partial bool IsNarrow { get; set; }

    /// <summary>Whether the rail opened the whole sidebar over the content, which keeps its width, so a group or a project can still be chosen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsSidebar))]
    [NotifyPropertyChangedFor(nameof(SidebarOverlays))]
    [NotifyPropertyChangedFor(nameof(ShowsRail), nameof(ContentIsTight))]
    private partial bool SidebarOpenedOver { get; set; }

    /// <summary>Whether the whole sidebar is on the screen.</summary>
    internal bool ShowsSidebar => IsNarrow ? SidebarOpenedOver : !SidebarHidden;

    /// <summary>Whether the rail of icons stands in for the sidebar: a narrow window, unless the person hid it or it is open over the content.</summary>
    internal bool ShowsRail => IsNarrow && !SidebarHidden && !SidebarOpenedOver;

    /// <summary>The window's width, in the design's pixels, which <see cref="ContentIsTight"/> is judged from.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContentIsTight))]
    internal partial double WindowWidth { get; set; }

    /// <summary>Whether the content is under <see cref="Views.WindowChrome.ContentTightBelow"/> wide, where the log drops a column, whatever takes the rest of the window.</summary>
    internal bool ContentIsTight =>
        WindowWidth - (ShowsRail ? Views.WindowChrome.RailWidth : !IsNarrow && !SidebarHidden ? Views.WindowChrome.SidebarWidth : 0) < Views.WindowChrome.ContentTightBelow;

    /// <summary>What a narrow window's titlebar button adds to its name: every way back, the button being all that is left of the places there.</summary>
    internal string? SidebarWayBack => IsNarrow
        ? $"The sidebar is hidden. Show it here, from the View menu or with {(OperatingSystem.IsMacOS() ? "⌃⌘S" : "Ctrl+B")}."
        : null;

    /// <summary>Whether the sidebar lies over the content rather than beside it.</summary>
    internal bool SidebarOverlays => IsNarrow && SidebarOpenedOver;

    /// <summary>The list's thousandths of the height it shares with the preview, or null until the divider moves.</summary>
    internal int? ListShare { get; set; }

    /// <summary>How each table was sorted and sized, which outlives the screens drawing them and is kept with the window.</summary>
    internal TableStates Tables { get; }

    /// <summary>The table the place in view draws, or null.</summary>
    internal TableState? TableInView => Content switch
    {
        EntriesViewModel => Tables.Items,
        TrashViewModel => Tables.Trash,
        LogViewModel => Tables.Log,
        _ => null,
    };

    /// <summary>View › Hide sidebar and Show sidebar, and the titlebar's button: the person's kept choice at every width, which hides the rail too.</summary>
    [RelayCommand]
    internal void ToggleSidebar() => SidebarHidden = !SidebarHidden;

    /// <summary>The rail's button and View › Show groups and projects: the whole sidebar over the content, for what does not fit in icons.</summary>
    [RelayCommand]
    internal void OpenSidebarOverlay() => SidebarOpenedOver = ShowsRail;

    /// <summary>Closes a sidebar lying over the content: a press outside it, or Escape.</summary>
    internal void CloseSidebarOverlay() => SidebarOpenedOver = false;

    partial void OnIsNarrowChanged(bool value) => SidebarOpenedOver = false;

    partial void OnSidebarHiddenChanged(bool value) => SidebarOpenedOver = false;

    /// <summary>What the notice says while the file holds another program's save that keypaste did not merge, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVaultChanged))]
    internal partial string? VaultChanged { get; private set; }

    internal bool HasVaultChanged => VaultChanged is not null;

    /// <summary>What the vault holds has just changed under the screen, whichever screen is open and whether or not it reads again.</summary>
    internal event Action? VaultMoved;

    /// <summary>The merge of another program's save the poll or the folder watch started, or a completed task when none runs.</summary>
    internal Task Merging => _merging;

    /// <summary>Whether a save is waiting for the vault's YubiKey to be touched.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelTouchCommand))]
    internal partial bool IsWaitingForTouch { get; private set; }

    /// <summary>Stops waiting for the YubiKey, so the save fails and writes nothing.</summary>
    internal RelayCommand CancelTouchCommand { get; }

    /// <summary>Recommendations, checked on unlock and after each save, and listed only on their screen under Settings.</summary>
    internal RecommendationsViewModel Recommendations { get; }

    /// <summary>The auto-clearing clipboard, and the toast that counts it down.</summary>
    internal ClipboardCountdown Clipboard { get; }

    /// <summary>The value of <c>KEYPASTE_HOME</c>, or null.</summary>
    internal string? Home { get; }

    /// <summary>What serves this vault to agents, or null where nothing does.</summary>
    internal AppAuthority? Authority { get; }

    /// <summary>What agents did with this vault's entries, read every few seconds; null where nothing serves agents.</summary>
    internal EntryActivitySource? EntryActivity { get; }

    /// <summary>Whether Settings offers a theme, which it does only where the system reports no light or dark of its own.</summary>
    internal bool OffersTheme { get; init; }

    /// <summary>Told when the person opens a place where a system dialog interrupts nothing.</summary>
    internal Action? CalmMoment { get; init; }

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
    /// <summary>The sidebar's row for Recommendations, under Items while there is anything to list.</summary>
    internal NavItem RecommendationsRow { get; } = new(Destinations.Of(DestinationKind.Recommendations));

    internal IReadOnlyList<NavItem> MainNav { get; }

    /// <summary>The sidebar's quieter rows at the bottom: Settings and Trash.</summary>
    internal IReadOnlyList<NavItem> FooterNav { get; }

    /// <summary>The four places, in the order the rail draws them.</summary>
    internal IReadOnlyList<NavItem> RailPlaces { get; }

    /// <summary>
    /// The sidebar's main list, in reading order: Items with the vault's group tree folded under it,
    /// the projects under their heading, then Agents.
    /// </summary>
    /// <remarks>
    /// One list rather than several, so the arrow keys walk the sidebar as a person reads it; Left and
    /// Right fold and unfold a row, as they do in KeePassXC's tree.
    /// </remarks>
    [ObservableProperty]
    internal partial IReadOnlyList<object> SidebarRows { get; private set; } = [];

    /// <summary>
    /// The sidebar row for where the shell is: the open project's row on its Env profiles, the group
    /// Items shows, otherwise the place the screen is, or is under. Choosing a row goes there.
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

            if (_current.Kind == DestinationKind.Entries && (Content as EntriesViewModel)?.SelectedGroup is { IsEverything: false } group
                && SidebarRows.OfType<GroupRow>().FirstOrDefault(row => row.Path == group.Path) is { } shown)
            {
                return shown;
            }

            return SelectedMain;
        }
        set
        {
            // Replacing the rows makes the list re-select, and write back, whichever row it had.
            if (_rebuildingSidebar || Equals(value, SelectedSidebarRow))
            {
                return;
            }

            switch (value)
            {
                case NavItem { Destination.Kind: DestinationKind.Entries }:
                    ShowGroup(string.Empty);
                    break;
                case NavItem item:
                    Current = item.Destination;
                    break;
                case GroupRow group:
                    ShowGroup(group.Path);
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

    /// <summary>The rail's row for the place <see cref="Current"/> is, or is under; a group or a project in view counts as Items.</summary>
    internal NavItem? SelectedRailPlace
    {
        get => RailPlaces.FirstOrDefault(item => item.Destination == Destinations.PlaceOf(_current));
        set
        {
            if (value is not null && !Equals(value, SelectedRailPlace))
            {
                SelectedSidebarRow = value;
            }
        }
    }

    /// <summary>The vault's projects, by name, with how many entries each holds.</summary>
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
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasProjects));
            RebuildSidebar();
        }
    }

    /// <summary>Folds or unfolds a row of the tree: Items, or a group with groups inside it.</summary>
    internal RelayCommand<object> FoldCommand { get; }

    /// <summary>
    /// Opens or closes a row of the tree, or flips it when <paramref name="open"/> is null. Folding away
    /// the group Items shows shows the folded row instead, as KeePassXC selects the parent it folds.
    /// </summary>
    internal void Fold(object? row, bool? open)
    {
        switch (row)
        {
            case NavItem { IsExpandable: true } items:
                items.IsExpanded = open ?? !items.IsExpanded;

                if (!items.IsExpanded)
                {
                    ShowGroupIfInside(string.Empty);
                }

                break;
            case GroupRow { HasChildren: true } group:
                if (open ?? !_expanded.Contains(group.Path))
                {
                    _expanded.Add(group.Path);
                }
                else
                {
                    _expanded.Remove(group.Path);
                    ShowGroupIfInside(group.Path);
                }

                break;
            default:
                return;
        }

        RebuildSidebar();
    }

    internal bool HasProjects => _projects.Count > 0;

    /// <summary>Goes up from a screen to the screen or place it sits under.</summary>
    internal RelayCommand BackCommand { get; }

    /// <summary>Whether the shell shows a screen under a place, which offers Back.</summary>
    internal bool HasBack => !_current.IsPlace;

    /// <summary>What Back says: the screen or place it returns to.</summary>
    internal string BackTitle => Destinations.UpOf(_current).Title;

    /// <summary>An item's ⋯ menu: Share…, as a dialog over the screen.</summary>
    internal RelayCommand<EntryName> ShareCommand { get; }

    /// <summary>The share dialog while it is open, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShare))]
    [NotifyCanExecuteChangedFor(nameof(ShareCommand))]
    internal partial SharingViewModel? Share { get; private set; }

    internal bool HasShare => Share is not null;

    /// <summary>Asks which file to import, a KeePass database or another app's export, then shows its dialog over the shell.</summary>
    internal AsyncRelayCommand ImportCommand { get; }

    /// <summary>The KeePass database import dialog while it is open, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKdbxImport), nameof(HasImport))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    internal partial KdbxImportViewModel? Import { get; private set; }

    /// <summary>The export import dialog while it is open, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExportImport), nameof(HasImport))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    internal partial ExportImportViewModel? ExportImport { get; private set; }

    internal bool HasKdbxImport => Import is not null;

    internal bool HasExportImport => ExportImport is not null;

    /// <summary>Whether either import dialog is open.</summary>
    internal bool HasImport => Import is not null || ExportImport is not null;

    /// <summary>
    /// Shows the import dialog for <paramref name="path"/>, replacing one already open: the KeePass database dialog for a file
    /// whose KeePass header reads, the export dialog for one an export reader recognizes, and otherwise the KeePass database
    /// dialog's refusal, naming the exports it is not either.
    /// </summary>
    internal void OpenImport(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (_disposed)
        {
            return;
        }

        Import?.Dispose();
        ExportImport?.Dispose();

        var readers = Core.Import.ExportImport.Readers;

        if (!Core.Import.KdbxImport.TryProbe(path, out _, out _) && Core.Import.ExportImport.Recognize(path, readers) is not null)
        {
            var export = new ExportImportViewModel(_session, path, Imported, readers, _picker is null ? null : PickImportAsync, _post);

            export.Closed += (_, _) =>
            {
                if (ReferenceEquals(ExportImport, export))
                {
                    ExportImport = null;
                }
            };

            ExportImport = export;
            _ = export.ReadAsync();
            return;
        }

        var import = new KdbxImportViewModel(
            _session,
            path,
            _openInPlace ?? ((_, _, _) => _session.Lock(VaultLockReason.Manual)),
            Imported,
            _picker is null ? null : _picker.PickKeyfileAsync,
            _picker is null ? null : PickImportAsync,
            Clipboard.TryPasteIntoAsync,
            _post,
            [.. readers.Select(reader => reader.Format)]);

        import.Closed += (_, _) =>
        {
            if (ReferenceEquals(Import, import))
            {
                Import = null;
            }
        };

        Import = import;
    }

    /// <summary>Says what an import copied, and rebuilds the screen so it lists the new items.</summary>
    private void Imported(string message)
    {
        if (_disposed)
        {
            return;
        }

        ShowDone(message);

        if (Content is not IHoldsDraft { UnsavedDraft: not null })
        {
            Show(_current);
        }
    }

    private async Task PickImportAsync()
    {
        if (_picker is not null && await _picker.PickImportAsync().ConfigureAwait(true) is { } path)
        {
            OpenImport(path);
        }
    }

    /// <summary>The open vault's file name. The full path is a tooltip, never a heading.</summary>
    internal string VaultName =>
        _session.VaultPath is { } path ? Shown(Path.GetFileName(path)) : string.Empty;

    /// <summary>The open vault's full path, for the tooltip.</summary>
    internal string VaultPath => _session.VaultPath ?? string.Empty;

    /// <summary>The titlebar's status line: whether the file holds what is open.</summary>
    [ObservableProperty]
    internal partial string VaultSaveWords { get; private set; } = string.Empty;

    /// <summary>The colour of the titlebar's dot.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VaultStatusOk), nameof(VaultStatusPending), nameof(VaultStatusDanger))]
    internal partial StatusTone VaultStatusTone { get; private set; }

    internal bool VaultStatusOk => VaultStatusTone == StatusTone.Ok;

    internal bool VaultStatusPending => VaultStatusTone == StatusTone.Pending;

    internal bool VaultStatusDanger => VaultStatusTone == StatusTone.Danger;

    /// <summary>The titlebar's tooltip: when the file was saved, and where it is.</summary>
    [ObservableProperty]
    internal partial string VaultStatusDetail { get; private set; } = string.Empty;

    /// <summary>Locks now, off the UI thread, so the window keeps painting until the endpoint has stopped.</summary>
    internal AsyncRelayCommand LockCommand { get; }

    /// <summary>Whether this app is answering agents for the vault, as its authority says; the Agents row's dot.</summary>
    internal bool AgentsServing
    {
        get => _agentsServing;
        private set
        {
            if (SetProperty(ref _agentsServing, value))
            {
                ShowAgentsDot();
            }
        }
    }

    /// <summary>
    /// Whether this app answers agents for the vault but refuses every request, because its audit log
    /// will not open; the Agents row's dot then shows danger in place of ok.
    /// </summary>
    internal bool AgentsRefusing
    {
        get => _agentsRefusing;
        private set
        {
            if (SetProperty(ref _agentsRefusing, value))
            {
                ShowAgentsDot();
            }
        }
    }

    /// <summary>The Agents row's tooltip: what the authority knows, and nothing it does not.</summary>
    internal string AgentsDetail
    {
        get => _agentsDetail;
        private set
        {
            if (SetProperty(ref _agentsDetail, value))
            {
                MainNav.Single(item => item.Destination.Kind == DestinationKind.AgentActivity).Detail = value;
            }
        }
    }

    /// <summary>The search in Items' list header, the app's only one, kept here across a trip to another place and rebuilds of the screen.</summary>
    internal string Search
    {
        get => _search;
        set
        {
            var before = _search;

            if (SetProperty(ref _search, value ?? string.Empty) && Content is EntriesViewModel)
            {
                Leave(() => (Content as EntriesViewModel)!.Search = _search, () => TakeBackSearch(before));
            }
        }
    }

    private void TakeBackSearch(string before)
    {
        _search = before;
        OnPropertyChanged(nameof(Search));
    }

    /// <summary>Raised when Find asks for the search, once Items is in view.</summary>
    internal event EventHandler? SearchFocusRequested;

    /// <summary>Find: the search is on Items, so it goes there from any other place, asking first about a draft it would leave.</summary>
    internal void FocusSearch()
    {
        CloseSidebarOverlay();

        if (_current.Kind == DestinationKind.Entries)
        {
            SearchFocusRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        Leave(() =>
        {
            MoveTo(Destinations.Of(DestinationKind.Entries));
            SearchFocusRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>Find's first chord on the search field, in the platform's own spelling.</summary>
    public static string SearchShortcut => OperatingSystem.IsMacOS() ? "⌘F" : "Ctrl F";

    /// <summary>What the bottom-right corner is saying.</summary>
    internal ToastStack Toasts { get; }

    internal void ShowDone(string message) => ShowToast(message, ToastTone.Success);

    internal void ShowToast(string message, ToastTone tone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (_disposed)
        {
            return;
        }

        Toasts.Show(message, tone);
    }

    /// <summary>Where the sidebar is.</summary>
    internal Destination Current
    {
        get => _current;
        set
        {
            if (value is not null && value != _current)
            {
                Leave(() => MoveTo(value));
            }
        }
    }

    private void MoveTo(Destination destination)
    {
        if (SetProperty(ref _current, destination, nameof(Current)))
        {
            OnPropertyChanged(nameof(CurrentTitle));
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(ShowsHeader));
            OnPropertyChanged(nameof(HasBack));
            OnPropertyChanged(nameof(BackTitle));
            OnPropertyChanged(nameof(SelectedMain));
            OnPropertyChanged(nameof(SelectedFooter));
            OnPropertyChanged(nameof(SelectedRailPlace));
            BackCommand.NotifyCanExecuteChanged();

            if (destination.Kind is DestinationKind.AgentActivity or DestinationKind.Settings)
            {
                CalmMoment?.Invoke();
            }

            if (destination.Kind != DestinationKind.EnvSets)
            {
                _openProject = null;
            }

            Show(destination);
            OnPropertyChanged(nameof(SelectedSidebarRow));
        }
    }

    /// <summary>What "Discard …?" names while it is asked over the window, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDiscardPrompt))]
    internal partial string? DiscardPrompt { get; private set; }

    internal bool HasDiscardPrompt => DiscardPrompt is not null;

    /// <summary>Asks before losing <paramref name="draft"/>, which only <paramref name="discard"/> then does; asks one thing at a time.</summary>
    internal void AskDiscard(string draft, Action discard, Action? keep = null)
    {
        if (_disposed || HasDiscardPrompt)
        {
            keep?.Invoke();
            return;
        }

        _discard = discard;
        _keep = keep;
        DiscardPrompt = $"Discard {draft}?";
    }

    [RelayCommand]
    private void KeepDraft()
    {
        var keep = _keep;
        CloseDiscardPrompt();
        keep?.Invoke();
    }

    [RelayCommand]
    private void ConfirmDiscard()
    {
        var discard = _discard;
        CloseDiscardPrompt();
        discard?.Invoke();
    }

    private void CloseDiscardPrompt()
    {
        _discard = null;
        _keep = null;
        DiscardPrompt = null;
    }

    /// <summary>
    /// Does <paramref name="go"/>, which replaces or refilters the screen, once any draft on it is discarded.
    /// A rebuilt screen holds none, including when <paramref name="go"/> stays on this one.
    /// </summary>
    private void Leave(Action go, Action? keep = null)
    {
        if (Content is IHoldsDraft { UnsavedDraft: { } draft })
        {
            AskDiscard(
                draft,
                () =>
                {
                    Show(_current);
                    go();
                },
                keep);
            return;
        }

        if (HasDiscardPrompt)
        {
            keep?.Invoke();
            return;
        }

        go();
    }

    /// <summary>The current destination's title, for the content header.</summary>
    internal string CurrentTitle => _current.Title;

    /// <summary>The window's title with no screen in it, as on the unlock screen.</summary>
    internal const string BareWindowTitle = "keypaste";

    /// <summary>Only the screen's own fixed name, never a vault, group, project or item: the system's window list, taskbar and switcher all show it.</summary>
    internal string WindowTitle => $"{CurrentTitle} - {BareWindowTitle}";

    /// <summary>Whether the shell draws the title above a screen that does not draw its own.</summary>
    internal bool ShowsHeader => !_current.OwnsHeader;

    /// <summary>Whatever the current destination shows.</summary>
    [ObservableProperty]
    internal partial object? Content { get; private set; }

    /// <summary>
    /// A quiet line that appears shortly before the vault locks, counts the whole seconds left, rounded
    /// up, and disappears on any input.
    /// </summary>
    /// <remarks>
    /// Muted, not red, and not a dialog: an auto-lock is the most normal thing this app does. The seconds
    /// are asked of the clock at each tick, as <see cref="ClipboardCountdown"/> asks it, so a late tick
    /// never shows more time than is left.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCountdown))]
    internal partial string Countdown { get; private set; } = string.Empty;

    internal bool HasCountdown => Countdown.Length > 0;

    /// <summary>What a screen reader is told about the countdown: its first line, set once per warning, so it is announced once rather than every second.</summary>
    [ObservableProperty]
    internal partial string CountdownAnnouncement { get; private set; } = string.Empty;

    /// <summary>Clears the countdown, because somebody is evidently still here.</summary>
    internal void ClearCountdown()
    {
        _countdownTimer?.Dispose();
        _countdownTimer = null;
        Countdown = string.Empty;
        CountdownAnnouncement = string.Empty;
    }

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

        // Recommendations lives as long as the unlock, and is disposed with the shell.
        if (!ReferenceEquals(Content, Recommendations))
        {
            (Content as IDisposable)?.Dispose();
        }

        if (_crashes is not null)
        {
            _crashes.Screen = CrashReporting.ScreenOf(destination.Kind);
        }

        Content = destination.Kind switch
        {
            // The audit log is machine state, which is why `keypaste log` reads it without a vault.
            DestinationKind.Log => new LogViewModel(Home, _clock, Clipboard, table: Tables.Log, openAgents: OpenAgents),
            DestinationKind.AgentHistory => new LogViewModel(Home, _clock, Clipboard, agentsOnly: true, table: Tables.Log),
            DestinationKind.Settings => new SettingsViewModel(_session, Home, Preferences, ApplyTheme, _picker, kind => Current = Destinations.Of(kind), Clipboard.TryPasteIntoAsync) { OffersTheme = OffersTheme, Login = LoginItem, CliLink = CliLink, Crashes = _crashes, BrowserFill = Authority is null ? null : new BrowserFillViewModel(Authority, _clock, _post) },
            DestinationKind.AgentActivity => Activity(),
            DestinationKind.Entries => Entries(),
            DestinationKind.EnvSets => new EnvSetsViewModel(_session, Clipboard, _picker, Launching ?? ProjectLaunching.ForThisMachine(Authority), ShowToast) { AskDiscard = AskDiscard },
            DestinationKind.Trash => new TrashViewModel(_session, Tables.Trash),
            DestinationKind.Sharing => Sharing(),
            DestinationKind.Tokens => new ScopedTokensViewModel(_session, Clipboard, ShowDone),
            DestinationKind.Diagnostics => new DiagnosticsViewModel(_session, Home, _crashes, Authority),
            DestinationKind.Advanced => new AdvancedViewModel(_session, kind => Current = Destinations.Of(kind)),
            DestinationKind.Recommendations => Recommendations,
            _ => null,
        };

        if (Content is EntriesViewModel entries)
        {
            entries.PropertyChanged += OnEntriesChanged;
        }

        Count();
    }

    private EntriesViewModel Entries()
    {
        var entries = new EntriesViewModel(_session, Clipboard, EntryActivity, _web, _post, _picker, Tables.Items) { AskDiscard = AskDiscard, Toast = ShowToast };

        entries.ClearSearchBox = () => Search = string.Empty;
        entries.WidenSearch = () => Leave(() => (Content as EntriesViewModel)?.ShowEveryItem());

        if (_search.Length > 0)
        {
            entries.Search = _search;
        }

        return entries;
    }

    /// <summary>Shows Agents with one agent's settings open on its row.</summary>
    /// <param name="agent">The agent's name.</param>
    internal void OpenAgentSettings(string agent) => Leave(() =>
    {
        MoveTo(Destinations.Of(DestinationKind.AgentActivity));
        (Content as AgentActivityViewModel)?.OpenSettings(agent);
    });

    private AgentActivityViewModel Activity() =>
        new(Authority, Home, _clock, _post, Authority is null ? null : Tools(), ShowToast, () => Current = Destinations.Of(DestinationKind.AgentHistory), Clipboard, _web) { RulesForEveryAgent = Authority is null ? null : new AgentRulesViewModel(Authority) };

    private void OnEntriesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // A group made, renamed, moved or deleted on Items is in the tree at once, even one holding no item,
        // whose change names no entry for Count to hear of.
        if (e.PropertyName == nameof(EntriesViewModel.Groups) && sender is EntriesViewModel { Groups.Count: > 0 } entries)
        {
            _groups = entries.Groups;
            RebuildSidebar();
        }

        if (e.PropertyName == nameof(EntriesViewModel.SelectedGroup))
        {
            OnPropertyChanged(nameof(SelectedSidebarRow));
        }
    }

    /// <summary>How share links reach their server; the shell makes one when none is given.</summary>
    /// <remarks>Redirects are never followed, so an envelope reaches only the origin the link names.</remarks>
    internal HttpMessageHandler? ShareTransport { get; init; }

    /// <summary>The entry that opens the app at login, which Settings turns on and off; none where the platform has no per-user one.</summary>
    internal ILoginItem? LoginItem { get; init; }

    /// <summary>The <c>keypaste</c> a terminal runs, which Settings links to this app's own; none where the platform needs no link.</summary>
    internal ICliLink? CliLink { get; init; }

    /// <summary>What Agents finds and connects AI tools through each time it opens; none looks for no tools.</summary>
    internal Func<ClientConnector?> Tools { get; init; } = () => null;

    /// <summary>What Run and Open terminal start a project's terminal with; the platform's own, answered by <see cref="Authority"/>, unless given.</summary>
    internal ProjectLaunching? Launching { get; init; }

    /// <summary>The app's crash reports, which Settings switches and Diagnostics lists, told each screen's name; none where the app composed none.</summary>
    internal CrashReporting? Crashes
    {
        get => _crashes;
        init
        {
            _crashes = value;

            if (value is not null)
            {
                value.Screen = CrashReporting.ScreenOf(_current.Kind);
            }
        }
    }

    private readonly CrashReporting? _crashes;
    private HttpMessageHandler? _ownShareTransport;

    private SharingViewModel Sharing(EntryName? what = null, Action? cancel = null)
    {
        var resolved = ShareEndpoint.TryResolve(null, Environment.GetEnvironmentVariable(ShareEndpoint.EnvironmentVariable), out var endpoint, out _);
#pragma warning disable RS0030 // The shell makes the share service's client.
        var transport = ShareTransport ?? (_ownShareTransport ??= ShareClient.CreateTransport());
#pragma warning restore RS0030
        var auditPath = Core.Audit.KeypasteHome.AuditPath(Home);
        var service = new ShareService(
#pragma warning disable RS0030 // The shell makes the share service's client.
            new ShareClient(transport, endpoint ?? ShareEndpoint.Default),
#pragma warning restore RS0030
            _clock,
            () => Core.Audit.AuditLog.TryOpen(auditPath, _clock, out var log, out _) ? log : null);

        return new SharingViewModel(
            _session,
            Clipboard,
            service,
            ShowDone,
            resolved ? null : $"Sharing is off: {ShareEndpoint.EnvironmentVariable} may only name a local development server. Unset it, then restart keypaste.",
            what,
            cancel);
    }

    /// <summary>Generate password…, as a dialog over whatever screen is showing (V.12).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGeneratorDialog))]
    internal partial GeneratePasswordViewModel? GeneratorDialog { get; private set; }

    internal bool HasGeneratorDialog => GeneratorDialog is not null;

    [RelayCommand]
    private void OpenGenerator()
    {
        if (_disposed || GeneratorDialog is not null)
        {
            return;
        }

        var detail = (Content as EntriesViewModel)?.Detail;
        GeneratorDialog = new GeneratePasswordViewModel(
            Clipboard,
            NewItemWith,
            detail is null ? null : (value, name) => detail.AddGeneratedField(name, value),
            detail?.Name.Title,
            CloseGenerator);
    }

    // New item with the generated value as its password, taken character by character into the form's own field.
    private void NewItemWith(SecretField value)
    {
        // The generator stays open behind "Discard …?", since closing it disposes the value.
        Leave(() =>
        {
            MoveTo(Destinations.Of(DestinationKind.Entries));

            if (Content is EntriesViewModel items)
            {
                items.BeginAddCommand.Press();
                items.NewItem?.Password.TakeFrom(value);
            }

            CloseGenerator();
        });
    }

    [RelayCommand]
    private void CloseGenerator()
    {
        var generator = GeneratorDialog;
        GeneratorDialog = null;
        generator?.Dispose();
    }

    /// <summary>Opens the share dialog on one entry, over whatever screen is showing.</summary>
    internal void OpenShare(EntryName? item)
    {
        if (_disposed || item is null || Share is not null)
        {
            return;
        }

        var share = Sharing(item, CloseShare);
        share.Created += (_, _) => CloseShare();
        Share = share;
    }

    [RelayCommand]
    private void CloseShare()
    {
        var share = Share;
        Share = null;
        share?.Dispose();
    }

    /// <summary>New project, from Items' "+" menu: Env profiles, with its create form open.</summary>
    [RelayCommand]
    private void NewProject() => Leave(() =>
    {
        MoveTo(Destinations.Of(DestinationKind.EnvSets));

        if (Content is EnvSetsViewModel env)
        {
            env.BeginAddCommand.Press();
        }
    });

    /// <summary>Import .env, from Items' "+" menu: into the first project; with none, create one first.</summary>
    [RelayCommand]
    private void ImportEnv()
    {
        if (_projects.Count == 0)
        {
            NewProject();
            return;
        }

        Leave(() =>
        {
            ShowProject(_projects[0].Name);

            if ((Content as EnvSetsViewModel)?.OpenProject is { } open)
            {
                open.Import.ChooseCommand.Press();
            }
        });
    }

    /// <summary>Opens Env profiles on a project.</summary>
    [RelayCommand]
    private void OpenProject(string? project)
    {
        if (project is not null)
        {
            Leave(() => ShowProject(project));
        }
    }

    private void ShowProject(string project)
    {
        _openProject = project;
        MoveTo(Destinations.Of(DestinationKind.EnvSets));

        if (Content is EnvSetsViewModel env)
        {
            env.OpenCommand.Press(project);
        }

        OnPropertyChanged(nameof(SelectedSidebarRow));
    }

    /// <summary>Items showing <paramref name="name"/>'s group with it selected, the search cleared so the list holds it.</summary>
    internal void OpenEntry(EntryName name)
    {
        ArgumentNullException.ThrowIfNull(name);

        Leave(() =>
        {
            Search = string.Empty;
            ShowGroupNow(name.GroupPath);

            if (Content is EntriesViewModel entries)
            {
                entries.Selected = entries.Rows.FirstOrDefault(row => row.Is(name));
            }
        });
    }

    /// <summary>Items showing <paramref name="path"/>, or every item when it is empty.</summary>
    private void ShowGroup(string path) => Leave(() => ShowGroupNow(path));

    private void ShowGroupNow(string path)
    {
        MoveTo(Destinations.Of(DestinationKind.Entries));

        if (Content is EntriesViewModel entries && entries.Groups.FirstOrDefault(node => node.Path == path) is { } node)
        {
            entries.SelectedGroup = node;
        }

        OnPropertyChanged(nameof(SelectedSidebarRow));
    }

    /// <summary>Shows <paramref name="path"/> when the group Items shows is folded away inside it.</summary>
    private void ShowGroupIfInside(string path)
    {
        if (Content is EntriesViewModel { SelectedGroup: { IsEverything: false } shown }
            && (path.Length == 0 || shown.Path.StartsWith(path + "/", StringComparison.Ordinal)))
        {
            ShowGroup(path);
        }
    }

    /// <summary>
    /// The sidebar's main list: Items, the groups whose parents are all open when Items is, the
    /// projects under their heading, then Agents.
    /// </summary>
    private List<object> SidebarOf()
    {
        var items = MainNav[0];
        List<object> rows = [items];

        if (items.IsExpanded)
        {
            foreach (var node in _groups.Where(node => !node.IsEverything && ParentsOpen(node.Path)))
            {
                rows.Add(new GroupRow(node.Path, node.Label, node.Depth, node.Children.Count > 0, _expanded.Contains(node.Path)));
            }
        }

        if (_projects.Count > 0)
        {
            rows.Add(_projectsHeading);
            rows.AddRange(_projects);
        }

        if (Recommendations is { IsEmpty: false })
        {
            rows.Add(RecommendationsRow);
        }

        rows.AddRange(MainNav.Skip(1));
        return rows;
    }

    private bool ParentsOpen(string path)
    {
        for (var slash = path.IndexOf('/', StringComparison.Ordinal); slash > 0; slash = path.IndexOf('/', slash + 1))
        {
            if (!_expanded.Contains(path[..slash]))
            {
                return false;
            }
        }

        return true;
    }

    private void SayWhenACopyArrives()
    {
        if (Recommendations.ConflictedCopies.Count > _conflictedCopiesSaid)
        {
            ShowToast(Resources.Strings.AnotherDevicesChangesWerentMerged, ToastTone.Information);
        }

        _conflictedCopiesSaid = Recommendations.ConflictedCopies.Count;
    }

    private void ShowRecommendationsRow()
    {
        RecommendationsRow.Count = Recommendations.NeedsReview > 0 ? Recommendations.NeedsReview.ToString(CultureInfo.InvariantCulture) : string.Empty;
        RebuildSidebar();
    }

    /// <summary>Replaces the sidebar's rows when they differ, keeping the list from writing back the row it had.</summary>
    private void RebuildSidebar()
    {
        var rows = SidebarOf();

        if (rows.SequenceEqual(SidebarRows))
        {
            return;
        }

        _rebuildingSidebar = true;

        try
        {
            SidebarRows = rows;
        }
        finally
        {
            _rebuildingSidebar = false;
        }

        OnPropertyChanged(nameof(SelectedSidebarRow));
    }

    /// <summary>Counts entries and projects for the sidebar.</summary>
    private void Count()
    {
        if (_disposed || _session.Unlocked is not { } vault)
        {
            _groups = [];
            Projects = [];
            RebuildSidebar();
            return;
        }

        IReadOnlyList<ProjectListing> projects;
        IReadOnlyList<GroupNode> groups;
        int total;

        try
        {
            total = vault.ReadEntries().Count;
            projects = ProjectCatalog.Read(vault).Projects;
            groups = GroupNode.Flatten(vault.ReadGroupPaths());
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        _groups = groups;
        Projects =
        [
            .. projects.Select(project => new ProjectRow(
                project.Name,
                project.Environments.SelectMany(environment => environment.Members).Distinct().Count())),
        ];
        RebuildSidebar();
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
            var waiting = activity.Waiting.Count + activity.WaitingRuns.Count + activity.WaitingEnvs.Count + activity.WaitingEdits.Count;
            var clients = Core.Clients.McpClientCards.Count(Authority.Clients);

            AgentsRefusing = Authority.LastAuditFailure is not null;
            AgentsServing = true;
            AgentsDetail = AgentsRefusing
                ? "Refusing agents · the audit log won't open"
                : waiting > 0
                    ? string.Create(CultureInfo.InvariantCulture, $"Answering agents · {waiting} waiting · {ClientCount(clients)}")
                    : "Answering agents · " + ClientCount(clients);
            SetCount(DestinationKind.AgentActivity, waiting);
            AgentsRow.Waiting = AgentsRefusing ? 0 : waiting;
            return;
        }

        AgentsServing = false;
        AgentsRefusing = false;
        AgentsDetail = status switch
        {
            AuthorityStatus.HeldBy => "another keypaste holds this vault",
            AuthorityStatus.NotServing => "agents cannot reach this vault",
            _ => "not serving agents",
        };
        SetCount(DestinationKind.AgentActivity, 0);
        AgentsRow.Waiting = 0;
    }

    private NavItem AgentsRow => MainNav.Single(item => item.Destination.Kind == DestinationKind.AgentActivity);

    private void ShowAgentsDot()
    {
        AgentsRow.DotLive = _agentsServing && !_agentsRefusing;
        AgentsRow.DotDanger = _agentsServing && _agentsRefusing;
    }

    private static string ClientCount(int count) => count switch
    {
        0 => "none connected",
        _ => string.Create(CultureInfo.InvariantCulture, $"{count} connected"),
    };

    private void OnStatusTick()
    {
        ReadAuthority();

        // The five-second poll covers a folder that cannot be watched; an edit, a save or a watched change says so at once.
        if (++_ticks % 5 == 0)
        {
            ReadVaultStatus();
        }
    }

    /// <summary>
    /// Watches the vault's folder, not the file alone, because another program often saves by renaming a
    /// new file over it, and compares the file a moment after the last change it sees; null where the
    /// folder cannot be watched, which leaves the five-second poll.
    /// </summary>
    private FileSystemWatcher? WatchVaultFile()
    {
        if (_session.VaultPath is not { } path || Path.GetDirectoryName(Path.GetFullPath(path)) is not { Length: > 0 } folder)
        {
            return null;
        }

        FileSystemWatcher? watch = null;

        try
        {
            watch = new FileSystemWatcher(folder, Path.GetFileName(path));
            watch.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime;
            watch.Changed += OnVaultFileChanged;
            watch.Created += OnVaultFileChanged;
            watch.Deleted += OnVaultFileChanged;
            watch.Renamed += OnVaultFileChanged;
            watch.Error += OnVaultWatchError;
            watch.EnableRaisingEvents = true;

            var watching = watch;
            watch = null;
            return watching;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return null;
        }
        finally
        {
            watch?.Dispose();
        }
    }

    private void OnVaultFileChanged(object? sender, FileSystemEventArgs e) => Settle();

    // A watch that lost track compares rather than waiting for the poll.
    private void OnVaultWatchError(object? sender, ErrorEventArgs e) => Settle();

    /// <summary>Compares the file a moment after the last change, so a save still being written is compared once it has finished.</summary>
    private void Settle()
    {
        try
        {
            _fileTimer?.Change(_fileSettle, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // The shell was disposed while the watch was still reporting, on a thread of its own.
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
            VaultSaveWords = string.Empty;
            VaultStatusDetail = string.Empty;
            VaultStatusTone = StatusTone.Ok;
            VaultChanged = null;
            return;
        }

        if (_isMerging)
        {
            ShowMerging();
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

        (VaultSaveWords, VaultStatusTone) = state.Status switch
        {
            VaultSaveStatus.Saved when _updatedAt is { } updated =>
                ($"updated {PersonTime.Clock(updated.ToLocalTime())}", StatusTone.Ok),
            VaultSaveStatus.Saved => ("saved", StatusTone.Ok),
            VaultSaveStatus.Unsaved => ("unsaved changes", StatusTone.Pending),
            VaultSaveStatus.ChangedOnDisk => ("changed on disk", StatusTone.Danger),
            _ => ("file unreadable", StatusTone.Danger),
        };

        VaultStatusDetail = (state.Status, _updatedAt, state.SavedAt) switch
        {
            (VaultSaveStatus.Saved, { } updated, _) =>
                $"Updated from another program's save at {PersonTime.ClockWithSeconds(updated.ToLocalTime())} · {VaultPath}",
            (_, _, { } saved) => $"Saved {PersonTime.ClockWithSeconds(saved.ToLocalTime())} · {VaultPath}",
            _ => VaultPath,
        };

        OffersSaveOverFile = false;
        Recommendations.LookForConflictedCopies();

        if (!File.Exists(vault.Path))
        {
            OffersSaveOverFile = true;
            VaultChanged = $"{VaultName} is no longer where it was. What's open here is safe until you lock it. Save it back, or put the file back and keypaste carries on.";
            return;
        }

        if (state.Status != VaultSaveStatus.ChangedOnDisk)
        {
            _reloadFailure = null;
            VaultChanged = null;
            return;
        }

        // The poll and the folder watch merge another program's save; Reload is offered for one keypaste did not merge.
        if (_session.MergeRefusal() is { } refused)
        {
            OffersSaveOverFile = refused.Outcome == VaultMergeSaveOutcome.Unreadable;
            VaultChanged = string.Concat(
                $"Another program saved {VaultName}, and keypaste could not merge it: {MergeNotice.Refused(refused)}. Agents are refused until you reload it.",
                state.Unwritten ? Discarded() : string.Empty,
                _reloadFailure is { } failed ? $" Reloading failed: {failed}" : string.Empty);
            return;
        }

        if (_session.Merges)
        {
            StartMerge();
            return;
        }

        VaultChanged = string.Concat(
            $"Another program saved {VaultName}. Agents are refused until you reload it.",
            state.Unwritten ? Discarded() : string.Empty,
            _reloadFailure is { } failure ? $" Reloading failed: {failure}" : string.Empty);
    }

    /// <summary>Merges another program's save on a thread of its own, the title bar saying so meanwhile.</summary>
    private void StartMerge()
    {
        if (_isMerging)
        {
            return;
        }

        _isMerging = true;
        ShowMerging();
        _merging = AppVaultSession.OnItsOwnThread(() =>
        {
            _session.Merge();
            Post(() =>
            {
                _isMerging = false;
                ReadVaultStatus();
            });
        });
    }

    private void OpenAgents() => Current = Destinations.Of(DestinationKind.AgentActivity);

    private void ShowMerging()
    {
        VaultSaveWords = "merging";
        VaultStatusTone = StatusTone.Pending;
        VaultStatusDetail = $"Merging another program's save · {VaultPath}";
        VaultChanged = null;
    }

    private void OnMerged(object? sender, MergeResult merged) => Post(() => ShowMerged(merged));

    /// <summary>
    /// Says what a merge did and shows the merged copy on the screen open now, leaving an item being edited as typed, so
    /// its next save writes by name to the merged copy; a refusal is said by the banner the status reads.
    /// </summary>
    private void ShowMerged(MergeResult merged)
    {
        if (_disposed)
        {
            return;
        }

        if (merged.Merged)
        {
            VaultMoved?.Invoke();
            _reloadFailure = null;
            _updatedAt = merged.Outcome == VaultMergeSaveOutcome.Updated ? merged.At : null;

            if (merged.Outcome == VaultMergeSaveOutcome.Saved)
            {
                var said = MergeNotice.Saved(VaultName, merged);
                Notice = _noticeNamesAConflict && Notice is { } standing ? $"{standing} {said}" : said;
                _noticeNamesAConflict |= MergeNotice.NamesAConflict(merged);
            }

            Count();

            switch (Content)
            {
                case IHoldsDraft { UnsavedDraft: not null }:
                case EntriesViewModel { Detail.IsEditing: true }:
                    break;

                case EntriesViewModel entries:
                    entries.Reload();
                    break;

                case EnvSetsViewModel env:
                    env.Reload();
                    break;

                default:
                    Show(_current);
                    break;
            }

            Recommendations.Check();
        }

        ReadVaultStatus();
    }

    [RelayCommand]
    private void DismissNotice()
    {
        Notice = null;
        _noticeNamesAConflict = false;
    }

    /// <summary>What reloading would discard, naming the items changed here since the last save and never a value.</summary>
    private string Discarded()
    {
        var unsaved = _session.Unsaved;
        var items = unsaved.Where(name => !ReservedGroups.IsReserved(name.GroupPath)).ToList();
        List<string> named = [.. items.Take(3).Select(Core.Approval.ApprovalPrompt.Shown)];

        if (items.Count > 3)
        {
            named.Add(items.Count == 4 ? "1 more item" : Invariant($"{items.Count - 3} more items"));
        }

        if (unsaved.Count > items.Count)
        {
            named.Add("keypaste's own records");
        }

        return named.Count switch
        {
            0 => " Your last change here was not saved, and reloading discards it.",
            1 => $" Your change to {named[0]} was not saved, and reloading discards it.",
            _ => $" Your changes to {string.Join(", ", named.Take(named.Count - 1))} and {named[^1]} were not saved, and reloading discards them.",
        };
    }

    /// <summary>Opens what the file holds now in place of the open copy, offered once a merge was refused.</summary>
    [RelayCommand]
    private void Reload() => Leave(ReloadNow);

    /// <summary>Whether the banner offers to write what is open over a file that is gone or unreadable.</summary>
    [ObservableProperty]
    internal partial bool OffersSaveOverFile { get; private set; }

    [RelayCommand]
    private void SaveOverFile()
    {
        if (_session.SaveOpenCopyOverFile() is { } failed)
        {
            _reloadFailure = failed;
        }

        ReadVaultStatus();
    }

    private void ReloadNow()
    {
        var result = _session.Reload();

        if (_disposed || result.Outcome == ReloadOutcome.Locked)
        {
            return;
        }

        VaultMoved?.Invoke();

        if (result.Outcome == ReloadOutcome.Failed)
        {
            _reloadFailure = result.Reason;
            ReadVaultStatus();
            return;
        }

        _reloadFailure = null;
        Show(_current);
        Recommendations.Check();
        ReadVaultStatus();
    }

    private void OnSaved(object? sender, EventArgs e) => Post(() =>
    {
        if (!_disposed)
        {
            _updatedAt = null;
            ReadVaultStatus();
            Recommendations.Check();
        }
    });

    /// <summary>
    /// Says a change you allowed from a terminal is saved, and shows it on the screen open now, unless
    /// you are editing the item there, whose form is left as it is.
    /// </summary>
    private void OnTerminalEdited(object? sender, EditDone done) => Post(() =>
    {
        if (_disposed)
        {
            return;
        }

        ShowDone($"Saved keypaste {TerminalEdit.VerbOf(done.Action)} for {EntryNameSanitizer.SanitizePath(done.Item).Text}");

        switch (Content)
        {
            case IHoldsDraft { UnsavedDraft: not null }:
                break;

            case EntriesViewModel entries when entries.Detail is not { IsEditing: true }:
                entries.Reload();
                break;

            case EnvSetsViewModel env:
                env.Reload();
                break;
        }
    });

    private void OnEdited(object? sender, VaultEdit edit) => Post(() =>
    {
        _updatedAt = null;
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

    /// <summary>Keeps the lock's deadline from the warning and counts down to it each second.</summary>
    private void OnLockingSoon(object? sender, TimeSpan remaining)
    {
        var wall = _clock.GetUtcNow();
        var stamp = _clock.GetTimestamp();

        Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            _countdownTimer?.Dispose();
            _lockWindow = remaining;
            _warnedWall = wall;
            _warnedStamp = stamp;
            _countdownTimer = _clock.CreateTimer(_ => Post(ShowCountdown), null, _countdownTick, _countdownTick);
            ShowCountdown();
            CountdownAnnouncement = Countdown;
        });
    }

    /// <summary>
    /// Says the whole seconds left, rounded up; past the deadline with the vault still open, the
    /// session saw activity this shell did not, so the line goes.
    /// </summary>
    private void ShowCountdown()
    {
        if (_disposed || _countdownTimer is null)
        {
            return;
        }

        var wall = _clock.GetUtcNow() - _warnedWall;
        var monotonic = _clock.GetElapsedTime(_warnedStamp);
        var left = _lockWindow - (wall > monotonic ? wall : monotonic);

        if (left <= TimeSpan.Zero)
        {
            ClearCountdown();
            return;
        }

        var seconds = (int)Math.Ceiling(left.TotalSeconds);
        Countdown = seconds == 1
            ? "Locking in 1 second."
            : string.Create(CultureInfo.InvariantCulture, $"Locking in {seconds} seconds.");
    }

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
        _session.Merged -= OnMerged;

        if (Authority is not null)
        {
            Authority.TerminalEdited -= OnTerminalEdited;
        }

        Recommendations.Dispose();
        CloseDiscardPrompt();
        CloseShare();
        CloseGenerator();

        _statusTimer?.Dispose();
        _fileWatch?.Dispose();
        _fileTimer?.Dispose();
        Toasts.Dispose();
        _countdownTimer?.Dispose();
        EntryActivity?.Dispose();
        Import?.Dispose();
        Import = null;
        ExportImport?.Dispose();
        ExportImport = null;
        Notice = null;
        Projects = [];
        (Content as IDisposable)?.Dispose();
        Content = null;
#pragma warning disable RS0030 // The share service's client's transport.
        _ownShareTransport?.Dispose();
#pragma warning restore RS0030

        // A secret on the clipboard is derived from an open vault, so it does not survive the lock
        // either. Disposing clears it, conditionally — a clipboard the user has changed since is
        // left alone.
        Clipboard.Dispose();
    }
}
