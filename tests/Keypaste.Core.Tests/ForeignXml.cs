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
/// A KDBX 4 file's XML, header or inner header changed under its own key, as a program writing elements, attributes or header fields
/// KeePassLib does not know leaves it: decrypted, edited and encrypted again with the file's own seed and IV.
/// </summary>
internal static class ForeignXml
{
    /// <summary>An element no KeePass program writes, which KeePassLib skips.</summary>
    internal const string Element = "<OtherProgramData>kept only by its program</OtherProgramData>";

    /// <summary>An attribute no KeePass program writes, which KeePassLib ignores.</summary>
    internal const string Attribute = "OtherProgramData=\"kept only by its program\"";

    /// <summary>A header field ID no KDBX version defines, which KeePassLib skips in the header and the inner header alike.</summary>
    private const byte _fieldId = 0x80;

    /// <summary>Puts <see cref="Element"/> in the file's <c>Meta</c>.</summary>
    internal static void AddTo(string path, string masterPassword) =>
        Edit(path, masterPassword, xml: xml => xml.Replace("</Meta>", Element + "</Meta>", StringComparison.Ordinal));

    /// <summary>Puts <see cref="Attribute"/> on the file's <c>Meta</c>.</summary>
    internal static void AddAttributeTo(string path, string masterPassword) =>
        Edit(path, masterPassword, xml: xml => xml.Replace("<Meta>", $"<Meta {Attribute}>", StringComparison.Ordinal));

    /// <summary>Puts a field of an unknown ID in the file's outer header, which its hash and HMAC then cover.</summary>
    internal static void AddHeaderFieldTo(string path, string masterPassword) =>
        Edit(path, masterPassword, header: true);

    /// <summary>Puts a field of an unknown ID in the file's inner header, inside the encrypted payload.</summary>
    internal static void AddInnerHeaderFieldTo(string path, string masterPassword) =>
        Edit(path, masterPassword, innerHeader: true);

    /// <summary>Whether the file holds <see cref="Element"/>, <see cref="Attribute"/> or a header or inner header field of the unknown ID.</summary>
    internal static bool Holds(string path, string masterPassword)
    {
        var file = Read(path, masterPassword);
        return file.Xml.Contains(Element, StringComparison.Ordinal)
            || file.Xml.Contains(Attribute, StringComparison.Ordinal)
            || file.HeaderIds.Contains(_fieldId)
            || file.InnerHeaderIds.Contains(_fieldId);
    }

    private static void Edit(string path, string masterPassword, Func<string, string>? xml = null, bool header = false, bool innerHeader = false)
    {
        var file = Read(path, masterPassword);
        var outer = WithField(file.Bytes[..(file.PayloadStart - 64)], file.HeaderEnd, header);
        var headerKey = SHA512.HashData([.. Enumerable.Repeat((byte)0xFF, 8), .. file.HmacKey]);

        using var output = new MemoryStream();
        output.Write(outer);
        output.Write(SHA256.HashData(outer));
        output.Write(HMACSHA256.HashData(headerKey, outer));

        using (var blocks = new HmacBlockStream(output, true, true, file.HmacKey))
        using (var plain = file.Cipher.EncryptStream(blocks, file.CipherKey, file.Iv))
        using (var content = file.Compressed ? new GZipStream(plain, CompressionMode.Compress) : plain)
        {
            content.Write(WithField(file.InnerHeader, file.InnerHeaderEnd, innerHeader));
            content.Write(Encoding.UTF8.GetBytes(xml is null ? file.Xml : xml(file.Xml)));
        }

        File.WriteAllBytes(path, output.ToArray());
    }

    /// <summary>A header's bytes with a field of the unknown ID put before its end field at <paramref name="end"/>, when <paramref name="add"/> says so.</summary>
    private static byte[] WithField(byte[] header, int end, bool add)
    {
        if (!add)
        {
            return header;
        }

        var data = Encoding.UTF8.GetBytes("kept only by its program");
        var length = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, data.Length);
        return [.. header[..end], _fieldId, .. length, .. data, .. header[end..]];
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
        List<byte> headerIds = [];
        var at = 12;
        int headerEnd;

        while (true)
        {
            var id = bytes[at];
            var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 1));
            var data = bytes.AsSpan(at + 5, length).ToArray();
            headerEnd = at;
            at += 5 + length;

            if (id == 0)
            {
                break;
            }

            headerIds.Add(id);
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
        List<byte> innerHeaderIds = [];
        var xmlStart = 0;
        int innerHeaderEnd;

        while (true)
        {
            var id = payload[xmlStart];
            innerHeaderEnd = xmlStart;
            xmlStart += 5 + BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(xmlStart + 1));

            if (id == 0)
            {
                break;
            }

            innerHeaderIds.Add(id);
        }

        return new Decrypted(
            bytes, headerEnd, headerIds, payloadStart, cipher, cipherKey, iv!, hmacKey, compressed,
            payload[..xmlStart], innerHeaderEnd, innerHeaderIds, Encoding.UTF8.GetString(payload, xmlStart, payload.Length - xmlStart));
    }

    private sealed record Decrypted(
        byte[] Bytes, int HeaderEnd, List<byte> HeaderIds, int PayloadStart, ICipherEngine Cipher, byte[] CipherKey, byte[] Iv, byte[] HmacKey,
        bool Compressed, byte[] InnerHeader, int InnerHeaderEnd, List<byte> InnerHeaderIds, string Xml);
}
