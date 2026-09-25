using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace Keypaste.Core.Sharing;

/// <summary>One share link as the vault remembers it: what, to whom and until when, never the key.</summary>
/// <param name="Id">The server-made id.</param>
/// <param name="What">The shared entry's path, sanitized.</param>
/// <param name="Field">The field or fields shared: <c>password</c>, <c>username</c>, <c>url</c>, <c>notes</c> or <c>login</c>.</param>
/// <param name="Recipient">The person's own label for who it went to, or null.</param>
/// <param name="Created">When the server's clock started.</param>
/// <param name="Expires">When the server stops serving it.</param>
/// <param name="Views">How many views it was made with.</param>
/// <param name="Passphrase">Whether opening it needs a passphrase.</param>
/// <param name="Endpoint">The origin it was made on.</param>
public sealed record ShareInfo(
    string Id,
    string What,
    string Field,
    string? Recipient,
    DateTimeOffset Created,
    DateTimeOffset Expires,
    int Views,
    bool Passphrase,
    string Endpoint)
{
    /// <summary>The limits in words: <c>1 view · 24h · passphrase</c>.</summary>
    public string Rule
    {
        get
        {
            var rule = Views == 1 ? "1 view" : $"{Views} views";
            rule += " · " + FormatTtl(Expires - Created);
            return Passphrase ? rule + " · passphrase" : rule;
        }
    }

    /// <summary>A span as a share limit is written: <c>90m</c>, <c>24h</c>, <c>7d</c>.</summary>
    public static string FormatTtl(TimeSpan ttl)
    {
        var minutes = (long)Math.Round(ttl.TotalMinutes);

        return minutes % 1440 == 0 && minutes > 1440 ? $"{minutes / 1440}d"
            : minutes % 60 == 0 && minutes > 0 ? $"{minutes / 60}h"
            : $"{minutes}m";
    }
}

/// <summary>
/// The vault's record of the share links made from it, one entry per link in
/// <see cref="ReservedGroups.Shares"/>. None of these save.
/// </summary>
/// <remarks>
/// An entry's title is the id, its user name the label, its password the revoke token, its URL the
/// endpoint and its notes the metadata. The link's key and the shared values are never written: a
/// vault that is later opened by somebody else yields a list of links, not a way to open them.
/// </remarks>
/// <param name="vault">The open vault.</param>
public sealed class ShareStore(Vault vault)
{
    private readonly Vault _vault = vault ?? throw new ArgumentNullException(nameof(vault));

    /// <summary>Every recorded share, soonest to expire first. A record that does not parse is left out.</summary>
    public IReadOnlyList<ShareInfo> List() =>
    [
        .. _vault.ReadEntries()
            .Where(entry => string.Equals(entry.GroupPath, ReservedGroups.Shares, StringComparison.Ordinal))
            .Select(Read)
            .OfType<ShareInfo>()
            .OrderBy(info => info.Expires)
            .ThenBy(info => info.Id, StringComparer.Ordinal),
    ];

    /// <summary>Records a share and the token that revokes it.</summary>
    public void Add(ShareInfo info, string revokeToken)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(revokeToken);

        _vault.AddEntry(new VaultEntry
        {
            Title = info.Id,
            GroupPath = ReservedGroups.Shares,
            Username = info.Recipient ?? string.Empty,
            Password = revokeToken,
            Url = info.Endpoint,
            Notes = Notes(info),
        });
    }

    /// <summary>The revoke token recorded for a share.</summary>
    public bool TryGetRevokeToken(string id, [NotNullWhen(true)] out string? token)
    {
        ArgumentNullException.ThrowIfNull(id);

        token = _vault.Find(Name(id))?.Password;
        if (string.IsNullOrEmpty(token))
        {
            token = null;
            return false;
        }

        return true;
    }

    /// <summary>Forgets a share for good, revoke token and history included.</summary>
    public bool Remove(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        var outcome = _vault.RemoveEntry(Name(id), out var recycled);
        if (outcome == DeletionOutcome.Recycled)
        {
            _vault.PurgeRecycled(recycled);
        }

        return outcome != DeletionOutcome.NothingMatched;
    }

    /// <summary>The one share whose id starts with <paramref name="idPrefix"/>.</summary>
    /// <param name="idPrefix">The start of an id, compared exactly.</param>
    /// <param name="ambiguous">Whether more than one share starts that way.</param>
    /// <returns>The share, or null when none or several match.</returns>
    public ShareInfo? Find(string idPrefix, out bool ambiguous)
    {
        ArgumentNullException.ThrowIfNull(idPrefix);

        var matches = List().Where(info => info.Id.StartsWith(idPrefix, StringComparison.Ordinal)).Take(2).ToList();

        ambiguous = matches.Count > 1;
        return matches.Count == 1 ? matches[0] : null;
    }

    private static EntryName Name(string id) => new(ReservedGroups.Shares, id);

    private static string Notes(ShareInfo info)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("v", 1);
            json.WriteString("what", info.What);
            json.WriteString("field", info.Field);
            json.WriteString("created", Iso(info.Created));
            json.WriteString("expires", Iso(info.Expires));
            json.WriteNumber("views", info.Views);
            json.WriteBoolean("passphrase", info.Passphrase);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static ShareInfo? Read(VaultEntry entry)
    {
        if (!ShareLink.IsId(entry.Title))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(entry.Notes);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("what", out var what) || what.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("field", out var field) || field.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("created", out var created) || !TryTime(created, out var createdAt)
                || !root.TryGetProperty("expires", out var expires) || !TryTime(expires, out var expiresAt)
                || !root.TryGetProperty("views", out var views) || !views.TryGetInt32(out var viewCount)
                || !root.TryGetProperty("passphrase", out var passphrase)
                || passphrase.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return null;
            }

            return new ShareInfo(
                entry.Title,
                what.GetString()!,
                field.GetString()!,
                entry.Username.Length == 0 ? null : entry.Username,
                createdAt,
                expiresAt,
                viewCount,
                passphrase.GetBoolean(),
                entry.Url);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryTime(JsonElement element, out DateTimeOffset value)
    {
        value = default;

        return element.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value);
    }

    internal static string Iso(DateTimeOffset when) =>
        when.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
