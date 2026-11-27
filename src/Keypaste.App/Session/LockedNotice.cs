using System.ComponentModel;
using Avalonia.Controls;
using Keypaste.App.Notifications;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Audit;
using Keypaste.Core.Ownership;
using Keypaste.Core.Settings;

namespace Keypaste.App.Session;

/// <summary>
/// While a vault open in the app is locked, hears a bridge about to refuse its agent for want of an owner, and tells
/// the person once that an agent asked.
/// </summary>
/// <remarks>
/// It listens for every vault the window lists that is locked, and, while every vault is locked, for the vault the
/// unlock screen shows and for the chosen vault, following the unlock screen as its vault changes and the chosen vault
/// as <c>app.toml</c> changes. Whichever vault was asked for, one notice is shown until a vault next opens or, once that
/// notice is gone, every vault locks again, and a vault opening takes it down. Only its Open keypaste shows the window, and while every vault is locked it puts that vault
/// on the unlock screen, asking Windows Hello or Touch ID at once where they can reopen it. It is a system notification, which names no vault since the lock screen and other
/// apps can read it; where the system takes none, as Windows takes none of these, keypaste shows its own window, which names the vault.
/// </remarks>
internal sealed class LockedNotice : IDisposable
{
    // Long enough for a system that answers at once; a permission dialog the person has not reached yet gets the window.
    private static readonly TimeSpan _systemAnswers = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan _choicePoll = TimeSpan.FromSeconds(3);

    private readonly OpenVaults _vaults;
    private readonly Action _open;
    private readonly TimeProvider _clock;
    private readonly Action<string> _unreachable;
    private readonly Action<Exception, string>? _unexpected;
    private readonly ISystemNotifications _notifications;
    private readonly Func<string, string, FileSystemWatcher> _watch;

    // Read and written on the UI thread only.
    private readonly Dictionary<string, LockedAppEndpoint> _listening = new(StringComparer.Ordinal);
    private FileSystemWatcher? _choiceWatcher;
    private ITimer? _choicePoller;
    private UnlockViewModel? _screen;
    private RaisedNotice? _raised;
    private bool _told;
    private bool _allLocked;
    private bool _disposed;

    /// <summary>Hears nothing until the first unlock screen is given to <see cref="Watch"/>.</summary>
    /// <param name="vaults">The vaults open in the app, whose locked ones it listens for.</param>
    /// <param name="open">Shows the main window.</param>
    /// <param name="clock">The clock a notice's arming delay, and the reading of the chosen vault where <c>app.toml</c> cannot be watched, run on.</param>
    /// <param name="unreachable">Told why, when a vault's endpoint cannot be bound and no bridge can reach this app for it.</param>
    /// <param name="unexpected">Keeps a report of a notice that could not be shown, at the site it names, or null.</param>
    /// <param name="notifications">The system's notifications, or null to use the window alone.</param>
    /// <param name="watch">Makes the watcher of a directory's file by name, or null for the system's.</param>
    internal LockedNotice(
        OpenVaults vaults,
        Action open,
        TimeProvider clock,
        Action<string> unreachable,
        Action<Exception, string>? unexpected = null,
        ISystemNotifications? notifications = null,
        Func<string, string, FileSystemWatcher>? watch = null)
    {
        ArgumentNullException.ThrowIfNull(vaults);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(unreachable);

        _vaults = vaults;
        _open = open;
        _clock = clock;
        _unreachable = unreachable;
        _unexpected = unexpected;
        _notifications = notifications ?? new NoSystemNotifications();
        _watch = watch ?? ((directory, file) => new FileSystemWatcher(directory, file));
        _vaults.Opened += OnOpened;
        _vaults.Locked += OnLocked;
        _vaults.PlacesChanged += OnPlacesChanged;
    }

    /// <summary>Raised on the UI thread once the notice's window is on screen.</summary>
    internal event EventHandler<Window>? Shown;

    /// <summary>The notice's window on screen, or null.</summary>
    internal LockedNoticeWindow? Showing { get; private set; }

    /// <summary>Listens for the locked vaults, the vault <paramref name="screen"/> shows and the chosen vault, and follows the screen as its vault changes.</summary>
    /// <param name="screen">The unlock screen just put up; called on the UI thread for each one.</param>
    internal void Watch(UnlockViewModel screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        if (_disposed)
        {
            return;
        }

        if (!ReferenceEquals(_screen, screen))
        {
            if (_screen is not null)
            {
                _screen.PropertyChanged -= OnScreenChanged;
            }

            _screen = screen;
            screen.PropertyChanged += OnScreenChanged;
        }

        Listen();
    }

    /// <summary>Stops listening and takes a notice still up down. Doing it twice is not an error.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _vaults.Opened -= OnOpened;
        _vaults.Locked -= OnLocked;
        _vaults.PlacesChanged -= OnPlacesChanged;

        if (_screen is not null)
        {
            _screen.PropertyChanged -= OnScreenChanged;
            _screen = null;
        }

        Stop();
    }

    private void OnOpened(object? sender, EventArgs e) => UiThread.Post(Unlocked);

    private void OnLocked(object? sender, VaultLockReason e) => UiThread.Post(Listen);

    private void OnPlacesChanged(object? sender, EventArgs e) => UiThread.Post(Listen);

    private void OnChoiceChanged(object? sender, FileSystemEventArgs e) => UiThread.Post(Listen);

    private void OnChoiceWatchFailed(object? sender, ErrorEventArgs e) => UiThread.Post(() =>
    {
        if (ReferenceEquals(sender, _choiceWatcher))
        {
            PollChoice();
            Listen();
        }
    });

    private void OnScreenChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UnlockViewModel.SelectedPath))
        {
            Listen();
        }
    }

    // A vault is open, so agents ask it now, and the next agent to find a vault locked may tell the person again.
    private void Unlocked()
    {
        TakeNoticeDown();
        _told = false;
        Listen();
    }

    // Nothing is heard before the first unlock screen.
    private void Listen()
    {
        if (_disposed || _screen is null)
        {
            return;
        }

        var allLocked = AllLocked;

        // Every vault locking is the app locking, which may tell the person again once the notice from before is gone.
        if (allLocked && !_allLocked && _raised is null && Showing is null)
        {
            _told = false;
        }

        _allLocked = allLocked;

        if (allLocked)
        {
            WatchChoice();
        }
        else
        {
            StopWatchingChoice();
        }

        var wanted = Wanted();

        foreach (var (name, endpoint) in _listening.Where(listening => !wanted.ContainsKey(listening.Key)).ToList())
        {
            endpoint.Dispose();
            _listening.Remove(name);
        }

        foreach (var (name, vault) in wanted.Where(want => !_listening.ContainsKey(want.Key)))
        {
            // Owned by _listening from here, and disposed once no longer wanted or at quit. CA2000 cannot see a lifetime that leaves the method.
#pragma warning disable CA2000
            _listening[name] = LockedAppEndpoint.Listen(name, () => UiThread.Post(() => Tell(name, vault)), _unreachable);
#pragma warning restore CA2000
        }
    }

    // Keyed by endpoint, so a vault both listed and on the unlock screen or chosen binds its endpoint once.
    private Dictionary<string, string> Wanted()
    {
        var vaults = _vaults.Listed.Where(open => !open.Session.IsUnlocked).Select(open => _vaults.PathOf(open.Session));

        if (AllLocked)
        {
            vaults = vaults.Prepend(ChosenVault.Read(Home)).Prepend(_screen?.SelectedPath);
        }

        var wanted = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var vault in vaults.OfType<string>())
        {
            wanted.TryAdd(EndpointFor(vault), vault);
        }

        return wanted;
    }

    // With no vault unlocked, the window shows the unlock screen last given to Watch.
    private bool AllLocked => _vaults.Unlocked.Count == 0;

    private string Home => _vaults.InView.Session.Home;

    // `keypaste use` changes the chosen vault while the app stays locked, with no lock or change of screen to follow.
    private void WatchChoice()
    {
        if (_choiceWatcher is not null || _choicePoller is not null)
        {
            return;
        }

        var settings = KeypasteHome.SettingsPath(Home);

        try
        {
            _choiceWatcher = _watch(Path.GetDirectoryName(settings)!, Path.GetFileName(settings));
            _choiceWatcher.Changed += OnChoiceChanged;
            _choiceWatcher.Created += OnChoiceChanged;
            _choiceWatcher.Deleted += OnChoiceChanged;
            _choiceWatcher.Renamed += OnChoiceChanged;
            _choiceWatcher.Error += OnChoiceWatchFailed;
            _choiceWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            PollChoice();
        }
    }

    // Where the system refuses or drops the watch, as at Linux's limit on watches, the choice is read again on the clock until the next unlock.
    private void PollChoice()
    {
        _choiceWatcher?.Dispose();
        _choiceWatcher = null;
        _choicePoller ??= _clock.CreateTimer(_ => UiThread.Post(Listen), null, _choicePoll, _choicePoll);
    }

    private void StopWatchingChoice()
    {
        _choiceWatcher?.Dispose();
        _choiceWatcher = null;
        _choicePoller?.Dispose();
        _choicePoller = null;
    }

    private void Tell(string name, string vault)
    {
        if (_told || _disposed || !_listening.ContainsKey(name) || !Wanted().ContainsKey(name))
        {
            return;
        }

        _told = true;

        var raised = RaisedNotice.Raise(_notifications, SystemNotice.VaultLocked, () => Open(name, vault));
        _raised = raised;
        _ = FallBackAsync(raised, name, vault);
    }

    private async Task FallBackAsync(RaisedNotice raised, string name, string vault)
    {
        if (!await raised.AcceptedWithinAsync(_systemAnswers, _clock).ConfigureAwait(false))
        {
            raised.Remove();
            UiThread.Post(() => ShowWindow(raised, name, vault));
        }
    }

    private void ShowWindow(RaisedNotice raised, string name, string vault)
    {
        if (!ReferenceEquals(_raised, raised) || _disposed)
        {
            return;
        }

        // The system's notice is down, so from here only this window, if it shows, is up.
        _raised = null;

        if (!Wanted().ContainsKey(name))
        {
            return;
        }

        try
        {
            var notice = new LockedNoticeWindow(Path.GetFileName(vault), () => Open(name, vault), _clock);
            notice.Closed += (_, _) =>
            {
                if (ReferenceEquals(Showing, notice))
                {
                    Showing = null;
                }
            };
            notice.Show();
            Showing = notice;
            Shown?.Invoke(this, notice);
        }
        catch (Exception ex)
        {
            // A notice that cannot be shown changes nothing: the agent was still refused, and told the app is locked.
            _unexpected?.Invoke(ex, CrashReporting.LockedNoticeSite);
        }
    }

    private void Open(string name, string vault)
    {
        _raised?.Remove();
        _raised = null;

        if (AllLocked && _screen is { } screen && (screen.SelectedPath is not { } selected || EndpointFor(selected) != name))
        {
            screen.Offer(vault);
        }

        _open();

        // The press is the person's own act, so a vault Windows Hello or Touch ID can reopen asks for it at once.
        if (AllLocked)
        {
            _screen?.AskQuickUnlockAtOnce();
        }
    }

    private void Stop()
    {
        TakeNoticeDown();
        StopWatchingChoice();

        foreach (var endpoint in _listening.Values)
        {
            endpoint.Dispose();
        }

        _listening.Clear();
    }

    private void TakeNoticeDown()
    {
        _raised?.Remove();
        _raised = null;
        Showing?.Close();
    }

    private string EndpointFor(string vault) => LockedAppEndpoint.NameFor(VaultIdentity.Of(Home, vault));
}
