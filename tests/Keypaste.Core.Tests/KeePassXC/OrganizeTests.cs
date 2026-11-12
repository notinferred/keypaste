using System.Xml.Linq;
using Xunit;

namespace Keypaste.Core.Tests.KeePassXC;

/// <summary>What keypaste renames and moves KeePassXC reads at its new path, as the same entry, in a file still KDBX 4.0.</summary>
/// <remarks>What keypaste refuses to rename or move is <c>VaultOrganizeTests</c>'.</remarks>
public sealed class OrganizeTests
{
    [Fact]
    public void KeePassXC_reads_a_renamed_group_and_a_renamed_moved_and_relocated_entry_as_the_same_entry()
    {
        using var file = new KeePassXcVault();
        var token = new EntryName("env/compat-organize", "TOKEN");
        using (var vault = file.Create())
        {
            vault.AddEntry(new VaultEntry { GroupPath = token.GroupPath, Title = token.Title, Password = "v1-first" });
            foreach (var value in new[] { "v2-second", "v3-third", "v4-current" })
            {
                vault.UpdateEntry(token, new VaultEntry { GroupPath = token.GroupPath, Title = token.Title, Password = value });
            }

            vault.AddEntry(new VaultEntry { GroupPath = "env/compat-organize", Title = "KEPT", Password = "kept-value" });
            vault.AddEntry(new VaultEntry { GroupPath = "env/compat-shipping", Title = "OTHER", Password = "other-value" });
            vault.Save();
        }

        var uuid = KeePassXcVault.Entry(file.Current(), "TOKEN").Element("UUID")!.Value;

        using (var vault = file.Open())
        {
            Assert.Equal(GroupOutcome.Renamed, vault.RenameGroup("env/compat-organize", "invoicing", out _));
            vault.Save();
        }

        AssertStill40(file);
        var tree = Tree(file);
        Assert.Contains("env/invoicing/TOKEN", tree);
        Assert.Contains("env/invoicing/KEPT", tree);
        Assert.Contains("env/compat-shipping/OTHER", tree);
        Assert.DoesNotContain(tree, line => line.StartsWith("env/compat-organize/", StringComparison.Ordinal));
        Assert.Equal(["v4-current"], KeePassXcCli.Show(file.Path_, "env/invoicing/TOKEN", "Password"));

        using (var vault = file.Open())
        {
            Assert.Equal(GroupOutcome.Created, vault.CreateGroup("", "archive", out _));
            Assert.Equal(OrganizeOutcome.Renamed, vault.RenameEntry(new EntryName("env/invoicing", "TOKEN"), "API_TOKEN", out _));
            Assert.Equal(OrganizeOutcome.Moved, vault.MoveEntry(new EntryName("env/invoicing", "API_TOKEN"), "archive", out _));
            vault.Save();
        }

        AssertStill40(file);
        Assert.Contains("archive/API_TOKEN", Tree(file));
        Assert.Equal(["v4-current"], KeePassXcCli.Show(file.Path_, "archive/API_TOKEN", "Password"));
        AssertSameEntryWithItsHistory(file, "API_TOKEN", uuid);

        // One write that changes both halves, as the desktop performs it.
        using (var vault = file.Open())
        {
            Assert.Equal(
                OrganizeOutcome.RenamedAndMoved,
                vault.Relocate(new EntryName("archive", "API_TOKEN"), new EntryName("env/invoicing", "FINAL_TOKEN"), out _));
            vault.Save();
        }

        AssertStill40(file);
        Assert.DoesNotContain("archive/API_TOKEN", Tree(file));
        Assert.Equal(["v4-current"], KeePassXcCli.Show(file.Path_, "env/invoicing/FINAL_TOKEN", "Password"));
        AssertSameEntryWithItsHistory(file, "FINAL_TOKEN", uuid);
    }

    [Fact]
    public void KeePassXC_reads_a_moved_group_and_keypaste_renames_a_group_KeePassXC_made()
    {
        using var file = new KeePassXcVault();
        using (var vault = file.Create())
        {
            vault.AddEntry(new VaultEntry { GroupPath = "drawer", Title = "LETTER", Password = "letter-value" });
            vault.Save();
        }

        KeePassXcCli.Run("mkdir", file.Path_, "toolmade");

        using (var vault = file.Open())
        {
            vault.CreateGroup("", "shelf", out _);
            Assert.Equal(GroupOutcome.Moved, vault.MoveGroup("drawer", "shelf", out _));
            Assert.Equal(GroupOutcome.Renamed, vault.RenameGroup("toolmade", "ours", out _));
            vault.Save();
        }

        AssertStill40(file);
        var tree = Tree(file);
        Assert.Contains("shelf/drawer/LETTER", tree);
        Assert.Contains("ours/", tree);
        Assert.DoesNotContain(tree, line => line.StartsWith("drawer/", StringComparison.Ordinal));
        Assert.Equal(["letter-value"], KeePassXcCli.Show(file.Path_, "shelf/drawer/LETTER", "Password"));
    }

    [Fact]
    public void KeePassXC_finds_a_group_keypaste_deleted_in_its_recycle_bin_and_an_item_of_it_restored_where_it_was()
    {
        using var file = new KeePassXcVault();
        using (var vault = file.Create())
        {
            vault.AddEntry(new VaultEntry { GroupPath = "shelf/drawer", Title = "LETTER", Password = "letter-value" });
            vault.Save();
            Assert.Equal(GroupOutcome.Recycled, vault.DeleteGroup("shelf"));
            vault.Save();
        }

        Assert.Contains("Recycle Bin/shelf/drawer/LETTER", Tree(file));
        var shelf = KeePassXcCli.Export(file.Path_).Descendants("Group").Single(group => (string?)group.Element("Name") == "shelf");
        Assert.NotNull(shelf.Element("PreviousParentGroup"));

        using (var vault = file.Open())
        {
            var letter = Assert.Single(vault.ReadRecycled(), row => row.Title == "LETTER");
            Assert.Equal("shelf/drawer", letter.OriginalGroupPath);
            Assert.Equal(RestoreOutcome.Restored, vault.RestoreRecycled(letter.Id));
            vault.Save();
        }

        Assert.Equal(["letter-value"], KeePassXcCli.Show(file.Path_, "shelf/drawer/LETTER", "Password"));
    }

    private static string[] Tree(KeePassXcVault file) => KeePassXcCli.Run("ls", file.Path_, "-R", "-f").Split('\n');

    // Only a deletion may raise the file to 4.1, whose PreviousParentGroup every reader below KeePassXC 2.7 refuses.
    private static void AssertStill40(KeePassXcVault file)
    {
        file.AssertKdbx4();
        Assert.Equal(0, File.ReadAllBytes(file.Path_)[8]);
        var export = KeePassXcCli.Export(file.Path_);
        Assert.Empty(export.Descendants("PreviousParentGroup"));
        // A tombstone would have a merge delete the entry from the other copy of the vault.
        Assert.Empty(export.Descendants("DeletedObjects").Elements());
    }

    private static void AssertSameEntryWithItsHistory(KeePassXcVault file, string title, string uuid)
    {
        var entry = KeePassXcCli.Export(file.Path_).Descendants("Entry").First(e => KeePassXcVault.StringValue(e, "Title") == title);
        Assert.Equal(uuid, entry.Element("UUID")!.Value);
        Assert.Superset(
            new HashSet<string> { "v1-first", "v2-second", "v3-third" },
            entry.Element("History")!.Descendants("Value").Select(v => v.Value).ToHashSet());
    }
}
