using System.Text.Json;
using Keypaste.Core.Infrastructure;

namespace Keypaste.Core.Recommendations;

/// <summary>One dismissed recommendation: which vault, which entry and which key, never a value.</summary>
/// <param name="Vault">The vault's non-secret identity (<see cref="Ownership.VaultIdentity.Key"/>).</param>
/// <param name="Entry">The entry's KDBX identifier as hex, which survives a rename.</param>
/// <param name="Kind">The kind of recommendation: <see cref="RecommendationDismissals.NoteKey"/> or <see cref="RecommendationDismissals.LostProjectTag"/>.</param>
/// <param name="Key">The field a key in notes names, or a lost tag with when it was lost (<see cref="LostProjectTag.Key"/>).</param>
public sealed record Dismissal(string Vault, string Entry, string Kind, string Key);

/// <summary>
/// Recommendations dismissed on this machine, in <c>recommendations.json</c> under keypaste's home (D-0372).
/// </summary>
/// <remarks>
/// It holds identifiers and key names only: no entry title, no value and no digest of one. An
/// unreadable file dismisses nothing, so the list fails toward showing what it found, and is never
/// replaced.
/// </remarks>
public static class RecommendationDismissals
{
    /// <summary>The kind a key left in notes is dismissed under.</summary>
    public const string NoteKey = "note-key";

    /// <summary>The kind a project tag an entry lost is dismissed under (V.11).</summary>
    public const string LostProjectTag = "lost-project-tag";

    /// <summary>The largest file read, in bytes.</summary>
    public const int MaximumBytes = 256 * 1024;

    /// <summary>Reads the dismissals.</summary>
    /// <param name="path">The file, from <see cref="Audit.KeypasteHome.RecommendationsPath"/>.</param>
    /// <param name="dismissals">Every well-formed dismissal, or none when this returns false.</param>
    /// <returns>False when the file exists and could not be read, which <see cref="Save"/> must then not replace.</returns>
    public static bool TryLoad(string path, out IReadOnlyList<Dismissal> dismissals)
    {
        ArgumentNullException.ThrowIfNull(path);

        dismissals = [];

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

            List<Dismissal> read = [];

            foreach (var item in items.EnumerateArray())
            {
                if (Text(item, "vault") is { } vault
                    && Text(item, "entry") is { } entry
                    && Text(item, "kind") is { } kind
                    && Text(item, "key") is { } key)
                {
                    read.Add(new Dismissal(vault, entry, kind, key));
                }
            }

            dismissals = read;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    /// <summary>Writes the dismissals, replacing the file.</summary>
    /// <param name="path">The file.</param>
    /// <param name="dismissals">What to write.</param>
    /// <returns>Whether the file was written.</returns>
    /// <remarks>Owner-only on Unix; on Windows the file inherits the profile's ACL, as <c>app.toml</c> does.</remarks>
    public static bool Save(string path, IReadOnlyList<Dismissal> dismissals)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(dismissals);

        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("dismissed");

            foreach (var dismissal in dismissals.Distinct())
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
            if (Path.GetDirectoryName(path) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            AtomicFile.Write(path, buffer.ToArray());
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? Text(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 } text
            ? text
            : null;
}
