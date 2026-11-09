using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>What the shell says of a real merge of another program's save.</summary>
public sealed class MergeNoticeTests
{
    private const string _agent = "claude-code";
    private const string _changed = """{"acaf":1,"mode":"ask"}""";

    [Fact]
    public void A_merge_changing_a_retired_agent_record_carries_it_and_says_nothing_of_settings()
    {
        using var fixture = new TempVault();
        var retired = new EntryName(ReservedGroups.Agents, _agent);
        using var open = Vault.Open(fixture.Path_, TempVault.Password);
        Assert.True(open.WriteRecord(retired, AcafIds.Agent, "acaf", """{"acaf":1,"mode":"automatic"}"""));
        open.Save();

        using (var other = Vault.Open(fixture.Path_, TempVault.Password))
        {
            Assert.True(other.WriteRecord(retired, AcafIds.Agent, "acaf", _changed));
            other.Save();
        }

        open.AddEntry(new VaultEntry { Title = "mine", Password = TempVault.ExamplePassword });
        var merged = open.MergeAnotherSave();
        using var copy = merged.Copy;

        Assert.Equal(VaultMergeSaveOutcome.Saved, merged.Outcome);
        Assert.Contains(_changed, copy!.ReadRecords(ReservedGroups.Agents, "acaf").Select(record => record.Value));

        var said = MergeNotice.Saved("billing.kdbx", new MergeResult(VaultMergeSaveOutcome.Saved, string.Empty, merged.Report, [], true, DateTimeOffset.UnixEpoch));

        Assert.DoesNotContain("settings", said, StringComparison.Ordinal);
        Assert.DoesNotContain(_agent, said, StringComparison.Ordinal);
    }
}
