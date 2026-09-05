using Keypaste.Core;

namespace Keypaste.App;

/// <summary>
/// What <c>--selftest</c> runs: a vault created through <c>Keypaste.Core</c> in a scratch directory,
/// one entry written, the file reopened, and the entry read back.
/// </summary>
/// <remarks>
/// <para>
/// It constructs no window, on purpose. A CI runner has no display, and a check that needed one
/// would either be skipped there or dragged behind a virtual X server. What this asserts instead is
/// the thing a published app binary can break silently: that the vendored KDBX path linked into it
/// works on this operating system, end to end, on disk. It does NOT prove the Skia and HarfBuzz
/// natives link — those load lazily on first render, and the package job asserts only that the
/// files are present beside the binary. Everything visual is on the manual checklist in
/// <c>docs/desktop.md</c>.
/// </para>
/// </remarks>
internal static class SelfTest
{
    private const string _masterPassword = "keypaste-selftest";
    private const string _sentinel = "selftest-value-that-must-round-trip";
    private const string _groupPath = "selftest";
    private const string _title = "entry";

    internal static int Run(TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        DirectoryInfo? scratch = null;

        try
        {
            scratch = Directory.CreateTempSubdirectory("keypaste-selftest-");
            var path = Path.Combine(scratch.FullName, "selftest.kdbx");

            using (var vault = Vault.Create(path, _masterPassword))
            {
                vault.AddEntry(new VaultEntry { Title = _title, GroupPath = _groupPath, Password = _sentinel });
                vault.Save();
            }

            using (var vault = Vault.Open(path, _masterPassword))
            {
                var entry = vault.Find(_groupPath + "/" + _title);

                if (entry is null || !string.Equals(entry.Password, _sentinel, StringComparison.Ordinal))
                {
                    stderr.WriteLine("keypaste-app: selftest failed: the entry did not read back from the vault it was written to");
                    return 1;
                }
            }

            stdout.WriteLine(
                $"keypaste-app: selftest ok on {Environment.OSVersion.Platform}: " +
                "a vault was created through Keypaste.Core, saved, reopened, and an entry read back");
            return 0;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"keypaste-app: selftest failed: {ex.Message}");
            return 1;
        }
        finally
        {
            RemoveScratch(scratch, stderr);
        }
    }

    private static void RemoveScratch(DirectoryInfo? scratch, TextWriter stderr)
    {
        if (scratch is null)
        {
            return;
        }

        try
        {
            scratch.Delete(recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"keypaste-app: selftest left {scratch.FullName} behind: {ex.Message}");
        }
    }
}
