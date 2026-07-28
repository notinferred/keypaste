using Avalonia;

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
    /// <summary>The flag that starts, exercises the session, and exits without a window.</summary>
    /// <remarks>
    /// A CI runner has no display, so the only thing a three-OS job can honestly assert about a
    /// published binary is that it starts, links its native assets and that its session works.
    /// That is what this does; it is not a substitute for the manual checklist in
    /// <c>docs/desktop.md</c>, and the workflow comment says so.
    /// </remarks>
    internal const string SelfTestFlag = "--selftest";

    [STAThread]
    private static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (Array.Exists(args, a => string.Equals(a, SelfTestFlag, StringComparison.Ordinal)))
        {
            return SelfTest.Run(Console.Out, Console.Error);
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>The Avalonia configuration, also used by the previewer and by headless tests.</summary>
    internal static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();
}
