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
/// Creating and revoking change the open vault and do not save it. Revoking deletes the entry and
/// purges it from the recycle bin, so restoring from the bin cannot bring a token back; a vault
/// restored from a backup can.
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
    /// <returns>The tokens that read back whole; a damaged entry is not listed.</returns>
    public IReadOnlyList<TokenInfo> List() =>
        [.. Entries(_vault.ReadEntries())
            .Select(entry => TryRead(entry, out var info) ? info : null)
            .OfType<TokenInfo>()
            .OrderBy(info => info.Name, StringComparer.Ordinal)];

    /// <summary>Mints a token and records it in the open vault, which the caller saves.</summary>
    /// <param name="name">What to call it, unique among the vault's tokens.</param>
    /// <param name="scopes">What it may read.</param>
    /// <param name="ttl">How long it lasts, from <see cref="MinimumLifetime"/> to <see cref="MaximumLifetime"/>.</param>
    /// <param name="allowProd">Whether a scope may name a protected profile, which then asks the person live.</param>
    /// <param name="now">When it is minted.</param>
    /// <param name="info">What was recorded.</param>
    /// <param name="token">The token, to be shown once and never stored.</param>
    /// <param name="error">Why nothing was minted, or empty.</param>
    /// <returns>Whether a token was minted.</returns>
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
            error = $"{protectedScope} names a protected profile; --allow-prod lets the token ask you live for it, every time";
            return false;
        }

        var entries = Entries(_vault.ReadEntries()).ToList();

        if (entries.Any(entry => string.Equals(entry.Username, name, StringComparison.Ordinal)))
        {
            error = $"a token named '{name}' already exists";
            return false;
        }

        token = TokenSecret.New(out var id, out var secret);

        while (entries.Any(entry => string.Equals(entry.Title, id, StringComparison.Ordinal)))
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
                Notes = Notes(info),
            });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }

        error = string.Empty;
        return true;
    }

    /// <summary>Deletes a token from the open vault for good, which the caller saves.</summary>
    /// <param name="nameOrId">The token's name, or its id when no token has that name.</param>
    /// <returns>Whether a token was deleted.</returns>
    public bool Revoke(string nameOrId)
    {
        ArgumentNullException.ThrowIfNull(nameOrId);

        var entries = Entries(_vault.ReadEntries()).ToList();
        var named = entries.Where(entry => string.Equals(entry.Username, nameOrId, StringComparison.Ordinal)).ToList();

        return Delete(named.Count > 0 ? named : WithId(entries, nameOrId));
    }

    /// <summary>Deletes the token with this id from the open vault for good, which the caller saves.</summary>
    /// <param name="id">The token's id, never taken as a name.</param>
    /// <returns>Whether a token was deleted.</returns>
    public bool RevokeId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        return Delete(WithId(Entries(_vault.ReadEntries()), id));
    }

    private static List<VaultEntry> WithId(IEnumerable<VaultEntry> entries, string id) =>
        [.. entries.Where(entry => string.Equals(entry.Title, id, StringComparison.Ordinal))];

    private bool Delete(List<VaultEntry> doomed)
    {
        foreach (var entry in doomed)
        {
            if (_vault.RemoveEntry(EntryName.Of(entry), out var recycled) == DeletionOutcome.Recycled)
            {
                _vault.PurgeRecycled(recycled);
            }
        }

        return doomed.Count > 0;
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
            switch (_vault.ReadSaved(out var saved))
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
                || !TryRead(entry, out var recorded)
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

    private static string Notes(TokenInfo info)
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
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static bool TryRead(VaultEntry entry, [NotNullWhen(true)] out TokenInfo? info)
    {
        info = null;

        if (entry.Title.Length != TokenSecret.IdLength || entry.Password.Length == 0)
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

            List<TokenScope> scopes = [];

            foreach (var element in scopeArray.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String || !TokenScope.TryParse(element.GetString()!, out var scope, out _))
                {
                    return false;
                }

                scopes.Add(scope);
            }

            // An expiry set on the entry in KeePassXC can only shorten the token's life.
            if (entry.Expires is { } entryExpires && entryExpires < expires)
            {
                expires = entryExpires;
            }

            info = new TokenInfo(entry.Title, name, scopes, created, expires, allowProd.GetBoolean());
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
}
