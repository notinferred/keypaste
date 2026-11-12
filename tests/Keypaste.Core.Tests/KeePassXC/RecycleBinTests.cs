using System.Xml.Linq;
using Xunit;

namespace Keypaste.Core.Tests.KeePassXC;

/// <summary>What keypaste recycles, restores and purges is what KeePassXC reads, with the entry's identity and history intact.</summary>
public sealed class RecycleBinTests
{
    private const string _group = "env/compat-trash";

    [Fact]
    public void KeePassXC_reads_a_recycled_entry_in_its_bin_and_a_restored_one_back_where_it_was()
    {
        using var file = new KeePassXcVault();
        var rotated = new EntryName(_group, "ROTATED");
        using (var vault = file.Create())
        {
            vault.AddEntry(new VaultEntry { GroupPath = _group, Title = "ROTATED", Password = "v1-first" });
            foreach (var value in new[] { "v2-second", "v3-third", "v4-current" })
            {
                vault.UpdateEntry(rotated, new VaultEntry { GroupPath = _group, Title = "ROTATED", Password = value });
            }

            vault.AddEntry(new VaultEntry { GroupPath = _group, Title = "KEPT", Password = "kept-value" });
            vault.Save();
        }

        var uuid = KeePassXcVault.Entry(file.Current(), "ROTATED").Element("UUID")!.Value;

        RecycledEntryId id;
        using (var vault = file.Open())
        {
            Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(rotated, out id));
            vault.Save();
        }

        // Only KDBX 4.1 carries PreviousParentGroup, so a restore after a reopen still knows where the entry came from.
        file.AssertKdbx4();
        Assert.Equal(1, File.ReadAllBytes(file.Path_)[8]);
        var tree = KeePassXcCli.Run("ls", file.Path_, "-R", "-f").Split('\n');
        Assert.Contains("Recycle Bin/ROTATED", tree);
        Assert.DoesNotContain($"{_group}/ROTATED", tree);
        Assert.Contains($"{_group}/KEPT", tree);
        Assert.Equal(["v4-current"], KeePassXcCli.Show(file.Path_, "Recycle Bin/ROTATED", "Password"));

        var export = KeePassXcCli.Export(file.Path_);
        Assert.NotEmpty(export.Descendants("PreviousParentGroup"));
        // A tombstone would have a merge delete the entry from the other copy of the vault.
        Assert.Empty(export.Descendants("DeletedObjects").Elements());
        AssertHistoryHolds(file, "ROTATED", "v1-first", "v2-second", "v3-third");

        using (var vault = file.Open())
        {
            var listed = Assert.Single(vault.ReadRecycled(), row => row.Title == "ROTATED");
            Assert.Equal(_group, listed.OriginalGroupPath);
            Assert.Equal(RestoreOutcome.Restored, vault.RestoreRecycled(id));
            vault.Save();
        }

        Assert.Equal(["v4-current"], KeePassXcCli.Show(file.Path_, $"{_group}/ROTATED", "Password"));
        Assert.DoesNotContain("Recycle Bin/ROTATED", KeePassXcCli.Run("ls", file.Path_, "-R", "-f").Split('\n'));
        Assert.Equal(uuid, KeePassXcVault.Entry(file.Current(), "ROTATED").Element("UUID")!.Value);
        AssertHistoryHolds(file, "ROTATED", "v1-first", "v2-second", "v3-third");
    }

    [Fact]
    public void A_purge_removes_the_value_from_the_file_and_tombstones_it_for_KeePassXC()
    {
        using var file = new KeePassXcVault();
        var gone = new EntryName(_group, "GONE");
        using (var vault = file.Create())
        {
            vault.AddEntry(new VaultEntry { GroupPath = _group, Title = "GONE", Password = "doomed-value" });
            vault.Save();
            vault.RemoveEntry(gone, out var id);
            Assert.True(vault.PurgeRecycled(id));
            vault.Save();
        }

        var export = KeePassXcCli.Export(file.Path_);
        Assert.DoesNotContain(export.Descendants("Value"), value => value.Value == "doomed-value");
        Assert.NotEmpty(export.Descendants("DeletedObjects").Elements());
    }

    [Fact]
    public void An_entry_whose_group_KeePassXC_removed_comes_back_at_the_root()
    {
        using var file = new KeePassXcVault();
        RecycledEntryId id;
        using (var vault = file.Create())
        {
            vault.AddEntry(new VaultEntry { GroupPath = "env/compat-trash-gone", Title = "DOOMED", Password = "orphan-value" });
            vault.Save();
            vault.RemoveEntry(new EntryName("env/compat-trash-gone", "DOOMED"), out id);
            vault.Save();
        }

        KeePassXcCli.Run("rmdir", file.Path_, "env/compat-trash-gone");

        using (var vault = file.Open())
        {
            Assert.Equal(RestoreOutcome.RestoredToRoot, vault.RestoreRecycled(id));
            vault.Save();
        }

        Assert.Equal(["orphan-value"], KeePassXcCli.Show(file.Path_, "DOOMED", "Password"));
    }

    [Fact]
    public void Emptying_the_bin_leaves_KeePassXC_an_empty_bin_and_a_restore_of_nothing_changes_no_byte()
    {
        using var file = new KeePassXcVault();
        using (var vault = file.Create())
        {
            vault.AddEntry(new VaultEntry { GroupPath = _group, Title = "KEPT", Password = "kept-value" });
            vault.Save();
            vault.RemoveEntry(new EntryName(_group, "KEPT"), out _);
            vault.EmptyRecycleBin();
            vault.Save();
        }

        var tree = KeePassXcCli.Run("ls", file.Path_, "-R", "-f").Split('\n');
        Assert.Contains("Recycle Bin/", tree);
        Assert.Equal(["Recycle Bin/", "Recycle Bin/[empty]"], tree.Where(line => line.StartsWith("Recycle Bin/", StringComparison.Ordinal)));

        var before = File.ReadAllBytes(file.Path_);
        using (var vault = file.Open())
        {
            Assert.True(RecycledEntryId.TryParse("0123456789ABCDEF0123456789ABCDEF", out var nothing));
            Assert.NotEqual(RestoreOutcome.Restored, vault.RestoreRecycled(nothing));
        }

        Assert.Equal(before, File.ReadAllBytes(file.Path_));
    }

    [Fact]
    public void A_restore_onto_a_name_something_else_took_comes_back_beside_it()
    {
        using var file = new KeePassXcVault();
        var rotated = new EntryName(_group, "ROTATED");
        using (var vault = file.Create())
        {
            vault.AddEntry(new VaultEntry { GroupPath = _group, Title = "ROTATED", Password = "old-value" });
            vault.Save();
            vault.RemoveEntry(rotated, out var id);
            vault.AddEntry(new VaultEntry { GroupPath = _group, Title = "ROTATED", Password = "a-new-value" });
            Assert.NotEqual(RestoreOutcome.DestinationOccupied, vault.RestoreRecycled(id));
            vault.Save();
        }

        var tree = KeePassXcCli.Run("ls", file.Path_, "-R", "-f").Split('\n');
        Assert.Equal(2, tree.Count(line => line == $"{_group}/ROTATED"));
        Assert.DoesNotContain("Recycle Bin/ROTATED", tree);
    }

    private static void AssertHistoryHolds(KeePassXcVault file, string title, params string[] values)
    {
        var entry = KeePassXcCli.Export(file.Path_).Descendants("Entry").First(e => KeePassXcVault.StringValue(e, "Title") == title);
        var history = entry.Element("History")!.Descendants("Value").Select(v => v.Value).ToHashSet();
        Assert.Superset(values.ToHashSet(), history);
    }
}
