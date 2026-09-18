using Keypaste.Core;

namespace Keypaste.VaultRestorer;

/// <summary>Restores one entry revision in one vault, once, and saves.</summary>
/// <remarks>
/// <para>
/// The history gate's driver, and the reason it is a process. There is no shipped binary a script
/// can tell to restore a revision — and a gate that cannot make KeePassXC look at a restored vault
/// is not checking the half of docs/PRODUCT.md law 4.6 that matters here. Everything the shipped
/// writer can do in that gate, it does; this performs only the step it cannot (DECISIONS.md
/// D-0228).
/// </para>
/// <para>
/// V.2b put a restore on the desktop entry pane, which a bash gate cannot drive, so this stays
/// until a restore verb reaches the CLI (D-0230). The two calls below are the pane's own, which is
/// what carries KeePassXC's verdict onto the button.
/// </para>
/// <para>
/// It prints how many revisions it saw and which one it restored, and never a value: the gate
/// seeded the values itself and asks KeePassXC for them, so nothing is learned by putting a secret
/// on this process's stdout.
/// </para>
/// <para>
/// The master password arrives in the environment rather than in <c>argv</c>, as it does for
/// <c>Keypaste.VaultSaver</c>. It is a fixture value and not a real secret, but a command line is
/// readable by every process on the machine and this repository does not write code that reads as
/// though that were fine.
/// </para>
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 2 || !int.TryParse(args[1], out var index))
        {
            Console.Error.WriteLine(
                "usage: <vault-path> <index>   (index is newest-first; password in KEYPASTE_RESTORER_PASSWORD)");
            return 2;
        }

        var password = Environment.GetEnvironmentVariable("KEYPASTE_RESTORER_PASSWORD");

        if (string.IsNullOrEmpty(password))
        {
            Console.Error.WriteLine("KEYPASTE_RESTORER_PASSWORD is unset");
            return 2;
        }

        var name = TargetEntry(out var entryPath);

        if (name is null)
        {
            Console.Error.WriteLine($"'{entryPath}' does not name an entry as <group>/<title>");
            return 2;
        }

        try
        {
            using var vault = Vault.Open(args[0], password);
            var revisions = vault.ReadHistory(name);

            if (revisions is null)
            {
                Console.Error.WriteLine($"no entry is called '{entryPath}'");
                return 1;
            }

            Console.WriteLine($"revisions     {revisions.Count}");

            foreach (var revision in revisions)
            {
                Console.WriteLine($"  [{revision.Index}]         {revision.ModifiedUtc:O}");
            }

            if (!vault.RestoreRevision(name, index))
            {
                Console.Error.WriteLine($"no entry is called '{entryPath}'");
                return 1;
            }

            vault.Save();
            Console.WriteLine($"restored      [{index}]");
            return 0;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Console.Error.WriteLine($"restore refused: {ex.Message}");
            return 1;
        }
        catch (VaultException ex)
        {
            Console.Error.WriteLine($"restore refused: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"restorer failed: {ex.GetType().Name}: {ex.Message}");
            return 3;
        }
    }

    /// <summary>
    /// The entry to restore, taken from <c>KEYPASTE_RESTORER_ENTRY</c> as a group path and a title.
    /// </summary>
    /// <remarks>
    /// Split on the last separator, which is exactly the lossy join <see cref="Core.EntryName"/>
    /// exists to avoid — acceptable in a fixture driver whose entry is one the gate just created,
    /// and not something to copy into shipped code.
    /// </remarks>
    private static EntryName? TargetEntry(out string entryPath)
    {
        entryPath = Environment.GetEnvironmentVariable("KEYPASTE_RESTORER_ENTRY") ?? string.Empty;
        var separator = entryPath.LastIndexOf('/');

        return separator <= 0 || separator == entryPath.Length - 1
            ? null
            : new EntryName(entryPath[..separator], entryPath[(separator + 1)..]);
    }
}
