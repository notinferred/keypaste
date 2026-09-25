using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace Keypaste.Core.Sharing;

/// <summary>One field of a shared entry, as the recipient's page shows it.</summary>
/// <param name="Name">The field's name: <c>password</c>, <c>username</c>, <c>url</c> or <c>notes</c>.</param>
/// <param name="Value">The field's value.</param>
public sealed record ShareField(string Name, string Value)
{
    /// <inheritdoc/>
    public override string ToString() => $"ShareField {{ Name = {Name}, Value = (redacted) }}";
}

/// <summary>What a share link opens to: a title and the fields chosen, encrypted before it leaves.</summary>
/// <param name="Title">The entry's title.</param>
/// <param name="Fields">The shared fields, in order.</param>
/// <param name="Created">When the share was made.</param>
public sealed record SharePayload(string Title, IReadOnlyList<ShareField> Fields, DateTimeOffset Created)
{
    /// <inheritdoc/>
    public override string ToString() => $"SharePayload {{ Title = {Title}, Fields = {Fields.Count} (redacted) }}";
}

/// <summary>A sealed share: the envelope the server keeps and the key only the link carries.</summary>
/// <param name="Envelope">The JSON envelope, holding ciphertext and no key.</param>
/// <param name="Key">The link key, base64url.</param>
/// <param name="HasPassphrase">Whether opening it also needs a passphrase.</param>
public sealed record SealedShare(string Envelope, string Key, bool HasPassphrase)
{
    /// <inheritdoc/>
    public override string ToString() => $"SealedShare {{ HasPassphrase = {HasPassphrase}, Key = (redacted) }}";
}

/// <summary>
/// Seals a share for the browser to open: AES-256-GCM under a key derived from the link's key and,
/// when one is given, a passphrase. <c>site/public/s/share-crypto.js</c> is the other half and
/// must agree byte for byte (D-0354).
/// </summary>
/// <remarks>
/// PBKDF2 rather than Argon2 because the recipient derives the key with WebCrypto, which has no
/// Argon2. The key-derivation parameters are bound into the additional data, so a server that
/// lowers the iterations or swaps the salt breaks decryption instead of weakening it.
/// </remarks>
public static class ShareCrypto
{
    /// <summary>PBKDF2-SHA256 iterations for a passphrase.</summary>
    public const int Pbkdf2Iterations = 600_000;

    /// <summary>The largest plaintext a share carries, in UTF-8 bytes.</summary>
    public const int MaximumPlaintextBytes = 8 * 1024;

    internal const int KeyBytes = 32;
    internal const int IvBytes = 12;
    internal const int SaltBytes = 16;
    internal const int TagBytes = 16;
    internal const int MinimumIterations = 1;
    internal const int MaximumIterations = 10_000_000;

    /// <summary>The first code point Unicode's NFC quick check does not answer "yes" for.</summary>
    /// <remarks>
    /// keypaste runs with invariant globalization, where .NET does not normalize, and the viewer
    /// normalizes to NFC. Every string below this point is already NFC, so both sides derive the
    /// same key from it; anything else is refused rather than sealed under a key the page cannot
    /// reproduce.
    /// </remarks>
    private const char _firstNonNfcStable = '̀';

    private const string _kdfName = "PBKDF2-SHA256";

    /// <summary>Whether a payload fits in <see cref="MaximumPlaintextBytes"/>.</summary>
    public static bool Fits(SharePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var buffer = Plaintext(payload);
        var length = buffer.WrittenCount;
        buffer.Clear();

        return length <= MaximumPlaintextBytes;
    }

    /// <summary>Whether a passphrase is one the viewer derives the same key from.</summary>
    /// <remarks>Refused otherwise, with <see cref="PassphraseRule"/> as the reason.</remarks>
    public static bool AcceptsPassphrase(ReadOnlySpan<char> passphrase)
    {
        foreach (var c in passphrase)
        {
            if (c >= _firstNonNfcStable)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>What <see cref="AcceptsPassphrase"/> requires, as a person reads it.</summary>
    public const string PassphraseRule = "a passphrase can use Latin letters, digits, spaces and punctuation only";

    /// <summary>Seals a payload under a fresh link key, IVs and salt.</summary>
    /// <param name="payload">What to share.</param>
    /// <param name="passphrase">The passphrase, or an empty span for none.</param>
    /// <returns>The envelope and the key.</returns>
    /// <exception cref="ArgumentException">The payload does not <see cref="Fits"/>, or the passphrase is not accepted.</exception>
    public static SealedShare Seal(SharePayload payload, ReadOnlySpan<char> passphrase)
    {
        Span<byte> key = stackalloc byte[KeyBytes];
        Span<byte> iv = stackalloc byte[IvBytes];
        Span<byte> checkIv = stackalloc byte[IvBytes];
        Span<byte> salt = stackalloc byte[SaltBytes];
        RandomNumberGenerator.Fill(key);
        RandomNumberGenerator.Fill(iv);
        RandomNumberGenerator.Fill(checkIv);
        RandomNumberGenerator.Fill(salt);

        try
        {
            return Seal(payload, passphrase, key, iv, checkIv, salt, Pbkdf2Iterations);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>Seals with every random input supplied, for the cross-language vector.</summary>
    internal static SealedShare Seal(
        SharePayload payload,
        ReadOnlySpan<char> passphrase,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> iv,
        ReadOnlySpan<byte> checkIv,
        ReadOnlySpan<byte> salt,
        int iterations)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!Fits(payload))
        {
            throw new ArgumentException($"a share holds at most {MaximumPlaintextBytes} bytes", nameof(payload));
        }

        if (!AcceptsPassphrase(passphrase))
        {
            throw new ArgumentException(PassphraseRule, nameof(passphrase));
        }

        var kdf = passphrase.IsEmpty ? null : new Kdf(iterations, Base64Url.EncodeToString(salt));
        var plaintext = Plaintext(payload);
        var ciphertext = new byte[plaintext.WrittenCount + TagBytes];
        var check = new byte[TagBytes];
        Span<byte> cek = stackalloc byte[KeyBytes];

        try
        {
            DeriveContentKey(key, passphrase, kdf, cek);

            using var aes = new AesGcm(cek, TagBytes);
            aes.Encrypt(
                iv,
                plaintext.WrittenSpan,
                ciphertext.AsSpan(0, plaintext.WrittenCount),
                ciphertext.AsSpan(plaintext.WrittenCount),
                Aad(kdf));
            aes.Encrypt(checkIv, ReadOnlySpan<byte>.Empty, Span<byte>.Empty, check, "keypaste-share-check:v1"u8);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cek);
            plaintext.Clear();
        }

        var envelope = WriteEnvelope(kdf, iv, ciphertext, checkIv, check);
        return new SealedShare(envelope, Base64Url.EncodeToString(key), kdf is not null);
    }

    /// <summary>Opens an envelope with its link key and passphrase.</summary>
    /// <param name="envelope">The envelope as the server stores it.</param>
    /// <param name="key">The link key, base64url.</param>
    /// <param name="passphrase">The passphrase, or empty.</param>
    /// <param name="payload">What was shared, on success.</param>
    /// <param name="error">Why it did not open, or empty.</param>
    /// <remarks>The viewer's job; here for tests and for parity with it.</remarks>
    public static bool TryOpen(
        string envelope,
        string key,
        ReadOnlySpan<char> passphrase,
        [NotNullWhen(true)] out SharePayload? payload,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(key);

        payload = null;

        if (!TryParse(envelope, requireCiphertext: true, out var parsed, out error)
            || !TryKey(key, parsed, passphrase, out var keyBytes, out error))
        {
            return false;
        }

        var ciphertext = parsed.Ciphertext!;
        var plaintext = new byte[ciphertext.Length - TagBytes];
        Span<byte> cek = stackalloc byte[KeyBytes];

        try
        {
            DeriveContentKey(keyBytes, passphrase, parsed.Kdf, cek);

            using var aes = new AesGcm(cek, TagBytes);
            aes.Decrypt(
                parsed.Iv!,
                ciphertext.AsSpan(0, plaintext.Length),
                ciphertext.AsSpan(plaintext.Length),
                plaintext,
                Aad(parsed.Kdf));

            if (!TryReadPayload(plaintext, out payload))
            {
                error = "the share opened but does not hold a payload keypaste made";
                return false;
            }

            return true;
        }
        catch (AuthenticationTagMismatchException)
        {
            error = "the share could not be decrypted: the key or passphrase is wrong, or it was altered";
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cek);
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(keyBytes);
        }
    }

    /// <summary>Whether a key and passphrase open an envelope, judged from its check alone.</summary>
    /// <remarks>
    /// Needs only what the server's metadata answer carries, which is how the viewer tells a wrong
    /// passphrase before it spends a view.
    /// </remarks>
    internal static bool TryCheck(string envelope, string key, ReadOnlySpan<char> passphrase, out string error)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(key);

        if (!TryParse(envelope, requireCiphertext: false, out var parsed, out error)
            || !TryKey(key, parsed, passphrase, out var keyBytes, out error))
        {
            return false;
        }

        Span<byte> cek = stackalloc byte[KeyBytes];

        try
        {
            DeriveContentKey(keyBytes, passphrase, parsed.Kdf, cek);

            using var aes = new AesGcm(cek, TagBytes);
            aes.Decrypt(parsed.CheckIv, ReadOnlySpan<byte>.Empty, parsed.Check, Span<byte>.Empty, "keypaste-share-check:v1"u8);
            return true;
        }
        catch (AuthenticationTagMismatchException)
        {
            error = "the key or passphrase does not open this share";
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cek);
            CryptographicOperations.ZeroMemory(keyBytes);
        }
    }

    private static void DeriveContentKey(ReadOnlySpan<byte> key, ReadOnlySpan<char> passphrase, Kdf? kdf, Span<byte> cek)
    {
        if (kdf is null)
        {
            HKDF.DeriveKey(HashAlgorithmName.SHA256, key, cek, ReadOnlySpan<byte>.Empty, "keypaste share v1"u8);
            return;
        }

        Span<byte> material = stackalloc byte[KeyBytes * 2];
        Span<byte> salt = stackalloc byte[SaltBytes];

        try
        {
            key.CopyTo(material);
            Base64Url.DecodeFromChars(kdf.Salt, salt);
            Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, material[KeyBytes..], kdf.Iterations, HashAlgorithmName.SHA256);
            HKDF.DeriveKey(HashAlgorithmName.SHA256, material, cek, ReadOnlySpan<byte>.Empty, "keypaste share v1 passphrase"u8);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }

    private static byte[] Aad(Kdf? kdf) => Encoding.UTF8.GetBytes(kdf is null
        ? "keypaste-share:v1:none"
        : string.Create(CultureInfo.InvariantCulture, $"keypaste-share:v1:pbkdf2-sha256:{kdf.Iterations}:{kdf.Salt}"));

    private static ArrayBufferWriter<byte> Plaintext(SharePayload payload)
    {
        var buffer = new ArrayBufferWriter<byte>(512);
        using var json = new Utf8JsonWriter(buffer);

        json.WriteStartObject();
        json.WriteNumber("v", 1);
        json.WriteString("title", payload.Title);
        json.WriteStartArray("fields");
        foreach (var field in payload.Fields)
        {
            json.WriteStartObject();
            json.WriteString("name", field.Name);
            json.WriteString("value", field.Value);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteString("created", payload.Created.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        json.WriteEndObject();
        json.Flush();

        return buffer;
    }

    private static bool TryReadPayload(byte[] plaintext, [NotNullWhen(true)] out SharePayload? payload)
    {
        payload = null;

        try
        {
            using var document = JsonDocument.Parse(plaintext);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("v", out var version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != 1
                || !root.TryGetProperty("title", out var title) || title.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array
                || !root.TryGetProperty("created", out var created) || created.ValueKind != JsonValueKind.String
                || !DateTimeOffset.TryParse(created.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when))
            {
                return false;
            }

            List<ShareField> read = [];
            foreach (var field in fields.EnumerateArray())
            {
                if (field.ValueKind != JsonValueKind.Object
                    || !field.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
                    || !field.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                read.Add(new ShareField(name.GetString()!, value.GetString()!));
            }

            payload = new SharePayload(title.GetString()!, read, when);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return false;
        }
    }

    private static string WriteEnvelope(Kdf? kdf, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> checkIv, ReadOnlySpan<byte> check)
    {
        var buffer = new ArrayBufferWriter<byte>(ciphertext.Length * 2);
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("v", 1);
            json.WriteString("alg", "A256GCM");
            if (kdf is null)
            {
                json.WriteNull("kdf");
            }
            else
            {
                json.WriteStartObject("kdf");
                json.WriteString("name", _kdfName);
                json.WriteNumber("iterations", kdf.Iterations);
                json.WriteString("salt", kdf.Salt);
                json.WriteEndObject();
            }

            json.WriteString("iv", Base64Url.EncodeToString(iv));
            json.WriteString("ct", Base64Url.EncodeToString(ciphertext));
            json.WriteString("check_iv", Base64Url.EncodeToString(checkIv));
            json.WriteString("check", Base64Url.EncodeToString(check));
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static bool TryParse(string envelope, bool requireCiphertext, [NotNullWhen(true)] out Envelope? parsed, out string error)
    {
        parsed = null;
        error = "the share's envelope is not one keypaste made";

        try
        {
            using var document = JsonDocument.Parse(envelope);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("v", out var version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != 1
                || !root.TryGetProperty("alg", out var alg) || alg.ValueKind != JsonValueKind.String || alg.GetString() != "A256GCM"
                || !TryBytes(root, "check_iv", IvBytes, out var checkIv)
                || !TryBytes(root, "check", TagBytes, out var check)
                || !TryKdf(root, out var kdf))
            {
                return false;
            }

            byte[]? iv = null;
            byte[]? ciphertext = null;
            if (requireCiphertext
                && (!TryBytes(root, "iv", IvBytes, out iv) || !TryBytes(root, "ct", null, out ciphertext) || ciphertext.Length < TagBytes))
            {
                return false;
            }

            parsed = new Envelope(iv, ciphertext, checkIv, check, kdf);
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryKdf(JsonElement root, out Kdf? kdf)
    {
        kdf = null;

        if (!root.TryGetProperty("kdf", out var element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String || name.GetString() != _kdfName
            || !element.TryGetProperty("iterations", out var iterations) || !iterations.TryGetInt32(out var count)
            || count is < MinimumIterations or > MaximumIterations
            || !TryBytes(element, "salt", SaltBytes, out _))
        {
            return false;
        }

        kdf = new Kdf(count, element.GetProperty("salt").GetString()!);
        return true;
    }

    private static bool TryBytes(JsonElement parent, string name, int? length, [NotNullWhen(true)] out byte[]? bytes)
    {
        bytes = null;

        if (!parent.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = element.GetString()!;
        if (!IsBase64Url(text, length))
        {
            return false;
        }

        bytes = Base64Url.DecodeFromChars(text);
        return true;
    }

    private static bool TryKey(string key, Envelope envelope, ReadOnlySpan<char> passphrase, out byte[] keyBytes, out string error)
    {
        keyBytes = [];

        if (!IsBase64Url(key, KeyBytes))
        {
            error = "the link's key is incomplete";
            return false;
        }

        if (envelope.Kdf is not null && passphrase.IsEmpty)
        {
            error = "this share needs its passphrase";
            return false;
        }

        keyBytes = Base64Url.DecodeFromChars(key);
        error = string.Empty;
        return true;
    }

    /// <summary>Whether text is unpadded base64url, decoding to <paramref name="bytes"/> bytes when that is given.</summary>
    internal static bool IsBase64Url(ReadOnlySpan<char> text, int? bytes)
    {
        foreach (var c in text)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            {
                return false;
            }
        }

        return Base64Url.IsValid(text, out var decoded)
            && (bytes is not { } exact || (decoded == exact && text.Length == Base64Url.GetEncodedLength(exact)));
    }

    private sealed record Kdf(int Iterations, string Salt);

    private sealed record Envelope(byte[]? Iv, byte[]? Ciphertext, byte[] CheckIv, byte[] Check, Kdf? Kdf);
}
