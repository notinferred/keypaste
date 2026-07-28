using Keypaste.Core;

namespace Keypaste.App.Tests;

/// <summary>
/// A real KDBX file in a temporary directory, deleted when the test finishes.
/// </summary>
/// <remarks>
/// A real vault rather than a fake, because what these tests assert is that Argon2 either accepted
/// a password or did not, and a fake that returns <c>true</c> would prove nothing about the one
/// path where being wrong matters.
/// </remarks>
internal sealed class TempVault : IDisposable
{
    internal const string Password = "correct-horse-battery-staple";

    private readonly string _directory;

    internal TempVault()
    {
        _directory = Path.Combine(Path.GetTempPath(), "keypaste-app-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_directory);

        Path_ = Path.Combine(_directory, "test.kdbx");

        using var vault = Vault.Create(Path_, Password);
        vault.AddEntry(new VaultEntry { Title = "example", Password = "entry-secret" });
        vault.Save();
    }

    /// <summary>The vault file.</summary>
    internal string Path_ { get; }

    /// <summary>A path inside the same directory that holds no file at all.</summary>
    internal string MissingPath => Path.Combine(_directory, "absent.kdbx");

    /// <summary>A file with a <c>.kdbx</c> name that was never a vault.</summary>
    internal string ImposterPath
    {
        get
        {
            var path = Path.Combine(_directory, "imposter.kdbx");
            File.WriteAllText(path, "this is not a vault, it is a text file wearing the extension");
            return path;
        }
    }

    /// <summary>A buffer holding <paramref name="text"/>, for handing to the session.</summary>
    internal static SecretBuffer Secret(string text)
    {
        var buffer = new SecretBuffer();
        buffer.Append(text);
        return buffer;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A test that cannot clean up its temporary directory has still made its point.
        }
    }
}
