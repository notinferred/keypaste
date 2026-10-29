using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Audit;
using Keypaste.Core.Ownership;
using Keypaste.Core.Settings;

namespace Keypaste.App.Session;

/// <summary>
/// While the app is locked, hears a bridge about to refuse its agent for want of an owner, and tells the person
/// once that an agent asked (D-0466).
/// </summary>
/// <remarks>
/// It listens for the vault the unlock screen shows and for the chosen vault, follows the unlock screen as its
/// vault changes and the chosen vault as <c>app.toml</c> changes, and stops when a vault opens. One notice is
/// shown each time the app is locked, whichever vault was asked for; it names that vault, and only its Open
/// keypaste shows the unlock screen, with that vault on it.
/// </remarks>
internal sealed class LockedNotice : IDisposable
{
    private readonly AppVaultSession _session;
    private readonly Action _open;
    private readonly TimeProvider _clock;
    private readonly Action<string> _unreachable;
    private readonly Action<Exception, string>? _unexpected;

    // Read and written on the UI thread only.
    private readonly Dictionary<string, LockedAppEndpoint> _listening = new(StringComparer.Ordinal);
    private FileSystemWatcher? _choiceWatcher;
    private UnlockViewModel? _screen;
    private bool _told;
    private bool _disposed;

    /// <summary>Hears nothing until the first unlock screen is given to <see cref="Watch"/>.</summary>
    /// <param name="session">The session whose opening of a vault ends the listening.</param>
    /// <param name="open">Shows the main window.</param>
    /// <param name="clock">The clock a notice's arming delay runs on.</param>
    /// <param name="unreachable">Told why, when a vault's endpoint cannot be bound and no bridge can reach this app for it.</param>
    /// <param name="unexpected">Keeps a report of a notice that could not be shown, at the site it names, or null (D-0596).</param>
    internal LockedNotice(AppVaultSession session, Action open, TimeProvider clock, Action<string> unreachable, Action<Exception, string>? unexpected = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(unreachable);

        _session = session;
        _open = open;
        _clock = clock;
        _unreachable = unreachable;
        _unexpected = unexpected;
        _session.Opened += OnOpened;
    }

    /// <summary>Raised on the UI thread once a notice is on screen.</summary>
    internal event EventHandler<Window>? Shown;

    /// <summary>The notice on screen, or null.</summary>
    internal LockedNoticeWindow? Showing { get; private set; }

    /// <summary>Listens for the vault <paramref name="screen"/> shows and the chosen vault, and follows the screen as its vault changes.</summary>
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
        _session.Opened -= OnOpened;

        if (_screen is not null)
        {
            _screen.PropertyChanged -= OnScreenChanged;
            _screen = null;
        }

        Stop();
    }

    private void OnOpened(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Unlocked);

    private void OnChoiceChanged(object? sender, FileSystemEventArgs e) => Dispatcher.UIThread.Post(Listen);

    private void OnScreenChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UnlockViewModel.SelectedPath))
        {
            Listen();
        }
    }

    // A vault is open, so agents ask it now, and the next lock may tell the person again.
    private void Unlocked()
    {
        Stop();
        _told = false;
    }

    private void Listen()
    {
        if (_disposed || _session.IsUnlocked)
        {
            return;
        }

        WatchChoice();

        var wanted = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var vault in new[] { _screen?.SelectedPath, ChosenVault.Read(_session.Home) })
        {
            if (vault is not null)
            {
                wanted.TryAdd(EndpointFor(vault), vault);
            }
        }

        foreach (var (name, endpoint) in _listening.Where(listening => !wanted.ContainsKey(listening.Key)).ToList())
        {
            endpoint.Dispose();
            _listening.Remove(name);
        }

        foreach (var (name, vault) in wanted.Where(want => !_listening.ContainsKey(want.Key)))
        {
            // Owned by _listening from here, and disposed when no longer wanted, at an unlock and at quit. CA2000 cannot see a lifetime that leaves the method.
#pragma warning disable CA2000
            _listening[name] = LockedAppEndpoint.Listen(name, () => Dispatcher.UIThread.Post(() => Tell(name, vault)), _unreachable);
#pragma warning restore CA2000
        }
    }

    // `keypaste use` changes the chosen vault while the app stays locked, with no lock or change of screen to follow.
    private void WatchChoice()
    {
        if (_choiceWatcher is not null)
        {
            return;
        }

        var settings = KeypasteHome.SettingsPath(_session.Home);

        try
        {
            _choiceWatcher = new FileSystemWatcher(Path.GetDirectoryName(settings)!, Path.GetFileName(settings));
            _choiceWatcher.Changed += OnChoiceChanged;
            _choiceWatcher.Created += OnChoiceChanged;
            _choiceWatcher.Renamed += OnChoiceChanged;
            _choiceWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // A home that cannot be watched leaves the choice to be read again at the next lock or change of screen.
            _choiceWatcher?.Dispose();
            _choiceWatcher = null;
        }
    }

    private void Tell(string name, string vault)
    {
        if (_told || _disposed || _session.IsUnlocked || !_listening.ContainsKey(name))
        {
            return;
        }

        _told = true;

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
        if (!_session.IsUnlocked && _screen is { } screen && (screen.SelectedPath is not { } selected || EndpointFor(selected) != name))
        {
            screen.Offer(vault);
        }

        _open();
    }

    private void Stop()
    {
        _choiceWatcher?.Dispose();
        _choiceWatcher = null;

        foreach (var endpoint in _listening.Values)
        {
            endpoint.Dispose();
        }

        _listening.Clear();
        Showing?.Close();
    }

    private string EndpointFor(string vault) => LockedAppEndpoint.NameFor(VaultIdentity.Of(_session.Home, vault));
}
