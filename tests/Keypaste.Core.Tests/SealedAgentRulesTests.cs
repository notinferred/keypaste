using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// <c>agents.toml</c> on the request path: the owner follows it only while the seal its vault's key wrote for this home
/// holds; anything else leaves every value asked and every run off, and the file as it is.
/// </summary>
public sealed class SealedAgentRulesTests : IDisposable
{
    private const string _agent = "claude-code";
    private const string _password = "SENTINEL-SEALED-PASSWORD";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-sealed-rules-").FullName;
    private readonly ManualClock _clock = new();
    private readonly FakeChannel _channel = new();
    private readonly ApprovalGate _gate;
    private readonly Vault _vault;
    private readonly OwnerLog _log;
    private readonly SessionLifetime _lifetime = new("session-one");
    private readonly AgentRulesFile _file;
    private readonly SealedAgentRules _rules;
    private readonly SessionAuthority _authority;

    public SealedAgentRulesTests()
    {
        _vault = Vault.Create(VaultPath, EnvStoreTests.MasterPassword);
        _vault.AddEntry(new VaultEntry { GroupPath = "work", Title = "github", Password = _password });
        _vault.Save();
        _log = new OwnerLog(_directory);
        _gate = new ApprovalGate(_channel, _clock, ApprovalLimits.Default);
        _file = new AgentRulesFile(KeypasteHome.AgentsPath(_directory));
        _rules = Sealer(_file, _vault);

        var handler = new ApproverHandler(new VaultCredentialSource(() => _lifetime.IsLive ? _vault : null, _clock), new VaultEntryNameLister(() => _vault), _gate, _rules);
        _authority = new SessionAuthority(VaultIdentity.Of(_directory, VaultPath), () => _lifetime, handler, () => _log.Log, clock: _clock);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    public void Dispose()
    {
        _gate.Dispose();
        _lifetime.Dispose();
        _vault.Dispose();
        _log.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task A_sealed_Allow_releases_unasked()
    {
        Assert.True(_rules.TryChange(rules => rules.Allowing(FieldCategory.Password, confirmed: true), out var problem), problem);

        var reply = await RequestAsync();

        Assert.Equal((AuditMethod.Allowed, _password), (reply.Method, reply.Value));
        Assert.Null(_rules.Ignored);
        Assert.Equal(0, _channel.Asked);
    }

    /// <summary>An Allow written into the file by hand, with or without the old seal, is ignored and asked, and the file is left as it is.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_hand_edited_Allow_is_ignored_and_asked(bool keepsTheSeal)
    {
        Assert.True(_rules.TryChange(rules => rules.WithRun(_agent, run: true), out var problem), problem);
        Assert.True(_file.TryReadFile(out var sealedRead, out problem), problem);
        Assert.True(_file.TryWrite(sealedRead.Rules.Allowing(FieldCategory.Password, confirmed: true), keepsTheSeal ? sealedRead.Seal : null, out problem), problem);
        var edited = File.ReadAllText(_file.Path);

        var reply = await RequestAsync();

        Assert.Equal((AuditDecision.Denied, AuditMethod.Prompt, null), (reply.Decision, reply.Method, reply.Value));
        Assert.Equal(1, _channel.Asked);
        Assert.Equal(SealedAgentRules.IgnoredText, _rules.Ignored);
        Assert.True(_rules.TryRead(out var held, out _));
        Assert.False(held.Runs(_agent));
        Assert.Equal(edited, File.ReadAllText(_file.Path));
    }

    /// <summary>A file this owner sealed, put back after a later sealed change, is ignored: the seal covers the vault's count of changes.</summary>
    [Fact]
    public async Task An_older_sealed_file_put_back_is_ignored_and_asked()
    {
        Assert.True(_rules.TryChange(rules => rules.Allowing(FieldCategory.Password, confirmed: true), out var problem), problem);
        var older = File.ReadAllText(_file.Path);
        Assert.True(_rules.TryChange(rules => rules.With(FieldCategory.Password, CategoryRule.Ask), out problem), problem);
        File.WriteAllText(_file.Path, older);

        var reply = await RequestAsync();

        Assert.Equal((AuditDecision.Denied, AuditMethod.Prompt, null), (reply.Decision, reply.Method, reply.Value));
        Assert.Equal(1, _channel.Asked);
        Assert.Equal(SealedAgentRules.IgnoredText, _rules.Ignored);
    }

    /// <summary>Two homes sharing one vault each keep their own rules across the other's sealed changes.</summary>
    [Fact]
    public void Two_homes_sharing_a_vault_each_keep_their_rules_across_the_others_change()
    {
        var other = Sealer(new AgentRulesFile(KeypasteHome.AgentsPath(Path.Combine(_directory, "other-home"))), _vault);
        Assert.True(_rules.TryChange(rules => rules.Allowing(FieldCategory.Password, confirmed: true), out var problem), problem);
        Assert.True(other.TryChange(rules => rules.Allowing(FieldCategory.Notes, confirmed: true), out problem), problem);
        Assert.True(other.TryChange(rules => rules.With(FieldCategory.Notes, CategoryRule.Block), out problem), problem);

        Assert.True(_rules.TryRead(out var here, out _));
        Assert.True(other.TryRead(out var there, out _));
        Assert.Equal((CategoryRule.Allow, CategoryRule.Ask), (here.RuleFor(FieldCategory.Password), here.RuleFor(FieldCategory.Notes)));
        Assert.Equal((CategoryRule.Ask, CategoryRule.Block), (there.RuleFor(FieldCategory.Password), there.RuleFor(FieldCategory.Notes)));
        Assert.Null(_rules.Ignored);
        Assert.Null(other.Ignored);
    }

    /// <summary>
    /// Two copies of the vault that each sealed a change for their own home merge to a vault both seals hold under, since each
    /// home's count is a record of its own and a merge settles a record changed in both copies as one whole.
    /// </summary>
    [Fact]
    public void A_merge_of_two_copies_each_sealing_its_own_home_keeps_both_counts()
    {
        Assert.True(_rules.TryChange(rules => rules.WithRun(_agent, run: true), out var problem), problem);
        var otherFile = new AgentRulesFile(KeypasteHome.AgentsPath(Path.Combine(_directory, "other-home")));
        using var theirs = Vault.Open(VaultPath, EnvStoreTests.MasterPassword);
        Assert.True(Sealer(otherFile, theirs).TryChange(rules => rules.Allowing(FieldCategory.Notes, confirmed: true), out problem), problem);

        Vault? merged = null;
        var mine = new SealedAgentRules(_file, () => _vault, edit =>
        {
            edit(_vault);
            var save = _vault.MergeAnotherSave();
            merged = save.Copy;
            return save.Outcome == VaultMergeSaveOutcome.Saved ? AgentSave.Saved : new AgentSave(AgentSaveOutcome.Failed, save.Reason);
        });
        Assert.True(mine.TryChange(rules => rules.Allowing(FieldCategory.Password, confirmed: true), out problem), problem);

        using (merged)
        {
            Assert.NotNull(merged);
            var here = new SealedAgentRules(_file, () => merged, _ => AgentSave.Saved);
            var there = new SealedAgentRules(otherFile, () => merged, _ => AgentSave.Saved);
            Assert.True(here.TryRead(out var heldHere, out _));
            Assert.True(there.TryRead(out var heldThere, out _));
            Assert.Equal((null, null), (here.Ignored, there.Ignored));
            Assert.Equal(CategoryRule.Allow, heldHere.RuleFor(FieldCategory.Password));
            Assert.Equal(CategoryRule.Allow, heldThere.RuleFor(FieldCategory.Notes));
        }
    }

    /// <summary>
    /// A file sealed in another home, its identity and the vault's key with it, is not this home's: this home's identity is
    /// its own, so the seal does not hold here.
    /// </summary>
    [Fact]
    public async Task A_file_copied_from_another_home_is_ignored()
    {
        var other = Path.Combine(_directory, "other-home");
        var otherFile = new AgentRulesFile(KeypasteHome.AgentsPath(other));
        Assert.True(Sealer(otherFile, _vault).TryChange(rules => rules.Allowing(FieldCategory.Password, confirmed: true), out var problem), problem);
        Assert.True(_file.TryEnsureHomeId(out _, out problem), problem);
        File.Copy(otherFile.Path, _file.Path);

        var reply = await RequestAsync();

        Assert.Equal(AuditMethod.Prompt, reply.Method);
        Assert.Null(reply.Value);
        Assert.Equal(SealedAgentRules.IgnoredText, _rules.Ignored);
    }

    /// <summary>A sealed file read against a vault that does not hold the key, as another vault or one that lost it, is ignored.</summary>
    [Fact]
    public async Task A_vault_without_the_key_ignores_the_file()
    {
        using var another = Vault.Create(Path.Combine(_directory, "another.kdbx"), EnvStoreTests.MasterPassword);
        Assert.True(Sealer(_file, another).TryChange(rules => rules.Allowing(FieldCategory.Password, confirmed: true), out var problem), problem);

        var reply = await RequestAsync();

        Assert.Equal(AuditMethod.Prompt, reply.Method);
        Assert.Null(reply.Value);
        Assert.Equal(SealedAgentRules.IgnoredText, _rules.Ignored);
    }

    /// <summary>The key is made in keypaste's own group the first time the owner seals, and kept there.</summary>
    [Fact]
    public void The_key_is_made_once_in_a_reserved_group()
    {
        Assert.Empty(_vault.ReadRecords(ReservedGroups.Keys, "key"));
        Assert.True(_rules.TryChange(rules => rules.WithRun(_agent, run: true), out var problem), problem);
        var key = Keys().Single();
        Assert.True(_rules.TryChange(rules => rules.WithRun(_agent, run: false), out problem), problem);

        Assert.Equal(key, Keys().Single());

        IEnumerable<string?> Keys() => _vault.ReadRecords(ReservedGroups.Keys, "key").Where(record => record.Value is { Length: > 0 }).Select(record => record.Value);
    }

    [Fact]
    public async Task A_lock_releases_nothing_and_changes_nothing()
    {
        Assert.True(_rules.TryChange(rules => rules.Allowing(FieldCategory.Password, confirmed: true), out var problem), problem);
        var connection = await AttachAsync();
        _lifetime.End();

        var reply = await _authority.RequestAsync(Request(), connection, Token);
        var change = _authority.ChangeRules(_agent, rules => rules.WithRun(_agent, run: true), "keypaste app", out _);

        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Null(reply.Value);
        Assert.False(change.Changed);
        Assert.DoesNotContain(_password, _log.Text(), StringComparison.Ordinal);
    }

    /// <summary>A change over the pipe is made and sealed by the owner and recorded; it holds on the next request.</summary>
    [Fact]
    public async Task A_change_from_keypaste_clients_is_sealed_by_the_owner_and_recorded()
    {
        var connection = await AttachAsync();

        var reply = await _authority.ChangeRulesAsync(new RulesChangeRequest(_agent, RulesChangeKind.RunOn) { Vault = VaultPath, Session = "session-one" }, connection, Token);

        Assert.True(reply.Changed, reply.Reason);
        Assert.True(reply.Recorded);
        Assert.True(_rules.TryRead(out var rules, out _));
        Assert.True(rules.Runs(_agent));
        Assert.Contains("\"method\":\"rules-changed\"", _log.Text(), StringComparison.Ordinal);
    }

    private static SealedAgentRules Sealer(AgentRulesFile file, Vault vault) => new(file, () => vault, edit =>
    {
        edit(vault);
        vault.Save();
        return AgentSave.Saved;
    });

    private async ValueTask<string> AttachAsync()
    {
        var connection = $"conn-{Guid.NewGuid():N}";
        var attached = await _authority.AttachAsync(new AttachRequest(VaultPath) { Client = new AttachClient("Claude Code", "2.1.0", _agent) }, connection, Token);
        Assert.True(attached.Attached, attached.Reason);
        return connection;
    }

    private async ValueTask<CredentialReply> RequestAsync() => await _authority.RequestAsync(Request(), await AttachAsync(), Token);

    private CredentialRequest Request() => new()
    {
        Entry = "work/github",
        Field = "password",
        Reason = "push the release",
        Exposure = ["**"],
        ClientName = "Claude Code",
        ClientLabel = _agent,
        Vault = VaultPath,
        Session = "session-one",
    };
}
