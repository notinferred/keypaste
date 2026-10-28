using System.Text;
using Keypaste.Core.Import;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// 9.1b's Bitwarden reader over exports built by hand from Bitwarden's export models: every item type lands with its
/// details where the vault keeps them, every part a vault has no place for is named with why, an organization's
/// collections nest as groups, and an encrypted export is refused, with no value in anything said.
/// </summary>
public sealed class BitwardenJsonReaderTests : IDisposable
{
    private const string _master = "bitwarden-reader-master";

    // Every value in the fixtures that is no name carries this mark, so nothing said may hold it.
    private const string _valueMark = "bw-";

    private const string _personal = """
        {
          "encrypted": false,
          "folders": [
            { "id": "folder-projects", "name": "Work/Projects" },
            { "id": "folder-empty", "name": "Empty" },
            { "id": "folder-work", "name": "Work" }
          ],
          "items": [
            {
              "id": "item-github", "organizationId": null, "folderId": "folder-projects", "type": 1, "reprompt": 1,
              "name": "GitHub", "notes": "bw-notes-github", "favorite": true,
              "fields": [
                { "name": "Account", "value": "bw-field-text", "type": 0, "linkedId": null },
                { "name": "Recovery code", "value": "bw-field-hidden", "type": 1, "linkedId": null },
                { "name": "Admin", "value": "true", "type": 2, "linkedId": null },
                { "name": "Login name", "value": null, "type": 3, "linkedId": 100 }
              ],
              "login": {
                "fido2Credentials": [
                  {
                    "credentialId": "a3e15f8b-27c1-42ae-90cf-a615ad87854f", "keyType": "public-key", "keyAlgorithm": "ECDSA", "keyCurve": "P-256",
                    "keyValue": "MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgmr4GQQjerojFuf0ZouOuUllMvAwxZSZAfB6gwDYcLiehRANCAAT0WR5zVSp6ieusvjkLkzaGc7fjGBmwpiuLPxR_d-ZjqMI9L2DKh-takp6wGt2x0n4jzr1KA352NZg0vjZX9CHh",
                    "rpId": "webauthn.io", "userHandle": "aTFtdmFnOHYtS2dxVEJ0by1rSFpLWGg0enlTVC1iUVJReDZ5czJXa3c2aw", "userName": "KPXC_BITWARDEN",
                    "counter": "0", "rpName": "webauthn.io", "userDisplayName": "KPXC_BITWARDEN", "discoverable": "true",
                    "creationDate": "2024-10-23T16:38:08.617Z"
                  },
                  {
                    "credentialId": "b64.AQIDBA", "keyType": "public-key", "keyAlgorithm": "ECDSA", "keyCurve": "P-256",
                    "keyValue": "AQIDBA", "rpId": "second.example", "userHandle": "AQIDBA", "userName": "bw-user-second", "counter": "0"
                  }
                ],
                "uris": [
                  { "match": null, "uri": "https://github.example/bw-login" },
                  { "match": 3, "uri": "https://github.example/bw-sso" },
                  { "match": null, "uri": "" },
                  { "match": null, "uri": "https://gist.example/bw-gist" }
                ],
                "username": "bw-user-github",
                "password": "bw-pw-github",
                "totp": "gezd gnbv gy3t qojq gezd gnbv gy3t qojq"
              },
              "collectionIds": null,
              "passwordHistory": [
                { "lastUsedDate": "2025-05-06T07:08:09.000Z", "password": "bw-old-pw-github-2" },
                { "lastUsedDate": "2024-03-04T05:06:07.000Z", "password": "bw-old-pw-github-1" },
                { "lastUsedDate": null, "password": "bw-old-pw-github-undated" }
              ],
              "revisionDate": "2025-06-07T08:09:10.000Z",
              "creationDate": "2024-01-02T03:04:05.000Z",
              "deletedDate": null,
              "archivedDate": null
            },
            { "id": "item-steam", "type": 1, "name": "Steam", "login": { "password": "bw-pw-steam", "totp": "steam://GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ" } },
            {
              "id": "item-bank", "type": 1, "name": "Bank", "archivedDate": "2025-01-01T00:00:00.000Z",
              "login": { "totp": "otpauth://totp/Bank?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZA&algorithm=SHA256&digits=8" }
            },
            { "id": "item-counter", "type": 1, "name": "Counter", "login": { "totp": "otpauth://hotp/Counter?secret=JBSWY3DPEHPK3PXP&counter=1" } },
            { "id": "item-note", "type": 2, "name": "Recovery", "notes": "bw-notes-recovery", "secureNote": { "type": 0 } },
            {
              "id": "item-card", "type": 3, "name": "Visa",
              "card": { "cardholderName": "bw-card-holder", "brand": "Visa", "number": "bw-card-number", "expMonth": "4", "expYear": "2030", "code": "bw-card-code" }
            },
            {
              "id": "item-identity", "type": 4, "name": "Me",
              "identity": { "title": "Dr", "firstName": "bw-first", "lastName": "bw-last", "email": "bw-mail@example.com", "ssn": "bw-ssn", "username": "bw-identity-user" }
            },
            {
              "id": "item-ssh", "type": 5, "name": "Server key",
              "sshKey": { "privateKey": "bw-ssh-private", "publicKey": "ssh-ed25519 bw-ssh-public", "keyFingerprint": "SHA256:bw-ssh-fingerprint" }
            },
            { "id": "item-account", "type": 6, "name": "Checking", "bankAccount": { "bankName": "Acme", "accountNumber": "bw-account-number" } },
            { "id": "item-old", "type": 1, "name": "Old", "deletedDate": "2025-01-01T00:00:00.000Z", "login": { "password": "bw-pw-old" } },
            { "id": "item-mystery", "type": 99, "name": "Mystery", "notes": "bw-notes-mystery" },
            { "id": "item-future", "type": 2, "name": "Future", "secureNote": { "type": 0 }, "futureThing": "bw-future" },
            {
              "id": "item-sign-in", "type": 1, "name": "Sign-in",
              "login": { "fido2Credentials": [ { "credentialId": "b64.AQIDBA", "keyValue": "BAMCAQ", "rpId": "sign-in.example", "userHandle": "BQYHCA", "userName": "bw-user-sign-in", "counter": "3" } ] }
            },
            {
              "id": "item-broken", "type": 1, "name": "Broken",
              "login": { "fido2Credentials": [ { "credentialId": "bw-not-a-credential-id", "keyValue": "BAMCAQ", "rpId": "broken.example" } ] }
            }
          ]
        }
        """;

    // KeePassXC's own Bitwarden passkey test (tests/TestImports.cpp, testBitwardenPasskey) expects these from the first passkey above.
    private const string _keePassXcCredentialId = "o-FfiyfBQq6Qz6YVrYeFTw";
    private const string _keePassXcPem =
        "-----BEGIN PRIVATE KEY-----MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgmr4GQQjerojFuf0ZouOuUllMvAwxZSZAfB6gwDYcLiehRANCAAT0WR5zVS"
        + "p6ieusvjkLkzaGc7fjGBmwpiuLPxR/d+ZjqMI9L2DKh+takp6wGt2x0n4jzr1KA352NZg0vjZX9CHh-----END PRIVATE KEY-----";
    private const string _keePassXcUserHandle = "aTFtdmFnOHYtS2dxVEJ0by1rSFpLWGg0enlTVC1iUVJReDZ5czJXa3c2aw";

    private const string _organization = """
        {
          "encrypted": false,
          "collections": [
            { "id": "collection-backend", "organizationId": "org", "name": "Engineering/Backend", "externalId": null },
            { "id": "collection-ops", "organizationId": "org", "name": "Ops", "externalId": null },
            { "id": "collection-unused", "organizationId": "org", "name": "Unused", "externalId": null }
          ],
          "items": [
            {
              "id": "item-db", "organizationId": "org", "folderId": null, "type": 1, "name": "Database",
              "login": { "username": "bw-db-user", "password": "bw-db-pw" }, "collectionIds": ["collection-backend", "collection-ops"]
            },
            { "id": "item-orphan", "organizationId": "org", "type": 2, "name": "Orphan", "notes": "bw-notes-orphan", "collectionIds": ["collection-gone"] }
          ]
        }
        """;

    private static readonly ExportOmission _noAttachments = new("attachments", "a Bitwarden .json export holds none; save each from Bitwarden and add it to its item");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-bitwarden-tests-").FullName;
    private readonly string _vaultPath;
    private readonly Vault _vault;

    public BitwardenJsonReaderTests()
    {
        _vaultPath = Path.Combine(_directory, "vault.kdbx");
        _vault = Vault.Create(_vaultPath, _master);
    }

    public void Dispose()
    {
        _vault.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Every_item_lands_or_is_named_and_every_part_a_vault_has_no_place_for_is_named_with_why()
    {
        using var source = Open("bitwarden_export.json", _personal);

        Assert.Equal("Bitwarden JSON", source.Format);
        Assert.Equal(
            ["GitHub", "Steam", "Bank", "Counter", "Recovery", "Visa", "Me", "Server key", "Checking", "Future", "Sign-in", "Broken"],
            source.Items.Select(item => item.Title));
        Assert.Equal(
            [
                new ExportOmission("item Old", "it is in Bitwarden's trash"),
                new ExportOmission("item Mystery", "keypaste does not read Bitwarden's item type 99"),
                new ExportOmission("folder Empty", "it holds no item"),
                _noAttachments,
            ],
            source.Unread);

        var plan = source.Plan(_vault, "imported");

        Assert.Null(plan.Refusal);
        Assert.Equal(new EntryName("imported/Work/Projects", "GitHub"), plan.Rows[0].Lands);
        Assert.All(plan.Rows.Skip(1), row => Assert.Equal("imported", row.Lands!.GroupPath));
        Assert.Equal(
            [
                new ExportLoss(-1, string.Empty, "item Old", "it is in Bitwarden's trash"),
                new ExportLoss(-1, string.Empty, "item Mystery", "keypaste does not read Bitwarden's item type 99"),
                new ExportLoss(-1, string.Empty, "folder Empty", "it holds no item"),
                new ExportLoss(-1, string.Empty, _noAttachments.Part, _noAttachments.Reason),
                new ExportLoss(0, "Work/Projects/GitHub", "passkey 2", "KeePassXC keeps one passkey per item, so only the item's first lands"),
                new ExportLoss(0, "Work/Projects/GitHub", "match detection of web address 2", "keypaste keeps no match rule for a web address"),
                new ExportLoss(0, "Work/Projects/GitHub", "passkey's counter, site name, display name, discoverable flag and creation date", "KeePassXC has no field for them"),
                new ExportLoss(0, "Work/Projects/GitHub", "field Login name", "it is a link to another of the item's own fields, which lands in its own right"),
                new ExportLoss(0, "Work/Projects/GitHub", "master password re-prompt", "keypaste asks for no password to show one item"),
                new ExportLoss(0, "Work/Projects/GitHub", "an earlier password", "the export gives it no date"),
                new ExportLoss(3, "Counter", "TOTP", "The otp field holds a counter-based HOTP, which keypaste does not compute."),
                new ExportLoss(9, "Future", "futureThing", "keypaste does not read this part of a Bitwarden export"),
                new ExportLoss(10, "Sign-in", "passkey's counter", "KeePassXC has no field for it"),
                new ExportLoss(11, "Broken", "passkey", "its credential id is neither a GUID nor Bitwarden's b64. form"),
            ],
            plan.Losses);
    }

    [Fact]
    public void A_login_lands_with_its_addresses_its_totp_its_fields_its_history_and_its_times()
    {
        using var source = Open("bitwarden_export.json", _personal);
        source.Commit(_vault, source.Plan(_vault, "imported"), vault => vault.Save());

        using var reopened = Vault.Open(_vaultPath, _master);
        var github = new EntryName("imported/Work/Projects", "GitHub");
        var entry = reopened.Find(github)!;

        Assert.Equal(("bw-user-github", "bw-pw-github", "https://github.example/bw-login", "bw-notes-github"), (entry.Username, entry.Password, entry.Url, entry.Notes));
        Assert.Equal(
            [
                new EntryField("Account", false, false), new EntryField("Admin", false, false), new EntryField("KP2A_URL_1", false, true),
                new EntryField("KP2A_URL_2", false, true), new EntryField("KPEX_PASSKEY_CREDENTIAL_ID", true, true),
                new EntryField("KPEX_PASSKEY_PRIVATE_KEY_PEM", true, true), new EntryField("KPEX_PASSKEY_RELYING_PARTY", true, true),
                new EntryField("KPEX_PASSKEY_USERNAME", true, true), new EntryField("KPEX_PASSKEY_USER_HANDLE", true, true),
                new EntryField("Recovery code", true, false), new EntryField("otp", true, true),
            ],
            reopened.Fields(github));
        Assert.Equal(
            ("bw-field-text", "bw-field-hidden", "true", "https://github.example/bw-sso", "https://gist.example/bw-gist"),
            (reopened.ReadField(github, "Account"), reopened.ReadField(github, "Recovery code"), reopened.ReadField(github, "Admin"),
                reopened.ReadField(github, "KP2A_URL_1"), reopened.ReadField(github, "KP2A_URL_2")));
        Assert.Equal(["Favorite", "Passkey"], reopened.Tags(github)!.Order(StringComparer.Ordinal));
        Assert.Equal(["Archived"], reopened.Tags(new EntryName("imported", "Bank")));

        var history = reopened.ReadHistory(github)!.OrderBy(revision => revision.ModifiedUtc).ToList();
        Assert.Equal(["bw-old-pw-github-1", "bw-old-pw-github-2"], history.Select(revision => revision.Fields.Password));
        Assert.Equal([new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Utc), new DateTime(2025, 5, 6, 7, 8, 9, DateTimeKind.Utc)], history.Select(revision => revision.ModifiedUtc));
        Assert.All(history, revision => Assert.Equal(("bw-user-github", "https://github.example/bw-login", "bw-notes-github"), (revision.Fields.Username, revision.Fields.Url, revision.Fields.Notes)));
        Assert.Equal(
            (new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero), new DateTimeOffset(2025, 6, 7, 8, 9, 10, TimeSpan.Zero)),
            (reopened.ReadTimes(github)!.Created, reopened.ReadTimes(github)!.Modified));
        Assert.Empty(reopened.Fields(new EntryName("imported", "Counter"))!);
    }

    [Fact]
    public void A_passkey_lands_in_the_fields_keepassxc_reads_as_keepassxcs_own_bitwarden_import_writes_them()
    {
        using var source = Open("bitwarden_export.json", _personal);
        source.Commit(_vault, source.Plan(_vault, "imported"), vault => vault.Save());

        using var reopened = Vault.Open(_vaultPath, _master);
        var github = new EntryName("imported/Work/Projects", "GitHub");
        var signIn = new EntryName("imported", "Sign-in");

        Assert.Equal(
            (_keePassXcCredentialId, _keePassXcPem, "webauthn.io", "KPXC_BITWARDEN", _keePassXcUserHandle),
            (reopened.ReadField(github, "KPEX_PASSKEY_CREDENTIAL_ID"), reopened.ReadField(github, "KPEX_PASSKEY_PRIVATE_KEY_PEM"),
                reopened.ReadField(github, "KPEX_PASSKEY_RELYING_PARTY"), reopened.ReadField(github, "KPEX_PASSKEY_USERNAME"),
                reopened.ReadField(github, "KPEX_PASSKEY_USER_HANDLE")));
        Assert.Equal(
            ("AQIDBA", "-----BEGIN PRIVATE KEY-----BAMCAQ==-----END PRIVATE KEY-----", "sign-in.example", "bw-user-sign-in", "BQYHCA"),
            (reopened.ReadField(signIn, "KPEX_PASSKEY_CREDENTIAL_ID"), reopened.ReadField(signIn, "KPEX_PASSKEY_PRIVATE_KEY_PEM"),
                reopened.ReadField(signIn, "KPEX_PASSKEY_RELYING_PARTY"), reopened.ReadField(signIn, "KPEX_PASSKEY_USERNAME"),
                reopened.ReadField(signIn, "KPEX_PASSKEY_USER_HANDLE")));
        Assert.Equal(["Passkey"], reopened.Tags(signIn));
        Assert.Empty(reopened.Fields(new EntryName("imported", "Broken"))!);
        Assert.Empty(reopened.Tags(new EntryName("imported", "Broken"))!);
    }

    /// <summary>A bare secret, as Bitwarden shows Google's, and a <c>steam://</c> one give the codes Bitwarden computes from them, RFC 6238's at 59 seconds and Steam's for that seed, and an <c>otpauth</c> URI lands as written.</summary>
    [Theory]
    [InlineData("GitHub", "imported/Work/Projects", "287082", false)]
    [InlineData("Steam", "imported", "PV9M4", true)]
    [InlineData("Bank", "imported", "46119246", false)]
    public void Each_totp_form_bitwarden_holds_lands_in_otp_and_gives_its_codes(string title, string group, string code, bool steam)
    {
        using var source = Open("bitwarden_export.json", _personal);
        source.Commit(_vault, source.Plan(_vault, "imported"), vault => vault.Save());

        using var reopened = Vault.Open(_vaultPath, _master);
        using var totp = reopened.ReadTotp(new EntryName(group, title), out var refusal);

        Assert.Null(refusal);
        Assert.Equal(steam, totp!.IsSteam);
        Assert.Equal(code, totp.Code(DateTimeOffset.FromUnixTimeSeconds(59)).Value);
    }

    [Fact]
    public void Every_other_item_type_lands_its_details_as_fields_protected_where_bitwarden_masks_them()
    {
        using var source = Open("bitwarden_export.json", _personal);
        source.Commit(_vault, source.Plan(_vault, "imported"), vault => vault.Save());

        using var reopened = Vault.Open(_vaultPath, _master);

        Assert.Equal("bw-notes-recovery", reopened.Find(new EntryName("imported", "Recovery"))!.Notes);
        Assert.Empty(reopened.Fields(new EntryName("imported", "Recovery"))!);
        Assert.Equal(
            [Plain("Brand"), Plain("Cardholder name"), Plain("Expiration month"), Plain("Expiration year"), Masked("Number"), Masked("Security code")],
            reopened.Fields(new EntryName("imported", "Visa")));
        Assert.Equal("bw-card-number", reopened.ReadField(new EntryName("imported", "Visa"), "Number"));
        Assert.Equal("bw-identity-user", reopened.Find(new EntryName("imported", "Me"))!.Username);
        Assert.Equal(
            [Plain("Email"), Plain("First name"), Plain("Last name"), Plain("Name title"), Masked("Social Security number")],
            reopened.Fields(new EntryName("imported", "Me")));
        Assert.Equal(
            [Plain("Fingerprint"), Masked("Private key"), Plain("Public key")],
            reopened.Fields(new EntryName("imported", "Server key")));
        Assert.Equal("bw-ssh-private", reopened.ReadField(new EntryName("imported", "Server key"), "Private key"));
        Assert.Equal([Masked("Account number"), Plain("Bank name")], reopened.Fields(new EntryName("imported", "Checking")));
        Assert.Null(reopened.Find(new EntryName("imported", "Old")));
        Assert.Null(reopened.Find(new EntryName("imported", "Mystery")));
    }

    [Fact]
    public void An_organizations_items_land_in_their_first_collection_and_every_other_is_named()
    {
        using var source = Open("org_export.json", _organization);

        Assert.Equal(["Engineering", "Backend"], source.Items[0].Folder);
        Assert.Equal([new ExportOmission("collection Ops", "the item lands once, in Engineering/Backend")], source.Items[0].Omitted);
        Assert.Empty(source.Items[1].Folder);
        Assert.Equal([new ExportOmission("collection", "the export lists no collection of one of this item's collection ids")], source.Items[1].Omitted);
        Assert.Equal([new ExportOmission("collection Unused", "it holds no item"), _noAttachments], source.Unread);

        source.Commit(_vault, source.Plan(_vault, into: null), vault => vault.Save());

        Assert.Equal("bw-db-pw", _vault.Find(new EntryName("org_export/Engineering/Backend", "Database"))!.Password);
        Assert.Equal("bw-notes-orphan", _vault.Find(new EntryName("org_export", "Orphan"))!.Notes);
    }

    [Theory]
    [InlineData(
        """{ "encrypted": true, "passwordProtected": true, "salt": "c2FsdA==", "kdfType": 0, "kdfIterations": 600000, "encKeyValidation_DO_NOT_EDIT": "2.bw-a|bw-b|bw-c", "data": "2.bw-d|bw-e|bw-f" }""",
        "it is password-protected. Export your vault from Bitwarden again with the .json file format, which is not encrypted")]
    [InlineData(
        """{ "encrypted": true, "encKeyValidation_DO_NOT_EDIT": "2.bw-a|bw-b|bw-c", "folders": [], "items": [ { "type": 1, "name": "2.bw-g|bw-h|bw-i" } ] }""",
        "it is encrypted with your Bitwarden account's key, which only Bitwarden reads. Export your vault again with the .json file format, which is not encrypted")]
    public void An_encrypted_export_is_refused_saying_how_to_export_one_keypaste_reads(string json, string reason)
    {
        var refused = Assert.Throws<VaultException>(() => Open("bitwarden_encrypted_export.json", json));

        Assert.Equal($"bitwarden_encrypted_export.json is not a Bitwarden JSON export keypaste reads: {reason}", refused.Message);
    }

    [Theory]
    [InlineData("""{ "encrypted": false, "items": [ { "type": 1, "name": "Mail", "login": { "username": 7 } } ] }""", "items[0].login.username is not text")]
    [InlineData("""{ "encrypted": false, "items": [ { "name": "bw-untyped" } ] }""", "items[0] has no type")]
    [InlineData("""{ "encrypted": false, "items": [ { "type": 1, "name": "Mail", "revisionDate": "bw-not-a-date" } ] }""", "items[0].revisionDate is not a date")]
    [InlineData("""{ "encrypted": false, "folders": [] }""", "it has no items list")]
    [InlineData("{ \"encrypted\": false,\n  \"items\": [ { \"name\": \"bw-cut", "it is not JSON keypaste reads at line 2: a value is malformed or a name is given twice")]
    public void A_file_holding_something_bitwarden_does_not_write_is_refused_naming_the_place_and_no_value(string json, string reason)
    {
        var refused = Assert.Throws<VaultException>(() => Open("bitwarden_export.json", json));

        Assert.Equal($"bitwarden_export.json is not a Bitwarden JSON export keypaste reads: {reason}", refused.Message);
        Assert.DoesNotContain(_valueMark, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_given_twice_is_refused_rather_than_one_value_chosen()
    {
        var refused = Assert.Throws<VaultException>(() => Open("bitwarden_export.json", """{ "encrypted": false, "items": [ { "type": 1, "name": "Mail", "login": { "password": "bw-one", "password": "bw-two" } } ] }"""));

        Assert.StartsWith("bitwarden_export.json is not a Bitwarden JSON export keypaste reads: it is not JSON keypaste reads", refused.Message, StringComparison.Ordinal);
        Assert.EndsWith(": a value is malformed or a name is given twice", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_valueMark, refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\n  \"encrypted\": false,\n  \"folders\": [", true)]
    [InlineData("﻿{ \"encrypted\": true, \"passwordProtected\": true", true)]
    [InlineData("{ \"items\": [", true)]
    [InlineData("{ \"collections\": [ { \"id\"", true)]
    [InlineData("{ \"encrypted\": \"no\" }", false)]
    [InlineData("{ \"name\": \"export\", \"items\": [] }", false)]
    [InlineData("[ { \"encrypted\": false } ]", false)]
    [InlineData("url,username,password,extra,name,grouping,fav\n", false)]
    [InlineData("", false)]
    public void Only_a_bitwarden_json_export_is_recognized(string start, bool recognized) =>
        Assert.Equal(recognized, new BitwardenJsonReader().Recognizes("export.json", Encoding.UTF8.GetBytes(start)));

    [Fact]
    public void A_kdbx_file_is_not_recognized() =>
        Assert.False(new BitwardenJsonReader().Recognizes("vault.json", [0x03, 0xD9, 0xA2, 0x9A, 0x67, 0xFB, 0x4B, 0xB5, 0x01, 0x00, 0x04, 0x00]));

    [Fact]
    public void Nothing_said_about_an_export_holds_a_value()
    {
        using var source = Open("bitwarden_export.json", _personal);
        var plan = source.Plan(_vault, "imported");

        var said = new List<string?> { plan.Into, plan.Refusal };
        said.AddRange(plan.Rows.SelectMany(row => new[] { row.Item, row.Note, row.ToString() }));
        said.AddRange(plan.Losses.Select(loss => loss.ToString()));
        said.AddRange(source.Unread.Select(unread => unread.ToString()));
        said.AddRange(source.Items.SelectMany(item => item.Omitted).Select(omitted => omitted.ToString()));
        said.AddRange(source.Items.Select(item => item.ToString()));
        said.AddRange(source.Items.SelectMany(item => item.Fields).Select(field => field.ToString()));
        said.AddRange(source.Items.SelectMany(item => item.History).Select(version => version.ToString()));
        said.AddRange(source.Items.SelectMany(item => item.Passkeys).Select(passkey => passkey.ToString()));

        string[] values = [_valueMark, _keePassXcCredentialId, "MIGHAgEA", _keePassXcUserHandle, "KPXC_BITWARDEN", "AQIDBA", "BAMCAQ", "webauthn.io"];
        Assert.All(values, value => Assert.DoesNotContain(said, text => text is not null && text.Contains(value, StringComparison.Ordinal)));
    }

    private static EntryField Plain(string name) => new(name, false, false);

    private static EntryField Masked(string name) => new(name, true, false);

    private ExportSource Open(string name, string json)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, json);
        return ExportImport.Open(path, ExportImport.Readers) ?? throw new InvalidOperationException($"no reader recognized {name}");
    }
}
