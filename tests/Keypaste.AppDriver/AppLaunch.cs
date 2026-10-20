using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using Keypaste.Core.Linking;
using Keypaste.Core.Ownership;

namespace Keypaste.AppDriver;

/// <summary>
/// Starts the app through <c>keypaste-app</c>'s own <c>Program.Run</c>, on a headless display in place of
/// the platform's, so <c>scripts/verify-session-lifecycle.sh</c> can count the app's processes in a home
/// and watch its window (F.38).
/// </summary>
/// <remarks>
/// A start that runs the app prints <c>running as process &lt;pid&gt; on &lt;endpoint&gt;</c>, then
/// <c>window shown</c> or <c>window hidden</c> for the main window as it is and each time that changes, and
/// quits as the app quits once standard input closes, printing <c>shut down</c>. A start the running app
/// took prints nothing.
/// While the app is locked, the notice an agent's request raises prints <c>locked-notice shown</c> with its
/// heading and text, and <c>locked-notice closed</c> when it comes down; the line <c>notice open</c> presses its
/// Open keypaste once armed, by a click the display routes to the button (G.4b).
/// </remarks>
internal static class AppLaunch
{
    private static readonly TimeSpan _armingWait = TimeSpan.FromSeconds(10);

    internal static int Run(string home, string[] args) =>
        App.Program.Run(
            args,
            home,
            CliLinks.ForThisProcess(),
            claim =>
            {
                var code = Headless(claim).StartWithClassicDesktopLifetime(args);
                Console.Out.WriteLine("shut down");
                return code;
            },
            Console.Error,
            App.Program.ReopenWait);

    private static AppBuilder Headless(AppClaim claim) =>
        AppBuilder.Configure(() => new App.App { Claim = claim })
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .AfterSetup(builder => Dispatcher.UIThread.Post(() => Watch((App.App)builder.Instance!)));

    private static void Watch(App.App app)
    {
        var window = app.Main ?? throw new DriverException("launch composed no main window");
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
                if (line is "notice open")
                {
                    OpenNotice(app);
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

    private static string? Text(Window window, string name) => window.FindControl<TextBlock>(name)?.Text;

    private static void OpenNotice(App.App app)
    {
        try
        {
            Dispatcher.UIThread.InvokeAsync(() => PressOpenAsync(app)).GetAwaiter().GetResult();
        }
        catch (DriverException ex)
        {
            Console.Error.WriteLine($"driver could not arrange the act: {ex.Message}");
        }
    }

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
}
