using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
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
/// </remarks>
internal static class AppLaunch
{
    internal static int Run(string home, string[] args) =>
        App.Program.Run(
            args,
            home,
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

        var input = new Thread(() =>
        {
            while (Console.In.ReadLine() is not null)
            {
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
}
