using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Keypaste.App.Clipboard;
using Keypaste.App.Globalization;
using Keypaste.App.GlobalShortcuts;
using Keypaste.App.HardwareKeys;
using Keypaste.App.Notifications;
using Keypaste.App.Session;
using Keypaste.App.Terms;
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
    private IQuickUnlockKeys? _quickUnlockKeys;
    private bool _quickUnlockComposed;
    private AppAuthority? _authority;
    private OpenVaults? _vaults;
    private Task? _ending;
    private DesktopPreferences? _preferences;
    private MinimizeLock? _minimize;
    private ActivityWatch? _activity;
    private Shortcuts? _shortcuts;
    private AppCommands? _commands;
    private NativeMenu? _appMenu;
    private AboutWindow? _about;
    private Action<UnlockViewModel>? _afterLock;
    private MainWindow? _window;
    private UnlockViewModel? _unlock;
    private ShellViewModel? _shell;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private AppTray? _tray;
    private SearchShortcut? _searchShortcut;
    private IActivatableLifetime? _activatable;
    private IDisposable? _reopen;
    private LockedNotice? _lockedNotice;
    private ISystemNotifications? _notifications;
    private (string Path, string? Keyfile, int? HardwareKeySlot)? _openNext;
    private ProjectRun? _lastRun;
    private int _unlockScreens;
    private string? _unlockForBrowser;
    private bool _shuttingDown;
    private bool _quitting;
    private bool _lockedAll;
    private bool _crashed;
    private Core.Settings.WindowLayout? _untouchedLayout;
    private const string _scrollBarsAutoHide = "KpScrollBarsAutoHide";

    private Core.Settings.AppTheme _theme = Core.Settings.AppTheme.System;
    private PlatformThemeVariant _platformTheme = PlatformThemeVariant.Dark;
    private ColorContrastPreference _platformContrast = ColorContrastPreference.NoPreference;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        _ = Theme.FontWeights.Pin(this, FontManager.Current);

        if (ReadingDirection.Of(CultureInfo.CurrentUICulture) == FlowDirection.RightToLeft)
        {
            Styles.Insert(0, ReadingDirection.RightToLeftWindows());
        }

        // macOS reads the app menu once, as the platform starts and before Launch, so it is made here and filled there.
        if (OperatingSystem.IsMacOS())
        {
            _appMenu = new NativeMenu();
            NativeMenu.SetMenu(this, _appMenu);
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        FollowPlatformTheme();

        // Only the app's start composes crash reports, and before anything can throw on the UI thread its exception is
        // made to lock, clear and quit; a test that launches the app in the shared test process sets no hook.
        if (Crashes is not null)
        {
            Dispatcher.UIThread.UnhandledException += OnUnhandledException;
        }

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
        var preferences = _preferences;
        _notifications = Notifications ?? SystemNotifications.ForThisProcess();

        if (OperatingSystem.IsLinux())
        {
            _ = AskTheDesktopAboutLightOrDarkAsync();
        }

        var requestNotices = new RequestNotices(_notifications, TimeProvider.System, () => desktop.Windows.Any(window => window.IsActive));
        var prompts = new WindowApprovalChannel.Screen();

        // Each vault is served on its own pipe, every vault's prompts share one screen and one queue, and they lock together.
#pragma warning disable CA2000 // The open vaults own the session in view, and the authority each composes owns its session.
        _vaults = new OpenVaults(
            Compose(preferences, TimeProvider.System),
            () => NewSession(preferences, TimeProvider.System),
            (vaults, session) => new AppAuthority(
                session,
                Environment.GetEnvironmentVariable(ApproverEndpoint.EnvironmentVariable),
                () => new WindowApprovalChannel(
                    TimeProvider.System,
                    ApprovalLimits.Default.Window,
                    Unexpected,
                    requestNotices,
                    () => _window is { } window ? Clipboards(window) : NoClipboard.Instance,
                    prompts,
                    () => OpenVaults.NameShown(session)),
                AppAuthority.RequestLock(session, action => Dispatcher.UIThread.Post(action)),
                Unexpected,
                vaults.Turns,
                () => vaults.KeysBesides(session),
                vaults.Pairings),
            preferences);
#pragma warning restore CA2000
        _session = _vaults.InView.Session;
        _authority = _vaults.InView.Authority;

        // While a vault is locked, the app that holds its home tells the person once that an agent asked for it.
        _lockedNotice = Claim is null ? null : new LockedNotice(
            _vaults,
            OpenWindow,
            TimeProvider.System,
            reason => Console.Error.WriteLine($"keypaste: an agent asking for a locked vault cannot reach this app: {reason}"),
            Unexpected,
            _notifications);
        _vaults.Locked += OnLocked;

        // A page's list asks to bring keypaste forward: the window, a locked vault's unlock, or New login in the site's vault.
        _vaults.Pairings.Show = (what, vault, page) => Dispatcher.UIThread.Post(() => ShowAsked(what, vault, page));
        _vaults.Opened += (opened, _) => Dispatcher.UIThread.Post(() => HandBackAfterTheBrowsersUnlock(opened as AppVaultSession));
        _session.Opened += (_, _) => Dispatcher.UIThread.Post(() => _tray?.Refresh());

        InterfaceZoom.Set(_preferences.Current.Window?.ZoomPercent ?? Core.Settings.WindowLayout.ActualSizePercent);
        InterfaceZoom.Changed += KeepWindowLayout;
        _window = new MainWindow();
        _window.AskForVibrancy(_preferences.ReduceTransparency);
        _window.Activated += (_, _) => _ = FollowScrollBarsAsync();

        if (_preferences.Current.Window is { } left)
        {
            _window.Restore(left);
        }
        else
        {
            _window.Opened += NoteUntouchedLayout;
        }

        _activity = Observe(_window, _vaults, TimeProvider.System, () => _shell?.ClearCountdown());
        _commands = Commands(_window, _session, home);
        _shortcuts = new Shortcuts(_window, _commands);
        _minimize = Watch(_window, _preferences, _session, () => _unlock?.CancelPendingRestore(), _vaults.LockAll);

        // The menus: the menu bar and the Dock's on macOS, the titlebar's menu button elsewhere.
        if (_commands.Mac)
        {
            NativeMenu.SetMenu(_window, AppMenu.Bar(_commands));
            NativeDock.SetMenu(this, AppMenu.Native(_commands.Dock));

            if (_appMenu is { Items.Count: 0 })
            {
                AppMenu.Fill(_appMenu, _commands.AppMenu);
            }
        }
        else
        {
            _window.UseMenuButton(AppMenu.Flyout(_commands));
        }

        ShowUnlock(home);

        if (HandedPath is { } handed)
        {
            _unlock?.TakeHanded(handed);
        }

        _tray = new AppTray(
            OpenWindowAtPersonsAct,
            SearchAtPersonsAct,
            () => _ = RunAgainAsync(),
            () => CanRunAgain,
            () => _ = _vaults?.LockAllAsync(VaultLockReason.Manual),
            () => _vaults?.Unlocked.Count > 0,
            Quit);
        _tray.Show(_preferences.StaysInTray);
        _searchShortcut = GlobalShortcutFor is { } system ? new SearchShortcut(system(), _preferences, SearchAtPersonsAct) : null;
        _preferences.Changed += OnPreferencesChanged;
        _window.Closing += OnMainWindowClosing;

        _desktop = desktop;

        // macOS answers a Dock click or a second open of the app by reopening the running one.
        _activatable = TryGetFeature(typeof(IActivatableLifetime)) as IActivatableLifetime;

        if (_activatable is not null)
        {
            _activatable.Activated += OnActivated;
        }

        // A second start of this user's app in this home asks it to show the window instead.
        _reopen = Claim?.Listen(
            path => Dispatcher.UIThread.InvokeAsync(() =>
            {
                OpenWindow();

                if (path is not null)
                {
                    TakeHanded(path);
                }
            }).GetTask(),
            reason => Console.Error.WriteLine($"keypaste: another start of keypaste cannot reach this one: {reason}"));

        // The lifetime shows its main window when it starts, so a start at login names none until Open.
        if (!StartsHidden(background, _preferences))
        {
            desktop.MainWindow = _window;
        }

        DockIcon?.Invoke(desktop.MainWindow is not null);

        // A prompt window still open must not keep the vault served once the main window closes.
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        desktop.ShutdownRequested += OnShutdownRequested;
    }

    /// <summary>The commands of the menus and the window's chords, as <see cref="Launch"/> composed them.</summary>
    internal AppCommands? Menus => _commands;

    /// <summary>The authority of the vault in view <see cref="Launch"/> composed, or null before it or after quitting.</summary>
    internal AppAuthority? Authority => _authority;

    /// <summary>The vaults open in the app, as <see cref="Launch"/> composed them, or null before it or after quitting.</summary>
    internal OpenVaults? Vaults => _vaults;

    /// <summary>The menu bar or tray icon <see cref="Launch"/> composed.</summary>
    internal AppTray? Tray => _tray;

    /// <summary>The chord that searches keypaste from any app, as <see cref="Launch"/> composed it, or null where none is.</summary>
    internal SearchShortcut? SearchShortcut => _searchShortcut;

    /// <summary>What tells the person an agent found a vault locked; none unless <see cref="Claim"/> is held, or after quitting.</summary>
    internal LockedNotice? LockedNotice => _lockedNotice;

    /// <summary>The system-wide chord the search shortcut registers; this platform's when <c>Program</c> starts the app, none unless a test gives one.</summary>
    internal Func<IGlobalShortcut>? GlobalShortcutFor { get; init; }

    /// <summary>The system's notifications; this platform's unless a test gives others.</summary>
    internal ISystemNotifications? Notifications { get; init; }

    /// <summary>The main window <see cref="Launch"/> composed, shown or not.</summary>
    internal MainWindow? Main => _window;

    /// <summary>The preferences launch composes from, which <c>Program</c> reads first; <c>app.toml</c> in the keypaste home unless a test gives others.</summary>
    internal DesktopPreferences? Preferences { get; init; }

    /// <summary>This process's claim on its user's home, which <c>Program.Run</c> took; none unless it gives one.</summary>
    internal AppClaim? Claim { get; init; }

    /// <summary>The file this start's arguments name, which the first unlock screen offers; none unless <c>Program</c> gives one.</summary>
    internal string? HandedPath { get; init; }

    /// <summary>The entry that opens the app at login; the platform's own unless a test gives another.</summary>
    internal Func<ILoginItem?> LoginItemFor { get; init; } = LoginItems.ForThisProcess;

    /// <summary>The link a terminal finds this app's <c>keypaste</c> through; the platform's own unless a test gives another.</summary>
    internal Func<ICliLink?> CliLinkFor { get; init; } = CliLinks.ForThisProcess;

    /// <summary>The browsers' hosts Settings turns on and off; the user's own unless a test or <c>Program</c> gives others.</summary>
    internal Func<BrowserHostSetup> BrowserHostsFor { get; init; } = BrowserHostSetup.ForThisProcess;

    /// <summary>What a project's Run and Open terminal start with, given the authority; the platform's own unless a test gives another.</summary>
    internal Func<AppAuthority?, ProjectLaunching> LaunchingFor { get; init; } = ProjectLaunching.ForThisMachine;

    /// <summary>Shows the Dock icon while the main window is open or a vault is unlocked, so ⌘Tab and the Dock reach the app, and hides it while the app is only in the menu bar; set on macOS alone, and by a test.</summary>
    internal Action<bool>? DockIcon { get; init; }

    /// <summary>Hides the whole app so the app used before it comes in front, as macOS's Hide keypaste does; none where the platform has no such call.</summary>
    internal Action? HideApp { get; init; }

    /// <summary>
    /// The crash reports of the process the app's start runs, whose choice the unlock screen also asks; none for an
    /// app a test or the previewer composes, which then shows neither and leaves an exception on the UI thread to the runtime.
    /// </summary>
    internal CrashReporting? Crashes { get; init; }

    /// <summary>How a window's clipboard is reached; the platform's unless a test or the driver gives another.</summary>
    internal Func<TopLevel, IAppClipboard> Clipboards { get; init; } = window => new AvaloniaClipboard(window);

    /// <summary>The exit code of a run a UI-thread exception ended.</summary>
    internal const int CrashExitCode = 70;

    /// <summary>How a run a UI-thread exception ended stops, given its exit code; the lifetime's own shutdown unless a test, whose shared dispatcher it would end, gives another.</summary>
    internal Action<int>? Ending { get; init; }

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

        // The window comes forward for something else now, so an unlock the browser asked for earlier keeps it in front.
        _unlockForBrowser = null;

        _desktop.MainWindow ??= _window;
        DockIcon?.Invoke(true);
        _window.Show();

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }

    /// <summary>
    /// Shows the window at an act of the person's own, a Dock click or the menu bar's or tray's Open, and where every vault is locked
    /// asks Windows Hello or Touch ID at once for a vault they can reopen.
    /// </summary>
    internal void OpenWindowAtPersonsAct()
    {
        OpenWindow();
        AskQuickUnlockAtOnce();
    }

    /// <summary>
    /// The search shortcut, pressed in any app, and Search… in the menu bar or tray: an agent's request waiting for an
    /// answer comes forward first, focused on Deny and disarmed for a second (THREATS T-11); otherwise the window, on Items
    /// with its search empty and holding the keyboard, or with every vault locked the unlock screen, which asks Windows
    /// Hello or Touch ID at once.
    /// </summary>
    internal void SearchAtPersonsAct()
    {
        // A Mac app pressed from the background is made active first, so what it shows comes in front of the app in use.
        DockIcon?.Invoke(true);

        if (_vaults?.ShowPrompt() == true)
        {
            return;
        }

        OpenWindow();

        if (_shell is null)
        {
            AskQuickUnlockAtOnce();
        }
        else
        {
            _commands?.StartSearch();
        }
    }

    private void AskQuickUnlockAtOnce()
    {
        if (_shell is null && Crashes is not { AsksTerms: true })
        {
            _unlock?.AskQuickUnlockAtOnce();
        }
    }

    /// <summary>Shows the window on Agents with one agent's settings open, as the Always allow notice asks.</summary>
    /// <param name="agent">The agent's name.</param>
    internal void OpenAgentSettings(string agent)
    {
        OpenWindow();
        _shell?.OpenAgentSettings(agent);
    }

    /// <summary>
    /// Closes the window into the menu bar or tray. Unlocked vaults stay unlocked, answering agents, until the idle lock,
    /// a lock the person makes, <c>keypaste lock</c> or quitting; with none unlocked the unlock screen starts over.
    /// </summary>
    internal void CloseToTray()
    {
        if (_session is null || _window is null)
        {
            return;
        }

        var unlocked = _vaults?.Unlocked.Count > 0;

        if (!unlocked)
        {
            // Locking stops a hardware key waiting for its touch, so an unlock under way cannot finish with a later touch.
            _ = _session.LockAsync(VaultLockReason.Closed);
            _ = _vaults?.LockAllAsync(VaultLockReason.Closed);

            // A password typed on the unlock screen is dropped with it, and an unlock it started locks again as it finishes.
            ShowUnlock(Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable));
        }

        _window.Hide();

        if (!unlocked)
        {
            DockIcon?.Invoke(false);
        }
    }

    /// <summary>
    /// Puts the browser that brought keypaste forward back in front, so the person can pick there: on a Mac keypaste hides as
    /// Hide keypaste does, elsewhere the window goes into the tray as closing it does. Vaults stay unlocked. With no tray on
    /// Windows or Linux the window stays, since nothing would bring it back, and so does one an agent's prompt waits beside.
    /// </summary>
    internal void HandBackToBrowser()
    {
        if (_window is null || _vaults?.IsAsking == true)
        {
            return;
        }

        if (HideApp is { } hide)
        {
            hide();
        }
        else if (_preferences?.StaysInTray == true)
        {
            _window.Hide();
        }
    }

    /// <summary>Hands keypaste back to the browser once the first vault to open after the browser's Unlock… is the one it asked for.</summary>
    private void HandBackAfterTheBrowsersUnlock(AppVaultSession? opened)
    {
        var asked = _unlockForBrowser;
        _unlockForBrowser = null;

        if (asked is not null && opened is not null && _vaults?.PathOf(opened) is { } path && Core.Settings.ChosenVault.Same(path, asked))
        {
            HandBackToBrowser();
        }
    }

    /// <summary>Whether Run again has a command to start: the last one a Run started, while its vault is unlocked, unless its stage is protected, whose run is asked on its card.</summary>
    internal bool CanRunAgain => _lastRun is { Protected: false } && RunAgainVault() is not null;

    /// <summary>
    /// Run again in the Dock or tray menu: starts once more the last command a Run started, in its project, stage and
    /// directory. It is the person's press in keypaste's own menu, so it starts as a Run does, recorded as pressed.
    /// </summary>
    /// <returns>Completes once the run started or was refused, which brings the window forward to say why.</returns>
    internal async Task RunAgainAsync()
    {
        if (_lastRun is not { Protected: false } last || RunAgainVault() is not { } vault)
        {
            return;
        }

        string? refused = null;
        using var launch = new ProjectLaunchViewModel(
            vault.Session,
            last.Project,
            picker: null,
            LaunchingFor(vault.Authority) with { Started = Remember },
            said => refused = said ?? refused,
            (said, tone) =>
            {
                if (said is not null)
                {
                    _shell?.ShowToast(said, tone);
                }
            });

        await launch.RunAgainAsync(last).ConfigureAwait(true);

        if (refused is not null)
        {
            OpenWindow();
            _shell?.ShowToast(refused, ToastTone.Warning);
        }
    }

    private OpenVault? RunAgainVault() =>
        _lastRun is { } last && _vaults?.Holding(last.Vault) is { Session.IsUnlocked: true } vault ? vault : null;

    private void Remember(ProjectRun run)
    {
        _lastRun = run;
        Dispatcher.UIThread.Post(() => _tray?.Refresh());
    }

    private void OnActivated(object? sender, ActivatedEventArgs e)
    {
        if (e.Kind == ActivationKind.Reopen)
        {
            OpenWindowAtPersonsAct();
        }
        else if (e is FileActivatedEventArgs { Files: [var file, ..] } && file.TryGetLocalPath() is { } path)
        {
            OpenWindow();
            TakeHanded(path);
        }
    }

    /// <summary>
    /// A file the system or a second start named: selected on the unlock screen, or with a vault open offered behind
    /// a question, since only the person's answer may lock that vault, and any draft in it goes through the discard ask.
    /// </summary>
    internal void TakeHanded(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (_shell is { } shell)
        {
            shell.AskOpenHanded(path, () =>
            {
                _afterLock = unlock => unlock.TakeHanded(path);
                _ = _vaults?.LockAllAsync(VaultLockReason.Manual);
            });
        }
        else
        {
            _unlock?.TakeHanded(path);
        }
    }

    private void NoteUntouchedLayout(object? sender, EventArgs e)
    {
        _window!.Opened -= NoteUntouchedLayout;
        _untouchedLayout = _window.Layout(false);
    }

    /// <summary>Keeps how the window stands in <c>app.toml</c>, on close, lock and quit; with no shell, what the last one left. A window nobody has touched since its first opening has nothing to keep, so the file stays as it was.</summary>
    private void KeepWindowLayout()
    {
        if (_crashed || _window is null || _preferences is null)
        {
            return;
        }

        var left = _preferences.Current.Window;

        var tables = _shell?.Tables;

        if (_window.Layout(_shell?.SidebarHidden ?? left?.SidebarHidden ?? false, tables is null ? left?.TableSorts : tables.SortsText, tables is null ? left?.TableWidths : tables.WidthsText) is { } layout
            && (left is not null || layout != _untouchedLayout))
        {
            _preferences.KeepWindow(layout);
        }
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        KeepWindowLayout();

        if (_quitting || _preferences?.StaysInTray != true || e.CloseReason != WindowCloseReason.WindowClosing)
        {
            return;
        }

        e.Cancel = true;
        CloseToTray();
    }

    private AppCommands Commands(MainWindow window, AppVaultSession session, string? home)
    {
        var mac = OperatingSystem.IsMacOS();

        return new AppCommands(new AppCommandHost
        {
            Session = session,
            Vaults = _vaults,
            Shell = () => _shell,
            Unlock = () => _unlock,
            AsksTerms = () => Crashes is { AsksTerms: true },
            Window = () => _window,
            Focused = mac ? null : () => window.EditTarget,
            Home = home,
            About = ShowAbout,
            Quit = Quit,
            OpenAnother = OpenAnother,
            OpenWindow = OpenWindow,
            ShowPrompt = () => _vaults?.ShowPrompt() == true,
            OpenWeb = OpenWeb,
            Hide = mac ? MacDock.Hide : null,
            HideOthers = mac ? MacDock.HideOthers : null,
            ShowAll = mac ? MacDock.ShowAll : null,
            RunAgain = () => _ = RunAgainAsync(),
            CanRunAgain = () => CanRunAgain,
            Search = SearchAtPersonsAct,
        });
    }

    private void ShowAbout()
    {
        if (_about is { } open)
        {
            open.Activate();
            return;
        }

        _about = new AboutWindow();
        _about.Closed += (_, _) => _about = null;
        _about.Show();
    }

    private void OpenWeb(Uri address)
    {
        if (_window is null)
        {
            return;
        }

#pragma warning disable RS0030 // Help's own pages, which the person asked to open from the Help menu.
        _ = WebLaunchers(_window).OpenAsync(address);
#pragma warning restore RS0030
    }

    /// <summary>File › Open vault… or Open recent with a vault open: locks it, then picks or offers the file on the unlock screen.</summary>
    private void OpenAnother(string? path)
    {
        _afterLock = path is null ? unlock => unlock.BrowseCommand.Press() : unlock => _ = unlock.Offer(path);
        _ = _vaults?.LockAllAsync(VaultLockReason.Manual);
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
            _window?.AskForVibrancy(preferences.ReduceTransparency);
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

        return NewSession(preferences, clock);
    }

    /// <summary>A locked session for one vault, with its own claim, idle timer and quick unlock copies, armed for <paramref name="preferences"/>.</summary>
    /// <param name="preferences">The preferences this process was composed from.</param>
    /// <param name="clock">The clock the session measures idleness against.</param>
    internal AppVaultSession NewSession(DesktopPreferences preferences, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var home = KeypasteHome.Resolve(Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable));

        return new AppVaultSession(
            clock,
            preferences.IdleTimeout,
            home,
            HardwareKeys(),
            QuickUnlockKeys is { } keys ? keys() : MachineKeys(home));
    }

    // One for the process, since Touch ID deletes what an earlier run under this home left when it is made.
    private IQuickUnlockKeys? MachineKeys(string home)
    {
        if (!_quickUnlockComposed)
        {
            _quickUnlockKeys = MachineQuickUnlock.Keys(home, Unexpected);
            _quickUnlockComposed = true;
        }

        return _quickUnlockKeys;
    }

    /// <summary>What reaches hardware keys; the connected YubiKeys unless a test gives another.</summary>
    internal Func<IChallengeResponseDevice> HardwareKeys { get; init; } = () => new YubiKeyHardware();

    /// <summary>What a quick unlock asks; Windows Hello in the Windows build on Windows, Touch ID on macOS and none elsewhere, unless a test gives another.</summary>
    internal Func<IQuickUnlockKeys?>? QuickUnlockKeys { get; init; }

    /// <summary>Keeps a report of an exception a catch-everything block caught, when crash reports are composed and on.</summary>
    /// <param name="exception">The exception.</param>
    /// <param name="site">The block, one of <see cref="CrashReporting"/>'s site names.</param>
    internal void Unexpected(Exception exception, string site) => Crashes?.Unexpected(exception, site);

    /// <summary>
    /// Arms the minimize-lock setting against a window.
    /// </summary>
    /// <param name="window">The window a person minimizes.</param>
    /// <param name="preferences">The preferences this process was composed from.</param>
    /// <param name="session">The session a minimize locks.</param>
    /// <param name="whileLocked">What else a minimize drops: a restore pending on the unlock screen.</param>
    /// <param name="lockAll">What locks every open vault, which launch gives so a minimize reaches a vault the session in view is not; null locks <paramref name="session"/>.</param>
    /// <returns>The watch, which the application owns for its lifetime.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <remarks>
    /// <c>internal</c> for the same reason <see cref="Compose"/> is: a saved setting that composition
    /// never wires to any behaviour is the failure to catch, so a test has to run this method rather
    /// than a hand-built equivalent, which would pass against an app that never armed it. The setting is passed as a function rather than a value, so
    /// a change in Settings is in force at the next minimize rather than at the next launch.
    /// </remarks>
    internal static MinimizeLock Watch(
        Window window,
        DesktopPreferences preferences,
        AppVaultSession session,
        Action? whileLocked = null,
        Action<VaultLockReason>? lockAll = null)
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
                (lockAll ?? session.Lock)(VaultLockReason.Minimized);
                whileLocked?.Invoke();
            });
    }

    /// <summary>
    /// Arms idle-activity tracking against a window.
    /// </summary>
    /// <param name="window">The window a person uses.</param>
    /// <param name="session">What activity keeps open: launch gives every open vault.</param>
    /// <param name="clock">The clock the pointer throttle measures against.</param>
    /// <param name="onActivity">What else a keystroke, click, wheel turn or counted pointer move does.</param>
    /// <returns>The watch, which the application owns for its lifetime.</returns>
    /// <remarks><c>internal</c> for the reason <see cref="Watch"/> is: a test runs what launch runs.</remarks>
    internal static ActivityWatch Observe(
        Window window,
        IIdleWatched session,
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
        KeepWindowLayout();

        if (_shuttingDown || _shell is null)
        {
            Dispose();

            // A second request while the quit below is stopping the endpoint ends the process only once it has stopped.
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
    /// for the authority to end on a thread of its own, so the app keeps responding while the endpoint stops.
    /// </summary>
    private async Task QuitAsync()
    {
        var vaults = _vaults;
        _vaults = null;
        _authority = null;

        if (_window is { IsVisible: true } window)
        {
            window.Hide();
        }

        Dispose();

        if (vaults is not null)
        {
            _ending = vaults.QuitAsync();
            await _ending.ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Any open vault locked: with another still open the window keeps them all, built again on an open one when the one
    /// it was built on locked, with that vault's unlock form in view; once none is open, the unlock screen.
    /// </summary>
    private void OnLocked(object? sender, VaultLockReason reason)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // An app ending after a crash builds no screen it is about to take down.
            if (_crashed || _vaults is not { } vaults)
            {
                return;
            }

            _tray?.Refresh();

            // The Dock icon stays only while the window shows or a vault is unlocked.
            if (vaults.Unlocked.Count == 0 && _window is { IsVisible: false })
            {
                DockIcon?.Invoke(false);
            }

            if (_shell is { } shell && vaults.Unlocked is [var open, ..])
            {
                if (ReferenceEquals(sender, shell.Anchor))
                {
                    KeepWindowLayout();
                    ShowShell(open, LockMessage(reason));
                    _shell?.ShowVaultOf(shell.Anchor);
                }

                return;
            }

            // Every vault locking together tells this once per vault; the first to arrive shows the unlock screen.
            if (_shell is null && (_lockedAll || !ReferenceEquals(sender, _session)))
            {
                return;
            }

            _lockedAll = _shell is not null;
            KeepWindowLayout();
            ShowUnlock(Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable), LockMessage(reason), reason);
        });
    }

    private void OnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        EndAfterCrash(e.Exception);
    }

    /// <summary>
    /// What an exception nothing on the UI thread caught does: its report is kept while reports are on, then the
    /// vault locks, the clipboard is given back and the app quits with <see cref="CrashExitCode"/>, so neither a copied
    /// secret nor the exception's message, which the runtime would print, outlives it.
    /// </summary>
    /// <remarks><c>internal</c> so a test runs it as the dispatcher's hook does; a second exception while it ends is only reported.</remarks>
    internal void EndAfterCrash(Exception exception)
    {
        Crashes?.Crash(exception);

        if (_crashed)
        {
            return;
        }

        _crashed = true;
        _quitting = true;
        StopAnswering();

        var session = _session;
        var clipboard = _shell?.Clipboard;
        var desktop = _desktop ?? ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (session is not null)
                {
                    await session.LockAsync(VaultLockReason.Shutdown).ConfigureAwait(true);
                }

                if (clipboard is not null)
                {
                    await clipboard.CloseAsync().ConfigureAwait(true);
                }

                await QuitAsync().ConfigureAwait(true);
            }
            finally
            {
                if (Ending is { } ending)
                {
                    ending(CrashExitCode);
                }
                else
                {
                    desktop?.Shutdown(CrashExitCode);
                }
            }
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
    private void OpenInPlace(string path, string? keyfile, int? hardwareKeySlot)
    {
        _openNext = (path, keyfile, hardwareKeySlot);
        _ = _vaults?.LockAllAsync(VaultLockReason.Manual);
    }

    /// <summary>What the unlock screen says when an access change could not carry on with the vault open.</summary>
    internal const string AccessChangedMessage =
        "The vault's password or key file was changed, and it locked rather than open again. Unlock it with the new ones.";

    /// <summary>What the unlock screen says when a reload found that another program changed what unlocks the vault.</summary>
    internal const string ReloadRefusedMessage =
        "Another program changed this vault's password or key file, so it locked instead of reloading. Unlock it with the new ones.";

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
            Clipboards(_window).TryPasteIntoAsync);

        if (next is { } file)
        {
            _unlock.Offer(file.Path, file.Keyfile, file.HardwareKeySlot);
        }

        _lockedNotice?.Watch(_unlock);

        ShowUnlockScreen();

        var then = _afterLock;
        _afterLock = null;

        if (then is not null && _unlock is { } unlock)
        {
            then(unlock);
        }
    }

    private void ShowUnlockScreen()
    {
        if (_window is null || _unlock is null)
        {
            return;
        }

        if (Crashes is { } crashes)
        {
            crashes.Screen = "unlock";
        }

        _window.FindControl<ContentControl>("Root")!.Content = Crashes is { AsksTerms: true } asking
            ? new FirstLaunchView { DataContext = new FirstLaunchViewModel(TermsOfUse.Carried, send => AgreeToTerms(asking, send), Quit) }
            : new UnlockView { DataContext = _unlock };
    }

    private void AgreeToTerms(CrashReporting crashes, bool sendCrashReports)
    {
        _ = crashes.Agree(sendCrashReports);
        ShowUnlockScreen();
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

        if (_vaults is { } vaults)
        {
            vaults.HoldAccount();
            ShowShell(vaults.InView, _unlock?.Notice);
        }
    }

    /// <summary>The window's shell, built on <paramref name="anchor"/>, the open vault whose own screens it shows, listing every open vault.</summary>
    private void ShowShell(OpenVault anchor, string? notice)
    {
        if (_window is null)
        {
            return;
        }

        _shell?.Dispose();
        _lockedAll = false;
        _shell = new ShellViewModel(
            anchor.Session,
            Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable),
            anchor.Authority,
            ApplyTheme,
            Clipboards(_window),
            TimeProvider.System,
            action => Dispatcher.UIThread.Post(action),
            _preferences,
            notice,
            Pickers(_window),
            OpenInPlace,
            WebLaunchers(_window))
        {
            ShareTransport = ShareTransport,
            OffersTheme = !SystemReportsLightOrDark,
            CalmMoment = _notifications is { } notifications ? new CalmPermission(notifications, () => _vaults?.IsAsking == true).Offer : null,
            LoginItem = LoginItemFor(),
            CliLink = CliLinkFor(),
            GlobalSearch = _searchShortcut,
            BrowserHosts = BrowserHostsFor(),
            HandBack = HandBackToBrowser,
            Tools = ClientConnector.ForThisProcess,
            Launching = LaunchingFor(anchor.Authority) with { Started = Remember },
            Crashes = Crashes,
            Vaults = _vaults,
        };

        _window.FindControl<ContentControl>("Root")!.Content =
            new ShellView { DataContext = _shell, Commands = _commands };
    }

    /// <summary>
    /// What a page's list asked keypaste to show: the window forward, then a vault's unlock form or New login in the shell, or the
    /// vault on the unlock screen when every vault is locked. A locked vault the browser asked to unlock hands keypaste back to it once open.
    /// </summary>
    private void ShowAsked(Core.Ipc.BrowserShow what, string vault, Core.Browser.NewLoginPage page)
    {
        OpenWindow();

        if (what == Core.Ipc.BrowserShow.Unlock && _vaults?.Named(vault) is { Session.IsUnlocked: false } asked)
        {
            _unlockForBrowser = _vaults.PathOf(asked.Session);
        }

        if (_shell is { } shell)
        {
            shell.ShowAsked(what, vault, page);
        }
        else if (what == Core.Ipc.BrowserShow.Unlock && _vaults?.Named(vault) is { } named && _vaults.PathOf(named.Session) is { } path)
        {
            // Unlock… is the person's press in their paired browser.
            _unlock?.TakeHanded(path);
            AskQuickUnlockAtOnce();
        }
    }

    /// <summary>
    /// Applies a theme choice where one counts. <c>System</c> paints in the operating system's light or
    /// dark setting, and keeps doing so as it changes.
    /// </summary>
    /// <remarks>
    /// System is the platform's current variant set explicitly, not <see cref="ThemeVariant.Default"/>:
    /// once Light or Dark had been requested, Avalonia's Default left the app with no variant at all,
    /// painting neither palette, until the platform next changed.
    /// </remarks>
    internal void ApplyTheme(Core.Settings.AppTheme theme)
    {
        _theme = theme;
        RequestedThemeVariant = ChosenVariant();
    }

    /// <summary>Whether the system reports light or dark, when it wins over <c>app.toml</c>'s theme, which is then kept unread. macOS and Windows always do; a Linux desktop says, once <see cref="SystemAppearance"/> has asked it.</summary>
    internal bool SystemReportsLightOrDark
    {
        get;
        set
        {
            field = value;
            RequestedThemeVariant = ChosenVariant();
        }
    } = !OperatingSystem.IsLinux();

    /// <summary>The system says light or dark wherever it reports one, and Settings only where it does not; only the system says high contrast, so a person who needs it keeps it under either.</summary>
    private ThemeVariant ChosenVariant()
    {
        var dark = (SystemReportsLightOrDark ? Core.Settings.AppTheme.System : _theme) switch
        {
            Core.Settings.AppTheme.Light => false,
            Core.Settings.AppTheme.Dark => true,
            _ => _platformTheme == PlatformThemeVariant.Dark,
        };

        return _platformContrast == ColorContrastPreference.High
            ? (dark ? HighContrast.Dark : HighContrast.Light)
            : (dark ? ThemeVariant.Dark : ThemeVariant.Light);
    }

    /// <summary>Whether scroll bars hide until scrolled. The person may have changed the system's setting while away, so a window coming forward reads it again.</summary>
    internal bool ScrollBarsAutoHide
    {
        set => Resources[_scrollBarsAutoHide] = value;
    }

    private async Task FollowScrollBarsAsync()
    {
        var autoHide = await SystemScrollBars.AutoHideAsync(Tmds.DBus.Protocol.DBusAddress.Session).ConfigureAwait(false);
        UiThread.Post(() => ScrollBarsAutoHide = autoHide);
    }

    private async Task AskTheDesktopAboutLightOrDarkAsync()
    {
        var reports = await SystemAppearance.LinuxReportsLightOrDarkAsync(Tmds.DBus.Protocol.DBusAddress.Session).ConfigureAwait(false);
        UiThread.Post(() => SystemReportsLightOrDark = reports);
    }

    /// <summary>Tracks the operating system's light or dark setting, followed unless Settings' choice counts, and its contrast preference and accent, always followed.</summary>
    private void FollowPlatformTheme()
    {
        ApplyAccent(PlatformSettings?.GetColorValues().AccentColor1);

        if (PlatformSettings is not { } settings)
        {
            return;
        }

        // What Avalonia has already resolved from the platform, when it has; its settings' own answer otherwise.
        _platformTheme = ActualThemeVariant == ThemeVariant.Dark ? PlatformThemeVariant.Dark
            : ActualThemeVariant == ThemeVariant.Light ? PlatformThemeVariant.Light
            : settings.GetColorValues().ThemeVariant;
        _platformContrast = settings.GetColorValues().ContrastPreference;
        settings.ColorValuesChanged += (_, values) =>
        {
            _platformTheme = values.ThemeVariant;
            _platformContrast = values.ContrastPreference;
            ApplyAccent(values.AccentColor1);
            RequestedThemeVariant = ChosenVariant();
        };

        if (_platformContrast == ColorContrastPreference.High)
        {
            RequestedThemeVariant = ChosenVariant();
        }
    }

    /// <summary>Sets every accent resource, in each palette, from the system's accent, or system blue where it reports none.</summary>
    internal void ApplyAccent(Color? system)
    {
        foreach (var (variant, accent) in AccentPalette.Resources(system, Resources))
        {
            if (Resources.ThemeDictionaries.TryGetValue(variant, out var held) && held is ResourceDictionary current)
            {
                foreach (var (key, value) in accent)
                {
                    current[key] = value;
                }
            }
            else
            {
                Resources.ThemeDictionaries[variant] = accent;
            }
        }
    }

    /// <summary>Drops the vault and the unlock screen's password buffer.</summary>
    /// <remarks>
    /// <see cref="Application"/> is not disposable, so this is called from the desktop lifetime's
    /// shutdown rather than by the framework. CA1001 is an error in this repository and it is right
    /// to be: the two fields below are the vault and the master-password buffer.
    /// </remarks>
    public void Dispose()
    {
        InterfaceZoom.Changed -= KeepWindowLayout;
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
        _searchShortcut?.Dispose();
        _searchShortcut = null;

        if (_activatable is not null)
        {
            _activatable.Activated -= OnActivated;
            _activatable = null;
        }

        StopAnswering();

        // An app ending after a crash keeps its hook until the process ends, so nothing further reaches the runtime.
        if (Crashes is not null && !_crashed)
        {
            Dispatcher.UIThread.UnhandledException -= OnUnhandledException;
        }

        if (_preferences is not null)
        {
            _preferences.Changed -= OnPreferencesChanged;
        }

        _vaults?.Dispose();
        _vaults = null;
        _authority = null;
        _notifications?.Dispose();
        _notifications = null;
        _session = null;
    }
}
