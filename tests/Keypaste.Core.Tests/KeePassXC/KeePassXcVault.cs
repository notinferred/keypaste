using System.Xml.Linq;
using Xunit;

namespace Keypaste.Core.Tests.KeePassXC;

/// <summary>A vault in a directory of its own that keypaste writes and KeePassXC reads, deleted when the test ends.</summary>
internal sealed class KeePassXcVault : IDisposable
{
    private static ReadOnlySpan<byte> Kdbx4Signature => [0x03, 0xd9, 0xa2, 0x9a, 0x67, 0xfb, 0x4b, 0xb5];

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-keepassxc-").FullName;

    internal KeePassXcVault() => Path_ = Path.Combine(_directory, "vault.kdbx");

    internal string Path_ { get; }

    internal string Directory_ => _directory;

    internal Vault Create() => Vault.Create(Path_, KeePassXcCli.Password);

    internal Vault Open() => Vault.Open(Path_, KeePassXcCli.Password);

    /// <summary>The file is still KDBX 4 and KeePassXC opens it with Argon2 and AES-256, which a silent downgrade to KDBX 3.1 would lose.</summary>
    internal void AssertKdbx4()
    {
        var header = File.ReadAllBytes(Path_).AsSpan(0, 12);
        Assert.True(header[..8].SequenceEqual(Kdbx4Signature), "the file is not KDBX");
        Assert.Equal(4, header[10]);

        var info = KeePassXcCli.Run("db-info", Path_);
        Assert.Matches(@"(?im)^\s*KDF:\s*Argon2", info);
        Assert.Matches(@"(?im)^\s*Cipher:\s*AES[-\s]*256", info);
    }

    /// <summary>KeePassXC's export, without any entry's <c>History</c>, so a value an edit dropped is not found in a revision.</summary>
    internal XDocument Current()
    {
        var export = KeePassXcCli.Export(Path_);
        export.Descendants("History").Remove();
        return export;
    }

    /// <summary>The entry KeePassXC exports with this title, outside history.</summary>
    internal static XElement Entry(XDocument export, string title) =>
        export.Descendants("Entry").Single(entry => StringValue(entry, "Title") == title);

    /// <summary>The value of the string <paramref name="key"/> on an exported entry, or null.</summary>
    internal static string? StringValue(XElement entry, string key) =>
        entry.Elements("String").FirstOrDefault(s => (string?)s.Element("Key") == key)?.Element("Value")?.Value;

    /// <summary>Whether KeePassXC holds the string <paramref name="key"/> on an exported entry protected in memory.</summary>
    internal static bool IsProtected(XElement entry, string key) =>
        (string?)entry.Elements("String").Single(s => (string?)s.Element("Key") == key).Element("Value")!.Attribute("ProtectInMemory") == "True";

    public void Dispose()
    {
        // KeePassXC makes the key files it creates read-only, which Windows refuses to delete.
        foreach (var path in Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }

        Directory.Delete(_directory, recursive: true);
    }
}
