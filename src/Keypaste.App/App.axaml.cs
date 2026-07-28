using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
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
    private MainWindow? _window;
    private UnlockViewModel? _unlock;
    private ShellViewModel? _shell;
    private DateTimeOffset _lastTouch = DateTimeOffset.MinValue;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var home = Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable);

            _session = new AppVaultSession(TimeProvider.System, AppVaultSession.DefaultIdleTimeout);
            _session.Locked += OnLocked;

            _window = new MainWindow();
            Observe(_window);
            ShowUnlock(home);

            desktop.MainWindow = _window;
            desktop.ShutdownRequested += (_, _) => Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Watches the window for signs of a person, and asks the session to re-check on activation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Tunnelling, at the top level, with <c>handledEventsToo</c>, so no control can swallow the
    /// signal by handling its own input first.
    /// </para>
    /// <para>
    /// <b>Window activation is not activity.</b> It re-checks the deadline instead.
    /// Under focus-follows-mouse a stray pointer pass would otherwise hold the vault open forever,
    /// and the case that matters — waking a machine that slept through the timeout — is one the
    /// timer cannot see, because timers run on a monotonic clock that slept too.
    /// </para>
    /// <para>
    /// Pointer movement is throttled: it fires at pointer-poll rate, and <c>Touch()</c> is cheap but
    /// not free. Everything else calls straight through.
    /// </para>
    /// </remarks>
    private void Observe(Window window)
    {
        window.AddHandler(InputElement.KeyDownEvent, OnActivity, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.TextInputEvent, OnActivity, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerPressedEvent, OnActivity, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerWheelChangedEvent, OnActivity, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);

        window.AddHandler(InputElement.KeyDownEvent, OnShortcut, RoutingStrategies.Tunnel, handledEventsToo: true);

        window.Activated += (_, _) => _session?.Reevaluate();
    }

    private void OnActivity(object? sender, RoutedEventArgs e)
    {
        _session?.Touch();
        _shell?.ClearCountdown();
    }

    private void OnPointerMoved(object? sender, RoutedEventArgs e)
    {
        var now = DateTimeOffset.UtcNow;

        if (now - _lastTouch < TimeSpan.FromSeconds(5))
        {
            return;
        }

        _lastTouch = now;
        _session?.Touch();
    }

    private void OnLocked(object? sender, VaultLockReason reason)
    {
        // Every view that could have held vault-derived data leaves the tree rather than being
        // hidden, so "locked" has exactly one meaning.
        Dispatcher.UIThread.Post(() =>
            ShowUnlock(Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable)));
    }

    private void ShowUnlock(string? home)
    {
        if (_session is null || _window is null)
        {
            return;
        }

        // The shell leaves the tree rather than being hidden, and its view models are disposed, so
        // nothing derived from an open vault can outlive the lock.
        _shell?.Dispose();
        _shell = null;

        _unlock?.Dispose();
        _unlock = new UnlockViewModel(_session, home, OnUnlocked);

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
            Environment.GetEnvironmentVariable(ApproverEndpoint.EnvironmentVariable),
            ApplyTheme);

        _shell.Current = Navigation.Destinations.All[0];

        _window.FindControl<ContentControl>("Root")!.Content =
            new ShellView { DataContext = _shell };
    }

    /// <summary>
    /// The chords, built from the platform's own command modifier rather than a hardcoded Ctrl.
    /// </summary>
    /// <remarks>
    /// Handled at the window, on the tunnelling pass, so a focused list or text field cannot eat
    /// them first. <c>Ctrl/Cmd+L</c> is the honest counterweight to a five-minute idle timeout:
    /// a default that short is only defensible when locking now is one keystroke.
    /// </remarks>
    private void OnShortcut(object? sender, KeyEventArgs e)
    {
        if (_shell is null || _window is null)
        {
            return;
        }

        // Cmd on macOS, Ctrl everywhere else — which is what a platform hotkey configuration
        // resolves to, without depending on where Avalonia keeps that configuration this version.
        var command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

        if ((e.KeyModifiers & command) != command)
        {
            return;
        }

        if (e.Key == Key.L)
        {
            _session?.Lock(VaultLockReason.Manual);
            e.Handled = true;
            return;
        }

        var digit = e.Key switch
        {
            Key.D1 or Key.NumPad1 => 1,
            Key.D2 or Key.NumPad2 => 2,
            Key.D3 or Key.NumPad3 => 3,
            Key.D4 or Key.NumPad4 => 4,
            Key.D5 or Key.NumPad5 => 5,
            _ => 0,
        };

        if (digit > 0 && _shell.GoTo(digit))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Applies a theme choice. <c>System</c> hands the decision back to the operating system.
    /// </summary>
    private void ApplyTheme(Core.Settings.AppTheme theme) =>
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
        _shell?.Dispose();
        _shell = null;
        _unlock?.Dispose();
        _unlock = null;
        _session?.Dispose();
        _session = null;
    }
}
