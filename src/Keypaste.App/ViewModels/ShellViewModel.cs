using Keypaste.App.Clipboard;
using Keypaste.App.Navigation;
using Keypaste.App.Session;

namespace Keypaste.App.ViewModels;

/// <summary>
/// The main window once a vault is open: a sidebar, a content region, and a way out.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything here is disposed on lock.</b> The whole shell leaves the visual tree rather than
/// being hidden, so "locked" has exactly one meaning and nothing that was derived from an open
/// vault can survive it. In 4.1 there is almost nothing to survive; making it a rule now is what
/// stops 4.2 quietly caching an entry list.
/// </para>
/// </remarks>
internal sealed class ShellViewModel : ObservableObject, IDisposable
{
    private readonly AppVaultSession _session;
    private Destination _current;
    private object? _content;
    private string _countdown = string.Empty;
    private bool _disposed;

    internal ShellViewModel(
        AppVaultSession session,
        string? home,
        string? approverFromEnvironment,
        Action<Core.Settings.AppTheme>? applyTheme = null,
        IAppClipboard? clipboard = null,
        TimeProvider? clock = null,
        Action<Action>? post = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        Home = home;
        ApproverFromEnvironment = approverFromEnvironment;
        ApplyTheme = applyTheme ?? (_ => { });

        // Owned here rather than by each screen, so a copy made on Entries is still counting down
        // after a move to Env Sets — and is cleared by the lock, because this is disposed with
        // everything else the shell built.
        Clipboard = new ClipboardCountdown(
            clipboard ?? NoClipboard.Instance,
            clock ?? TimeProvider.System,
            post);

        LockCommand = new RelayCommand(() => _session.Lock(VaultLockReason.Manual));

        _current = Destinations.All[0];
        _session.LockingSoon += OnLockingSoon;

        // Built here rather than left to the first navigation. Assigning Current to the destination
        // it already holds changes nothing, so Show never ran and the shell opened on a blank pane —
        // invisible in 4.1, when the first destination was an empty state with nothing to miss.
        Show(_current);
    }

    /// <summary>The auto-clearing clipboard, and the toast that counts it down.</summary>
    internal ClipboardCountdown Clipboard { get; }

    /// <summary>The value of <c>KEYPASTE_HOME</c>, or null.</summary>
    internal string? Home { get; }

    /// <summary>The value of <c>KEYPASTE_APPROVER</c>, or null.</summary>
    internal string? ApproverFromEnvironment { get; }

    /// <summary>How a theme choice reaches the application object.</summary>
    /// <remarks>
    /// Passed in rather than reached for, so this class still names no Avalonia type and its tests
    /// still need no application.
    /// </remarks>
    internal Action<Core.Settings.AppTheme> ApplyTheme { get; }

    /// <summary>The five places the sidebar offers.</summary>
    /// <remarks>
    /// An instance property over a static list, because a binding needs one. The trailing
    /// underscore keeps it from colliding with the <see cref="Navigation.Destinations"/> class it
    /// reads from.
    /// </remarks>
#pragma warning disable CA1822
    internal IReadOnlyList<Destination> Destinations_ => Navigation.Destinations.All;
#pragma warning restore CA1822

    /// <summary>The open vault's file name. The full path is a tooltip, never a heading.</summary>
    internal string VaultName =>
        _session.VaultPath is { } path ? Path.GetFileName(path) : string.Empty;

    /// <summary>The open vault's full path, for the tooltip.</summary>
    internal string VaultPath => _session.VaultPath ?? string.Empty;

    /// <summary>Locks now.</summary>
    internal RelayCommand LockCommand { get; }

    /// <summary>Where the sidebar is.</summary>
    internal Destination Current
    {
        get => _current;
        set
        {
            if (value is not null && Set(ref _current, value))
            {
                Raise(nameof(CurrentTitle));
                Show(value);
            }
        }
    }

    /// <summary>The current destination's title, for the content header.</summary>
    internal string CurrentTitle => _current.Title;

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
    /// Muted, not red, and not a dialog. <c>the Ideas table in DECISIONS.md</c> names "red scary warnings for normal
    /// actions" as an anti-pattern, and an auto-lock is the most normal thing this app does.
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
    /// <param name="digit">1 through 5.</param>
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
            // Real in 4.1, and it needs no unlocked vault: the audit log is machine state, which is
            // why `keypaste log` reads it without one.
            DestinationKind.Log => new LogViewModel(Home),
            DestinationKind.Settings => new SettingsViewModel(_session, Home, ApplyTheme),
            DestinationKind.AgentActivity => Activity(),
            DestinationKind.Entries => new EntriesViewModel(_session, Clipboard),
            DestinationKind.EnvSets => new EnvSetsViewModel(_session, Clipboard),
            _ => null,
        };
    }

    private AgentActivityViewModel Activity()
    {
        var activity = new AgentActivityViewModel(ApproverFromEnvironment);
        _ = activity.RefreshAsync();
        return activity;
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
        (Content as IDisposable)?.Dispose();
        Content = null;

        // A secret on the clipboard is derived from an open vault, so it does not survive the lock
        // either. Disposing clears it, conditionally — a clipboard the user has changed since is
        // left alone.
        Clipboard.Dispose();
    }
}
