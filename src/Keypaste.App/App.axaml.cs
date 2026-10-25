using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Keypaste.App.Clipboard;
using Keypaste.App.HardwareKeys;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.HardwareKeys;
using Keypaste.Core.Ipc;
using Keypaste.Core.Linking;
using Keypaste.Core.Login;
using Keypaste.Core.Ownership;
using Keypaste.Core.Recent;

namespace Keypaste.App;

/// <summary>
/// The application object, and the one place the app's objects are constructed.
/// </summary>
/// <remarks>
/// There is no dependency-injection container. Composition is a handful of objects built by hand in
/// one method, which is what <c>CliContext.CreateDefault</c> already does on the other side of this
/// repository and is easier to follow than a container for something this size.
/// </remarks>
internal sealed partial class App : Application, IDisposable
{
    private AppVaultSession? _session;
    private AppAuthority? _authority;
    private Task? _ending;
    private DesktopPreferences? _preferences;
    private MinimizeLock? _minimize;
    private ActivityWatch? _activity;
    private Shortcuts? _shortcuts;
    private MainWindow? _window;
    private UnlockViewModel? _unlock;
    private ShellViewModel? _shell;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private AppTray? _tray;
    private IActivatableLifetime? _activatable;
    private IDisposable? _reopen;
    private LockedNotice? _lockedNotice;
    private (string Path, string? Keyfile)? _openNext;
    private int _unlockScreens;
    private bool _shuttingDown;
    private bool _quitting;
    private Core.Settings.AppTheme _theme = Core.Settings.AppTheme.System;
    private PlatformThemeVariant _platformTheme = PlatformThemeVariant.Dark;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        FollowPlatformTheme();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Launch(desktop, LoginItems.StartsInBackground(Environment.GetCommandLineArgs()));
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Composes the app into a desktop lifetime: the session, the authority serving it, the main
    /// window and what quitting does.
    /// </summary>
    /// <param name="desktop">The lifetime the app runs in.</param>
    /// <param name="background">A start at login, which opens no window while the app stays in the menu bar or tray.</param>
    /// <exception cref="ArgumentNullException"><paramref name="desktop"/> is null.</exception>
    /// <remarks><c>internal</c> for the reason <see cref="Watch"/> is: a test runs what launch runs.</remarks>
    internal void Launch(IClassicDesktopStyleApplicationLifetime desktop, bool background = false)
    {
        ArgumentNullException.ThrowIfNull(desktop);

        var home = Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable);

        _preferences = Preferences ?? new DesktopPreferences(home);
        _session = Compose(_preferences, TimeProvider.System);
        _session.Locked += OnLocked;
        _session.Opened += (_, _) => Dispatcher.UIThread.Post(() => _tray?.Refresh());
        _authority = new AppAuthority(
            _session,
            Environment.GetEnvironmentVariable(ApproverEndpoint.EnvironmentVariable),
            () => new WindowApprovalChannel(TimeProvider.System, ApprovalLimits.Default.Window, OpenAgentSettings),
            AppAuthority.RequestLock(_session, action => Dispatcher.UIThread.Post(action)));

        _window = new MainWindow();
        _activity = Observe(_window, _session, TimeProvider.System, () => _shell?.ClearCountdown());
        _shortcuts = Bind(_window, _session, () => _unlock, () => _shell);
        _minimize = Watch(_window, _preferences, _session, () => _unlock?.CancelPendingRestore());

        // A Mac user looks for an import under File (D-0374); elsewhere it is on Items' "+".
        if (OperatingSystem.IsMacOS())
        {
            NativeMenu.SetMenu(_window, AppMenu.Build(() => _shell));
        }

        // While locked, the app that holds its home tells the person once that an agent asked for a vault it would open (D-0466).
        _lockedNotice = Claim is null ? null : new LockedNotice(
            _session,
            OpenWindow,
            TimeProvider.System,
            reason => Console.Error.WriteLine($"keypaste: an agent asking for a locked vault cannot reach this app: {reason}"));

        ShowUnlock(home);

        _tray = new AppTray(OpenWindow, () => _ = _session?.LockAsync(VaultLockReason.Manual), () => _session?.IsUnlocked == true, Quit);
        _tray.Show(_preferences.StaysInTray);
        _preferences.Changed += OnPreferencesChanged;
        _window.Closing += OnMainWindowClosing;

        _desktop = desktop;

        // macOS answers a Dock click or a second open of the app by reopening the running one.
        _activatable = TryGetFeature(typeof(IActivatableLifetime)) as IActivatableLifetime;

        if (_activatable is not null)
        {
            _activatable.Activated += OnActivated;
        }

        // A second start of this user's app in this home asks it to show the window instead (D-0397).
        _reopen = Claim?.Listen(
            () => Dispatcher.UIThread.InvokeAsync(OpenWindow).GetTask(),
            reason => Console.Error.WriteLine($"keypaste: another start of keypaste cannot reach this one: {reason}"));

        // The lifetime shows its main window when it starts, so a start at login names none until Open.
        if (!StartsHidden(background, _preferences))
        {
            desktop.MainWindow = _window;
        }

        DockIcon?.Invoke(desktop.MainWindow is not null);

        // A prompt window still open must not keep the vault served once the main window closes (F.21).
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        desktop.ShutdownRequested += OnShutdownRequested;
    }

    /// <summary>The authority <see cref="Launch"/> composed, or null before it or after quitting.</summary>
    internal AppAuthority? Authority => _authority;

    /// <summary>The menu bar or tray icon <see cref="Launch"/> composed.</summary>
    internal AppTray? Tray => _tray;

    /// <summary>What tells the person an agent found the app locked; none unless <see cref="Claim"/> is held, or after quitting.</summary>
    internal LockedNotice? LockedNotice => _lockedNotice;

    /// <summary>The main window <see cref="Launch"/> composed, shown or not.</summary>
    internal MainWindow? Main => _window;

    /// <summary>The preferences launch composes from, which <c>Program</c> reads first; <c>app.toml</c> in the keypaste home unless a test gives others.</summary>
    internal DesktopPreferences? Preferences { get; init; }

    /// <summary>This process's claim on its user's home, which <c>Program.Run</c> took; none unless it gives one.</summary>
    internal AppClaim? Claim { get; init; }

    /// <summary>The entry that opens the app at login; the platform's own unless a test gives another.</summary>
    internal Func<ILoginItem?> LoginItemFor { get; init; } = LoginItems.ForThisProcess;

    /// <summary>The link a terminal finds this app's <c>keypaste</c> through; the platform's own unless a test gives another.</summary>
    internal Func<ICliLink?> CliLinkFor { get; init; } = CliLinks.ForThisProcess;

    /// <summary>What a project's Run and Open terminal start with, given the authority; the platform's own unless a test gives another.</summary>
    internal Func<AppAuthority?, ProjectLaunching> LaunchingFor { get; init; } = ProjectLaunching.ForThisMachine;

    /// <summary>Shows the Dock icon while the main window is open and hides it while the app is only in the menu bar; set on macOS alone, and by a test.</summary>
    internal Action<bool>? DockIcon { get; init; }

    /// <summary>Whether a start opens no window: one at login while the app stays in the menu bar or tray.</summary>
    /// <param name="background">Whether it is a start at login.</param>
    /// <param name="preferences">The preferences it starts with.</param>
    internal static bool StartsHidden(bool background, DesktopPreferences preferences) =>
        background && preferences.StaysInTray;

    /// <summary>Shows the window from the menu bar or tray.</summary>
    internal void OpenWindow()
    {
        if (_window is null || _desktop is null)
        {
            return;
        }

        _desktop.MainWindow ??= _window;
        DockIcon?.Invoke(true);
        _window.Show();

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }

    /// <summary>Shows the window on Agents with one agent's settings open, as the Always for notice asks.</summary>
    /// <param name="agent">The agent's name.</param>
    internal void OpenAgentSettings(string agent)
    {
        OpenWindow();
        _shell?.OpenAgentSettings(agent);
    }

    /// <summary>Closes the window into the menu bar or tray; closing still ends the session, so the process stays locked and holds no secret (D-0385).</summary>
    internal void CloseToTray()
    {
        if (_session is null || _window is null)
        {
            return;
        }

        var unlocked = _session.IsUnlocked;

        // Locking also stops a hardware key waiting for its touch, so an unlock under way cannot finish with a later touch.
        _ = _session.LockAsync(VaultLockReason.Closed);

        if (!unlocked)
        {
            // A password typed on the unlock screen is dropped with it, and an unlock it started locks again as it finishes.
            ShowUnlock(Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable));
        }

        _window.Hide();
        DockIcon?.Invoke(false);
    }

    private void OnActivated(object? sender, ActivatedEventArgs e)
    {
        if (e.Kind == ActivationKind.Reopen)
        {
            OpenWindow();
        }
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_quitting || _preferences?.StaysInTray != true || e.CloseReason != WindowCloseReason.WindowClosing)
        {
            return;
        }

        e.Cancel = true;
        CloseToTray();
    }

    private void Quit()
    {
        _quitting = true;
        StopAnswering();
        _ = _desktop?.TryShutdown();
    }

    // A quitting app no longer tells a second start it showed its window, so that start waits to take the home instead, and no longer hears an agent find it locked.
    private void StopAnswering()
    {
        _reopen?.Dispose();
        _reopen = null;
        _lockedNotice?.Dispose();
        _lockedNotice = null;
    }

    private void OnPreferencesChanged(object? sender, EventArgs e)
    {
        if (_preferences is { } preferences)
        {
            _tray?.Show(preferences.StaysInTray);
        }
    }

    /// <summary>How the app asks for a file; the platform's picker unless a test gives another.</summary>
    internal Func<TopLevel, IVaultFilePicker> Pickers { get; init; } = window => new StorageProviderPicker(window);

    /// <summary>KeePassXC's local settings files, whose databases the first run offers; read each time the welcome shows, where KeePassXC keeps them unless a test gives others.</summary>
    internal Func<IReadOnlyList<string>> KeePassXcConfigs { get; init; } = KeePassXcDatabases.LocalConfigPaths;

    /// <summary>How an item's web address reaches the browser; the platform's launcher unless a test gives another.</summary>
    internal Func<TopLevel, IWebLauncher> WebLaunchers { get; init; } = window => new PlatformWebLauncher(window);

    /// <summary>How share links reach their server; the shell makes one unless a test gives another.</summary>
    internal HttpMessageHandler? ShareTransport { get; init; }

    /// <summary>
    /// Turns the saved preferences into behaviour: the palette the app paints in and the timeout
    /// its session locks on.
    /// </summary>
    /// <param name="preferences">The preferences this process was composed from.</param>
    /// <param name="clock">The clock the session measures idleness against.</param>
    /// <returns>A session armed for <paramref name="preferences"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="preferences"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// <b>The theme is applied before the window is built</b>, so the first frame is painted in the
    /// saved palette rather than flashing the default one on the way to it.
    /// </para>
    /// <para>
    /// <c>internal</c> so a test can run exactly what launch runs. Starting a second Avalonia
    /// application to check this is not available — the test assembly's headless session is one per
    /// process and says why — so the seam is this method rather than the process.
    /// </para>
    /// </remarks>
    internal AppVaultSession Compose(DesktopPreferences preferences, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        ApplyTheme(preferences.Current.Theme);

        return new AppVaultSession(
            clock,
            preferences.IdleTimeout,
            KeypasteHome.Resolve(Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable)),
            HardwareKeys(),
            QuickUnlockSigner());
    }

    /// <summary>What reaches hardware keys; the connected YubiKeys unless a test gives another.</summary>
    internal Func<IChallengeResponseDevice> HardwareKeys { get; init; } = () => new YubiKeyHardware();

    /// <summary>What a quick unlock asks; Windows Hello in the Windows build on Windows and none elsewhere, unless a test gives another.</summary>
    internal Func<IQuickUnlockSigner?> QuickUnlockSigner { get; init; } = QuickUnlockSigners.ForThisMachine;

    /// <summary>
    /// Arms the minimize-lock setting against a window.
    /// </summary>
    /// <param name="window">The window a person minimizes.</param>
    /// <param name="preferences">The preferences this process was composed from.</param>
    /// <param name="session">The session a minimize locks.</param>
    /// <param name="whileLocked">What else a minimize drops: a restore pending on the unlock screen.</param>
    /// <returns>The watch, which the application owns for its lifetime.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <remarks>
    /// <c>internal</c> for the same reason <see cref="Compose"/> is: the defect F.2b repairs was
    /// that composition never introduced the saved setting to any behaviour at all, so a test has
    /// to run this method rather than a hand-built equivalent, which would have passed against the
    /// broken app throughout (D-0095). The setting is passed as a function rather than a value, so
    /// a change in Settings is in force at the next minimize rather than at the next launch.
    /// </remarks>
    internal static MinimizeLock Watch(
        Window window,
        DesktopPreferences preferences,
        AppVaultSession session,
        Action? whileLocked = null)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(session);

        // The locked screen can hold a checked backup's password one click from an open vault, so the
        // setting that closes a vault on minimize drops that too.
        return new MinimizeLock(
            window,
            () => preferences.LockWhenMinimized,
            () =>
            {
                session.Lock(VaultLockReason.Minimized);
                whileLocked?.Invoke();
            });
    }

    /// <summary>
    /// Arms idle-activity tracking against a window.
    /// </summary>
    /// <param name="window">The window a person uses.</param>
    /// <param name="session">The session activity keeps open.</param>
    /// <param name="clock">The clock the pointer throttle measures against.</param>
    /// <param name="onActivity">What else a keystroke, click, wheel turn or counted pointer move does.</param>
    /// <returns>The watch, which the application owns for its lifetime.</returns>
    /// <remarks><c>internal</c> for the reason <see cref="Watch"/> is: a test runs what launch runs.</remarks>
    internal static ActivityWatch Observe(
        Window window,
        AppVaultSession session,
        TimeProvider clock,
        Action onActivity) =>
        new(window, session, clock, onActivity);

    /// <summary>
    /// Binds the keyboard chords to a window.
    /// </summary>
    /// <param name="window">The window a person types into.</param>
    /// <param name="session">The session <c>Ctrl/Cmd+L</c> locks.</param>
    /// <param name="unlock">The unlock screen, while it is showing.</param>
    /// <param name="shell">The unlocked shell, while it is showing.</param>
    /// <returns>The binding, which the application owns for its lifetime.</returns>
    /// <remarks><c>internal</c> for the reason <see cref="Watch"/> is: a test runs what launch runs.</remarks>
    internal static Shortcuts Bind(
        Window window,
        AppVaultSession session,
        Func<UnlockViewModel?> unlock,
        Func<ShellViewModel?> shell) =>
        new(window, session, unlock, shell);

    /// <summary>
    /// Takes a secret back off the clipboard before the process goes away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cancels the shutdown once, clears, then shuts down for real. Without this a quit two seconds
    /// after a copy leaves the password on the clipboard with nothing left running to clear it, and
    /// quitting is the most ordinary thing anybody does with an app.
    /// </para>
    /// <para>
    /// <b>The limit, said out loud:</b> this is the orderly path only. <c>kill -9</c>, End Task, an
    /// OOM kill, a power cut and a logout each skip it entirely, and nothing can be done about that
    /// from inside a process that is being killed. THREATS.md T-19 says so rather than implying a
    /// promise this cannot keep.
    /// </para>
    /// </remarks>
    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        StopAnswering();

        if (_shuttingDown || _shell is null)
        {
            Dispose();

            // A second request while the quit below is stopping the endpoint ends the process only once it has stopped (D-0392).
            _ending?.Wait();
            return;
        }

        _shuttingDown = true;
        e.Cancel = true;

        var shell = _shell;

        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await shell.Clipboard.CloseAsync().ConfigureAwait(true);
            await QuitAsync().ConfigureAwait(true);
            _desktop?.Shutdown();
        });
    }

    /// <summary>
    /// Hides the window, so nothing reaches the screens it disposes, drops what <see cref="Dispose"/> drops, then waits
    /// for the authority to end on a thread of its own, so the app keeps responding while the endpoint stops (D-0392).
    /// </summary>
    private async Task QuitAsync()
    {
        var authority = _authority;
        _authority = null;

        if (_window is { IsVisible: true } window)
        {
            window.Hide();
        }

        Dispose();

        if (authority is not null)
        {
            _ending = authority.QuitAsync();
            await _ending.ConfigureAwait(true);
        }
    }

    private void OnLocked(object? sender, VaultLockReason reason)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ShowUnlock(Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable), LockMessage(reason), reason);
            _tray?.Refresh();
        });
    }

    /// <summary>What the unlock screen says after a lock for <paramref name="reason"/>, or null where its lock note is enough.</summary>
    internal static string? LockMessage(VaultLockReason reason) => reason switch
    {
        VaultLockReason.AccessChanged => AccessChangedMessage,
        VaultLockReason.ReloadRefused => ReloadRefusedMessage,
        _ => null,
    };

    /// <summary>Keeps an imported file in place: locks the open vault and puts that file on the unlock screen.</summary>
    private void OpenInPlace(string path, string? keyfile)
    {
        _openNext = (path, keyfile);
        _ = _session?.LockAsync(VaultLockReason.Manual);
    }

    /// <summary>What the unlock screen says when an access change could not carry on with the vault open.</summary>
    internal const string AccessChangedMessage =
        "The vault's password or keyfile was changed, and it locked rather than open again. Unlock it with the new ones.";

    /// <summary>What the unlock screen says when a reload found that another program changed what unlocks the vault.</summary>
    internal const string ReloadRefusedMessage =
        "Another program changed this vault's password or keyfile, so it locked instead of reloading. Unlock it with the new ones.";

    private void ShowUnlock(string? home, string? message = null, VaultLockReason? reason = null)
    {
        if (_session is null || _window is null)
        {
            return;
        }

        // The shell leaves the tree rather than being hidden, and its view models are disposed, so
        // nothing derived from an open vault can outlive the lock.
        _shell?.Dispose();
        _shell = null;

        var next = _openNext;
        _openNext = null;

        _unlock?.Dispose();

        var screen = ++_unlockScreens;
        _unlock = new UnlockViewModel(
            _session, home, Pickers(_window), () => OnUnlocked(screen),
            action => Dispatcher.UIThread.Post(action),
            message,
            next is null ? reason : null,
            KeePassXcConfigs,
            new AvaloniaClipboard(_window).TryPasteIntoAsync);

        if (next is { } file)
        {
            _unlock.Offer(file.Path, file.Keyfile);
        }

        _lockedNotice?.Watch(_unlock);

        _window.FindControl<ContentControl>("Root")!.Content =
            new UnlockView { DataContext = _unlock };
    }

    private void OnUnlocked(int screen)
    {
        if (_window is null || _session is null)
        {
            return;
        }

        // An unlock that finishes after its screen was replaced, by closing into the tray, opened a vault nobody is looking at.
        if (screen != _unlockScreens)
        {
            _session.Lock(VaultLockReason.Closed);
            return;
        }

        _shell?.Dispose();
        _shell = new ShellViewModel(
            _session,
            Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable),
            _authority,
            ApplyTheme,
            new AvaloniaClipboard(_window),
            TimeProvider.System,
            action => Dispatcher.UIThread.Post(action),
            _preferences,
            _unlock?.Notice,
            Pickers(_window),
            OpenInPlace,
            WebLaunchers(_window))
        {
            ShareTransport = ShareTransport,
            LoginItem = LoginItemFor(),
            CliLink = CliLinkFor(),
            Tools = ClientConnector.ForThisProcess,
            Launching = LaunchingFor(_authority),
        };

        _window.FindControl<ContentControl>("Root")!.Content =
            new ShellView { DataContext = _shell };
    }

    /// <summary>
    /// Applies a theme choice. <c>System</c> paints in the operating system's light or dark setting,
    /// and keeps doing so as it changes.
    /// </summary>
    /// <remarks>
    /// System is the platform's current variant set explicitly, not <see cref="ThemeVariant.Default"/>:
    /// once Light or Dark had been requested, Avalonia's Default left the app with no variant at all,
    /// painting neither palette, until the platform next changed.
    /// </remarks>
    internal void ApplyTheme(Core.Settings.AppTheme theme)
    {
        _theme = theme;
        RequestedThemeVariant = theme switch
        {
            Core.Settings.AppTheme.Light => ThemeVariant.Light,
            Core.Settings.AppTheme.Dark => ThemeVariant.Dark,
            _ => VariantOf(_platformTheme),
        };
    }

    /// <summary>Tracks the operating system's light or dark setting, and follows it while System is chosen.</summary>
    private void FollowPlatformTheme()
    {
        if (PlatformSettings is not { } settings)
        {
            return;
        }

        // What Avalonia has already resolved from the platform, when it has; its settings' own answer otherwise.
        _platformTheme = ActualThemeVariant == ThemeVariant.Dark ? PlatformThemeVariant.Dark
            : ActualThemeVariant == ThemeVariant.Light ? PlatformThemeVariant.Light
            : settings.GetColorValues().ThemeVariant;
        settings.ColorValuesChanged += (_, values) =>
        {
            _platformTheme = values.ThemeVariant;

            if (_theme == Core.Settings.AppTheme.System)
            {
                RequestedThemeVariant = VariantOf(values.ThemeVariant);
            }
        };
    }

    private static ThemeVariant VariantOf(PlatformThemeVariant platform) =>
        platform == PlatformThemeVariant.Dark ? ThemeVariant.Dark : ThemeVariant.Light;

    /// <summary>Drops the vault and the unlock screen's password buffer.</summary>
    /// <remarks>
    /// <see cref="Application"/> is not disposable, so this is called from the desktop lifetime's
    /// shutdown rather than by the framework. CA1001 is an error in this repository and it is right
    /// to be: the two fields below are the vault and the master-password buffer.
    /// </remarks>
    public void Dispose()
    {
        _minimize?.Dispose();
        _minimize = null;
        _activity?.Dispose();
        _activity = null;
        _shortcuts?.Dispose();
        _shortcuts = null;
        _shell?.Dispose();
        _shell = null;
        _unlock?.Dispose();
        _unlock = null;
        _tray?.Dispose();
        _tray = null;

        if (_activatable is not null)
        {
            _activatable.Activated -= OnActivated;
            _activatable = null;
        }

        StopAnswering();

        if (_preferences is not null)
        {
            _preferences.Changed -= OnPreferencesChanged;
        }

        _authority?.Dispose();
        _authority = null;
        _session = null;
    }
}
