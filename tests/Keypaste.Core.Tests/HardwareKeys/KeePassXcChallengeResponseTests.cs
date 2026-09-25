using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using Keypaste.Core.HardwareKeys;
using Xunit;

namespace Keypaste.Core.Tests.HardwareKeys;

/// <summary>
/// Byte compatibility with KeePassXC's YubiKey challenge-response: a database KeePassXC wrote opens,
/// and the key keypaste derives is the one KeePassXC's source defines, computed here without the
/// vendored key derivation for KDBX 4 and KDBX 3.1 alike.
/// </summary>
/// <remarks>
/// What KeePassXC defines (develop 9e0f57a): <c>ChallengeResponseKey</c> answers
/// HMAC-SHA1 of the challenge padded to 64 bytes with the pad length (YubiKeyInterfaceUSB.cpp);
/// <c>CompositeKey::challenge</c> hashes the answers with SHA-256; from KDBX 4
/// <c>CompositeKey::rawKey</c> appends that hash to the static components and the KDF seed is the
/// challenge (CompositeKey.cpp, Kdbx4Reader.cpp); for KDBX 3.1 the master seed is the challenge and
/// the final key is SHA-256(master seed ‖ hash ‖ transformed key) (Kdbx3Reader.cpp).
/// </remarks>
public sealed class KeePassXcChallengeResponseTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-keepassxc-cr-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>
    /// KeePassXC's tests/data/YubiKeyProtectedPasswords.kdbx, which its CLI test lists as entry1 and
    /// entry2 with the password "a" and a slot programmed with <see cref="SoftwareYubiKey.KeePassXcTestSecret"/>.
    /// </summary>
    [Fact]
    public void A_database_KeePassXC_protected_with_a_YubiKey_opens()
    {
        var path = WriteFixture();

        using var key = new HardwareKey(new SoftwareYubiKey(SoftwareYubiKey.KeePassXcTestSecret), 2);
        using var vault = Vault.Open(path, "a", null, key);

        Assert.Equal(["entry1", "entry2"], vault.ReadEntries().Select(entry => entry.Title).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void KeePassXCs_database_refuses_the_password_alone_and_a_fixed_length_slot()
    {
        var path = WriteFixture();

        using var fixedLength = new HardwareKey(new SoftwareYubiKey(SoftwareYubiKey.KeePassXcTestSecret, variableLength: false), 2);

        Assert.Throws<InvalidMasterPasswordException>(() => Vault.Open(path, "a"));
        Assert.Throws<InvalidMasterPasswordException>(() => Vault.Open(path, "a", null, fixedLength));
    }

    [Fact]
    public void A_KDBX_4_vault_carries_the_header_HMAC_of_KeePassXCs_derivation()
    {
        var device = new SoftwareYubiKey(SoftwareYubiKey.KeePassXcTestSecret);
        var path = Path.Combine(_directory, "four.kdbx");
        using (var key = new HardwareKey(device, 2))
        using (var vault = Vault.CreateWith(path, _master, null, key))
        {
            vault.Save();
        }

        var file = File.ReadAllBytes(path);
        var header = Kdbx4Header.Read(file);
        var parameters = KdfParameters.DeserializeExt(header.KdfParameters);
        var seed = parameters.GetByteArray("S");

        var response = SoftwareYubiKey.Answer(SoftwareYubiKey.KeePassXcTestSecret, Padded(seed), variableLength: true);
        var raw = SHA256.HashData([.. SHA256.HashData(Encoding.UTF8.GetBytes(_master)), .. SHA256.HashData(response)]);
        var transformed = KdfPool.Get(parameters.KdfUuid).Transform(raw, parameters);

        var hmacKey = SHA512.HashData([.. header.MasterSeed, .. transformed, 0x01]);
        var blockIndex = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(blockIndex, ulong.MaxValue);
        var headerKey = SHA512.HashData([.. blockIndex, .. hmacKey]);

        Assert.Equal(Padded(seed), device.Challenges[^1]);
        Assert.Equal(file.AsSpan(header.Length + 32, 32).ToArray(), HMACSHA256.HashData(headerKey, file.AsSpan(0, header.Length)));
    }

    /// <summary>
    /// keypaste writes KDBX 4 only, but a KeePassXC 2.x database can still be KDBX 3.1, so one is
    /// written here through the vendored writer's own version override and a test-local key.
    /// </summary>
    [Fact]
    public void A_KDBX_3_1_database_uses_the_master_seed_and_the_final_key_KeePassXC_defines()
    {
        var path = WriteKdbx31("three.kdbx");
        var file = File.ReadAllBytes(path);
        var header = Kdbx31Header.Read(file);

        var composite = SHA256.HashData(SHA256.HashData(Encoding.UTF8.GetBytes(_master)));
        var transformed = AesKdfTransform(composite, header.TransformSeed, header.TransformRounds);
        var response = SoftwareYubiKey.Answer(SoftwareYubiKey.KeePassXcTestSecret, Padded(header.MasterSeed), variableLength: true);
        var finalKey = SHA256.HashData([.. header.MasterSeed, .. SHA256.HashData(response), .. transformed]);

        using var aes = Aes.Create();
        aes.Key = finalKey;
        var start = aes.DecryptCbc(file.AsSpan(header.Length, 32), header.EncryptionIV, PaddingMode.None);

        Assert.Equal(header.StreamStartBytes, start);

        using var key = new HardwareKey(new SoftwareYubiKey(SoftwareYubiKey.KeePassXcTestSecret), 2);
        using (var vault = Vault.Open(path, _master, null, key))
        {
            Assert.Equal("value", Assert.Single(vault.ReadEntries()).Password);
            vault.Save();
        }

        Assert.Equal(4, KdbxHeader.Read(path).FormatMajorVersion);
        Assert.Throws<InvalidMasterPasswordException>(() => Vault.Open(path, _master));

        using var again = new HardwareKey(new SoftwareYubiKey(SoftwareYubiKey.KeePassXcTestSecret), 2);
        using var reopened = Vault.Open(path, _master, null, again);
        Assert.Single(reopened.ReadEntries());
    }

    private static byte[] Padded(byte[] challenge) =>
        [.. challenge, .. Enumerable.Repeat((byte)(64 - challenge.Length), 64 - challenge.Length)];

    private static byte[] AesKdfTransform(byte[] key, byte[] seed, ulong rounds)
    {
        using var aes = Aes.Create();
        aes.Key = seed;
        var block = (byte[])key.Clone();
        for (ulong round = 0; round < rounds; round++)
        {
#pragma warning disable CA5358 // AES-KDF is defined as AES-256 in ECB mode; this computes it, and encrypts nothing.
            block = aes.EncryptEcb(block, PaddingMode.None);
#pragma warning restore CA5358
        }

        return SHA256.HashData(block);
    }

    private string WriteKdbx31(string name)
    {
        var path = Path.Combine(_directory, name);
        CompositeKey key = new();
        key.AddUserKey(new KcpPassword(_master));
        key.AddUserKey(new TestChallengeResponseKey(SoftwareYubiKey.KeePassXcTestSecret));

        PwDatabase database = new();
        database.New(IOConnectionInfo.FromPath(path), key);
        database.KdfParameters = new AesKdf().GetDefaultParameters();
        database.KdfParameters.SetUInt64(AesKdf.ParamRounds, 1000);

        PwEntry entry = new(true, true);
        entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "TOKEN"));
        entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "value"));
        database.RootGroup.AddEntry(entry, true);

        KdbxFile writer = new(database);
        typeof(KdbxFile).GetProperty("ForceVersion", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(writer, 0x00030001u);

        using (var stream = File.Create(path))
        {
            writer.Save(stream, null, KeePassLib.Serialization.KdbxFormat.Default, null);
        }

        database.Close();
        Assert.Equal(3, KdbxHeader.Read(path).FormatMajorVersion);
        return path;
    }

    private string WriteFixture()
    {
        var path = Path.Combine(_directory, "YubiKeyProtectedPasswords.kdbx");
        File.WriteAllBytes(path, Convert.FromBase64String(string.Concat(_keePassXcFixture.Split('\n', StringSplitOptions.TrimEntries))));
        return path;
    }

    /// <summary>A challenge-response key written for the test, so the KDBX 3.1 writer is not keypaste's own key.</summary>
    private sealed class TestChallengeResponseKey(byte[] secret) : IChallengeResponseUserKey
    {
        public ProtectedBinary KeyData => null!;

        public byte[] GetResponse(byte[] pbChallenge) =>
            SoftwareYubiKey.Answer(secret, Padded(pbChallenge), variableLength: true);
    }

    private sealed record Kdbx4Header(int Length, byte[] MasterSeed, byte[] KdfParameters)
    {
        internal static Kdbx4Header Read(byte[] file)
        {
            var fields = Fields(file, sizeLength: 4, out var length);
            return new Kdbx4Header(length, fields[4], fields[11]);
        }
    }

    private sealed record Kdbx31Header(
        int Length, byte[] MasterSeed, byte[] TransformSeed, ulong TransformRounds, byte[] EncryptionIV, byte[] StreamStartBytes)
    {
        internal static Kdbx31Header Read(byte[] file)
        {
            var fields = Fields(file, sizeLength: 2, out var length);
            return new Kdbx31Header(
                length, fields[4], fields[5], BinaryPrimitives.ReadUInt64LittleEndian(fields[6]), fields[7], fields[9]);
        }
    }

    /// <summary>The header fields after the 12-byte signature and version, and where the header ends.</summary>
    private static Dictionary<int, byte[]> Fields(byte[] file, int sizeLength, out int length)
    {
        var fields = new Dictionary<int, byte[]>();
        var at = 12;
        while (true)
        {
            var id = file[at];
            var size = sizeLength == 4
                ? BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(at + 1))
                : BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(at + 1));
            at += 1 + sizeLength;
            fields[id] = file.AsSpan(at, size).ToArray();
            at += size;

            if (id == 0)
            {
                length = at;
                return fields;
            }
        }
    }

    /// <summary>
    /// KeePassXC's tests/data/YubiKeyProtectedPasswords.kdbx at commit 964478e ("CLI: Add Yubikey
    /// unlock support", 2019), SHA-256 566be617…8fad3b, GPL-2.0-or-later or GPL-3.0 as KeePassXC is.
    /// </summary>
    private const string _keePassXcFixture =
        """
        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
        """;
}
