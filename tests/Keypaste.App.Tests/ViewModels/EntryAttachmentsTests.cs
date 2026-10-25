using System.Text;
using KeePassLib.Security;
using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Internal;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// The item pane's attachments: listed by name and size, each add, replace and remove one revision written through core,
/// and a copy saved only where nothing is, each refusal said in a sentence and writing nothing (V.8b).
/// </summary>
public sealed class EntryAttachmentsTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private static readonly EntryName _checking = new("Banking", "Checking");
    private static readonly EntryName _loose = new(string.Empty, "Loose");
    private static readonly byte[] _statement = Encoding.UTF8.GetBytes("statement bytes");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-entry-attachments-").FullName;
    private readonly string _vaultPath;

    public EntryAttachmentsTests()
    {
        _vaultPath = Path.Combine(_directory, "vault.kdbx");
        KeePassInterop.WriteForeignUnchecked(_vaultPath, Encoding.UTF8.GetBytes(_master), null, "AES-KDF", "AES-256");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void The_pane_lists_each_attachment_by_name_and_size()
    {
        using var context = new Context(_vaultPath);

        var rows = context.Open(_checking).Attachments.Rows;
        Assert.Equal(["statement.txt"], rows.Select(row => row.Name));
        Assert.Equal(["15 bytes"], rows.Select(row => row.SizeText));
        Assert.Equal("Save statement.txt", rows[0].SaveLabel);

        var loose = context.Open(_loose).Attachments;
        Assert.Empty(loose.Rows);
        Assert.False(loose.HasRows);
    }

    [Fact]
    public async Task Adding_a_file_attaches_it_under_its_own_name_as_one_revision()
    {
        using var context = new Context(_vaultPath);
        var attachments = context.Open(_checking).Attachments;
        var revisions = Revisions();
        var bytes = Encoding.UTF8.GetBytes("-----BEGIN KEY-----");
        context.Picker.AttachmentPath = Chosen("deploy-key.pem", bytes);

        await attachments.AddCommand.ExecuteAsync();

        Assert.Equal(1, context.Picker.AttachmentCalls);
        Assert.Null(context.Entries.Error);
        Assert.Null(attachments.Note);
        Assert.Equal(["deploy-key.pem", "statement.txt"], attachments.Rows.Select(row => row.Name));
        Assert.Equal(revisions + 1, Revisions());
        Assert.Equal(bytes, Saved(_checking, "deploy-key.pem"));
        Assert.Equal(_statement, Saved(_checking, "statement.txt"));
    }

    [Fact]
    public async Task A_cancelled_picker_writes_nothing()
    {
        using var context = new Context(_vaultPath);
        var attachments = context.Open(_checking).Attachments;
        var before = File.ReadAllBytes(_vaultPath);

        await attachments.AddCommand.ExecuteAsync();
        await attachments.Rows[0].ReplaceCommand.ExecuteAsync();
        await attachments.Rows[0].SaveCommand.ExecuteAsync();

        Assert.Equal(2, context.Picker.AttachmentCalls);
        Assert.Equal(1, context.Picker.AttachmentDestinationCalls);
        Assert.Null(context.Entries.Error);
        Assert.Equal(before, File.ReadAllBytes(_vaultPath));
    }

    [Fact]
    public async Task A_file_over_the_cap_a_name_the_item_has_and_a_file_that_cannot_be_read_write_nothing_and_say_why()
    {
        using var context = new Context(_vaultPath);
        var attachments = context.Open(_checking).Attachments;
        var before = File.ReadAllBytes(_vaultPath);

        context.Picker.AttachmentPath = Chosen("large.bin", new byte[EntryAttachment.MaximumSize + 1]);
        await attachments.AddCommand.ExecuteAsync();
        Assert.Equal("That file is larger than 5 MiB, the most keypaste attaches.", context.Entries.Error);

        context.Picker.AttachmentPath = Chosen("statement.txt", [1, 2, 3]);
        await attachments.AddCommand.ExecuteAsync();
        Assert.Equal("This item already has an attachment called statement.txt. Replace it instead.", context.Entries.Error);

        context.Picker.AttachmentPath = Path.Combine(_directory, "missing.bin");
        await attachments.AddCommand.ExecuteAsync();
        Assert.Equal("keypaste couldn't read missing.bin. Check that it's still there and that you can open it, then choose it again.", context.Entries.Error);

        Assert.Equal(before, File.ReadAllBytes(_vaultPath));
        Assert.Equal(["statement.txt"], attachments.Rows.Select(row => row.Name));
    }

    [Fact]
    public async Task Exactly_the_cap_is_attached()
    {
        using var context = new Context(_vaultPath);
        var attachments = context.Open(_loose).Attachments;
        context.Picker.AttachmentPath = Chosen("full.bin", new byte[EntryAttachment.MaximumSize]);

        await attachments.AddCommand.ExecuteAsync();

        Assert.Null(context.Entries.Error);
        Assert.Equal(["5.0 MiB"], attachments.Rows.Select(row => row.SizeText));
    }

    [Fact]
    public async Task Replacing_keeps_the_name_as_one_revision()
    {
        using var context = new Context(_vaultPath);
        var attachments = context.Open(_checking).Attachments;
        var revisions = Revisions();
        var bytes = Encoding.UTF8.GetBytes("the new statement");
        context.Picker.AttachmentPath = Chosen("march.txt", bytes);

        await attachments.Rows[0].ReplaceCommand.ExecuteAsync();

        Assert.Null(context.Entries.Error);
        Assert.Null(attachments.Note);
        Assert.Equal(["statement.txt"], attachments.Rows.Select(row => row.Name));
        Assert.Equal(["17 bytes"], attachments.Rows.Select(row => row.SizeText));
        Assert.Equal(revisions + 1, Revisions());
        Assert.Equal(bytes, Saved(_checking, "statement.txt"));
    }

    [Fact]
    public void Removing_asks_first_and_is_one_revision()
    {
        using var context = new Context(_vaultPath);
        var attachments = context.Open(_checking).Attachments;
        var revisions = Revisions();
        var before = File.ReadAllBytes(_vaultPath);

        attachments.Rows[0].RemoveCommand.Execute(null);
        Assert.True(attachments.IsRemoving);
        Assert.Equal(
            "Remove statement.txt from this item? History keeps it unless the item is larger than this vault's history size limit.",
            attachments.RemovePrompt);

        attachments.CancelRemoveCommand.Execute(null);
        Assert.False(attachments.IsRemoving);
        Assert.Equal(before, File.ReadAllBytes(_vaultPath));

        attachments.Rows[0].RemoveCommand.Execute(null);
        attachments.ConfirmRemoveCommand.Execute(null);

        Assert.Null(context.Entries.Error);
        Assert.False(attachments.IsRemoving);
        Assert.Empty(attachments.Rows);
        Assert.Equal(revisions + 1, Revisions());

        using var vault = Vault.Open(_vaultPath, _master);
        Assert.Empty(vault.Attachments(_checking)!);
    }

    [Fact]
    public async Task Saving_writes_a_copy_where_the_person_chose_and_says_it_is_not_encrypted()
    {
        using var context = new Context(_vaultPath);
        var attachments = context.Open(_checking).Attachments;
        var before = File.ReadAllBytes(_vaultPath);
        var destination = Path.Combine(Directory.CreateDirectory(Path.Combine(_directory, "saved")).FullName, "statement.txt");
        context.Picker.AttachmentDestination = destination;

        await attachments.Rows[0].SaveCommand.ExecuteAsync();

        Assert.Equal("statement.txt", context.Picker.SuggestedAttachmentName);
        Assert.Null(context.Entries.Error);
        Assert.Equal(_statement, File.ReadAllBytes(destination));
        Assert.Equal(before, File.ReadAllBytes(_vaultPath));
        Assert.StartsWith($"Saved statement.txt to {destination}. The file isn't encrypted, and ", attachments.Note, StringComparison.Ordinal);
        Assert.EndsWith("Delete it when you're done: keypaste can't take it back.", attachments.Note, StringComparison.Ordinal);

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(destination));
        }
    }

    [Fact]
    public async Task Saving_where_a_file_already_is_replaces_nothing_and_says_to_choose_another_name()
    {
        using var context = new Context(_vaultPath);
        var attachments = context.Open(_checking).Attachments;
        var occupied = Chosen("statement.txt", Encoding.UTF8.GetBytes("keep me"));
        context.Picker.AttachmentDestination = occupied;

        await attachments.Rows[0].SaveCommand.ExecuteAsync();

        Assert.Equal("keep me", File.ReadAllText(occupied));
        Assert.Equal("There's already a file there, and keypaste won't replace it. Save again and choose a name that isn't taken.", context.Entries.Error);
        Assert.Null(attachments.Note);
    }

    /// <summary>With a 5 MiB and a 2 MiB attachment, the item as it was is over the vault's 6 MiB history size, so history drops it and the pane says so.</summary>
    [Fact]
    public void An_edit_history_cannot_keep_says_what_is_gone()
    {
        using (var vault = Vault.Open(_vaultPath, _master))
        {
            using var large = new MemoryStream(new byte[EntryAttachment.MaximumSize]);
            using var smaller = new MemoryStream(new byte[2 * 1024 * 1024]);
            Assert.True(vault.AddAttachment(_checking, "a.bin", large, out _));
            Assert.True(vault.AddAttachment(_checking, "b.bin", smaller, out _));
            vault.Save();
        }

        using var context = new Context(_vaultPath);
        var attachments = context.Open(_checking).Attachments;

        attachments.Rows.Single(row => row.Name == "a.bin").RemoveCommand.Execute(null);
        attachments.ConfirmRemoveCommand.Execute(null);

        Assert.Null(context.Entries.Error);
        Assert.Equal(
            "a.bin is removed for good: the item as it was is larger than this vault's history size limit, so history doesn't keep it.",
            attachments.Note);
        Assert.Equal(["b.bin", "statement.txt"], attachments.Rows.Select(row => row.Name));
    }

    /// <summary>
    /// An attachment another program named with a bidi override and path separators is drawn without them, offered to the
    /// save dialog as one file name, and saved by the name the vault holds to the path the person chose.
    /// </summary>
    [Fact]
    public async Task A_name_another_program_wrote_is_drawn_and_offered_safely_and_still_saves()
    {
        var hostile = "../../.ssh/" + (char)0x202E + "txt.exe";
        var bytes = Encoding.UTF8.GetBytes("planted");
        ForeignEdit.Apply(_vaultPath, _master, "Loose", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), entry => entry.Binaries.Set(hostile, new ProtectedBinary(false, bytes)));

        using var context = new Context(_vaultPath);
        var row = Assert.Single(context.Open(_loose).Attachments.Rows);
        var saved = Directory.CreateDirectory(Path.Combine(_directory, "saved")).FullName;

        Assert.Equal(hostile, row.Name);
        Assert.Equal(".. .. .ssh txt.exe", row.DisplayName);
        Assert.Equal(".. .. .ssh txt.exe", row.SuggestedFileName);

        context.Picker.AttachmentDestination = Path.Combine(saved, row.SuggestedFileName);
        await row.SaveCommand.ExecuteAsync();

        Assert.Null(context.Entries.Error);
        Assert.Equal([".. .. .ssh txt.exe"], Directory.EnumerateFileSystemEntries(saved).Select(path => Path.GetFileName(path)));
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(saved, row.SuggestedFileName)));
    }

    [Theory]
    [InlineData("statement.txt", "statement.txt")]
    [InlineData(".env", ".env")]
    [InlineData("..", "attachment")]
    [InlineData("a:b?.txt", "a_b_.txt")]
    [InlineData("say \"hi\"*.txt", "say _hi__.txt")]
    [InlineData("C:\\Users\\maya\\key.txt", "C_ Users maya key.txt")]
    [InlineData("trailing. ", "trailing")]
    [InlineData("\u202Eexe.pdf", "exe.pdf")]
    public void A_suggested_file_name_is_one_file_name(string name, string suggested) =>
        Assert.Equal(suggested, EntryAttachmentRow.SafeFileName(name));

    /// <summary>The shell disposes the pane when the vault locks, so a file chosen after that is never attached.</summary>
    [Fact]
    public async Task A_lock_while_the_picker_is_open_writes_nothing()
    {
        using var context = new Context(_vaultPath);
        var attachments = context.Open(_checking).Attachments;
        var before = File.ReadAllBytes(_vaultPath);
        context.Picker.AttachmentPath = Chosen("deploy-key.pem", [1, 2, 3]);
        context.Picker.WhileAttachmentPickerOpen = () =>
        {
            context.Session.Lock(VaultLockReason.Manual);
            context.Entries.Dispose();
        };

        await attachments.AddCommand.ExecuteAsync();

        Assert.Equal(1, context.Picker.AttachmentCalls);
        Assert.Equal(before, File.ReadAllBytes(_vaultPath));
        Assert.Empty(attachments.Rows);
    }

    private string Chosen(string name, byte[] bytes)
    {
        var path = Path.Combine(Directory.CreateDirectory(Path.Combine(_directory, "chosen")).FullName, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private byte[] Saved(EntryName item, string attachment)
    {
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("n"));
        using var vault = Vault.Open(_vaultPath, _master);
        Assert.Equal(AttachmentSaveOutcome.Saved, vault.SaveAttachment(item, attachment, path, out _));
        var bytes = File.ReadAllBytes(path);
        File.Delete(path);
        return bytes;
    }

    private int Revisions()
    {
        using var vault = Vault.Open(_vaultPath, _master);
        return vault.ReadHistory(_checking)!.Count;
    }

    private sealed class Context : IDisposable
    {
        internal Context(string vaultPath)
        {
            Session = new AppVaultSession(new ManualClock(AppClock.Start));

            using (var master = TempVault.Secret(_master))
            {
                Assert.Equal(UnlockOutcome.Opened, Session.TryUnlock(vaultPath, master.Value));
            }

            Countdown = new ClipboardCountdown(new FakeClipboard(), new ManualClock(AppClock.Start));
            Entries = new EntriesViewModel(Session, Countdown, picker: Picker);
        }

        internal AppVaultSession Session { get; }

        internal ClipboardCountdown Countdown { get; }

        internal EntriesViewModel Entries { get; }

        internal FakeVaultFilePicker Picker { get; } = new();

        internal EntryDetailViewModel Open(EntryName name)
        {
            Entries.Selected = Entries.Rows.Single(row => row.Title == name.Title && row.GroupPath == name.GroupPath);
            return Entries.Detail!;
        }

        public void Dispose()
        {
            Entries.Dispose();
            Countdown.Dispose();
            Session.Dispose();
        }
    }
}
