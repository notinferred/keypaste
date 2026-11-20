using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using KeePassLib;
using KeePassLib.Cryptography.Cipher;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Serialization;

namespace Keypaste.Core.Tests;

/// <summary>
/// A KDBX 4 file's XML changed under its own key, header and inner header, as a program writing elements KeePassLib does not know
/// leaves it: decrypted, edited and encrypted again with the file's own seed and IV.
/// </summary>
internal static class ForeignXml
{
    /// <summary>An element no KeePass program writes, which KeePassLib skips.</summary>
    internal const string Element = "<OtherProgramData>kept only by its program</OtherProgramData>";

    /// <summary>Puts <see cref="Element"/> in the file's <c>Meta</c>.</summary>
    internal static void AddTo(string path, string masterPassword) =>
        Edit(path, masterPassword, xml => xml.Replace("</Meta>", Element + "</Meta>", StringComparison.Ordinal));

    /// <summary>Whether the file's XML holds <see cref="Element"/>.</summary>
    internal static bool Holds(string path, string masterPassword) =>
        Read(path, masterPassword).Xml.Contains(Element, StringComparison.Ordinal);

    private static void Edit(string path, string masterPassword, Func<string, string> edit)
    {
        var file = Read(path, masterPassword);
        var xml = Encoding.UTF8.GetBytes(edit(file.Xml));

        using var output = new MemoryStream();
        output.Write(file.Bytes, 0, file.PayloadStart);

        using (var blocks = new HmacBlockStream(output, true, true, file.HmacKey))
        using (var plain = file.Cipher.EncryptStream(blocks, file.CipherKey, file.Iv))
        using (var content = file.Compressed ? new GZipStream(plain, CompressionMode.Compress) : plain)
        {
            content.Write(file.InnerHeader);
            content.Write(xml);
        }

        File.WriteAllBytes(path, output.ToArray());
    }

    private static Decrypted Read(string path, string masterPassword)
    {
        var bytes = File.ReadAllBytes(path);
        var key = new CompositeKey();
        key.AddUserKey(new KcpPassword(Encoding.UTF8.GetBytes(masterPassword), false));

        PwDatabase database = new();
        KdfParameters kdf;
        ICipherEngine cipher;
        bool compressed;

        try
        {
            database.Open(IOConnectionInfo.FromPath(path), key, null);
            kdf = database.KdfParameters;
            cipher = CipherPool.GlobalPool.GetCipher(database.DataCipherUuid);
            compressed = database.Compression == PwCompressionAlgorithm.GZip;
        }
        finally
        {
            database.Close();
        }

        byte[]? seed = null;
        byte[]? iv = null;
        var at = 12;

        while (true)
        {
            var id = bytes[at];
            var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 1));
            var data = bytes.AsSpan(at + 5, length).ToArray();
            at += 5 + length;

            if (id == 0)
            {
                break;
            }

            seed = id == 4 ? data : seed;
            iv = id == 7 ? data : iv;
        }

        // The header's SHA-256 and HMAC follow it.
        var payloadStart = at + 64;
        var transformed = key.GenerateKey32(kdf).ReadData();
        var cipherKey = SHA256.HashData([.. seed!, .. transformed]);
        var hmacKey = SHA512.HashData([.. seed!, .. transformed, 1]);

        using var decrypted = new MemoryStream();

        using (var blocks = new HmacBlockStream(new MemoryStream(bytes, payloadStart, bytes.Length - payloadStart), false, true, hmacKey))
        using (var plain = cipher.DecryptStream(blocks, cipherKey, iv!))
        using (var content = compressed ? new GZipStream(plain, CompressionMode.Decompress) : plain)
        {
            content.CopyTo(decrypted);
        }

        var payload = decrypted.ToArray();
        var xmlStart = 0;

        while (true)
        {
            var id = payload[xmlStart];
            xmlStart += 5 + BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(xmlStart + 1));

            if (id == 0)
            {
                break;
            }
        }

        return new Decrypted(
            bytes, payloadStart, cipher, cipherKey, iv!, hmacKey, compressed, payload[..xmlStart], Encoding.UTF8.GetString(payload, xmlStart, payload.Length - xmlStart));
    }

    private sealed record Decrypted(
        byte[] Bytes, int PayloadStart, ICipherEngine Cipher, byte[] CipherKey, byte[] Iv, byte[] HmacKey, bool Compressed, byte[] InnerHeader, string Xml);
}
