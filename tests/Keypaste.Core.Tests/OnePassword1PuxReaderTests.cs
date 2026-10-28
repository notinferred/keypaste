using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Keypaste.Core.Import;
using Keypaste.Core.Internal;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// 9.1d1's 1PUX reader over archives each test builds in memory from 1Password's documented layout and KeePassXC's own
/// test export: every vault's items land with their fields, TOTP, files, history and times, every part that lands nowhere
/// is named with why, and an archive past a cap, with a path that leaves it, or damaged is refused before it is unpacked,
/// with no value in anything said.
/// </summary>
public sealed class OnePassword1PuxReaderTests : IDisposable
{
    private const string _master = "onepassword-1pux-master";

    // Every value in the fixtures that is no name carries this mark, so nothing said may hold it.
    private const string _valueMark = "op-";

    // RFC 6238's SHA-1 seed in base32, as a person types it into 1Password, whose code at 59 seconds is 287082.
    private const string _seed = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    private const string _export = """
        {
          "accounts": [
            {
              "attrs": { "accountName": "Ada", "name": "Ada", "avatar": "", "email": "op-ada@example.com", "uuid": "ACCOUNT", "domain": "https://my.1password.com/" },
              "vaults": [
                {
                  "attrs": { "uuid": "vault-personal", "desc": "op-vault-description", "avatar": "personal.png", "name": "Personal", "type": "P" },
                  "items": [
                    {
                      "uuid": "item-github", "favIndex": 1, "createdAt": 1704164645, "updatedAt": 1749283750, "state": "active", "categoryUuid": "001",
                      "futureThing": "op-future",
                      "overview": {
                        "subtitle": "op-user-github", "title": "GitHub", "url": "https://github.example/op-login",
                        "urls": [
                          { "label": "website", "url": "https://github.example/op-login" },
                          { "label": "Gists", "url": "https://gist.example/op-gist", "mode": "host" },
                          { "label": "", "url": "" }
                        ],
                        "tags": [ "dev", "" ], "ps": 80, "pbe": 70.5, "pgrng": true,
                        "watchtowerExclusions": { "weak": true, "reused": false }
                      },
                      "details": {
                        "loginFields": [
                          { "value": "op-user-github", "id": "", "name": "login", "fieldType": "T", "designation": "username" },
                          { "value": "op-pw-github", "id": "", "name": "password", "fieldType": "P", "designation": "password" },
                          { "value": "✓", "id": "", "name": "remember", "fieldType": "C" },
                          { "value": "op-pin-github", "id": "", "name": "pin", "fieldType": "P" }
                        ],
                        "notesPlain": "op-notes-github",
                        "sections": [
                          {
                            "title": "", "name": "add more",
                            "fields": [
                              { "title": "one-time password", "id": "TOTP_one", "value": { "totp": "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ" }, "indexAtSource": 0, "guarded": false },
                              { "title": "backup code", "id": "TOTP_two", "value": { "totp": "otpauth://totp/Backup?secret=op-second" } },
                              { "title": "Recovery code", "id": "recovery", "value": { "concealed": "op-recovery" } },
                              { "title": "password", "id": "old", "value": { "concealed": "op-pw-github" } },
                              { "title": "empty", "id": "empty", "value": { "string": "" } }
                            ]
                          },
                          {
                            "title": "Security", "name": "Section_security",
                            "fields": [
                              { "title": "PIN", "id": "pin", "value": { "concealed": "op-security-pin" } },
                              { "title": "PIN", "id": "pin2", "value": { "concealed": "op-security-pin-2" } },
                              { "title": "", "id": "since", "value": { "date": 1451649660 } },
                              { "title": "renewal", "id": "renewal", "value": { "monthYear": 202706 } }
                            ]
                          }
                        ],
                        "passwordHistory": [
                          { "value": "op-old-pw-github-2", "time": 1746515289 },
                          { "value": "op-old-pw-github-1", "time": 1709528767 },
                          { "value": "op-old-pw-undated" }
                        ]
                      }
                    },
                    {
                      "uuid": "item-visa", "favIndex": 0, "createdAt": 1700000000, "updatedAt": 1700000000, "state": "active", "categoryUuid": "002",
                      "overview": { "subtitle": "op-visa-subtitle", "title": "Visa", "url": "" },
                      "details": {
                        "loginFields": [], "notesPlain": "",
                        "sections": [
                          {
                            "title": "", "name": "",
                            "fields": [
                              { "title": "cardholder name", "id": "cardholder", "value": { "string": "op-holder" } },
                              { "title": "type", "id": "type", "value": { "creditCardType": "visa" } },
                              { "title": "number", "id": "ccnum", "value": { "creditCardNumber": "op-card-number" } },
                              { "title": "verification number", "id": "cvv", "value": { "concealed": "op-card-cvv" } },
                              { "title": "expiry date", "id": "expiry", "value": { "monthYear": 202706 } }
                            ]
                          },
                          { "title": "Contact Information", "name": "contactInfo", "fields": [ { "title": "phone (local)", "id": "phoneLocal", "value": { "phone": "" } } ] }
                        ],
                        "passwordHistory": []
                      }
                    },
                    {
                      "uuid": "item-me", "favIndex": 0, "state": "active", "categoryUuid": "004",
                      "overview": { "title": "Me", "url": "" },
                      "details": {
                        "sections": [
                          {
                            "title": "Identification", "name": "name",
                            "fields": [
                              { "title": "first name", "id": "firstname", "value": { "string": "op-first" } },
                              { "title": "birth date", "id": "birthdate", "value": { "date": 1451649660 } }
                            ]
                          },
                          {
                            "title": "Address", "name": "address",
                            "fields": [ { "title": "address", "id": "address", "value": { "address": { "street": "op-street", "city": "Boston", "country": "us", "zip": "12345", "state": "MA" } } } ]
                          },
                          {
                            "title": "Internet Details", "name": "internet",
                            "fields": [
                              { "title": "email", "id": "email", "value": { "email": { "email_address": "op-me@example.com", "provider": "op-provider" } } },
                              { "title": "username", "id": "username", "value": { "string": "" } }
                            ]
                          }
                        ]
                      }
                    },
                    {
                      "uuid": "item-scan", "favIndex": 0, "state": "active", "categoryUuid": "006",
                      "overview": { "title": "Scan", "url": "" },
                      "details": { "documentAttributes": { "fileName": "scan.pdf", "documentId": "doc-scan", "decryptedSize": 13 } }
                    },
                    {
                      "uuid": "item-recovery", "favIndex": 0, "state": "archived", "categoryUuid": "003",
                      "overview": { "title": "Recovery", "url": "", "icons": "op-icon" },
                      "details": {
                        "notesPlain": "op-notes-recovery",
                        "sections": [
                          {
                            "title": "",
                            "fields": [
                              { "title": "photo", "id": "photo", "value": { "file": { "fileName": "photo.png", "documentId": "doc-photo", "decryptedSize": 4 } } },
                              { "title": "missing", "id": "missing", "value": { "file": { "fileName": "gone.txt", "documentId": "doc-gone" } } },
                              { "title": "related", "id": "related", "value": { "reference": "item-github" } },
                              { "title": "mood", "id": "mood", "value": { "futureKind": "op-mood" } },
                              { "title": "shape", "id": "shape", "value": { "futureShape": { "corners": 4 } } }
                            ]
                          }
                        ]
                      }
                    },
                    {
                      "uuid": "item-ssh", "state": "active", "categoryUuid": "114",
                      "overview": { "title": "Server key" },
                      "details": {
                        "sections": [
                          {
                            "title": "",
                            "fields": [
                              {
                                "title": "private key", "id": "private_key",
                                "value": {
                                  "sshKey": {
                                    "privateKey": "op-ssh-stored",
                                    "metadata": { "privateKey": "op-ssh-described", "publicKey": "ssh-ed25519 op-ssh-public", "fingerprint": "SHA256:op-ssh-fingerprint", "keyType": "ed25519" }
                                  }
                                }
                              }
                            ]
                          }
                        ]
                      }
                    },
                    {
                      "uuid": "item-db", "state": "active", "categoryUuid": "102",
                      "overview": { "title": "Postgres", "url": "" },
                      "details": {
                        "sections": [
                          {
                            "title": "",
                            "fields": [
                              { "title": "server", "id": "hostname", "value": { "string": "op-db-host" } },
                              { "title": "username", "id": "username", "value": { "string": "op-db-user" } },
                              { "title": "password", "id": "password", "value": { "concealed": "op-db-pw" } },
                              { "title": "URL", "id": "url", "value": { "url": "https://db.example/op-console" } },
                              { "title": "notes", "id": "notes", "value": { "string": "op-db-notes-field" } }
                            ]
                          }
                        ]
                      }
                    },
                    { "categoryUuid": "005", "details": { "loginFields": [], "sections": [], "passwordHistory": [], "password": "op-pw-door" }, "overview": { "title": "Door code" } },
                    {
                      "uuid": "item-old", "state": "trashed", "categoryUuid": "001", "overview": { "title": "Old" },
                      "details": { "loginFields": [ { "value": "op-pw-old", "designation": "password" } ] }
                    },
                    { "uuid": "item-mystery", "state": "frozen", "categoryUuid": "999", "overview": { "title": "Mystery" }, "details": { "notesPlain": "op-notes-mystery" } }
                  ]
                },
                {
                  "attrs": { "uuid": "vault-shared", "desc": "", "avatar": "", "name": "Shared", "type": "U" },
                  "items": [
                    {
                      "uuid": "item-router", "state": "active", "categoryUuid": "109",
                      "overview": { "title": "Router" },
                      "details": {
                        "sections": [
                          {
                            "title": "",
                            "fields": [
                              { "title": "base station password", "id": "password", "value": { "concealed": "op-router-pw" } },
                              { "title": "network name", "id": "network_name", "value": { "string": "op-network" } }
                            ]
                          }
                        ]
                      }
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

    private static readonly byte[] _scan = Encoding.UTF8.GetBytes("op-scan-bytes");
    private static readonly byte[] _photo = [0x89, 0x50, 0x4E, 0x47];

    private static readonly (string Path, byte[] Content)[] _files =
    [
        ("files/", []),
        ("files/doc-scan__scan.pdf", _scan),
        ("files/doc-photo__photo.png", _photo),
        ("files/personal.png", [1, 2, 3]),
        ("files/orphan__left.txt", Encoding.UTF8.GetBytes("op-orphan")),
    ];

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-1pux-tests-").FullName;
    private readonly string _vaultPath;
    private readonly Vault _vault;

    public OnePassword1PuxReaderTests()
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
    public void Every_item_lands_in_its_vaults_group_or_is_named_and_every_part_that_lands_nowhere_is_named_with_why()
    {
        using var source = Open(Archive(_export, _files));

        Assert.Equal("1Password 1PUX", source.Format);
        Assert.Equal(
            ["GitHub", "Visa", "Me", "Scan", "Recovery", "Server key", "Postgres", "Door code", "Mystery", "Router"],
            source.Items.Select(item => item.Title));
        Assert.Equal(
            [
                new ExportOmission("item Old", "it is in 1Password's Recently Deleted"),
                new ExportOmission("description of vault Personal", "keypaste keeps no description for a group"),
                new ExportOmission("icon of vault Personal", "keypaste keeps no icon for a group"),
                new ExportOmission("file files/orphan__left.txt", "no item the import copies holds it"),
                new ExportOmission("passkeys", "1Password leaves every passkey out of a 1PUX export"),
            ],
            source.Unread);

        var plan = source.Plan(_vault, "imported");

        Assert.Null(plan.Refusal);
        Assert.All(plan.Rows.Take(9), row => Assert.Equal("imported/Personal", row.Lands!.GroupPath));
        Assert.Equal(new EntryName("imported/Shared", "Router"), plan.Rows[9].Lands);

        const string unread = "keypaste does not read this part of a 1Password export";
        Assert.Equal(
            [
                .. source.Unread.Select(omission => new ExportLoss(-1, string.Empty, omission.Part, omission.Reason)),
                new ExportLoss(0, "Personal/GitHub", "futureThing", unread),
                new ExportLoss(0, "Personal/GitHub", "label of web address 2", "keypaste keeps no label for a web address"),
                new ExportLoss(0, "Personal/GitHub", "autofill setting of web address 2", "keypaste keeps no autofill setting for a web address"),
                new ExportLoss(0, "Personal/GitHub", "Watchtower exclusions", "keypaste keeps the recommendations you dismiss on each computer"),
                new ExportLoss(0, "Personal/GitHub", "TOTP 2", "keypaste and KeePassXC compute one TOTP per item, so only the item's first lands"),
                new ExportLoss(0, "Personal/GitHub", "an earlier password", "the export gives it no time"),
                new ExportLoss(4, "Personal/Recovery", "icon", "keypaste sets no item's icon"),
                new ExportLoss(4, "Personal/Recovery", "attachment gone.txt", "the export holds no file for it"),
                new ExportLoss(4, "Personal/Recovery", "field related", "it links to another 1Password item, which lands in its own right"),
                new ExportLoss(4, "Personal/Recovery", "field shape", "keypaste does not read 1Password's futureShape field"),
                new ExportLoss(8, "Personal/Mystery", "category 999", "keypaste does not know this 1Password category, so no tag names it"),
                new ExportLoss(8, "Personal/Mystery", "state frozen", "keypaste does not know this 1Password item state"),
            ],
            plan.Losses);
    }

    [Fact]
    public void A_login_lands_with_its_addresses_its_first_totp_its_fields_named_as_keepassxc_names_them_its_history_and_its_times()
    {
        Commit();

        using var reopened = Vault.Open(_vaultPath, _master);
        var github = new EntryName("imported/Personal", "GitHub");
        var entry = reopened.Find(github)!;

        Assert.Equal(("op-user-github", "op-pw-github", "https://github.example/op-login", "op-notes-github"), (entry.Username, entry.Password, entry.Url, entry.Notes));
        Assert.Equal(
            [
                new EntryField("KP2A_URL_1", false, true), new EntryField("Recovery code", true, false), new EntryField("Security_PIN", true, false),
                new EntryField("Security_PIN (2)", true, false), new EntryField("Security_renewal", false, false), new EntryField("Security_since", false, false),
                new EntryField("otp", true, true), new EntryField("pin", true, false), new EntryField("remember", false, false),
            ],
            reopened.Fields(github));
        Assert.Equal(
            ("https://gist.example/op-gist", "op-recovery", "op-security-pin", "op-security-pin-2", "2027-06", "2016-01-01", "op-pin-github", "✓"),
            (reopened.ReadField(github, "KP2A_URL_1"), reopened.ReadField(github, "Recovery code"), reopened.ReadField(github, "Security_PIN"),
                reopened.ReadField(github, "Security_PIN (2)"), reopened.ReadField(github, "Security_renewal"), reopened.ReadField(github, "Security_since"),
                reopened.ReadField(github, "pin"), reopened.ReadField(github, "remember")));
        Assert.Equal($"otpauth://totp/GitHub:op-user-github?secret={_seed}", reopened.ReadField(github, "otp"));
        Assert.Equal(["Favorite", "Login", "dev"], reopened.Tags(github)!.Order(StringComparer.Ordinal));

        using (var totp = reopened.ReadTotp(github, out var refusal))
        {
            Assert.Null(refusal);
            Assert.Equal("287082", totp!.Code(DateTimeOffset.FromUnixTimeSeconds(59)).Value);
        }

        var history = reopened.ReadHistory(github)!.OrderBy(revision => revision.ModifiedUtc).ToList();
        Assert.Equal(["op-old-pw-github-1", "op-old-pw-github-2"], history.Select(revision => revision.Fields.Password));
        Assert.Equal([new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Utc), new DateTime(2025, 5, 6, 7, 8, 9, DateTimeKind.Utc)], history.Select(revision => revision.ModifiedUtc));
        Assert.All(history, revision => Assert.Equal(("op-user-github", "https://github.example/op-login", "op-notes-github"), (revision.Fields.Username, revision.Fields.Url, revision.Fields.Notes)));
        Assert.Equal(
            (new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero), new DateTimeOffset(2025, 6, 7, 8, 9, 10, TimeSpan.Zero)),
            (reopened.ReadTimes(github)!.Created, reopened.ReadTimes(github)!.Modified));
    }

    [Fact]
    public void Every_other_category_lands_its_section_fields_protected_where_1password_conceals_them()
    {
        Commit();

        using var reopened = Vault.Open(_vaultPath, _master);
        var visa = new EntryName("imported/Personal", "Visa");
        var me = new EntryName("imported/Personal", "Me");
        var key = new EntryName("imported/Personal", "Server key");
        var postgres = new EntryName("imported/Personal", "Postgres");
        var router = new EntryName("imported/Shared", "Router");

        Assert.Equal([Plain("cardholder name"), Plain("expiry date"), Masked("number"), Plain("type"), Masked("verification number")], reopened.Fields(visa));
        Assert.Equal(("op-card-number", "op-card-cvv", "2027-06"), (reopened.ReadField(visa, "number"), reopened.ReadField(visa, "verification number"), reopened.ReadField(visa, "expiry date")));
        Assert.Equal(["Credit Card"], reopened.Tags(visa));

        // KeePassXC's own testOPUX expects an identity's address as Address_address, joined over three lines.
        Assert.Equal(
            [Plain("Address_address"), Plain("Identification_birth date"), Plain("Identification_first name"), Plain("Internet Details_email"), Plain("Internet Details_email provider")],
            reopened.Fields(me));
        Assert.Equal("op-street\nBoston, MA 12345\nus", reopened.ReadField(me, "Address_address"));
        Assert.Equal(("2016-01-01", "op-me@example.com", "op-provider"), (reopened.ReadField(me, "Identification_birth date"), reopened.ReadField(me, "Internet Details_email"), reopened.ReadField(me, "Internet Details_email provider")));

        Assert.Equal([Plain("fingerprint"), Plain("key type"), Masked("private key"), Masked("private key (2)"), Plain("public key")], reopened.Fields(key));
        Assert.Equal(("op-ssh-described", "op-ssh-stored", "ssh-ed25519 op-ssh-public"), (reopened.ReadField(key, "private key"), reopened.ReadField(key, "private key (2)"), reopened.ReadField(key, "public key")));

        var database = reopened.Find(postgres)!;
        Assert.Equal(("op-db-user", "op-db-pw", "https://db.example/op-console"), (database.Username, database.Password, database.Url));
        Assert.Equal([Plain("notes (2)"), Plain("server")], reopened.Fields(postgres));

        Assert.Equal("op-pw-door", reopened.Find(new EntryName("imported/Personal", "Door code"))!.Password);
        Assert.Equal(["Password"], reopened.Tags(new EntryName("imported/Personal", "Door code")));
        Assert.Equal([Masked("base station password"), Plain("network name")], reopened.Fields(router));
        Assert.Equal(string.Empty, reopened.Find(router)!.Password);
        Assert.Equal(["Archived", "Secure Note"], reopened.Tags(new EntryName("imported/Personal", "Recovery"))!.Order(StringComparer.Ordinal));
        Assert.Equal([Masked("mood")], reopened.Fields(new EntryName("imported/Personal", "Recovery")));
        Assert.Null(reopened.Find(new EntryName("imported/Personal", "Old")));
    }

    [Fact]
    public void A_document_and_a_file_field_land_as_attachments_byte_for_byte()
    {
        Commit();

        var scan = new EntryName("imported/Personal", "Scan");
        var recovery = new EntryName("imported/Personal", "Recovery");

        using (var reopened = Vault.Open(_vaultPath, _master))
        {
            Assert.Equal([new EntryAttachment("scan.pdf", _scan.Length)], reopened.Attachments(scan));
            Assert.Equal([new EntryAttachment("photo.png", _photo.Length)], reopened.Attachments(recovery));
        }

        using var interop = KeePassInterop.Open(_vaultPath, Encoding.UTF8.GetBytes(_master));
        Assert.Equal(_scan, interop.FactsUnchecked(scan)!.Attachments["scan.pdf"]);
        Assert.Equal(_photo, interop.FactsUnchecked(recovery)!.Attachments["photo.png"]);
    }

    [Fact]
    public void A_file_over_5_mib_is_named_without_being_unpacked()
    {
        var archive = Archive(Document("Big", "doc-big", "big.bin"), ("files/doc-big__big.bin", [1, 2, 3]));
        Declare(archive, "files/doc-big__big.bin", EntryAttachment.MaximumSize + 1);

        using var source = Open(archive);

        // Unpacked, its three bytes would fall short of what the archive declares and refuse the file as damaged.
        Assert.Empty(source.Items[0].Attachments);
        Assert.Equal([new ExportOmission("attachment big.bin", "it is larger than 5 MiB, the most keypaste attaches")], source.Items[0].Omitted);
    }

    [Fact]
    public void Several_accounts_land_each_vault_under_its_account()
    {
        const string export = """
            {
              "accounts": [
                { "attrs": { "accountName": "Home" }, "vaults": [ { "attrs": { "name": "Personal" }, "items": [ { "overview": { "title": "Mail" } } ] } ] },
                { "attrs": { "accountName": "Work" }, "vaults": [ { "attrs": { "name": "Personal" }, "items": [ { "overview": { "title": "Mail" } } ] } ] }
              ]
            }
            """;

        using var source = Open(Archive(export));

        Assert.Equal(["Home/Personal", "Work/Personal"], source.Items.Select(item => string.Join("/", item.Folder)));
    }

    [Fact]
    public void An_archive_listing_more_than_10_000_files_is_refused_before_it_is_opened()
    {
        var files = Enumerable.Range(0, OnePassword1PuxReader.MaximumEntries).Select(number => ($"files/n{number}", Array.Empty<byte>())).ToArray();

        AssertRefused(Archive(_export, files), "its ZIP archive lists more than 10,000 files, the most keypaste reads of a 1PUX export");
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("files/../../evil.txt")]
    [InlineData("/evil.txt")]
    [InlineData("C:/evil.txt")]
    public void An_entry_whose_path_leaves_the_archive_is_refused(string path) =>
        AssertRefused(Archive(_export, (path, [1])), $"its ZIP archive holds {path}, whose path leaves the archive");

    [Fact]
    public void Two_entries_at_one_path_are_refused_rather_than_one_chosen() =>
        AssertRefused(
            Archive(_export, ("files/doc-scan__scan.pdf", _scan), ("files/doc-scan__scan.pdf", [1])),
            "its ZIP archive holds two files at files/doc-scan__scan.pdf");

    [Fact]
    public void An_export_data_declared_larger_than_128_mib_is_refused_before_it_is_unpacked()
    {
        var archive = Archive(_export);
        Declare(archive, "export.data", OnePassword1PuxReader.MaximumDataBytes + 1);

        AssertRefused(archive, "its export.data unpacks to more than 128 MiB, the most keypaste reads");
    }

    [Fact]
    public void Files_declared_to_unpack_past_512_mib_in_all_are_refused_before_any_is_unpacked()
    {
        const int count = 103;
        var items = string.Join(",", Enumerable.Range(0, count).Select(number =>
            $$"""{ "overview": { "title": "File {{number}}" }, "details": { "documentAttributes": { "fileName": "f.bin", "documentId": "doc{{number}}" } } }"""));
        var archive = Archive(
            $$"""{ "accounts": [ { "vaults": [ { "attrs": { "name": "Files" }, "items": [ {{items}} ] } ] } ] }""",
            [.. Enumerable.Range(0, count).Select(number => ($"files/doc{number}__f.bin", new byte[] { 1 }))]);

        foreach (var number in Enumerable.Range(0, count))
        {
            Declare(archive, $"files/doc{number}__f.bin", EntryAttachment.MaximumSize);
        }

        AssertRefused(archive, "it unpacks to more than 512 MiB, the most keypaste reads of a 1PUX export");
    }

    [Fact]
    public void A_file_that_unpacks_to_less_than_the_archive_declares_is_refused_as_damaged()
    {
        var archive = Archive(_export, _files);
        Declare(archive, "files/doc-scan__scan.pdf", (uint)_scan.Length + 10);

        AssertRefused(archive, "files/doc-scan__scan.pdf in its ZIP archive is damaged, or packed in a way keypaste does not unpack");
    }

    [Theory]
    [InlineData("""{ "accounts": [ { "vaults": [ { "items": [ { "overview": { "title": 7 } } ] } ] } ] }""", "accounts[0].vaults[0].items[0].overview.title is not text")]
    [InlineData("""{ "accounts": [ { "vaults": [ { "items": [ { "createdAt": "op-yesterday" } ] } ] } ] }""", "accounts[0].vaults[0].items[0].createdAt is not a whole number")]
    [InlineData("""{ "accounts": [ { "vaults": [ { "items": [ "op-item" ] } ] } ] }""", "accounts[0].vaults[0].items[0] is not a JSON object")]
    [InlineData("""{ "vaults": [] }""", "its export.data has no accounts list")]
    [InlineData("""{ "accounts": [ { "vaults": [ { "items": [ { "overview": { "title": "op-cut""", "its export.data is not JSON keypaste reads at line 1: a value is malformed or a name is given twice")]
    public void An_export_data_holding_something_1password_does_not_write_is_refused_naming_the_place_and_no_value(string data, string reason) =>
        AssertRefused(Archive(data), reason);

    [Fact]
    public void An_archive_without_export_data_or_cut_short_is_refused()
    {
        AssertRefused(Archive(null, ("files/doc-scan__scan.pdf", _scan)), "it holds no export.data, which every 1PUX export holds");
        AssertRefused(Archive(_export)[..64], "it is not a ZIP archive: it has no end of central directory record");
    }

    [Fact]
    public void Only_a_zip_named_1pux_or_starting_with_1puxs_own_entries_is_recognized()
    {
        var reader = new OnePassword1PuxReader();

        Assert.True(reader.Recognizes("ACCOUNT.1pux", Start(Zip(("data.json", [1])))));
        Assert.True(reader.Recognizes("export.zip", Start(Archive(_export))));
        Assert.True(reader.Recognizes("export.zip", Start(Zip(("export.data", Encoding.UTF8.GetBytes(_export))))));
        Assert.True(reader.Recognizes("export.zip", Start(Zip(("files/doc-scan__scan.pdf", _scan)))));
        Assert.False(reader.Recognizes("bitwarden_export.zip", Start(Zip(("data.json", [1])))));
        Assert.False(reader.Recognizes("export.1pux", Encoding.UTF8.GetBytes(_export)));
        Assert.False(reader.Recognizes("vault.1pux", [0x03, 0xD9, 0xA2, 0x9A, 0x67, 0xFB, 0x4B, 0xB5, 0x01, 0x00, 0x04, 0x00]));
    }

    [Fact]
    public void Nothing_said_about_an_export_holds_a_value()
    {
        using var source = Open(Archive(_export, _files));
        var plan = source.Plan(_vault, "imported");

        var said = new List<string?> { plan.Into, plan.Refusal };
        said.AddRange(plan.Rows.SelectMany(row => new[] { row.Item, row.Note, row.ToString() }));
        said.AddRange(plan.Losses.Select(loss => loss.ToString()));
        said.AddRange(source.Unread.Select(unread => unread.ToString()));
        said.AddRange(source.Items.SelectMany(item => item.Omitted).Select(omitted => omitted.ToString()));
        said.AddRange(source.Items.Select(item => item.ToString()));
        said.AddRange(source.Items.SelectMany(item => item.Fields).Select(field => field.ToString()));
        said.AddRange(source.Items.SelectMany(item => item.History).Select(version => version.ToString()));
        said.AddRange(source.Items.SelectMany(item => item.Attachments).Select(attachment => attachment.ToString()));

        string[] values = [_valueMark, _seed];
        Assert.All(values, value => Assert.DoesNotContain(said, text => text is not null && text.Contains(value, StringComparison.Ordinal)));
    }

    [Fact]
    public void Disposing_the_source_zeroes_every_attachment()
    {
        using var source = Open(Archive(_export, _files));
        var attachments = source.Items.SelectMany(item => item.Attachments).ToList();

        source.Dispose();

        Assert.Equal(2, attachments.Count);
        Assert.All(attachments, attachment => Assert.True(attachment.Content.Span.IndexOfAnyExcept((byte)0) < 0, $"{attachment} was not zeroed"));
    }

    private static EntryField Plain(string name) => new(name, false, false);

    private static EntryField Masked(string name) => new(name, true, false);

    private static string Document(string title, string documentId, string fileName) =>
        $$"""{ "accounts": [ { "vaults": [ { "attrs": { "name": "Files" }, "items": [ { "overview": { "title": "{{title}}" }, "details": { "documentAttributes": { "fileName": "{{fileName}}", "documentId": "{{documentId}}" } } } ] } ] } ] }""";

    /// <summary>A 1PUX archive as 1Password lays one out: <c>export.attributes</c>, then <c>export.data</c> unless it is null, then the files.</summary>
    private static byte[] Archive(string? data, params (string Path, byte[] Content)[] files)
    {
        List<(string Path, byte[] Content)> entries = [("export.attributes", Encoding.UTF8.GetBytes("""{ "version": 3, "description": "1Password Unencrypted Export", "createdAt": 1700000000 }"""))];

        if (data is not null)
        {
            entries.Add(("export.data", Encoding.UTF8.GetBytes(data)));
        }

        entries.AddRange(files);
        return Zip([.. entries]);
    }

    /// <summary>A ZIP archive holding these entries in this order, each deflated as ZipArchive packs one.</summary>
    private static byte[] Zip(params (string Path, byte[] Content)[] entries)
    {
        using var memory = new MemoryStream();

        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                using var stream = zip.CreateEntry(path).Open();
                stream.Write(content);
            }
        }

        return memory.ToArray();
    }

    /// <summary>Makes the archive's central directory declare <paramref name="size"/> unpacked bytes for one entry, leaving what it holds.</summary>
    private static void Declare(byte[] archive, string path, long size)
    {
        var name = Encoding.UTF8.GetBytes(path);

        for (var at = 0; at + 46 + name.Length <= archive.Length; at++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(at)) == 0x02014b50
                && BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(at + 28)) == name.Length
                && archive.AsSpan(at + 46, name.Length).SequenceEqual(name))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(at + 24), checked((uint)size));
                return;
            }
        }

        throw new InvalidOperationException($"no central directory header names {path}");
    }

    private static byte[] Start(byte[] archive) => archive[..Math.Min(archive.Length, ExportImport.StartBytes)];

    private void Commit()
    {
        using var source = Open(Archive(_export, _files));
        source.Commit(_vault, source.Plan(_vault, "imported"), vault => vault.Save());
    }

    private void AssertRefused(byte[] archive, string reason)
    {
        var refused = Assert.Throws<VaultException>(() => Open(archive));

        Assert.Equal($"export.1pux is not a 1Password 1PUX export keypaste reads: {reason}", refused.Message);
        Assert.DoesNotContain(_valueMark, refused.Message, StringComparison.Ordinal);
    }

    private ExportSource Open(byte[] archive)
    {
        var path = Path.Combine(_directory, "export.1pux");
        File.WriteAllBytes(path, archive);
        return ExportImport.Open(path, ExportImport.Readers) ?? throw new InvalidOperationException("no reader recognized export.1pux");
    }
}
