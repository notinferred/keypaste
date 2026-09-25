using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Keypaste.Core.Tokens;

/// <summary>One set a bundle carries.</summary>
/// <param name="Project">The project.</param>
/// <param name="Profile">The profile.</param>
/// <param name="Variables">The released set.</param>
public sealed record BundledSet(string Project, string Profile, IReadOnlyList<EnvVariable> Variables)
{
    /// <summary>A description with every value left out.</summary>
    /// <returns>The pair and a count, never a value.</returns>
    public override string ToString() => $"BundledSet {{ {Project}/{Profile}, Variables = {Variables.Count} }}";
}

/// <summary>
/// A token's scoped sets sealed for a machine with no vault, such as CI: AES-256-GCM under a key
/// derived from the token's secret, with its header authenticated as associated data.
/// </summary>
/// <remarks>
/// <para>
/// The file is JSON text so it can live in a CI secret or an artifact. Its header names the token
/// id, the scopes and the expiry in the clear, and is bound to the ciphertext, so changing any of
/// it makes the bundle fail to open. Expiry is read only once the bundle has authenticated.
/// </para>
/// <para>
/// A bundle opens without asking anybody and cannot be revoked once written, so it never carries a
/// protected profile; the verb that makes one refuses that before anything is sealed.
/// </para>
/// </remarks>
public static class TokenBundle
{
    /// <summary>The largest bundle file read.</summary>
    public const int MaximumBytes = 1024 * 1024;

    private const string _kind = "keypaste-bundle";
    private const int _nonceBytes = 12;
    private const int _tagBytes = 16;
    private const int _saltBytes = 32;

    private static readonly JsonDocumentOptions _strict = new() { AllowDuplicateProperties = false };

    /// <summary>Seals sets under a token.</summary>
    /// <param name="info">The token, as its vault records it.</param>
    /// <param name="secret">The token's secret.</param>
    /// <param name="sets">What to carry.</param>
    /// <param name="now">When the bundle is made.</param>
    /// <returns>The file's bytes.</returns>
    public static byte[] Seal(TokenInfo info, ReadOnlySpan<byte> secret, IReadOnlyList<BundledSet> sets, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(sets);

        var salt = RandomNumberGenerator.GetBytes(_saltBytes);
        var nonce = RandomNumberGenerator.GetBytes(_nonceBytes);
        var header = Json(json =>
        {
            json.WriteNumber("v", 1);
            json.WriteString("token_id", info.Id);
            json.WriteString("name", info.Name);
            json.WriteStartArray("scopes");
            foreach (var scope in info.Scopes)
            {
                json.WriteStringValue(scope.ToString());
            }

            json.WriteEndArray();
            json.WriteString("created", TokenStore.Timestamp(now));
            json.WriteString("expires", TokenStore.Timestamp(info.Expires));
            json.WriteString("salt", Base64Url.EncodeToString(salt));
        });

        var plaintext = Json(json =>
        {
            json.WriteStartArray("sets");
            foreach (var set in sets)
            {
                json.WriteStartObject();
                json.WriteString("project", set.Project);
                json.WriteString("profile", set.Profile);
                json.WriteStartArray("variables");
                foreach (var variable in set.Variables)
                {
                    json.WriteStartObject();
                    json.WriteString("key", variable.Key);
                    json.WriteString("value", variable.Value);
                    json.WriteEndObject();
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
        });

        var key = TokenSecret.BundleKey(secret, salt);
        var sealedBytes = new byte[plaintext.Length + _tagBytes];

        try
        {
            using var aes = new AesGcm(key, _tagBytes);
            aes.Encrypt(nonce, plaintext, sealedBytes.AsSpan(0, plaintext.Length), sealedBytes.AsSpan(plaintext.Length), header);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }

        return Json(json =>
        {
            json.WriteNumber("v", 1);
            json.WriteString("kind", _kind);
            json.WriteString("header", Base64Url.EncodeToString(header));
            json.WriteString("nonce", Base64Url.EncodeToString(nonce));
            json.WriteString("ct", Base64Url.EncodeToString(sealedBytes));
        });
    }

    /// <summary>Opens a bundle with the token it was made for.</summary>
    /// <param name="file">The file's bytes.</param>
    /// <param name="token">The token.</param>
    /// <param name="now">What expiry is judged against: this machine's clock.</param>
    /// <param name="contents">The sets, when it opened.</param>
    /// <param name="error">Why it did not open, never naming a value or the token.</param>
    /// <returns>Whether it opened.</returns>
    public static bool TryOpen(
        ReadOnlySpan<byte> file,
        string token,
        DateTimeOffset now,
        [NotNullWhen(true)] out TokenBundleContents? contents,
        out string error)
    {
        contents = null;

        if (file.Length > MaximumBytes)
        {
            error = "the bundle is larger than any keypaste makes";
            return false;
        }

        if (!TokenSecret.TryParse(token, out var id, out var secret))
        {
            error = "that is not a keypaste token";
            return false;
        }

        try
        {
            if (!TryOuter(file, out var header, out var nonce, out var sealedBytes)
                || !TryHeader(header, out var tokenId, out var expires, out var salt))
            {
                error = "that is not a keypaste bundle";
                return false;
            }

            if (!string.Equals(tokenId, id, StringComparison.Ordinal))
            {
                error = "the bundle was made for another token";
                return false;
            }

            var plaintext = new byte[sealedBytes.Length - _tagBytes];
            var key = TokenSecret.BundleKey(secret, salt);

            try
            {
                try
                {
                    using var aes = new AesGcm(key, _tagBytes);
                    aes.Decrypt(nonce, sealedBytes.AsSpan(0, plaintext.Length), sealedBytes.AsSpan(plaintext.Length), plaintext, header);
                }
                catch (AuthenticationTagMismatchException)
                {
                    error = "the bundle does not open with this token";
                    return false;
                }

                if (now >= expires)
                {
                    error = $"the bundle expired at {TokenStore.Timestamp(expires)}";
                    return false;
                }

                if (!TrySets(plaintext, out var sets))
                {
                    error = "the bundle's contents are damaged";
                    return false;
                }

                contents = new TokenBundleContents(tokenId, expires, sets);
                error = string.Empty;
                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    private static bool TryOuter(ReadOnlySpan<byte> file, out byte[] header, out byte[] nonce, out byte[] sealedBytes)
    {
        header = nonce = sealedBytes = [];

        try
        {
            using var document = JsonDocument.Parse(file.ToArray(), _strict);
            var root = document.RootElement;

            return root.ValueKind == JsonValueKind.Object
                && IsVersionOne(root)
                && TryString(root, "kind", out var kind) && kind == _kind
                && TryBytes(root, "header", out header)
                && TryBytes(root, "nonce", out nonce) && nonce.Length == _nonceBytes
                && TryBytes(root, "ct", out sealedBytes) && sealedBytes.Length >= _tagBytes;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryHeader(byte[] header, [NotNullWhen(true)] out string? tokenId, out DateTimeOffset expires, out byte[] salt)
    {
        tokenId = null;
        expires = default;
        salt = [];

        try
        {
            using var document = JsonDocument.Parse(header, _strict);
            var root = document.RootElement;

            return root.ValueKind == JsonValueKind.Object
                && IsVersionOne(root)
                && TryString(root, "token_id", out tokenId)
                && TryString(root, "expires", out var expiresText) && TokenStore.TryTimestamp(expiresText, out expires)
                && TryBytes(root, "salt", out salt) && salt.Length == _saltBytes;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TrySets(byte[] plaintext, [NotNullWhen(true)] out IReadOnlyList<BundledSet>? sets)
    {
        sets = null;

        try
        {
            using var document = JsonDocument.Parse(plaintext, _strict);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("sets", out var array)
                || array.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            List<BundledSet> read = [];

            foreach (var set in array.EnumerateArray())
            {
                if (set.ValueKind != JsonValueKind.Object
                    || !TryString(set, "project", out var project)
                    || !TryString(set, "profile", out var profile)
                    || !set.TryGetProperty("variables", out var variables)
                    || variables.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                List<EnvVariable> released = [];

                foreach (var variable in variables.EnumerateArray())
                {
                    if (variable.ValueKind != JsonValueKind.Object
                        || !TryString(variable, "key", out var key)
                        || !TryString(variable, "value", out var value))
                    {
                        return false;
                    }

                    released.Add(new EnvVariable(key, value));
                }

                read.Add(new BundledSet(project, profile, released));
            }

            sets = read;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsVersionOne(JsonElement root) =>
        root.TryGetProperty("v", out var version)
        && version.ValueKind == JsonValueKind.Number
        && version.TryGetInt32(out var number)
        && number == 1;

    private static bool TryString(JsonElement root, string name, [NotNullWhen(true)] out string? value)
    {
        value = root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;
        return value is not null;
    }

    private static bool TryBytes(JsonElement root, string name, out byte[] value)
    {
        value = [];

        if (!TryString(root, name, out var text))
        {
            return false;
        }

        var decoded = new byte[Base64Url.GetMaxDecodedLength(text.Length)];

        if (!Base64Url.IsValid(text) || !Base64Url.TryDecodeFromChars(text, decoded, out var written))
        {
            return false;
        }

        value = decoded[..written];
        return true;
    }

    private static byte[] Json(Action<Utf8JsonWriter> body)
    {
        using var buffer = new MemoryStream();

        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            body(json);
            json.WriteEndObject();
        }

        var bytes = buffer.ToArray();
        CryptographicOperations.ZeroMemory(buffer.GetBuffer());
        return bytes;
    }
}

/// <summary>What an opened bundle holds, released one set at a time.</summary>
public sealed class TokenBundleContents
{
    private readonly IReadOnlyList<BundledSet> _sets;

    internal TokenBundleContents(string tokenId, DateTimeOffset expires, IReadOnlyList<BundledSet> sets)
    {
        TokenId = tokenId;
        Expires = expires;
        _sets = sets;
        Pairs = [.. sets.Select(set => (set.Project, set.Profile))];
    }

    /// <summary>The id of the token the bundle was made for.</summary>
    public string TokenId { get; }

    /// <summary>When the bundle stops opening.</summary>
    public DateTimeOffset Expires { get; }

    /// <summary>Every set the bundle carries, by project and profile.</summary>
    public IReadOnlyList<(string Project, string Profile)> Pairs { get; }

    /// <summary>Releases one set: the one named, or the only one when nothing narrows it further.</summary>
    /// <param name="project">The project, or null for whichever the bundle holds.</param>
    /// <param name="profile">The profile, or null for whichever the bundle holds of that project.</param>
    /// <returns>The set, or why none was released: no such project, no such profile, or a choice that is not one set.</returns>
    public EnvResolved Resolve(string? project, string? profile)
    {
        var matches = _sets
            .Where(set => project is null || string.Equals(set.Project, project, StringComparison.Ordinal))
            .Where(set => profile is null || string.Equals(set.Profile, profile, StringComparison.Ordinal))
            .ToList();

        return matches switch
        {
            [var set] => EnvResolved.Released(set.Project, set.Variables, set.Profile),
            [] when project is not null && !_sets.Any(set => string.Equals(set.Project, project, StringComparison.Ordinal)) =>
                EnvResolved.Refused(project, EnvOutcome.NoProject, profile: profile ?? EnvProfileNames.Default),
            [] => EnvResolved.Refused(project ?? string.Empty, EnvOutcome.NoProfile, profile: profile ?? EnvProfileNames.Default),
            _ => EnvResolved.Refused(project ?? string.Empty, EnvOutcome.Invalid, profile: profile ?? EnvProfileNames.Default),
        };
    }
}
