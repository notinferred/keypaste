using Keypaste.Core;

namespace Keypaste.CompatFixture;

/// <summary>
/// Writes the KDBX fixture that <c>scripts/verify-keepassxc-compat.sh</c> checks against a real
/// KeePassXC (CORE.md law 4.6).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately throwaway. Stage 0.3 replaces it with <c>keypaste init</c> + <c>keypaste add</c>,
/// which is a strictly stronger gate because it exercises the shipped binary. The contract —
/// <c>argv[0]</c> is the output path, <c>KP_COMPAT_PASSWORD</c> is the master password — is what
/// makes that swap a one-line change to the workflow, leaving the script and its expected values
/// untouched.
/// </para>
/// <para>
/// The values written here are duplicated in the verification script on purpose. That
/// duplication is the change detector: expectations generated from the writer under test would
/// agree with it forever and assert nothing.
/// </para>
/// </remarks>
internal static class Program
{
    internal const string PasswordVariable = "KP_COMPAT_PASSWORD";

    internal static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: keypaste-compat-fixture <output.kdbx>");
            return 1;
        }

        var masterPassword = Environment.GetEnvironmentVariable(PasswordVariable);
        if (string.IsNullOrEmpty(masterPassword))
        {
            Console.Error.WriteLine($"{PasswordVariable} is not set.");
            return 1;
        }

        var path = args[0];
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        using (var vault = Vault.Create(path, masterPassword))
        {
            foreach (var entry in FixtureEntries())
            {
                vault.AddEntry(entry);
            }

            vault.Save();
        }

        var header = KdbxHeader.Read(path);
        Console.WriteLine(
            $"wrote {path} (KDBX {header.FormatMajorVersion}.{header.FormatMinorVersion})");

        return 0;
    }

    private static IEnumerable<VaultEntry> FixtureEntries()
    {
        // Plain ASCII, every field populated, with a multi-line Notes value carrying the
        // punctuation most likely to break a shell-based assertion.
        yield return new VaultEntry
        {
            Title = "ascii",
            Username = "compat-user",
            Password = "ascii-only-P@ssw0rd",
            Url = "https://example.invalid/keypaste",
            Notes = "first notes line\nsecond line: , ; = \" ' punctuation",
            GroupPath = "compat",
        };

        // Non-ASCII across several scripts plus an emoji, to prove UTF-8 survives the format.
        yield return new VaultEntry
        {
            Title = "unicode",
            Username = "ünïcode-user",
            Password = "pässwörd-ünïcode",
            Url = "https://example.invalid/ünïcode",
            Notes = "café — 日本語 — 🔑",
            GroupPath = "compat",
        };

        // A nested group, so the gate covers group-path handling and not just the root.
        yield return new VaultEntry
        {
            Title = "deep",
            Username = "deep-user",
            Password = "deep-pass",
            Url = "https://example.invalid/deep",
            Notes = "entry in a nested group",
            GroupPath = "compat/nested",
        };
    }
}
