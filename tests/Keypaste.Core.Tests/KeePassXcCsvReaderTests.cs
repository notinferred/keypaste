using System.Globalization;
using System.Text;
using Keypaste.Core.Import;
using Keypaste.Core.Totp;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// 9.1e's KeePassXC CSV reader over exports written by hand as KeePassXC's <c>CsvExporter</c> writes them: every row outside the recycle bin lands
/// as an item in its group below the root, with its standard fields, TOTP and times, and every row or part that does not
/// land is named with why, never by value.
/// </summary>
public sealed class KeePassXcCsvReaderTests : IDisposable
{
    private const string _master = "keepassxc-import-master";
    private const string _header = "\"Group\",\"Title\",\"Username\",\"Password\",\"URL\",\"Notes\",\"TOTP\",\"Icon\",\"Last Modified\",\"Created\"";
    private const string _iconReason = "an imported item gets KeePass's first icon, the key";
    private const string _timeReason = "it is not a time as KeePassXC writes one, such as 2024-01-31T09:30:00Z, so the import's time is kept";
    private const string _recycledReason = "it was in KeePassXC's recycle bin, which an import leaves behind, as it leaves a KeePass file's";

    private static readonly string[] _values =
    [
        "kx-pw-top", "kx-pw-clock", "kx-pw,", "kx-notes-bank", "JBSWY3DPEHPK3PXP", "kx-pw-github", "kx-notes-github", "kx-pw-old", "kx-pw-older",
        "kx-pw-kept", "kx-tag-value",
    ];

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-keepassxc-tests-").FullName;
    private readonly KeePassXcCsvReader _reader = new();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    // ---------------------------------------------------------------- reading

    [Fact]
    public void Each_row_outside_the_recycle_bin_lands_in_its_group_below_the_root_with_its_fields_totp_and_times()
    {
        var contents = _reader.Read(Export());

        Assert.Equal(
            [
                "Top |  | top-user | kx-pw-top | https://top.example |  |  | 2024-05-06T07:08:09 | 2023-01-02T03:04:05 | ",
                "Clock |  |  | kx-pw-clock |  |  |  |  |  | Last Modified time; Created time",
                "Bank | Banking | ada@example.com | kx-pw,\"bank\" | https://bank.example | kx-notes-bank line one\nline two | otpauth://totp/Bank:ada%40example.com?secret=JBSWY3DPEHPK3PXP&period=30&digits=8&issuer=Bank&algorithm=SHA256 | 2025-01-01T00:00:00 | 2025-01-01T00:00:00 | ",
                "GitHub | Work/Dev | octo | kx-pw-github | https://github.com/login | kx-notes-github | otpauth://totp/GitHub:octo?secret=JBSWY3DPEHPK3PXP&period=30&digits=6&issuer=GitHub | 2025-06-07T08:09:10 | 2024-01-02T03:04:05 | icon",
                "Kept | Work/Recycle Bin |  | kx-pw-kept |  |  |  | 2024-02-03T04:05:06 | 2024-02-03T04:05:06 | ",
            ],
            contents.Items.Select(Shape));
        Assert.Equal(
            [
                new ExportOmission("line 4", "it holds nothing"),
                new ExportOmission("item Recycle Bin/Old", _recycledReason),
                new ExportOmission("item Recycle Bin/Gone/Older", _recycledReason),
            ],
            contents.Unread);
        Assert.Equal(
            [new ExportOmission("Last Modified time", _timeReason), new ExportOmission("Created time", _timeReason)],
            contents.Items[1].Omitted);
    }

    [Fact]
    public void An_older_header_reads_without_its_later_columns_and_a_column_keypaste_does_not_read_is_named()
    {
        var contents = _reader.Read(Bytes(
            "\"Group\",\"Title\",\"Username\",\"Password\",\"URL\",\"Notes\",\"Tags\"",
            "\"Root/A\",\"A\",\"\",\"kx-pw-a\",\"\",\"\",\"kx-tag-value\"",
            "\"Root\",\"B\",\"\",\"kx-pw-b\",\"\",\"\",\"\""));

        Assert.Equal(
            [
                "A | A |  | kx-pw-a |  |  |  |  |  | column Tags",
                "B |  |  | kx-pw-b |  |  |  |  |  | ",
            ],
            contents.Items.Select(Shape));
        Assert.Equal([new ExportOmission("column Tags", "keypaste does not read this column of a KeePassXC export")], contents.Items[0].Omitted);
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
    [InlineData("\"Group\",\"Title\",\"Username\",\"Password\",\"URL\",\"Notes\"\n", true)]
    [InlineData("Group,Title,Username,Password,URL,Notes,TOTP,Icon,Last Modified,Created\r\n", true)]
    [InlineData("group,title,username,password,url,notes\n", false)]
    [InlineData("url,username,password,totp,extra,name,grouping,fav\n", false)]
    [InlineData("Title,Url,Username,Password,OTPAuth,Favorite,Archived,Tags,Notes\n", false)]
    [InlineData("folder,favorite,type,name,notes,fields,reprompt,login_uri,login_username,login_password,login_totp\n", false)]
    [InlineData("not an export", false)]
    [InlineData("", false)]
    public void A_file_is_recognized_by_its_header(string start, bool recognized)
    {
        Assert.Equal(recognized, _reader.Recognizes("export.csv", Encoding.UTF8.GetBytes(start)));
    }

    [Fact]
    public void The_readers_keypaste_ships_read_a_KeePassXC_export()
    {
        using var source = ExportImport.Open(Write("Passwords.csv", Export()), ExportImport.Readers);

        Assert.NotNull(source);
        Assert.Equal("KeePassXC CSV", source.Format);
        Assert.Equal(5, source.Items.Count);
    }

    [Theory]
    [InlineData("\"Root\",\"X\",\"kx-pw-short\"", "line 2: it has 3 fields, and the first record has 10 fields")]
    [InlineData("\"Root\",\"X\",\"\",\"kx-pw-open,,,0,,", "line 2: a quoted field is not closed")]
    [InlineData("\"Root\",\"X\",\"\",\"kx-pw\"broken\",\"\",\"\",\"\",\"0\",\"\",\"\"", "line 2: text follows a quoted field's closing quote")]
    public void A_malformed_export_is_refused_naming_its_line_and_never_a_value(string row, string expected)
    {
        var path = Write("keepassxc.csv", Bytes(_header, row));

        var refused = Assert.Throws<VaultException>(() => ExportImport.Open(path, ExportImport.Readers));

        Assert.Equal($"keepassxc.csv is not a KeePassXC CSV export keypaste reads: {expected}", refused.Message);
        Assert.DoesNotContain("kx-pw", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_header_naming_a_column_twice_is_refused()
    {
        var refused = Assert.Throws<ExportFormatException>(() => _reader.Read(Bytes("\"Group\",\"Title\",\"Username\",\"Password\",\"URL\",\"Notes\",\"Title\"", "\"a\",\"b\",\"c\",\"d\",\"e\",\"f\",\"g\"")));

        Assert.Equal("its header names the Title column twice", refused.Message);
    }

    // ---------------------------------------------------------------- into a vault

    [Fact]
    public void Imported_into_a_vault_every_part_lands_or_is_named_with_why()
    {
        var vaultPath = Path.Combine(_directory, "vault.kdbx");
        using var vault = Vault.Create(vaultPath, _master);
        using var source = ExportImport.Open(Write("Passwords.csv", Export()), ExportImport.Readers)!;

        var plan = source.Plan(vault, "KeePassXC");

        Assert.Null(plan.Refusal);
        Assert.Equal(
            [
                new ExportRow(0, "Top", new EntryName("KeePassXC", "Top"), ExportCollision.None, CollisionDecision.KeepBoth, null),
                new ExportRow(1, "Clock", new EntryName("KeePassXC", "Clock"), ExportCollision.None, CollisionDecision.KeepBoth, null),
                new ExportRow(2, "Banking/Bank", new EntryName("KeePassXC/Banking", "Bank"), ExportCollision.None, CollisionDecision.KeepBoth, null),
                new ExportRow(3, "Work/Dev/GitHub", new EntryName("KeePassXC/Work/Dev", "GitHub"), ExportCollision.None, CollisionDecision.KeepBoth, null),
                new ExportRow(4, "Work/Recycle Bin/Kept", new EntryName("KeePassXC/Work/Recycle Bin (imported)", "Kept"), ExportCollision.None, CollisionDecision.KeepBoth, "Recycle Bin is the recycle bin's name"),
            ],
            plan.Rows);
        Assert.Equal(
            [
                new ExportLoss(-1, string.Empty, "line 4", "it holds nothing"),
                new ExportLoss(-1, string.Empty, "item Recycle Bin/Old", _recycledReason),
                new ExportLoss(-1, string.Empty, "item Recycle Bin/Gone/Older", _recycledReason),
                new ExportLoss(1, "Clock", "Last Modified time", _timeReason),
                new ExportLoss(1, "Clock", "Created time", _timeReason),
                new ExportLoss(3, "Work/Dev/GitHub", "icon", _iconReason),
            ],
            plan.Losses);

        source.Commit(vault, plan, saving => saving.Save());

        using var reopened = Vault.Open(vaultPath, _master);
        var github = new EntryName("KeePassXC/Work/Dev", "GitHub");
        var bank = new EntryName("KeePassXC/Banking", "Bank");

        var entry = reopened.Find(github)!;
        Assert.Equal(("octo", "kx-pw-github", "https://github.com/login", "kx-notes-github"), (entry.Username, entry.Password, entry.Url, entry.Notes));
        Assert.Equal([new EntryField("otp", true, true)], reopened.Fields(github));
        Assert.Equal(
            (new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero), new DateTimeOffset(2025, 6, 7, 8, 9, 10, TimeSpan.Zero)),
            (reopened.ReadTimes(github)!.Created, reopened.ReadTimes(github)!.Modified));
        using (var totp = reopened.ReadTotp(github, out var refusal))
        {
            Assert.Null(refusal);
            Assert.Equal((6, TimeSpan.FromSeconds(30)), (totp!.Digits, totp.Period));
        }

        Assert.Equal(("kx-pw,\"bank\"", "kx-notes-bank line one\nline two"), (reopened.Find(bank)!.Password, reopened.Find(bank)!.Notes));
        using (var totp = reopened.ReadTotp(bank, out var refusal))
        {
            Assert.Null(refusal);
            Assert.Equal((8, TotpAlgorithm.Sha256), (totp!.Digits, totp.Algorithm));
        }

        Assert.Equal(new DateTimeOffset(2023, 1, 2, 3, 4, 5, TimeSpan.Zero), reopened.ReadTimes(new EntryName("KeePassXC", "Top"))!.Created);
        Assert.Equal("kx-pw-kept", reopened.Find(new EntryName("KeePassXC/Work/Recycle Bin (imported)", "Kept"))!.Password);
        Assert.DoesNotContain(reopened.ReadGroupPaths(includeReserved: true), group => group.StartsWith("KeePassXC/Recycle Bin", StringComparison.Ordinal));
        Assert.Empty(reopened.Tags(github)!);
    }

    [Fact]
    public void No_item_row_loss_or_omission_shows_a_value()
    {
        var vaultPath = Path.Combine(_directory, "vault.kdbx");
        using var vault = Vault.Create(vaultPath, _master);
        using var source = ExportImport.Open(Write("Passwords.csv", Export()), ExportImport.Readers)!;
        var plan = source.Plan(vault, "KeePassXC");

        List<string?> said = [plan.Into, plan.Refusal];
        said.AddRange(plan.Rows.SelectMany(row => new[] { row.Item, row.Note, row.ToString() }));
        said.AddRange(plan.Losses.SelectMany(loss => new[] { loss.Part, loss.Reason, loss.ToString() }));
        said.AddRange(source.Unread.Select(unread => unread.ToString()));
        said.AddRange(source.Items.Select(item => item.ToString()));
        said.AddRange(source.Items.SelectMany(item => item.Omitted).Select(omitted => omitted.ToString()));

        Assert.All(_values, value => Assert.DoesNotContain(said, text => text is not null && text.Contains(value, StringComparison.Ordinal)));
    }

    /// <summary>An item's parts in one line, its times in UTC, and what the reader omitted by name.</summary>
    private static string Shape(ExportItem item) => string.Join(
        " | ",
        item.Title,
        string.Join('/', item.Folder),
        item.Username,
        item.Password,
        string.Join(' ', item.Urls),
        item.Notes,
        item.Totp,
        item.Modified?.UtcDateTime.ToString("s", CultureInfo.InvariantCulture),
        item.Created?.UtcDateTime.ToString("s", CultureInfo.InvariantCulture),
        string.Join("; ", item.Omitted.Select(omitted => omitted.Part)));

    /// <summary>A KeePassXC export as its <c>CsvExporter</c> writes one, every field quoted, each row ending in <paramref name="lineBreak"/> and the notes' lines in LF, the root group's items first: one created at an offset, one whose times are not KeePassXC's, a row holding nothing, then items in groups, one with an icon, two in the recycle bin and one in a group of that name elsewhere.</summary>
    private static byte[] Export(string lineBreak = "\n", bool byteOrderMark = false)
    {
        string[] rows =
        [
            _header,
            Row("Root", "Top", "top-user", "kx-pw-top", "https://top.example", string.Empty, string.Empty, "0", "2024-05-06T07:08:09Z", "2023-01-02T05:04:05+02:00"),
            Row("Root", "Clock", string.Empty, "kx-pw-clock", string.Empty, string.Empty, string.Empty, "0", "2024-05-06 07:08:09", "2024-05-06T07:08:09"),
            Row(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty),
            Row("Root/Banking", "Bank", "ada@example.com", "kx-pw,\"bank\"", "https://bank.example", "kx-notes-bank line one\nline two", "otpauth://totp/Bank:ada%40example.com?secret=JBSWY3DPEHPK3PXP&period=30&digits=8&issuer=Bank&algorithm=SHA256", "0", "2025-01-01T00:00:00Z", "2025-01-01T00:00:00Z"),
            Row("Root/Work/Dev", "GitHub", "octo", "kx-pw-github", "https://github.com/login", "kx-notes-github", "otpauth://totp/GitHub:octo?secret=JBSWY3DPEHPK3PXP&period=30&digits=6&issuer=GitHub", "1", "2025-06-07T08:09:10Z", "2024-01-02T03:04:05Z"),
            Row("Root/Recycle Bin", "Old", string.Empty, "kx-pw-old", string.Empty, string.Empty, string.Empty, "0", "2024-02-03T04:05:06Z", "2024-02-03T04:05:06Z"),
            Row("Root/Recycle Bin/Gone", "Older", string.Empty, "kx-pw-older", string.Empty, string.Empty, string.Empty, "0", "2024-02-03T04:05:06Z", "2024-02-03T04:05:06Z"),
            Row("Root/Work/Recycle Bin", "Kept", string.Empty, "kx-pw-kept", string.Empty, string.Empty, string.Empty, "0", "2024-02-03T04:05:06Z", "2024-02-03T04:05:06Z"),
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
