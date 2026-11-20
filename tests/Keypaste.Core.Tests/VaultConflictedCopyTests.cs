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
        using var vault = ClaimedVault.Create(VaultPath, _password);
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
    [InlineData("vault 2.kdbx", false)]
    [InlineData("other (conflicted copy).kdbx", false)]
    [InlineData("vault (copy).kdbx", false)]
    public void A_conflicted_copy_is_found_by_the_names_sync_tools_give(string name, bool found)
    {
        File.Delete(CopyPath);
        File.WriteAllBytes(Path.Combine(_directory, name), [1]);

        Assert.Equal(found, Found().Count == 1);
    }

    [Theory]
    [InlineData("vault 2.kdbx", true)]
    [InlineData("vault 3.kdbx", true)]
    [InlineData("vault-JOHNS-SURFACE.kdbx", true)]
    [InlineData("vault 1.kdbx", false)]
    [InlineData("vault 02.kdbx", false)]
    [InlineData("vault2.kdbx", false)]
    [InlineData("vault two.kdbx", false)]
    public void A_copy_named_as_iCloud_Drive_or_OneDrive_names_one_is_found_when_it_holds_another_devices_changes(string name, bool found)
    {
        File.Move(CopyPath, Path.Combine(_directory, name));
        Edit(Path.Combine(_directory, name), "stripe", "phone");

        Assert.Equal(found, Found().Count == 1);
    }

    [Fact]
    public void A_second_vault_named_as_iCloud_Drive_names_a_copy_is_not_one()
    {
        File.Delete(CopyPath);
        using (var second = ClaimedVault.Create(Path.Combine(_directory, "vault 2.kdbx"), _password))
        {
            second.AddEntry(new VaultEntry { Title = "stripe", GroupPath = _work, Username = "user", Password = "second" });
            second.Save();
        }

        Assert.Empty(Found());
    }

    [Fact]
    public void An_old_copy_named_as_OneDrive_names_one_is_not_one_when_it_holds_nothing_the_vault_lacks()
    {
        File.Move(CopyPath, Path.Combine(_directory, "vault-old.kdbx"));
        Edit(VaultPath, "github", "pc");

        Assert.Empty(Found());
    }

    [Fact]
    public void A_lock_forgets_what_was_found()
    {
        var copies = new ConflictedCopies();
        using (var open = ClaimedVault.Open(VaultPath, _password))
        {
            copies.Look(open);
        }

        Assert.Equal(CopyPath, Assert.Single(copies.Found));
        copies.Clear();

        Assert.Empty(copies.Found);
    }

    [Fact]
    public void Reviewing_says_what_would_come_in_and_writes_nothing()
    {
        Edit(CopyPath, "stripe", "phone");
        var before = File.ReadAllBytes(VaultPath);
        using var open = ClaimedVault.Open(VaultPath, _password);

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
        using (var open = ClaimedVault.Open(VaultPath, _password))
        {
            Assert.Equal(VaultMergeSaveOutcome.Saved, open.MergeConflictedCopy(CopyPath, save: true).Outcome);
        }

        using var merged = ClaimedVault.Open(VaultPath, _password);
        Assert.Equal("phone", merged.Find(new EntryName(_work, "stripe"))!.Password);
        Assert.Equal("pc", merged.Find(new EntryName(_work, "github"))!.Password);
        Assert.False(File.Exists(CopyPath));
        Assert.True(File.Exists(Path.Combine(VaultBackups.DirectoryFor(VaultPath), Path.GetFileName(CopyPath))));
    }

    [Fact]
    public void An_entry_each_device_changed_in_a_different_field_keeps_both()
    {
        using (var phone = ClaimedVault.Open(CopyPath, _password))
        {
            Assert.True(phone.UpdateEntry(new VaultEntry { Title = "stripe", GroupPath = _work, Username = "phone-user", Password = "stripe-0" }));
            phone.Save();
        }

        Edit(VaultPath, "stripe", "pc");

        using (var open = ClaimedVault.Open(VaultPath, _password))
        {
            Assert.Equal(VaultMergeSaveOutcome.Saved, open.MergeConflictedCopy(CopyPath, save: true).Outcome);
        }

        using var merged = ClaimedVault.Open(VaultPath, _password);
        var entry = merged.Find(new EntryName(_work, "stripe"))!;
        Assert.Equal(("phone-user", "pc"), (entry.Username, entry.Password));
    }

    [Fact]
    public void A_copy_that_opens_with_other_credentials_is_refused_and_left_alone()
    {
        using (var phone = ClaimedVault.Open(CopyPath, _password))
        {
            Assert.Equal(VaultAccessOutcome.Changed, phone.ChangeAccess(new VaultAccessChange(true, AccessKeyfileChange.Keep), "another password", "another password").Outcome);
        }

        var before = File.ReadAllBytes(VaultPath);
        using var open = ClaimedVault.Open(VaultPath, _password);

        Assert.Equal(VaultMergeSaveOutcome.KeyRefused, open.MergeConflictedCopy(CopyPath, save: true).Outcome);
        Assert.Equal(before, File.ReadAllBytes(VaultPath));
        Assert.True(File.Exists(CopyPath));
    }

    [Fact]
    public void A_group_holding_two_items_of_one_name_can_be_moved_and_renamed()
    {
        using var vault = ClaimedVault.Open(VaultPath, _password);
        vault.AddEntry(new VaultEntry { Title = "stripe", GroupPath = _work, Password = "second" });
        vault.AddEntry(new VaultEntry { Title = "elsewhere", GroupPath = "dest", Password = "p" });

        Assert.Equal(GroupOutcome.Renamed, vault.RenameGroup(_work, "billing", out _));
        Assert.Equal(GroupOutcome.Moved, vault.MoveGroup("billing", "dest", out _));
    }

    private IReadOnlyList<string> Found()
    {
        var copies = new ConflictedCopies();
        using var open = ClaimedVault.Open(VaultPath, _password);
        copies.Look(open);
        return copies.Found;
    }

    private static void Edit(string path, string title, string password)
    {
        using var vault = ClaimedVault.Open(path, _password);
        Assert.True(vault.UpdateEntry(new VaultEntry { Title = title, GroupPath = _work, Username = "user", Password = password }));
        vault.Save();
    }
}
