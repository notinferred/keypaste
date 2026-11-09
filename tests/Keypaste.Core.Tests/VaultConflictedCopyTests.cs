using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>A sync tool's conflicted copy of a vault, reviewed and merged entry by entry into the vault's file.</summary>
public sealed class VaultConflictedCopyTests : IDisposable
{
    private const string _password = "correct horse battery staple";
    private const string _work = "work";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-conflicted-").FullName;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    private string CopyPath => Path.Combine(_directory, "vault (conflicted copy 2026-11-09 120000).kdbx");

    public VaultConflictedCopyTests()
    {
        using var vault = Vault.Create(VaultPath, _password);
        vault.AddEntry(new VaultEntry { Title = "stripe", GroupPath = _work, Username = "user", Password = "stripe-0" });
        vault.AddEntry(new VaultEntry { Title = "github", GroupPath = _work, Username = "user", Password = "github-0" });
        vault.Save();
        File.Copy(VaultPath, CopyPath);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData("vault (conflicted copy 2026-11-09 120000).kdbx", true)]
    [InlineData("vault (Dan's conflicted copy 2026-11-09).kdbx", true)]
    [InlineData("vault.sync-conflict-20261109-120000-ABCDEFG.kdbx", true)]
    [InlineData("vault-other.kdbx", false)]
    [InlineData("other (conflicted copy).kdbx", false)]
    [InlineData("vault (copy).kdbx", false)]
    public void A_conflicted_copy_is_found_by_the_names_sync_tools_give(string name, bool found)
    {
        File.Delete(CopyPath);
        File.WriteAllBytes(Path.Combine(_directory, name), [1]);

        Assert.Equal(found, ConflictedCopies.Beside(VaultPath).Count == 1);
    }

    [Fact]
    public void Reviewing_says_what_would_come_in_and_writes_nothing()
    {
        Edit(CopyPath, "stripe", "phone");
        var before = File.ReadAllBytes(VaultPath);
        using var open = Vault.Open(VaultPath, _password);

        var review = open.MergeConflictedCopy(CopyPath, save: false);

        Assert.Equal(new EntryName(_work, "stripe"), Assert.Single(review.Report!.Taken));
        Assert.Equal(before, File.ReadAllBytes(VaultPath));
        Assert.True(File.Exists(CopyPath));
    }

    [Fact]
    public void Merging_keeps_both_devices_changes_and_moves_the_copy_in_with_the_backups()
    {
        Edit(CopyPath, "stripe", "phone");
        Edit(VaultPath, "github", "pc");
        using (var open = Vault.Open(VaultPath, _password))
        {
            Assert.Equal(VaultMergeSaveOutcome.Saved, open.MergeConflictedCopy(CopyPath, save: true).Outcome);
        }

        using var merged = Vault.Open(VaultPath, _password);
        Assert.Equal("phone", merged.Find(new EntryName(_work, "stripe"))!.Password);
        Assert.Equal("pc", merged.Find(new EntryName(_work, "github"))!.Password);
        Assert.False(File.Exists(CopyPath));
        Assert.True(File.Exists(Path.Combine(VaultBackups.DirectoryFor(VaultPath), Path.GetFileName(CopyPath))));
    }

    [Fact]
    public void An_entry_each_device_changed_in_a_different_field_keeps_both()
    {
        using (var phone = Vault.Open(CopyPath, _password))
        {
            Assert.True(phone.UpdateEntry(new VaultEntry { Title = "stripe", GroupPath = _work, Username = "phone-user", Password = "stripe-0" }));
            phone.Save();
        }

        Edit(VaultPath, "stripe", "pc");

        using (var open = Vault.Open(VaultPath, _password))
        {
            Assert.Equal(VaultMergeSaveOutcome.Saved, open.MergeConflictedCopy(CopyPath, save: true).Outcome);
        }

        using var merged = Vault.Open(VaultPath, _password);
        var entry = merged.Find(new EntryName(_work, "stripe"))!;
        Assert.Equal(("phone-user", "pc"), (entry.Username, entry.Password));
    }

    [Fact]
    public void A_copy_that_opens_with_other_credentials_is_refused_and_left_alone()
    {
        using (var phone = Vault.Open(CopyPath, _password))
        {
            Assert.Equal(VaultAccessOutcome.Changed, phone.ChangeAccess(new VaultAccessChange(true, AccessKeyfileChange.Keep), "another password", "another password").Outcome);
        }

        var before = File.ReadAllBytes(VaultPath);
        using var open = Vault.Open(VaultPath, _password);

        Assert.Equal(VaultMergeSaveOutcome.KeyRefused, open.MergeConflictedCopy(CopyPath, save: true).Outcome);
        Assert.Equal(before, File.ReadAllBytes(VaultPath));
        Assert.True(File.Exists(CopyPath));
    }

    [Fact]
    public void A_group_holding_two_items_of_one_name_can_be_moved_and_renamed()
    {
        using var vault = Vault.Open(VaultPath, _password);
        vault.AddEntry(new VaultEntry { Title = "stripe", GroupPath = _work, Password = "second" });
        vault.AddEntry(new VaultEntry { Title = "elsewhere", GroupPath = "dest", Password = "p" });

        Assert.Equal(GroupOutcome.Renamed, vault.RenameGroup(_work, "billing", out _));
        Assert.Equal(GroupOutcome.Moved, vault.MoveGroup("billing", "dest", out _));
    }

    private static void Edit(string path, string title, string password)
    {
        using var vault = Vault.Open(path, _password);
        Assert.True(vault.UpdateEntry(new VaultEntry { Title = title, GroupPath = _work, Username = "user", Password = password }));
        vault.Save();
    }
}
