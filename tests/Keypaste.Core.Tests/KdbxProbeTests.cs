using Keypaste.Core.Import;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>What a KDBX file's outer header says before anybody types its password.</summary>
public sealed class KdbxProbeTests : IDisposable
{
    private const string _password = "probe-password";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-probe-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Probe_Keypaste4_Argon2d()
    {
        var path = Path.Combine(_directory, "mine.kdbx");
        using (var vault = Vault.Create(path, _password))
        {
            vault.Save();
        }

        Assert.True(KdbxImport.TryProbe(path, out var probe, out var error), error);
        Assert.Equal("KDBX 4.0", probe.Version);
        Assert.Equal("Argon2d", probe.Kdf);
        Assert.Equal("AES-256", probe.Cipher);
        Assert.Equal("mine.kdbx", probe.FileName);
        Assert.Equal(Path.GetFullPath(path), probe.Path);
        Assert.Null(probe.SiblingKeyfile);
    }

    [Fact]
    public void Probe_Argon2id_ChaCha20()
    {
        var path = Path.Combine(_directory, "foreign.kdbx");
        KdbxImportTests.WriteForeign(path, _password, "Argon2id", "ChaCha20");

        Assert.True(KdbxImport.TryProbe(path, out var probe, out var error), error);
        Assert.StartsWith("KDBX 4.", probe.Version, StringComparison.Ordinal);
        Assert.Equal("Argon2id", probe.Kdf);
        Assert.Equal("ChaCha20", probe.Cipher);
    }

    /// <summary>KeePassLib writes only KDBX 4, so the 3.1 header is written here byte for byte: a probe reads nothing after it.</summary>
    [Fact]
    public void Probe_Kdbx31_AesKdf()
    {
        var path = Path.Combine(_directory, "old.kdbx");
        File.WriteAllBytes(path, Kdbx31Header());

        Assert.True(KdbxImport.TryProbe(path, out var probe, out var error), error);
        Assert.Equal("KDBX 3.1", probe.Version);
        Assert.Equal("AES-KDF", probe.Kdf);
        Assert.Equal("AES-256", probe.Cipher);
    }

    [Fact]
    public void Probe_NotAKdbx_IsRefused()
    {
        var path = Path.Combine(_directory, "notes.kdbx");
        File.WriteAllText(path, "a text file wearing the extension");

        Assert.False(KdbxImport.TryProbe(path, out var probe, out var error));
        Assert.Null(probe);
        Assert.Contains("not a KDBX vault", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Probe_TruncatedHeader_IsRefused()
    {
        var path = Path.Combine(_directory, "cut.kdbx");
        using (var vault = Vault.Create(path, _password))
        {
            vault.Save();
        }

        File.WriteAllBytes(path, File.ReadAllBytes(path)[..40]);

        Assert.False(KdbxImport.TryProbe(path, out _, out var error));
        Assert.Contains("ends inside its header", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Probe_FindsASiblingKeyfile()
    {
        var path = Path.Combine(_directory, "acme.kdbx");
        using (var vault = Vault.Create(path, _password))
        {
            vault.Save();
        }

        File.WriteAllText(Path.Combine(_directory, "acme.key"), "legacy");
        Assert.True(KdbxImport.TryProbe(path, out var legacy, out _));
        Assert.Equal(Path.Combine(_directory, "acme.key"), legacy.SiblingKeyfile);

        File.WriteAllText(Path.Combine(_directory, "acme.keyx"), "preferred");
        Assert.True(KdbxImport.TryProbe(path, out var probe, out _));
        Assert.Equal(Path.Combine(_directory, "acme.keyx"), probe.SiblingKeyfile);
    }

    private static byte[] Kdbx31Header()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(KdbxFormat.FileSignature1);
        writer.Write(KdbxFormat.FileSignature2);
        writer.Write((ushort)1);
        writer.Write((ushort)3);

        writer.Write((byte)2);
        writer.Write((ushort)16);
        writer.Write(new byte[] { 0x31, 0xC1, 0xF2, 0xE6, 0xBF, 0x71, 0x43, 0x50, 0xBE, 0x58, 0x05, 0x21, 0x6A, 0xFC, 0x5A, 0xFF });

        writer.Write((byte)6);
        writer.Write((ushort)8);
        writer.Write(60_000UL);

        writer.Write((byte)0);
        writer.Write((ushort)4);
        writer.Write("\r\n\r\n"u8.ToArray());

        writer.Write(new byte[64]);
        writer.Flush();
        return stream.ToArray();
    }
}
