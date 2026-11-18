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

    private readonly AppVaultSession _session;
    private readonly IVaultFilePicker? _picker;
    private readonly IWebLauncher? _web;
    private readonly TimeProvider _clock;
    private readonly Action<Action>? _post;
    private readonly ITimer? _statusTimer;
    private VaultFileWatch? _ownWatch;
    private readonly Dictionary<AppVaultSession, VaultFileWatch?> _followed = [];
    private AppVaultSession? _statusSession;
    private ITimer? _countdownTimer;
    private TimeSpan _lockWindow;
    private DateTimeOffset _warnedWall;
    private long _warnedStamp;
    private Destination _current;
    private string _search = string.Empty;
    private IReadOnlyList<ProjectRow> _projects = [];
    private string? _openProject;
    private int _conflictedCopiesSaid;
    private bool _agentsServing;
    private bool _agentsRefusing;
    private string _agentsDetail = string.Empty;
    private string? _reloadFailure;
    private readonly Dictionary<AppVaultSession, DateTimeOffset> _updatedAt = [];
    private bool _noticeNamesAConflict;
    private readonly Action<string, string?, int?>? _openInPlace;
    private Action? _discard;
    private Action? _keep;
    private bool _disposed;
    private SecretField? _newItemPassword;
    private readonly Dictionary<AppVaultSession, RecommendationsViewModel> _scans = [];
    private IReadOnlyList<AppVaultSession> _contentVaults = [];
    private bool _contentSpansVaults;
    private VaultSection? _settingsVault;
    private VaultSection? _agentsVault;
    private VaultSection? _securityVault;

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

        LockCommand = new AsyncRelayCommand(LockAllAsync);
        Toasts = new ToastStack(_clock, Post);
        BackCommand = new RelayCommand(() => Current = Destinations.UpOf(Current), () => HasBack);
        ShareCommand = new RelayCommand<EntryName>(OpenShare, item => item is not null && Share is null);
        ImportCommand = new AsyncRelayCommand(PickImportAsync, () => _picker is not null && !HasImport);
        CancelTouchCommand = new RelayCommand(_session.CancelHardwareKeyWait, () => IsWaitingForTouch);

        FoldCommand = new RelayCommand<object>(row => Fold(row, open: null));
        RailPlaces = [.. Destinations.Places.Take(3).Select(d => new NavItem(d) { HasDot = d.Kind == DestinationKind.AgentActivity }), new NavItem(Destinations.Access), new NavItem(Destinations.Places[3])];
        _current = Destinations.Places[0];
        _itemsVault = new VaultSection(session);
        Account = new AccountSection(AccountSection.LocalTitle, [_itemsVault]);
        SidebarRows = SidebarOf();

        _session.LockingSoon += OnLockingSoon;
        _session.WaitingForTouch += OnWaitingForTouch;
        _ownWatch = new VaultFileWatch(session);

        if (authority is not null)
        {
            authority.TerminalEdited += OnTerminalEdited;
        }

        if (authority is not null && _session.Identity is { } identity)
        {
            EntryActivity = new EntryActivitySource(
                authority, Core.Audit.KeypasteHome.AuditPath(home), identity.Key, _clock, post);
        }

        Recommendations = new RecommendationsViewModel(session, home, OpenEntry, post, Preferences, web);
        Recommendations.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(RecommendationsViewModel.NeedsReview) or nameof(RecommendationsViewModel.IsEmpty))
            {
                ShowSecurityCount();
            }

            if (e.PropertyName == nameof(RecommendationsViewModel.ConflictedCopies))
            {
                SayWhenACopyArrives();
            }
        };

        // Built here rather than left to the first navigation. Assigning Current to the destination
        // it already holds changes nothing, so Show never ran and the shell opened on a blank pane.
        Show(_current);
        ShowSecurityCount();
        ReadAuthority();
        ReadVaultStatus();

        _statusTimer = _clock.CreateTimer(_ => Post(OnStatusTick), null, _statusTick, _statusTick);
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

    /// <summary>What the notice says while the file holds another program's save that keypaste did not merge, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVaultChanged))]
    internal partial string? VaultChanged { get; private set; }

    internal bool HasVaultChanged => VaultChanged is not null;

    /// <summary>What the vault holds has just changed under the screen, whichever screen is open and whether or not it reads again.</summary>
    internal event Action? VaultMoved;

    /// <summary>The merge of another program's save the vault in view's watch started, or a completed task when none runs.</summary>
    internal Task Merging => WatchOf(ShownSession)?.Merging ?? Task.CompletedTask;

    /// <summary>The vault whose save state the title bar shows and whose banner offers Reload: the vault in view.</summary>
    private AppVaultSession ShownSession => VaultForScreens.Session;

    /// <summary>The watch merging another program's save of a vault's file.</summary>
    private VaultFileWatch? WatchOf(AppVaultSession session) =>
        OpenOf(session)?.Watch ?? (ReferenceEquals(session, _session) ? _ownWatch : null);

    /// <summary>A vault's file name, as a notice names it.</summary>
    private string FileNameOf(AppVaultSession session) =>
        (Vaults?.PathOf(session) ?? session.VaultPath) is { } path ? Shown(Path.GetFileName(path)) : string.Empty;

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

    /// <summary>Goes up from a screen to the screen or place it sits under.</summary>
    internal RelayCommand BackCommand { get; }

    /// <summary>Whether the shell shows a screen under a place, which offers Back.</summary>
    internal bool HasBack => _current.HasBack;

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
    internal string VaultName => FileNameOf(ShownSession);

    /// <summary>The open vault's full path, for the tooltip.</summary>
    internal string VaultPath => ShownSession.VaultPath ?? string.Empty;

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
                AgentsRow.Detail = value;
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

            if (!SetProperty(ref _search, value ?? string.Empty))
            {
                return;
            }

            if (Content is EntriesViewModel)
            {
                Leave(() => (Content as EntriesViewModel)!.Search = _search, () => TakeBackSearch(before));
            }
            else if (_search.Length > 0)
            {
                // Typing in the title bar's search from another place searches Items, which takes the query as it is built.
                Leave(() => MoveTo(Destinations.Of(DestinationKind.Entries)), () => TakeBackSearch(before));
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
            OnPropertyChanged(nameof(SelectedRailPlace));
            OnPropertyChanged(nameof(ShowsItemsPlace));
            OnPropertyChanged(nameof(ShowsSidebar));
            OnPropertyChanged(nameof(SidebarOverlays));
            OnPropertyChanged(nameof(ContentIsTight));
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
    [NotifyPropertyChangedFor(nameof(EntriesInView), nameof(ProjectsInView))]
    internal partial object? Content { get; private set; }

    /// <summary>Items, while it is the screen in view, whose New item and generator are sheets over the window.</summary>
    internal EntriesViewModel? EntriesInView => Content as EntriesViewModel;

    /// <summary>Projects, while it is the screen in view, whose generator is a sheet over the window.</summary>
    internal EnvSetsViewModel? ProjectsInView => Content as EnvSetsViewModel;

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

    private Core.Audit.AuditCheckpointKey? CheckpointKey() =>
        _session.Unlocked is { } vault && _session.Identity is { } identity ? Core.Audit.AuditCheckpointKey.Read(vault, identity) : null;

    /// <summary>Builds the current destination's content.</summary>
    private void Show(Destination destination)
    {
        if (Content is EnvSetsViewModel previousProjects)
        {
            previousProjects.PropertyChanged -= OnProjectsChanged;
        }

        // Each vault's check lives as long as its unlock, and is disposed with the shell or the vault's lock.
        if (Content is not RecommendationsViewModel)
        {
            (Content as IDisposable)?.Dispose();
        }

        if (_crashes is not null)
        {
            _crashes.Screen = CrashReporting.ScreenOf(destination.Kind);
        }

        _contentSpansVaults = false;

        Content = destination.Kind switch
        {
            // The audit log is machine state, which is why `keypaste log` reads it without a vault.
            DestinationKind.Log => new LogViewModel(Home, _clock, Clipboard, OpenAgents, Tables.Log, CheckpointKey),
            DestinationKind.Settings => Settings(),
            DestinationKind.AgentActivity => Activity(),
            DestinationKind.Entries => ItemsScreen(),
            DestinationKind.EnvSets => new EnvSetsViewModel(_itemsVault.Session, Clipboard, _picker, Launching ?? ProjectLaunching.ForThisMachine(AuthorityOf(_itemsVault.Session)), ShowToast),
            DestinationKind.Trash => new TrashViewModel(_itemsVault.Session, Tables.Trash),
            DestinationKind.Sharing => Sharing(),
            DestinationKind.Tokens => new ScopedTokensViewModel(_session, Clipboard, ShowDone),
            DestinationKind.Diagnostics => new DiagnosticsViewModel(_session, Home, _crashes, Authority),
            DestinationKind.Advanced => new AdvancedViewModel(_session, kind => Current = Destinations.Of(kind)),
            DestinationKind.Recommendations => SecurityInView,
            _ => null,
        };

        _contentVaults = ContentVaults();

        if (Content is EnvSetsViewModel projects)
        {
            projects.PropertyChanged += OnProjectsChanged;
            FollowOpenProject(projects);
        }

        Count();

        if (!ReferenceEquals(ShownSession, _statusSession))
        {
            ReadVaultStatus();
        }
    }

    /// <summary>The vaults the screen in view reads, whose lock makes it read again; none for a screen of the shell's own vault, whose lock builds the window again.</summary>
    private IReadOnlyList<AppVaultSession> ContentVaults()
    {
        switch (Content)
        {
            case EntriesViewModel shown:
                return shown.Vaults;
            case EnvSetsViewModel or TrashViewModel:
                return [_itemsVault.Session];
            case SettingsViewModel:
                return [_settingsVault?.Session ?? _session];
            case AgentActivityViewModel:
                return [_agentsVault?.Session ?? _session];
            case RecommendationsViewModel check:
                return [check.Session, .. check.AlsoShown.Select(other => other.Session)];
            default:
                return [];
        }
    }

    /// <summary>Settings, its This vault on the vault in view or the one its picker chose.</summary>
    private SettingsViewModel Settings()
    {
        var vault = Chosen(_settingsVault, VaultForScreens);
        _settingsVault = vault;

        return new SettingsViewModel(vault.Session, Home, Preferences, ApplyTheme, _picker, kind => Current = Destinations.Of(kind), Clipboard.TryPasteIntoAsync)
        {
            OffersTheme = OffersTheme,
            Login = LoginItem,
            CliLink = CliLink,
            Crashes = _crashes,
            BrowserFill = Authority is null ? null : new BrowserFillViewModel(Authority, _post),
            BrowserHosts = Authority is null || BrowserHosts is not { } hosts ? null : new BrowserHostsViewModel(hosts.Hosts, hosts.Unavailable),
            VaultChoices = VaultChoices(withAll: false),
            ShowVault = choice => ShowAnother(choice, chosen => _settingsVault = chosen, () =>
            {
                if (Content is SettingsViewModel settings)
                {
                    settings.Section = SettingsSectionChoice.ThisVault;
                }
            }),
        };
    }

    /// <summary>Security: one vault's check, or every unlocked vault's under each one's title.</summary>
    /// <remarks>A property, so CA2000 reads it as a check the shell keeps rather than one it makes per visit.</remarks>
    private RecommendationsViewModel SecurityInView => Security();

    private RecommendationsViewModel Security()
    {
        var unlocked = UnlockedSections;
        var all = _securityVault is null || !unlocked.Contains(_securityVault);
        var shown = all ? unlocked : [_securityVault!];
        List<RecommendationsViewModel> checks = [.. shown.Select(vault => ScanOf(vault.Session))];
        var first = checks.Count > 0 ? checks[0] : Recommendations;
        var choices = VaultChoices(withAll: true);
        var headed = all && checks.Count > 1;

        foreach (var check in _scans.Values.Append(Recommendations).Distinct())
        {
            check.IsListedUnderAnother = !ReferenceEquals(check, first);
            check.VaultHeading = headed && checks.Contains(check) ? SectionOf(check.Session).Title : string.Empty;
            check.AlsoShown = [];
            check.VaultChoices = [];
        }

        first.AlsoShown = all ? [.. checks.Skip(1)] : [];
        first.VaultChoices = choices;
        first.ShowVault = choice => ShowAnother(choice, chosen => _securityVault = chosen, null, allowsAll: true);
        _contentSpansVaults = all;
        return first;
    }

    /// <summary>The check of an unlocked vault's items, made the first time Security or its count asks for it.</summary>
    private RecommendationsViewModel ScanOf(AppVaultSession session)
    {
        if (ReferenceEquals(session, _session))
        {
            return Recommendations;
        }

        if (!_scans.TryGetValue(session, out var scan))
        {
            scan = new RecommendationsViewModel(session, Home, name => OpenEntry(name, session), _post, Preferences, _web);
            scan.PropertyChanged += OnScanChanged;
            _scans[session] = scan;
        }

        return scan;
    }

    private void OnScanChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RecommendationsViewModel.NeedsReview) or nameof(RecommendationsViewModel.IsEmpty))
        {
            ShowSecurityCount();
        }
    }

    /// <summary>Starts a check for each unlocked vault that has none and drops the check of each vault that locked.</summary>
    private void KeepScans()
    {
        foreach (var locked in _scans.Keys.Where(session => !session.IsUnlocked || !Account.Vaults.Any(vault => vault.Session == session)).ToList())
        {
            var scan = _scans[locked];
            _scans.Remove(locked);
            scan.PropertyChanged -= OnScanChanged;
            scan.Dispose();
        }

        foreach (var vault in UnlockedSections)
        {
            _ = ScanOf(vault.Session);
        }

        ShowSecurityCount();
    }

    /// <summary>Shows the screen in view again for a vault its picker chose, asking first about a draft it would leave.</summary>
    private void ShowAnother(VaultChoice choice, Action<VaultSection?> choose, Action? then, bool allowsAll = false)
    {
        if (choice.Vault is null && !allowsAll)
        {
            return;
        }

        Leave(
            () =>
            {
                choose(choice.Vault is { } session ? SectionOf(session) : null);
                Show(_current);
                then?.Invoke();
            },
            () => OnPropertyChanged(nameof(Content)));
    }

    /// <summary>Items' list: the place in view, or a locked vault's form.</summary>
    private IDisposable ItemsScreen() => _itemsVault.IsLocked && !_allAccounts ? LockedVault() : Entries();

    private EntriesViewModel Entries()
    {
        var unlocked = UnlockedSections;
        var home = _itemsVault.IsLocked && unlocked.Count > 0 ? unlocked[0] : _itemsVault;
        IReadOnlyList<AppVaultSession> others = _allAccounts ? [.. unlocked.Where(vault => vault != home).Select(vault => vault.Session)] : [];
        var inView = ReferenceEquals(home.Session, _session);
        _contentSpansVaults = _allAccounts;
        var entries = new EntriesViewModel(home.Session, Clipboard, inView ? EntryActivity : null, _web, _post, _picker, Tables.Items, others, session => SectionOf(session).Title) { AskDiscard = AskDiscard, Toast = ShowToast, EndReveals = () => VaultMoved?.Invoke(), StartNewItemWith = NewItemWith, Place = _place, VaultTitle = home.Title, ListSubtitle = ListSubtitle(), SaveInPlaces = () => [.. Account.Vaults.Select(vault => new SaveInChoice(vault.Session, Account.Title, vault.Title))] };

        entries.ClearSearchBox = () => Search = string.Empty;

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

    /// <summary>Agents, on the vault in view or the one its picker chose, since each vault keeps its own agents' rules.</summary>
    private AgentActivityViewModel Activity()
    {
        var vault = Chosen(_agentsVault, VaultForScreens);
        _agentsVault = vault;
        var authority = AuthorityOf(vault.Session);

        return new(authority, Home, _clock, _post, authority is null ? null : Tools(), ShowToast, () => Current = Destinations.Of(DestinationKind.Log), Clipboard, _web)
        {
            RulesForEveryAgent = authority is null ? null : new AgentRulesViewModel(authority),
            VaultChoices = authority is null ? [] : VaultChoices(withAll: false),
            ShowVault = choice => ShowAnother(choice, chosen => _agentsVault = chosen, null),
        };
    }

    private void OnProjectsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EnvSetsViewModel.OpenProject) && sender is EnvSetsViewModel projects)
        {
            FollowOpenProject(projects);
        }
    }

    /// <summary>The sidebar highlights the project Environments has open, however it was opened, under its vault's Environments.</summary>
    private void FollowOpenProject(EnvSetsViewModel projects)
    {
        _openProject = projects.OpenProject?.Name;
        OnPropertyChanged(nameof(SelectedSidebarRow));
    }

    /// <summary>How share links reach their server; the shell makes one when none is given.</summary>
    /// <remarks>Redirects are never followed, so an envelope reaches only the origin the link names.</remarks>
    internal HttpMessageHandler? ShareTransport { get; init; }

    /// <summary>The entry that opens the app at login, which Settings turns on and off; none where the platform has no per-user one.</summary>
    internal ILoginItem? LoginItem { get; init; }

    /// <summary>The <c>keypaste</c> a terminal runs, which Settings links to this app's own; none where the platform needs no link.</summary>
    internal ICliLink? CliLink { get; init; }

    /// <summary>The browsers' hosts, which Settings › Browser fill turns on and off beside the pairings; none lists no browsers.</summary>
    internal BrowserHostSetup? BrowserHosts { get; init; }

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

    /// <summary>Whether New item or a form's generator covers the window as a sheet, which the menus wait behind as they do behind a dialog.</summary>
    internal bool HasSheet =>
        EntriesInView is { IsAdding: true }
        || EntriesInView?.NewItem?.GeneratorPanel is not null
        || EntriesInView?.Detail?.GeneratorPanel is not null
        || ProjectsInView?.OpenProject?.GeneratorPanel is not null;

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
            value => NewItemWith(value, CloseGenerator),
            detail is null ? null : (value, name) => detail.AddGeneratedField(name, value),
            detail?.Name.Title,
            CloseGenerator);
    }

    // New item with the generated value as its password, taken character by character into the form's own field.
    private void NewItemWith(SecretField value, Action close)
    {
        // Discarding the form a pane's generator belongs to disposes that generator's value, so the shell holds a copy.
        DropNewItemPassword();
        var password = new SecretField(Clipboard);
        password.TakeFrom(value);
        _newItemPassword = password;

        // The generator stays open behind "Discard …?", so keeping the draft leaves the panel as it was.
        Leave(
            () =>
            {
                MoveTo(Destinations.Of(DestinationKind.Entries));

                if (Content is EntriesViewModel items)
                {
                    items.BeginAddOfKindCommand.Press(ItemTemplate.Login);
                    items.NewItem?.Password.TakeFrom(password);
                }

                DropNewItemPassword();
                close();
            },
            DropNewItemPassword);
    }

    private void DropNewItemPassword()
    {
        _newItemPassword?.Dispose();
        _newItemPassword = null;
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

    /// <summary>Items listing every item with <paramref name="name"/> selected, the search cleared so the list holds it.</summary>
    internal void OpenEntry(EntryName name) => OpenEntry(name, _session);

    /// <summary>Items listing every item of <paramref name="vault"/>'s vault with <paramref name="name"/> selected.</summary>
    private void OpenEntry(EntryName name, AppVaultSession vault)
    {
        ArgumentNullException.ThrowIfNull(name);

        Leave(() =>
        {
            Search = string.Empty;
            ShowEveryItemNow(SectionOf(vault));

            if (Content is EntriesViewModel entries)
            {
                entries.Selected = entries.Rows.FirstOrDefault(row => row.Is(name));
            }
        });
    }

    private void SayWhenACopyArrives()
    {
        if (Recommendations.ConflictedCopies.Count > _conflictedCopiesSaid)
        {
            ShowToast(Resources.Strings.AnotherDevicesChangesWerentMerged, ToastTone.Information);
        }

        _conflictedCopiesSaid = Recommendations.ConflictedCopies.Count;
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
            var waiting = activity.Waiting.Count + activity.WaitingSets.Count + activity.WaitingSaves.Count + activity.WaitingChanges.Count + activity.WaitingRuns.Count + activity.WaitingEnvs.Count + activity.WaitingEdits.Count;
            var clients = Core.Clients.McpClientCards.Count(Authority.Clients);

            AgentsRefusing = Authority.LastAuditFailure is not null;
            AgentsServing = true;

            var unwritten = Authority.LastAuditFailureIsWrite;

            if (AgentsRefusing && Authority.ClaimAuditFailureNews())
            {
                ShowToast(unwritten ? Resources.Strings.KeypasteCantWriteItsAuditLog : Resources.Strings.KeypasteIsRefusingEveryAgentRequest, ToastTone.Warning);
            }

            AgentsDetail = AgentsRefusing
                ? unwritten ? "Refusing agents · the audit log can't be written" : "Refusing agents · the audit log won't open"
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

    private NavItem AgentsRow => RailPlaces.Single(item => item.Destination.Kind == DestinationKind.AgentActivity);

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
        CountWhenAVaultChanged();

        // A vault's mark for unsaved changes follows a save that failed, which no event names.
        RebuildSidebar();
    }

    /// <summary>
    /// Follows each listed vault's saves, merges and file watch, so another program's save of any open vault is merged and
    /// said, and the title bar and the sidebar say each vault's state; drops a vault no longer listed.
    /// </summary>
    private void FollowVaults()
    {
        List<AppVaultSession> listed = [.. Account.Vaults.Select(vault => vault.Session)];

        foreach (var gone in _followed.Keys.Where(session => !listed.Contains(session)).ToList())
        {
            Unfollow(gone);
        }

        foreach (var session in listed)
        {
            var watch = WatchOf(session);

            if (_followed.TryGetValue(session, out var followed))
            {
                if (ReferenceEquals(followed, watch))
                {
                    continue;
                }

                if (followed is not null)
                {
                    followed.Changed -= OnWatchChanged;
                }
            }
            else
            {
                session.Saved += OnSaved;
                session.Merged += OnMerged;
                session.Edited += OnEdited;
            }

            if (watch is not null)
            {
                watch.Changed += OnWatchChanged;
            }

            _followed[session] = watch;
        }
    }

    private void Unfollow(AppVaultSession session)
    {
        session.Saved -= OnSaved;
        session.Merged -= OnMerged;
        session.Edited -= OnEdited;

        if (_followed.TryGetValue(session, out var watch) && watch is not null)
        {
            watch.Changed -= OnWatchChanged;
        }

        _followed.Remove(session);
    }

    /// <summary>A vault's watch saw its file change or a merge start or finish: the title bar reads the vault in view again, and the sidebar its marks.</summary>
    private void OnWatchChanged(object? sender, EventArgs e) => Post(() =>
    {
        if (_disposed || sender is not VaultFileWatch watch)
        {
            return;
        }

        if (ReferenceEquals(watch.Session, ShownSession))
        {
            ReadVaultStatus();
        }

        RebuildSidebar();
    });

    /// <summary>Whether the screen in view reads <paramref name="session"/>'s vault, which a merge or a reload of it shows again.</summary>
    private bool ReadsVault(AppVaultSession session) =>
        _contentVaults.Contains(session) || (_contentVaults.Count == 0 && ReferenceEquals(session, _session));

    /// <summary>What a vault's heading says beside its title: a merge of another program's save under way, or changes not yet in its file.</summary>
    private string SaveMarkOf(VaultSection vault) =>
        vault.IsLocked ? string.Empty
        : WatchOf(vault.Session)?.IsMerging == true ? "merging"
        : vault.Session.Unsaved.Count > 0 ? "unsaved"
        : string.Empty;

    private void ReadVaultStatus()
    {
        if (_disposed)
        {
            return;
        }

        var session = ShownSession;

        if (!ReferenceEquals(session, _statusSession))
        {
            _statusSession = session;
            _reloadFailure = null;
        }

        if (session.Unlocked is not { } vault || VaultName.Length == 0)
        {
            VaultSaveWords = string.Empty;
            VaultStatusDetail = string.Empty;
            VaultStatusTone = StatusTone.Ok;
            VaultChanged = null;
            return;
        }

        if (WatchOf(session)?.IsMerging == true)
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
            VaultSaveStatus.Saved when _updatedAt.TryGetValue(session, out var updated) =>
                ($"updated {PersonTime.Clock(updated.ToLocalTime())}", StatusTone.Ok),
            VaultSaveStatus.Saved => ("saved", StatusTone.Ok),
            VaultSaveStatus.Unsaved => ("unsaved changes", StatusTone.Pending),
            VaultSaveStatus.ChangedOnDisk => ("changed on disk", StatusTone.Danger),
            _ => ("file unreadable", StatusTone.Danger),
        };

        VaultStatusDetail = (state.Status, _updatedAt.TryGetValue(session, out var at) ? at : (DateTimeOffset?)null, state.SavedAt) switch
        {
            (VaultSaveStatus.Saved, { } updated, _) =>
                $"Updated from another program's save at {PersonTime.ClockWithSeconds(updated.ToLocalTime())} · {VaultPath}",
            (_, _, { } saved) => $"Saved {PersonTime.ClockWithSeconds(saved.ToLocalTime())} · {VaultPath}",
            _ => VaultPath,
        };

        OffersSaveOverFile = false;
        ScanOf(session).LookForConflictedCopies();

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

        // The vault's watch merges another program's save; Reload is offered for one keypaste did not merge.
        if (session.MergeRefusal() is { } refused)
        {
            OffersSaveOverFile = refused.Outcome == VaultMergeSaveOutcome.Unreadable;
            VaultChanged = string.Concat(
                $"Another program saved {VaultName}, and keypaste could not merge it: {MergeNotice.Refused(refused)}. Agents are refused until you reload it.",
                state.Unwritten ? Discarded() : string.Empty,
                _reloadFailure is { } failed ? $" Reloading failed: {failed}" : string.Empty);
            return;
        }

        if (session.Merges)
        {
            ShowMerging();
            return;
        }

        VaultChanged = string.Concat(
            $"Another program saved {VaultName}. Agents are refused until you reload it.",
            state.Unwritten ? Discarded() : string.Empty,
            _reloadFailure is { } failure ? $" Reloading failed: {failure}" : string.Empty);
    }

    private void OpenAgents() => Current = Destinations.Of(DestinationKind.AgentActivity);

    private void ShowMerging()
    {
        VaultSaveWords = "merging";
        VaultStatusTone = StatusTone.Pending;
        VaultStatusDetail = $"Merging another program's save · {VaultPath}";
        VaultChanged = null;
    }

    private void OnMerged(object? sender, MergeResult merged)
    {
        if (sender is AppVaultSession session)
        {
            Post(() => ShowMerged(session, merged));
        }
    }

    /// <summary>
    /// Says what a merge of any open vault did, naming that vault, and shows the merged copy on the screen open now where it
    /// reads that vault, leaving an item being edited as typed, so its next save writes by name to the merged copy; a refusal
    /// is said by the banner the status reads.
    /// </summary>
    private void ShowMerged(AppVaultSession session, MergeResult merged)
    {
        if (_disposed)
        {
            return;
        }

        if (merged.Merged)
        {
            var shown = ReadsVault(session);

            if (shown)
            {
                VaultMoved?.Invoke();
            }

            if (ReferenceEquals(session, ShownSession))
            {
                _reloadFailure = null;
            }

            if (merged.Outcome == VaultMergeSaveOutcome.Updated)
            {
                _updatedAt[session] = merged.At;
            }
            else
            {
                _updatedAt.Remove(session);
            }

            if (merged.Outcome == VaultMergeSaveOutcome.Saved)
            {
                var said = MergeNotice.Saved(FileNameOf(session), merged);
                Notice = _noticeNamesAConflict && Notice is { } standing ? $"{standing} {said}" : said;
                _noticeNamesAConflict |= MergeNotice.NamesAConflict(merged);
            }

            Count();

            switch (shown ? Content : null)
            {
                case null:
                    break;

                case EntriesViewModel entries:
                    entries.ShowChangeFromElsewhere();
                    break;

                case ScopedTokensViewModel tokens:
                    tokens.ShowChangeFromElsewhere();
                    break;

                case IHoldsDraft { UnsavedDraft: not null }:
                    break;

                case EnvSetsViewModel env:
                    env.Reload();
                    break;

                default:
                    Show(_current);
                    break;
            }

            if (session.IsUnlocked)
            {
                ScanOf(session).Check();
            }
        }

        ReadVaultStatus();
        RebuildSidebar();
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
        var unsaved = ShownSession.Unsaved;
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
        if (ShownSession.SaveOpenCopyOverFile() is { } failed)
        {
            _reloadFailure = failed;
        }

        ReadVaultStatus();
    }

    private void ReloadNow()
    {
        var session = ShownSession;
        var result = session.Reload();

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
        ScanOf(session).Check();
        ReadVaultStatus();
    }

    private void OnSaved(object? sender, EventArgs e) => Post(() =>
    {
        if (!_disposed && sender is AppVaultSession session)
        {
            _updatedAt.Remove(session);
            ReadVaultStatus();

            if (session.IsUnlocked)
            {
                ScanOf(session).Check();
            }

            RebuildSidebar();
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
            case EntriesViewModel entries:
                entries.ShowChangeFromElsewhere();
                break;

            case IHoldsDraft { UnsavedDraft: not null }:
                break;

            case EnvSetsViewModel env:
                env.Reload();
                break;
        }
    });

    private void OnEdited(object? sender, VaultEdit edit) => Post(() =>
    {
        if (_disposed || sender is not AppVaultSession session)
        {
            return;
        }

        _updatedAt.Remove(session);
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
        _session.WaitingForTouch -= OnWaitingForTouch;

        foreach (var session in _followed.Keys.ToList())
        {
            Unfollow(session);
        }

        _ownWatch?.Dispose();
        _ownWatch = null;

        if (Authority is not null)
        {
            Authority.TerminalEdited -= OnTerminalEdited;
        }

        Recommendations.Dispose();

        foreach (var scan in _scans.Values)
        {
            scan.PropertyChanged -= OnScanChanged;
            scan.Dispose();
        }

        _scans.Clear();
        CloseDiscardPrompt();
        DropNewItemPassword();
        CloseShare();
        CloseGenerator();

        _statusTimer?.Dispose();
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
