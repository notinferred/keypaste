using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;

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
    private DesktopPreferences? _preferences;
    private MinimizeLock? _minimize;
    private ActivityWatch? _activity;
    private Shortcuts? _shortcuts;
    private MainWindow? _window;
    private UnlockViewModel? _unlock;
    private ShellViewModel? _shell;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private (string Path, string? Keyfile)? _openNext;
    private bool _shuttingDown;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Launch(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Composes the app into a desktop lifetime: the session, the authority serving it, the main
    /// window and what quitting does.
    /// </summary>
    /// <param name="desktop">The lifetime the app runs in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="desktop"/> is null.</exception>
    /// <remarks><c>internal</c> for the reason <see cref="Watch"/> is: a test runs what launch runs.</remarks>
    internal void Launch(IClassicDesktopStyleApplicationLifetime desktop)
    {
        ArgumentNullException.ThrowIfNull(desktop);

        var home = Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable);

        _preferences = new DesktopPreferences(home);
        _session = Compose(_preferences, TimeProvider.System);
        _session.Locked += OnLocked;
        _authority = new AppAuthority(
            _session,
            Environment.GetEnvironmentVariable(ApproverEndpoint.EnvironmentVariable),
            () => new WindowApprovalChannel(TimeProvider.System, ApprovalLimits.Default.Window),
            AppAuthority.RequestLock(_session, action => Dispatcher.UIThread.Post(action)));

        _window = new MainWindow();
        _activity = Observe(_window, _session, TimeProvider.System, () => _shell?.ClearCountdown());
        _shortcuts = Bind(_window, _session, () => _unlock, () => _shell);
        _minimize = Watch(_window, _preferences, _session, () => _unlock?.CancelPendingRestore());
        ShowUnlock(home);

        _desktop = desktop;
        desktop.MainWindow = _window;

        // A prompt window still open must not keep the vault served once the main window closes (F.21).
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        desktop.ShutdownRequested += OnShutdownRequested;
    }

    /// <summary>The authority <see cref="Launch"/> composed, or null before it or after quitting.</summary>
    internal AppAuthority? Authority => _authority;

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
            KeypasteHome.Resolve(Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable)));
    }

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
    /// <param name="onActivity">What else a keystroke, click or wheel turn does.</param>
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

        if (_shuttingDown || _shell is null)
        {
            Dispose();
            return;
        }

        _shuttingDown = true;
        e.Cancel = true;

        var shell = _shell;

        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await shell.Clipboard.CloseAsync().ConfigureAwait(true);
            Dispose();
            _desktop?.Shutdown();
        });
    }

    private void OnLocked(object? sender, VaultLockReason reason)
    {
        Dispatcher.UIThread.Post(() =>
            ShowUnlock(
                Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable),
                reason == VaultLockReason.AccessChanged ? AccessChangedMessage : null,
                reason));
    }

    /// <summary>Keeps an imported file in place: locks the open vault and puts that file on the unlock screen.</summary>
    private void OpenInPlace(string path, string? keyfile)
    {
        _openNext = (path, keyfile);
        _session?.Lock(VaultLockReason.Manual);
    }

    /// <summary>What the unlock screen says when an access change could not carry on with the vault open.</summary>
    internal const string AccessChangedMessage =
        "The vault's password or keyfile was changed, and it locked rather than open again. Unlock it with the new ones.";

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
        _unlock = new UnlockViewModel(
            _session, home, new StorageProviderPicker(_window), OnUnlocked,
            action => Dispatcher.UIThread.Post(action),
            message,
            next is null ? reason : null);

        if (next is { } file)
        {
            _unlock.Offer(file.Path, file.Keyfile);
        }

        _window.FindControl<ContentControl>("Root")!.Content =
            new UnlockView { DataContext = _unlock };
    }

    private void OnUnlocked()
    {
        if (_window is null || _session is null)
        {
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
            new StorageProviderPicker(_window),
            OpenInPlace);

        _window.FindControl<ContentControl>("Root")!.Content =
            new ShellView { DataContext = _shell };
    }

    /// <summary>
    /// Applies a theme choice. <c>System</c> hands the decision back to the operating system.
    /// </summary>
    internal void ApplyTheme(Core.Settings.AppTheme theme) =>
        RequestedThemeVariant = theme switch
        {
            Core.Settings.AppTheme.Light => Avalonia.Styling.ThemeVariant.Light,
            Core.Settings.AppTheme.Dark => Avalonia.Styling.ThemeVariant.Dark,
            _ => Avalonia.Styling.ThemeVariant.Default,
        };

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

        _authority?.Dispose();
        _authority = null;
        _session = null;
    }
}
