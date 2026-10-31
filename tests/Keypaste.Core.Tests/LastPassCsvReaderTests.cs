using System.Text;
using Keypaste.Core.Import;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// 9.1c's LastPass CSV reader over exports written by hand: every row lands as an item with its folder, fields, TOTP and
/// favorite, a typed secure note is expanded as LastPass's <c>lpass</c> expands one, and every row or part that does not
/// land is named with why, never by value.
/// </summary>
public sealed class LastPassCsvReaderTests : IDisposable
{
    private const string _master = "lastpass-import-master";
    private const string _header = "url,username,password,totp,extra,name,grouping,fav";
    private const string _languageReason = "it is the language of LastPass's form for the note, which keypaste does not keep";

    private const string _creditCard = """
        NoteType:Credit Card
        Language:en-US
        Name on Card:Ada Lovelace
        Type:
        Number:lp-card-number
        Security Code:lp-card-csc
        Start Date:,
        Expiration Date:June,2030
        Notes:lp-card-notes
        continued

        """;

    private const string _server = """
        NoteType:Server
        Language:en-US
        Hostname:db.example
        Username:admin
        Password:lp-pw-server
        Notes:

        """;

    private const string _sshKey = """
        NoteType:SSH Key
        Language:en-US
        Bit Strength:2048
        Format:RSA
        Passphrase:lp-ssh-passphrase
        Private Key:-----BEGIN RSA PRIVATE KEY-----
        Proc-Type: 4,ENCRYPTED
        DEK-Info: AES-128-CBC,00FF

        lp-ssh-private-body
        -----END RSA PRIVATE KEY-----
        Public Key:ssh-ed25519 AAAA deploy
        Hostname:
        Date:
        Notes:

        """;

    private const string _address = """
        NoteType:Address
        Language:en-US
        Title:mr
        First Name:Alan
        Notes:

        """;

    private const string _privateKey = "-----BEGIN RSA PRIVATE KEY-----\nProc-Type: 4,ENCRYPTED\nDEK-Info: AES-128-CBC,00FF\n\nlp-ssh-private-body\n-----END RSA PRIVATE KEY-----";

    private static readonly string[] _values =
    [
        "lp-pw-github", "lp-notes-github", "JBSWY3DPEHPK3PXP", "lp-pw,", "lp-notes-bank", "lp-wifi-note", "lp-card-number",
        "lp-card-csc", "lp-card-notes", "lp-pw-server", "lp-ssh-passphrase", "lp-ssh-private-body", "lp-pw-untitled", "lp-totp-bad",
    ];

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-lastpass-tests-").FullName;
    private readonly LastPassCsvReader _reader = new();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    // ---------------------------------------------------------------- reading

    [Fact]
    public void Each_row_lands_as_an_item_with_its_folder_fields_totp_and_favorite()
    {
        var contents = _reader.Read(Export());

        Assert.Equal(
            [
                @"GitHub | Work/Dev | octo | lp-pw-github | https://github.com/login | lp-notes-github |  | Favorite | otpauth://totp/GitHub:octo?secret=JBSWY3DPEHPK3PXP | ",
                "Bank | Shared-Family/Money | ada@example.com | lp-pw,\"bank\" | https://bank.example | lp-notes-bank line one\nline two |  |  |  | ",
                "Router |  |  |  |  | lp-wifi-note: a colon\nsecond line |  |  |  | ",
                "Visa | Finance |  |  |  | lp-card-notes\ncontinued | Name on Card=Ada Lovelace (protected); Number=lp-card-number (protected); Security Code=lp-card-csc (protected); Start Date=, (protected); Expiration Date=June,2030 (protected) | Credit Card |  | field Language",
                "Database server | Work | admin | lp-pw-server |  |  | Hostname=db.example (protected) | Server |  | field Language",
                $"Deploy key | Work/Dev |  |  |  |  | Bit Strength=2048 (protected); Format=RSA (protected); Passphrase=lp-ssh-passphrase (protected); Private Key={_privateKey} (protected); Public Key=ssh-ed25519 AAAA deploy (protected) | SSH Key |  | field Language",
                "Home |  |  |  |  |  | Title=mr (protected); First Name=Alan (protected) | Address |  | field Language",
                " |  |  | lp-pw-untitled | https://untitled.example |  |  |  | otpauth://totp/:?secret=lp-totp-bad | ",
            ],
            contents.Items.Select(Shape));
        Assert.Equal(
            [
                new ExportOmission("line 2", "it holds nothing"),
                new ExportOmission(@"folder Archive\Old", "LastPass lists an empty folder this way, and an import makes a group only for an item in it"),
            ],
            contents.Unread);
    }

    [Fact]
    public void An_lpass_export_without_totp_names_a_column_keypaste_does_not_read_and_a_favorite_mark_it_cannot()
    {
        var contents = _reader.Read(Bytes(
            "url,username,password,extra,name,grouping,fav,id",
            "https://a.example,ada,lp-pw-a,lp-notes-a,A,,yes,4242",
            "https://b.example,bob,lp-pw-b,,B,,0,"));

        Assert.Equal(
            [
                "A |  | ada | lp-pw-a | https://a.example | lp-notes-a |  |  |  | column id; favorite mark",
                "B |  | bob | lp-pw-b | https://b.example |  |  |  |  | ",
            ],
            contents.Items.Select(Shape));
        Assert.Equal(
            [new ExportOmission("column id", "keypaste does not read this column of a LastPass export"), new ExportOmission("favorite mark", "LastPass marks a favorite with 1 and no other value")],
            contents.Items[0].Omitted);
    }

    // ---------------------------------------------------------------- recognizing and refusing

    [Theory]
    [InlineData(_header + "\r\n", true)]
    [InlineData("﻿" + _header + "\n", true)]
    [InlineData("url,username,password,extra,name,grouping,fav\r\n", true)]
    [InlineData("url,username,password,extra,name,fav\r\n", false)]
    [InlineData("folder,favorite,type,name,notes,fields,reprompt,login_uri,login_username,login_password,login_totp\n", false)]
    [InlineData("\"Group\",\"Title\",\"Username\",\"Password\",\"URL\",\"Notes\",\"TOTP\",\"Icon\",\"Last Modified\",\"Created\"\n", false)]
    [InlineData("Title,Url,Username,Password,OTPAuth,Favorite,Archived,Tags,Notes\n", false)]
    [InlineData("not an export", false)]
    [InlineData("", false)]
    public void A_file_is_recognized_by_its_header(string start, bool recognized)
    {
        Assert.Equal(recognized, _reader.Recognizes("export.csv", Encoding.UTF8.GetBytes(start)));
    }

    [Fact]
    public void The_readers_keypaste_ships_read_a_LastPass_export()
    {
        using var source = ExportImport.Open(Write("lastpass_export.csv", Export()), ExportImport.Readers);

        Assert.NotNull(source);
        Assert.Equal("LastPass CSV", source.Format);
        Assert.Equal(8, source.Items.Count);
    }

    // ---------------------------------------------------------------- into a vault

    [Fact]
    public void Imported_into_a_vault_every_part_lands_or_is_named_with_why()
    {
        var vaultPath = Path.Combine(_directory, "vault.kdbx");
        using var vault = Vault.Create(vaultPath, _master);
        using var source = ExportImport.Open(Write("lastpass_export.csv", Export()), ExportImport.Readers)!;

        var plan = source.Plan(vault, "LastPass");

        Assert.Null(plan.Refusal);
        Assert.Equal(
            [
                new ExportRow(0, "Work/Dev/GitHub", new EntryName("LastPass/Work/Dev", "GitHub"), ExportCollision.None, CollisionDecision.KeepBoth, null),
                new ExportRow(1, "Shared-Family/Money/Bank", new EntryName("LastPass/Shared-Family/Money", "Bank"), ExportCollision.None, CollisionDecision.KeepBoth, null),
                new ExportRow(2, "Router", new EntryName("LastPass", "Router"), ExportCollision.None, CollisionDecision.KeepBoth, null),
                new ExportRow(3, "Finance/Visa", new EntryName("LastPass/Finance", "Visa"), ExportCollision.None, CollisionDecision.KeepBoth, null),
                new ExportRow(4, "Work/Database server", new EntryName("LastPass/Work", "Database server"), ExportCollision.None, CollisionDecision.KeepBoth, null),
                new ExportRow(5, "Work/Dev/Deploy key", new EntryName("LastPass/Work/Dev", "Deploy key"), ExportCollision.None, CollisionDecision.KeepBoth, null),
                new ExportRow(6, "Home", new EntryName("LastPass", "Home"), ExportCollision.None, CollisionDecision.KeepBoth, null),
                new ExportRow(7, string.Empty, new EntryName("LastPass", "untitled"), ExportCollision.None, CollisionDecision.KeepBoth, "the item title cannot be empty"),
            ],
            plan.Rows);
        Assert.Equal(
            [
                new ExportLoss(-1, string.Empty, "line 2", "it holds nothing"),
                new ExportLoss(-1, string.Empty, @"folder Archive\Old", "LastPass lists an empty folder this way, and an import makes a group only for an item in it"),
                new ExportLoss(3, "Finance/Visa", "field Language", _languageReason),
                new ExportLoss(4, "Work/Database server", "field Language", _languageReason),
                new ExportLoss(5, "Work/Dev/Deploy key", "field Language", _languageReason),
                new ExportLoss(6, "Home", "field Title", "'Title' is one of the item's standard fields, not a custom field"),
                new ExportLoss(6, "Home", "field Language", _languageReason),
                new ExportLoss(7, string.Empty, "TOTP", "The otp field's secret is not base32."),
            ],
            plan.Losses);

        source.Commit(vault, plan, saving => saving.Save());

        using var reopened = Vault.Open(vaultPath, _master);
        var github = new EntryName("LastPass/Work/Dev", "GitHub");
        var visa = new EntryName("LastPass/Finance", "Visa");
        var server = new EntryName("LastPass/Work", "Database server");
        var deploy = new EntryName("LastPass/Work/Dev", "Deploy key");
        var untitled = new EntryName("LastPass", "untitled");

        var entry = reopened.Find(github)!;
        Assert.Equal(("octo", "lp-pw-github", "https://github.com/login", "lp-notes-github"), (entry.Username, entry.Password, entry.Url, entry.Notes));
        Assert.Equal([new EntryField("otp", true, true)], reopened.Fields(github));
        using (var totp = reopened.ReadTotp(github, out var refusal))
        {
            Assert.Null(refusal);
            Assert.Equal((6, TimeSpan.FromSeconds(30)), (totp!.Digits, totp.Period));
        }

        Assert.Equal(["Favorite"], reopened.Tags(github));
        Assert.Equal("lp-pw,\"bank\"", reopened.Find(new EntryName("LastPass/Shared-Family/Money", "Bank"))!.Password);
        Assert.Equal(
            ["Expiration Date", "Name on Card", "Number", "Security Code", "Start Date"],
            reopened.Fields(visa)!.Where(field => field.IsProtected && !field.IsReadOnly).Select(field => field.Name));
        Assert.Equal("lp-card-number", reopened.ReadField(visa, "Number"));
        Assert.Equal("lp-card-notes\ncontinued", reopened.Find(visa)!.Notes);
        Assert.Equal(["Credit Card"], reopened.Tags(visa));
        Assert.Equal(("admin", "lp-pw-server"), (reopened.Find(server)!.Username, reopened.Find(server)!.Password));
        Assert.Equal(_privateKey, reopened.ReadField(deploy, "Private Key"));
        Assert.Empty(reopened.Fields(untitled)!);
        Assert.DoesNotContain(reopened.ReadGroupPaths(includeReserved: true), group => group.StartsWith("LastPass/Archive", StringComparison.Ordinal));
    }

    [Fact]
    public void No_item_row_loss_or_omission_shows_a_value()
    {
        var vaultPath = Path.Combine(_directory, "vault.kdbx");
        using var vault = Vault.Create(vaultPath, _master);
        using var source = ExportImport.Open(Write("lastpass_export.csv", Export()), ExportImport.Readers)!;
        var plan = source.Plan(vault, "LastPass");

        List<string?> said = [plan.Into, plan.Refusal];
        said.AddRange(plan.Rows.SelectMany(row => new[] { row.Item, row.Note, row.ToString() }));
        said.AddRange(plan.Losses.SelectMany(loss => new[] { loss.Part, loss.Reason, loss.ToString() }));
        said.AddRange(source.Unread.Select(unread => unread.ToString()));
        said.AddRange(source.Items.Select(item => item.ToString()));
        said.AddRange(source.Items.SelectMany(item => item.Fields).Select(field => field.ToString()));
        said.AddRange(source.Items.SelectMany(item => item.Omitted).Select(omitted => omitted.ToString()));

        Assert.All(_values, value => Assert.DoesNotContain(said, text => text is not null && text.Contains(value, StringComparison.Ordinal)));
    }

    /// <summary>An item's parts in one line, each field with its value and whether it is protected, and what the reader omitted by name.</summary>
    private static string Shape(ExportItem item) => string.Join(
        " | ",
        item.Title,
        string.Join('/', item.Folder),
        item.Username,
        item.Password,
        string.Join(' ', item.Urls),
        item.Notes,
        string.Join("; ", item.Fields.Select(field => $"{field.Name}={field.Value}{(field.Protect ? " (protected)" : string.Empty)}")),
        string.Join(", ", item.Tags),
        item.Totp,
        string.Join("; ", item.Omitted.Select(omitted => omitted.Part)));

    /// <summary>A LastPass export as its vault writes one, each row ending in <paramref name="lineBreak"/> and each note's lines in LF: an empty row, sites, secure notes plain and typed, an untitled site whose TOTP keypaste cannot read and an empty folder.</summary>
    private static byte[] Export(string lineBreak = "\r\n", bool byteOrderMark = false)
    {
        string[] rows =
        [
            _header,
            ",,,,,,,",
            @"https://github.com/login,octo,lp-pw-github,JBSWY3DPEHPK3PXP,lp-notes-github,GitHub,Work\Dev,1",
            $@"https://bank.example,ada@example.com,{Quoted("lp-pw,\"bank\"")},,{Quoted("lp-notes-bank line one\nline two")},Bank,Shared-Family\Money,0",
            $"http://sn,,,,{Quoted("lp-wifi-note: a colon\nsecond line")},Router,(none),0",
            $"http://sn,,,,{Quoted(_creditCard)},Visa,Finance,0",
            $"http://sn,,,,{Quoted(_server)},Database server,Work,0",
            $@"http://sn,,,,{Quoted(_sshKey)},Deploy key,Work\Dev,0",
            $"http://sn,,,,{Quoted(_address)},Home,(none),0",
            "https://untitled.example,,lp-pw-untitled,lp-totp-bad,,,,0",
            @"http://group,,,,,,Archive\Old,0",
        ];

        var text = string.Join(lineBreak, rows) + lineBreak;
        return byteOrderMark ? [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(text)] : Encoding.UTF8.GetBytes(text);
    }

    private static byte[] Bytes(params string[] rows) => Encoding.UTF8.GetBytes(string.Join("\r\n", rows) + "\r\n");

    private static string Quoted(string text) => "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
