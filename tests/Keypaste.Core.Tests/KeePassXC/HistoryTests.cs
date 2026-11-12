using Xunit;

namespace Keypaste.Core.Tests.KeePassXC;

/// <summary>A revision keypaste restores is what KeePassXC reads as current, with every other revision kept behind it.</summary>
public sealed class HistoryTests
{
    private const string _path = "env/compat-history/ROTATED";

    private static readonly EntryName _name = EntryName.Parse(_path);

    [Fact]
    public void KeePassXC_reads_the_restored_revision_as_current_and_every_value_in_its_history()
    {
        using var file = new KeePassXcVault();
        using (var vault = file.Create())
        {
            vault.AddEntry(new VaultEntry { GroupPath = _name.GroupPath, Title = _name.Title, Password = "v1-first" });
            vault.Save();
            foreach (var value in new[] { "v2-second", "v3-third", "v4-current" })
            {
                vault.UpdateEntry(_name, new VaultEntry { GroupPath = _name.GroupPath, Title = _name.Title, Password = value });
                vault.Save();
            }
        }

        var uuid = KeePassXcVault.Entry(file.Current(), _name.Title).Element("UUID")!.Value;

        // Newest first, so 1 is v2-second: neither end, which a restore that took the wrong one would still look like.
        using (var vault = file.Open())
        {
            Assert.True(vault.RestoreRevision(_name, 1));
            vault.Save();
        }

        file.AssertKdbx4();
        Assert.Equal(["v2-second"], KeePassXcCli.Show(file.Path_, _path, "Password"));

        var entry = KeePassXcCli.Export(file.Path_).Descendants("Entry").First(e => KeePassXcVault.StringValue(e, "Title") == _name.Title);
        var history = entry.Element("History")!.Descendants("Value").Select(v => v.Value).ToHashSet();
        Assert.Superset(new HashSet<string> { "v1-first", "v3-third", "v4-current" }, history);
        Assert.DoesNotContain("v0-never-written", history);
        Assert.Equal(uuid, entry.Element("UUID")!.Value);
    }

    [Fact]
    public void A_revision_the_entry_does_not_have_is_refused_without_changing_the_file()
    {
        using var file = new KeePassXcVault();
        using (var vault = file.Create())
        {
            vault.AddEntry(new VaultEntry { GroupPath = _name.GroupPath, Title = _name.Title, Password = "v1-first" });
            vault.Save();
        }

        var before = File.ReadAllBytes(file.Path_);
        using (var vault = file.Open())
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => vault.RestoreRevision(_name, 99));
        }

        Assert.Equal(before, File.ReadAllBytes(file.Path_));
        Assert.Equal(["v1-first"], KeePassXcCli.Show(file.Path_, _path, "Password"));
    }
}
