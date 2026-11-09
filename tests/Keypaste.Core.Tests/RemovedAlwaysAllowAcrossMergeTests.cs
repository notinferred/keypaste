using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>An Always allow removed here stays removed when another copy of the vault, carrying the agent's old record, comes back in.</summary>
public sealed class RemovedAlwaysAllowAcrossMergeTests : IDisposable
{
    private const string _agent = "claude-code";
    private const string _password = "SENTINEL-REMOVED-ALWAYS-ALLOW";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-removed-always-").FullName;
    private readonly ManualClock _clock = new();
    private readonly FakeChannel _channel = new();
    private readonly ApprovalGate _gate;
    private readonly Vault _vault;
    private readonly OwnerLog _log;
    private readonly SessionLifetime _lifetime = new("session-one");
    private readonly AgentRulesFile _file;

    public RemovedAlwaysAllowAcrossMergeTests()
    {
        _vault = Vault.Create(VaultPath, EnvStoreTests.MasterPassword);
        _vault.AddEntry(new VaultEntry { GroupPath = "work", Title = "github", Password = _password });
        _vault.Save();
        _log = new OwnerLog(_directory);
        _gate = new ApprovalGate(_channel, _clock, ApprovalLimits.Default);
        _file = new AgentRulesFile(KeypasteHome.AgentsPath(_directory));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    private string SnapshotPath => Path.Combine(_directory, "other-device.kdbx");

    public void Dispose()
    {
        _gate.Dispose();
        _lifetime.Dispose();
        _vault.Dispose();
        _log.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task A_removed_always_allow_is_asked_after_a_merge_of_a_copy_that_still_holds_the_row()
    {
        var answer = await RemoveAnAlwaysAllowAfterSnapshotAsync();
        DeliverTheSnapshotHoldingTheRow(answer.Item);

        var save = _vault.MergeAnotherSave();
        Assert.Equal(VaultMergeSaveOutcome.Saved, save.Outcome);
        using var merged = save.Copy!;
        Assert.Contains(merged.ReadRecords(ReservedGroups.Agents, "acaf"), record => record.Value?.Contains(answer.Item, StringComparison.Ordinal) == true);

        var rules = Rules(merged);
        Assert.True(rules.TryRead(out var held, out var problem), problem);
        Assert.Null(rules.Ignored);
        Assert.Empty(held.Answers);

        var reply = await RequestAsync(merged, rules);

        Assert.Equal((AuditDecision.Denied, AuditMethod.Prompt, null), (reply.Decision, reply.Method, reply.Value));
        Assert.Equal(2, _channel.Asked);
    }

    [Fact]
    public async Task A_removed_always_allow_is_asked_after_the_older_file_replaces_the_vault()
    {
        var answer = await RemoveAnAlwaysAllowAfterSnapshotAsync();
        DeliverTheSnapshotHoldingTheRow(answer.Item);

        using var reopened = Vault.Open(VaultPath, EnvStoreTests.MasterPassword);
        var rules = Rules(reopened);
        var reply = await RequestAsync(reopened, rules);

        Assert.Equal((AuditDecision.Denied, AuditMethod.Prompt, null), (reply.Decision, reply.Method, reply.Value));
        Assert.Equal(2, _channel.Asked);
    }

    private async Task<RememberedAnswer> RemoveAnAlwaysAllowAfterSnapshotAsync()
    {
        var rules = Rules(_vault);
        var answer = new RememberedAnswer(_agent, _vault.EntryUuid(new EntryName("work", "github"))!, "password", CategoryRule.Allow);
        Assert.True(rules.TryChange(held => held.Remembering(answer, confirmed: true), out var problem), problem);

        var released = await RequestAsync(_vault, rules);
        Assert.Equal(_password, released.Value);
        Assert.Equal(0, _channel.Asked);

        File.Copy(VaultPath, SnapshotPath);
        Assert.True(rules.TryChange(held => held.Forgetting(answer), out problem), problem);

        var removed = await RequestAsync(_vault, rules);
        Assert.Null(removed.Value);
        Assert.Equal(1, _channel.Asked);
        return answer;
    }

    private void DeliverTheSnapshotHoldingTheRow(string item)
    {
        using (var theirs = Vault.Open(SnapshotPath, EnvStoreTests.MasterPassword))
        {
            var record = "{\"acaf\":1,\"see\":[\"**\"],\"values\":true,\"mode\":\"automatic\",\"always\":{\"items\":[\"" + item + "\"]}}";
            Assert.True(theirs.WriteRecord(new EntryName(ReservedGroups.Agents, _agent), AcafIds.Agent, "acaf", record));
            theirs.Save();
        }

        File.Copy(SnapshotPath, VaultPath, overwrite: true);
        _clock.Advance(TimeSpan.FromHours(1));
    }

    private SealedAgentRules Rules(Vault vault) => new(_file, () => vault, edit =>
    {
        edit(vault);
        vault.Save();
        return AgentSave.Saved;
    });

    private async ValueTask<CredentialReply> RequestAsync(Vault vault, SealedAgentRules rules)
    {
        var handler = new ApproverHandler(new VaultCredentialSource(() => vault, _clock), new VaultEntryNameLister(() => vault), _gate, rules);
        var authority = new SessionAuthority(VaultIdentity.Of(_directory, VaultPath), () => _lifetime, handler, () => _log.Log, clock: _clock);
        var connection = $"conn-{Guid.NewGuid():N}";
        var attached = await authority.AttachAsync(new AttachRequest(VaultPath) { Client = new AttachClient("Claude Code", "2.1.0", _agent) }, connection, Token);
        Assert.True(attached.Attached, attached.Reason);

        return await authority.RequestAsync(
            new CredentialRequest
            {
                Entry = "work/github",
                Field = "password",
                Reason = "push the release",
                Exposure = ["**"],
                ClientName = "Claude Code",
                ClientLabel = _agent,
                Vault = VaultPath,
                Session = "session-one",
            },
            connection,
            Token);
    }
}
