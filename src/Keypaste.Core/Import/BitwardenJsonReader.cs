using System.Buffers.Text;
using System.Globalization;
using System.Text.Json;

namespace Keypaste.Core.Import;

/// <summary>Reads Bitwarden's <c>.json</c> export, of a person's vault or an organization's, and refuses an encrypted one (D-0568).</summary>
/// <remarks>
/// A login's username, password, web addresses and TOTP land in their own places, and every other item type's details
/// as custom fields under Bitwarden's labels, protected where Bitwarden masks them (D-0569). What a vault has no place
/// for is named with why (D-0570).
/// </remarks>
internal sealed class BitwardenJsonReader : IExportReader
{
    private const int _login = 1;
    private const int _identity = 4;
    private const int _textField = 0;
    private const int _booleanField = 2;
    private const int _linkedField = 3;
    private const int _repromptPassword = 1;
    private const string _favoriteTag = "Favorite";

    /// <summary>The tag every reader gives an archived item, as KeePassXC's 1Password import tags one (D-0569).</summary>
    private const string _archivedTag = "Archived";

    private static readonly JsonDocumentOptions _strict = new() { AllowDuplicateProperties = false };

    private static readonly string[] _exportMembers = ["encrypted", "folders", "collections", "items"];

    private static readonly string[] _itemMembers =
    [
        "id", "organizationId", "folderId", "collectionIds", "type", "reprompt", "name", "notes", "favorite", "fields", "passwordHistory",
        "creationDate", "revisionDate", "deletedDate", "archivedDate", "key", "login", "secureNote", "card", "identity", "sshKey",
        "bankAccount", "driversLicense", "passport",
    ];

    private static readonly string[] _uriMembers = ["uri", "match", "uriChecksum"];
    private static readonly string[] _fieldMembers = ["name", "value", "type", "linkedId"];
    private static readonly string[] _historyMembers = ["password", "lastUsedDate"];

    private static readonly string[] _passkeyMembers =
    [
        "credentialId", "keyType", "keyAlgorithm", "keyCurve", "keyValue", "rpId", "userHandle", "userName", "counter", "rpName",
        "userDisplayName", "discoverable", "creationDate",
    ];

    /// <summary>What a Bitwarden passkey holds that KeePassXC has no field for, by member and as the preview names it.</summary>
    private static readonly (string Member, string Named)[] _passkeyExtras =
    [
        ("counter", "counter"), ("rpName", "site name"), ("userDisplayName", "display name"), ("discoverable", "discoverable flag"),
        ("creationDate", "creation date"),
    ];

    private static readonly ItemType[] _types =
    [
        new(_login, "login", [], ["uris", "username", "password", "totp", "fido2Credentials"]),
        new(2, "secureNote", [], ["type"]),
        new(3, "card",
        [
            new("cardholderName", "Cardholder name"), new("brand", "Brand"), new("number", "Number", Protect: true),
            new("expMonth", "Expiration month"), new("expYear", "Expiration year"), new("code", "Security code", Protect: true),
        ], []),
        new(_identity, "identity",
        [
            new("title", "Name title"), new("firstName", "First name"), new("middleName", "Middle name"), new("lastName", "Last name"),
            new("company", "Company"), new("email", "Email"), new("phone", "Phone"), new("ssn", "Social Security number", Protect: true),
            new("passportNumber", "Passport number", Protect: true), new("licenseNumber", "License number"), new("address1", "Address 1"),
            new("address2", "Address 2"), new("address3", "Address 3"), new("city", "City"), new("state", "State"),
            new("postalCode", "Postal code"), new("country", "Country"),
        ], ["username"]),
        new(5, "sshKey", [new("privateKey", "Private key", Protect: true), new("publicKey", "Public key"), new("keyFingerprint", "Fingerprint")], []),
        new(6, "bankAccount",
        [
            new("bankName", "Bank name"), new("nameOnAccount", "Name on account"), new("accountType", "Account type"),
            new("accountNumber", "Account number", Protect: true), new("routingNumber", "Routing number"), new("branchNumber", "Branch number"),
            new("pin", "PIN", Protect: true), new("swiftCode", "SWIFT code", Protect: true), new("iban", "IBAN", Protect: true),
            new("bankContactPhone", "Bank contact phone"),
        ], []),
        new(7, "driversLicense",
        [
            new("firstName", "First name"), new("middleName", "Middle name"), new("lastName", "Last name"), new("dateOfBirth", "Date of birth"),
            new("licenseNumber", "License number", Protect: true), new("issuingCountry", "Issuing country"), new("issuingState", "Issuing state"),
            new("issueDate", "Issue date"), new("expirationDate", "Expiration date"), new("issuingAuthority", "Issuing authority"),
            new("licenseClass", "License class"),
        ], []),
        new(8, "passport",
        [
            new("surname", "Surname"), new("givenName", "Given name"), new("dateOfBirth", "Date of birth"), new("sex", "Sex"),
            new("birthPlace", "Birth place"), new("nationality", "Nationality"), new("issuingCountry", "Issuing country"),
            new("passportNumber", "Passport number", Protect: true), new("passportType", "Passport type"),
            new("nationalIdentificationNumber", "National identification number", Protect: true), new("issuingAuthority", "Issuing authority"),
            new("issueDate", "Issue date"), new("expirationDate", "Expiration date"),
        ], []),
    ];

    /// <inheritdoc/>
    public string Format => "Bitwarden JSON";

    /// <inheritdoc/>
    public int MaximumBytes => 64 * 1024 * 1024;

    /// <summary>Whether the file is a JSON object whose first member is <c>encrypted</c>, as Bitwarden writes every JSON export, or the folders, collections or items a hand-made one starts with.</summary>
    /// <param name="fileName">Not read.</param>
    /// <param name="start">The file's first bytes.</param>
    /// <returns><see langword="true"/> when this reader reads it.</returns>
    public bool Recognizes(string fileName, ReadOnlySpan<byte> start)
    {
        var reader = new Utf8JsonReader(WithoutBom(start), isFinalBlock: false, state: default);

        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject || !reader.Read() || reader.TokenType != JsonTokenType.PropertyName)
            {
                return false;
            }

            var encrypted = reader.ValueTextEquals("encrypted"u8);
            var listed = reader.ValueTextEquals("folders"u8) || reader.ValueTextEquals("collections"u8) || reader.ValueTextEquals("items"u8);

            return reader.Read()
                && ((encrypted && reader.TokenType is JsonTokenType.True or JsonTokenType.False)
                    || (listed && reader.TokenType == JsonTokenType.StartArray));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Reads every item, every record that is none and every part of an item a vault has no place for.</summary>
    /// <param name="file">The whole file.</param>
    /// <returns>Its items and the records that are none.</returns>
    /// <exception cref="ExportFormatException">The file is encrypted, is not JSON, or holds something where Bitwarden writes something else, named by its place.</exception>
    public ExportContents Read(ReadOnlySpan<byte> file)
    {
        // JsonDocument reads only memory it is handed, so the file is copied once and the copy zeroed.
        var copy = WithoutBom(file).ToArray();

        try
        {
            using var document = Parse(copy);
            return Contents(document.RootElement);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(copy);
        }
    }

    private static JsonDocument Parse(byte[] json)
    {
        try
        {
            return JsonDocument.Parse(json, _strict);
        }
        catch (JsonException ex)
        {
            var line = ex.LineNumber is { } number ? string.Create(CultureInfo.InvariantCulture, $" at line {number + 1}") : string.Empty;
            throw new ExportFormatException($"it is not JSON keypaste reads{line}: a value is malformed or a name is given twice");
        }
    }

    private static ExportContents Contents(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ExportFormatException("it is not a JSON object");
        }

        if (Flag(root, "encrypted", string.Empty))
        {
            throw new ExportFormatException(Flag(root, "passwordProtected", string.Empty)
                ? "it is password-protected. Export your vault from Bitwarden again with the .json file format, which is not encrypted"
                : "it is encrypted with your Bitwarden account's key, which only Bitwarden reads. Export your vault again with the .json file format, which is not encrypted");
        }

        var folders = Places(root, "folders");
        var collections = Places(root, "collections");
        var items = OptionalList(root, "items", string.Empty) ?? throw new ExportFormatException("it has no items list");
        List<ExportItem> read = [];
        List<ExportOmission> unread = [];
        HashSet<string> landedFolders = new(StringComparer.Ordinal);
        HashSet<string> referenced = new(StringComparer.Ordinal);

        for (var index = 0; index < items.Count; index++)
        {
            var element = items[index];
            var where = string.Create(CultureInfo.InvariantCulture, $"items[{index}]");
            RequireObject(element, where);

            var title = Text(element, "name", where);
            var typeNumber = Number(element, "type", where) ?? throw new ExportFormatException($"{where} has no type");
            var named = title.Length == 0 ? "an item with no name" : $"item {title}";

            if (Present(element, "deletedDate"))
            {
                unread.Add(new ExportOmission(named, "it is in Bitwarden's trash"));
            }
            else if (_types.FirstOrDefault(known => known.Number == typeNumber) is not { } type)
            {
                unread.Add(new ExportOmission(named, string.Create(CultureInfo.InvariantCulture, $"keypaste does not read Bitwarden's item type {typeNumber}")));
            }
            else
            {
                var item = Item(element, where, title, type, folders, collections, referenced);
                landedFolders.UnionWith(Enumerable.Range(1, item.Folder.Count).Select(depth => string.Join('/', item.Folder.Take(depth))));
                read.Add(item);
            }
        }

        unread.AddRange(Unknown(root, _exportMembers, string.Empty));
        unread.AddRange(Empty(folders, "folder", referenced, landedFolders));
        unread.AddRange(Empty(collections, "collection", referenced, landedFolders));
        unread.Add(new ExportOmission("attachments", "a Bitwarden .json export holds none; save each from Bitwarden and add it to its item"));

        return new ExportContents(read, unread);
    }

    private static ExportItem Item(
        JsonElement element, string where, string title, ItemType type, Dictionary<string, Place> folders, Dictionary<string, Place> collections, HashSet<string> referenced)
    {
        List<ExportOmission> omitted = [];
        List<ExportField> fields = [];
        List<string> urls = [];
        List<ExportPasskey> passkeys = [];
        var username = string.Empty;
        var password = string.Empty;
        var totp = string.Empty;
        var notes = Text(element, "notes", where);
        var folder = Landing(element, where, folders, collections, referenced, omitted);

        omitted.AddRange(Unknown(element, _itemMembers, string.Empty));

        if (OptionalObject(element, type.Member, where) is { } details)
        {
            var detailsWhere = Join(where, type.Member);
            omitted.AddRange(Unknown(details, type.Members, $"{type.Member}."));

            foreach (var detail in type.Details)
            {
                if (Text(details, detail.Member, detailsWhere) is { Length: > 0 } value)
                {
                    fields.Add(new ExportField(detail.Field, value, detail.Protect));
                }
            }

            if (type.Number is _login or _identity)
            {
                username = Text(details, "username", detailsWhere);
            }

            if (type.Number == _login)
            {
                password = Text(details, "password", detailsWhere);
                urls = Urls(details, detailsWhere, omitted);
                totp = Totp(Text(details, "totp", detailsWhere), title, username);
                passkeys = Passkeys(details, detailsWhere, omitted);
            }
        }

        fields.AddRange(Fields(element, where, omitted));

        if (Number(element, "reprompt", where) == _repromptPassword)
        {
            omitted.Add(new ExportOmission("master password re-prompt", "keypaste asks for no password to show one item"));
        }

        List<string> tags = [];

        if (Flag(element, "favorite", where))
        {
            tags.Add(_favoriteTag);
        }

        if (Present(element, "archivedDate"))
        {
            tags.Add(_archivedTag);
        }

        return new ExportItem
        {
            Title = title,
            Folder = folder,
            Username = username,
            Password = password,
            Urls = urls,
            Notes = notes,
            Fields = fields,
            Tags = tags,
            Totp = totp,
            Passkeys = passkeys,
            History = History(element, where, username, urls.FirstOrDefault() ?? string.Empty, notes, omitted),
            Created = Date(element, "creationDate", where),
            Modified = Date(element, "revisionDate", where),
            Omitted = omitted,
        };
    }

    /// <summary>The folder an item lands in: its own, else its first collection, every other collection it is in named; each it is in joins <paramref name="referenced"/>.</summary>
    private static IReadOnlyList<string> Landing(
        JsonElement element, string where, Dictionary<string, Place> folders, Dictionary<string, Place> collections, HashSet<string> referenced, List<ExportOmission> omitted)
    {
        Place? landed = null;

        if (OptionalText(element, "folderId", where) is { } folderId)
        {
            referenced.Add(folderId);

            if (!folders.TryGetValue(folderId, out landed))
            {
                omitted.Add(new ExportOmission("folder", "the export lists no folder of this item's folder id"));
            }
        }

        var collectionIds = ListOf(element, "collectionIds", where);

        for (var index = 0; index < collectionIds.Count; index++)
        {
            if (collectionIds[index].ValueKind != JsonValueKind.String)
            {
                throw new ExportFormatException(string.Create(CultureInfo.InvariantCulture, $"{where}.collectionIds[{index}] is not text"));
            }

            var collectionId = collectionIds[index].GetString()!;
            referenced.Add(collectionId);

            if (!collections.TryGetValue(collectionId, out var collection))
            {
                omitted.Add(new ExportOmission("collection", "the export lists no collection of one of this item's collection ids"));
            }
            else if (landed is null)
            {
                landed = collection;
            }
            else
            {
                omitted.Add(new ExportOmission($"collection {collection.Name}", $"the item lands once, in {landed.Name}"));
            }
        }

        return landed?.Segments ?? [];
    }

    private static List<string> Urls(JsonElement login, string where, List<ExportOmission> omitted)
    {
        List<string> urls = [];
        var uris = ListOf(login, "uris", where);

        for (var index = 0; index < uris.Count; index++)
        {
            var uriWhere = string.Create(CultureInfo.InvariantCulture, $"{where}.uris[{index}]");
            RequireObject(uris[index], uriWhere);
            omitted.AddRange(Unknown(uris[index], _uriMembers, "uri."));

            if (Text(uris[index], "uri", uriWhere) is { Length: > 0 } address)
            {
                urls.Add(address);

                if (Number(uris[index], "match", uriWhere) is not null)
                {
                    omitted.Add(new ExportOmission(
                        string.Create(CultureInfo.InvariantCulture, $"match detection of web address {urls.Count}"),
                        "keypaste keeps no match rule for a web address"));
                }
            }
        }

        return urls;
    }

    /// <summary>
    /// The TOTP as an <c>otpauth://</c> URI: one the export holds as written, and a bare base32 secret or a <c>steam://</c> one
    /// made one as KeePassXC's own Bitwarden import makes it, without the spaces and padding that no code depends on.
    /// </summary>
    private static string Totp(string text, string title, string username)
    {
        const string steamScheme = "steam://";
        var trimmed = text.Trim();

        if (trimmed.Length == 0 || trimmed.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var steam = trimmed.StartsWith(steamScheme, StringComparison.OrdinalIgnoreCase);
        var secret = new string([.. (steam ? trimmed[steamScheme.Length..] : trimmed).Where(c => !char.IsWhiteSpace(c))]).TrimEnd('=');
        var uri = $"otpauth://totp/{Uri.EscapeDataString(title)}:{Uri.EscapeDataString(username)}?secret={Uri.EscapeDataString(secret)}";

        return steam ? uri + "&period=30&digits=5&encoder=steam" : uri;
    }

    /// <summary>
    /// Each passkey as KeePassXC's own Bitwarden import converts it: the credential id, a GUID or Bitwarden's <c>b64.</c> form, as its
    /// bytes in base64url, and the base64url PKCS #8 key as base64 between KeePassXC's PEM markers on one line (D-0571).
    /// </summary>
    private static List<ExportPasskey> Passkeys(JsonElement login, string where, List<ExportOmission> omitted)
    {
        List<ExportPasskey> passkeys = [];
        var list = ListOf(login, "fido2Credentials", where);

        for (var index = 0; index < list.Count; index++)
        {
            var passkeyWhere = string.Create(CultureInfo.InvariantCulture, $"{where}.fido2Credentials[{index}]");
            RequireObject(list[index], passkeyWhere);
            omitted.AddRange(Unknown(list[index], _passkeyMembers, "passkey."));

            var credentialId = CredentialId(Text(list[index], "credentialId", passkeyWhere));
            var key = Base64UrlBytes(Text(list[index], "keyValue", passkeyWhere));

            if (credentialId is null || key is null)
            {
                omitted.Add(new ExportOmission("passkey", credentialId is null ? "its credential id is neither a GUID nor Bitwarden's b64. form" : "its private key is not base64url"));
                continue;
            }

            // Only the first lands (ExportItem.Passkeys); each later one is named whole.
            if (passkeys.Count == 0 && PasskeyExtras(list[index]) is { } extras)
            {
                omitted.Add(extras);
            }

            try
            {
                passkeys.Add(new ExportPasskey(
                    credentialId,
                    $"-----BEGIN PRIVATE KEY-----{Convert.ToBase64String(key)}-----END PRIVATE KEY-----",
                    Text(list[index], "rpId", passkeyWhere),
                    Text(list[index], "userName", passkeyWhere),
                    Text(list[index], "userHandle", passkeyWhere)));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        return passkeys;
    }

    /// <summary>One line naming each part of a landing passkey that KeePassXC has no field for, or null when it holds none.</summary>
    private static ExportOmission? PasskeyExtras(JsonElement passkey)
    {
        List<string> held = [.. _passkeyExtras.Where(extra => passkey.TryGetProperty(extra.Member, out var value) && Holds(value)).Select(extra => extra.Named)];

        return held.Count switch
        {
            0 => null,
            1 => new ExportOmission($"passkey's {held[0]}", "KeePassXC has no field for it"),
            _ => new ExportOmission($"passkey's {string.Join(", ", held.SkipLast(1))} and {held[^1]}", "KeePassXC has no field for them"),
        };
    }

    /// <summary>A credential id's bytes in base64url, as Bitwarden's <c>parseCredentialId</c> reads them; null for one it does not.</summary>
    private static string? CredentialId(string text)
    {
        var bytes = text.StartsWith("b64.", StringComparison.Ordinal) ? Base64UrlBytes(text[4..])
            : Guid.TryParseExact(text, "D", out var guid) ? Convert.FromHexString(guid.ToString("N"))
            : null;

        return bytes is null ? null : Base64Url.EncodeToString(bytes);
    }

    private static byte[]? Base64UrlBytes(string text) =>
        text.Length > 0 && Base64Url.IsValid(text) ? Base64Url.DecodeFromChars(text) : null;

    private static List<ExportField> Fields(JsonElement element, string where, List<ExportOmission> omitted)
    {
        List<ExportField> fields = [];
        var list = ListOf(element, "fields", where);

        for (var index = 0; index < list.Count; index++)
        {
            var fieldWhere = string.Create(CultureInfo.InvariantCulture, $"{where}.fields[{index}]");
            RequireObject(list[index], fieldWhere);
            omitted.AddRange(Unknown(list[index], _fieldMembers, "field."));

            var name = Text(list[index], "name", fieldWhere);
            var type = Number(list[index], "type", fieldWhere) ?? _textField;

            if (type == _linkedField)
            {
                omitted.Add(new ExportOmission(
                    name.Length == 0 ? "a field with no name" : $"field {name}",
                    "it is a link to another of the item's own fields, which lands in its own right"));
            }
            else
            {
                // Bitwarden shows a text or a true-or-false field openly and masks a hidden one; one of a type it does not know stays masked.
                fields.Add(new ExportField(name, Text(list[index], "value", fieldWhere), Protect: type is not (_textField or _booleanField)));
            }
        }

        return fields;
    }

    /// <summary>Each earlier password as a version holding it and the item's other standard fields as they are now, since Bitwarden keeps only the password and when it was last used.</summary>
    private static List<ExportVersion> History(JsonElement element, string where, string username, string url, string notes, List<ExportOmission> omitted)
    {
        List<ExportVersion> versions = [];
        var list = ListOf(element, "passwordHistory", where);

        for (var index = 0; index < list.Count; index++)
        {
            var entryWhere = string.Create(CultureInfo.InvariantCulture, $"{where}.passwordHistory[{index}]");
            RequireObject(list[index], entryWhere);
            omitted.AddRange(Unknown(list[index], _historyMembers, "passwordHistory."));

            if (Text(list[index], "password", entryWhere) is not { Length: > 0 } password)
            {
                continue;
            }

            if (Date(list[index], "lastUsedDate", entryWhere) is { } when)
            {
                versions.Add(new ExportVersion(when, username, password, url, notes));
            }
            else
            {
                omitted.Add(new ExportOmission("an earlier password", "the export gives it no date"));
            }
        }

        return versions;
    }

    /// <summary>The folders or collections by id, each nesting as Bitwarden nests them, split on <c>/</c>.</summary>
    private static Dictionary<string, Place> Places(JsonElement root, string member)
    {
        Dictionary<string, Place> places = new(StringComparer.Ordinal);
        var list = ListOf(root, member, string.Empty);

        for (var index = 0; index < list.Count; index++)
        {
            var where = string.Create(CultureInfo.InvariantCulture, $"{member}[{index}]");
            RequireObject(list[index], where);

            var name = Text(list[index], "name", where);

            if (!places.TryAdd(Text(list[index], "id", where), new Place(name, name.Split('/', StringSplitOptions.RemoveEmptyEntries))))
            {
                throw new ExportFormatException($"{where} has the id of an earlier one");
            }
        }

        return places;
    }

    /// <summary>Each folder or collection that no item read is in and no landing item is under, named as holding none.</summary>
    private static IEnumerable<ExportOmission> Empty(Dictionary<string, Place> places, string noun, HashSet<string> referenced, HashSet<string> landed) =>
        places
            .Where(place => !referenced.Contains(place.Key) && place.Value.Segments.Count > 0 && !landed.Contains(string.Join('/', place.Value.Segments)))
            .Select(place => new ExportOmission($"{noun} {place.Value.Name}", "it holds no item"));

    /// <summary>Each member these rules do not read that holds anything, named under <paramref name="prefix"/>.</summary>
    private static List<ExportOmission> Unknown(JsonElement element, string[] known, string prefix) =>
        [.. element.EnumerateObject()
            .Where(member => !known.Contains(member.Name, StringComparer.Ordinal) && Holds(member.Value))
            .Select(member => new ExportOmission(prefix + member.Name, "keypaste does not read this part of a Bitwarden export"))];

    private static bool Holds(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.False => false,
        JsonValueKind.String => value.GetString()!.Length > 0,
        JsonValueKind.Array => value.GetArrayLength() > 0,
        JsonValueKind.Object => value.EnumerateObject().Any(),
        _ => true,
    };

    private static bool Present(JsonElement element, string member) =>
        element.TryGetProperty(member, out var value) && value.ValueKind != JsonValueKind.Null;

    private static void RequireObject(JsonElement element, string where)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ExportFormatException($"{where} is not a JSON object");
        }
    }

    private static string Text(JsonElement element, string member, string where) => OptionalText(element, member, where) ?? string.Empty;

    private static string? OptionalText(JsonElement element, string member, string where) =>
        !element.TryGetProperty(member, out var value) ? null : value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null => null,
            _ => throw new ExportFormatException($"{Join(where, member)} is not text"),
        };

    private static int? Number(JsonElement element, string member, string where) =>
        !element.TryGetProperty(member, out var value) || value.ValueKind == JsonValueKind.Null ? null
        : value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number
        : throw new ExportFormatException($"{Join(where, member)} is not a whole number");

    private static bool Flag(JsonElement element, string member, string where) =>
        element.TryGetProperty(member, out var value) && value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False or JsonValueKind.Null => false,
            _ => throw new ExportFormatException($"{Join(where, member)} is not true or false"),
        };

    private static DateTimeOffset? Date(JsonElement element, string member, string where) =>
        OptionalText(element, member, where) is not { } text ? null
        : DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when) ? when
        : throw new ExportFormatException($"{Join(where, member)} is not a date");

    private static JsonElement? OptionalObject(JsonElement element, string member, string where)
    {
        if (!element.TryGetProperty(member, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        RequireObject(value, Join(where, member));
        return value;
    }

    private static List<JsonElement>? OptionalList(JsonElement element, string member, string where) =>
        !element.TryGetProperty(member, out var value) || value.ValueKind == JsonValueKind.Null ? null
        : value.ValueKind == JsonValueKind.Array ? [.. value.EnumerateArray()]
        : throw new ExportFormatException($"{Join(where, member)} is not a list");

    private static List<JsonElement> ListOf(JsonElement element, string member, string where) => OptionalList(element, member, where) ?? [];

    private static string Join(string where, string member) => where.Length == 0 ? member : $"{where}.{member}";

    private static ReadOnlySpan<byte> WithoutBom(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith("﻿"u8) ? bytes[3..] : bytes;

    /// <summary>A Bitwarden item type: its number, the member holding its details, the details that land as custom fields, and its other members.</summary>
    private sealed record ItemType(int Number, string Member, IReadOnlyList<Detail> Details, IReadOnlyList<string> Others)
    {
        public string[] Members { get; } = [.. Details.Select(detail => detail.Member), .. Others];
    }

    /// <summary>One detail that lands as a custom field: its member, the field's name, and whether Bitwarden masks it.</summary>
    private sealed record Detail(string Member, string Field, bool Protect = false);

    /// <summary>A folder or collection: its name, and the groups it nests as.</summary>
    private sealed record Place(string Name, IReadOnlyList<string> Segments);
}
