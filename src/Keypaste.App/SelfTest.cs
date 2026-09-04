namespace Keypaste.App;

/// <summary>
/// What <c>--selftest</c> runs: enough to prove this binary starts and its own code runs here.
/// </summary>
/// <remarks>
/// <para>
/// It constructs no window, on purpose. A CI runner has no display, and a check that needed one
/// would either be skipped there or dragged behind a virtual X server — which is a lot of machinery
/// for a smoke test that still would not render anything a person could look at. What this can
/// honestly assert is narrower than it used to claim: that the published binary starts, that its own
/// code runs on this operating system, and that it exits 0. It does NOT prove the Skia and HarfBuzz
/// natives link — those load lazily on first render, and the package job asserts only that the files
/// are present beside the binary. It does not open a vault either. Step 4.7 owns making this earn
/// its place before the app ships, because `--selftest` on every artifact is that step's gate;
/// everything visual is on the manual checklist in <c>docs/desktop.md</c> instead.
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
