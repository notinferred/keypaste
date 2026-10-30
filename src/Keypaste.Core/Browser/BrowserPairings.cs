using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace Keypaste.Core.Browser;

/// <summary>One browser's pairing as the vault records it: its name, the KeePassXC-Browser build that paired and when, never the key.</summary>
/// <param name="Id">The pairing's name, which the extension keeps as its id.</param>
/// <param name="Identity">The build the browser named when it started keypaste to pair.</param>
/// <param name="Created">When it was paired.</param>
public sealed record BrowserPairing(string Id, BrowserIdentity Identity, DateTimeOffset Created);

/// <summary>What checking a pairing the extension presents came to.</summary>
public enum PairingCheck
{
    /// <summary>The vault as its file holds it records this pairing for this build and this key.</summary>
    Valid = 0,

    /// <summary>No pairing of that name records this build and this key, or its record does not read back whole.</summary>
    Unknown = 1,

    /// <summary>The open vault holds a change not yet saved, so its file decides nothing.</summary>
    Unsaved = 2,

    /// <summary>Another program changed the vault's file since it was opened.</summary>
    ChangedOnDisk = 3,

    /// <summary>The vault could not be compared with its file.</summary>
    Unreadable = 4,
}

/// <summary>The names a pairing may be given, which the extension keeps as the pairing's id.</summary>
public static class PairingNames
{
    /// <summary>The longest name, which is the longest id the owner's pipe carries.</summary>
    public const int MaximumLength = 64;

    /// <summary>Why a name cannot be given to a new pairing, or null when it can.</summary>
    /// <param name="name">The name.</param>
    /// <param name="taken">The names the vault's pairings already have.</param>
    /// <returns>keypaste's own words for the problem, or null.</returns>
    public static string? Problem(string name, IEnumerable<string> taken)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(taken);

        if (!VaultNameRules.IsValidName(name, "name", out var error))
        {
            return error;
        }

        if (name.Length > MaximumLength)
        {
            return string.Create(CultureInfo.InvariantCulture, $"the name can be at most {MaximumLength} characters");
        }

        if (name.Any(c => char.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.OtherNotAssigned))
        {
            return "the name cannot contain invisible or unassigned characters";
        }

        return taken.Contains(name, StringComparer.Ordinal) ? "another pairing already has this name" : null;
    }

    /// <summary>A name to offer for a new pairing: the browser a build most likely runs in and this computer's name, made unique.</summary>
    /// <param name="identity">The build that asks to pair.</param>
    /// <param name="computer">This computer's name.</param>
    /// <param name="taken">The names the vault's pairings already have.</param>
    /// <returns>A name <see cref="Problem"/> accepts.</returns>
    public static string Suggested(BrowserIdentity identity, string computer, IReadOnlyCollection<string> taken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(computer);
        ArgumentNullException.ThrowIfNull(taken);

        var place = new string([.. computer.Where(char.IsAsciiLetterOrDigit).Take(32)]);
        var stem = place.Length == 0 ? identity.Browser : $"{identity.Browser} on {place}";
        var name = stem;

        for (var n = 2; Problem(name, taken) is not null; n++)
        {
            name = string.Create(CultureInfo.InvariantCulture, $"{stem} {n}");
        }

        return name;
    }
}

/// <summary>
/// Browsers' pairings kept in a vault, one entry each in <see cref="ReservedGroups.Browsers"/>: titled with the pairing's name,
/// its password the SHA-256 of the extension's identification key, its notes the build that paired and when (D-0633).
/// </summary>
/// <remarks>
/// <para>
/// The extension presents the same identification key to prove a pairing, so it is a bearer key (THREATS.md T-50): the vault
/// keeps only its hash, which proves a key presented again and gives nobody who reads the vault a key to present. Nothing of
/// KeePassXC's own browser data is read or written here, so its pairings, made under its own prompts, authorize nothing in
/// keypaste (D-0548).
/// </para>
/// <para>
/// A check reads the vault as its file holds it, as a token is checked (D-0317), so a pairing added in memory and never saved,
/// or one another program removed, proves nothing. Removing a pairing purges it past the recycle bin, so its tombstone
/// carries the removal through a merge with a copy that still holds it. Writing changes the open vault and does not save it.
/// </para>
/// </remarks>
/// <param name="vault">The open vault.</param>
public sealed class BrowserPairings(Vault vault)
{
    private const string _timestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    // A property named twice would leave the reader to pick one, and the writer may have meant the other (D-0325).
    private static readonly JsonDocumentOptions _strict = new() { AllowDuplicateProperties = false };

    private readonly Vault _vault = vault ?? throw new ArgumentNullException(nameof(vault));

    /// <summary>Every pairing the open vault holds that reads back whole, by name; one the vault also holds a tombstone for is left out.</summary>
    /// <returns>The pairings.</returns>
    public IReadOnlyList<BrowserPairing> List()
    {
        var tombstoned = _vault.ReadTombstoned();

        return
        [
            .. Entries(_vault.ReadEntries(includeReserved: true))
                .Where(entry => !tombstoned.Contains(EntryName.Of(entry)))
                .Select(entry => TryRead(entry, out var pairing, out _) ? pairing : null)
                .OfType<BrowserPairing>()
                .OrderBy(pairing => pairing.Id, StringComparer.Ordinal)
                .ThenBy(pairing => pairing.Created),
        ];
    }

    /// <summary>Records a new pairing in the open vault, which the caller saves.</summary>
    /// <param name="id">Its name, which <see cref="PairingNames.Problem"/> accepts.</param>
    /// <param name="identity">The build that asked to pair.</param>
    /// <param name="identificationKey">The extension's identification key, in base64, of which only the hash is kept.</param>
    /// <param name="now">When it is paired.</param>
    /// <param name="problem">Why nothing was recorded, or empty.</param>
    /// <returns>Whether the pairing was recorded.</returns>
    public bool TryAdd(string id, BrowserIdentity identity, string identificationKey, DateTimeOffset now, out string problem)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(identificationKey);

        if (PairingNames.Problem(id, List().Select(pairing => pairing.Id)) is { } invalid)
        {
            problem = invalid;
            return false;
        }

        if (KeyHash(identificationKey) is not { } hash)
        {
            problem = "the extension's key is not a public key";
            return false;
        }

        _vault.AddEntry(new VaultEntry
        {
            GroupPath = ReservedGroups.Browsers,
            Title = id,
            Password = hash,
            Notes = Notes(identity, Truncated(now)),
        });

        problem = string.Empty;
        return true;
    }

    /// <summary>Checks a pairing the extension presents against the vault as its file holds it.</summary>
    /// <param name="id">The pairing's name, as the extension keeps it.</param>
    /// <param name="identity">The build the browser named when it started keypaste.</param>
    /// <param name="identificationKey">The key the extension presents, in base64.</param>
    /// <returns>What the check came to; only <see cref="PairingCheck.Valid"/> proves the pairing.</returns>
    /// <remarks>An unknown name, another build's pairing and a wrong key all read <see cref="PairingCheck.Unknown"/>, so a refusal never says which.</remarks>
    public PairingCheck Verify(string id, BrowserIdentity identity, string identificationKey)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(identificationKey);

        switch (_vault.ReadSavedWithTombstones(out var saved, out var tombstoned))
        {
            case SavedRead.Current:
                break;
            case SavedRead.Unsaved:
                return PairingCheck.Unsaved;
            case SavedRead.ChangedOnDisk:
                return PairingCheck.ChangedOnDisk;
            default:
                return PairingCheck.Unreadable;
        }

        if (KeyHash(identificationKey) is not { } presented)
        {
            return PairingCheck.Unknown;
        }

        var proven = Entries(saved!)
            .Where(entry => string.Equals(entry.Title, id, StringComparison.Ordinal) && !tombstoned!.Contains(EntryName.Of(entry)))
            .Any(entry => TryRead(entry, out var pairing, out var hash)
                && ReferenceEquals(pairing.Identity, identity)
                && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(presented)));

        return proven ? PairingCheck.Valid : PairingCheck.Unknown;
    }

    /// <summary>Removes a pairing from the open vault for good, which the caller saves.</summary>
    /// <param name="id">The pairing's name.</param>
    /// <param name="removed">The pairing removed, when there was one.</param>
    /// <returns>Whether a pairing was removed.</returns>
    /// <exception cref="VaultException">Two entries of the browsers group have that name, which only another program makes.</exception>
    public bool Remove(string id, [NotNullWhen(true)] out BrowserPairing? removed)
    {
        ArgumentNullException.ThrowIfNull(id);

        removed = List().FirstOrDefault(pairing => string.Equals(pairing.Id, id, StringComparison.Ordinal));

        if (removed is null)
        {
            return false;
        }

        if (_vault.RemoveEntry(new EntryName(ReservedGroups.Browsers, id), out var recycled) == DeletionOutcome.Recycled)
        {
            _vault.PurgeRecycled(recycled);
        }

        return true;
    }

    /// <summary>The SHA-256 of an identification key's 32 bytes in lowercase hex, or null when the text is not a key in base64.</summary>
    /// <param name="identificationKey">The key as the extension sends it.</param>
    /// <returns>The hash, or null.</returns>
    public static string? KeyHash(string identificationKey)
    {
        ArgumentNullException.ThrowIfNull(identificationKey);

        Span<byte> key = stackalloc byte[33];

        return Convert.TryFromBase64String(identificationKey, key, out var written) && written == 32
            ? Convert.ToHexStringLower(SHA256.HashData(key[..32]))
            : null;
    }

    private static IEnumerable<VaultEntry> Entries(IEnumerable<VaultEntry> entries) =>
        entries.Where(entry => string.Equals(entry.GroupPath, ReservedGroups.Browsers, StringComparison.Ordinal));

    private static bool TryRead(VaultEntry entry, [NotNullWhen(true)] out BrowserPairing? pairing, out string hash)
    {
        pairing = null;
        hash = entry.Password;

        if (hash.Length != 64 || !hash.All(char.IsAsciiHexDigitLower))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(entry.Notes, _strict);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("v", out var version) || !version.TryGetInt32(out var v) || v != 1
                || !root.TryGetProperty("identity", out var caller) || caller.ValueKind != JsonValueKind.String
                || BrowserIdentity.Of(caller.GetString()) is not { } identity
                || !root.TryGetProperty("created", out var created) || created.ValueKind != JsonValueKind.String
                || !DateTimeOffset.TryParseExact(
                    created.GetString(),
                    _timestampFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var createdAt))
            {
                return false;
            }

            pairing = new BrowserPairing(entry.Title, identity, createdAt);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Notes(BrowserIdentity identity, DateTimeOffset created)
    {
        using var buffer = new MemoryStream();

        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("v", 1);
            json.WriteString("identity", identity.Caller);
            json.WriteString("created", created.UtcDateTime.ToString(_timestampFormat, CultureInfo.InvariantCulture));
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static DateTimeOffset Truncated(DateTimeOffset now) =>
        new(now.UtcTicks - (now.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
}
