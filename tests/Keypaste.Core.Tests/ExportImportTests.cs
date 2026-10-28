using System.Text;
using Keypaste.Core.Import;
using Keypaste.Core.Internal;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// 9.1a's import contract over a fake reader: every item and part of an export lands or is named with why, a taken
/// name is kept beside the other or skipped as decided, and a commit adds everything, or nothing when adding or
/// saving fails or the vault changed since the preview.
/// </summary>
public sealed class ExportImportTests : IDisposable
{
    private const string _master = "export-import-master";

    private static readonly EntryName _github = new("imported/Work", "GitHub");

    private static readonly string[] _values =
    [
        "export-pw-github", "export-pw-bank", "export-user-bank", "https://bank.example/one", "https://bank.example/two",
        "export-notes-bank", "export-pin-bank", "export-standard-field", "export-otp-field", "export-pin-twice",
        "otpauth://totp/bank?secret=JBSWY3DPEHPK3PXP", "export-old-pw-bank", "export-pw-slash", "export-pw-bank-two",
        "export-pw-stale", "export-old-pw-stale", "KRSXG5CTMVRXEZLU",
    ];

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-export-tests-").FullName;
    private readonly string _exports;
    private readonly string _vaultPath;
    private readonly Vault _vault;
    private readonly byte[] _statement = Encoding.UTF8.GetBytes("statement bytes");

    public ExportImportTests()
    {
        _exports = Directory.CreateDirectory(Path.Combine(_directory, "exports")).FullName;
        _vaultPath = Path.Combine(_directory, "vault.kdbx");
        _vault = Vault.Create(_vaultPath, _master);
        _vault.AddEntry(new VaultEntry { GroupPath = _github.GroupPath, Title = _github.Title, Password = "vault-pw-github" });
        _vault.Save();
    }

    public void Dispose()
    {
        _vault.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    // ---------------------------------------------------------------- the preview

    [Fact]
    public void The_preview_says_where_every_item_lands_and_names_everything_that_does_not()
    {
        var before = File.ReadAllBytes(_vaultPath);
        using var source = Open(Items(), [new ExportOmission("card Visa", "this format's cards are not read")]);

        var plan = source.Plan(_vault, "imported");

        Assert.Null(plan.Refusal);
        Assert.Equal("imported", plan.Into);
        Assert.Equal(
            [
                new ExportRow(0, "Work/GitHub", new EntryName("imported/Work", "GitHub (2)"), ExportCollision.VaultItem, CollisionDecision.KeepBoth, "'GitHub' is already an item there"),
                new ExportRow(1, "Bank", new EntryName("imported", "Bank"), ExportCollision.None, CollisionDecision.KeepBoth, null),
                new ExportRow(2, "Personal/a/b", new EntryName("imported/Personal", "a-b"), ExportCollision.None, CollisionDecision.KeepBoth, "the item title cannot contain '/'"),
                new ExportRow(3, "Bank", new EntryName("imported", "Bank (2)"), ExportCollision.ExportItem, CollisionDecision.KeepBoth, "'Bank' is another item of this export"),
                new ExportRow(4, "Stale", new EntryName("imported", "Stale"), ExportCollision.None, CollisionDecision.KeepBoth, null),
            ],
            plan.Rows);
        Assert.Equal(
            [
                new ExportLoss(-1, string.Empty, "card Visa", "this format's cards are not read"),
                new ExportLoss(1, "Bank", "field Password", "'Password' is one of the item's standard fields, not a custom field"),
                new ExportLoss(1, "Bank", "field otp", "'otp' is KeePassXC's own attribute, which keypaste leaves to KeePassXC"),
                new ExportLoss(1, "Bank", "field PIN", "the item has another field of that name"),
                new ExportLoss(1, "Bank", "tag a,b", "a tag cannot contain ',' or ';', which separate tags"),
                new ExportLoss(1, "Bank", "attachment big.bin", "it is larger than 5 MiB, the most keypaste attaches"),
                new ExportLoss(1, "Bank", "attachment a/b", "the attachment name cannot contain '/'"),
                new ExportLoss(1, "Bank", "passkey", "keypaste holds no passkeys"),
                new ExportLoss(4, "Stale", "TOTP", "The otp field holds a counter-based HOTP, which keypaste does not compute."),
            ],
            plan.Losses);
        Assert.Equal(["payroll"], plan.Projects);
        Assert.Equal(5, plan.Landing);

        Assert.Null(_vault.Find(new EntryName("imported", "Bank")));
        Assert.Equal(VaultSaveStatus.Saved, _vault.SaveState().Status);
        Assert.Equal(before, File.ReadAllBytes(_vaultPath));
    }

    [Fact]
    public void A_folder_named_like_the_recycle_bin_lands_beside_it_and_a_default_group_is_named_after_the_file()
    {
        using var source = Open([new ExportItem { Title = "Old", Folder = ["Recycle Bin"], Password = "export-pw-old" }]);

        var plan = source.Plan(_vault, into: null);

        Assert.Equal("export", plan.Into);
        var row = Assert.Single(plan.Rows);
        Assert.Equal(new EntryName("export/Recycle Bin (imported)", "Old"), row.Lands);
        Assert.Equal("Recycle Bin is the recycle bin's name", row.Note);
    }

    // ---------------------------------------------------------------- collision decisions

    [Fact]
    public void Skipping_a_taken_name_keeps_the_vault_item_and_imports_the_rest()
    {
        using var source = Open(Items());
        var plan = source.Plan(_vault, "imported", new Dictionary<int, CollisionDecision> { [0] = CollisionDecision.Skip, [1] = CollisionDecision.Skip });

        Assert.Equal(new ExportRow(0, "Work/GitHub", null, ExportCollision.VaultItem, CollisionDecision.Skip, "'GitHub' is already an item there"), plan.Rows[0]);
        Assert.Equal(new EntryName("imported", "Bank"), plan.Rows[1].Lands);
        Assert.Equal(4, plan.Landing);

        var result = source.Commit(_vault, plan, vault => vault.Save());

        Assert.Equal(4, result.Entries);
        using var reopened = Vault.Open(_vaultPath, _master);
        Assert.Equal("vault-pw-github", reopened.Find(_github)!.Password);
        Assert.Null(reopened.Find(new EntryName("imported/Work", "GitHub (2)")));
        Assert.Equal("export-pw-bank-two", reopened.Find(new EntryName("imported", "Bank (2)"))!.Password);
    }

    [Fact]
    public void An_item_the_export_holds_twice_is_kept_beside_the_other_or_skipped()
    {
        using var source = Open(
        [
            new ExportItem { Title = "Twice", Password = "export-pw-first" },
            new ExportItem { Title = "Twice", Password = "export-pw-second" },
        ]);
        var kept = source.Plan(_vault, "imported");
        Assert.Equal(new ExportRow(1, "Twice", new EntryName("imported", "Twice (2)"), ExportCollision.ExportItem, CollisionDecision.KeepBoth, "'Twice' is another item of this export"), kept.Rows[1]);

        var plan = source.Plan(_vault, "imported", new Dictionary<int, CollisionDecision> { [1] = CollisionDecision.Skip });
        source.Commit(_vault, plan, vault => vault.Save());

        Assert.Null(plan.Rows[1].Lands);
        Assert.Equal("export-pw-first", _vault.Find(new EntryName("imported", "Twice"))!.Password);
        Assert.Null(_vault.Find(new EntryName("imported", "Twice (2)")));
    }

    // ---------------------------------------------------------------- the commit

    [Fact]
    public void A_commit_adds_every_part_the_plan_lands_as_one_change_and_saves()
    {
        using var source = Open(Items());
        var plan = source.Plan(_vault, "imported");
        List<VaultEdit> edits = [];
        _vault.Edited += (_, edit) => edits.Add(edit);

        var result = source.Commit(_vault, plan, vault => vault.Save());

        Assert.Equal(5, result.Entries);
        Assert.Equal(1, result.Groups);
        var edit = Assert.Single(edits);
        Assert.Equal(plan.Rows.Select(row => row.Lands).OfType<EntryName>().ToHashSet(), edit.Entries.ToHashSet());
        Assert.Equal(VaultSaveStatus.Saved, _vault.SaveState().Status);

        var bank = new EntryName("imported", "Bank");
        using (var reopened = Vault.Open(_vaultPath, _master))
        {
            var entry = reopened.Find(bank)!;
            Assert.Equal(("export-user-bank", "export-pw-bank", "https://bank.example/one", "export-notes-bank"), (entry.Username, entry.Password, entry.Url, entry.Notes));
            Assert.Equal(
                [new EntryField("KP2A_URL_1", false, true), new EntryField("PIN", true, false), new EntryField("Plain", false, false), new EntryField("otp", true, true)],
                reopened.Fields(bank));
            Assert.Equal("export-pin-bank", reopened.ReadField(bank, "PIN"));
            Assert.Equal("https://bank.example/two", reopened.ReadField(bank, "KP2A_URL_1"));
            Assert.Equal("otpauth://totp/bank?secret=JBSWY3DPEHPK3PXP", reopened.ReadField(bank, "otp"));
            using (var totp = reopened.ReadTotp(bank, out var refusal))
            {
                Assert.Null(refusal);
                Assert.Equal((6, TimeSpan.FromSeconds(30)), (totp!.Digits, totp.Period));
            }

            Assert.Equal(["env:payroll", "finance"], reopened.Tags(bank)!.Order(StringComparer.Ordinal));
            Assert.Equal([new EntryAttachment("statement.txt", _statement.Length)], reopened.Attachments(bank));

            var revision = Assert.Single(reopened.ReadHistory(bank)!);
            Assert.Equal("export-old-pw-bank", revision.Fields.Password);
            Assert.Equal(new DateTime(2025, 3, 4, 5, 6, 7, DateTimeKind.Utc), revision.ModifiedUtc);
            Assert.Equal(
                (new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero), new DateTimeOffset(2025, 6, 7, 8, 9, 10, TimeSpan.Zero)),
                (reopened.ReadTimes(bank)!.Created, reopened.ReadTimes(bank)!.Modified));

            var stale = new EntryName("imported", "Stale");
            Assert.Equal(new DateTimeOffset(2021, 1, 1, 0, 0, 1, TimeSpan.Zero), reopened.ReadTimes(stale)!.Modified);
            Assert.Equal("export-old-pw-stale", Assert.Single(reopened.ReadHistory(stale)!).Fields.Password);
            Assert.Empty(reopened.Fields(stale)!);

            Assert.Equal("export-pw-github", reopened.Find(new EntryName("imported/Work", "GitHub (2)"))!.Password);
            Assert.Equal("vault-pw-github", reopened.Find(_github)!.Password);
            Assert.Equal("export-pw-slash", reopened.Find(new EntryName("imported/Personal", "a-b"))!.Password);
            Assert.Empty(reopened.ReadHistory(new EntryName("imported/Personal", "a-b"))!);
        }

        using var interop = KeePassInterop.Open(_vaultPath, Encoding.UTF8.GetBytes(_master));
        Assert.Equal(_statement, interop.FactsUnchecked(bank)!.Attachments["statement.txt"]);
    }

    [Fact]
    public void A_save_that_fails_takes_everything_back_out_of_the_open_vault()
    {
        var before = File.ReadAllBytes(_vaultPath);
        var groups = _vault.ReadGroupPaths(includeReserved: true);
        using var source = Open(Items());
        var plan = source.Plan(_vault, "imported");

        var failure = Assert.Throws<IOException>(() => source.Commit(_vault, plan, _ => throw new IOException("the disk is full")));

        Assert.Equal("the disk is full", failure.Message);
        AssertNothingImported(groups);
        Assert.Equal(VaultSaveStatus.Saved, _vault.SaveState().Status);
        Assert.Equal(before, File.ReadAllBytes(_vaultPath));

        source.Commit(_vault, source.Plan(_vault, "imported"), vault => vault.Save());
        Assert.NotNull(_vault.Find(new EntryName("imported", "Bank")));
    }

    [Fact]
    public void A_vault_another_program_saved_meanwhile_keeps_its_change_and_gains_nothing()
    {
        var groups = _vault.ReadGroupPaths(includeReserved: true);
        using var source = Open(Items());
        var plan = source.Plan(_vault, "imported");

        using (var other = Vault.Open(_vaultPath, _master))
        {
            other.AddEntry(new VaultEntry { GroupPath = "elsewhere", Title = "Theirs", Password = "their-pw" });
            other.Save();
        }

        Assert.Throws<VaultChangedOnDiskException>(() => source.Commit(_vault, plan, vault => vault.Save()));

        AssertNothingImported(groups);
        using var reopened = Vault.Open(_vaultPath, _master);
        Assert.NotNull(reopened.Find(new EntryName("elsewhere", "Theirs")));
        Assert.Null(reopened.Find(new EntryName("imported", "Bank")));
    }

    [Fact]
    public void A_plan_the_vault_no_longer_matches_is_refused_before_anything_is_added()
    {
        using var source = Open(Items());
        var plan = source.Plan(_vault, "imported");
        _vault.AddEntry(new VaultEntry { GroupPath = "imported/Personal", Title = "a-b", Password = "vault-pw-ab" });
        _vault.Save();
        var groups = _vault.ReadGroupPaths(includeReserved: true);
        var saved = File.ReadAllBytes(_vaultPath);

        var refused = Assert.Throws<VaultException>(() => source.Commit(_vault, plan, vault => vault.Save()));

        Assert.Equal("Nothing was imported: the vault changed since the preview. Preview the import again.", refused.Message);
        Assert.Null(_vault.Find(new EntryName("imported", "Bank")));
        Assert.Equal(saved, File.ReadAllBytes(_vaultPath));
        Assert.Equal(groups, _vault.ReadGroupPaths(includeReserved: true));
    }

    [Theory]
    [InlineData("Recycle Bin", "Recycle Bin is the recycle bin's name")]
    [InlineData(".keypaste/tokens", ".keypaste is keypaste's own group; nothing is imported into it")]
    [InlineData("", "name a group to import into")]
    public void A_refused_group_stops_the_commit(string into, string refusal)
    {
        var groups = _vault.ReadGroupPaths(includeReserved: true);
        using var source = Open(Items());

        var plan = source.Plan(_vault, into);

        Assert.Equal(refusal, plan.Refusal);
        var refused = Assert.Throws<VaultException>(() => source.Commit(_vault, plan, vault => vault.Save()));
        Assert.Equal($"Nothing was imported: {refusal}.", refused.Message);
        AssertNothingImported(groups);
    }

    [Fact]
    public void A_group_two_vault_groups_answer_to_stops_the_commit()
    {
        _vault.AddGroupUnchecked("imported", "Personal");
        _vault.AddGroupUnchecked("imported", "Personal");
        using var source = Open(Items());

        var plan = source.Plan(_vault, "imported");

        Assert.Equal("imported/Personal names 2 groups in this vault; rename one in KeePassXC", plan.Refusal);
    }

    // ---------------------------------------------------------------- reading the file

    [Fact]
    public void A_file_no_reader_recognizes_opens_nothing()
    {
        var path = Path.Combine(_exports, "notes.txt");
        File.WriteAllText(path, "not an export");
        var reader = new FakeExportReader(() => new ExportContents([], []));

        using var opened = ExportImport.Open(path, [reader]);

        Assert.Null(opened);
        Assert.Equal(0, reader.Reads);
    }

    [Fact]
    public void A_file_larger_than_its_reader_takes_is_refused_before_it_is_read()
    {
        var path = Path.Combine(_exports, "big.fake");
        byte[] bytes = [.. FakeExportReader.Magic, .. new byte[100]];
        File.WriteAllBytes(path, bytes);
        var reader = new FakeExportReader(() => new ExportContents([], []), maximumBytes: 64);

        var refused = Assert.Throws<VaultException>(() => ExportImport.Open(path, [reader]));

        Assert.Equal("big.fake is larger than 64 bytes, the most keypaste reads of a Fake export.", refused.Message);
        Assert.Equal(0, reader.Reads);
    }

    [Fact]
    public void A_reader_failure_is_said_without_its_message_unless_it_is_the_readers_own_refusal()
    {
        var path = Write("export.fake");

        var failed = Assert.Throws<VaultException>(() => ExportImport.Open(path, [new FakeExportReader(() => throw new InvalidOperationException("near export-pw-bank"))]));
        var refused = Assert.Throws<VaultException>(() => ExportImport.Open(path, [new FakeExportReader(() => throw new ExportFormatException("item 3 has no name"))]));

        Assert.Equal("export.fake could not be read as a Fake export.", failed.Message);
        Assert.Equal("export.fake is not a Fake export keypaste reads: item 3 has no name", refused.Message);
    }

    // ---------------------------------------------------------------- values stay out

    [Fact]
    public void No_name_note_or_text_shows_a_value_and_no_copy_of_the_export_is_written()
    {
        var items = Items();
        using var source = Open(items);
        var exportFiles = Directory.GetFileSystemEntries(_exports);
        var plan = source.Plan(_vault, "imported");
        source.Commit(_vault, plan, vault => vault.Save());

        var said = new List<string?> { plan.Into, plan.Refusal };
        said.AddRange(plan.Rows.SelectMany(row => new[] { row.Item, row.Note, row.ToString() }));
        said.AddRange(plan.Losses.SelectMany(loss => new[] { loss.Part, loss.Reason, loss.ToString() }));
        said.AddRange(items.Select(item => item.ToString()));
        said.AddRange(items.SelectMany(item => item.Fields).Select(field => field.ToString()));
        said.AddRange(items.SelectMany(item => item.History).Select(version => version.ToString()));
        said.AddRange(items.SelectMany(item => item.Attachments).Select(attachment => attachment.ToString()));

        Assert.All(_values, value => Assert.DoesNotContain(said, text => text is not null && text.Contains(value, StringComparison.Ordinal)));
        Assert.Equal(exportFiles, Directory.GetFileSystemEntries(_exports));
    }

    [Fact]
    public void Disposing_the_source_zeroes_every_attachment()
    {
        var items = Items();
        using var source = Open(items);
        var attachments = items.SelectMany(item => item.Attachments).ToList();

        source.Dispose();

        Assert.All(attachments, attachment => Assert.True(attachment.Content.Span.IndexOfAnyExcept((byte)0) < 0, $"{attachment} was not zeroed"));
        Assert.Throws<ObjectDisposedException>(() => source.Plan(_vault, "imported"));
    }

    private void AssertNothingImported(IReadOnlyList<string> groups)
    {
        Assert.Equal(groups, _vault.ReadGroupPaths(includeReserved: true));
        Assert.Null(_vault.Find(new EntryName("imported", "Bank")));
        Assert.Null(_vault.Find(new EntryName("imported/Work", "GitHub (2)")));
        Assert.Equal("vault-pw-github", _vault.Find(_github)!.Password);
    }

    /// <summary>One of each thing an item may carry, one item that collides with the vault and one with the export, and one changed before its last version whose TOTP keypaste does not read.</summary>
    private List<ExportItem> Items() =>
    [
        new ExportItem { Title = "GitHub", Folder = ["Work"], Password = "export-pw-github" },
        new ExportItem
        {
            Title = "Bank",
            Username = "export-user-bank",
            Password = "export-pw-bank",
            Urls = ["https://bank.example/one", "https://bank.example/two"],
            Notes = "export-notes-bank",
            Fields =
            [
                new ExportField("PIN", "export-pin-bank"),
                new ExportField("Plain", "shown", Protect: false),
                new ExportField("Password", "export-standard-field"),
                new ExportField("otp", "export-otp-field"),
                new ExportField("PIN", "export-pin-twice"),
            ],
            Tags = ["finance", "finance", "a,b", "env:payroll"],
            Totp = "otpauth://totp/bank?secret=JBSWY3DPEHPK3PXP",
            Attachments =
            [
                new ExportAttachment("statement.txt", [.. _statement]),
                new ExportAttachment("big.bin", new byte[EntryAttachment.MaximumSize + 1]),
                new ExportAttachment("a/b", [1, 2, 3]),
            ],
            History = [new ExportVersion(new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero), "export-user-bank", "export-old-pw-bank", "https://bank.example/one", string.Empty)],
            Omitted = [new ExportOmission("passkey", "keypaste holds no passkeys")],
            Created = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero),
            Modified = new DateTimeOffset(2025, 6, 7, 8, 9, 10, TimeSpan.Zero),
        },
        new ExportItem { Title = "a/b", Folder = ["Personal"], Password = "export-pw-slash" },
        new ExportItem { Title = "Bank", Password = "export-pw-bank-two" },
        new ExportItem { Title = "Stale", Password = "export-pw-stale", Totp = "otpauth://hotp/stale?secret=KRSXG5CTMVRXEZLU&counter=1", Modified = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), History = [new ExportVersion(new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero), string.Empty, "export-old-pw-stale", string.Empty, string.Empty)] },
    ];

    private ExportSource Open(IReadOnlyList<ExportItem> items, IReadOnlyList<ExportOmission>? unread = null) =>
        ExportImport.Open(Write("export.fake"), [new FakeExportReader(() => new ExportContents(items, unread ?? []))])
            ?? throw new InvalidOperationException("the fake reader did not recognize its own file");

    private string Write(string name)
    {
        var path = Path.Combine(_exports, name);
        byte[] bytes = [.. FakeExportReader.Magic, .. "the fake reader ignores the rest"u8];
        File.WriteAllBytes(path, bytes);
        return path;
    }
}

/// <summary>A reader of a format nobody exports, recognized by its first bytes, that hands back what a test gives it.</summary>
internal sealed class FakeExportReader(Func<ExportContents> read, int maximumBytes = 1024 * 1024) : IExportReader
{
    internal static readonly byte[] Magic = "FAKE-EXPORT\n"u8.ToArray();

    public string Format => "Fake";

    public int MaximumBytes => maximumBytes;

    internal int Reads { get; private set; }

    public bool Recognizes(string fileName, ReadOnlySpan<byte> start) => start.StartsWith(Magic);

    public ExportContents Read(ReadOnlySpan<byte> file)
    {
        Reads++;
        return read();
    }
}
