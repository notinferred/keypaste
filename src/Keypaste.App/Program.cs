using Avalonia;
using Keypaste.Core;

namespace Keypaste.App;

/// <summary>
/// The desktop app's entry point.
/// </summary>
/// <remarks>
/// <para>
/// <b>This process is not the approver.</b> It holds an unlocked vault for its own windows and
/// nothing else: it does not bind the approver pipe, so a running <c>keypaste agent</c> keeps
/// answering agent requests exactly as it did before. Binding it here would mean whichever of the
/// two started second failed to bind, and the loser would be a silent loss of the approval path.
/// Stage 4.3 owns that hand-off (DECISIONS.md D-0044).
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

    [STAThread]
    private static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (TryRunHeadless(args, Console.Out, Console.Error, out var exitCode))
        {
            return exitCode;
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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
    internal static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();
}
