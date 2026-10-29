using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using Keypaste.App;
using Keypaste.App.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Linking;
using Keypaste.Core.Ownership;
using Keypaste.Mvvm;

namespace Keypaste.AppDriver;

/// <summary>
/// Starts the app through <c>keypaste-app</c>'s own <c>Program.Run</c>, crash reports composed as its start composes them,
/// on a headless display in place of the platform's, so <c>scripts/verify-session-lifecycle.sh</c> can count the app's
/// processes in a home and watch its window (F.38) and <c>scripts/verify-crash-report.sh</c> can crash it (4.11c).
/// </summary>
/// <remarks>
/// <para>
/// A start that runs the app prints <c>running as process &lt;pid&gt; on &lt;endpoint&gt;</c>, then
/// <c>window shown</c> or <c>window hidden</c> for the main window as it is and each time that changes, and
/// <c>screen first-run</c>, <c>screen unlock</c> or <c>screen shell</c> for what the window holds and each time that
/// changes; it quits as the app quits once standard input closes, printing <c>shut down with exit code &lt;n&gt;</c>.
/// A start the running app took prints nothing.
/// While the app is locked, the notice an agent's request raises prints <c>locked-notice shown</c> with its
/// heading and text, and <c>locked-notice closed</c> when it comes down; the line <c>notice open</c> presses its
/// Open keypaste once armed, by a click the display routes to the button (G.4b).
/// </para>
/// <para>
/// The lines <c>first-run send</c> and <c>first-run dont-send</c> press the first run's Continue and then that choice;
/// <c>unlock &lt;vault&gt;</c> types <c>KEYPASTE_DRIVER_PASSWORD</c> on the unlock screen and presses Unlock;
/// <c>copy &lt;title&gt;</c> copies that item's password from Items; <c>lock</c> locks; <c>crash &lt;text&gt;</c>
/// throws on the UI thread an exception whose message and inner exception's message hold the text; and
/// <c>unobserved &lt;text&gt;</c> drops a task that failed with that text and collects it, printing <c>collected</c>.
/// Nothing in the shipped app can do the last two. The clipboard prints <c>clipboard set</c> and <c>clipboard cleared</c>
/// as the app writes and clears a secret on it.
/// </para>
/// </remarks>
internal static class AppLaunch
{
    private static readonly TimeSpan _armingWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _screenWait = TimeSpan.FromSeconds(30);

    internal static int Run(string home, string[] args) =>
        App.Program.Run(
            args,
            home,
            CliLinks.ForThisProcess(),
            claim =>
            {
                var preferences = new DesktopPreferences(Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable));
                int code;

                // Ended before the line, so a run marker the run removes is gone once the line says it shut down.
                using (var crashes = App.Program.StartCrashReports(preferences))
                {
                    code = Headless(claim, preferences, crashes).StartWithClassicDesktopLifetime(args);
                }

                Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"shut down with exit code {code}"));
                return code;
            },
            Console.Error,
            App.Program.ReopenWait);

    private static AppBuilder Headless(AppClaim claim, DesktopPreferences preferences, CrashReporting crashes) =>
        AppBuilder.Configure(() => new App.App
        {
            Claim = claim,
            Preferences = preferences,
            Crashes = crashes,
            Clipboards = window => new WatchedClipboard(new AvaloniaClipboard(window)),
        })
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .AfterSetup(builder => Dispatcher.UIThread.Post(() => Watch((App.App)builder.Instance!)));

    private static void Watch(App.App app)
    {
        var window = app.Main ?? throw new DriverException("launch composed no main window");
        var root = window.FindControl<ContentControl>("Root") ?? throw new DriverException("the main window has no root");
        var lifetime = (IClassicDesktopStyleApplicationLifetime)app.ApplicationLifetime!;

        Console.Out.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"running as process {Environment.ProcessId} on {app.Claim?.Endpoint ?? "no endpoint"}"));
        Visibility(window);
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty)
            {
                Visibility(window);
            }
        };

        Screen(root);
        root.PropertyChanged += (_, e) =>
        {
            if (e.Property == ContentControl.ContentProperty)
            {
                Screen(root);
            }
        };

        if (app.LockedNotice is { } locked)
        {
            locked.Shown += (_, notice) =>
            {
                notice.CaptureRenderedFrame();
                Console.Out.WriteLine($"locked-notice shown head={Text(notice, "HeadText")} text={Text(notice, "NoticeText")}");
                notice.Closed += (_, _) => Console.Out.WriteLine("locked-notice closed");
            };
        }

        var input = new Thread(() =>
        {
            while (Console.In.ReadLine() is { } line)
            {
                switch (line.Split(' ', 2))
                {
                    case ["notice", "open"]:
                        Act(() => PressOpenAsync(app));
                        break;
                    case ["first-run", var answer]:
                        Act(() => AnswerFirstRun(root, answer));
                        break;
                    case ["unlock", var vault]:
                        Act(() => UnlockAsync(root, vault));
                        break;
                    case ["copy", var title]:
                        Act(() => CopyAsync(root, title));
                        break;
                    case ["lock"]:
                        Act(() => LockAsync(root));
                        break;
                    case ["crash", var text]:
                        Console.Out.WriteLine("crashing");
                        Dispatcher.UIThread.Post(() => throw new InvalidOperationException(text, new IOException(text)));
                        break;
                    case ["unobserved", var text]:
                        DropFailedTask(text);
                        Collect();
                        Console.Out.WriteLine("collected");
                        break;
                    default:
                        Console.Error.WriteLine($"driver could not arrange the act: no act is called '{line}'");
                        break;
                }
            }

            Dispatcher.UIThread.Post(() => _ = lifetime.TryShutdown());
        })
        {
            IsBackground = true,
        };
        input.Start();
    }

    private static void Visibility(Window window) =>
        Console.Out.WriteLine(window.IsVisible ? "window shown" : "window hidden");

    private static void Screen(ContentControl root) => Console.Out.WriteLine(root.Content switch
    {
        FirstRunView => "screen first-run",
        UnlockView => "screen unlock",
        ShellView => "screen shell",
        _ => "screen none",
    });

    private static string? Text(Window window, string name) => window.FindControl<TextBlock>(name)?.Text;

    // Not inlined, so nothing in the caller keeps the failed task reachable when the collector looks.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DropFailedTask(string text)
    {
        var failed = Task.Run(new Action(() => throw new InvalidOperationException(text)));
        ((IAsyncResult)failed).AsyncWaitHandle.WaitOne();
    }

    // A failed task's exception is reported as unobserved from its finalizer, so collecting runs the runtime's hook.
    private static void Collect()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    // Caught on the UI thread, since what escapes there is the app's crash, which only the crash line is to cause.
    private static void Act(Func<Task> act) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                await act().ConfigureAwait(true);
            }
            catch (DriverException ex)
            {
                Console.Error.WriteLine($"driver could not arrange the act: {ex.Message}");
            }
        }).GetAwaiter().GetResult();

    private static async Task PressOpenAsync(App.App app)
    {
        var notice = app.LockedNotice?.Showing ?? throw new DriverException("no locked notice is on screen");
        var open = notice.FindControl<Button>("Open") ?? throw new DriverException("the locked notice has no Open button");
        var armedBy = DateTime.UtcNow + _armingWait;

        while (!open.IsEffectivelyEnabled)
        {
            if (DateTime.UtcNow > armedBy)
            {
                throw new DriverException("Open keypaste never became pressable");
            }

            await Task.Delay(50).ConfigureAwait(true);
        }

        PromptScreen.Click(notice, open);
    }

    private static Task AnswerFirstRun(ContentControl root, string answer)
    {
        if (root.Content is not FirstRunView { DataContext: FirstRunViewModel first })
        {
            throw new DriverException("the first run's terms are not on screen");
        }

        var choice = answer switch
        {
            "send" => first.SendCommand,
            "dont-send" => first.DontSendCommand,
            _ => throw new DriverException($"the first run offers send and dont-send, not {answer}"),
        };

        Press(first.ContinueCommand, "Continue");
        Press(choice, answer);
        return Task.CompletedTask;
    }

    private static async Task UnlockAsync(ContentControl root, string vault)
    {
        if (root.Content is not UnlockView { DataContext: UnlockViewModel unlock })
        {
            throw new DriverException("the unlock screen is not on screen");
        }

        if (!unlock.Offer(vault))
        {
            throw new DriverException($"the unlock screen did not take the vault: {unlock.Message}");
        }

        foreach (var c in Environment.GetEnvironmentVariable("KEYPASTE_DRIVER_PASSWORD") ?? string.Empty)
        {
            unlock.Type(c);
        }

        if (!unlock.UnlockCommand.CanExecute(null))
        {
            throw new DriverException("the Unlock button is disabled");
        }

        await unlock.UnlockCommand.ExecuteAsync().ConfigureAwait(true);
        await UntilAsync(() => root.Content is ShellView, "the shell never came up after Unlock").ConfigureAwait(true);
    }

    private static async Task CopyAsync(ContentControl root, string title)
    {
        var shell = Shell(root);
        var items = shell.Content as EntriesViewModel ?? throw new DriverException("Items is not the screen showing");
        items.Selected = items.Rows.FirstOrDefault(row => row.Title == title) ?? throw new DriverException($"Items lists no {title}");

        var detail = items.Detail ?? throw new DriverException($"{title}'s pane did not open");

        if (!detail.CopyPasswordCommand.CanExecute(null))
        {
            throw new DriverException($"{title}'s Copy password is disabled");
        }

        await detail.CopyPasswordCommand.ExecuteAsync().ConfigureAwait(true);
    }

    private static async Task LockAsync(ContentControl root)
    {
        await Shell(root).LockCommand.ExecuteAsync().ConfigureAwait(true);
        await UntilAsync(() => root.Content is UnlockView, "the unlock screen never came back after Lock").ConfigureAwait(true);
    }

    private static ShellViewModel Shell(ContentControl root) =>
        root.Content is ShellView { DataContext: ShellViewModel shell } ? shell : throw new DriverException("the shell is not on screen");

    private static async Task UntilAsync(Func<bool> done, string failure)
    {
        var by = DateTime.UtcNow + _screenWait;

        while (!done())
        {
            if (DateTime.UtcNow > by)
            {
                throw new DriverException(failure);
            }

            await Task.Delay(50).ConfigureAwait(true);
        }
    }

    private static void Press(RelayCommand command, string name)
    {
        if (!command.CanExecute(null))
        {
            throw new DriverException($"{name} is disabled");
        }

        command.Execute(null);
    }

    /// <summary>The app's clipboard, saying when a secret lands on it and when it is cleared, and never what it holds.</summary>
    private sealed class WatchedClipboard(IAppClipboard inner) : IAppClipboard
    {
        public async Task<bool> TrySetSecretAsync(string secret)
        {
            var set = await inner.TrySetSecretAsync(secret).ConfigureAwait(true);

            if (set)
            {
                Console.Out.WriteLine("clipboard set");
            }

            return set;
        }

        public Task<bool> TrySetPlainAsync(string text) => inner.TrySetPlainAsync(text);

        public Task<byte[]?> TryReadHashAsync() => inner.TryReadHashAsync();

        public Task<PasteOutcome> TryPasteIntoAsync(SecretBuffer destination) => inner.TryPasteIntoAsync(destination);

        public async Task<bool> TryClearAsync()
        {
            var cleared = await inner.TryClearAsync().ConfigureAwait(true);

            if (cleared)
            {
                Console.Out.WriteLine("clipboard cleared");
            }

            return cleared;
        }
    }
}
