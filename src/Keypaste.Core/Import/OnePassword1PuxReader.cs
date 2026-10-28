using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace Keypaste.Core.Import;

/// <summary>
/// Reads 1Password's 1PUX export: a ZIP archive holding <c>export.data</c>, its accounts' vaults and items in JSON, and
/// under <c>files/</c> the files its items hold (D-0582).
/// </summary>
/// <remarks>
/// The archive is read in memory from the buffer <see cref="ExportImport"/> hands over, and refused before anything is
/// unpacked when it lists more entries than keypaste reads, an entry's path leaves the archive or two entries share one,
/// or it would unpack to more than keypaste reads (D-0583). Each vault becomes a group, each section's fields custom
/// fields named as KeePassXC's own 1PUX import names them (D-0584), and what lands nowhere is named with why (D-0585).
/// </remarks>
internal sealed class OnePassword1PuxReader : IExportReader
{
    /// <summary>The most entries an archive may list, counted from its end records before it is opened.</summary>
    internal const int MaximumEntries = 10_000;

    /// <summary>The most bytes <c>export.data</c> may unpack to.</summary>
    internal const int MaximumDataBytes = 128 * 1024 * 1024;

    /// <summary>The most bytes everything read may unpack to: <c>export.data</c> and each file an item holds.</summary>
    internal const long MaximumUnpackedBytes = 512L * 1024 * 1024;

    private const string _dataEntry = "export.data";
    private const string _attributesEntry = "export.attributes";
    private const string _filesFolder = "files/";
    private const string _favoriteTag = "Favorite";
    private const string _archivedTag = "Archived";
    private const string _source = "1Password";
    private const uint _localHeader = 0x04034b50;
    private const uint _centralHeader = 0x02014b50;
    private const uint _endRecord = 0x06054b50;
    private const uint _zip64Locator = 0x07064b50;
    private const uint _zip64Record = 0x06064b50;

    private static readonly JsonDocumentOptions _strict = new() { AllowDuplicateProperties = false };

    /// <summary>1Password's categories by the id an item names its own by, as 1Password names them.</summary>
    private static readonly Dictionary<string, string> _categories = new(StringComparer.Ordinal)
    {
        ["001"] = "Login",
        ["002"] = "Credit Card",
        ["003"] = "Secure Note",
        ["004"] = "Identity",
        ["005"] = "Password",
        ["006"] = "Document",
        ["100"] = "Software License",
        ["101"] = "Bank Account",
        ["102"] = "Database",
        ["103"] = "Driver License",
        ["104"] = "Outdoor License",
        ["105"] = "Membership",
        ["106"] = "Passport",
        ["107"] = "Reward Program",
        ["108"] = "Social Security Number",
        ["109"] = "Wireless Router",
        ["110"] = "Server",
        ["111"] = "Email Account",
        ["112"] = "API Credential",
        ["113"] = "Medical Record",
        ["114"] = "SSH Key",
    };

    // The members each object holds that these rules read, or leave as 1Password's own reckoning: a password's strength
    // (ps, pbe, pgrng), a subtitle it derives, and how a field is shown, typed or generated.
    private static readonly string[] _rootMembers = ["accounts"];
    private static readonly string[] _accountMembers = ["attrs", "vaults"];
    private static readonly string[] _vaultMembers = ["attrs", "items"];
    private static readonly string[] _itemMembers = ["uuid", "favIndex", "createdAt", "updatedAt", "state", "categoryUuid", "overview", "details"];
    private static readonly string[] _overviewMembers = ["title", "url", "urls", "tags", "subtitle", "ps", "pbe", "pgrng", "icons", "watchtowerExclusions"];
    private static readonly string[] _urlMembers = ["label", "url", "mode"];
    private static readonly string[] _detailsMembers = ["loginFields", "notesPlain", "password", "sections", "passwordHistory", "documentAttributes"];
    private static readonly string[] _loginFieldMembers = ["value", "id", "name", "fieldType", "type", "designation"];
    private static readonly string[] _sectionMembers = ["title", "name", "fields", "hideAddAnotherField"];

    private static readonly string[] _fieldMembers =
        ["title", "id", "value", "indexAtSource", "guarded", "multiline", "dontGenerate", "inputTraits", "clipboardFilter", "placeholder"];

    private static readonly string[] _historyMembers = ["value", "time"];
    private static readonly string[] _fileMembers = ["fileName", "documentId", "decryptedSize"];
    private static readonly string[] _emailMembers = ["email_address", "provider"];
    private static readonly string[] _addressMembers = ["street", "city", "state", "zip", "country"];
    private static readonly string[] _sshKeyMembers = ["privateKey", "metadata"];
    private static readonly string[] _sshMetadataMembers = ["privateKey", "publicKey", "fingerprint", "keyType"];

    private static readonly long _earliestSecond = DateTimeOffset.MinValue.ToUnixTimeSeconds();
    private static readonly long _latestSecond = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

    /// <inheritdoc/>
    public string Format => "1Password 1PUX";

    /// <inheritdoc/>
    public int MaximumBytes => 256 * 1024 * 1024;

    /// <summary>Whether the file is a ZIP archive named <c>.1pux</c>, as 1Password names every 1PUX export, or one whose first entry is 1PUX's own.</summary>
    /// <param name="fileName">The file's name.</param>
    /// <param name="start">The file's first bytes.</param>
    /// <returns><see langword="true"/> when this reader reads it.</returns>
    public bool Recognizes(string fileName, ReadOnlySpan<byte> start)
    {
        if (start.Length < 30 || BinaryPrimitives.ReadUInt32LittleEndian(start) != _localHeader)
        {
            return false;
        }

        if (fileName.EndsWith(".1pux", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var length = BinaryPrimitives.ReadUInt16LittleEndian(start[26..]);
        var first = start.Length >= 30 + length ? start.Slice(30, length) : ReadOnlySpan<byte>.Empty;

        return first.SequenceEqual("export.data"u8) || first.SequenceEqual("export.attributes"u8) || first.StartsWith("files/"u8);
    }

    /// <summary>Reads every item of every vault, every file they hold, and names everything that lands nowhere.</summary>
    /// <param name="file">The whole file.</param>
    /// <returns>Its items and the records that are none.</returns>
    /// <exception cref="ExportFormatException">The archive exceeds a cap, is damaged, or holds something where 1Password writes something else, named by its place.</exception>
    public ExportContents Read(ReadOnlySpan<byte> file)
    {
        if (Listed(file) > MaximumEntries)
        {
            throw TooManyEntries();
        }

        // ZipArchive reads only a stream, so the file is copied once and the copy zeroed.
        var copy = file.ToArray();

        try
        {
            using var memory = new MemoryStream(copy, writable: false);
            using var archive = Open(memory);
            return new Reading(Entries(archive)).Contents();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(copy);
        }
    }

    /// <summary>
    /// The most entries any reading of the archive's end records gives it: the count each declares, and the central
    /// directory headers that follow one another from where each says the directory starts, counted without allocating
    /// any, so ZipArchive never reads a directory larger than <see cref="MaximumEntries"/> (D-0583).
    /// </summary>
    private static long Listed(ReadOnlySpan<byte> zip)
    {
        var end = -1;

        for (var at = zip.Length - 22; at >= Math.Max(0, zip.Length - 22 - ushort.MaxValue); at--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(zip[at..]) == _endRecord)
            {
                end = at;
                break;
            }
        }

        if (end < 0)
        {
            throw new ExportFormatException("it is not a ZIP archive: it has no end of central directory record");
        }

        long declared = BinaryPrimitives.ReadUInt16LittleEndian(zip[(end + 10)..]);
        var headers = Headers(zip, BinaryPrimitives.ReadUInt32LittleEndian(zip[(end + 16)..]));

        if (end >= 20 && BinaryPrimitives.ReadUInt32LittleEndian(zip[(end - 20)..]) == _zip64Locator)
        {
            var record = BinaryPrimitives.ReadUInt64LittleEndian(zip[(end - 12)..]);

            if (zip.Length >= 56 && record <= (ulong)(zip.Length - 56) && BinaryPrimitives.ReadUInt32LittleEndian(zip[(int)record..]) == _zip64Record)
            {
                var entries = BinaryPrimitives.ReadUInt64LittleEndian(zip[((int)record + 32)..]);
                var zip64 = entries > MaximumEntries ? MaximumEntries + 1 : (long)entries;

                // A ZIP64 archive may leave its 16-bit count at the most it holds, which is then no count.
                declared = declared == ushort.MaxValue ? zip64 : Math.Max(declared, zip64);
                headers = Math.Max(headers, Headers(zip, BinaryPrimitives.ReadUInt64LittleEndian(zip[((int)record + 48)..])));
            }
        }

        return Math.Max(declared, headers);
    }

    /// <summary>How many central directory headers follow one another from <paramref name="start"/>, up to one past <see cref="MaximumEntries"/>.</summary>
    private static int Headers(ReadOnlySpan<byte> zip, ulong start)
    {
        var count = 0;
        var at = start;

        while (count <= MaximumEntries && zip.Length >= 46 && at <= (ulong)(zip.Length - 46) && BinaryPrimitives.ReadUInt32LittleEndian(zip[(int)at..]) == _centralHeader)
        {
            var header = zip[(int)at..];
            at += 46UL + BinaryPrimitives.ReadUInt16LittleEndian(header[28..]) + BinaryPrimitives.ReadUInt16LittleEndian(header[30..]) + BinaryPrimitives.ReadUInt16LittleEndian(header[32..]);
            count++;
        }

        return count;
    }

    private static ZipArchive Open(MemoryStream memory)
    {
        try
        {
            return new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            throw new ExportFormatException("it is not a ZIP archive keypaste reads");
        }
    }

    /// <summary>Every entry by its path, in the archive's order, refusing a path that leaves the archive and one two entries share (D-0583).</summary>
    private static List<ZipArchiveEntry> Entries(ZipArchive archive)
    {
        List<ZipArchiveEntry> listed;

        try
        {
            listed = [.. archive.Entries];
        }
        catch (InvalidDataException)
        {
            throw new ExportFormatException("its ZIP archive's central directory is damaged");
        }

        if (listed.Count > MaximumEntries)
        {
            throw TooManyEntries();
        }

        HashSet<string> paths = new(StringComparer.Ordinal);

        foreach (var entry in listed)
        {
            if (Leaves(entry.FullName))
            {
                throw new ExportFormatException($"its ZIP archive holds {entry.FullName}, whose path leaves the archive");
            }

            if (!paths.Add(entry.FullName))
            {
                throw new ExportFormatException($"its ZIP archive holds two files at {entry.FullName}");
            }
        }

        return listed;
    }

    /// <summary>Whether a path is absolute, names a drive, or climbs out of its folder.</summary>
    private static bool Leaves(string path) =>
        path.StartsWith('/') || path.StartsWith('\\') || (path.Length > 1 && path[1] == ':') || path.Split('/', '\\').Contains("..", StringComparer.Ordinal);

    private static ExportFormatException TooManyEntries() =>
        new(string.Create(CultureInfo.InvariantCulture, $"its ZIP archive lists more than {MaximumEntries:N0} files, the most keypaste reads of a 1PUX export"));

    /// <summary>An entry's bytes, exactly as many as the archive says it holds, which the caller has held to the caps.</summary>
    private static byte[] Unpack(ZipArchiveEntry entry)
    {
        var bytes = new byte[(int)entry.Length];

        if (Unpacked(entry, bytes))
        {
            return bytes;
        }

        CryptographicOperations.ZeroMemory(bytes);
        throw new ExportFormatException($"{entry.FullName} in its ZIP archive is damaged, or packed in a way keypaste does not unpack");
    }

    private static bool Unpacked(ZipArchiveEntry entry, byte[] bytes)
    {
        try
        {
            using var stream = entry.Open();
            return stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false) == bytes.Length && stream.ReadByte() < 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException)
        {
            return false;
        }
    }

    private static JsonDocument Parse(byte[] json)
    {
        try
        {
            return JsonDocument.Parse(json.AsMemory(json.AsSpan().StartsWith("﻿"u8) ? 3 : 0), _strict);
        }
        catch (JsonException ex)
        {
            var line = ex.LineNumber is { } number ? string.Create(CultureInfo.InvariantCulture, $" at line {number + 1}") : string.Empty;
            throw new ExportFormatException($"its export.data is not JSON keypaste reads{line}: a value is malformed or a name is given twice");
        }
    }

    /// <summary>A field's name as KeePassXC's own 1PUX import names it: its section's title, when it has one, joined with <c>_</c> to its own title, else its id (D-0584).</summary>
    private static string FieldName(string section, string title, string id)
    {
        var own = title.Trim() is { Length: > 0 } trimmed ? trimmed : id.Trim() is { Length: > 0 } named ? named : "field";
        return section.Trim() is { Length: > 0 } heading ? $"{heading}_{own}" : own;
    }

    /// <summary>A date field's day, from the second 1Password keeps it at.</summary>
    private static string Day(long seconds, string where) =>
        UnixTime(seconds, where).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A month field's <c>YYYYMM</c> as <c>YYYY-MM</c>, or its digits as written when they are no month.</summary>
    private static string Month(long yearMonth) =>
        yearMonth is >= 100_001 and <= 999_912 && yearMonth % 100 is >= 1 and <= 12
            ? string.Create(CultureInfo.InvariantCulture, $"{yearMonth / 100}-{yearMonth % 100:D2}")
            : yearMonth.ToString(CultureInfo.InvariantCulture);

    private static DateTimeOffset UnixTime(long seconds, string where) =>
        seconds >= _earliestSecond && seconds <= _latestSecond ? DateTimeOffset.FromUnixTimeSeconds(seconds) : throw new ExportFormatException($"{where} is not a time");

    private static string Joined(string separator, params string[] parts) => string.Join(separator, parts.Where(part => part.Length > 0));

    private static string Named(string kind, string name) => name.Length == 0 ? $"a {kind} with no name" : $"{kind} {name}";

    /// <summary>Each member these rules do not read that holds anything, named under <paramref name="prefix"/>, so a part 1Password adds later is named rather than lost.</summary>
    private static IEnumerable<ExportOmission> Unknown(JsonElement element, string[] known, string prefix) =>
        element.EnumerateObject()
            .Where(member => !known.Contains(member.Name, StringComparer.Ordinal) && Holds(member.Value))
            .Select(member => new ExportOmission(prefix + member.Name, $"keypaste does not read this part of a {_source} export"));

    private static bool Holds(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.False => false,
        JsonValueKind.String => value.GetString()!.Length > 0,
        JsonValueKind.Array => value.GetArrayLength() > 0,
        JsonValueKind.Object => value.EnumerateObject().Any(),
        _ => true,
    };

    private static void RequireObject(JsonElement element, string where)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ExportFormatException($"{where} is not a JSON object");
        }
    }

    private static string Text(JsonElement element, string member, string where) =>
        element.TryGetProperty(member, out var value) ? TextOf(value, Join(where, member)) : string.Empty;

    private static string TextOf(JsonElement value, string where) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Null => string.Empty,
        _ => throw new ExportFormatException($"{where} is not text"),
    };

    private static long? Whole(JsonElement element, string member, string where) =>
        element.TryGetProperty(member, out var value) ? WholeOf(value, Join(where, member)) : null;

    private static long? WholeOf(JsonElement value, string where) =>
        value.ValueKind == JsonValueKind.Null ? null
        : value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number
        : throw new ExportFormatException($"{where} is not a whole number");

    private static DateTimeOffset? Time(JsonElement element, string member, string where) =>
        Whole(element, member, where) is { } seconds ? UnixTime(seconds, Join(where, member)) : null;

    private static JsonElement? OptionalObject(JsonElement element, string member, string where)
    {
        if (!element.TryGetProperty(member, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        RequireObject(value, Join(where, member));
        return value;
    }

    /// <summary>Each object of a list with its place, a null in the list skipped.</summary>
    private static IEnumerable<(JsonElement Element, string Where)> Objects(JsonElement element, string member, string where)
    {
        var list = ListOf(element, member, where);

        for (var index = 0; index < list.Count; index++)
        {
            var place = string.Create(CultureInfo.InvariantCulture, $"{Join(where, member)}[{index}]");

            if (list[index].ValueKind != JsonValueKind.Null)
            {
                RequireObject(list[index], place);
                yield return (list[index], place);
            }
        }
    }

    private static List<JsonElement> ListOf(JsonElement element, string member, string where) =>
        !element.TryGetProperty(member, out var value) || value.ValueKind == JsonValueKind.Null ? []
        : value.ValueKind == JsonValueKind.Array ? [.. value.EnumerateArray()]
        : throw new ExportFormatException($"{Join(where, member)} is not a list");

    private static string Join(string where, string member) => where.Length == 0 ? member : $"{where}.{member}";

    /// <summary>One read of one archive: its entries, the files items took, and those still to unpack.</summary>
    private sealed class Reading(List<ZipArchiveEntry> entries)
    {
        private readonly List<ZipArchiveEntry> _entries = entries;
        private readonly Dictionary<string, ZipArchiveEntry> _byPath = entries.ToDictionary(entry => entry.FullName, StringComparer.Ordinal);
        private readonly HashSet<string> _taken = new(StringComparer.Ordinal) { _dataEntry, _attributesEntry };
        private readonly List<(List<ExportAttachment> Into, ZipArchiveEntry Entry, string Name)> _pending = [];
        private readonly List<ExportOmission> _unread = [];

        internal ExportContents Contents()
        {
            if (!_byPath.TryGetValue(_dataEntry, out var data))
            {
                throw new ExportFormatException("it holds no export.data, which every 1PUX export holds");
            }

            if (data.Length > MaximumDataBytes)
            {
                throw new ExportFormatException(string.Create(
                    CultureInfo.InvariantCulture, $"its export.data unpacks to more than {MaximumDataBytes / (1024 * 1024)} MiB, the most keypaste reads"));
            }

            var json = Unpack(data);
            List<ExportItem> items;

            try
            {
                using var document = Parse(json);
                items = Accounts(document.RootElement);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(json);
            }

            if (data.Length + _pending.Sum(pending => pending.Entry.Length) > MaximumUnpackedBytes)
            {
                throw new ExportFormatException(string.Create(
                    CultureInfo.InvariantCulture, $"it unpacks to more than {MaximumUnpackedBytes / (1024 * 1024)} MiB, the most keypaste reads of a 1PUX export"));
            }

            UnpackAttachments();

            _unread.AddRange(_entries
                .Where(entry => !_taken.Contains(entry.FullName) && !entry.FullName.EndsWith('/'))
                .Select(entry => new ExportOmission($"file {entry.FullName}", "no item the import copies holds it")));
            _unread.Add(new ExportOmission("passkeys", "1Password leaves every passkey out of a 1PUX export"));

            return new ExportContents(items, _unread);
        }

        private void UnpackAttachments()
        {
            List<ExportAttachment> unpacked = [];

            try
            {
                foreach (var (into, entry, name) in _pending)
                {
                    var attachment = new ExportAttachment(name, Unpack(entry));
                    unpacked.Add(attachment);
                    into.Add(attachment);
                }
            }
            catch
            {
                foreach (var attachment in unpacked)
                {
                    attachment.Zero();
                }

                throw;
            }
        }

        private List<ExportItem> Accounts(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new ExportFormatException("its export.data is not a JSON object");
            }

            if (!root.TryGetProperty("accounts", out _))
            {
                throw new ExportFormatException("its export.data has no accounts list");
            }

            var accounts = Objects(root, "accounts", string.Empty).ToList();
            List<ExportItem> items = [];

            foreach (var (account, where) in accounts)
            {
                var accountName = OptionalObject(account, "attrs", where) is { } attributes ? Text(attributes, "accountName", Join(where, "attrs")) : string.Empty;

                foreach (var (vault, vaultWhere) in Objects(account, "vaults", where))
                {
                    var attrs = OptionalObject(vault, "attrs", vaultWhere);
                    var attrsWhere = Join(vaultWhere, "attrs");
                    var name = attrs is { } named ? Text(named, "name", attrsWhere) : string.Empty;

                    // 1Password writes one account per file; several, each vault lands under its account.
                    IReadOnlyList<string> folder = accounts.Count > 1 ? [accountName, name] : [name];

                    foreach (var (item, itemWhere) in Objects(vault, "items", vaultWhere))
                    {
                        if (Item(item, itemWhere, folder) is { } read)
                        {
                            items.Add(read);
                        }
                    }

                    if (attrs is { } held)
                    {
                        VaultAttributes(held, attrsWhere, Named("vault", name));
                    }

                    _unread.AddRange(Unknown(vault, _vaultMembers, $"{vaultWhere}."));
                }

                _unread.AddRange(Unknown(account, _accountMembers, $"{where}."));
            }

            _unread.AddRange(Unknown(root, _rootMembers, string.Empty));
            return items;
        }

        /// <summary>What a vault holds beyond its name that a group has no place for.</summary>
        private void VaultAttributes(JsonElement attrs, string where, string vault)
        {
            if (Text(attrs, "desc", where).Length > 0)
            {
                _unread.Add(new ExportOmission($"description of {vault}", "keypaste keeps no description for a group"));
            }

            if (Text(attrs, "avatar", where) is { Length: > 0 } avatar)
            {
                _taken.Add(_filesFolder + avatar);
                _unread.Add(new ExportOmission($"icon of {vault}", "keypaste keeps no icon for a group"));
            }
        }

        private ExportItem? Item(JsonElement element, string where, IReadOnlyList<string> folder)
        {
            var overview = OptionalObject(element, "overview", where);
            var overviewWhere = Join(where, "overview");
            var title = overview is { } summary ? Text(summary, "title", overviewWhere) : string.Empty;
            var state = Text(element, "state", where);

            if (state == "trashed")
            {
                _unread.Add(new ExportOmission(Named("item", title), "it is in 1Password's Recently Deleted"));
                return null;
            }

            var landing = new Landing();
            landing.Omitted.AddRange(Unknown(element, _itemMembers, string.Empty));

            if (overview is { } held)
            {
                Overview(landing, held, overviewWhere);
            }

            if (Text(element, "categoryUuid", where) is { Length: > 0 } category)
            {
                if (_categories.TryGetValue(category, out var named))
                {
                    landing.Tags.Add(named);
                }
                else
                {
                    landing.Omitted.Add(new ExportOmission($"category {category}", "keypaste does not know this 1Password category, so no tag names it"));
                }
            }

            if (Whole(element, "favIndex", where) > 0)
            {
                landing.Tags.Add(_favoriteTag);
            }

            switch (state)
            {
                case "" or "active":
                    break;
                case "archived":
                    landing.Tags.Add(_archivedTag);
                    break;
                default:
                    landing.Omitted.Add(new ExportOmission($"state {state}", "keypaste does not know this 1Password item state"));
                    break;
            }

            var details = OptionalObject(element, "details", where);
            var detailsWhere = Join(where, "details");
            var notes = string.Empty;

            if (details is { } inside)
            {
                notes = Text(inside, "notesPlain", detailsWhere);
                Details(landing, inside, detailsWhere);
            }

            for (var more = 1; more < landing.Totps.Count; more++)
            {
                landing.Omitted.Add(new ExportOmission(
                    string.Create(CultureInfo.InvariantCulture, $"TOTP {more + 1}"), "keypaste and KeePassXC compute one TOTP per item, so only the item's first lands"));
            }

            List<ExportVersion> history = details is { } past ? History(landing, past, detailsWhere, notes) : [];

            return new ExportItem
            {
                Title = title,
                Folder = folder,
                Username = landing.Username,
                Password = landing.Password,
                Urls = landing.Urls,
                Notes = notes,
                Fields = landing.Fields,
                Tags = landing.Tags,
                Totp = landing.Totps.Count > 0 ? CsvExport.Totp(landing.Totps[0], title, landing.Username) : string.Empty,
                Attachments = landing.Attachments,
                History = history,
                Created = Time(element, "createdAt", where),
                Modified = Time(element, "updatedAt", where),
                Omitted = landing.Omitted,
            };
        }

        private static void Overview(Landing landing, JsonElement overview, string where)
        {
            landing.Omitted.AddRange(Unknown(overview, _overviewMembers, "overview."));

            if (Text(overview, "url", where) is { Length: > 0 } primary)
            {
                landing.Urls.Add(primary);
            }

            foreach (var (url, urlWhere) in Objects(overview, "urls", where))
            {
                landing.Omitted.AddRange(Unknown(url, _urlMembers, "url."));

                if (Text(url, "url", urlWhere) is not { Length: > 0 } address)
                {
                    continue;
                }

                if (!landing.Urls.Contains(address, StringComparer.Ordinal))
                {
                    landing.Urls.Add(address);
                }

                var number = landing.Urls.IndexOf(address) + 1;

                if (Text(url, "label", urlWhere) is { Length: > 0 } label && !string.Equals(label, "website", StringComparison.OrdinalIgnoreCase))
                {
                    landing.Omitted.Add(new ExportOmission(
                        string.Create(CultureInfo.InvariantCulture, $"label of web address {number}"), "keypaste keeps no label for a web address"));
                }

                if (Text(url, "mode", urlWhere) is { Length: > 0 } mode && mode != "default")
                {
                    landing.Omitted.Add(new ExportOmission(
                        string.Create(CultureInfo.InvariantCulture, $"autofill setting of web address {number}"), "keypaste keeps no autofill setting for a web address"));
                }
            }

            var tags = ListOf(overview, "tags", where);

            for (var index = 0; index < tags.Count; index++)
            {
                if (TextOf(tags[index], string.Create(CultureInfo.InvariantCulture, $"{Join(where, "tags")}[{index}]")) is { Length: > 0 } tag)
                {
                    landing.Tags.Add(tag);
                }
            }

            if (overview.TryGetProperty("icons", out var icons) && Holds(icons))
            {
                landing.Omitted.Add(new ExportOmission("icon", "keypaste sets no item's icon"));
            }

            if (OptionalObject(overview, "watchtowerExclusions", where) is { } exclusions && exclusions.EnumerateObject().Any(exclusion => exclusion.Value.ValueKind == JsonValueKind.True))
            {
                landing.Omitted.Add(new ExportOmission("Watchtower exclusions", "keypaste keeps the recommendations you dismiss on each computer"));
            }
        }

        private void Details(Landing landing, JsonElement details, string where)
        {
            landing.Omitted.AddRange(Unknown(details, _detailsMembers, "details."));

            List<(string Name, string Value, bool Protect)> undesignated = [];

            foreach (var (field, fieldWhere) in Objects(details, "loginFields", where))
            {
                landing.Omitted.AddRange(Unknown(field, _loginFieldMembers, "loginField."));

                var value = Text(field, "value", fieldWhere);
                var type = Text(field, "fieldType", fieldWhere) is { Length: > 0 } fieldType ? fieldType : Text(field, "type", fieldWhere);

                switch (Text(field, "designation", fieldWhere).ToUpperInvariant())
                {
                    case "USERNAME":
                        landing.Put("username", value, protect: false);
                        break;
                    case "PASSWORD":
                        landing.Put("password", value, protect: true);
                        break;
                    default:
                        undesignated.Add((FieldName(string.Empty, Text(field, "name", fieldWhere), Text(field, "id", fieldWhere)), value, type == "P"));
                        break;
                }
            }

            landing.Put("password", Text(details, "password", where), protect: true);

            foreach (var (name, value, protect) in undesignated)
            {
                landing.Put(name, value, protect);
            }

            foreach (var (section, sectionWhere) in Objects(details, "sections", where))
            {
                landing.Omitted.AddRange(Unknown(section, _sectionMembers, "section."));

                var heading = Text(section, "title", sectionWhere);

                foreach (var (field, fieldWhere) in Objects(section, "fields", sectionWhere))
                {
                    Field(landing, field, fieldWhere, heading);
                }
            }

            if (OptionalObject(details, "documentAttributes", where) is { } document)
            {
                Attach(landing, document, Join(where, "documentAttributes"), "documentAttributes.");
            }
        }

        /// <summary>One section field, by the kind its value names: text plain, a concealed value or card number protected, a TOTP for the item's <c>otp</c>, a file an attachment (D-0584).</summary>
        private void Field(Landing landing, JsonElement field, string where, string section)
        {
            landing.Omitted.AddRange(Unknown(field, _fieldMembers, "field."));

            var name = FieldName(section, Text(field, "title", where), Text(field, "id", where));

            if (OptionalObject(field, "value", where) is not { } value || !value.EnumerateObject().Any())
            {
                return;
            }

            var valueWhere = Join(where, "value");
            var kind = value.EnumerateObject().First();
            var held = kind.Value;
            var heldWhere = Join(valueWhere, kind.Name);

            landing.Omitted.AddRange(value.EnumerateObject().Skip(1)
                .Where(other => Holds(other.Value))
                .Select(other => new ExportOmission($"value.{other.Name}", $"keypaste does not read this part of a {_source} export")));

            switch (kind.Name)
            {
                case "string" or "url" or "phone" or "menu" or "gender" or "creditCardType":
                    landing.Put(name, TextOf(held, heldWhere), protect: false);
                    break;
                case "concealed" or "creditCardNumber":
                    landing.Put(name, TextOf(held, heldWhere), protect: true);
                    break;
                case "totp":
                    if (TextOf(held, heldWhere) is { Length: > 0 } totp)
                    {
                        landing.Totps.Add(totp);
                    }

                    break;
                case "date":
                    if (WholeOf(held, heldWhere) is { } seconds)
                    {
                        landing.Put(name, Day(seconds, heldWhere), protect: false);
                    }

                    break;
                case "monthYear":
                    if (WholeOf(held, heldWhere) is { } yearMonth)
                    {
                        landing.Put(name, Month(yearMonth), protect: false);
                    }

                    break;
                case "email":
                    Email(landing, name, held, heldWhere);
                    break;
                case "address":
                    Address(landing, name, held, heldWhere);
                    break;
                case "sshKey":
                    SshKey(landing, name, section, held, heldWhere);
                    break;
                case "file":
                    if (held.ValueKind != JsonValueKind.Null)
                    {
                        RequireObject(held, heldWhere);
                        Attach(landing, held, heldWhere, "file.");
                    }

                    break;
                case "reference":
                    if (Holds(held))
                    {
                        landing.Omitted.Add(new ExportOmission(Named("field", name), "it links to another 1Password item, which lands in its own right"));
                    }

                    break;
                default:
                    // A kind 1Password adds later: text lands protected, as a value nothing says 1Password shows.
                    if (held.ValueKind == JsonValueKind.String)
                    {
                        landing.Put(name, held.GetString()!, protect: true);
                    }
                    else if (Holds(held))
                    {
                        landing.Omitted.Add(new ExportOmission(Named("field", name), $"keypaste does not read 1Password's {kind.Name} field"));
                    }

                    break;
            }
        }

        private static void Email(Landing landing, string name, JsonElement value, string where)
        {
            if (value.ValueKind is JsonValueKind.String or JsonValueKind.Null)
            {
                landing.Put(name, TextOf(value, where), protect: false);
                return;
            }

            RequireObject(value, where);
            landing.Omitted.AddRange(Unknown(value, _emailMembers, "email."));
            landing.Put(name, Text(value, "email_address", where), protect: false);
            landing.Put($"{name} provider", Text(value, "provider", where), protect: false);
        }

        /// <summary>An address as one value of up to three lines, street, then city, state and code, then country, as KeePassXC's own 1PUX import joins one.</summary>
        private static void Address(Landing landing, string name, JsonElement value, string where)
        {
            if (value.ValueKind == JsonValueKind.Null)
            {
                return;
            }

            RequireObject(value, where);
            landing.Omitted.AddRange(Unknown(value, _addressMembers, "address."));

            var place = Joined(", ", Text(value, "city", where), Joined(" ", Text(value, "state", where), Text(value, "zip", where)));
            landing.Put(name, Joined("\n", Text(value, "street", where), place, Text(value, "country", where)), protect: false);
        }

        /// <summary>An SSH key: its private key protected under the field's name, in the form 1Password describes it and, when that differs, as it stores it, and its public key, fingerprint and type plain.</summary>
        private static void SshKey(Landing landing, string name, string section, JsonElement value, string where)
        {
            if (value.ValueKind == JsonValueKind.Null)
            {
                return;
            }

            RequireObject(value, where);
            landing.Omitted.AddRange(Unknown(value, _sshKeyMembers, "sshKey."));

            var stored = Text(value, "privateKey", where);

            if (OptionalObject(value, "metadata", where) is not { } metadata)
            {
                landing.Put(name, stored, protect: true);
                return;
            }

            var metadataWhere = Join(where, "metadata");
            var described = Text(metadata, "privateKey", metadataWhere);
            landing.Omitted.AddRange(Unknown(metadata, _sshMetadataMembers, "sshKey.metadata."));
            landing.Put(name, described, protect: true);

            if (stored != described)
            {
                landing.Put(name, stored, protect: true);
            }

            landing.Put(FieldName(section, "public key", string.Empty), Text(metadata, "publicKey", metadataWhere), protect: false);
            landing.Put(FieldName(section, "fingerprint", string.Empty), Text(metadata, "fingerprint", metadataWhere), protect: false);
            landing.Put(FieldName(section, "key type", string.Empty), Text(metadata, "keyType", metadataWhere), protect: false);
        }

        /// <summary>A file an item holds, found at <c>files/&lt;documentId&gt;__&lt;fileName&gt;</c> as 1Password writes it, unpacked later when no larger than D-0533's cap.</summary>
        private void Attach(Landing landing, JsonElement file, string where, string prefix)
        {
            landing.Omitted.AddRange(Unknown(file, _fileMembers, prefix));

            var name = Text(file, "fileName", where);
            var path = $"{_filesFolder}{Text(file, "documentId", where)}__{name}";

            if (!_byPath.TryGetValue(path, out var entry) || path.EndsWith('/'))
            {
                landing.Omitted.Add(new ExportOmission(Named("attachment", name), "the export holds no file for it"));
                return;
            }

            _taken.Add(path);

            if (entry.Length > EntryAttachment.MaximumSize)
            {
                landing.Omitted.Add(new ExportOmission(Named("attachment", name), ExportSource.AttachmentTooLarge));
            }
            else
            {
                _pending.Add((landing.Attachments, entry, name));
            }
        }

        /// <summary>Each earlier password as a version holding it at the second it stopped being current, with the item's other standard fields as they are now, since 1Password keeps only the password.</summary>
        private static List<ExportVersion> History(Landing landing, JsonElement details, string where, string notes)
        {
            List<ExportVersion> versions = [];
            var url = landing.Urls.Count > 0 ? landing.Urls[0] : string.Empty;

            foreach (var (entry, entryWhere) in Objects(details, "passwordHistory", where))
            {
                landing.Omitted.AddRange(Unknown(entry, _historyMembers, "passwordHistory."));

                if (Text(entry, "value", entryWhere) is not { Length: > 0 } password)
                {
                    continue;
                }

                if (Time(entry, "time", entryWhere) is { } when)
                {
                    versions.Add(new ExportVersion(when, landing.Username, password, url, notes));
                }
                else
                {
                    landing.Omitted.Add(new ExportOmission("an earlier password", "the export gives it no time"));
                }
            }

            return versions;
        }
    }

    /// <summary>One item's parts as they are read, each custom field under a name none of its others has.</summary>
    private sealed class Landing
    {
        private readonly HashSet<string> _names = new(StringComparer.Ordinal);

        internal string Username { get; private set; } = string.Empty;

        internal string Password { get; private set; } = string.Empty;

        internal List<string> Urls { get; } = [];

        internal List<ExportField> Fields { get; } = [];

        internal List<string> Tags { get; } = [];

        internal List<string> Totps { get; } = [];

        internal List<ExportAttachment> Attachments { get; } = [];

        internal List<ExportOmission> Omitted { get; } = [];

        /// <summary>
        /// Lands a value: as the item's user name, password or web address when it is named as one and that is still
        /// empty, else as a custom field, whose name, when it is a standard field's, KeePassXC's or another of the item's,
        /// is kept beside the other as <c>name (2)</c>, as a taken title is (D-0584). A value its standard field already
        /// holds is there already.
        /// </summary>
        internal void Put(string name, string value, bool protect)
        {
            var username = Is(name, "UserName");
            var password = Is(name, "Password");
            var url = Is(name, "URL");

            if (value.Length == 0 || (username && Username == value) || (password && Password == value) || (url && Urls.Contains(value, StringComparer.Ordinal)))
            {
                return;
            }

            if (username && Username.Length == 0)
            {
                Username = value;
            }
            else if (password && Password.Length == 0)
            {
                Password = value;
            }
            else if (url && Urls.Count == 0)
            {
                Urls.Add(value);
            }
            else
            {
                Fields.Add(new ExportField(Free(name), value, protect));
            }
        }

        private static bool Is(string name, string standard) => string.Equals(name, standard, StringComparison.OrdinalIgnoreCase);

        private string Free(string name)
        {
            if (FieldNameRules.IsWritable(name, out _) && _names.Add(name))
            {
                return name;
            }

            for (var suffix = 2; ; suffix++)
            {
                var candidate = string.Create(CultureInfo.InvariantCulture, $"{name} ({suffix})");

                // A name no suffix makes writable, such as one holding a control character, is left for the plan to name.
                if (!FieldNameRules.IsWritable(candidate, out _))
                {
                    return name;
                }

                if (_names.Add(candidate))
                {
                    return candidate;
                }
            }
        }
    }
}
