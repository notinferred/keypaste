using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Keypaste.Core.Agents;
using Keypaste.Core.Ownership;
using Keypaste.Infrastructure;

namespace Keypaste.Core.Browser;

/// <summary>One browser's pairing as this computer records it: its name, the KeePassXC-Browser build that paired and when, never the key.</summary>
/// <param name="Id">The pairing's name, which the extension keeps as its id.</param>
/// <param name="Identity">The build the browser named when it started keypaste to pair.</param>
/// <param name="Created">When it was paired.</param>
public sealed record BrowserPairing(string Id, BrowserIdentity Identity, DateTimeOffset Created);

/// <summary>What checking a pairing the extension presents came to.</summary>
public enum PairingCheck
{
    /// <summary>An open vault vouches for a pairing of this name, build and key.</summary>
    Valid = 0,

    /// <summary>No open vault vouches for a pairing of that name, build and key.</summary>
    Unknown = 1,

    /// <summary>This computer's pairings file could not be read.</summary>
    Unreadable = 2,
}

/// <summary>An unlocked vault that vouches for this computer's pairings, and how to save an edit to it under its owner's claim.</summary>
/// <param name="Vault">The unlocked vault.</param>
/// <param name="Save">Runs an edit on that vault and saves it.</param>
public sealed record PairingVault(Vault Vault, Func<Func<Vault, bool>, AgentSave> Save);

/// <summary>The names a pairing may be given, which the extension keeps as the pairing's id.</summary>
public static class PairingNames
{
    /// <summary>The longest name, which is the longest id the owner's pipe carries.</summary>
    public const int MaximumLength = 64;

    /// <summary>Why a name cannot be given to a new pairing, or null when it can.</summary>
    /// <param name="name">The name.</param>
    /// <param name="taken">The names the pairings already have.</param>
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

        return taken.Contains(name, StringComparer.Ordinal) ? "Another pairing has this name." : null;
    }

    /// <summary>A name to offer for a new pairing: the browser a build most likely runs in and this computer's name, made unique.</summary>
    /// <param name="identity">The build that asks to pair.</param>
    /// <param name="computer">This computer's name.</param>
    /// <param name="taken">The names the pairings already have.</param>
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

/// <summary>The six digits keypaste's extension and keypaste both show for a pairing, so the person pairs only the browser they are looking at.</summary>
/// <remarks>
/// Both sides derive it from the identification key the extension sends with <c>associate</c>, so KeePassXC-Browser's protocol
/// carries nothing new: the first four bytes of the key's SHA-256, read big-endian, modulo a million.
/// </remarks>
public static class PairingCode
{
    /// <summary>The code for an identification key, as <c>472 913</c>, or null when the text is not a key in base64.</summary>
    /// <param name="identificationKey">The key as the extension sends it.</param>
    /// <returns>The code, or null.</returns>
    public static string? Of(string identificationKey)
    {
        ArgumentNullException.ThrowIfNull(identificationKey);

        Span<byte> key = stackalloc byte[33];

        if (!Convert.TryFromBase64String(identificationKey, key, out var written) || written != 32)
        {
            return null;
        }

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(key[..32], hash);
        var digits = (BinaryPrimitives.ReadUInt32BigEndian(hash) % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        return $"{digits[..3]} {digits[3..]}";
    }
}

/// <summary>
/// This computer's browser pairings, one per browser for every vault open here, kept in <see cref="FileName"/> in keypaste's
/// home, each holding the SHA-256 of the extension's identification key, never the key (THREATS.md T-50).
/// </summary>
/// <remarks>
/// <para>
/// Any program the person runs can write the file, so a pairing counts only while an unlocked vault vouches for it: each record
/// carries, per vault, an HMAC-SHA256 under a key that vault keeps in <see cref="ReservedGroups.Keys"/>, over this home's random
/// identity, that vault's count of this home's pairing writes and the record. A record added by writing the file, or one a vault
/// never sealed, proves nothing; a file put back from before a write is refused by every vault that write counted up, since each
/// write counts up every unlocked vault first and seals under the new count after.
/// </para>
/// <para>
/// A write seals every pairing any unlocked vault vouches for under every unlocked vault, so a vault unlocked beside one that
/// vouches takes the pairings over at the next write. Removing a pairing removes it for every vault. A vault's first write here
/// also removes the pairings earlier versions kept inside it, in <see cref="ReservedGroups.Browsers"/>, which nothing reads now.
/// </para>
/// </remarks>
/// <param name="path">The pairings file.</param>
public sealed class BrowserPairings(string path)
{
    /// <summary>The pairings file's name in keypaste's home.</summary>
    public const string FileName = "browsers.json";

    /// <summary>The title of the vault's record that holds its sealing key.</summary>
    internal const string KeyTitle = "browser-pairings";

    /// <summary>The record's field that holds the key, as base64.</summary>
    internal const string KeyField = "key";

    /// <summary>The field of each home's own record that counts that home's pairing writes, as a decimal number.</summary>
    internal const string CountField = "count";

    private const string _timestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    private static readonly TimeSpan _lockWait = TimeSpan.FromSeconds(5);

    // A property named twice would leave the reader to pick one, and the writer may have meant the other.
    private static readonly JsonDocumentOptions _strict = new() { AllowDuplicateProperties = false };

    /// <summary>The pairings file.</summary>
    public string Path { get; } = System.IO.Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));

    /// <summary>The pairings file in a keypaste home.</summary>
    /// <param name="home">keypaste's home directory.</param>
    /// <returns>The pairings.</returns>
    public static BrowserPairings In(string home) => new(System.IO.Path.Combine(home, FileName));

    /// <summary>This home's identity, made and written with an empty file when there is none or the file says nothing keypaste can read.</summary>
    /// <returns>32 lowercase hex digits, or null when the file can be neither read nor written.</returns>
    public string? HomeId()
    {
        switch (Load(out var held))
        {
            case Loaded.Read:
                return held.Home;
            case Loaded.Unreadable:
                return null;
        }

        using var lease = Lease(out _);

        if (lease is null)
        {
            return null;
        }

        if (Load(out held) == Loaded.Read)
        {
            return held.Home;
        }

        var made = new Held(NewHome(), []);
        return TryWrite(made, out _) ? made.Home : null;
    }

    /// <summary>The pairings any of the vaults vouches for, by name.</summary>
    /// <param name="vaults">The unlocked vaults.</param>
    /// <returns>The pairings; none when the file cannot be read.</returns>
    public IReadOnlyList<BrowserPairing> List(IReadOnlyList<Vault> vaults)
    {
        ArgumentNullException.ThrowIfNull(vaults);

        return Load(out var held) == Loaded.Read
            ? [.. Vouched(held, vaults).Select(stored => stored.Pairing).OrderBy(pairing => pairing.Id, StringComparer.Ordinal).ThenBy(pairing => pairing.Created)]
            : [];
    }

    /// <summary>Checks a pairing the extension presents.</summary>
    /// <param name="vaults">The unlocked vaults, any of which may vouch for it.</param>
    /// <param name="id">The pairing's name, as the extension keeps it.</param>
    /// <param name="identity">The build the browser named when it started keypaste.</param>
    /// <param name="identificationKey">The key the extension presents, in base64.</param>
    /// <param name="pairing">The pairing proven, when it is.</param>
    /// <returns>What the check came to; only <see cref="PairingCheck.Valid"/> proves the pairing.</returns>
    /// <remarks>An unknown name, another build's pairing and a wrong key all read <see cref="PairingCheck.Unknown"/>, so a refusal never says which.</remarks>
    public PairingCheck Verify(IReadOnlyList<Vault> vaults, string id, BrowserIdentity identity, string identificationKey, [NotNullWhen(true)] out BrowserPairing? pairing)
    {
        ArgumentNullException.ThrowIfNull(vaults);
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(identificationKey);

        pairing = null;

        if (Load(out var held) == Loaded.Unreadable)
        {
            return PairingCheck.Unreadable;
        }

        if (KeyHash(identificationKey) is not { } presented)
        {
            return PairingCheck.Unknown;
        }

        pairing = Vouched(held, vaults)
            .FirstOrDefault(stored => string.Equals(stored.Pairing.Id, id, StringComparison.Ordinal)
                && ReferenceEquals(stored.Pairing.Identity, identity)
                && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(stored.KeyHash), Encoding.ASCII.GetBytes(presented)))?.Pairing;

        return pairing is null ? PairingCheck.Unknown : PairingCheck.Valid;
    }

    /// <summary>Whether every one of the vaults itself vouches for a pairing, so none needs to take it over.</summary>
    /// <param name="vaults">The unlocked vaults.</param>
    /// <param name="pairing">The pairing.</param>
    /// <returns>Whether each does.</returns>
    public bool AllVouch(IReadOnlyList<Vault> vaults, BrowserPairing pairing)
    {
        ArgumentNullException.ThrowIfNull(vaults);
        ArgumentNullException.ThrowIfNull(pairing);

        return Load(out var held) == Loaded.Read && vaults.All(vault => Vouched(held, [vault]).Any(stored => stored.Pairing == pairing));
    }

    /// <summary>Records a new pairing for every unlocked vault, the first of which must save.</summary>
    /// <param name="vaults">The unlocked vaults, the one asked first.</param>
    /// <param name="id">Its name, which <see cref="PairingNames.Problem"/> accepts.</param>
    /// <param name="identity">The build that asked to pair.</param>
    /// <param name="identificationKey">The extension's identification key, in base64, of which only the hash is kept.</param>
    /// <param name="now">When it is paired.</param>
    /// <param name="problem">Why nothing was recorded, or empty.</param>
    /// <returns>Whether the pairing was recorded.</returns>
    public bool TryAdd(IReadOnlyList<PairingVault> vaults, string id, BrowserIdentity identity, string identificationKey, DateTimeOffset now, out string problem)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(identificationKey);

        if (KeyHash(identificationKey) is not { } hash)
        {
            problem = "the extension's key is not a public key";
            return false;
        }

        var added = new Stored(new BrowserPairing(id, identity, Truncated(now)), hash, new Dictionary<string, string>(StringComparer.Ordinal));

        return TryChange(
            vaults,
            vouched =>
            {
                if (PairingNames.Problem(id, vouched.Select(stored => stored.Pairing.Id)) is { } invalid)
                {
                    return (null, invalid);
                }

                return ([.. vouched, added], string.Empty);
            },
            out problem);
    }

    /// <summary>Removes a pairing for every vault, counting up every unlocked vault, the first of which must save.</summary>
    /// <param name="vaults">The unlocked vaults, the one asked first.</param>
    /// <param name="id">The pairing's name.</param>
    /// <param name="removed">The pairing removed, when there was one.</param>
    /// <param name="problem">Why nothing was removed, or empty.</param>
    /// <returns>Whether a pairing was removed.</returns>
    public bool TryRemove(IReadOnlyList<PairingVault> vaults, string id, [NotNullWhen(true)] out BrowserPairing? removed, out string problem)
    {
        ArgumentNullException.ThrowIfNull(id);

        BrowserPairing? found = null;

        var changed = TryChange(
            vaults,
            vouched =>
            {
                found = vouched.FirstOrDefault(stored => string.Equals(stored.Pairing.Id, id, StringComparison.Ordinal))?.Pairing;
                return found is null ? (null, "no pairing has this name any more") : ([.. vouched.Where(stored => stored.Pairing != found)], string.Empty);
            },
            out problem);

        removed = changed ? found : null;
        return removed is not null;
    }

    /// <summary>Seals every pairing any unlocked vault vouches for under each of them, so a vault unlocked beside one that vouches takes them over.</summary>
    /// <param name="vaults">The unlocked vaults, the one asking first.</param>
    /// <param name="problem">Why nothing was sealed, or empty.</param>
    /// <returns>Whether the file now holds the seals.</returns>
    public bool TryAdopt(IReadOnlyList<PairingVault> vaults, out string problem) =>
        TryChange(vaults, vouched => (vouched, string.Empty), out problem);

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

    /// <summary>The title of a home's own record of its count.</summary>
    /// <remarks>A record of its own per home, as the agent rules' count is, so two homes sharing a synced vault never count each other's writes.</remarks>
    internal static string CountTitle(string home) => $"{KeyTitle}-{home}";

    /// <summary>The seal a vault's key gives a record for a home at a count.</summary>
    internal static string Seal(byte[] key, string home, ulong count, BrowserPairing pairing, string keyHash)
    {
        using var buffer = new MemoryStream();

        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartArray();
            json.WriteStringValue("keypaste browsers.json 1");
            json.WriteStringValue(home);
            json.WriteStringValue(count.ToString(CultureInfo.InvariantCulture));
            json.WriteStringValue(pairing.Id);
            json.WriteStringValue(pairing.Identity.Caller);
            json.WriteStringValue(Timestamp(pairing.Created));
            json.WriteStringValue(keyHash);
            json.WriteEndArray();
        }

        return Convert.ToHexStringLower(HMACSHA256.HashData(key, buffer.ToArray()));
    }

    /// <summary>The name a vault's seals go under in the file, which names the vault's key without giving it.</summary>
    internal static string SealId(byte[] key) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes("keypaste browsers.json id")))[..32];

    private static IEnumerable<Stored> Vouched(Held held, IReadOnlyList<Vault> vaults)
    {
        var vouchers = vaults.Select(vault => Voucher(vault, held.Home)).OfType<(byte[] Key, ulong Count)>().ToList();

        return held.Pairings.Where(stored => vouchers.Any(voucher => Holds(stored, held.Home, voucher.Key, voucher.Count)));
    }

    private static bool Holds(Stored stored, string home, byte[] key, ulong count) =>
        stored.Seals.TryGetValue(SealId(key), out var seal)
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Seal(key, home, count, stored.Pairing, stored.KeyHash)), Encoding.ASCII.GetBytes(seal));

    /// <summary>A vault's key and its count of this home's writes, or null when it keeps no key or its count cannot be read.</summary>
    private static (byte[] Key, ulong Count)? Voucher(Vault vault, string home) =>
        KeyOf(vault) is { } key && CountOf(vault, home) is { } count ? (key, count) : null;

    private static byte[]? KeyOf(Vault vault)
    {
        try
        {
            return vault.ReadRecords(ReservedGroups.Keys, KeyField).FirstOrDefault(record => string.Equals(record.Title, KeyTitle, StringComparison.Ordinal))?.Value is { } text
                && Convert.TryFromBase64String(text, new byte[32], out var length) && length == 32
                ? Convert.FromBase64String(text)
                : null;
        }
        catch (VaultException)
        {
            return null;
        }
    }

    /// <summary>A vault's count of this home's writes: none before the first, or null when it cannot be read.</summary>
    private static ulong? CountOf(Vault vault, string home)
    {
        try
        {
            var title = CountTitle(home);
            var text = vault.ReadRecords(ReservedGroups.Keys, CountField).FirstOrDefault(record => string.Equals(record.Title, title, StringComparison.Ordinal))?.Value;

            return text is not { Length: > 0 }
                ? 0
                : ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ? count : null;
        }
        catch (VaultException)
        {
            return null;
        }
    }

    /// <summary>
    /// Under the file's lease, applies an edit to the pairings the vaults vouch for, counts up each vault, the first of which must
    /// save, and writes the file sealed under each vault that saved; a vault that did not keeps the seals it had.
    /// </summary>
    private bool TryChange(IReadOnlyList<PairingVault> vaults, Func<IReadOnlyList<Stored>, (IReadOnlyList<Stored>? Kept, string Problem)> edit, out string problem)
    {
        ArgumentNullException.ThrowIfNull(vaults);

        if (vaults.Count == 0)
        {
            problem = "the vault is locked";
            return false;
        }

        using var lease = Lease(out problem);

        if (lease is null)
        {
            return false;
        }

        var held = Load(out var read) switch
        {
            Loaded.Read => read,
            Loaded.Unreadable => null,
            _ => new Held(NewHome(), []),
        };

        if (held is null)
        {
            problem = $"{Path} could not be read";
            return false;
        }

        var vouched = Vouched(held, [.. vaults.Select(vault => vault.Vault)]).ToList();
        var (kept, refused) = edit(vouched);

        if (kept is null)
        {
            problem = refused;
            return false;
        }

        var outgoing = vouched.Except(kept).ToHashSet();
        List<Stored> pairings = [.. held.Pairings.Where(stored => !outgoing.Contains(stored)).Concat(kept.Except(held.Pairings))];

        for (var index = 0; index < vaults.Count; index++)
        {
            if (Counted(vaults[index], held.Home, out var why) is not { } voucher)
            {
                if (index == 0)
                {
                    problem = why;
                    return false;
                }

                continue;
            }

            var id = SealId(voucher.Key);

            foreach (var stored in pairings)
            {
                stored.Seals.Remove(id);

                if (kept.Contains(stored))
                {
                    stored.Seals[id] = Seal(voucher.Key, held.Home, voucher.Count, stored.Pairing, stored.KeyHash);
                }
            }
        }

        return TryWrite(held with { Pairings = pairings }, out problem);
    }

    /// <summary>Counts up a vault's writes for this home, making its key and retiring the pairings it kept itself in the same save, and returns the key and the new count.</summary>
    private static (byte[] Key, ulong Count)? Counted(PairingVault vault, string home, out string problem)
    {
        try
        {
            var key = KeyOf(vault.Vault);
            var made = key is null ? RandomNumberGenerator.GetBytes(32) : null;

            if (CountOf(vault.Vault, home) is not { } count || count == ulong.MaxValue)
            {
                problem = "the vault's count of pairing changes cannot be read";
                return null;
            }

            var counted = (count + 1).ToString(CultureInfo.InvariantCulture);
            var saved = vault.Save(open =>
            {
                var changed = made is not null && open.WriteRecord(new EntryName(ReservedGroups.Keys, KeyTitle), AcafIds.Agent, KeyField, Convert.ToBase64String(made));
                changed |= open.WriteRecord(new EntryName(ReservedGroups.Keys, CountTitle(home)), AcafIds.Agent, CountField, counted);
                return RetireOwnPairings(open) | changed;
            });

            if (!saved.Holds)
            {
                problem = $"the vault was not saved: {saved.Reason}";
                return null;
            }

            problem = string.Empty;
            return (key ?? made!, count + 1);
        }
        catch (ObjectDisposedException)
        {
            problem = "the vault is locked";
            return null;
        }
    }

    /// <summary>Purges the pairings an earlier version kept in the vault itself, which nothing reads now, past the recycle bin.</summary>
    private static bool RetireOwnPairings(Vault open)
    {
        var retired = false;

        foreach (var title in open.ReadEntries(includeReserved: true)
            .Where(entry => string.Equals(entry.GroupPath, ReservedGroups.Browsers, StringComparison.Ordinal))
            .Select(entry => entry.Title)
            .Distinct(StringComparer.Ordinal)
            .ToList())
        {
            try
            {
                if (open.RemoveEntry(new EntryName(ReservedGroups.Browsers, title), out var recycled) == DeletionOutcome.Recycled)
                {
                    open.PurgeRecycled(recycled);
                }

                retired = true;
            }
            catch (VaultException)
            {
                // Two entries of one title, which only another program makes: left for KeePassXC, and read by nothing.
            }
        }

        return retired;
    }

    /// <summary>Holds the file's lock, so two owner processes on this computer never write over each other's change.</summary>
    private FileStream? Lease(out string problem)
    {
        var directory = System.IO.Path.GetDirectoryName(Path)!;
        var deadline = Environment.TickCount64 + (long)_lockWait.TotalMilliseconds;

        while (true)
        {
            try
            {
                VaultClaim.CreateDirectory(directory);
                problem = string.Empty;
                return new FileStream(Path + ".lock", VaultClaim.Options(FileMode.OpenOrCreate, FileAccess.Write, FileShare.None));
            }
            catch (IOException) when (Environment.TickCount64 < deadline)
            {
                Thread.Sleep(50);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problem = $"{Path} is being changed by another keypaste, or cannot be locked: {ex.Message}";
                return null;
            }
        }
    }

    private enum Loaded
    {
        Read,
        Missing,
        Garbled,
        Unreadable,
    }

    /// <summary>Reads the file; a file keypaste cannot parse is garbled, which a write replaces, since nothing in it can be proven.</summary>
    private Loaded Load(out Held held)
    {
        held = new Held(string.Empty, []);
        byte[] bytes;

        try
        {
            bytes = File.ReadAllBytes(Path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Loaded.Missing;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Loaded.Unreadable;
        }

        try
        {
            using var document = JsonDocument.Parse(bytes, _strict);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("v", out var version) || !version.TryGetInt32(out var v) || v != 1
                || !root.TryGetProperty("home", out var home) || home.GetString() is not { Length: 32 } homeId || !homeId.All(char.IsAsciiHexDigitLower)
                || !root.TryGetProperty("pairings", out var pairings) || pairings.ValueKind != JsonValueKind.Array)
            {
                return Loaded.Garbled;
            }

            held = new Held(homeId, [.. pairings.EnumerateArray().Select(Read).OfType<Stored>()]);
            return Loaded.Read;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return Loaded.Garbled;
        }
    }

    /// <summary>One record, or null when it does not read back whole.</summary>
    private static Stored? Read(JsonElement record)
    {
        try
        {
            return ReadWhole(record);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static Stored? ReadWhole(JsonElement record)
    {
        if (record.ValueKind != JsonValueKind.Object
            || !record.TryGetProperty("name", out var name) || name.GetString() is not { } id || PairingNames.Problem(id, []) is not null
            || !record.TryGetProperty("identity", out var caller) || BrowserIdentity.Of(caller.GetString()) is not { } identity
            || !record.TryGetProperty("created", out var created)
            || !DateTimeOffset.TryParseExact(created.GetString(), _timestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var createdAt)
            || !record.TryGetProperty("key", out var key) || key.GetString() is not { Length: 64 } hash || !hash.All(char.IsAsciiHexDigitLower)
            || !record.TryGetProperty("seals", out var seals) || seals.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var sealed_ = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var seal in seals.EnumerateObject())
        {
            if (seal.Value.GetString() is not { Length: 64 } value || seal.Name.Length != 32)
            {
                return null;
            }

            sealed_[seal.Name] = value;
        }

        return new Stored(new BrowserPairing(id, identity, createdAt), hash, sealed_);
    }

    private bool TryWrite(Held held, out string problem)
    {
        using var buffer = new MemoryStream();

        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteNumber("v", 1);
            json.WriteString("home", held.Home);
            json.WriteStartArray("pairings");

            foreach (var stored in held.Pairings)
            {
                json.WriteStartObject();
                json.WriteString("name", stored.Pairing.Id);
                json.WriteString("identity", stored.Pairing.Identity.Caller);
                json.WriteString("created", Timestamp(stored.Pairing.Created));
                json.WriteString("key", stored.KeyHash);
                json.WriteStartObject("seals");

                foreach (var (id, seal) in stored.Seals.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    json.WriteString(id, seal);
                }

                json.WriteEndObject();
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        try
        {
            AtomicFile.Write(Path, buffer.ToArray());
            problem = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = $"{Path} could not be written: {ex.Message}";
            return false;
        }
    }

    private static string NewHome() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    private static string Timestamp(DateTimeOffset at) => at.UtcDateTime.ToString(_timestampFormat, CultureInfo.InvariantCulture);

    private static DateTimeOffset Truncated(DateTimeOffset now) =>
        new(now.UtcTicks - (now.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);

    /// <summary>What the file says: this home's identity and each record as read.</summary>
    private sealed record Held(string Home, IReadOnlyList<Stored> Pairings);

    /// <summary>A record: the pairing, its key's hash and each vault's seal by that vault's seal id.</summary>
    private sealed record Stored(BrowserPairing Pairing, string KeyHash, Dictionary<string, string> Seals);
}
