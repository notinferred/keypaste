using System.Xml.Linq;
using Xunit;

namespace Keypaste.Core.Tests.KeePassXC;

/// <summary>What an agent's approved changes write, KeePassXC reads and keeps through its own save: expiry, the tag and the mark of an agent's item (T-57).</summary>
public sealed class AgentChangesTests
{
    private const string _group = "env/compat-agent";

    [Fact]
    public void KeePassXC_reads_the_expiry_tag_and_mark_and_keeps_them_through_its_own_save()
    {
        using var file = new KeePassXcVault();
        var item = new EntryName(_group, "SCRATCH");
        var expires = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
        using (var vault = file.Create())
        {
            vault.AddEntry(new VaultEntry { GroupPath = _group, Title = "SCRATCH", Password = "scratch-value" });
            vault.MarkAgentCreated(item, "claude-code");
            Assert.True(vault.SetExpiry(item, expires));
            Assert.True(vault.AddTag(item, "needs-update"));
            vault.Save();
        }

        var entry = KeePassXcVault.Entry(file.Current(), "SCRATCH");
        Assert.Equal("True", entry.Element("Times")!.Element("Expires")!.Value);
        // KDBX 4 writes a time as base64 of its seconds since 0001-01-01.
        var seconds = BitConverter.ToInt64(Convert.FromBase64String(entry.Element("Times")!.Element("ExpiryTime")!.Value));
        Assert.Equal(expires.UtcDateTime, DateTime.MinValue.AddSeconds(seconds));
        Assert.Contains("needs-update", entry.Element("Tags")!.Value, StringComparison.Ordinal);
        var mark = Assert.Single(entry.Element("CustomData")!.Elements("Item"), data => data.Element("Key")!.Value == "acaf.agent-created");
        Assert.EndsWith(" claude-code", mark.Element("Value")!.Value, StringComparison.Ordinal);

        KeePassXcCli.Run("edit", file.Path_, "-u", "edited-in-keepassxc", $"{_group}/SCRATCH");

        using (var vault = file.Open())
        {
            Assert.Equal("claude-code", vault.AgentThatCreated(item));
            Assert.Equal(expires, vault.ReadTimes(item)!.Expires);
            Assert.Contains("needs-update", vault.Tags(item)!);
            Assert.True(vault.DeleteForGood(item));
            vault.Save();
        }

        Assert.DoesNotContain($"{_group}/SCRATCH", KeePassXcCli.Run("ls", file.Path_, "-R", "-f").Split('\n'));
        Assert.DoesNotContain("Recycle Bin/SCRATCH", KeePassXcCli.Run("ls", file.Path_, "-R", "-f").Split('\n'));
        Assert.NotEmpty(KeePassXcCli.Export(file.Path_).Descendants("DeletedObjects").Elements());
    }
}
