using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Keypaste.App.Clipboard;
using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Settings;
using DesktopApp = Keypaste.App.App;

namespace Keypaste.MinimizeObserver;

/// <summary>
/// Runs the desktop app's own composition on a real windowing backend and reports what minimizing it did.
/// </summary>
/// <remarks>
/// It never changes the window's state itself: <c>scripts/observe-minimize-lock.sh</c> minimizes it from
/// outside the process and decides what the events mean. Exit 3 means the app could not be arranged.
/// </remarks>
internal static class Program
{
    private const string _password = "minimize-observer-disposable";
    private const string _marker = "minimize-observer-marker";
    private const int _longIdleSeconds = 8 * 60 * 60;

    private static readonly Stopwatch _clock = Stopwatch.StartNew();
    private static readonly Lock _output = new();

    [STAThread]
    private static int Main(string[] args)
    {
        Emit(new { @event = "start", ms = Ms });

        if (!Options.TryParse(args, out var options))
        {
            Console.Error.WriteLine(
                "usage: --scenario enabled|control|hide|disabled|persist-write|persist-read --quit-file <path> [--deadline-seconds <n>]   (with KEYPASTE_HOME set)");
            return 2;
        }

        var home = Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable);

        if (string.IsNullOrEmpty(home))
        {
            return Refuse("KEYPASTE_HOME is unset, and the observer never touches a real home");
        }

        if (options.Settings is { } settings && !AppSettings.Save(KeypasteHome.SettingsPath(home), settings))
        {
            return Refuse("app.toml could not be written");
        }

        var observer = new Observer(options, Path.Combine(home, "observer.kdbx"));
        var code = Keypaste.App.Program.BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime([], desktop => desktop.Startup += (_, _) =>
                Dispatcher.UIThread.Post(() => _ = observer.RunAsync(desktop)));

        code = observer.Refusal is null ? code : 3;
        Emit(new { @event = "exit", code });
        return code;
    }

    internal static void Emit(object line)
    {
        var json = JsonSerializer.Serialize(line);

        lock (_output)
        {
            Console.Out.WriteLine(json);
            Console.Out.Flush();
        }
    }

    internal static long Ms => _clock.ElapsedMilliseconds;

    private static int Refuse(string why)
    {
        Emit(new { @event = "refused", why });
        return 3;
    }

    private sealed record Options(string Scenario, string QuitFile, int DeadlineSeconds)
    {
        internal bool Unwired => Scenario == "control";

        internal AppSettings? Settings => Scenario switch
        {
            "enabled" or "control" or "hide" => Written(_longIdleSeconds, lockWhenMinimized: true),
            "disabled" => Written(60, lockWhenMinimized: false),
            "persist-write" => Written(_longIdleSeconds, lockWhenMinimized: false),
            _ => null,
        };

        internal static bool TryParse(string[] args, out Options options)
        {
            string? scenario = null;
            string? quit = null;
            var deadline = 180;

            for (var i = 0; i + 1 < args.Length; i += 2)
            {
                switch (args[i])
                {
                    case "--scenario": scenario = args[i + 1]; break;
                    case "--quit-file": quit = args[i + 1]; break;
                    case "--deadline-seconds" when int.TryParse(args[i + 1], out var seconds) && seconds > 0: deadline = seconds; break;
                    default: options = null!; return false;
                }
            }

            options = new Options(scenario ?? string.Empty, quit ?? string.Empty, deadline);
            return args.Length % 2 == 0
                && quit is not null
                && scenario is "enabled" or "control" or "hide" or "disabled" or "persist-write" or "persist-read";
        }

        private static AppSettings Written(int idleSeconds, bool lockWhenMinimized) =>
            AppSettings.Default with { IdleTimeoutSeconds = idleSeconds, LockWhenMinimized = lockWhenMinimized };
    }

    private sealed class Observer(Options options, string vaultPath)
    {
        private IClassicDesktopStyleApplicationLifetime? _desktop;
        private MainWindow? _window;
        private bool _stopping;

        internal string? Refusal { get; private set; }

        internal async Task RunAsync(IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;

            try
            {
                await ArrangeAsync(desktop).ConfigureAwait(true);
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or VaultException)
            {
                Stop(e.Message);
                return;
            }

            if (Refusal is not null)
            {
                return;
            }

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (_, _) =>
            {
                if (File.Exists(options.QuitFile))
                {
                    timer.Stop();
                    Quit("quit");
                }
                else if (Ms > options.DeadlineSeconds * 1000L)
                {
                    timer.Stop();
                    Quit("deadline");
                }
            };
            timer.Start();
        }

        private async Task ArrangeAsync(IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Application.Current is not DesktopApp app || desktop.MainWindow is not MainWindow window)
            {
                Stop("the app did not compose its main window");
                return;
            }

            _window = window;
            var session = Field<AppVaultSession>(app, "_session");
            var activity = new Activity(session);
            var nonce = $"minimize-observer-{Guid.NewGuid():n}";
            window.Title = nonce;

            window.PropertyChanged += (_, e) =>
            {
                if (e.Property == Window.WindowStateProperty)
                {
                    Emit(new { @event = "windowState", value = e.NewValue?.ToString(), ms = Ms });
                    activity.Mark(e.NewValue is WindowState.Minimized ? "minimize" : "restore");
                }
            };

            activity.Watch(window);

            session.Locked += (_, reason) =>
            {
                Emit(new { @event = "locked", reason = reason.ToString(), ms = Ms });
                activity.Mark("lock");
                Dispatcher.UIThread.Post(() => _ = ReportClipboardAsync(), DispatcherPriority.Background);
            };

            if (options.Unwired)
            {
                Field<IDisposable>(app, "_minimize").Dispose();
                Emit(new { @event = "unwired", ms = Ms });
            }

            var shell = await UnlockAsync(window, session).ConfigureAwait(true);

            if (shell is null)
            {
                return;
            }

            activity.After("unlock");
            activity.Mark("unlock");
            activity.Poll();

            if (options.Scenario == "persist-write")
            {
                shell.GoTo(Destinations.All.Single(d => d.Kind == DestinationKind.Settings).Shortcut);

                if (shell.Content is not SettingsViewModel settings)
                {
                    Stop("Settings did not open");
                    return;
                }

                settings.LockWhenMinimized = true;
                Emit(new { @event = "settings", lockWhenMinimized = shell.Preferences.LockWhenMinimized, ms = Ms });
                Quit("persisted");
                return;
            }

            await shell.Clipboard.CopyAsync(_marker, "observer").ConfigureAwait(true);
            await shell.Clipboard.SettledAsync().ConfigureAwait(true);

            Emit(new
            {
                @event = "ready",
                pid = Environment.ProcessId,
                title = nonce,
                scenario = options.Scenario,
                os = RuntimeInformation.OSDescription,
                session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
                display = Environment.GetEnvironmentVariable("DISPLAY"),
                clipboard = await MarkerAsync().ConfigureAwait(true),
                idleSeconds = shell.Preferences.Current.IdleTimeoutSeconds,
                lockWhenMinimized = shell.Preferences.LockWhenMinimized,
                ms = Ms,
            });
        }

        private async Task<ShellViewModel?> UnlockAsync(MainWindow window, AppVaultSession session)
        {
            using (var vault = Vault.Create(vaultPath, _password))
            {
                vault.AddEntry(new VaultEntry { Title = "observer", Password = _marker });
                vault.Save();
            }

            var root = window.FindControl<ContentControl>("Root");

            if (root?.Content is not UnlockView { DataContext: UnlockViewModel unlock } || !unlock.Offer(vaultPath))
            {
                Stop("the unlock screen did not take the vault");
                return null;
            }

            foreach (var c in _password)
            {
                unlock.Type(c);
            }

            await unlock.UnlockAsync().ConfigureAwait(true);

            if (!session.IsUnlocked || root.Content is not ShellView { DataContext: ShellViewModel shell })
            {
                Stop("the unlock screen did not open the vault");
                return null;
            }

            Emit(new { @event = "unlocked", ms = Ms });
            return shell;
        }

        private async Task ReportClipboardAsync()
        {
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(true);
            Emit(new { @event = "clipboard", marker = await MarkerAsync().ConfigureAwait(true), ms = Ms });
        }

        private async Task<string> MarkerAsync()
        {
            if (_window is null)
            {
                return "unreadable";
            }

            var hash = await new AvaloniaClipboard(_window).TryReadHashAsync().ConfigureAwait(true);

            return hash is null ? "absent"
                : hash.AsSpan().SequenceEqual(SHA256.HashData(Encoding.UTF8.GetBytes(_marker))) ? "present"
                : "absent";
        }

        private void Quit(string why)
        {
            if (_stopping)
            {
                return;
            }

            _stopping = true;
            Emit(new { @event = "stopping", why, ms = Ms });

            if (_desktop is not null && !_desktop.TryShutdown())
            {
                Dispatcher.UIThread.Post(() => _desktop.Shutdown(), DispatcherPriority.Background);
            }
        }

        private void Stop(string why)
        {
            Refusal = why;
            Emit(new { @event = "refused", why, ms = Ms });
            _stopping = true;
            _desktop?.Shutdown(3);
        }

        private static T Field<T>(DesktopApp app, string name) =>
            typeof(DesktopApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(app) is T value
                ? value
                : throw new InvalidOperationException($"App.{name} is not a {typeof(T).Name}; the observer no longer matches the app");
    }

    /// <summary>Reports each change to the session's idle clock, and the input event that made it.</summary>
    private sealed class Activity(AppVaultSession session)
    {
        private readonly FieldInfo _wall = SessionField("_activityWall");
        private readonly FieldInfo _stamp = SessionField("_activityStamp");
        private readonly FieldInfo _timeout = SessionField("_idleTimeout");
        private long _seen;

        internal void Watch(Window window)
        {
            window.AddHandler(InputElement.KeyDownEvent, (_, e) => AfterDispatch($"KeyDown:{e.Key}", null), RoutingStrategies.Tunnel, handledEventsToo: true);
            window.AddHandler(InputElement.TextInputEvent, (_, _) => AfterDispatch("TextInput", null), RoutingStrategies.Tunnel, handledEventsToo: true);
            window.AddHandler(InputElement.PointerPressedEvent, (_, e) => AfterDispatch("PointerPressed", e.GetPosition(window)), RoutingStrategies.Tunnel, handledEventsToo: true);
            window.AddHandler(InputElement.PointerWheelChangedEvent, (_, e) => AfterDispatch("PointerWheelChanged", e.GetPosition(window)), RoutingStrategies.Tunnel, handledEventsToo: true);
            window.AddHandler(InputElement.PointerMovedEvent, (_, e) => AfterDispatch("PointerMoved", e.GetPosition(window)), RoutingStrategies.Tunnel, handledEventsToo: true);
            window.Activated += (_, _) => Emit(new { @event = "activated", ms = Ms, utc = Utc });
            window.Deactivated += (_, _) => Emit(new { @event = "deactivated", ms = Ms, utc = Utc });
        }

        // Checked once the event has finished routing, so App.Observe's handler has run whatever order the route took.
        private void AfterDispatch(string source, Point? position) =>
            Dispatcher.UIThread.Post(() => After(source, position), DispatcherPriority.Send);

        private static long Utc => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        internal void Poll()
        {
            var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            poll.Tick += (_, _) => After("unattributed");
            poll.Start();
        }

        internal void After(string source, Point? position = null)
        {
            var stamp = (long)_stamp.GetValue(session)!;

            if (stamp == Interlocked.Exchange(ref _seen, stamp))
            {
                return;
            }

            Emit(new { @event = "touch", source, x = position?.X, y = position?.Y, deadlineMs = DeadlineMs(), ms = Ms, utc = Utc });
        }

        internal void Mark(string at) => Emit(new { @event = "deadline", at, deadlineMs = DeadlineMs(), ms = Ms });

        /// <summary>When the session will lock, on this process's clock, by the same larger-of-two-clocks rule it uses.</summary>
        private long DeadlineMs()
        {
            var wall = DateTimeOffset.UtcNow - (DateTimeOffset)_wall.GetValue(session)!;
            var monotonic = Stopwatch.GetElapsedTime((long)_stamp.GetValue(session)!);
            var idle = wall > monotonic ? wall : monotonic;
            return Ms + (long)((TimeSpan)_timeout.GetValue(session)! - idle).TotalMilliseconds;
        }

        private static FieldInfo SessionField(string name) =>
            typeof(AppVaultSession).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"AppVaultSession.{name} is missing; the observer no longer matches the app");
    }
}
