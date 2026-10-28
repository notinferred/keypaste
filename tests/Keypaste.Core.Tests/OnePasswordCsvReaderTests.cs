using System.Text;
using Keypaste.Core.Import;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// 9.1d2's 1Password CSV reader over exports written by hand: every row of 1Password 8's export and of earlier versions'
/// common fields lands as an item with its web address, TOTP, tags, favorite and archived marks, and every row or part
/// that does not land is named with why, never by value.
/// </summary>
public sealed class OnePasswordCsvReaderTests : IDisposable
{
    private const string _master = "onepassword-import-master";
    private const string _header = "Title,Url,Username,Password,OTPAuth,Favorite,Archived,Tags,Notes";
    private const string _markReason = "1Password writes true or false here and no other value";

    private static readonly string[] _values =
    [
        "op-pw-github", "op-notes-github", "JBSWY3DPEHPK3PXP", "op-pw,", "op-notes-bank", "op-pw-router", "op-pw-untitled", "op-totp-bad",
        "op7-pw-mail", "op7-notes",
    ];

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-onepassword-tests-").FullName;
    private readonly OnePasswordCsvReader _reader = new();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    // ---------------------------------------------------------------- reading

    [Fact]
    public void Each_row_of_a_1Password_8_export_lands_with_its_address_totp_tags_and_marks()
    {
        var contents = _reader.Read(Export());

        Assert.Equal(
            [
                "GitHub | octo | op-pw-github | https://github.com/login | op-notes-github | Work, Dev, Favorite | otpauth://totp/GitHub:octo?secret=JBSWY3DPEHPK3PXP&issuer=GitHub | ",
                "Bank | ada@example.com | op-pw,\"bank\" | https://bank.example | op-notes-bank line one\nline two | Finance, Archived | otpauth://totp/Bank:ada%40example.com?secret=JBSWY3DPEHPK3PXP | ",
                "Router |  | op-pw-router |  |  | Home, Network |  | favorite mark; archived mark",
                " |  | op-pw-untitled | https://untitled.example |  |  | otpauth://totp/:?secret=op-totp-bad | ",
            ],
            contents.Items.Select(Shape));
        Assert.Equal([new ExportOmission("line 5", "it holds nothing")], contents.Unread);
        Assert.Equal(
            [new ExportOmission("favorite mark", _markReason), new ExportOmission("archived mark", _markReason)],
            contents.Items[2].Omitted);
        Assert.All(contents.Items, item => Assert.Empty(item.Folder));
    }

    [Fact]
    public void Earlier_versions_common_fields_land_with_the_type_as_a_tag()
    {
        var contents = _reader.Read(Bytes(
            "notes,password,title,type,url,username",
            "op7-notes,op7-pw-mail,Mail,Login,https://mail.example,ada",
            "\"op7-notes wifi\",,Wi-Fi,Secure Note,,"));

        Assert.Equal(
            [
                "Mail | ada | op7-pw-mail | https://mail.example | op7-notes | Login |  | ",
                "Wi-Fi |  |  |  | op7-notes wifi | Secure Note |  | ",
            ],
            contents.Items.Select(Shape));
        Assert.Empty(contents.Unread);
    }

    [Fact]
    public void A_byte_order_mark_and_CRLF_line_breaks_read_as_LF_does()
    {
        var lf = _reader.Read(Export());
        var crlf = _reader.Read(Export("\r\n", byteOrderMark: true));

        Assert.Equal(lf.Items.Select(Shape), crlf.Items.Select(Shape));
        Assert.Equal(lf.Unread, crlf.Unread);
    }

    // ---------------------------------------------------------------- recognizing and refusing

    [Theory]
    [InlineData(_header + "\n", true)]
    [InlineData("﻿" + _header + "\r\n", true)]
    [InlineData("\"Title\",\"Url\",\"Username\",\"Password\",\"OTPAuth\",\"Favorite\",\"Archived\",\"Tags\",\"Notes\"\n", true)]
    [InlineData(_header + ",\n", true)]
    [InlineData("notes,password,title,type,url,username\n", true)]
    [InlineData("title,url,username,password,notes\n", true)]
    [InlineData("Title,Url,Username,Password\n", false)]
    [InlineData("\"UUID\",\"TITLE\",\"USERNAME\",\"PASSWORD\",\"URL\",\"URLS\",\"EMAIL\",\"SCOPE\",\"TAGS\",\"NOTES\"\n", false)]
    [InlineData("\"Created Date\",\"Modified Date\",\"Notes\",\"Password\",\"Tags\",\"Title\",\"Type\",\"URL\",\"Username\",\n", false)]
    [InlineData("url,username,password,totp,extra,name,grouping,fav\n", false)]
    [InlineData("folder,favorite,type,name,notes,fields,reprompt,login_uri,login_username,login_password,login_totp\n", false)]
    [InlineData("\"Group\",\"Title\",\"Username\",\"Password\",\"URL\",\"Notes\",\"TOTP\",\"Icon\",\"Last Modified\",\"Created\"\n", false)]
    [InlineData("not an export", false)]
    [InlineData("", false)]
    public void A_file_is_recognized_by_its_header(string start, bool recognized)
    {
        Assert.Equal(recognized, _reader.Recognizes("export.csv", Encoding.UTF8.GetBytes(start)));
    }

    [Fact]
    public void The_readers_keypaste_ships_read_a_1Password_export()
    {
        using var source = ExportImport.Open(Write("1PasswordExport.csv", Export()), ExportImport.Readers);

        Assert.NotNull(source);
        Assert.Equal("1Password CSV", source.Format);
        Assert.Equal(4, source.Items.Count);
    }

    [Theory]
    [InlineData("\"X\",\"https://x.example\",\"u\",\"op-pw-short\"", "line 2: it has 4 fields, and the first record has 9 fields")]
    [InlineData("\"X\",\"https://x.example\",\"u\",\"op-pw-open,false,false,,", "line 2: a quoted field is not closed")]
    [InlineData("X,https://x.example,u,op-pw\"broken,,false,false,,", "line 2: a field that does not start with a quote holds one")]
    public void A_malformed_export_is_refused_naming_its_line_and_never_a_value(string row, string expected)
    {
        var path = Write("1password.csv", Bytes(_header, row));

        var refused = Assert.Throws<VaultException>(() => ExportImport.Open(path, ExportImport.Readers));

        Assert.Equal($"1password.csv is not a 1Password CSV export keypaste reads: {expected}", refused.Message);
        Assert.DoesNotContain("op-pw", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_header_naming_a_column_twice_in_any_case_is_refused()
    {
        var refused = Assert.Throws<ExportFormatException>(() => _reader.Read(Bytes("Title,Url,Username,Password,Notes,TITLE", "a,b,c,d,e,f")));

        Assert.Equal("its header names the TITLE column twice", refused.Message);
    }

    // ---------------------------------------------------------------- into a vault

    [Fact]
    public void Imported_into_a_vault_every_part_lands_or_is_named_with_why()
    {
        var vaultPath = Path.Combine(_directory, "vault.kdbx");
        using var vault = Vault.Create(vaultPath, _master);
        using var source = ExportImport.Open(Write("1PasswordExport.csv", Export()), ExportImport.Readers)!;

        var plan = source.Plan(vault, "1Password");

        Assert.Null(plan.Refusal);
        Assert.Equal(
            [
                new ExportRow(0, "GitHub", new EntryName("1Password", "GitHub"), ExportCollision.None, CollisionDecision.KeepBoth, null),
                new ExportRow(1, "Bank", new EntryName("1Password", "Bank"), ExportCollision.None, CollisionDecision.KeepBoth, null),
                new ExportRow(2, "Router", new EntryName("1Password", "Router"), ExportCollision.None, CollisionDecision.KeepBoth, null),
                new ExportRow(3, string.Empty, new EntryName("1Password", "untitled"), ExportCollision.None, CollisionDecision.KeepBoth, "the item title cannot be empty"),
            ],
            plan.Rows);
        Assert.Equal(
            [
                new ExportLoss(-1, string.Empty, "line 5", "it holds nothing"),
                new ExportLoss(2, "Router", "favorite mark", _markReason),
                new ExportLoss(2, "Router", "archived mark", _markReason),
                new ExportLoss(3, string.Empty, "TOTP", "The otp field's secret is not base32."),
            ],
            plan.Losses);

        source.Commit(vault, plan, saving => saving.Save());

        using var reopened = Vault.Open(vaultPath, _master);
        var github = new EntryName("1Password", "GitHub");
        var bank = new EntryName("1Password", "Bank");

        var entry = reopened.Find(github)!;
        Assert.Equal(("octo", "op-pw-github", "https://github.com/login", "op-notes-github"), (entry.Username, entry.Password, entry.Url, entry.Notes));
        Assert.Equal([new EntryField("otp", true, true)], reopened.Fields(github));
        using (var totp = reopened.ReadTotp(github, out var refusal))
        {
            Assert.Null(refusal);
            Assert.Equal((6, TimeSpan.FromSeconds(30)), (totp!.Digits, totp.Period));
        }

        Assert.Equal(["Dev", "Favorite", "Work"], reopened.Tags(github)!.Order(StringComparer.Ordinal));
        Assert.Equal(("op-pw,\"bank\"", "op-notes-bank line one\nline two"), (reopened.Find(bank)!.Password, reopened.Find(bank)!.Notes));
        Assert.Equal("otpauth://totp/Bank:ada%40example.com?secret=JBSWY3DPEHPK3PXP", reopened.ReadField(bank, "otp"));
        Assert.Equal(["Archived", "Finance"], reopened.Tags(bank)!.Order(StringComparer.Ordinal));
        Assert.Equal(["Home", "Network"], reopened.Tags(new EntryName("1Password", "Router"))!.Order(StringComparer.Ordinal));
        Assert.Empty(reopened.Fields(new EntryName("1Password", "untitled"))!);
    }

    [Fact]
    public void No_item_row_loss_or_omission_shows_a_value()
    {
        var vaultPath = Path.Combine(_directory, "vault.kdbx");
        using var vault = Vault.Create(vaultPath, _master);
        using var source = ExportImport.Open(Write("1PasswordExport.csv", Export()), ExportImport.Readers)!;
        var plan = source.Plan(vault, "1Password");

        List<string?> said = [plan.Into, plan.Refusal];
        said.AddRange(plan.Rows.SelectMany(row => new[] { row.Item, row.Note, row.ToString() }));
        said.AddRange(plan.Losses.SelectMany(loss => new[] { loss.Part, loss.Reason, loss.ToString() }));
        said.AddRange(source.Unread.Select(unread => unread.ToString()));
        said.AddRange(source.Items.Select(item => item.ToString()));
        said.AddRange(source.Items.SelectMany(item => item.Omitted).Select(omitted => omitted.ToString()));

        Assert.All(_values, value => Assert.DoesNotContain(said, text => text is not null && text.Contains(value, StringComparison.Ordinal)));
    }

    /// <summary>An item's parts in one line, and what the reader omitted by name.</summary>
    private static string Shape(ExportItem item) => string.Join(
        " | ",
        item.Title,
        item.Username,
        item.Password,
        string.Join(' ', item.Urls),
        item.Notes,
        string.Join(", ", item.Tags),
        item.Totp,
        string.Join("; ", item.Omitted.Select(omitted => omitted.Part)));

    /// <summary>A 1Password 8 export as it writes one, its header bare and every field quoted, each row ending in <paramref name="lineBreak"/> and the notes' lines in LF: a favorite with a TOTP link, an archived item with a bare secret, a row holding nothing, marks 1Password does not write, and an untitled item whose TOTP keypaste cannot read.</summary>
    private static byte[] Export(string lineBreak = "\n", bool byteOrderMark = false)
    {
        string[] rows =
        [
            _header,
            Row("GitHub", "https://github.com/login", "octo", "op-pw-github", "otpauth://totp/GitHub:octo?secret=JBSWY3DPEHPK3PXP&issuer=GitHub", "true", "false", "Work;Dev", "op-notes-github"),
            Row("Bank", "https://bank.example", "ada@example.com", "op-pw,\"bank\"", "JBSWY3DPEHPK3PXP", "false", "true", "Finance", "op-notes-bank line one\nline two"),
            ",,,,,,,,",
            Row("Router", string.Empty, string.Empty, "op-pw-router", string.Empty, "yes", "maybe", "Home, Network", string.Empty),
            Row(string.Empty, "https://untitled.example", string.Empty, "op-pw-untitled", "op-totp-bad", "false", "false", string.Empty, string.Empty),
        ];

        var text = string.Join(lineBreak, rows) + lineBreak;
        return byteOrderMark ? [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(text)] : Encoding.UTF8.GetBytes(text);
    }

    private static string Row(params string[] fields) => string.Join(',', fields.Select(field => "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""));

    private static byte[] Bytes(params string[] rows) => Encoding.UTF8.GetBytes(string.Join("\n", rows) + "\n");

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
