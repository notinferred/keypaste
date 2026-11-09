using System.Text;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// F.79's acceptance on the save path: a real vault, the owner's own authority and log, and only the person faked. The value
/// the person enters reaches the place the agent named and never the agent, a cancel or a lock saves nothing, and a value is
/// replaced only where the prompt said it would be.
/// </summary>
public sealed class ValueSaveRequestTests : IDisposable
{
    private const string _master = "correct horse battery staple";
    private const string _value = "SENTINEL-SAVED-VALUE-7d1f";
    private static readonly EntryName _github = new("work", "github");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-value-save-").FullName;
    private readonly ManualClock _clock = new();
    private readonly FakeChannel _channel = new();
    private readonly ApprovalGate _gate;
    private readonly Vault _vault;
    private readonly OwnerLog _log;
    private readonly SessionLifetime _lifetime = new("session-one");
    private readonly SessionAuthority _authority;

    public ValueSaveRequestTests()
    {
        _vault = Vault.Create(VaultPath, _master);
        _vault.AddEntry(new VaultEntry { GroupPath = _github.GroupPath, Title = _github.Title, Password = "old-password" });
        Assert.True(_vault.SetFields(_github, [new FieldWrite("API_TOKEN", "old-token")]));
        _vault.Save();
        _log = new OwnerLog(_directory);
        _gate = new ApprovalGate(_channel, _clock, ApprovalLimits.Default);
        _channel.SaveValue = _value;
        _authority = new SessionAuthority(
            VaultIdentity.Of(_directory, VaultPath),
            () => _lifetime,
            new ApproverHandler(new VaultCredentialSource(() => _lifetime.IsLive ? _vault : null, _clock), new VaultEntryNameLister(() => _vault), _gate, IAgentRules.Defaults),
            () => _log.Log,
            clock: _clock,
            saver: new SessionSaver(_gate, lifetime => ReferenceEquals(lifetime, _lifetime) && lifetime.IsLive ? _vault : null, Write, _clock, narrate: null));
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
    public async Task A_value_the_person_saves_reaches_the_named_place_and_never_the_agent()
    {
        _channel.Answer = ApprovalAnswer.ApprovedOnce;

        var reply = await SaveAsync(new SaveRequest { Item = "work/github", Field = "password", Reason = "rotate", Exposure = ["**"] });

        Assert.Equal((AuditDecision.Granted, AuditMethod.Prompt, "work/github"), (reply.Decision, reply.Method, reply.Where));
        Assert.True(reply.Recorded);
        Assert.Equal(_value, Reopened(vault => vault.Find(_github)!.Password));
        Assert.True(_channel.LastSavePrompt!.Replaces);
        Assert.DoesNotContain(_value, Encoding.UTF8.GetString(ApproverProtocol.Encode(reply)), StringComparison.Ordinal);
        Assert.DoesNotContain(_value, _log.Text(), StringComparison.Ordinal);
        Assert.Equal("request_save", Assert.Single(_log.Lines()).GetProperty("tool").GetString());
    }

    [Fact]
    public async Task A_new_item_and_a_new_project_variable_are_created_and_said_to_be()
    {
        _channel.Answer = ApprovalAnswer.ApprovedOnce;

        var item = await SaveAsync(new SaveRequest { Item = "work/stripe", Field = "STRIPE_KEY", Reason = "new key", Exposure = ["**"] });
        var itemPrompt = _channel.LastSavePrompt!;
        var variable = await SaveAsync(new SaveRequest { Project = "acme-api", Environment = "dev", Key = "DATABASE_URL", Reason = "new database", Exposure = ["**"] });
        var variablePrompt = _channel.LastSavePrompt!;

        Assert.Equal((AuditDecision.Granted, AuditDecision.Granted), (item.Decision, variable.Decision));
        Assert.Equal((true, false), (itemPrompt.Creates, itemPrompt.Replaces));
        Assert.True(variablePrompt.ForProject);
        Assert.Equal(_value, Reopened(vault => vault.ReadField(new EntryName("work", "stripe"), "STRIPE_KEY")));
        Assert.True(Reopened(vault => vault.Fields(new EntryName("work", "stripe"))!.Single().IsProtected));
        Assert.Equal(_value, Reopened(vault => vault.ReadField(EnvStore.HomeEntry("acme-api", "dev"), "DATABASE_URL")));
    }

    [Fact]
    public async Task A_cancel_saves_nothing_and_holds_the_same_place_back()
    {
        _channel.Answer = ApprovalAnswer.Denied;

        var cancelled = await SaveAsync(new SaveRequest { Item = "work/github", Field = "API_TOKEN", Reason = "rotate", Exposure = ["**"] });
        var again = await SaveAsync(new SaveRequest { Item = "work/github", Field = "API_TOKEN", Reason = "rotate", Exposure = ["**"] });

        Assert.Equal((AuditDecision.Denied, AuditMethod.Prompt), (cancelled.Decision, cancelled.Method));
        Assert.Equal(AuditMethod.Cooldown, again.Method);
        Assert.Equal(1, _channel.Asked);
        Assert.Equal("old-token", Reopened(vault => vault.ReadField(_github, "API_TOKEN")));
    }

    [Fact]
    public async Task A_lock_while_the_person_is_asked_saves_nothing()
    {
        _channel.Hold = true;
        _channel.Answer = ApprovalAnswer.ApprovedOnce;

        var asking = SaveAsync(new SaveRequest { Item = "work/github", Field = "password", Reason = "rotate", Exposure = ["**"] });
        await _channel.Waiting.WaitAsync(TimeSpan.FromSeconds(10), Token);
        _lifetime.End();
        var reply = await asking.WaitAsync(TimeSpan.FromSeconds(10), Token);

        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Equal("old-password", Reopened(vault => vault.Find(_github)!.Password));
    }

    /// <summary>A value is replaced only where the person was told it would be: one that appeared while they were asked is not overwritten.</summary>
    [Fact]
    public async Task A_value_that_appeared_while_the_person_was_asked_is_not_replaced()
    {
        _channel.Answer = ApprovalAnswer.ApprovedOnce;
        _channel.WhileAsking = () => Assert.True(_vault.SetFields(_github, [new FieldWrite("NEW_TOKEN", "arrived-meanwhile")]));

        var reply = await SaveAsync(new SaveRequest { Item = "work/github", Field = "NEW_TOKEN", Reason = "new token", Exposure = ["**"] });

        Assert.False(_channel.LastSavePrompt!.Replaces);
        Assert.Equal((AuditDecision.Denied, AuditMethod.VaultChanged), (reply.Decision, reply.Method));
        Assert.Equal("arrived-meanwhile", _vault.ReadField(_github, "NEW_TOKEN"));
    }

    [Theory]
    [InlineData("other/**", AuditMethod.OutOfScope)]
    [InlineData("**", AuditMethod.InvalidRequest)]
    public async Task A_place_the_agent_cannot_see_or_name_is_refused_unasked(string exposure, AuditMethod method)
    {
        var request = method == AuditMethod.OutOfScope
            ? new SaveRequest { Item = "work/github", Field = "password", Reason = "rotate", Exposure = [exposure] }
            : new SaveRequest { Item = "work/github", Field = "password", Project = "acme-api", Reason = "both", Exposure = [exposure] };

        var reply = await SaveAsync(request);

        Assert.Equal((AuditDecision.Denied, method), (reply.Decision, reply.Method));
        Assert.Equal(0, _channel.Asked);
    }

    [Fact]
    public void A_save_request_and_reply_survive_the_wire_and_the_reply_holds_no_value()
    {
        var request = new SaveRequest { Project = "acme-api", Environment = "dev", Key = "DATABASE_URL", Reason = "why", Exposure = ["**"], ClientLabel = "claude-code", Vault = "/v", Session = "s" };
        var reply = new SaveReply(AuditDecision.Granted, AuditMethod.Prompt, "saved") { Where = "acme-api/dev", Recorded = true, Session = "s" };

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(request), out SaveRequest? decoded));
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(reply), out SaveReply? replied));

        Assert.Equal((request.Where, request.Named, request.ClientLabel), (decoded.Where, decoded.Named, decoded.ClientLabel));
        Assert.Equal(reply, replied);
        Assert.Equal(ApproverMessageKind.Save, ApproverProtocol.KindOf(ApproverProtocol.Encode(request)));
    }

    private AgentSave Write(SessionLifetime asked, Func<Vault, bool> apply)
    {
        if (!ReferenceEquals(asked, _lifetime) || !_lifetime.IsLive)
        {
            return new AgentSave(AgentSaveOutcome.Locked, "the vault is locked");
        }

        if (!apply(_vault))
        {
            return AgentSave.Unchanged;
        }

        _vault.Save();
        return AgentSave.Saved;
    }

    private T Reopened<T>(Func<Vault, T> read)
    {
        using var reopened = Vault.Open(VaultPath, _master);
        return read(reopened);
    }

    private async Task<SaveReply> SaveAsync(SaveRequest request)
    {
        var connection = $"conn-{Guid.NewGuid():N}";
        var attached = await _authority.AttachAsync(new AttachRequest(VaultPath) { Client = new AttachClient("Claude Code", "2.1.0", "claude-code") }, connection, Token);
        Assert.True(attached.Attached, attached.Reason);

        return await _authority.SaveAsync(request with { ClientName = "Claude Code", ClientLabel = "claude-code", Vault = VaultPath, Session = attached.Session! }, connection, Token);
    }
}
