using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Settings;
using DesktopApp = Keypaste.App.App;

namespace Keypaste.MinimizeObserver;

/// <summary>
/// Runs the desktop app's own composition on a real windowing backend so that
/// <c>scripts/verify-clipboard-markers.sh</c> can read the platform clipboard after a secret copy, its
/// clear and a plain copy (N.12). Exit 3 means the app could not be arranged.
/// </summary>
internal static class Program
{
    private const string _password = "minimize-observer-disposable";
    private const string _marker = "minimize-observer-marker";
    private const int _longIdleSeconds = 8 * 60 * 60;
    private const string _plainCopy = "keypaste run billing -- npm start";

    private static readonly Stopwatch _clock = Stopwatch.StartNew();
    private static readonly Lock _output = new();

    [STAThread]
    private static int Main(string[] args)
    {
        Emit(new { @event = "start", ms = Ms });

        if (!Options.TryParse(args, out var options))
        {
            Console.Error.WriteLine(
                "usage: --scenario markers --quit-file <path> [--deadline-seconds <n>]   (with KEYPASTE_HOME set)");
            return 2;
        }

        var home = Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable);

        if (string.IsNullOrEmpty(home))
        {
            return Refuse("KEYPASTE_HOME is unset, and the observer never touches a real home");
        }

        if (!AppSettings.Save(KeypasteHome.SettingsPath(home), AppSettings.Default with { IdleTimeoutSeconds = _longIdleSeconds }))
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

    private sealed record Options(string QuitFile, int DeadlineSeconds)
    {
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

            options = new Options(quit ?? string.Empty, deadline);
            return args.Length % 2 == 0 && quit is not null && scenario == "markers";
        }
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
            var shell = await UnlockAsync(window, session).ConfigureAwait(true);

            if (shell is null)
            {
                return;
            }

            await shell.Clipboard.CopyAsync(_marker, "observer").ConfigureAwait(true);
            await shell.Clipboard.SettledAsync().ConfigureAwait(true);

            Emit(new
            {
                @event = "ready",
                os = RuntimeInformation.OSDescription,
                session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
                display = Environment.GetEnvironmentVariable("DISPLAY"),
                clipboard = await MarkerAsync().ConfigureAwait(true),
                ms = Ms,
            });

            _ = MarkersAsync(shell);
        }

        /// <summary>
        /// Reports the secret's clear, then copies a run command plainly once the script has read the
        /// cleared clipboard, which it says by writing the quit file's <c>.plain</c> sibling.
        /// </summary>
        private async Task MarkersAsync(ShellViewModel shell)
        {
            while (shell.Clipboard.IsCounting)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(true);
            }

            Emit(new { @event = "cleared", failure = shell.Clipboard.Failure, ms = Ms });

            while (!File.Exists(options.QuitFile + ".plain"))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(true);
            }

            await shell.Clipboard.CopyPlainAsync(_plainCopy, "run command").ConfigureAwait(true);
            await shell.Clipboard.SettledAsync().ConfigureAwait(true);
            Emit(new { @event = "plain", ms = Ms });
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
}
