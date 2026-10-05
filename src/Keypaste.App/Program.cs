using Avalonia;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Login;
using Keypaste.Core.Ownership;

namespace Keypaste.App;

/// <summary>
/// The desktop app's entry point.
/// </summary>
/// <remarks>
/// <para>
/// <b>This process owns the vault it unlocks</b> and serves it to <c>keypaste mcp</c> on that
/// vault's endpoint while it is unlocked; a <c>keypaste agent</c> on the same vault is refused by
/// name (D-0309).
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>The flag that creates a vault through the core, reads it back, and exits without a window.</summary>
    /// <remarks>
    /// A CI runner has no display, so what a three-OS job can honestly assert about a published
    /// binary is that it starts, that its own code runs there, and that the KDBX path it wraps
    /// works on that operating system. That is what this does; it is not a substitute for the
    /// manual checklist in <c>docs/desktop.md</c>, and the workflow comment says so.
    /// </remarks>
    internal const string SelfTestFlag = "--selftest";

    /// <summary>The flag that prints the version and exits, so a release can be checked against its tag.</summary>
    internal const string VersionFlag = "--version";

    /// <summary>How long a second start waits for the running app to answer, since it may still be starting.</summary>
    internal static readonly TimeSpan ReopenWait = TimeSpan.FromSeconds(10);

    [STAThread]
    private static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (TryRunHeadless(args, Console.Out, Console.Error, out var exitCode))
        {
            return exitCode;
        }

        var home = KeypasteHome.Resolve(Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable));

        return Run(
            args,
            home,
            claim => Configured(() => new App { Claim = claim }).StartWithClassicDesktopLifetime(args),
            Console.Error,
            ReopenWait);
    }

    /// <summary>
    /// Runs the app as the one process for this user and home, or hands a start to the process that already is (D-0397).
    /// </summary>
    /// <param name="args">The process's arguments, where <c>--background</c> marks a start at login.</param>
    /// <param name="home">keypaste's home.</param>
    /// <param name="start">Runs the app under its claim and returns its exit code.</param>
    /// <param name="stderr">Where a start the running app never answered says so.</param>
    /// <param name="wait">How long to wait for the running app to answer.</param>
    /// <returns>The app's exit code, 0 when the running app showed its window, or 1 when it never answered.</returns>
    internal static int Run(string[] args, string home, Func<AppClaim, int> start, TextWriter stderr, TimeSpan wait)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(stderr);

        AppClaim? claim = null;

        try
        {
            if (!AppClaim.TryAcquire(home, out claim))
            {
                if (LoginItems.StartsInBackground(args))
                {
                    return 0;
                }

                if (OperatingSystem.IsWindows())
                {
                    WindowsForeground.LetAnyProcessTakeIt();
                }

                if (AppClaim.AskToShow(home, wait, out claim))
                {
                    return 0;
                }

                if (claim is null)
                {
                    stderr.WriteLine("keypaste is already running and did not answer; open it from its menu bar or tray icon.");
                    return 1;
                }
            }

            return start(claim);
        }
        finally
        {
            claim?.Dispose();
        }
    }

    /// <summary>
    /// Handles the flags that must never open a window, so a runner with no display can run them.
    /// </summary>
    /// <returns><see langword="true"/> if a flag was handled and the app should exit with <paramref name="exitCode"/>.</returns>
    internal static bool TryRunHeadless(string[] args, TextWriter stdout, TextWriter stderr, out int exitCode)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        if (Array.Exists(args, a => string.Equals(a, VersionFlag, StringComparison.Ordinal)))
        {
            stdout.WriteLine(CoreInfo.Version);
            exitCode = 0;
            return true;
        }

        if (Array.Exists(args, a => string.Equals(a, SelfTestFlag, StringComparison.Ordinal)))
        {
            exitCode = SelfTest.Run(stdout, stderr);
            return true;
        }

        exitCode = 0;
        return false;
    }

    /// <summary>The Avalonia configuration, also used by the previewer and by headless tests.</summary>
    internal static AppBuilder BuildAvaloniaApp() => Configured(() => new App());

    private static AppBuilder Configured(Func<App> app) =>
        AppBuilder.Configure(app)
            .UsePlatformDetect()
            .WithInterFont();
}
