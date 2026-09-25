using System.Globalization;
using Keypaste.App.Clipboard;
using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.Core;

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
    private bool _mcpRunning;
    private string _mcpDetail = string.Empty;
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
        IVaultFilePicker? picker = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _notice = notice;
        _picker = picker;
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
        OpenAgentsCommand = new RelayCommand(() => Current = Destinations.Of(DestinationKind.AgentActivity));

        MainNav = [.. Destinations.Main.Select(d => new NavItem(d))];
        FooterNav = [.. Destinations.Footer.Select(d => new NavItem(d))];

        _current = Destinations.All[0];
        _session.LockingSoon += OnLockingSoon;
        _session.Edited += OnEdited;

        // Built here rather than left to the first navigation. Assigning Current to the destination
        // it already holds changes nothing, so Show never ran and the shell opened on a blank pane.
        Show(_current);
        ReadAuthority();

        if (authority is not null)
        {
            _statusTimer = _clock.CreateTimer(_ => Post(ReadAuthority), null, _statusTick, _statusTick);
        }
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

    /// <summary>The auto-clearing clipboard, and the toast that counts it down.</summary>
    internal ClipboardCountdown Clipboard { get; }

    /// <summary>The value of <c>KEYPASTE_HOME</c>, or null.</summary>
    internal string? Home { get; }

    /// <summary>What serves this vault to agents, or null where nothing does.</summary>
    internal AppAuthority? Authority { get; }

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

    /// <summary>The main row for <see cref="Current"/>, or null while a footer row is current.</summary>
    internal NavItem? SelectedMain
    {
        get => MainNav.FirstOrDefault(item => item.Destination == _current);
        set
        {
            if (value is not null)
            {
                Current = value.Destination;
            }
        }
    }

    /// <summary>The footer row for <see cref="Current"/>, or null while a main row is current.</summary>
    internal NavItem? SelectedFooter
    {
        get => FooterNav.FirstOrDefault(item => item.Destination == _current);
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
            if (Set(ref _projects, value))
            {
                Raise(nameof(HasProjects));
            }
        }
    }

    internal bool HasProjects => _projects.Count > 0;

    /// <summary>Opens Env profiles on a project.</summary>
    internal RelayCommand<string> OpenProjectCommand { get; }

    /// <summary>Whether the sidebar offers "Import .kdbx". Nothing imports yet, so it does not.</summary>
#pragma warning disable CA1822
    internal bool ImportAvailable => false;
#pragma warning restore CA1822

    /// <summary>The open vault's file name. The full path is a tooltip, never a heading.</summary>
    internal string VaultName =>
        _session.VaultPath is { } path ? Path.GetFileName(path) : string.Empty;

    /// <summary>The open vault's full path, for the tooltip.</summary>
    internal string VaultPath => _session.VaultPath ?? string.Empty;

    /// <summary>The titlebar's status line.</summary>
    internal string VaultStatus => VaultName.Length > 0 ? $"{VaultName} · unlocked" : string.Empty;

    /// <summary>Locks now.</summary>
    internal RelayCommand LockCommand { get; }

    /// <summary>Whether this app is answering agents for the vault, as its authority says.</summary>
    internal bool McpRunning
    {
        get => _mcpRunning;
        private set
        {
            if (Set(ref _mcpRunning, value))
            {
                Raise(nameof(McpState));
            }
        }
    }

    internal string McpState => _mcpRunning ? "running" : "stopped";

    /// <summary>The MCP card's second line: what the authority knows, and nothing it does not.</summary>
    internal string McpDetail
    {
        get => _mcpDetail;
        private set => Set(ref _mcpDetail, value);
    }

    /// <summary>Opens Agents, from the MCP card.</summary>
    internal RelayCommand OpenAgentsCommand { get; }

    /// <summary>The titlebar search. It filters Secrets, and moves there to do it.</summary>
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
                Raise(nameof(CurrentTitle));
                Raise(nameof(ShowsHeader));
                Raise(nameof(SelectedMain));
                Raise(nameof(SelectedFooter));
                Show(value);
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

    /// <summary>Moves to a destination by its shortcut digit.</summary>
    /// <param name="digit">A destination's position in the sidebar, from 1.</param>
    /// <returns><see langword="true"/> when a destination matched.</returns>
    internal bool GoTo(int digit)
    {
        foreach (var destination in Navigation.Destinations.All)
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
        (Content as IDisposable)?.Dispose();

        Content = destination.Kind switch
        {
            // The audit log is machine state, which is why `keypaste log` reads it without a vault.
            DestinationKind.Log => new LogViewModel(Home),
            DestinationKind.Settings => new SettingsViewModel(_session, Home, Preferences, ApplyTheme, _picker),
            DestinationKind.AgentActivity => Activity(),
            DestinationKind.Entries => Entries(),
            DestinationKind.EnvSets => new EnvSetsViewModel(_session, Clipboard, _picker),
            DestinationKind.Trash => new TrashViewModel(_session),
            _ => null,
        };

        Count();
    }

    private EntriesViewModel Entries()
    {
        var entries = new EntriesViewModel(_session, Clipboard);

        if (_search.Length > 0)
        {
            entries.Search = _search;
        }

        return entries;
    }

    private AgentActivityViewModel Activity() => new(Authority, Home, _clock, _post);

    private void OpenProject(string? project)
    {
        if (project is null)
        {
            return;
        }

        Current = Destinations.Of(DestinationKind.EnvSets);

        if (Content is EnvSetsViewModel env)
        {
            env.OpenCommand.Execute(project);
        }
    }

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
            var entries = vault.ReadEntries();
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
        SetCount(DestinationKind.EnvSets, names.Count);
    }

    private void SetCount(DestinationKind kind, int count, bool live = false)
    {
        foreach (var item in MainNav.Concat(FooterNav))
        {
            if (item.Destination.Kind == kind)
            {
                item.Count = count > 0 ? count.ToString(CultureInfo.InvariantCulture) : string.Empty;
                item.IsLive = live && count > 0;
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
            var grants = activity.Grants.Count;
            var waiting = activity.Waiting.Count;

            McpRunning = true;
            McpDetail = waiting > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{waiting} waiting for you · {Grants(grants)}")
                : Grants(grants);
            SetCount(DestinationKind.AgentActivity, grants + waiting, live: true);
            return;
        }

        McpRunning = false;
        McpDetail = status switch
        {
            AuthorityStatus.HeldBy => "another keypaste holds this vault",
            AuthorityStatus.NotServing => "agents cannot reach this vault",
            _ => "not serving agents",
        };
        SetCount(DestinationKind.AgentActivity, 0);
    }

    private static string Grants(int count) => count switch
    {
        0 => "no grants in force",
        1 => "1 grant in force",
        _ => string.Create(CultureInfo.InvariantCulture, $"{count} grants in force"),
    };

    private void OnEdited(object? sender, VaultEdit edit) => Post(Count);

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
        _statusTimer?.Dispose();
        _toastTimer?.Dispose();
        Notice = null;
        Toast = null;
        Projects = [];
        (Content as IDisposable)?.Dispose();
        Content = null;

        // A secret on the clipboard is derived from an open vault, so it does not survive the lock
        // either. Disposing clears it, conditionally — a clipboard the user has changed since is
        // left alone.
        Clipboard.Dispose();
    }
}
