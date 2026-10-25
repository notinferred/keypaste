using System.Text;
using Keypaste.Core.Internal;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// An item's attachments: listed by name and size, added and replaced under the cap, removed, each as one revision,
/// and saved once to a new file (V.8a, D-0533, D-0534).
/// </summary>
public sealed class VaultAttachmentTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private static readonly EntryName _stripe = new("api", "Stripe");
    private static readonly EntryName _checking = new("Banking", "Checking");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-attachment-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void An_added_attachment_is_one_revision_naming_the_entry_and_survives_a_save_byte_for_byte()
    {
        var path = NewVaultPath();
        var bytes = RandomBytes(4096);
        var edits = new List<VaultEdit>();

        using (var vault = Seeded(path))
        {
            vault.Edited += (_, edit) => edits.Add(edit);

            Assert.True(Add(vault, "statement.pdf", bytes, out var kept));
            Assert.True(kept);
            Assert.Equal([_stripe], Assert.Single(edits).Entries);
            Assert.Equal(SavedRead.Unsaved, vault.ReadSaved(out _));
            vault.Save();
        }

        using (var reopened = Vault.Open(path, _master))
        {
            Assert.Equal([new EntryAttachment("statement.pdf", 4096)], reopened.Attachments(_stripe));
            Assert.Single(reopened.ReadHistory(_stripe)!);
        }

        using var interop = KeePassInteropFor(path);
        var facts = interop.FactsUnchecked(_stripe)!;
        Assert.Equal(bytes, facts.Attachments["statement.pdf"]);
        Assert.Empty(Assert.Single(facts.HistoryAttachments));
    }

    [Fact]
    public void Attachments_are_listed_in_ordinal_order_and_an_unknown_item_is_null()
    {
        using var vault = Seeded(NewVaultPath());

        Assert.Empty(vault.Attachments(_stripe)!);
        Add(vault, "b.txt", [1], out _);
        Add(vault, "B.txt", [1, 2], out _);
        Add(vault, "a.txt", [], out _);

        Assert.Equal(
            [new EntryAttachment("B.txt", 2), new EntryAttachment("a.txt", 0), new EntryAttachment("b.txt", 1)],
            vault.Attachments(_stripe));
        Assert.Null(vault.Attachments(new EntryName("api", "Nobody")));
    }

    [Theory]
    [InlineData(1, "1 byte")]
    [InlineData(812, "812 bytes")]
    [InlineData(4096, "4.0 KiB")]
    [InlineData(2621440, "2.5 MiB")]
    public void A_size_reads_as_a_person_says_it(int size, string text) =>
        Assert.Equal(text, new EntryAttachment("a", size).SizeText);

    [Fact]
    public void Adding_a_name_the_item_already_has_is_refused_and_changes_nothing()
    {
        using var vault = Seeded(NewVaultPath());
        Add(vault, "notes.txt", [1, 2, 3], out _);
        vault.Save();
        var edits = new List<VaultEdit>();
        vault.Edited += (_, edit) => edits.Add(edit);

        Assert.Throws<VaultException>(() => Add(vault, "notes.txt", [9], out _));

        Assert.Empty(edits);
        Assert.Equal(SavedRead.Current, vault.ReadSaved(out _));
        Assert.Equal([new EntryAttachment("notes.txt", 3)], vault.Attachments(_stripe));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("tab\there")]
    [InlineData("line\nbreak")]
    [InlineData("a/b.txt")]
    [InlineData("a\\b.txt")]
    [InlineData(".")]
    [InlineData("..")]
    public void A_refused_name_changes_nothing(string name)
    {
        using var vault = Seeded(NewVaultPath());
        var edits = new List<VaultEdit>();
        vault.Edited += (_, edit) => edits.Add(edit);

        Assert.False(VaultNameRules.IsValidAttachmentName(name, out var error));
        Assert.NotEmpty(error);
        Assert.Throws<VaultException>(() => Add(vault, name, [1], out _));

        Assert.Empty(edits);
        Assert.Equal(SavedRead.Current, vault.ReadSaved(out _));
        Assert.Empty(vault.Attachments(_stripe)!);
    }

    [Fact]
    public void Content_of_exactly_the_cap_is_attached()
    {
        using var vault = Seeded(NewVaultPath());

        Assert.True(Add(vault, "full.bin", new byte[EntryAttachment.MaximumSize], out var kept));

        Assert.True(kept);
        Assert.Equal([new EntryAttachment("full.bin", EntryAttachment.MaximumSize)], vault.Attachments(_stripe));
    }

    /// <summary>A file's length refuses it before anything is read; a stream that cannot say is read one byte past the cap and refused there.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Content_over_the_cap_is_refused_and_changes_nothing(bool seekable)
    {
        using var vault = Seeded(NewVaultPath());
        Add(vault, "kept.bin", [7, 7], out _);
        vault.Save();
        var edits = new List<VaultEdit>();
        vault.Edited += (_, edit) => edits.Add(edit);
        var over = new byte[EntryAttachment.MaximumSize + 1];

        using (var added = Content(over, seekable))
        {
            var refused = Assert.Throws<VaultException>(() => vault.AddAttachment(_stripe, "big.bin", added, out _));
            Assert.Contains("5 MiB", refused.Message, StringComparison.Ordinal);
        }

        using (var replacing = Content(over, seekable))
        {
            Assert.Throws<VaultException>(() => vault.ReplaceAttachment(_stripe, "kept.bin", replacing, out _));
        }

        Assert.Empty(edits);
        Assert.Equal(SavedRead.Current, vault.ReadSaved(out _));
        Assert.Equal([new EntryAttachment("kept.bin", 2)], vault.Attachments(_stripe));
    }

    [Fact]
    public void Replacing_keeps_the_name_and_what_it_held_stays_in_history()
    {
        var path = NewVaultPath();
        byte[] first = RandomBytes(1024), second = RandomBytes(3072);

        using (var vault = Seeded(path))
        {
            Add(vault, "notes.bin", first, out _);
            vault.Save();

            using var content = new MemoryStream(second);
            Assert.True(vault.ReplaceAttachment(_stripe, "notes.bin", content, out var kept));
            Assert.True(kept);
            vault.Save();
        }

        using var interop = KeePassInteropFor(path);
        var facts = interop.FactsUnchecked(_stripe)!;
        Assert.Equal(second, Assert.Single(facts.Attachments).Value);
        Assert.Equal(first, facts.HistoryAttachments[^1]["notes.bin"]);
        Assert.Equal(2, facts.HistoryUuids.Count);
    }

    [Fact]
    public void Removing_is_one_revision_and_the_attachment_stays_in_history()
    {
        var path = NewVaultPath();
        var bytes = RandomBytes(2048);

        using (var vault = Seeded(path))
        {
            Add(vault, "notes.bin", bytes, out _);
            vault.Save();
            var edits = new List<VaultEdit>();
            vault.Edited += (_, edit) => edits.Add(edit);

            Assert.True(vault.RemoveAttachment(_stripe, "notes.bin", out var kept));
            Assert.True(kept);
            Assert.Equal([_stripe], Assert.Single(edits).Entries);
            vault.Save();
        }

        using var interop = KeePassInteropFor(path);
        var facts = interop.FactsUnchecked(_stripe)!;
        Assert.Empty(facts.Attachments);
        Assert.Equal(bytes, facts.HistoryAttachments[^1]["notes.bin"]);
        Assert.Equal(2, facts.HistoryUuids.Count);
    }

    [Fact]
    public void Replacing_or_removing_an_attachment_the_item_lacks_changes_nothing()
    {
        using var vault = Seeded(NewVaultPath());
        var edits = new List<VaultEdit>();
        vault.Edited += (_, edit) => edits.Add(edit);
        var nobody = new EntryName("api", "Nobody");

        using (var content = new MemoryStream([1]))
        {
            Assert.False(vault.ReplaceAttachment(_stripe, "absent.bin", content, out _));
        }

        Assert.False(vault.RemoveAttachment(_stripe, "absent.bin", out _));
        Assert.False(Add(vault, "new.bin", [1], out _, nobody));
        Assert.False(vault.RemoveAttachment(nobody, "absent.bin", out _));

        Assert.Empty(edits);
        Assert.Equal(SavedRead.Current, vault.ReadSaved(out _));
        Assert.Empty(vault.ReadHistory(_stripe)!);
    }

    /// <summary>
    /// KeePassLib drops a revision larger than the vault's history size limit, 6 MiB by default, as KeePassXC does, and the
    /// edit says so rather than claiming the item's history holds what it replaced (D-0533).
    /// </summary>
    [Fact]
    public void An_edit_whose_revision_is_over_the_history_limit_says_history_could_not_keep_it()
    {
        using var vault = Seeded(NewVaultPath());

        Assert.True(Add(vault, "a.bin", new byte[EntryAttachment.MaximumSize], out var keptFirst));
        Assert.True(Add(vault, "b.bin", new byte[2 * 1024 * 1024], out var keptSecond));
        Assert.True(vault.RemoveAttachment(_stripe, "a.bin", out var keptThird));

        Assert.True(keptFirst);
        Assert.True(keptSecond);
        Assert.False(keptThird);
        Assert.Empty(vault.ReadHistory(_stripe)!);
        Assert.Equal([new EntryAttachment("b.bin", 2 * 1024 * 1024)], vault.Attachments(_stripe));
    }

    /// <summary>The attachment KeePass wrote is kept byte for byte through keypaste's edits of the item's other attachments and fields.</summary>
    [Fact]
    public void An_attachment_keypaste_does_not_edit_is_kept_byte_for_byte()
    {
        var path = NewVaultPath();
        KdbxImportTests.WriteForeign(path, _master);
        byte[] original;

        using (var interop = KeePassInteropFor(path))
        {
            original = interop.FactsUnchecked(_checking)!.Attachments["statement.txt"];
        }

        using (var vault = Vault.Open(path, _master))
        {
            Add(vault, "added.bin", RandomBytes(512), out _, _checking);
            using (var content = new MemoryStream(RandomBytes(256)))
            {
                vault.ReplaceAttachment(_checking, "added.bin", content, out _);
            }

            vault.RemoveAttachment(_checking, "added.bin", out _);
            vault.SetFields(_checking, [new FieldWrite("PIN", "1111")]);
            vault.Save();
        }

        using var reopened = KeePassInteropFor(path);
        var facts = reopened.FactsUnchecked(_checking)!;
        var kept = Assert.Single(facts.Attachments);
        Assert.Equal("statement.txt", kept.Key);
        Assert.Equal(original, kept.Value);
    }

    [Fact]
    public void Saving_writes_the_bytes_once_to_a_new_file_only_its_owner_can_read()
    {
        using var vault = Seeded(NewVaultPath());
        var bytes = RandomBytes(3000);
        Add(vault, "statement.pdf", bytes, out _);
        var saves = Directory.CreateDirectory(Path.Combine(_directory, "saves")).FullName;
        var destination = Path.Combine(saves, "statement.pdf");

        Assert.Equal(AttachmentSaveOutcome.Saved, vault.SaveAttachment(_stripe, "statement.pdf", destination, out var failure));

        Assert.Empty(failure);
        Assert.Equal(bytes, File.ReadAllBytes(destination));
        Assert.Equal([destination], Directory.EnumerateFileSystemEntries(saves));

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(destination));
        }
    }

    [Fact]
    public void Saving_over_something_already_there_touches_nothing()
    {
        using var vault = Seeded(NewVaultPath());
        Add(vault, "statement.pdf", RandomBytes(64), out _);
        var saves = Directory.CreateDirectory(Path.Combine(_directory, "saves")).FullName;
        var occupied = Path.Combine(saves, "occupied.pdf");
        File.WriteAllText(occupied, "someone else's");
        var folder = Directory.CreateDirectory(Path.Combine(saves, "folder")).FullName;

        Assert.Equal(AttachmentSaveOutcome.PathAlreadyExists, vault.SaveAttachment(_stripe, "statement.pdf", occupied, out _));
        Assert.Equal(AttachmentSaveOutcome.PathAlreadyExists, vault.SaveAttachment(_stripe, "statement.pdf", folder, out _));

        Assert.Equal("someone else's", File.ReadAllText(occupied));
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
        Assert.Equal([folder, occupied], Directory.EnumerateFileSystemEntries(saves).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Saving_what_is_not_there_or_where_nothing_can_go_writes_nothing()
    {
        using var vault = Seeded(NewVaultPath());
        Add(vault, "statement.pdf", RandomBytes(64), out _);
        var saves = Directory.CreateDirectory(Path.Combine(_directory, "saves")).FullName;

        Assert.Equal(AttachmentSaveOutcome.NotFound, vault.SaveAttachment(_stripe, "absent.pdf", Path.Combine(saves, "a.pdf"), out _));
        Assert.Equal(AttachmentSaveOutcome.NotFound, vault.SaveAttachment(new EntryName("api", "Nobody"), "statement.pdf", Path.Combine(saves, "b.pdf"), out _));
        Assert.Equal(
            AttachmentSaveOutcome.Failed,
            vault.SaveAttachment(_stripe, "statement.pdf", Path.Combine(saves, "missing", "c.pdf"), out var failure));

        Assert.NotEmpty(failure);
        Assert.Empty(Directory.EnumerateFileSystemEntries(saves));
    }

    private static bool Add(Vault vault, string attachment, byte[] bytes, out bool kept, EntryName? name = null)
    {
        using var content = new MemoryStream(bytes);
        return vault.AddAttachment(name ?? _stripe, attachment, content, out kept);
    }

    private static Stream Content(byte[] bytes, bool seekable) =>
        seekable ? new MemoryStream(bytes) : new Unseekable(bytes);

    private static byte[] RandomBytes(int length) => System.Security.Cryptography.RandomNumberGenerator.GetBytes(length);

    private static Vault Seeded(string path)
    {
        var vault = Vault.Create(path, _master);
        vault.AddEntry(new VaultEntry { Title = _stripe.Title, GroupPath = _stripe.GroupPath, Password = "login-password" });
        vault.Save();
        return vault;
    }

    private static KeePassInterop KeePassInteropFor(string path) =>
        KeePassInterop.Open(path, Encoding.UTF8.GetBytes(_master));

    private string NewVaultPath() => Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".kdbx");

    /// <summary>A stream that reads like a pipe: no length and no seeking.</summary>
    private sealed class Unseekable(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
    }
}
