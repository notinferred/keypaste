using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace Keypaste.Core.Tokens;

/// <summary>What checking a presented token came to.</summary>
public enum TokenCheck
{
    /// <summary>The token is one this vault minted, unexpired and unrevoked.</summary>
    Valid = 0,

    /// <summary>The text is not a token at all.</summary>
    Malformed = 1,

    /// <summary>This vault holds no token that answers to it: never minted here, revoked, or a wrong secret.</summary>
    Unknown = 2,

    /// <summary>The token is this vault's and has expired.</summary>
    Expired = 3,

    /// <summary>The open vault holds a change its file does not, so it was not checked.</summary>
    Unsaved = 4,

    /// <summary>Another program saved the file since the vault read it, so it was not checked.</summary>
    ChangedOnDisk = 5,

    /// <summary>The file could not be read to confirm it is unchanged, so it was not checked.</summary>
    Unreadable = 6,
}

/// <summary>
/// Scoped tokens kept in a vault: one entry per token in <see cref="ReservedGroups.Tokens"/>, holding
/// its verifier, name, scopes and expiry, never the token.
/// </summary>
/// <remarks>
/// <para>
/// The entry's title is the id, its user name the name, its password the verifier and its notes
/// the rest as JSON. Anything that does not read back exactly so is not a token (fail closed): a
/// damaged entry verifies nothing, and a KDBX expiry somebody set on it in KeePassXC is honoured
/// as well as the recorded one.
/// </para>
/// <para>
/// Creating and revoking change the open vault and do not save it. Revoking writes a newer version
/// of the record with no verifier and the time it was revoked, which KeePassXC's default merge
/// carries over an older copy where it would drop a deletion, and the record is purged once the
/// token has expired; a record whose UUID the vault also holds a tombstone for reads as revoked
/// (D-0544, D-0629, D-0630).
/// </para>
/// </remarks>
/// <param name="vault">The open vault.</param>
public sealed class TokenStore(Vault vault)
{
    /// <summary>The longest token name.</summary>
    public const int MaximumNameLength = 40;

    /// <summary>The shortest a token may last.</summary>
    public static readonly TimeSpan MinimumLifetime = TimeSpan.FromMinutes(1);

    /// <summary>The longest a token may last.</summary>
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromDays(365);

    private const string _timestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    private const string _revokedProperty = "revoked";

    // A property named twice would leave the reader to pick one, and the writer may have meant the other (D-0325).
    private static readonly JsonDocumentOptions _strict = new() { AllowDuplicateProperties = false };

    private readonly Vault _vault = vault ?? throw new ArgumentNullException(nameof(vault));

    /// <summary>Whether a name is one a token can be given: <c>[a-z0-9][a-z0-9-]{0,39}</c>.</summary>
    /// <param name="name">The name.</param>
    /// <param name="error">What is wrong, or empty.</param>
    /// <returns>Whether it is valid.</returns>
    public static bool IsValidName(string name, out string error)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length is 0 or > MaximumNameLength)
        {
            error = $"a token name has 1 to {MaximumNameLength} characters";
            return false;
        }

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || (c == '-' && i > 0)))
            {
                error = $"'{EntryNameSanitizer.Sanitize(name, 64).Text}' is not a token name: use lowercase letters, digits and '-'";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    /// <summary>Every token the open vault holds, by name.</summary>
    /// <returns>The tokens that read back whole and unrevoked; a damaged entry is not listed.</returns>
    public IReadOnlyList<TokenInfo> List() =>
        [.. Records()
            .Where(record => !record.Revoked)
            .Select(record => record.Info)
            .OfType<TokenInfo>()
            .OrderBy(info => info.Name, StringComparer.Ordinal)];

    /// <summary>Mints a token and records it in the open vault, which the caller saves.</summary>
    /// <param name="name">What to call it, unique among the vault's unrevoked tokens.</param>
    /// <param name="scopes">What it may read.</param>
    /// <param name="ttl">How long it lasts, from <see cref="MinimumLifetime"/> to <see cref="MaximumLifetime"/>.</param>
    /// <param name="allowProd">Whether a scope may name a protected profile, which then asks the person live.</param>
    /// <param name="now">When it is minted.</param>
    /// <param name="info">What was recorded.</param>
    /// <param name="token">The token, to be shown once and never stored.</param>
    /// <param name="error">Why nothing was minted, or empty.</param>
    /// <returns>Whether a token was minted.</returns>
    /// <remarks>Minting also purges each revoked record whose token has expired (D-0630).</remarks>
    public bool TryCreate(
        string name,
        IReadOnlyList<TokenScope> scopes,
        TimeSpan ttl,
        bool allowProd,
        DateTimeOffset now,
        [NotNullWhen(true)] out TokenInfo? info,
        [NotNullWhen(true)] out string? token,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(scopes);

        info = null;
        token = null;

        if (!IsValidName(name, out error))
        {
            return false;
        }

        if (scopes.Count is 0 or > TokenScope.MaximumScopes)
        {
            error = $"a token needs 1 to {TokenScope.MaximumScopes} scopes";
            return false;
        }

        if (ttl < MinimumLifetime || ttl > MaximumLifetime)
        {
            error = "a token lasts from one minute to 365 days";
            return false;
        }

        if (!allowProd && scopes.FirstOrDefault(scope => scope.IsProtected) is { } protectedScope)
        {
            error = $"{protectedScope} names a protected environment; --allow-prod lets the token ask you live for it, every time";
            return false;
        }

        var records = Records();

        if (records.Any(record => !record.Revoked && string.Equals(record.Entry.Username, name, StringComparison.Ordinal)))
        {
            error = $"a token named '{name}' already exists";
            return false;
        }

        PurgeExpired(records, now);

        token = TokenSecret.New(out var id, out var secret);

        while (records.Any(record => string.Equals(record.Entry.Title, id, StringComparison.Ordinal)))
        {
            CryptographicOperations.ZeroMemory(secret);
            token = TokenSecret.New(out id, out secret);
        }

        try
        {
            var created = Truncated(now);
            info = new TokenInfo(id, name, scopes, created, created + ttl, allowProd);

            _vault.AddEntry(new VaultEntry
            {
                GroupPath = ReservedGroups.Tokens,
                Title = id,
                Username = name,
                Password = TokenSecret.Verifier(id, secret),
                Notes = Notes(info, revoked: null),
            });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }

        error = string.Empty;
        return true;
    }

    /// <summary>Revokes a token in the open vault for good, which the caller saves.</summary>
    /// <param name="nameOrId">The token's name, or its id when no unrevoked token has that name.</param>
    /// <param name="now">What expiry is judged against.</param>
    /// <returns>Whether a token was revoked.</returns>
    /// <remarks>
    /// An unexpired token's record becomes a newer version with no verifier and an expired one's is
    /// purged, and each revoked record whose token has expired is purged with it (D-0629, D-0630).
    /// </remarks>
    public bool Revoke(string nameOrId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(nameOrId);

        var records = Records();
        var named = records.Where(record => !record.Revoked && string.Equals(record.Entry.Username, nameOrId, StringComparison.Ordinal)).ToList();

        return Revoke(named.Count > 0 ? named : WithId(records, nameOrId), records, now);
    }

    /// <summary>Revokes the token with this id in the open vault for good, which the caller saves, as <see cref="Revoke(string, DateTimeOffset)"/> does.</summary>
    /// <param name="id">The token's id, never taken as a name.</param>
    /// <param name="now">What expiry is judged against.</param>
    /// <returns>Whether a token was revoked.</returns>
    public bool RevokeId(string id, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(id);

        var records = Records();

        return Revoke(WithId(records, id), records, now);
    }

    /// <summary>Takes a token whose creation no save wrote back out of the open vault, deleting and purging its record.</summary>
    /// <param name="id">The token's id, as <see cref="TryCreate"/> recorded it.</param>
    /// <returns>Whether a record was taken back.</returns>
    /// <remarks>
    /// Only for a creation the caller's save failed to write: no copy of the vault holds that record,
    /// so no merge can bring it back and nothing needs to stay revoked (D-0630).
    /// </remarks>
    public bool TakeBack(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        var doomed = WithId(Records(), id);

        foreach (var record in doomed)
        {
            Purge(record.Entry);
        }

        return doomed.Count > 0;
    }

    private static List<TokenRecord> WithId(List<TokenRecord> records, string id) =>
        [.. records.Where(record => !record.Revoked && string.Equals(record.Entry.Title, id, StringComparison.Ordinal))];

    private bool Revoke(List<TokenRecord> doomed, List<TokenRecord> records, DateTimeOffset now)
    {
        if (doomed.Count == 0)
        {
            return false;
        }

        PurgeExpired(records, now);

        foreach (var record in doomed)
        {
            // An emptied verifier also makes a keypaste that predates the revoked mark refuse the version (D-0629).
            if (record.Info is { } info && now < record.Lasts)
            {
                _vault.UpdateEntry(record.Entry with
                {
                    Password = string.Empty,
                    Notes = Notes(info with { Expires = record.Lasts }, Truncated(now)),
                });
            }
            else
            {
                Purge(record.Entry);
            }
        }

        return true;
    }

    /// <summary>Purges each revoked record whose token has expired by its notes, the expiry every copy of the record holds (D-0630).</summary>
    private void PurgeExpired(List<TokenRecord> records, DateTimeOffset now)
    {
        foreach (var record in records)
        {
            if (record is { Revoked: true, Info: not null } && now >= record.Lasts)
            {
                Purge(record.Entry);
            }
        }
    }

    private void Purge(VaultEntry entry)
    {
        if (_vault.RemoveEntry(EntryName.Of(entry), out var recycled) == DeletionOutcome.Recycled)
        {
            _vault.PurgeRecycled(recycled);
        }
    }

    /// <summary>Checks a presented token against the vault as its file holds it.</summary>
    /// <param name="token">The token as given.</param>
    /// <param name="now">What expiry is judged against.</param>
    /// <param name="info">What the vault records about it, when it is this vault's.</param>
    /// <returns>What the check came to. Only <see cref="TokenCheck.Valid"/> authorizes anything.</returns>
    /// <remarks>
    /// An unminted token, a revoked one and a wrong secret all read <see cref="TokenCheck.Unknown"/>,
    /// so a refusal never says which. <paramref name="info"/> is set only once the secret has proved
    /// itself, so <see cref="TokenCheck.Expired"/> is said only to its holder.
    /// </remarks>
    public TokenCheck Verify(string token, DateTimeOffset now, out TokenInfo? info)
    {
        info = null;

        if (!TokenSecret.TryParse(token, out var id, out var secret))
        {
            return TokenCheck.Malformed;
        }

        try
        {
            switch (_vault.ReadSavedWithTombstones(out var saved, out var tombstoned))
            {
                case SavedRead.Current:
                    break;
                case SavedRead.Unsaved:
                    return TokenCheck.Unsaved;
                case SavedRead.ChangedOnDisk:
                    return TokenCheck.ChangedOnDisk;
                default:
                    return TokenCheck.Unreadable;
            }

            var matches = Entries(saved!).Where(entry => string.Equals(entry.Title, id, StringComparison.Ordinal)).ToList();

            if (matches is not [var entry]
                || tombstoned!.Contains(EntryName.Of(entry))
                || !TryRead(entry, out var recorded, out _, out var revoked)
                || revoked
                || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(TokenSecret.Verifier(id, secret)),
                    Encoding.UTF8.GetBytes(entry.Password)))
            {
                return TokenCheck.Unknown;
            }

            info = recorded;

            return recorded.IsExpired(now) ? TokenCheck.Expired : TokenCheck.Valid;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    /// <summary>Each entry of the tokens group as the open vault holds it, read back.</summary>
    private List<TokenRecord> Records()
    {
        var tombstoned = _vault.ReadTombstoned();

        return
        [
            .. Entries(_vault.ReadEntries(includeReserved: true)).Select(entry =>
            {
                _ = TryRead(entry, out var info, out var lasts, out var revoked);
                return new TokenRecord(entry, info, lasts, revoked || tombstoned.Contains(EntryName.Of(entry)));
            }),
        ];
    }

    private static IEnumerable<VaultEntry> Entries(IEnumerable<VaultEntry> entries) =>
        entries.Where(entry => string.Equals(entry.GroupPath, ReservedGroups.Tokens, StringComparison.Ordinal));

    private static DateTimeOffset Truncated(DateTimeOffset now) =>
        new(now.UtcTicks - (now.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);

    internal static string Timestamp(DateTimeOffset time) =>
        time.UtcDateTime.ToString(_timestampFormat, CultureInfo.InvariantCulture);

    internal static bool TryTimestamp(string text, out DateTimeOffset time) =>
        DateTimeOffset.TryParseExact(
            text,
            _timestampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out time);

    private static string Notes(TokenInfo info, DateTimeOffset? revoked)
    {
        using var buffer = new MemoryStream();

        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("v", 1);
            json.WriteString("name", info.Name);
            json.WriteStartArray("scopes");
            foreach (var scope in info.Scopes)
            {
                json.WriteStringValue(scope.ToString());
            }

            json.WriteEndArray();
            json.WriteString("created", Timestamp(info.Created));
            json.WriteString("expires", Timestamp(info.Expires));
            json.WriteBoolean("allow_prod", info.AllowProd);

            if (revoked is { } at)
            {
                json.WriteString(_revokedProperty, Timestamp(at));
            }

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Reads a record of the tokens group back whole.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="info">What it records, its expiry shortened by one set on the entry.</param>
    /// <param name="lasts">The expiry its notes record, which an expiry set on the entry in one copy does not change in another.</param>
    /// <param name="revoked">Whether its notes say it was revoked.</param>
    /// <returns>Whether it reads back whole; an unrevoked record also needs its verifier.</returns>
    private static bool TryRead(VaultEntry entry, [NotNullWhen(true)] out TokenInfo? info, out DateTimeOffset lasts, out bool revoked)
    {
        info = null;
        lasts = default;
        revoked = false;

        if (entry.Title.Length != TokenSecret.IdLength)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(entry.Notes, _strict);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("v", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1
                || !TryString(root, "name", out var name) || !string.Equals(name, entry.Username, StringComparison.Ordinal)
                || !IsValidName(name, out _)
                || !TryString(root, "created", out var createdText) || !TryTimestamp(createdText, out var created)
                || !TryString(root, "expires", out var expiresText) || !TryTimestamp(expiresText, out var expires)
                || !root.TryGetProperty("allow_prod", out var allowProd) || allowProd.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !root.TryGetProperty("scopes", out var scopeArray) || scopeArray.ValueKind != JsonValueKind.Array
                || scopeArray.GetArrayLength() is 0 or > TokenScope.MaximumScopes)
            {
                return false;
            }

            var marked = root.TryGetProperty(_revokedProperty, out _);

            if (!marked && entry.Password.Length == 0)
            {
                return false;
            }

            List<TokenScope> scopes = [];

            foreach (var element in scopeArray.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String || !TokenScope.TryParse(element.GetString()!, out var scope, out _))
                {
                    return false;
                }

                scopes.Add(scope);
            }

            lasts = expires;

            // An expiry set on the entry in KeePassXC can only shorten the token's life.
            if (entry.Expires is { } entryExpires && entryExpires < expires)
            {
                expires = entryExpires;
            }

            info = new TokenInfo(entry.Title, name, scopes, created, expires, allowProd.GetBoolean());
            revoked = marked;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryString(JsonElement root, string name, [NotNullWhen(true)] out string? value)
    {
        value = root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;
        return value is not null;
    }

    /// <summary>One entry of the tokens group as it reads back.</summary>
    /// <param name="Entry">The entry.</param>
    /// <param name="Info">What it records, or null when it does not read back whole.</param>
    /// <param name="Lasts">The expiry its notes record, when <paramref name="Info"/> is set.</param>
    /// <param name="Revoked">Whether its notes say it was revoked or the vault holds a tombstone for its UUID (D-0544).</param>
    private sealed record TokenRecord(VaultEntry Entry, TokenInfo? Info, DateTimeOffset Lasts, bool Revoked);
}
