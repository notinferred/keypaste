using System.Text.Json;
using Keypaste.Core.Agents;

namespace Keypaste.Core.Recommendations;

/// <summary>One recommendation as a dismissal matches it: which vault, which entry and which key, never a value.</summary>
/// <param name="Vault">The vault's non-secret identity (<see cref="Ownership.VaultIdentity.Key"/>).</param>
/// <param name="Entry">The entry's KDBX identifier as hex, which survives a rename.</param>
/// <param name="Kind">The kind of recommendation: <see cref="RecommendationDismissals.NoteKey"/>, <see cref="RecommendationDismissals.LostProjectTag"/>, <see cref="RecommendationDismissals.Password"/> or <see cref="RecommendationDismissals.Reminder"/>.</param>
/// <param name="Key">The field a key in notes names, a lost tag with when it was lost (<see cref="LostProjectTag.Key"/>), a password's problem (<see cref="PasswordFinding.Key"/>), or a reminder's reason and time (<see cref="ItemReminder.Key"/>).</param>
public sealed record Dismissal(string Vault, string Entry, string Kind, string Key);

/// <summary>
/// Recommendations dismissed on this machine, in <c>recommendations.json</c> under keypaste's home.
/// </summary>
/// <remarks>
/// <para>
/// The file keeps each dismissal as its vault's identity and an HMAC-SHA256 of the dismissal under a key derived from that vault's
/// agent-rules key, so it names no entry, field, project or reminder, and only the process holding the unlocked vault matches one.
/// </para>
/// <para>
/// A dismissal an earlier keypaste wrote in the clear still matches, and is kept as its HMAC from the next change made with its
/// vault unlocked. An unreadable file dismisses nothing, so the list fails toward showing what it found, and is never replaced.
/// </para>
/// </remarks>
public sealed class RecommendationDismissals
{
    /// <summary>The kind a key left in notes is dismissed under.</summary>
    public const string NoteKey = "note-key";

    /// <summary>The kind a project tag an entry lost is dismissed under (V.11).</summary>
    public const string LostProjectTag = "lost-project-tag";

    /// <summary>The kind a weak, reused or expired password is dismissed under (V.9).</summary>
    public const string Password = "password";

    /// <summary>The kind an item expiring soon or marked as needing an update is dismissed under.</summary>
    public const string Reminder = "reminder";

    /// <summary>The kind a warning about a key file keyed by its exact bytes is dismissed under (T-28).</summary>
    public const string FragileKeyFile = "fragile-key-file";

    /// <summary>The kind a vault's key derivation weaker than keypaste's is dismissed under.</summary>
    public const string KeyDerivation = "key-derivation";

    /// <summary>The largest file read, in bytes.</summary>
    public const int MaximumBytes = 256 * 1024;

    private readonly List<(string Vault, string Digest)> _digests;
    private readonly List<Dismissal> _inTheClear;

    private RecommendationDismissals(List<(string Vault, string Digest)> digests, List<Dismissal> inTheClear)
    {
        _digests = digests;
        _inTheClear = inTheClear;
    }

    /// <summary>The key a vault's dismissals are kept under: its agent-rules key, or null while it is locked or holds none.</summary>
    /// <param name="vault">The unlocked vault.</param>
    /// <returns>The key, or null.</returns>
    public static byte[]? KeyOf(Vault? vault) => SealedAgentRules.KeyOf(vault);

    /// <summary>Writes the vault's agent-rules key when it holds none, as an edit to save.</summary>
    /// <param name="vault">The unlocked vault.</param>
    /// <returns>Whether the vault changed.</returns>
    public static bool WriteKey(Vault vault)
    {
        ArgumentNullException.ThrowIfNull(vault);
        return SealedAgentRules.WriteNewKey(vault);
    }

    /// <summary>Reads the dismissals.</summary>
    /// <param name="path">The file, from <see cref="Audit.KeypasteHome.RecommendationsPath"/>.</param>
    /// <param name="dismissals">Every well-formed dismissal, or none when this returns false.</param>
    /// <returns>False when the file exists and could not be read, which <see cref="Save"/> must then not replace.</returns>
    public static bool TryLoad(string path, out RecommendationDismissals dismissals)
    {
        ArgumentNullException.ThrowIfNull(path);

        dismissals = new([], []);

        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);

            if (bytes.Length > MaximumBytes)
            {
                return false;
            }

            using var document = JsonDocument.Parse(bytes);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("dismissed", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            List<(string, string)> digests = [];
            List<Dismissal> inTheClear = [];

            foreach (var item in items.EnumerateArray())
            {
                if (Text(item, "vault") is not { } vault)
                {
                    continue;
                }

                if (Text(item, "digest") is { } digest)
                {
                    digests.Add((vault, digest));
                }
                else if (Text(item, "entry") is { } entry && Text(item, "kind") is { } kind && Text(item, "key") is { } key)
                {
                    inTheClear.Add(new Dismissal(vault, entry, kind, key));
                }
            }

            dismissals = new(digests, inTheClear);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    /// <summary>Whether a recommendation is dismissed.</summary>
    /// <param name="finding">The recommendation.</param>
    /// <param name="key">Its vault's <see cref="KeyOf"/>, or null when it holds none.</param>
    /// <returns>Whether it is.</returns>
    public bool IsDismissed(Dismissal finding, byte[]? key)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return _inTheClear.Contains(finding) || (key is not null && _digests.Contains((finding.Vault, Digest(finding, key))));
    }

    /// <summary>Dismisses recommendations of one vault, and keeps that vault's dismissals written in the clear as their HMAC from now on.</summary>
    /// <param name="findings">The recommendations.</param>
    /// <param name="key">Their vault's <see cref="KeyOf"/>.</param>
    public void Dismiss(IEnumerable<Dismissal> findings, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(key);

        foreach (var finding in findings)
        {
            HideInTheClear(finding.Vault, key);
            Keep(finding, key);
        }
    }

    /// <summary>Lists recommendations of one vault again, however they were kept.</summary>
    /// <param name="findings">The recommendations.</param>
    /// <param name="key">Their vault's <see cref="KeyOf"/>, or null when it holds none.</param>
    public void ReviewAgain(IEnumerable<Dismissal> findings, byte[]? key)
    {
        ArgumentNullException.ThrowIfNull(findings);

        foreach (var finding in findings)
        {
            if (key is not null)
            {
                HideInTheClear(finding.Vault, key);
                _digests.Remove((finding.Vault, Digest(finding, key)));
            }

            _inTheClear.Remove(finding);
        }
    }

    /// <summary>Writes the dismissals, replacing the file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Whether the file was written.</returns>
    /// <remarks>Owner-only on Unix; on Windows the file inherits the profile's ACL, as <c>app.toml</c> does.</remarks>
    public bool Save(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("dismissed");

            foreach (var (vault, digest) in _digests)
            {
                writer.WriteStartObject();
                writer.WriteString("vault", vault);
                writer.WriteString("digest", digest);
                writer.WriteEndObject();
            }

            foreach (var dismissal in _inTheClear)
            {
                writer.WriteStartObject();
                writer.WriteString("vault", dismissal.Vault);
                writer.WriteString("entry", dismissal.Entry);
                writer.WriteString("kind", dismissal.Kind);
                writer.WriteString("key", dismissal.Key);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        try
        {
            HomeFiles.Replace(path, buffer.ToArray());
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void HideInTheClear(string vault, byte[] key)
    {
        foreach (var dismissal in _inTheClear.Where(dismissal => dismissal.Vault == vault).ToList())
        {
            _inTheClear.Remove(dismissal);
            Keep(dismissal, key);
        }
    }

    private void Keep(Dismissal dismissal, byte[] key)
    {
        var kept = (dismissal.Vault, Digest(dismissal, key));

        if (!_digests.Contains(kept))
        {
            _digests.Add(kept);
        }
    }

    private static string Digest(Dismissal dismissal, byte[] key) =>
        Convert.ToBase64String(HMACSHA256.HashData(
            HKDF.DeriveKey(HashAlgorithmName.SHA256, key, 32, info: "keypaste recommendations.json dismissal"u8.ToArray()),
            Encoding.UTF8.GetBytes($"{dismissal.Vault}\n{dismissal.Entry}\n{dismissal.Kind}\n{dismissal.Key}")));

    private static string? Text(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 } text
            ? text
            : null;
}
