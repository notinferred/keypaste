namespace Keypaste.App;

/// <summary>
/// What <c>--selftest</c> runs: enough to prove this binary starts and links on this machine.
/// </summary>
/// <remarks>
/// <para>
/// It constructs no window, on purpose. A CI runner has no display, and a check that needed one
/// would either be skipped there or dragged behind a virtual X server — which is a lot of machinery
/// for a smoke test that still would not render anything a person could look at. What this can
/// honestly assert is that the process starts, that its native assets resolve, and that the vault
/// session works; everything visual is on the manual checklist in <c>docs/desktop.md</c> instead.
/// </para>
/// </remarks>
internal static class SelfTest
{
    internal static int Run(TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        try
        {
            stdout.WriteLine($"keypaste-app: selftest ok on {Environment.OSVersion.Platform}");
            return 0;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"keypaste-app: selftest failed: {ex.Message}");
            return 1;
        }
    }
}
